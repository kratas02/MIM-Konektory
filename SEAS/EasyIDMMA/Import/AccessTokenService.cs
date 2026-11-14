using System;
using System.Collections.Generic;
using System.Net.Http;
using EasyIDMMA.Model.Parameters;
using Newtonsoft.Json;
using NLog;

namespace EasyIDMMA.Import
{
    public static class AccessTokenService
    {
        private static Logger Logger = LogManager.GetCurrentClassLogger();
        private static string _cachedToken;
        private static DateTime _tokenExpiration = DateTime.MinValue;
        private struct TokenResponse
        {
            [JsonProperty("access_token")]
            public string AccessToken { get; set; }

            [JsonProperty("token_type")]
            public string TokenType { get; set; }

            [JsonProperty("scope")]
            public string Scope { get; set; }

            [JsonProperty("expires_in")]
            public int ExpiresIn { get; set; }
        }

        public static string GetToken(SynchronizationParameters synchronizationParameters)
        {
            var token = GetAccessToken(synchronizationParameters);
            if (string.IsNullOrEmpty(token))
            {
                throw new InvalidOperationException($"Unable to retrieve access token for the specified base URL {synchronizationParameters.BaseUrl}.");
            }
            return token;
        }

        private static string GetAccessToken(SynchronizationParameters synchronizationParameters)
        {
            if (!string.IsNullOrEmpty(_cachedToken) && DateTime.UtcNow < _tokenExpiration)
            {
                return _cachedToken;
            }

            if (string.IsNullOrEmpty(synchronizationParameters.BaseUrl))
            {
                Logger.Error("Missing base url of server to get token");
                return string.Empty;
            }
            
            var tokenUrl = $"{synchronizationParameters.BaseUrl}/connect/token";

            var tokenData = new Dictionary<string, string>
            {
                { "client_id", "EasyIDMPortalVNext" },
                { "grant_type", "client_credentials" },
                { "scope", "easyidm_portal" },
                { "client_secret", synchronizationParameters.ClientSecret }
            };

            try
            {
                using (var httpClient = new HttpClient())
                {
                    var tokenEncoded = new FormUrlEncodedContent(tokenData);
                    var response = httpClient.PostAsync(tokenUrl, tokenEncoded).Result;

                    if (!response.IsSuccessStatusCode)
                    {
                        Logger.Error($"Error response with status code: {response.StatusCode}");
                        return string.Empty;
                    }

                    var responseString = response.Content.ReadAsStringAsync().Result;
                    var tokenResponse = JsonConvert.DeserializeObject<TokenResponse>(responseString);

                    if (string.IsNullOrEmpty(tokenResponse.AccessToken))
                    {
                        Logger.Error("Server returned empty token");
                        return string.Empty;
                    }

                    // Cache token and expiration
                    _cachedToken = tokenResponse.AccessToken;
                    _tokenExpiration = DateTime.UtcNow.AddSeconds(tokenResponse.ExpiresIn - 30); // 30 is buffer window

                    return _cachedToken;
                }
            }
            catch (Exception ex)
            {
                Logger.Error(ex, $"POST token request error, message: {ex.Message}");
                return string.Empty;
            }
        }

        public static bool WillExpireSoon()
        {
            return string.IsNullOrEmpty(_cachedToken) || DateTime.UtcNow >= _tokenExpiration;
        }
    }
}
