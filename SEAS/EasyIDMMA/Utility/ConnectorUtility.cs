using System.Collections.Generic;
using System.IO;
using EasyIDMMA.Converters;
using EasyIDMMA.Model.Connector;
using Newtonsoft.Json;

namespace EasyIDMMA.Utility
{
    public static class ConnectorUtility
    {
        public static dynamic LoadConfiguration(string configurationFilePath)
        {
            var configJson = File.ReadAllText(configurationFilePath);
            return JsonConvert.DeserializeObject<Dictionary<string, object>>(configJson);
        }
        
        public static ConnectorConfiguration LoadConnector(string connectorSchemaFilePath)
        {
            return XmlConvert.DeserializeFromFile<ConnectorConfiguration>(connectorSchemaFilePath);
        }
    }
}