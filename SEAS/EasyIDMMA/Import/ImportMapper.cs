using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using EasyIDMMA.Model.Connector;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using NLog;
using Attribute = EasyIDMMA.Model.Connector.Attribute;

namespace EasyIDMMA.Import
{
    public static class ImportMapper
    {
        private static readonly Logger Logger = LogManager.GetCurrentClassLogger();
        private static readonly Logger ImportLogger = LogManager.GetLogger("Import");
        
        public static List<IDMObject> MapImportDataToObjects(Stream jsonStream, Attribute[] attributes)
        {
            Logger.Info("Mapping imported data to IDM objects:");
            var idmObjects = new List<IDMObject>();
            try
            {
                using (var streamReader = new StreamReader(jsonStream))
                using (var jsonReader = new JsonTextReader(streamReader)
                {
                    DateParseHandling = DateParseHandling.None // prevents auto-conversion for DateTime strings
                })
                {
                    var root = JObject.Load(jsonReader);

                    if (root.TryGetValue("Objects", out var jsonObjects))
                    {
                        foreach (var jsonObject in jsonObjects.Children<JObject>())
                        {
                            var idmObject = new IDMObject(attributes);

                            // Iterate through the jsonObject properties
                            foreach (var jsonProperty in jsonObject.Properties())
                            {
                                if (jsonProperty.Name == "Attributes")
                                {
                                    continue;
                                }

                                // Set value of idmObject property
                                var idmProperty = idmObject.GetType().GetProperty(jsonProperty.Name);
                                if (idmProperty != null && jsonProperty.Value.Type != JTokenType.Null)
                                {
                                    var value = jsonProperty.Value.ToObject(idmProperty.PropertyType);
                                    idmProperty.SetValue(idmObject, value);
                                }
                            }

                            // Iterate through the json "Attributes" property
                            if (jsonObject.TryGetValue("Attributes", out var jsonAttributes))
                            {
                                foreach (var jsonAttribute in jsonAttributes.Children<JProperty>())
                                {
                                    // Set value of idmObject attribute
                                    var idmObjectAttribute = idmObject.Attributes.FirstOrDefault(att => att.Name == jsonAttribute.Name);
                                    if (idmObjectAttribute == null)
                                    {
                                        continue;
                                    }

                                    if (jsonAttribute.Value.Type != JTokenType.Null)
                                    {
                                        var convertedValue = ConvertJsonValue(jsonAttribute.Value, idmObjectAttribute.AttributeTypeEnum);
                                        if (convertedValue != null)
                                        {
                                            idmObject.Values.Add(jsonAttribute.Name, convertedValue);
                                        }
                                    }
                                    else
                                    {
                                        // If attribute value in json is null, use value of already set property with the same name
                                        var idmObjectProperty = idmObject.GetType().GetProperty(
                                            jsonAttribute.Name,
                                            BindingFlags.IgnoreCase | BindingFlags.Public | BindingFlags.Instance);
                                        if (idmObjectProperty != null)
                                        {
                                            var idmObjectPropertyValue = idmObjectProperty.GetValue(idmObject);
                                            var convertedPropertyValue = ConvertObjectValue(idmObjectPropertyValue, idmObjectAttribute.AttributeTypeEnum);
                                            if (convertedPropertyValue != null)
                                            {
                                                idmObject.Values.Add(jsonAttribute.Name, convertedPropertyValue);
                                            }
                                        }
                                    }
                                }
                            }

                            ImportLogger.Info($"Imported data of IDM object {idmObject.IDMObjectId}:\n{jsonObject}");
                            Logger.Info($"Mapped imported data to IDM object {idmObject.IDMObjectId} of type {idmObject.IDMObjectTypeId}");

                            idmObjects.Add(idmObject);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Logger.Error(ex, "Error parsing imported JSON stream");
                throw;
            }

            return idmObjects;
        }
        
        private static object ConvertJsonValue(JToken jsonValue, AttributeTypeEnum attributeType)
        {
            if (jsonValue == null || jsonValue.Type == JTokenType.Null)
            {
                return null;
            }
            
            switch (attributeType)
            {
                case AttributeTypeEnum.String:
                    return jsonValue.ToString();
                case AttributeTypeEnum.Integer:
                    if (jsonValue.Type == JTokenType.Integer)
                    {
                        return jsonValue.ToObject<int>();
                    }
                    var stringValue = jsonValue.ToString().Trim();
                    if (string.IsNullOrEmpty(stringValue))
                    {
                        return null;
                    }
                    if (int.TryParse(stringValue, out var intResult))
                    {
                        return intResult;
                    }
                    throw new InvalidCastException($"Invalid integer value: '{stringValue}'");
                case AttributeTypeEnum.Boolean:
                    if (jsonValue.Type == JTokenType.Boolean)
                    {
                        return jsonValue.ToObject<bool>();
                    }
                    var str = jsonValue.ToString().Trim();
                    if (string.IsNullOrEmpty(str))
                    {
                        return null;
                    }
                    if (bool.TryParse(str, out var result))
                    {
                        return result;
                    }
                    throw new InvalidCastException($"Invalid boolean value: '{str}'");
                case AttributeTypeEnum.Reference:
                    return jsonValue.ToString(); // GUID
                default:
                    throw new ArgumentOutOfRangeException(nameof(attributeType), attributeType, "Error while converting attribute value. Attribute type not supported");
            }
        }

        private static object ConvertObjectValue(object value, AttributeTypeEnum attributeType)
        {
            if (value == null)
            {
                return null;
            }
            
            switch (attributeType)
            {
                case AttributeTypeEnum.String:
                    return value.ToString();
                case AttributeTypeEnum.Integer:
                    if (value is int i)
                    {
                        return i;
                    }
                    var stringValue = value.ToString().Trim();
                    if (string.IsNullOrEmpty(stringValue))
                    {
                        return null;
                    }
                    if (int.TryParse(stringValue, out var intResult))
                    {
                        return intResult;
                    }
                    throw new InvalidCastException($"Invalid integer value: '{stringValue}'");
                case AttributeTypeEnum.Boolean:
                    if (value is bool b)
                    {
                        return b;
                    }
                    var str = value.ToString().Trim();
                    if (string.IsNullOrEmpty(str))
                    {
                        return null;
                    }
                    if (bool.TryParse(str, out var result))
                    {
                        return result;
                    }
                    throw new InvalidCastException($"Invalid boolean value: '{str}'");
                case AttributeTypeEnum.Reference:
                    return value.ToString(); // GUID
                default:
                    throw new ArgumentOutOfRangeException(nameof(attributeType), attributeType, "Error while converting attribute value. Attribute type not supported");
            }
        }
    }
}