using System.Collections.Generic;
using System.IO;
using System.Net;
using EasyIDMMA.Enums;
using EasyIDMMA.Import;
using EasyIDMMA.Model.Parameters;
using Microsoft.MetadirectoryServices;
using Newtonsoft.Json;
using NLog;

namespace EasyIDMMA.Export
{
    public static class ExportService
    {
        private static Logger Logger = LogManager.GetCurrentClassLogger();
        
        /// <summary>
        /// Creates and configures a WebRequest to Portal.
        /// </summary>
        private static WebRequest CreateWebRequest(WebRequestType requestType, string url, string token, string data = null)
        {
            var webRequest = WebRequest.Create(url);
            webRequest.Method = requestType.ToString();
            webRequest.Headers["Authorization"] = $"Bearer {token}";
            webRequest.ContentType = "application/json";
            webRequest.Timeout = 1500000; // 25 minutes

            if (requestType == WebRequestType.POST && !string.IsNullOrEmpty(data))
            {
                using (var streamWriter = new StreamWriter(webRequest.GetRequestStream()))
                {
                    streamWriter.Write(data);
                }
            }
            
            Logger.Info($"{requestType.ToString()} web request created at url {url}");
            return webRequest;
        }
        
        /// <summary>
        /// Checks progress of running async export for exported CSEntry.
        /// If export is still running, then checks the progress again (loop) until it is finished.
        /// </summary>
        public static CSEntryChangeResult CheckExportProgress(string url, CSEntryChange csEntry, SynchronizationParameters synchronizationParameters, string idmObjectId)
        {
            Logger.Info($"Checking running export for IDM object {idmObjectId}");
            var token = AccessTokenService.GetToken(synchronizationParameters);
            do
            {
                // Check token expiration
                if (AccessTokenService.WillExpireSoon())
                {
                    token = AccessTokenService.GetToken(synchronizationParameters);
                }
                
                var webRequest = CreateWebRequest(WebRequestType.GET, url, token);
                try
                {
                    Logger.Info("Getting response from portal");
                    using (var response = webRequest.GetResponse() as HttpWebResponse)
                    {
                        // No response from server
                        if (response == null)
                        {
                            var noResponseDescription = "No response received from the server";
                            Logger.Error(noResponseDescription);
                            return CSEntryChangeResult.Create(
                                csEntry.Identifier,
                                csEntry.AttributeChanges,
                                MAExportError.ExportErrorCustomContinueRun,
                                "eidm-export-error",
                                noResponseDescription
                            );
                        }

                        // Processing is finished or still in progress
                        switch (response.StatusCode)
                        {
                            case HttpStatusCode.OK:
                                Logger.Info($"IDM object {idmObjectId} successfully exported to Portal");
                                return CSEntryChangeResult.Create(csEntry.Identifier, null, MAExportError.Success);
                            case HttpStatusCode.Accepted:
                                Logger.Info($"Processing of IDM Object {idmObjectId} is still in progress");
                                break;
                        }
                    }
                }
                catch (WebException ex)
                {
                    // Validation error from Portal
                    if (ex.Response is HttpWebResponse errorResponse && errorResponse.StatusCode == HttpStatusCode.BadRequest)
                    {
                        string errorBody;
                        using (var stream = errorResponse.GetResponseStream())
                        using (var reader = new StreamReader(stream))
                        {
                            errorBody = reader.ReadToEnd();
                        }

                        var serverErrorDescription = $"Error response from portal while exporting IDM object {idmObjectId} of CSEntry:";
                        Logger.Error(serverErrorDescription + "\r\n" + errorBody);
                        return CSEntryChangeResult.Create(
                            csEntry.Identifier,
                            csEntry.AttributeChanges,
                            MAExportError.ExportErrorCustomContinueRun,
                            "eidm-export-error",
                            serverErrorDescription + "\r\n" + errorBody
                        );
                    }
                    
                    // Connection error
                    var errorDescription = $"Error while exporting IDM object {idmObjectId} of CSEntry:";
                    Logger.Error(errorDescription);
                    return CSEntryChangeResult.Create(
                        csEntry.Identifier,
                        csEntry.AttributeChanges,
                        MAExportError.ExportErrorCustomContinueRun,
                        "failed-connection",
                        errorDescription + "\r\n" + ex.Message
                    );
                }
            } while (true);
        }

