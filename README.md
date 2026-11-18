# AutoUpdater

給 .NET 8、9、10 WPF / WinForms 程式使用的自動更新函式庫。程式啟動時檢查伺服器上的設定檔，有新版本就顯示更新視窗，使用者按下更新後自動下載、備份、解壓縮並重新啟動程式。

搭配內建的 Publish 自動部署，在 Visual Studio 按下「發行」就會把更新包上傳到 FTP 並更新伺服器上的設定檔。

## 專案結構

| 專案 | 說明 |
|---|---|
| `AutoUpdater` | 函式庫本體（NuGet 套件）。內嵌 `Resources\AutoUpdater.ZipExtractor.zip` 與部署用的 `build\AutoUpdater.targets`、`build\AutoUpdater.Deploy.ps1` |
| `AutoUpdater.ZipExtractor` | 實際執行更新的程式：等待主程式結束、下載、備份、解壓縮、重新啟動主程式 |
| `TestWPF`、`TetsWinForm` | 範例程式 |

## 更新流程

1. 主程式呼叫 `CheckUpdate`，下載伺服器上的 `AutoUpdaterConfig.json`。
2. 比較 `NewVersion` 與主程式的 **AssemblyVersion**，伺服器的版號較新才繼續。
3. 顯示更新視窗（含更新說明頁）；使用者按「更新」或 `forceUpdate = true` 時：
   - 從函式庫解出 `AutoUpdater.ZipExtractor.exe` 到程式資料夾並啟動
   - 主程式自動關閉（或觸發 `NeedCloseApplication` 事件由主程式自行關閉）
4. ZipExtractor 等待主程式結束後下載 `PackageUrl` 的 zip，備份將被覆蓋的檔案，解壓縮覆蓋，最後重新啟動主程式。

## 安裝

用 NuGet 套件引用 `AutoUpdater`。套件提供 `net8.0-windows`、`net9.0-windows`、`net10.0-windows` 三個版本，每個版本只有一個 `AutoUpdater.dll`（中文語系已內嵌），另外會自動匯入 Publish 部署用的 targets。

> 以專案參考（ProjectReference）引用時不會自動匯入部署 targets，需要在 csproj 手動加入：
> ```xml
> <Import Project="..\AutoUpdater\build\AutoUpdater.targets" />
> ```

## 伺服器設定檔 AutoUpdaterConfig.json

放在 IIS 上，用戶端以 http 網址讀取：

```json
{
  "NewVersion": "1.2026.1118.0",
  "PackageUrl": "http://伺服器/Deploy/update/MyApp/MyApp_1.2026.1118.0.zip",
  "ChangeLogUrl": "http://伺服器/Deploy/update/MyApp/ChangeLog.html",
  "IsMandatory": false,
  "BackupOldFile": true
}
```

| 欄位 | 說明 |
|---|---|
| `NewVersion` | 最新版號，與主程式的 AssemblyVersion 比較 |
| `PackageUrl` | 更新包 zip 的 http(s) 網址 |
| `ChangeLogUrl` | 更新說明頁網址，顯示在更新視窗中；空白則不顯示 |
| `IsMandatory` | `true` 時隱藏「忽略」按鈕，必須更新 |
| `BackupOldFile` | `true` 時更新前把會被覆蓋的檔案備份到程式資料夾的 `AutoUpdaterBackup\日期_時間.zip` |
| `PackageSha256` | （選用）更新包的 SHA-256，填寫後下載完會驗證 |

使用 Publish 自動部署時，`NewVersion` 與 `PackageUrl` 會自動更新，其他欄位維持手動設定。

## 在程式中檢查更新

```csharp
// WPF：在 UI 執行緒呼叫
var handle = new WindowInteropHelper(this).Handle;
AutoUpdater.AutoUpdater.CheckUpdate(
    "http://伺服器/Deploy/update/MyApp/AutoUpdaterConfig.json",
    handle,                 // 更新視窗的擁有者視窗，可為 null
    forceUpdate: false,     // true：不顯示視窗，直接更新
    appName: "MyApp",       // 視窗標題顯示的名稱，null 時使用組件標題
    appIconUri: "pack://application:,,,/Resources/MainIcon.ico");

// 非同步版本：下載設定檔時不會卡住 UI，可從任何執行緒呼叫
await AutoUpdater.AutoUpdater.CheckUpdateAsync(configUrl, handle, false, "MyApp");
```

WinForms 用法相同，`windowHandle` 傳 `this.Handle`，圖示可改用 `appIcon: byte[]`。

