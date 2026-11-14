using System;
using System.Linq;
using System.Xml.Serialization;

namespace EasyIDMMA.Model.Connector
{
    [XmlRoot("Schema")]
    public class ConnectorSchema
    {
        [XmlElement("AnchorAttribute")]
        public string AnchorAttribute { get; set; }

        [XmlArray("ObjectTypes")]
        [XmlArrayItem("ObjectType")]
        public ObjectType[] ObjectTypes { get; set; } = Array.Empty<ObjectType>();

        public ObjectType GetObjectType(string typeName)
        {
            return ObjectTypes.FirstOrDefault(t => t.TypeName == typeName);
        }
    }
}