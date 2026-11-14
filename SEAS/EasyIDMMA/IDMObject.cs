using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using EasyIDMMA.Exceptions;
using EasyIDMMA.Model.Connector;
using Microsoft.MetadirectoryServices;
using NLog;
using Attribute = EasyIDMMA.Model.Connector.Attribute;

namespace EasyIDMMA
{
    public class IDMObject
    {
        public Attribute[] Attributes { get; set; }

        public Dictionary<string, object> Values { get; set; }

        public string IDMObjectId { get; set; }

        public string Created { get; set; }

        public string Description { get; set; }

        public int IDMObjectTypeId { get; set; }

        public string Name { get; set; }

        public string TechName { get; set; }

        public string ValidFrom { get; set; }

        public string ValidTo { get; set; }

        public bool? IsArchived { get; set; }
        
        private static Logger Logger = LogManager.GetCurrentClassLogger();
        
        public string Member { get; set; }
        
        public string ManualMemberAndParentBr { get; set; }
        
        public string AppRole { get; set; }

        public string AutoCastRule { get; set; }

        public IDMObject()
        {
            
        }

        public IDMObject(Attribute[] attributes)
        {
            this.Attributes = attributes;
            this.Values = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);
        }

        public PropertyInfo[] GetProperties()
        {
            return this.GetType().GetProperties();
        }

        public object GetAttributeValue(string attributeName)
        {
            if (string.IsNullOrEmpty(attributeName))
            {
                return null;
            }

            if (this.Values != null && this.Values.TryGetValue(attributeName, out var attributeValue) && attributeValue != null)
            {
                return attributeValue;
            }

            return null;
        }

        /// <param name="selectedAttributes">
        /// Names of attributes selected in the Management Agent for the object type (compared case-insensitively).
        /// Attributes that are not in the set are not written to the CSEntry. When null, no filtering is applied.
        /// </param>
        public CSEntryChange MapToCSEntry(string objectTypeName, string anchorAttributeName, bool isDelta = false, ISet<string> selectedAttributes = null)
        {
            Logger.Info($"Mapping IDM object {this.Name} of type {objectTypeName} to CSEntry");

            bool IsSelected(string name) => selectedAttributes == null || selectedAttributes.Contains(name);

            var csEntry = CSEntryChange.Create();
            csEntry.ObjectType = objectTypeName;
            if (this.IsArchived == true)
            {
                Logger.Info("Archived IDM object detected during import. CSEntry object modification type will be changed to Delete");
                csEntry.ObjectModificationType = ObjectModificationType.Delete;
                csEntry.AnchorAttributes.Add(AnchorAttribute.Create(anchorAttributeName, this.IDMObjectId));
                return csEntry;
            }
            csEntry.ObjectModificationType = ObjectModificationType.Add;

            var anchorAttributeIsPresent = false;

            var properties = GetProperties();
            foreach (var property in properties)
            {
                var propertyValue = property.GetValue(this);
                if (propertyValue != null)
                {
                    switch (property.Name)
                    {
                        case "Member":
                            if (this.Member != null && IsSelected("member"))
                            {
                                var members = SplitToDistinctGuids(this.Member); // List of GUIDs
                                if (members.Count > 0)
                                {
                                    csEntry.AttributeChanges.Add(AttributeChange.CreateAttributeAdd("member", members.ToArray()));
                                }
                            }
                            break;
                        case "ManualMemberAndParentBr":
                            if (this.ManualMemberAndParentBr != null && IsSelected("ManualMemberAndParentBr"))
                            {
                                var refs = SplitToDistinctGuids(this.ManualMemberAndParentBr); // List of GUIDs
                                if (refs.Count > 0)
                                {
                                    csEntry.AttributeChanges.Add(AttributeChange.CreateAttributeAdd("ManualMemberAndParentBr", refs.ToArray()));
                                }
                            }
                            break;
                        case "AppRole":
                            if (this.AppRole != null && IsSelected("AppRole"))
                            {
                                var appRolesList = SplitToDistinctGuids(this.AppRole); // List of GUIDs
                                if (appRolesList.Count > 0)
                                {
                                    csEntry.AttributeChanges.Add(AttributeChange.CreateAttributeAdd("AppRole", appRolesList.ToArray()));
                                }
                            }
                            break;
                        case "AutoCastRule":
                            if (this.AutoCastRule != null && IsSelected("AutoCastRule"))
                            {
                                csEntry.AttributeChanges.Add(AttributeChange.CreateAttributeAdd("AutoCastRule", propertyValue));
                            }
                            break;
                        case "IsArchived":
                            if (IsSelected("IsArchived"))
                            {
                                csEntry.AttributeChanges.Add(AttributeChange.CreateAttributeAdd("IsArchived", propertyValue));
                            }
                            break;
                        case "IDMObjectId":
                            Logger.Info($"Value of Anchor Attribute IDMObjectId: {propertyValue}");
                            csEntry.AnchorAttributes.Add(AnchorAttribute.Create(anchorAttributeName, propertyValue.ToString()));
                            anchorAttributeIsPresent = true;
                            break;
                    }
                }
            }

            foreach (var attribute in Attributes)
            {
                if (!Values.TryGetValue(attribute.Name, out var attributeValue) || attributeValue == null)
                {
                    continue;
                }

                // Add Anchor Attribute
                if (attribute.Name.Equals(anchorAttributeName, StringComparison.OrdinalIgnoreCase) && !anchorAttributeIsPresent)
                {
                    Logger.Info($"Value of Anchor Attribute {attribute.Name}: {attributeValue}");
                    csEntry.AnchorAttributes.Add(AnchorAttribute.Create(anchorAttributeName, attributeValue.ToString()));
                    anchorAttributeIsPresent = true;
                    continue;
                }

                // MIM Does not support attribute named "dn"
                var attributeName = attribute.Name;
                if (attributeName.Equals("dn"))
                {
                    attributeName = "adDN";
                }

                // Attribute is not selected in the Management Agent, MIM would not accept it
                if (!IsSelected(attributeName))
                {
                    continue;
                }

                // Multivalued attribute
                if (attribute.IsMultiValued)
                {
                    var attributeStringValue = attributeValue.ToString();
                    char[] possibleSeparators = { ';', ',' };
                    var values = new List<string>();
                    
                    foreach (var value in attributeStringValue.Split(possibleSeparators, StringSplitOptions.RemoveEmptyEntries))
                    {
                        if (!values.Contains(value))
                        {
                            values.Add(value);
                        }
                    }

                    if (values.Count > 0)
                    {
                        csEntry.AttributeChanges.Add(AttributeChange.CreateAttributeAdd(attributeName, values.ToArray()));
                    }

                    continue;
                }

                csEntry.AttributeChanges.Add(AttributeChange.CreateAttributeAdd(attributeName, attributeValue));
            }

            if (!anchorAttributeIsPresent)
            {
                throw new AnchorAttributeNotPresentException($"IDM object {this.TechName} does not contain Anchor Attribute {anchorAttributeName}");
            }

            return csEntry;
        }

