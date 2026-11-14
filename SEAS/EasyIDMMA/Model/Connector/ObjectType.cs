using System;
using System.Linq;
using System.Xml.Serialization;

namespace EasyIDMMA.Model.Connector
{
    [XmlRoot("ObjectType")]
    public class ObjectType
    {
        [XmlAttribute("name")]
        public string TypeName { get; set; }

        [XmlArray("Attributes")]
        [XmlArrayItem("Attribute")]
        public Attribute[] Attributes { get; set; }
        
        public ObjectType() { }

        public ObjectType(string typeName, params Attribute[] attributes)
        {
            TypeName = typeName;
            Attributes = attributes ?? Array.Empty<Attribute>();
        }

        public Attribute GetAttribute(string attributeName)
        {
            return Attributes.FirstOrDefault(
                a => a.Name.Equals(attributeName, StringComparison.OrdinalIgnoreCase)
            );
        }
    }
}