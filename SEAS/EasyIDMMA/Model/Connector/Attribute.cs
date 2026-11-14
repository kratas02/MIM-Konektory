using System.Xml.Serialization;

namespace EasyIDMMA.Model.Connector
{
    public class Attribute
    {
        [XmlText]
        public string Name { get; set; }

        [XmlAttribute("dataType")]
        public AttributeTypeEnum AttributeTypeEnum { get; set; }

        [XmlAttribute("isIdentifier")]
        [System.ComponentModel.DefaultValue(false)]
        public bool IsIdentifier { get; set; }

        [XmlAttribute("isNullable")]
        [System.ComponentModel.DefaultValue(false)]
        public bool IsNullable { get; set; }

        [XmlAttribute("isMultivalued")]
        [System.ComponentModel.DefaultValue(false)]
        public bool IsMultiValued { get; set; }

        [XmlAttribute("isReferenceMapping")]
        [System.ComponentModel.DefaultValue(false)]
        public bool IsReferenceMapping { get; set; }
        
        public Attribute() { }

        public Attribute(
            string name,
            AttributeTypeEnum attributeTypeEnum,
            bool isNullable = false,
            bool isIdentifier = false,
            bool isMultiValued = false,
            bool isReferenceMapping = false
        )
        {
            Name = name;
            IsNullable = isNullable;
            AttributeTypeEnum = attributeTypeEnum;
            IsIdentifier = isIdentifier;
            IsMultiValued = isMultiValued;
            IsReferenceMapping = isReferenceMapping;
        }
    }
}