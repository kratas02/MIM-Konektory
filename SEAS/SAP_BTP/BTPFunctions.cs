using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Text;
using System.Threading.Tasks;

namespace FimSync_Ezma
{
    public static class BTPFunctions
    {
        public static string logFolder;
        public static AccessToken GetToken(string proxy, string sURL, string authStr)
        {
            ServicePointManager.ServerCertificateValidationCallback = delegate { return true; };
            ServicePointManager.SecurityProtocol = SecurityProtocolType.Tls12;
            //string sURL = "https://edu-axde4s15.authentication.eu20.hana.ondemand.com/oauth/token?grant_type=client_credentials&response_type=token";
            //string authStr = "sb-na-904000e3-653d-4e22-9644-1683cb155d3e!a72669:ea10c2bb-5afc-430c-86ea-021d5a30ccf1$dMkCFTnKB25-RkrXQBZlupx_FxkgDV0EU7fp74bPuOU=";
            var authBytes = System.Text.Encoding.UTF8.GetBytes(authStr);
            string authBase64 = Convert.ToBase64String(authBytes);
            WebRequest wrGETURL;
            wrGETURL = WebRequest.Create(sURL);
            if (!string.IsNullOrEmpty(proxy))
            {
                wrGETURL.Proxy = new WebProxy(proxy, true);
            }
            wrGETURL.Method = "POST";
            //@"application/json; charset=utf-8";
            wrGETURL.ContentType = @"application/json";
            wrGETURL.Headers.Add("Authorization", "Basic " + authBase64);

            HttpWebResponse webresponse = wrGETURL.GetResponse() as HttpWebResponse;
            Encoding enc = System.Text.Encoding.GetEncoding("utf-8");
            // read response stream from response object
            StreamReader loResponseStream = new StreamReader(webresponse.GetResponseStream(), enc);
            // read string from stream data
            string strResult = loResponseStream.ReadToEnd();
            // close the stream object
            loResponseStream.Close();
            // close the response object
            webresponse.Close();

            return Deserialize<AccessToken>(strResult);
        }
        public static string HttpGet(string sURL, string accessToken, string proxy, int maxRetries = 6)
        {
            for (int attempt = 1; attempt <= maxRetries; attempt++)
            {
                WebRequest wrGETURL = WebRequest.Create(sURL);
                if (!string.IsNullOrEmpty(proxy))
                    wrGETURL.Proxy = new WebProxy(proxy, true);
                wrGETURL.Method = "GET";
                wrGETURL.ContentType = @"application/json";
                wrGETURL.Headers.Add("Authorization", "Bearer " + accessToken);

                try
                {
                    HttpWebResponse webresponse = wrGETURL.GetResponse() as HttpWebResponse;
                    Encoding enc = Encoding.GetEncoding("utf-8");
                    StreamReader loResponseStream = new StreamReader(webresponse.GetResponseStream(), enc);
                    string strResult = loResponseStream.ReadToEnd();
                    loResponseStream.Close();
                    webresponse.Close();
                    return strResult;
                }
                catch (WebException wex)
                {
                    var httpResponse = wex.Response as HttpWebResponse;
                    if (httpResponse != null && (int)httpResponse.StatusCode == 429 && attempt < maxRetries)
                    {
                        int waitSeconds = 10;
                        string retryAfterHeader = httpResponse.Headers["Retry-After"];
                        if (!string.IsNullOrEmpty(retryAfterHeader) && int.TryParse(retryAfterHeader, out int ra) && ra > 0)
                            waitSeconds = ra;
                        waitSeconds = Math.Max(waitSeconds, 5 * attempt);
                        if (waitSeconds > 120)
                            waitSeconds = 120;
                        Log($"429 Too Many Requests ({sURL}) – waiting {waitSeconds}s (attempt {attempt}/{maxRetries})");
                        try { httpResponse.Close(); } catch { }
                        System.Threading.Thread.Sleep(waitSeconds * 1000);
                        continue;
                    }
                    if (httpResponse != null)
                    {
                        try { httpResponse.Close(); } catch { }
                    }
                    throw;
                }
            }
            throw new WebException("GET failed after retries: " + sURL);
        }

        public static T Deserialize<T>(string responseBody)
        {
            //responseBody = responseBody.Replace("\"", "'");
            try
            {
                var toReturns = JsonConvert.DeserializeObject<T>(responseBody);
                return toReturns;
            }
            catch (Exception ex)
            {
                Log("chyba deserializace: " + ex.Message);
                Log(responseBody);
                string errores;
                errores = ex.Message;
            }
            var toReturn = JsonConvert.DeserializeObject<T>(responseBody);
            return toReturn;
        }

        public static void  Log(String msg)
        {
            //string logFolder = @"C:\Batch\Logs\BTP";
            string logfile = Path.Combine(logFolder, DateTime.Now.ToString("yyyyMMdd") + "_MAlog.txt");
            File.AppendAllText(logfile,"[" + DateTime.Now.ToString("HH:mm:ss") +"] " + msg + Environment.NewLine);
        }

    }


}
