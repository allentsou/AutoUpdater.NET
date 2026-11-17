# 由 AutoUpdater.targets 在 Publish 後呼叫：上傳更新包到 FTP，並更新 FTP 上的 AutoUpdaterConfig.json。
# FTP 帳號密碼從環境變數 AUTOUPDATER_FTP_USER、AUTOUPDATER_FTP_PASSWORD 讀取。
# 需相容 Windows PowerShell 5.1。輸出的中文訊息以系統字碼頁顯示，繁體中文 Windows 正常；英文語系 Windows 上中文會變成問號。
# 本檔必須存成含 BOM 的 UTF-8，PowerShell 5.1 才能正確讀取中文。
# 錯誤與警告訊息開頭的「: error :」「: warning :」必須維持英文，MSBuild 才能辨識並列入錯誤清單。
param(
    [Parameter(Mandatory = $true)][string]$ZipPath,
    [Parameter(Mandatory = $true)][string]$FtpUrl,
    [Parameter(Mandatory = $true)][string]$BaseUrl,
    [Parameter(Mandatory = $true)][string]$Version,
    [string]$ConfigFileName = 'AutoUpdaterConfig.json',
    # 保留最新的幾個版本的更新包，其餘刪除；0 表示不清理
    [int]$KeepVersions = 5,
    # 要一起上傳到 FTP 資料夾的其他檔案（例如 ChangeLog.html），完整路徑以 | 分隔
    [string]$UploadFiles = ''
)

$ErrorActionPreference = 'Stop'

function Test-NotFound($exception) {
    # PowerShell 會把 .NET 例外包在 MethodInvocationException 裡，所以沿著 InnerException 逐層檢查
    while ($exception) {
        # FTP 550：檔案不存在（第一次部署時還沒有設定檔）
        if ($exception -is [System.Net.WebException] -and
            $exception.Response -is [System.Net.FtpWebResponse] -and
            $exception.Response.StatusCode -eq [System.Net.FtpStatusCode]::ActionNotTakenFileUnavailable) {
            return $true
        }
        # file:// 網址（本機測試用）找不到檔案
        if ($exception -is [System.IO.FileNotFoundException]) {
            return $true
        }
        $exception = $exception.InnerException
    }
    return $false
}

function New-FtpRequest([string]$url, [string]$method) {
    $request = [System.Net.FtpWebRequest]::Create($url)
    $request.Method = $method
    $request.Credentials = $script:credentials
    return $request
}

# 列出資料夾內的檔名；file:// 網址（本機測試用）直接讀本機資料夾
function Get-RemoteFileNames([string]$dirUrl) {
    $uri = [Uri]$dirUrl
    if ($uri.IsFile) {
        return [System.IO.Directory]::GetFiles($uri.LocalPath) | ForEach-Object { [System.IO.Path]::GetFileName($_) }
    }
    $response = (New-FtpRequest $dirUrl ([System.Net.WebRequestMethods+Ftp]::ListDirectory)).GetResponse()
    try {
        $reader = New-Object System.IO.StreamReader($response.GetResponseStream())
        $lines = $reader.ReadToEnd() -split "`r?`n"
    }
    finally {
        $response.Close()
    }
    # 部分 FTP 伺服器回傳的名稱會帶路徑，只取檔名
    return $lines | Where-Object { $_.Trim() } | ForEach-Object { [System.IO.Path]::GetFileName($_.Trim()) }
}

function Remove-RemoteFile([string]$url) {
    $uri = [Uri]$url
    if ($uri.IsFile) {
        Remove-Item -LiteralPath $uri.LocalPath
        return
    }
    (New-FtpRequest $url ([System.Net.WebRequestMethods+Ftp]::DeleteFile)).GetResponse().Close()
}