### 其他 API

| 成員 | 說明 |
|---|---|
| `AutoUpdater.Culture` | 更新視窗與 ZipExtractor 的語系，例如 `new CultureInfo("en-US")`。只影響 AutoUpdater，不改變主程式語系。預設跟隨 Windows 顯示語言；目前內建 zh-TW 與英文 |
| `OnError` 事件 | 檢查或啟動更新失敗時觸發（例如連不到伺服器） |
| `OnIgnoreUpdate` 事件 | 使用者按「忽略」時觸發 |
| `NeedCloseApplication` 事件 | 有訂閱時，啟動更新後改由主程式自行關閉；沒訂閱時 WPF 呼叫 `Application.Shutdown()`，其他程式呼叫 `Environment.Exit` |
| `OpenChangeLogInBrowser(configUrl)` | 用預設瀏覽器開啟更新說明頁 |

### 版號

主程式以 csproj 的 `<Version>` 設定版號（同時決定 AssemblyVersion）。每次發行都要調高，用戶端才會更新。

## Publish 自動部署

在 Visual Studio 用發行設定檔（.pubxml）發行後，自動：

1. 清空 publish 資料夾（發行前），避免舊檔案被打包
2. 把 publish 輸出打包成 `<AssemblyName>_<版號>.zip`（排除 ZipExtractor、AutoUpdaterConfig.json、Log、AutoUpdaterBackup 等）
3. 上傳更新包與 `AutoUpdaterUploadFiles` 指定的檔案到 FTP
4. 更新 FTP 上 `AutoUpdaterConfig.json` 的 `NewVersion` 與 `PackageUrl`（不存在時建立新的）
5. FTP 上只保留最新的 N 個更新包

### 1. 設定 FTP 帳號密碼

帳號密碼不寫在專案裡，請設定 **Windows 使用者環境變數**，設定後重新開啟 Visual Studio：

| 環境變數 | 說明 |
|---|---|
| `AUTOUPDATER_FTP_USER` | FTP 帳號 |
| `AUTOUPDATER_FTP_PASSWORD` | FTP 密碼 |

### 2. 在發行設定檔加入設定

```xml
<PropertyGroup>
  <Configuration>Release</Configuration>
  <PublishDir>bin\Release\net8.0-windows7.0\publish\win-x64\</PublishDir>
  ...
  <!-- 上傳更新包與設定檔的 FTP 資料夾 -->
  <AutoUpdaterFtpUrl>ftp://伺服器/MyApp/</AutoUpdaterFtpUrl>
  <!-- 同一個資料夾在 IIS 上的 http 網址，用戶端從這裡下載 -->
  <AutoUpdaterBaseUrl>http://伺服器/Deploy/update/MyApp/</AutoUpdaterBaseUrl>
  <!-- 不放進更新包的檔案，例如正式機各自不同的設定檔 -->
  <AutoUpdaterExcludeFiles>MyApp.dll.config;NLog.config;**\*.xlsx</AutoUpdaterExcludeFiles>
  <!-- 和更新包一起上傳的檔案 -->
  <AutoUpdaterUploadFiles>ChangeLog.html</AutoUpdaterUploadFiles>
</PropertyGroup>
```

只有設定了 `AutoUpdaterFtpUrl` 與 `AutoUpdaterBaseUrl` 的發行設定檔才會部署；一般 Publish 不受影響。

| 屬性 | 預設值 | 說明 |
|---|---|---|
| `AutoUpdaterFtpUrl` | （必填） | FTP 資料夾網址 |
| `AutoUpdaterBaseUrl` | （必填） | 同一資料夾的 http 網址，用來組出 `PackageUrl` |
| `AutoUpdaterExcludeFiles` | 空 | 不放進更新包的檔案，分號分隔，**相對於 publish 資料夾**，可用萬用字元 |
| `AutoUpdaterUploadFiles` | 空 | 一起上傳的檔案，分號分隔，**相對於專案資料夾**，可用萬用字元；上傳到 FTP 資料夾根目錄，同名覆蓋 |
| `AutoUpdaterKeepVersions` | `5` | FTP 上保留的更新包數量，`0` 表示不清理 |
| `AutoUpdaterCleanPublishDir` | `true` | 發行前清空 publish 資料夾 |
| `AutoUpdaterConfigFileName` | `AutoUpdaterConfig.json` | 伺服器設定檔名稱 |

