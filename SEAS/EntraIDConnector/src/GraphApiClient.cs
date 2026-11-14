using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Runtime.InteropServices;
using System.Security;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;

namespace EntraID.MIM.Connector
{
    /// <summary>
    /// Klient pro Microsoft Graph API s podporou OAuth2 client credentials
    /// a HTTP proxy. Implementuje delta query pro inkrementální import.
    /// </summary>
    internal sealed class GraphApiClient : IDisposable
    {
        private const string GraphBaseUrl   = "https://graph.microsoft.com/v1.0";
        private const string LoginBaseUrl   = "https://login.microsoftonline.com";
        private const string GraphScope     = "https://graph.microsoft.com/.default";

        // Atributy načítané z Entra ID – rozšiř dle potřeby
        public const string SelectFields =
            "id,userPrincipalName,displayName,givenName,surname,mail," +
            "accountEnabled,employeeId,department,jobTitle,mobilePhone," +
            "businessPhones,officeLocation,usageLocation,companyName," +
            "createdDateTime,userType,onPremisesSamAccountName," +
            "onPremisesImmutableId,onPremisesDistinguishedName";

        private readonly string     _tenantId;
        private readonly string     _clientId;
        private readonly string     _clientSecret;
        private readonly HttpClient _httpClient;

        private string _accessToken;
        private DateTime _tokenExpiry = DateTime.MinValue;

        // ── Stav delta / stránkování ────────────────────────────────────────

        /// <summary>URL pro načtení další stránky nebo delta změn.</summary>
        public string NextLink  { get; private set; }

        /// <summary>
        /// Delta token uložený po dokončení celého průchodu (full i delta).
        /// Použij tuto hodnotu jako watermark pro další delta import.
        /// </summary>
        public string DeltaLink { get; private set; }

        /// <summary>True, pokud jsou k dispozici další stránky dat.</summary>
        public bool HasMorePages => !string.IsNullOrEmpty(NextLink);

        // ── Konstruktory ────────────────────────────────────────────────────

        public GraphApiClient(
            string tenantId,
            string clientId,
            string clientSecret,
            string proxyUrl)
        {
            // .NET Framework defaultně používá TLS 1.0 – vynuť TLS 1.2+
            // login.microsoftonline.com a graph.microsoft.com vyžadují TLS 1.2 minimum
            System.Net.ServicePointManager.SecurityProtocol =
                System.Net.SecurityProtocolType.Tls12 |
                System.Net.SecurityProtocolType.Tls11;

            _tenantId     = tenantId;
            _clientId     = clientId;
            _clientSecret = clientSecret;
            _httpClient   = BuildHttpClient(proxyUrl);
        }

        // ── Veřejné metody ──────────────────────────────────────────────────

        /// <summary>
        /// Inicializuje full import – nastaví NextLink na první stránku delta query.
        /// Po průchodu všech stránek bude k dispozici DeltaLink jako watermark.
        /// </summary>
        public void InitFullImport()
        {
            NextLink  = $"{GraphBaseUrl}/users/delta?$select={SelectFields}&$top=999";
            DeltaLink = null;
        }

        /// <summary>
        /// Inicializuje delta import ze uloženého delta tokenu (watermark).
        /// </summary>
        public void InitDeltaImport(string savedDeltaLink)
        {
            if (string.IsNullOrWhiteSpace(savedDeltaLink))
                throw new ArgumentException(
                    "Delta import vyžaduje platný DeltaLink z předchozího běhu.", 
                    nameof(savedDeltaLink));

            NextLink  = savedDeltaLink;
            DeltaLink = null;
        }

