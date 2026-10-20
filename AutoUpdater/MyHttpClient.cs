using System;
using System.Net.Http;

namespace AutoUpdater
{
    public static class MyHttpClient
    {
        // The default 100 seconds would freeze the calling UI thread for too long when the server is unreachable.
        public static readonly HttpClient HttpClient = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
    }
}