萬用字元：`*.xlsx` 只比對 publish 資料夾第一層；`**\*.xlsx` 包含所有子資料夾。

### 3. 發行

1. 調高 csproj 的 `<Version>`
2. Visual Studio →「發行」選擇該設定檔 →「發行」
3. 在輸出視窗查看 `AutoUpdater:` 開頭的訊息；錯誤會列在錯誤清單

命令列：

```bash
dotnet publish MyApp.csproj -p:PublishProfile=MyProfile
```

### 部署規則

- **版號比伺服器舊**：中止，不上傳任何檔案，避免降版。
- **版號與伺服器相同**：允許重新發行（覆蓋同名更新包）並顯示警告。已經更新到該版號的用戶端不會再收到這次的修正，需要的話請調高版號。
- **舊更新包清理**：只刪除符合 `<AssemblyName>_<版號>.zip` 格式的檔案，ChangeLog.html 等其他檔案不動；清理失敗只顯示警告。
- **清空 publish 資料夾**：publish 資料夾是磁碟根目錄、專案資料夾或其上層、或包含 Build 輸出時，為了安全不會清空，只顯示警告。
- **上傳順序**：更新包 → 其他檔案 → 設定檔，用戶端看到新版號時檔案都已就緒。

## ZipExtractor

- 由函式庫在更新時自動解出到程式資料夾，不需要手動部署，也不會被打包進更新包。
- 以 .NET 8 建置並設定 `RollForward=Major`：用戶端沒有 .NET 8 Desktop Runtime 時，會改用已安裝的較新版本（例如 .NET 9、10）執行。
- 等待主程式（依 PID）結束，逾時則強制結束主程式。
- 解壓縮時略過自己的檔案，並防止 zip 內的路徑寫到程式資料夾以外。
- Log：程式資料夾的 `AutoUpdater.ZipExtractor.txt`（無寫入權限時改寫到 `%LocalAppData%\AutoUpdater\`），超過 1 MB 時改名為 `.1.txt` 保留一份。

## 開發此函式庫

- **Release 建置 `AutoUpdater.ZipExtractor`** 會自動把輸出打包成 `AutoUpdater\Resources\AutoUpdater.ZipExtractor.zip`；建置 `AutoUpdater` 時會先建置 extractor。修改 extractor 後請用 Release 建置，並把更新後的 zip 一起 commit。
- **建置 `AutoUpdater`** 會產生 NuGet 套件（`GeneratePackageOnBuild`）。
- **目標框架**：`AutoUpdater` 同時建置 net8、net9、net10。Visual Studio 2022 只能使用 .NET 9 SDK，無法建置 .NET 10，所以只有用 .NET 10 SDK 建置（`dotnet build` / `dotnet pack`，或 Visual Studio 2026）時才會加入 net10。用 VS 2022 打包的套件只有 net8、net9，.NET 10 的程式會使用 net9 版本，一樣可以執行。要發佈正式套件時建議用命令列：
  ```bash
  dotnet pack AutoUpdater\AutoUpdater.csproj -c Release
  ```
- **每次修改都要調高 `AutoUpdater` 的 `<Version>`** 再打包。NuGet 會快取同版號的套件（`%UserProfile%\.nuget\packages\autoupdater\<版號>`），重複使用版號時引用端拿到的仍是舊內容。
- **新增語系**：在 `Localization` 加入 `LocalizationResources.<語系>.resx`，並仿照 csproj 中 zh-TW 那筆設定 `WithCulture=false` 與 `LogicalName`，語系資源才會內嵌在主組件而不是產生附屬組件。兩個專案都要加。

## 疑難排解

| 狀況 | 處理方式 |
|---|---|
| 發行時出現「找不到 FTP 帳號密碼」 | 設定環境變數後要重新開啟 Visual Studio |
| 發行時出現「版號比伺服器上的舊」 | 調高 csproj 的 `<Version>` |
| 引用 NuGet 套件後沒有自動部署 | 確認套件版號是否為新版、清除 NuGet 快取中的同版號套件；確認 `obj\*.nuget.g.targets` 有匯入 `AutoUpdater.targets` |
| 輸出視窗中文變成問號 | 部署腳本的中文訊息依系統字碼頁顯示，英文語系 Windows 會出現問號，不影響功能 |
| 更新後程式設定檔被覆蓋 | 把設定檔加入 `AutoUpdaterExcludeFiles` |
| 更新失敗想查原因 | 查看程式資料夾的 `AutoUpdater.ZipExtractor.txt` 與主程式的 NLog log |
