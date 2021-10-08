using System.Net.Http;

namespace AutoUpdater.ZipExtractor
{
    public static class MyHttpClient
    {
        public static readonly HttpClient HttpClient = new HttpClient();
    }
}
