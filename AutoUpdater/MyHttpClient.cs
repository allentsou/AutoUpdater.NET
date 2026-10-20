using System;
using System.Net.Http;

namespace AutoUpdater
{
    public static class MyHttpClient
    {
        // 預設的 100 秒在伺服器連不上時，會讓呼叫端的 UI 執行緒卡住太久。
        public static readonly HttpClient HttpClient = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
    }
}
