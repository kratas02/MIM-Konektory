using System;
using System.Collections.Generic;
using System.Linq;
using System.Xml.Linq;
using System.Xml.Serialization;

namespace EasyIDMMA.Model.Connector
{
    [XmlRoot("Connector")]
    public class ConnectorConfiguration
    {
        [XmlElement("Id")]
        public Guid Id { get; set; }

        [XmlElement("Name")]
        public string Name { get; set; }

        [XmlElement("Created")]
        public DateTime Created { get; set; }

        [XmlElement("Description")]
        public string Description { get; set; }

        [XmlElement("Schema")]
        public ConnectorSchema ConnectorSchema { get; set; }

        [XmlElement("Settings")]
        public XElement Settings { get; set; }

        private Dictionary<string, string> GetObjectTypeMappings()
        {
            var objectTypeMappings = Settings
                .Element("TypeMappings")
                .Elements("TypeMapping")
                .OfType<XElement>()
                .ToDictionary(
                    e => e.Value,
                    e => e.Attribute("idmObjectType").Value);

            return objectTypeMappings;
        }
        
        public string MapIDMObjectTypeNameToId(string objectType)
        {
            var objectTypeMappings = GetObjectTypeMappings();
            if (!objectTypeMappings.TryGetValue(objectType, out var objectTypeId))
            {
                throw new ArgumentException($"Error when mapping object type to id: object type \"{objectType}\" is not supported");
            }

            return objectTypeId;
        }

        public Attribute[] GetAttributesByObjectType(string objectTypeName)
        {
            return ConnectorSchema.GetObjectType(objectTypeName).Attributes;
        }

        public bool IsExportAsync()
        {
            var asyncExportElement = Settings.Element("AsyncExport");
            if (asyncExportElement == null)
            {
                return false;
            }

            return bool.TryParse(asyncExportElement.Value, out var result) && result;
        }

        public int? GetDeltaFallbackHours()
        {
            var deltaFallbackHoursElement = Settings.Element("DeltaFallbackHours");
            if (deltaFallbackHoursElement == null)
            {
                return null;
            }

            return int.TryParse(deltaFallbackHoursElement.Value, out var result) && result > 0 ? result : (int?)null;
        }
    }
}