        public void MapFromCSEntry(CSEntryChange csEntry, string idmObjectTypeId)
        {
            Logger.Info($"Mapping CSEntry {csEntry.DN} to IDM object of type {csEntry.ObjectType}");

            if (!int.TryParse(idmObjectTypeId, out var idmObjectTypeIdInt))
            {
                throw new ArgumentException($"Id \"{idmObjectTypeId}\" is not valid IDMObjectType id.");
            }

            // 1. Prepare IDM object
            IDMObjectTypeId = idmObjectTypeIdInt;
            IsArchived = csEntry.ObjectModificationType == ObjectModificationType.Delete;
            var idmObjectIdAttribute = csEntry.AnchorAttributes.FirstOrDefault(aa =>
                aa.Name.Equals("IDMObjectId", StringComparison.OrdinalIgnoreCase));
            if (idmObjectIdAttribute == null)
            {
                Logger.Error("CSEntry is missing value of anchor attribute IDMObjectId");
                throw new InvalidOperationException("CSEntry is missing required anchor attribute IDMObjectId");
            }
            IDMObjectId = idmObjectIdAttribute.Value.ToString();
            var properties = GetProperties().ToDictionary(p => p.Name, p => p,  StringComparer.OrdinalIgnoreCase);

            // 2. Map changes from CSEntry
            foreach (var attributeChange in csEntry.AttributeChanges)
            {
                var attributeName = attributeChange.Name;
                if (attributeName.Equals("adDN"))
                {
                    attributeName = "dn";
                }

                var attributeChangeHasValues = attributeChange.ValueChanges != null && attributeChange.ValueChanges.Count > 0;

                // Diagnostics: shows exactly what the sync engine sent (modification type + every value change).
                Logger.Info($"CSEntry attribute change: {attributeName}, ModificationType: {attributeChange.ModificationType}, " +
                            $"IsMultiValued: {attributeChange.IsMultiValued}, ValueChanges: {(attributeChangeHasValues ? attributeChange.ValueChanges.Count : 0)}");
                if (attributeChangeHasValues && attributeChange.IsMultiValued)
                {
                    foreach (var valueChange in attributeChange.ValueChanges)
                    {
                        Logger.Info($"  {attributeName} value change: {valueChange.ModificationType} {valueChange.Value}");
                    }
                }

                if (!attributeChangeHasValues)
                {
                    Logger.Error($"Attribute change of CSEntry {attributeName} has no value changes");
                }

                // Map special IDM object properties
                switch (attributeName)
                {
                    case "IsArchived":
                        Logger.Info($"Value of IsArchived based on CSEntry modification type: {IsArchived}");
                        continue;
                    case "AppRole":
                        if (!attributeChangeHasValues)
                        {
                            continue;
                        }
                        var stringList = new List<string>();
                        foreach (var appRole in attributeChange.ValueChanges)
                        {
                            if (appRole.ModificationType != ValueModificationType.Delete && !stringList.Contains(appRole.Value.ToString()))
                            {
                                stringList.Add(appRole.Value.ToString());
                            }
                        }
                        var appRolePropertyValue = string.Join(",", stringList);
                        AppRole = appRolePropertyValue;
                        continue;
                    case "member":
                        if (!attributeChangeHasValues)
                        {
                            continue;
                        }
                        var members = new List<string>();
                        foreach (var member in attributeChange.ValueChanges)
                        {
                            if (member.ModificationType != ValueModificationType.Delete && !members.Contains(member.Value.ToString()))
                            {
                                members.Add(member.Value.ToString());
                            }
                        }
                        var memberPropertyValue = string.Join(",", members);
                        Member = memberPropertyValue;
                        continue;
                    case "ManualMemberAndParentBr":
                        if (!attributeChangeHasValues)
                        {
                            continue;
                        }
                        var refs = new List<string>();
                        foreach (var refValue in attributeChange.ValueChanges)
                        {
                            if (refValue.ModificationType != ValueModificationType.Delete && !refs.Contains(refValue.Value.ToString()))
                            {
                                refs.Add(refValue.Value.ToString());
                            }
                        }
                        var propertyValue = string.Join(",", refs);
                        ManualMemberAndParentBr = propertyValue;
                        continue;
                }

                // Map multivalued attribute
                if (attributeChange.IsMultiValued && attributeChangeHasValues)
                {
                    var valueList = new List<string>();
                    foreach (var value in attributeChange.ValueChanges)
                    {
                        if (value.ModificationType != ValueModificationType.Delete && !valueList.Contains(value.Value.ToString()))
                        {
                            valueList.Add(value.Value.ToString());
                        }
                    }
                    var attributeValue = string.Join(";", valueList);
                    this.Values[attributeName] = attributeValue;
                    continue;
                }

                // Map attribute based on modification type
                object csEntryAttributeValue = null;
                switch (attributeChange.ModificationType)
                {
                    case AttributeModificationType.Delete:
                        csEntryAttributeValue = "";
                        break;
                    case AttributeModificationType.Update:
                        if (attributeChangeHasValues)
                        {
                            csEntryAttributeValue = attributeChange.ValueChanges.First(x => x.ModificationType == ValueModificationType.Add)?.Value.ToString();
                        }
                        break;
                    default:
                        if (attributeChangeHasValues)
                        {
                            csEntryAttributeValue = attributeChange.ValueChanges[0]?.Value;
                        }
                        break;
                }
                if (csEntryAttributeValue == null)
                {
                    Logger.Error($"Could not map value of attribute {attributeName} from CSEntry");
                    continue;
                }

                // Set value of IDM object property
                if (properties.TryGetValue(attributeName, out var property))
                {
                    var propertyType = Nullable.GetUnderlyingType(property.PropertyType) ?? property.PropertyType;
                    var convertedPropertyValue = Convert.ChangeType(csEntryAttributeValue, propertyType);
                    property.SetValue(this, convertedPropertyValue);
                }

                // Set value of IDM object attribute
                if (!Attributes.Any(att => att.Name.Equals(attributeName, StringComparison.OrdinalIgnoreCase)))
                {
                    Logger.Error($"Configuration of IDM object with IDMObjectType {idmObjectTypeId} does not contain attribute {attributeName} of CSEntry");
                    continue;
                }
                var convertedAttributeValue = ConvertToAttributeValue(csEntryAttributeValue, attributeName);
                this.Values[attributeName] = convertedAttributeValue;
            }
        }
        
        private object ConvertToAttributeValue(object value, string attributeName)
        {
            if (value == null)
            {
                return null;
            }

            var attribute = Attributes.FirstOrDefault(att => att.Name.Equals(attributeName, StringComparison.OrdinalIgnoreCase));
            if (attribute == null)
            {
                return null;
            }

            switch (attribute.AttributeTypeEnum)
            {
                case AttributeTypeEnum.String:
                    return value.ToString();
                case AttributeTypeEnum.Integer:
                    if (int.TryParse(value.ToString(), out var intValue))
                    {
                        return intValue;
                    }
                    return null;
                case AttributeTypeEnum.Boolean:
                    if (bool.TryParse(value.ToString(), out var boolValue))
                    {
                        return boolValue;
                    }
                    return null;
                case AttributeTypeEnum.Reference:
                    return value.ToString(); // GUID
                default:
                    return null;
            }
        }

        private static List<string> SplitToDistinctGuids(string value)
        {
            char[] possibleSeparators = { ';', ',' };
            return value.Split(possibleSeparators, StringSplitOptions.RemoveEmptyEntries)
                .Select(v => v.Trim())
                .Where(v => v.Length > 0)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
        }
    }
}