using System.Collections.Generic;

namespace EasyIDMMA.Model.Connector
{
    public class Join
    {
        public List<JoinRule> Rules { get; set; } = new List<JoinRule>();
    }
    
    public class JoinRule
    {
        public ConnectorConfiguration ConnectorConfiguration { get; set; }
        public string SourceType { get; set; }
        public string DestinationType { get; set; }
        public List<JoinAttribute> Attributes { get; set; } = new List<JoinAttribute>();
    }

    public class JoinAttribute
    {
        public string SourceAttributeName { get; set; }
        public string MetaverseAttributeName { get; set; }
    }

    public class ConnectorJoinAttribute
    {
        public ConnectorJoinAttribute(string name, ConnectorConfiguration connectorConfiguration)
        {
            Name = name;
            ConnectorConfiguration = connectorConfiguration;
        }

        public string Name { get; }
        public ConnectorConfiguration ConnectorConfiguration { get; }
    }
}