        public static ExportResponse ExportToPortal(ExportParameters exportParameters)
        {
            var token = AccessTokenService.GetToken(exportParameters);
            var webRequest = CreateWebRequest(WebRequestType.POST, exportParameters.ExportUrl, token, exportParameters.Data);
            var exportResponse = new ExportResponse();

            try
            {
                Logger.Info("Getting response from portal");
                using (var response = webRequest.GetResponse() as HttpWebResponse)
                {
                    // No response from server
                    if (response == null)
                    {
                        exportResponse.ResultType = ExportResultType.ConnectionError;
                        exportResponse.ErrorMessage = "No response received from server.";
                        return exportResponse;
                    }

                    exportResponse.StatusCode = (int)response.StatusCode;

                    // Error response from server
                    if (response.StatusCode != HttpStatusCode.OK)
                    {
                        exportResponse.ResultType = ExportResultType.ServerError;
                        using (var stream = response.GetResponseStream())
                        {
                            if (stream != null)
                            {
                                using (var reader = new StreamReader(stream))
                                {
                                    exportResponse.ErrorMessage = reader.ReadToEnd();
                                }
                            }
                            else
                            {
                                exportResponse.ErrorMessage = $"Error response returned status {response.StatusCode} with no content.";
                            }
                        }
                        return exportResponse;
                    }

                    exportResponse.ResultType = ExportResultType.Success;
                    return exportResponse;
                }
            }
            catch (WebException webException)
            {
                // No response = connection error
                var exceptionResponse = webException.Response as HttpWebResponse;
                if (exceptionResponse == null)
                {
                    exportResponse.ResultType = ExportResultType.ConnectionError;
                    exportResponse.ErrorMessage =
                        $"WebException occurred but no response was returned. Status: {webException.Status}, Message: {webException.Message}";
                    return exportResponse;
                }

                // Server (validation) error
                exportResponse.ResultType = ExportResultType.ServerError;
                exportResponse.StatusCode = (int)exceptionResponse.StatusCode;
                using (var stream = exceptionResponse.GetResponseStream())
                {
                    if (stream != null)
                    {
                        using (var reader = new StreamReader(stream))
                        {
                            exportResponse.ErrorMessage = reader.ReadToEnd();
                        }
                    }
                    else
                    {
                        exportResponse.ErrorMessage =
                            $"WebException returned status {exceptionResponse.StatusCode} with no content.";
                    }
                }
                return exportResponse;
            }
        }

        /// <summary>
        /// Exports IDM object to Portal which returns async export response with id of process to check the status of the running export.
        /// </summary>
        public static List<AsyncExportResponseItem> ExportToPortalAsync(AsyncExportParameters asyncExportParameters)
        {
            var token = AccessTokenService.GetToken(asyncExportParameters);
            var dataForExport = BuildAsyncExportBody(asyncExportParameters);
            var webRequest = CreateWebRequest(WebRequestType.POST, asyncExportParameters.ExportUrl, token, dataForExport);

            Logger.Info("Getting response from portal");
            using (var response = webRequest.GetResponse() as HttpWebResponse)
            using (var stream = response.GetResponseStream())
            using (var reader = new StreamReader(stream))
            {
                var responseString = reader.ReadToEnd();
                var responseObject = JsonConvert.DeserializeObject<List<AsyncExportResponseItem>>(responseString);

                Logger.Info($"Response from Portal: {responseString}");
                return responseObject;
            }
        }

        private static string BuildAsyncExportBody(AsyncExportParameters asyncExportParameters)
        {
            return JsonConvert.SerializeObject(
                new[]
                {
                    new AsyncExportItem
                    {
                        Id = asyncExportParameters.ExportId,
                        IDMObject = ExportMapper.MapIdmObjectToExportData(asyncExportParameters.IdmObject)
                    }
                },
                Formatting.None,
                new JsonSerializerSettings
                {
                    NullValueHandling = NullValueHandling.Ignore
                });
        }
    }
}