try {
    $user = $env:AUTOUPDATER_FTP_USER
    $password = $env:AUTOUPDATER_FTP_PASSWORD
    if ([string]::IsNullOrEmpty($user) -or [string]::IsNullOrEmpty($password)) {
        throw '找不到 FTP 帳號密碼。請設定 Windows 使用者環境變數 AUTOUPDATER_FTP_USER 與 AUTOUPDATER_FTP_PASSWORD，再重新開啟 Visual Studio。'
    }
    if (-not $FtpUrl.EndsWith('/')) { $FtpUrl += '/' }
    if (-not $BaseUrl.EndsWith('/')) { $BaseUrl += '/' }

    $zipName = [System.IO.Path]::GetFileName($ZipPath)
    $configUrl = $FtpUrl + $ConfigFileName

    $credentials = New-Object System.Net.NetworkCredential($user, $password)
    $client = New-Object System.Net.WebClient
    $client.Credentials = $credentials
    $client.Encoding = New-Object System.Text.UTF8Encoding($false)

    # 1. 下載目前的設定檔；不存在時建立新的
    $config = $null
    try {
        $config = $client.DownloadString($configUrl) | ConvertFrom-Json
        Write-Host "AutoUpdater: 伺服器上的 $ConfigFileName 目前版號為 $($config.NewVersion)"
    }
    catch {
        if (-not (Test-NotFound $_.Exception)) { throw }
        Write-Host "AutoUpdater: 伺服器上沒有 $ConfigFileName，將建立新的設定檔。"
        $config = [pscustomobject][ordered]@{
            NewVersion    = '0.0'
            PackageUrl    = ''
            ChangeLogUrl  = ''
            IsMandatory   = $false
            BackupOldFile = $true
        }
    }

    # 版號比伺服器上的舊時中止，避免把伺服器上的版本降版。
    # 版號相同時允許重新發行（例如上次發行的內容有錯），會覆蓋伺服器上同名的更新包。
    $serverVersion = [Version]'0.0'
    if ($config.NewVersion) { $serverVersion = [Version]$config.NewVersion }
    if ([Version]$Version -lt $serverVersion) {
        throw "版號 $Version 比伺服器上的 $($config.NewVersion) 舊，請先調高專案的 <Version> 再發行。"
    }
    if ([Version]$Version -eq $serverVersion) {
        Write-Host "AutoUpdater.Deploy.ps1 : warning : 版號 $Version 與伺服器上相同，將覆蓋重新發行。已經更新到這個版號的用戶端不會再收到這次的修正。"
    }

    # 2. 先上傳更新包，再更新設定檔，用戶端才不會看到新版號卻下載不到檔案
    Write-Host "AutoUpdater: 上傳 $zipName 到 $FtpUrl"
    $null = $client.UploadFile($FtpUrl + $zipName, $ZipPath)

    # 其他檔案（例如 ChangeLog.html）也在更新設定檔之前上傳，用戶端看到新版號時就能看到新的更新說明
    foreach ($file in $UploadFiles.Split([char[]]'|', [StringSplitOptions]::RemoveEmptyEntries)) {
        $name = [System.IO.Path]::GetFileName($file)
        Write-Host "AutoUpdater: 上傳 $name 到 $FtpUrl"
        $null = $client.UploadFile($FtpUrl + $name, $file)
    }

    # 3. 只改版號與下載網址，其餘欄位（ChangeLogUrl、IsMandatory、BackupOldFile...）保留原設定
    $config.NewVersion = $Version
    $config.PackageUrl = $BaseUrl + [Uri]::EscapeDataString($zipName)
    # 沒有使用 SHA-256 驗證時不加這個欄位；若設定檔已有值，就更新成新檔案的雜湊，避免舊雜湊擋住更新
    if (($config.PSObject.Properties.Name -contains 'PackageSha256') -and $config.PackageSha256) {
        $config.PackageSha256 = (Get-FileHash -Path $ZipPath -Algorithm SHA256).Hash
    }

    $json = $config | ConvertTo-Json -Depth 5
    $null = $client.UploadData($configUrl, $client.Encoding.GetBytes($json))
    Write-Host "AutoUpdater: 已更新 $ConfigFileName，NewVersion=$Version，PackageUrl=$($config.PackageUrl)"

    # 4. 只保留最新的 N 個版本的更新包。只處理「<AssemblyName>_<版號>.zip」格式的檔案，其他檔案不動。
    #    清理失敗只顯示警告，因為新版本已經部署完成。
    if ($KeepVersions -gt 0) {
        try {
            $prefix = $zipName.Substring(0, $zipName.LastIndexOf('_'))
            $pattern = '^' + [regex]::Escape($prefix) + '_(\d+(?:\.\d+){1,3})\.zip$'
            $packages = @(Get-RemoteFileNames $FtpUrl | ForEach-Object {
                    $match = [regex]::Match($_, $pattern, [System.Text.RegularExpressions.RegexOptions]::IgnoreCase)
                    if ($match.Success) { [pscustomobject]@{ Name = $_; Version = [Version]$match.Groups[1].Value } }
                } | Sort-Object Version -Descending)
            # 剛上傳的更新包一定保留
            $toDelete = @($packages | Select-Object -Skip $KeepVersions | Where-Object { $_.Name -ne $zipName })
            foreach ($old in $toDelete) {
                Write-Host "AutoUpdater: 刪除舊版更新包 $($old.Name)"
                Remove-RemoteFile ($FtpUrl + $old.Name)
            }
            Write-Host "AutoUpdater: 保留 $([Math]::Min($packages.Count, $KeepVersions)) 個更新包，刪除 $($toDelete.Count) 個"
        }
        catch {
            Write-Host "AutoUpdater.Deploy.ps1 : warning : 刪除舊版更新包失敗：$($_.Exception.Message)"
        }
    }
}
catch {
    # 以 MSBuild 能辨識的錯誤格式輸出，Visual Studio 才會列在錯誤清單
    [Console]::Error.WriteLine("AutoUpdater.Deploy.ps1 : error : $($_.Exception.Message)")
    exit 1
}
