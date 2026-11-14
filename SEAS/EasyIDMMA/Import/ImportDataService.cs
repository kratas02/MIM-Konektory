using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using Newtonsoft.Json;
using NLog;

namespace EasyIDMMA.Import
{
    public class ImportDataService
    {
        private static Logger Logger = LogManager.GetCurrentClassLogger();
        
        public List<IDMObject> Import(ImportParameters importParams)
        {
            var objects = new List<IDMObject>();
            var carryOn = true;
            var pagination = new Pagination
            {
                currentPage = 1,
                itemsPerPage = importParams.PageSize
            };

            var token = AccessTokenService.GetToken(importParams);

            var connectorObjectType = importParams.ConnectorSchema.GetObjectType(importParams.ObjectTypeName);

            if (connectorObjectType == null)
            {
                throw new ArgumentException(
                    $"Object type \"{importParams.ObjectTypeName}\" was not found in connector schema configuration.");
            }

            while (carryOn)
            {
                // Check token expiration
                if (AccessTokenService.WillExpireSoon())
                {
                    token = AccessTokenService.GetToken(importParams);
                }

                using (var jsonStream = ImportData(importParams.ImportUrl, pagination, token))
                {
                    if (jsonStream != Stream.Null)
                    {
                        Logger.Info($"Reading data, current page: {pagination.currentPage}");
                        var idmObjects = ImportMapper.MapImportDataToObjects(jsonStream, connectorObjectType.Attributes);
                        objects.AddRange(idmObjects);

                        // Check to NOT carry on
                        if (idmObjects.Count < pagination.itemsPerPage ||
                            idmObjects.Count >= pagination.totalItems ||
                            pagination.currentPage >= pagination.totalPages)
                        {
                            carryOn = false;
                        }

                        pagination.currentPage++;
                    }
                    else
                    {
                        Logger.Error($"No data returned, reading finished. Current page: {pagination.currentPage}");
                        carryOn = false;
                    }
                }
            }

            return objects;
        }

        private Stream ImportData(string importUrl, Pagination pagination, string token = "")
        {
            var request = (HttpWebRequest)WebRequest.Create(importUrl);
            request.Method = "GET";
            request.Headers["Authorization"] = $"Bearer {token}";
            var paginationJson = JsonConvert.SerializeObject(pagination);
            request.Headers["Pagination"] = paginationJson;

            try
            {
                Logger.Info("Sending GET request to server");
                using (var response = (HttpWebResponse)request.GetResponse())
                {
                    if (response.StatusCode != HttpStatusCode.OK)
                    {
                        Logger.Error($"Page {pagination.currentPage} returned error status code {(int)response.StatusCode}");
                        return Stream.Null;
                    }

                    var responsePaginationHeader = response.Headers["Pagination"];
                    if (responsePaginationHeader != null && !string.IsNullOrEmpty(responsePaginationHeader))
                    {
                        var responseHeader = JsonConvert.DeserializeObject<Pagination>(responsePaginationHeader);
                        if (responseHeader != null)
                        {
                            pagination.totalPages = responseHeader.totalPages;
                            pagination.totalItems = responseHeader.totalItems;
                            Logger.Info($"Response header details: currentpage [-> {responseHeader.currentPage} <-] total pages [-> {responseHeader.totalPages} <-] total items [-> {responseHeader.totalItems} <-] items per page [-> {responseHeader.itemsPerPage} <-]");
                        }
                    }

                    var responseStream = response.GetResponseStream();
                    if (responseStream == null)
                    {
                        Logger.Error("Stream error: response stream is null");
                        return Stream.Null;
                    }

                    var memoryStream = new MemoryStream();
                    responseStream.CopyTo(memoryStream);
                    memoryStream.Position = 0; // Reset position to the beginning of the stream

                    return memoryStream;
                }
            }
            catch (WebException ex)
            {
                Logger.Error(ex, $"Error during GET request to portal, message: {ex.Message}");
                throw;
            }
        }
    }
}