        /// <summary>
        /// Načte jednu stránku uživatelů (max 999) a aktualizuje NextLink / DeltaLink.
        /// Vrací seznam uživatelů jako JObject; prázdný seznam = konec dat.
        /// </summary>
        public async Task<IList<JObject>> GetNextPageAsync()
        {
            if (string.IsNullOrEmpty(NextLink))
                return Array.Empty<JObject>();

            await EnsureAccessTokenAsync();

            var response = await _httpClient.GetAsync(NextLink);

            if (response.StatusCode == HttpStatusCode.Unauthorized)
            {
                // Token mohl expirovat při dlouhém běhu – obnov a zkus znovu
                _tokenExpiry = DateTime.MinValue;
                await EnsureAccessTokenAsync();
                response = await _httpClient.GetAsync(NextLink);
            }

            if (!response.IsSuccessStatusCode)
            {
                var body = await response.Content.ReadAsStringAsync();
                throw new InvalidOperationException(
                    $"Graph API vrátilo {(int)response.StatusCode}: {body}");
            }

            var json = JObject.Parse(await response.Content.ReadAsStringAsync());

            // Aktualizuj stav stránkování
            if (json.TryGetValue("@odata.nextLink", out var next))
            {
                NextLink = next.ToString();
            }
            else if (json.TryGetValue("@odata.deltaLink", out var delta))
            {
                DeltaLink = delta.ToString();
                NextLink  = null;        // žádné další stránky
            }
            else
            {
                NextLink = null;
            }

            var users = new List<JObject>();
            if (json["value"] is JArray arr)
            {
                foreach (var item in arr)
                {
                    if (item is JObject obj)
                        users.Add(obj);
                }
            }

            return users;
        }

        // ── Privátní pomocné metody ─────────────────────────────────────────

        private async Task EnsureAccessTokenAsync()
        {
            if (!string.IsNullOrEmpty(_accessToken) && DateTime.UtcNow < _tokenExpiry)
                return;

            var tokenUrl = $"{LoginBaseUrl}/{_tenantId}/oauth2/v2.0/token";
            var body = new FormUrlEncodedContent(new[]
            {
                new KeyValuePair<string, string>("grant_type",    "client_credentials"),
                new KeyValuePair<string, string>("client_id",     _clientId),
                new KeyValuePair<string, string>("client_secret", _clientSecret),
                new KeyValuePair<string, string>("scope",         GraphScope),
            });

            var response = await _httpClient.PostAsync(tokenUrl, body);
            if (!response.IsSuccessStatusCode)
            {
                var err = await response.Content.ReadAsStringAsync();
                throw new UnauthorizedAccessException(
                    $"Nepodařilo se získat access token: {err}");
            }

            var json       = JObject.Parse(await response.Content.ReadAsStringAsync());
            _accessToken   = json["access_token"].ToString();
            int expiresIn  = json["expires_in"]?.ToObject<int>() ?? 3600;
            _tokenExpiry   = DateTime.UtcNow.AddSeconds(expiresIn - 60); // 60s buffer

            _httpClient.DefaultRequestHeaders.Authorization =
                new AuthenticationHeaderValue("Bearer", _accessToken);
        }

        private static HttpClient BuildHttpClient(string proxyUrl)
        {
            HttpClientHandler handler;

            if (!string.IsNullOrWhiteSpace(proxyUrl))
            {
                handler = new HttpClientHandler
                {
                    Proxy    = new WebProxy(proxyUrl)
                               { UseDefaultCredentials = true },
                    UseProxy = true,
                };
            }
            else
            {
                handler = new HttpClientHandler { UseProxy = false };
            }

            return new HttpClient(handler)
            {
                Timeout = TimeSpan.FromSeconds(120),
            };
        }

        // ── Pomocné statické metody ─────────────────────────────────────────

        /// <summary>Bezpečná konverze SecureString → string.</summary>
        public static string SecureStringToString(SecureString secure)
        {
            if (secure == null) return string.Empty;
            var ptr = Marshal.SecureStringToGlobalAllocUnicode(secure);
            try   { return Marshal.PtrToStringUni(ptr); }
            finally { Marshal.ZeroFreeGlobalAllocUnicode(ptr); }
        }

        public void Dispose() => _httpClient?.Dispose();
    }
}
