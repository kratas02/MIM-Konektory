using System.Collections.Generic;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using NLog;

namespace EasyIDMMA.Export
{
    public static class ExportMapper
    {
        private static readonly Logger Logger = LogManager.GetCurrentClassLogger();
        private static readonly Logger ExportLogger = LogManager.GetLogger("Export");
        
        public static string MapIdmObjectsToExportData(List<IDMObject> idmObjects)
        {
            var jArray = new JArray();
        
            foreach (var idmObject in idmObjects)
            {
                var jObject = MapIdmObjectToExportData(idmObject);
                jArray.Add(jObject);
            }
        
            var result = JsonConvert.SerializeObject(jArray,
                                Formatting.None,
                                new JsonSerializerSettings
                                {
                                    NullValueHandling = NullValueHandling.Ignore
                                });

            return result;
        }

        public static JObject MapIdmObjectToExportData(IDMObject idmObject)
        {
            Logger.Info($"Mapping IDM object {idmObject.IDMObjectId} to export data");
            var jObject = new JObject();
            
                // Iterate through properties of IDMObject and add them to JObject
                foreach (var property in idmObject.GetProperties())
                {
                    var value = property.GetValue(idmObject);
                    switch (property.Name)
                    {
                        case "Attributes":
                        case "Values":
                            break;
                        case "IsArchived":
                            jObject[property.Name] = value != null ? JToken.FromObject(value) : JToken.FromObject(false);
                            break;
                        case "AppRole":
                        case "Member":
                        case "ManualMemberAndParentBr":
                        case "AutoCastRule":
                            if (value != null)
                            {
                                jObject[property.Name] = JToken.FromObject(value);
                            }
                            break;
                        default:
                            jObject[property.Name] = value != null ? JToken.FromObject(value) : null;
                            break;
                    }
                }
            
                // Iterate through attributes of IDMObject and add them to "Attributes" JObject
                var attributes = new JObject();
                foreach (var attribute in idmObject.Attributes)
                {
                    if (idmObject.Values.TryGetValue(attribute.Name, out var attributeValue))
                    {
                        attributes[attribute.Name] = attributeValue != null ? JToken.FromObject(attributeValue) : null;
                    }
                }
                jObject["Attributes"] = attributes;

            ExportLogger.Info($"Data for export of IDM object {idmObject.IDMObjectId}:\n{jObject}");

            return jObject;
        }
    }
}