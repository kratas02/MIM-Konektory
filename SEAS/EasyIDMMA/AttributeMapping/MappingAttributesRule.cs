using EasyIDMMA.Enums;

namespace EasyIDMMA.AttributeMapping
{
    public class MappingAttributesRule
    {
        public OperationDirection OperationDirection { get; set; }
        public string Rule { get; set; }
        public string ConnectorSpaceAttribute { get; set; }
        public string MetaverseAttribute { get; set; }
        public string ConnectorSpaceObjectType { get; set; }
        public string MetaverseObjectType { get; set; }
    }
}