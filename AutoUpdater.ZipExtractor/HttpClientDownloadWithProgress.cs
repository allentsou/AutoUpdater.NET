using System;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace AutoUpdater.ZipExtractor
{
    public class HttpClientDownloadWithProgress : IDisposable
    {
        //https://stackoverflow.com/questions/20661652/progress-bar-with-httpclient
        private const int BufferSize = 81920;
        private static readonly TimeSpan ProgressInterval = TimeSpan.FromMilliseconds(200);
        // 使用 ResponseHeadersRead 時，HttpClient.Timeout 只涵蓋到收到回應標頭為止；
        // 讀取內容另外用閒置逾時保護，連線卡住時才不會永遠等下去。
        private static readonly TimeSpan HeadersTimeout = TimeSpan.FromSeconds(60);
        private static readonly TimeSpan IdleTimeout = TimeSpan.FromSeconds(60);

        private readonly string _downloadUrl;
        private readonly string _destinationFilePath;

        private HttpClient _httpClient;

        public delegate void ProgressChangedHandler(long? totalFileSize, long totalBytesDownloaded, double? progressPercentage, double KBPerSeconds);

        public event ProgressChangedHandler ProgressChanged;

        public HttpClientDownloadWithProgress(string downloadUrl, string destinationFilePath)
        {
            _downloadUrl = downloadUrl;
            _destinationFilePath = destinationFilePath;
        }

        public async Task StartDownload()
        {
            _httpClient = new HttpClient { Timeout = HeadersTimeout };

            using (var response = await _httpClient.GetAsync(_downloadUrl, HttpCompletionOption.ResponseHeadersRead))
                await DownloadFileFromHttpResponseMessage(response);
        }

        private async Task DownloadFileFromHttpResponseMessage(HttpResponseMessage response)
        {
            response.EnsureSuccessStatusCode();

            var totalBytes = response.Content.Headers.ContentLength;

            using (var contentStream = await response.Content.ReadAsStreamAsync())
                await ProcessContentStream(totalBytes, contentStream);
        }

        private async Task ProcessContentStream(long? totalDownloadSize, Stream contentStream)
        {
            var totalBytesRead = 0L;
            var buffer = new byte[BufferSize];
            var stopwatch = Stopwatch.StartNew();
            var lastReportElapsed = TimeSpan.Zero;
            var lastReportBytes = 0L;

            using (var idleCts = new CancellationTokenSource())
            using (var fileStream = new FileStream(_destinationFilePath, FileMode.Create, FileAccess.Write, FileShare.None, BufferSize, true))
            {
                int bytesRead;
                while ((bytesRead = await ReadWithIdleTimeoutAsync(contentStream, buffer, idleCts)) > 0)
                {
                    await fileStream.WriteAsync(buffer, 0, bytesRead);
                    totalBytesRead += bytesRead;

                    var elapsed = stopwatch.Elapsed;
                    if (elapsed - lastReportElapsed >= ProgressInterval)
                    {
                        TriggerProgressChanged(totalDownloadSize, totalBytesRead, totalBytesRead - lastReportBytes, elapsed - lastReportElapsed);
                        lastReportElapsed = elapsed;
                        lastReportBytes = totalBytesRead;
                    }
                }
                await fileStream.FlushAsync();

                var finalElapsed = stopwatch.Elapsed;
                TriggerProgressChanged(totalDownloadSize, totalBytesRead, totalBytesRead - lastReportBytes, finalElapsed - lastReportElapsed);
            }

            if (totalDownloadSize.HasValue && totalBytesRead != totalDownloadSize.Value)
            {
                throw new IOException($"Incomplete download: received {totalBytesRead} of {totalDownloadSize.Value} bytes.");
            }
        }

        private static async Task<int> ReadWithIdleTimeoutAsync(Stream stream, byte[] buffer, CancellationTokenSource idleCts)
        {
            idleCts.CancelAfter(IdleTimeout); // 每次讀取都重新開始倒數
            try
            {
                return await stream.ReadAsync(buffer.AsMemory(), idleCts.Token);
            }
            catch (OperationCanceledException) when (idleCts.IsCancellationRequested)
            {
                throw new TimeoutException($"No data received for {IdleTimeout.TotalSeconds:N0} seconds, download aborted.");
            }
        }

        private void TriggerProgressChanged(long? totalDownloadSize, long totalBytesRead, long diffBytes, TimeSpan diffTime)
        {
            if (ProgressChanged == null)
                return;

            double? progressPercentage = null;
            if (totalDownloadSize.HasValue && totalDownloadSize.Value > 0)
                progressPercentage = Math.Round((double)totalBytesRead / totalDownloadSize.Value * 100, 2);

            double kbPerSeconds = diffTime.TotalSeconds > 0 ? diffBytes / diffTime.TotalSeconds / 1024 : 0;

            ProgressChanged(totalDownloadSize, totalBytesRead, progressPercentage, Math.Round(kbPerSeconds, 1));
        }

        public void Dispose()
        {
            _httpClient?.Dispose();
        }
    }
}
