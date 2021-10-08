using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Text;

namespace AutoUpdater
{
    public static class MyHttpClient
    {
        public static readonly HttpClient HttpClient = new HttpClient();
    }
}
