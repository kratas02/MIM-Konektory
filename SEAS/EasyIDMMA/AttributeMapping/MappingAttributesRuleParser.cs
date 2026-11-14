using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using EasyIDMMA.Enums;
using EasyIDMMA.Model.Connector;
using Attribute = EasyIDMMA.Model.Connector.Attribute;

namespace EasyIDMMA.AttributeMapping
{
    public static class MappingAttributesRuleParser
    {
        public static OperationDirection ExtractOperationDirectionFromRule(string rule)
        {
            var direction = rule.Contains("->") ? OperationDirection.Import : OperationDirection.Export;
            return direction;
        }

        public static Tuple<string, string> ExtractAttributeNamesFromRule(string rule, OperationDirection operationDirection)
        {
            var separator = operationDirection == OperationDirection.Import ? "->" : "<-";
            var ruleRegex = $@"^(\w+(\.\w+)*):((\w+)(,\w+)*){separator}(\w+(\.\w+)*):(\w+)$";

            if (!Regex.IsMatch(rule, ruleRegex))
            {
                throw new FormatException($"Rule: {rule} does not match the expected format.");
            }

            var parts = rule.Split(new[] { separator }, StringSplitOptions.None);
            var csAtt = parts[0].Split(':')[1];
            var mvAtt = parts[1].Split(':')[1];
            return Tuple.Create(csAtt, mvAtt);
        }

        public static Tuple<string, string> ExtractObjectTypeNamesFromRule(string rule)
        {
            const string pattern = @"\b\w+\.(\w+):";
            var matches = Regex.Matches(rule, pattern);

            if (matches.Count >= 2)
            {
                var csObj = matches[0].Groups[1].Value;
                var mvObj = matches[1].Groups[1].Value;
                return Tuple.Create(csObj, mvObj);
            }
            else
            {
                throw new FormatException($"Rule: {rule} does not match the expected format.");
            }
        }
        
        public static ImportAttributeFlow ParseImportRule(string rule, string importFlowFormula = null)
        {
            var flowRule = ParseRule(rule);
            var formula = string.IsNullOrEmpty(importFlowFormula) ? $"[{flowRule.ConnectorSpaceAttribute}]" : importFlowFormula;

            var importAttributeFlow = new ImportAttributeFlow
            {
                MappingAttributesRule = flowRule,
                Attribute = new Attribute(
                    flowRule.MetaverseAttribute,
                    AttributeTypeEnum.String
                ),
                ImportFlows = new List<ImportFlow>
                {
                    new ImportFlow
                    {
                        TypeName = flowRule.ConnectorSpaceObjectType,
                        ImportAttributeFlowMapping = new ImportFlowFormula(formula)
                    }
                }
            };

            return importAttributeFlow;
        }

        public static MappingAttributesRule ParseRule(string rule)
        {
            var operationDirection = ExtractOperationDirectionFromRule(rule);
            var attr = ExtractAttributeNamesFromRule(rule, operationDirection);
            var objType = ExtractObjectTypeNamesFromRule(rule);

            return new MappingAttributesRule
            {
                Rule = rule,
                OperationDirection = operationDirection,
                ConnectorSpaceAttribute = attr.Item1,
                MetaverseAttribute = attr.Item2,
                ConnectorSpaceObjectType = objType.Item1,
                MetaverseObjectType = objType.Item2
            };
        }
    }
}