using System.Collections.Generic;
using EasyIDMMA.Model.Connector;

namespace EasyIDMMA.AttributeMapping
{
    public class ImportObjectFlow
    {
        public List<ImportObjectTypeFlow> ImportObjectTypeFlows { get; set; } =
            new List<ImportObjectTypeFlow>();
    }

    public class ImportObjectTypeFlow
    {
        public string TypeName { get; set; }

        public List<ImportAttributeFlow> ImportAttributeFlows { get; set; } =
            new List<ImportAttributeFlow>();
    }

    public class ImportAttributeFlow
    {
        public MappingAttributesRule MappingAttributesRule { get; set; }
        public Attribute Attribute { get; set; }

        public string AttributeName => Attribute.Name;

        public List<ImportFlow> ImportFlows { get; set; } = new List<ImportFlow>();

        public ImportAttributeFlow() { }

        public ImportAttributeFlow(MappingAttributesRule mappingAttributesRule, Attribute attribute, List<ImportFlow> importFlows)
        {
            MappingAttributesRule = mappingAttributesRule;
            Attribute = attribute;
            ImportFlows = importFlows;
        }
    }

    public class ImportFlow
    {
        public ImportFlow(
            string typeName,
            ConnectorConfiguration connectorConfiguration,
            ImportFlowMapping importAttributeFlowMapping,
            bool nullable = false
        )
        {
            TypeName = typeName;
            ConnectorConfiguration = connectorConfiguration;
            ImportAttributeFlowMapping = importAttributeFlowMapping;
            Nullable = nullable;
        }

        public ImportFlow() { }

        public ImportFlowMapping ImportAttributeFlowMapping { get; set; }

        public bool Nullable { get; set; }

        public ConnectorConfiguration ConnectorConfiguration { get; set; }

        public string TypeName { get; set; }
    }

    public class ImportFlowMapping { }

    public class ImportFlowFormula : ImportFlowMapping
    {
        public ImportFlowFormula(string formula)
        {
            Formula = formula;
        }

        public string Formula { get; }
    }
}