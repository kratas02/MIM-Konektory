using System.Xml.Serialization;

namespace EasyIDMMA.Model.Connector
{
    public enum AttributeTypeEnum
    {
        [XmlEnum(Name = "string")]
        String,

        [XmlEnum(Name = "int")]
        Integer,

        [XmlEnum(Name = "bool")]
        Boolean,

        [XmlEnum(Name = "reference")]
        Reference
    }
}