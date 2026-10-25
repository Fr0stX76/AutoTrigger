using System;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Threading;
using System.Threading.Tasks;
using AutoTrigger.Core.Json;
using AutoTrigger.Core.Logging;
using AutoTrigger.Core.Model;

namespace AutoTrigger.Core.Platform
{
    public sealed class UpdateCheck : IDisposable
    {
        public const string Repository = "Fr0stX76/AutoTrigger";
        private const int MaxResponseBytes = 256 * 1024;

        private static readonly Uri LatestRelease = new Uri("https://api.github.com/repos/" + Repository + "/releases/latest");

        private readonly HttpClientHandler _handler;
        private readonly HttpClient _client;

        public UpdateCheck(AppVersion current)
        {
            // Read only: no cookies, no credentials, no redirect; a revoked certificate is refused.
            _handler = new HttpClientHandler
            {
                AllowAutoRedirect = false,
                UseCookies = false,
                UseDefaultCredentials = false,
                CheckCertificateRevocationList = true,
            };
            _client = new HttpClient(_handler, false)
            {
                Timeout = TimeSpan.FromSeconds(10),
                MaxResponseContentBufferSize = MaxResponseBytes,
            };
            _client.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("AutoTrigger", current?.ToString() ?? "0.0.0"));
            _client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
        }

        // The page is built here from the checked version, never taken from the response.
        public static Uri ReleasePage(AppVersion version) =>
            new Uri("https://github.com/" + Repository + "/releases/tag/v" + version);

        // Null when the check fails or finds no valid release; throws only when cancelled.
        public async Task<AppVersion> LatestAsync(CancellationToken cancel)
        {
            try
            {
                using (HttpResponseMessage response = await _client.GetAsync(LatestRelease, cancel).ConfigureAwait(false))
                {
                    response.EnsureSuccessStatusCode();
                    string json = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
                    AppVersion latest = AppVersion.ParseRelease(JsonValue.Parse(json)["tag_name"]?.AsString());
                    Log.Detailed(latest == null ? "Update check: no valid release found" : $"Update check: latest release is {latest}");
                    return latest;
                }
            }
            catch (Exception ex) when (ex is HttpRequestException || ex is JsonException
                || (ex is TaskCanceledException && !cancel.IsCancellationRequested))
            {
                // Offline, rate-limited by GitHub, timed out or an unexpected answer: the next check tries again.
                Log.Info("Update check failed: " + ex.GetBaseException().Message);
                return null;
            }
        }

        public void Dispose()
        {
            _client.Dispose();
            _handler.Dispose();
        }
    }
}
