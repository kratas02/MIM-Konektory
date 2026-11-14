using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Configuration;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Reflection;
using System.Security;
using EasyIDMMA.AttributeMapping;
using EasyIDMMA.Enums;
using EasyIDMMA.Export;
using EasyIDMMA.Extensions;
using EasyIDMMA.Import;
using EasyIDMMA.Model.Connector;
using EasyIDMMA.Utility;
using Microsoft.MetadirectoryServices;
using NLog;

namespace EasyIDMMA
{
    public class EasyIDMMAExtension :
      IMAExtensible2CallExport,
      IMAExtensible2CallImport,
      IMAExtensible2GetSchema,
      IMAExtensible2GetCapabilities,
      IMAExtensible2GetParameters,
      IMAExtensible2Password,
      IMASynchronization

    {
        /// <summary>
        /// Default pagination values for Management Agent
        /// </summary>
        private int m_importDefaultPageSize = 12;
        private int m_importMaxPageSize = 50;
        private int m_exportDefaultPageSize = 150;
        private int m_exportMaxPageSize = 1000;
        
        /// <summary>
        /// Timestamp of the last import operation. It is used for Delta import
        /// </summary>
        private string _deltaTimeStamp = "";
        
        /// <summary>
        /// Instance of Logger to log into log file
        /// </summary>
        Logger Logger = LogManager.Setup().LoadConfigurationFromFile().GetCurrentClassLogger();
        
        /// <summary>
        /// Collection of stored IDM objects
        /// Key is object type, value is list of IDM objects
        /// </summary>
        public Dictionary<string, List<IDMObject>> IdmObjects = new Dictionary<string, List<IDMObject>>(StringComparer.OrdinalIgnoreCase);
        
        /// <summary>
        /// Control set used to map only distinct IDM objects to CSEntry
        /// </summary>
        public HashSet<string> UniqueImportedObjects = new HashSet<string>();
        
        /// <summary>
        /// Stores imported object types
        /// </summary>
        public List<string> ObjectTypesImported = new List<string>();
        
        /// <summary>
        /// Pagination parameters for importing data
        /// </summary>
        public ImportEntriesPagination ImportEntriesPagination = new ImportEntriesPagination();
        
        public Dictionary<string, List<ImportAttributeFlow>> ImportObjectTypeFlow =
            new Dictionary<string, List<ImportAttributeFlow>>(); // key: MV object type, value: attributes flows
        
        /// <summary>
        /// Representation of connector XML configuration
        /// </summary>
        private ConnectorConfiguration _connectorConfiguration;

        /// <summary>
        /// Dictionary with configuration parameters of Management Agent (MA) from GUI.
        /// </summary>
        private readonly Dictionary<string, string> _maConfigurationParameters = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        /// <summary>
        /// Boolean to tell if current import is Delta import.
        /// </summary>
        private bool _isCurrentImportDelta = false;

        /// <summary>
        /// Attributes selected in the Management Agent (Select Attributes) for the imported object types.
        /// Key is object type name, value is set of selected attribute names.
        /// </summary>
        private readonly Dictionary<string, HashSet<string>> _selectedAttributes =
            new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase);

        public EasyIDMMAExtension()
        {
            Logger.Info("");
            Logger.Info("************************** Initialization operation started **************************");

            // Log version info
            var assembly = Assembly.GetExecutingAssembly();
            var version = assembly.GetName().Version;
            var informationalVersion = assembly
                .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?
                .InformationalVersion ?? version.ToString();
            Logger.Info($"Library Version: {version}");
            Logger.Info($"Informational Version: {informationalVersion}");

            // Log capabilities info
            var capabilitiesProperties = Capabilities.GetType().GetProperties();
            foreach (var capability in capabilitiesProperties)
            {
                var capabilityName = capability.Name;
                var capabilityValue = capability.GetValue(Capabilities);
                Logger.Info($"MA capability: {capabilityName}, value: {capabilityValue}");
            }

            Logger.Info("");
            Logger.Info("Initialization was successful.");
        }

        // Mainly for testing purposes
        public EasyIDMMAExtension(Dictionary<string, string> maConfigurationParameters = null)
        {
            this._maConfigurationParameters =  maConfigurationParameters;
            LoadConnectorConfiguration();
        }

        /// <summary>
        /// Sets management agent capabilities.
        /// </summary>
        public MACapabilities Capabilities
        {
            get
            {
                var capabilities = new MACapabilities();
                
                capabilities.ConcurrentOperation = true;
                capabilities.DeltaImport = true;
                capabilities.NoReferenceValuesInFirstExport = false;
                capabilities.IsDNAsAnchor = false;
                capabilities.ObjectConfirmation = MAObjectConfirmation.NoAddAndDeleteConfirmation;
                capabilities.ObjectRename = false;
                capabilities.SupportExport = true;
                capabilities.SupportHierarchy = false;
                capabilities.SupportImport = true;
                capabilities.SupportPartitions = false;
                capabilities.SupportPassword = false;
                capabilities.FullExport = true;
                // AttributeReplace: for a changed multi-valued attribute the engine sends the full list of values
                // (the portal expects the whole list, not value-level add/delete changes).
                capabilities.ExportType = MAExportType.AttributeReplace;
                capabilities.DeleteAddAsReplace = true;
                capabilities.DistinguishedNameStyle = MADistinguishedNameStyle.None;
                capabilities.Normalizations = MANormalizations.None;
                capabilities.ExportPasswordInFirstPass = false;
                
                return capabilities;
            }
        }

        /// <summary>
        /// Gets an array of values indicating the configuration parameter definitions supported by the management agent.
        /// </summary>
        public IList<ConfigParameterDefinition> GetConfigParameters(
          KeyedCollection<string, ConfigParameter> configParameters,
          ConfigParameterPage page)
        {
            Logger.Info("************************** GetConfigParameters operation started **************************");

            List<ConfigParameterDefinition> configParameters1 = new List<ConfigParameterDefinition>();
            if (page == 0)
            {
                configParameters1.Add(ConfigParameterDefinition.CreateStringParameter("MA instance", ""));
                configParameters1.Add(ConfigParameterDefinition.CreateStringParameter("EasyIDM Portal base URL", ""));
                configParameters1.Add(ConfigParameterDefinition.CreateStringParameter("Full Import URL", ""));
                configParameters1.Add(ConfigParameterDefinition.CreateStringParameter("Delta Import URL", ""));
                configParameters1.Add(ConfigParameterDefinition.CreateStringParameter("Full Export URL", ""));
                configParameters1.Add(ConfigParameterDefinition.CreateStringParameter("Full Async Export URL", ""));
                configParameters1.Add(ConfigParameterDefinition.CreateEncryptedStringParameter("EasyIDM Portal Client Secret", ""));
                configParameters1.Add(ConfigParameterDefinition.CreateStringParameter("Connector XML configuration file path", ""));
            }
            return configParameters1;
        }

        /// <summary>
        /// Gets the result of parameter validation by the extension.
        /// </summary>
        public ParameterValidationResult ValidateConfigParameters(
          KeyedCollection<string, ConfigParameter> configParameters,
          ConfigParameterPage page)
        {
            Logger.Info("************************** ValidateConfigParameters operation started **************************");

            return new ParameterValidationResult();
        }
        
        /// <summary>
        /// Reads configuration parameters from GUI and stores them to accessible class property.
        /// </summary>
        private void LoadMaConfigurationParameters(KeyedCollection<string, ConfigParameter> configParameters)
        {
            if (configParameters == null)
            {
                return;
            }
            
            foreach (var configParameter in configParameters)
            {
                var value = configParameter.IsEncrypted 
                    ? new NetworkCredential(null, configParameter.SecureValue).Password 
                    : configParameter.Value;

                _maConfigurationParameters[configParameter.Name] = value;
            }
        }

        /// <summary>
        /// Returns base URL of EasyIDM Portal from configuration parameters.
        /// </summary>
        private string GetBaseUrl()
        {
            const string parameter = "EasyIDM Portal base URL";

            if (!_maConfigurationParameters.TryGetValue(parameter, out var baseUrl))
            {
                Logger.Error("Base URL configuration is missing");
                throw new ConfigurationErrorsException("Base URL configuration is missing");
            }

            return baseUrl;
        }
        
        /// <summary>
        /// Returns Client Secret of EasyIDM Portal from configuration parameters.
        /// </summary>
        private string GetClientSecret()
        {
            const string parameter = "EasyIDM Portal Client Secret";

            if (!_maConfigurationParameters.TryGetValue(parameter, out var clientSecret))
            {
                Logger.Error("ClientSecret configuration is missing");
                throw new ConfigurationErrorsException("ClientSecret configuration is missing");
            }

            return clientSecret;
        }

        /// <summary>
        /// Returns timestamp for Delta import.
        /// If import is not Delta or DeltaFallbackHours is not set in connector configuration, timestamp of the last import is returned unchanged.
        /// Otherwise timestamp of the last import is used only if it is not older than DeltaFallbackHours.
        /// </summary>
        public string GetDeltaTimeStamp(string lastImportTimeStamp, bool isDeltaImport)
        {
            var fallbackHours = _connectorConfiguration.GetDeltaFallbackHours();
            if (!isDeltaImport || fallbackHours == null)
            {
                return lastImportTimeStamp;
            }

            var fallbackTime = DateTime.Now.AddHours(-fallbackHours.Value);

            if (!string.IsNullOrEmpty(lastImportTimeStamp)
                && DateTime.TryParseExact(lastImportTimeStamp.Replace("from=", ""), "yyyy-MM-dd HH:mm", CultureInfo.CurrentCulture, DateTimeStyles.None, out var lastImportTime)
                && lastImportTime >= fallbackTime)
            {
                return lastImportTimeStamp;
            }

            var fallbackTimeStamp = $"from={fallbackTime:yyyy-MM-dd HH:mm}";
            Logger.Info($"Timestamp of the last import is missing or older than {fallbackHours} hours. Using timestamp: {fallbackTimeStamp}");
            return fallbackTimeStamp;
        }

        /// <summary>
        ///  Returns URL based on operation direction, operation mode and IDM object type.
        /// </summary>
        public string GetUrl(OperationMode operationMode, OperationDirection operationDirection, string objectTypeId, string deltaTimeStamp = null)
        {
            var baseUrl = GetBaseUrl();

            var asyncURl = string.Empty;
            if (operationDirection == OperationDirection.Export && _connectorConfiguration.IsExportAsync())
            {
                asyncURl = " Async";
            }

            var urlParameter = $"{operationMode}{asyncURl} {operationDirection} URL";
            if (!_maConfigurationParameters.TryGetValue(urlParameter, out var url))
            {
                Logger.Error($"URL configuration for {operationMode}{asyncURl} {operationDirection} is missing");
                throw new ConfigurationErrorsException($"URL configuration for {operationMode}{asyncURl} {operationDirection} is missing");
            }
            
            var isDelta = operationMode == OperationMode.Delta && !string.IsNullOrEmpty(deltaTimeStamp);
            var requestUrl = isDelta
                ? $"{baseUrl}{url}/{objectTypeId}?{deltaTimeStamp}"
                : $"{baseUrl}{url}/{objectTypeId}";

            return requestUrl;
        }

        /// <summary>
        /// Defines objects and attributes contained by the data source through .xml configuration file.
        /// </summary>
        public Schema GetSchema(
          KeyedCollection<string, ConfigParameter> configParameters)
        {
            Logger.Info("************************** GetSchema operation started **************************");
            SetupFromConfiguration(configParameters);

            if (configParameters != null)
            {
                var maInstance = configParameters["MA instance"].Value;
                Logger.Info($"Filling schema of MA {maInstance} with attributes from configuration file");
            }

            var schema = Schema.Create();
            try
            {
                FillSchemaByConfigFile(schema);
            }
            catch (Exception e)
            {
                Logger.Error(e, "Error during creating connector schema from configuration file");
                throw;
            }
            return schema;
        }
        
        /// <summary>
        /// Loads Connector configuration based on Connector XML configuration file path.
        /// </summary>
        private void LoadConnectorConfiguration()
        {
            const string parameter = "Connector XML configuration file path";

            if (!_maConfigurationParameters.TryGetValue(parameter, out var schemaConfigurationPath))
            {
                Logger.Error("Connector XML configuration file path configuration is missing");
                throw new ConfigurationErrorsException("Connector XML configuration file path configuration is missing");
            }
            
            this._connectorConfiguration = ConnectorUtility.LoadConnector(schemaConfigurationPath);
            Logger.Info($"Loaded connector schema configuration from path {schemaConfigurationPath}");
        }

        /// <summary>
        /// Setup for internal components based on their configuration parameters.
        /// </summary>
        private void SetupFromConfiguration(KeyedCollection<string, ConfigParameter> configParameters)
        {
            LoadMaConfigurationParameters(configParameters);
            LoadConnectorConfiguration();
        }

        private void FillSchemaByConfigFile(Schema schema)
        {
            var connectorSchema = this._connectorConfiguration.ConnectorSchema;

            var anchorAttributeName = connectorSchema.AnchorAttribute;
            if (string.IsNullOrEmpty(anchorAttributeName))
            {
                throw new ConfigurationErrorsException("No Anchor Attribute Name defined in connector schema.");
            }

            foreach (var objectType in connectorSchema.ObjectTypes)
            {
                Logger.Info($"Updating schema with object type: {objectType.TypeName}");
                var schemaType = SchemaType.Create(objectType.TypeName, false);

                // Create Anchor Attribute
                Logger.Info($"Anchor Attribute {anchorAttributeName}");
                schemaType.Attributes.Add(SchemaAttribute.CreateAnchorAttribute(anchorAttributeName, AttributeType.String));

                var properties = new IDMObject().GetProperties();
                foreach (var property in properties)
                {
                    switch (property.Name)
                    {
                        case "AppRole":
                            if (objectType.TypeName.Is(IDMObjectType.BusinessRole) && !schemaType.Attributes.Contains("AppRole"))
                            {
                                Logger.Info("Implicit attribute AppRole of type Reference");
                                schemaType.Attributes.Add(SchemaAttribute.CreateMultiValuedAttribute("AppRole", AttributeType.Reference));
                            }
                            break;
                        case "AutoCastRule":
                            if (objectType.TypeName.Is(IDMObjectType.BusinessRole) && !schemaType.Attributes.Contains("AutoCastRule"))
                            {
                                Logger.Info("Implicit attribute AutoCastRule of type string");
                                schemaType.Attributes.Add(SchemaAttribute.CreateSingleValuedAttribute("AutoCastRule", AttributeType.String));
                            }
                            break;
                        case "ManualMemberAndParentBr":
                            if (objectType.TypeName.Is(IDMObjectType.ApplicationRole) && !schemaType.Attributes.Contains("ManualMemberAndParentBr"))
                            {
                                Logger.Info("Implicit multivalued attribute ManualMemberAndParentBr of type Reference");
                                schemaType.Attributes.Add(SchemaAttribute.CreateMultiValuedAttribute("ManualMemberAndParentBr", AttributeType.Reference));
                            }
                            break;
                        case "IsArchived":
                            if (!schemaType.Attributes.Contains("IsArchived"))
                            {
                                Logger.Info("Implicit attribute IsArchived of type Boolean");
                                schemaType.Attributes.Add(SchemaAttribute.CreateSingleValuedAttribute("IsArchived", AttributeType.Boolean));
                            }
                            break;
                    }
                }

                var anchorAttributeIsPresentInSchema = false;
                foreach (var attribute in objectType.Attributes)
                {
                    var attributeName = attribute.Name;
                    if (attributeName.Equals("dn"))
                    {
                        attributeName = "adDN";
                    }
                    
                    // Check that anchor attribute is present in schema
                    if (attributeName.Equals(anchorAttributeName, StringComparison.OrdinalIgnoreCase))
                    {
                        anchorAttributeIsPresentInSchema = true;
                        // Anchor Attribute is already created
                        continue;
                    }
                    
                    // Check for duplicates
                    if (schemaType.Attributes.Contains(attributeName))
                    {
                        continue;
                    }
                    
                    var attributeType = MapAttributeTypeToMIMAttributeType(attribute);
                    
                    if (objectType.TypeName.Is(IDMObjectType.Identity))
                    {
                        if (attributeName.Equals("managerObjectId"))
                        {
                            attributeType = AttributeType.Reference;
                        }
                    }

                    Logger.Info($"Attribute {attributeName} of type {attributeType}");
                    if (attribute.IsMultiValued)
                    {
                        schemaType.Attributes.Add(
                            SchemaAttribute.CreateMultiValuedAttribute(attributeName, attributeType));
                    }
                    else
                    {
                        schemaType.Attributes.Add(
                            SchemaAttribute.CreateSingleValuedAttribute(attributeName, attributeType));
                    }
                }

                if (!anchorAttributeIsPresentInSchema)
                {
                    throw new ConfigurationErrorsException($"Anchor Attribute was not found in Attributes of object type schema {objectType.TypeName}");
                }

                if (objectType.TypeName.Is(IDMObjectType.ApplicationRole))
                {
                    var resourceProperties = new List<string> { "Name", "TechName", "OwnerIdentityId" };
                    foreach (var resourceProperty in resourceProperties)
                    {
                        var attributeType = resourceProperty.Equals("OwnerIdentityId")
                            ? AttributeType.Reference
                            : AttributeType.String;
                        if (!schemaType.Attributes.Contains("res_" + resourceProperty))
                        {
                            Logger.Info($"Attribute {"res_" + resourceProperty} of type {attributeType}");
                            schemaType.Attributes.Add(
                                SchemaAttribute.CreateSingleValuedAttribute("res_" + resourceProperty, attributeType));
                        }
                    }
                }

                // Connector Space attribute member stores references of Identities that have cast with the role
                if (!schemaType.Attributes.Contains("member") &&
                    (objectType.TypeName.Is(IDMObjectType.ApplicationRole) ||
                     objectType.TypeName.Is(IDMObjectType.BusinessRole)))
                {
                    Logger.Info("Attribute member of type Reference");
                    schemaType.Attributes.Add(
                        SchemaAttribute.CreateMultiValuedAttribute("member", AttributeType.Reference));
                }

                schema.Types.Add(schemaType);
            }
        }
        
        private AttributeType MapAttributeTypeToMIMAttributeType(EasyIDMMA.Model.Connector.Attribute attribute)
        {
            switch (attribute.AttributeTypeEnum)
            {
                case AttributeTypeEnum.String:
                    return AttributeType.String;
                case AttributeTypeEnum.Integer:
                    return AttributeType.Integer;
                case AttributeTypeEnum.Boolean:
                    return AttributeType.Boolean;
                case AttributeTypeEnum.Reference:
                    return AttributeType.Reference;
                default:
                    throw new ArgumentOutOfRangeException(attribute.AttributeTypeEnum.ToString(), "Attribute type not supported");
            }
        }
        
        private AttributeType MapPropertyTypeToMIMAttributeType(PropertyInfo property)
        {
            if (property.PropertyType.FullName.Contains("System.String"))
            {
                return AttributeType.String;
            }
            if (property.PropertyType.FullName.Contains("System.DateTime"))
            {
                return AttributeType.String;
            }
            if (property.PropertyType.FullName.Contains("System.Int32"))
            {
                return AttributeType.Integer;
            }
            if (property.PropertyType.FullName.Contains("System.Boolean"))
            {
                return AttributeType.Boolean;
            }
            // TODO - How to handle reference (string) type property?

            throw new ArgumentOutOfRangeException(property.PropertyType.FullName, "Property type not supported");
        }

        /// <summary>
        /// Imports IDM objects from the portal.
        /// Is called once at the beginning of import.
        /// </summary>
        public OpenImportConnectionResults OpenImportConnection(
          KeyedCollection<string, ConfigParameter> configParameters,
          Schema types,
          OpenImportConnectionRunStep importRunStep)
        {
            Logger.Info($"************************** {importRunStep.ImportType.ToString()} OpenImportConnection operation started **************************");
            SetupFromConfiguration(configParameters);

            this._deltaTimeStamp = $"from={DateTime.Now:yyyy-MM-dd HH:mm}";
            Logger.Info($"Timestamp of the current import: {this._deltaTimeStamp}");
            
            this.ImportEntriesPagination.PageSize = importRunStep.PageSize;

            var importDataService = new ImportDataService();

            var baseUrl = GetBaseUrl();
            var clientSecret = GetClientSecret();

            // If Delta import
            var isDeltaImport = importRunStep.ImportType == OperationType.Delta;
            Logger.Info($"Last import: {importRunStep.CustomData}");
            var deltaTimeStamp = GetDeltaTimeStamp(importRunStep.CustomData, isDeltaImport);
            var mode = isDeltaImport && !string.IsNullOrEmpty(deltaTimeStamp)
                ? OperationMode.Delta
                : OperationMode.Full;
            this._isCurrentImportDelta = isDeltaImport;
            this._selectedAttributes.Clear();
            
            foreach (var type in types.Types)
            {
                var objectTypeName = type.Name;
                var objectTypeId = _connectorConfiguration.MapIDMObjectTypeNameToId(objectTypeName);
                Logger.Info($"Importing IDM objects of type {objectTypeName}");

                this.ObjectTypesImported.Add(objectTypeName);

                // Remember attributes selected in the Management Agent, so that only these are returned to MIM
                var selectedAttributeNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                foreach (var schemaAttribute in type.Attributes)
                {
                    selectedAttributeNames.Add(schemaAttribute.Name);
                }
                this._selectedAttributes[objectTypeName] = selectedAttributeNames;
                Logger.Info($"Selected attributes of type {objectTypeName} ({selectedAttributeNames.Count}): {string.Join(", ", selectedAttributeNames.OrderBy(n => n, StringComparer.OrdinalIgnoreCase))}");

                var importUrl = GetUrl(mode, OperationDirection.Import, objectTypeId, deltaTimeStamp);
                Logger.Info($"Import URL: {importUrl}");
                
                var importParams = new ImportParameters
                {
                    ConnectorSchema = this._connectorConfiguration.ConnectorSchema,
                    ClientSecret = clientSecret,
                    BaseUrl = baseUrl,
                    ImportUrl = importUrl,
                    ObjectTypeName = objectTypeName,
                    PageSize = importRunStep.PageSize
                };

                try
                {
                    var idmObjects = importDataService.Import(importParams);
                    Logger.Info($"Loaded {idmObjects.Count} IDM objects of type {objectTypeName}");
                
                    if (this.IdmObjects.TryGetValue(objectTypeName, out var objects))
                    {
                        objects.AddRange(idmObjects);
                        this.IdmObjects[objectTypeName] = objects.Distinct().ToList();
                    }
                    else
                    {
                        this.IdmObjects[objectTypeName] = idmObjects;
                    }
                }
                catch (Exception e)
                {
                    Logger.Error(e, $"Error during importing objects of type {objectTypeName}");
                    this._deltaTimeStamp = ""; // timestamp of failed import must not be saved
                    throw;
                }
            }
            
            this.ImportEntriesPagination.Reset();
            
            return new OpenImportConnectionResults();
        }

        /// <summary>
        /// Reads imported data from the portal and map the information to the object model used by the Synchronization Service.
        /// Assumes that OpenImportConnection has been called successfully.
        /// </summary>
        public GetImportEntriesResults GetImportEntries(
          GetImportEntriesRunStep importRunStep)
        {
            Logger.Info($"************************** GetImportEntries operation started **************************");
            
            var importEntries = new GetImportEntriesResults();
            var entries = new List<CSEntryChange>();
            importEntries.MoreToImport = true;

            var processedObjectType = this.ObjectTypesImported[this.ImportEntriesPagination.LastObjectTypeIndex];
            Logger.Info($"Processing object type: {processedObjectType}");

            var anchorAttributeName = this._connectorConfiguration.ConnectorSchema.AnchorAttribute;

            var start = this.ImportEntriesPagination.PagesCount * this.ImportEntriesPagination.PageSize;
            var end = this.ImportEntriesPagination.PagesCount * this.ImportEntriesPagination.PageSize + this.ImportEntriesPagination.PageSize - 1;
            var objectsCount = this.IdmObjects[processedObjectType].Count;
            if (end > objectsCount - 1)
            {
                end = objectsCount - 1;
            }

            for (var objectIndex = start; objectIndex <= end; objectIndex++)
            {
                var idmObject = this.IdmObjects[processedObjectType][objectIndex];
                Logger.Info($"Processing IDM object: {idmObject.Name}");

                if (this.UniqueImportedObjects.Add(idmObject.IDMObjectId))
                {
                    try
                    {
                        this._selectedAttributes.TryGetValue(processedObjectType, out var selectedAttributes);
                        var csEntryChange = idmObject.MapToCSEntry(processedObjectType, anchorAttributeName, this._isCurrentImportDelta, selectedAttributes);
                        if (csEntryChange != null)
                        {
                            entries.Add(csEntryChange);
                        }
                    }
                    catch (Exception e)
                    {
                        Logger.Error(e, "Error during mapping of IDM object to CSEntry");
                        importEntries.MoreToImport = false;
                        return importEntries;
                    }
                }
                else
                {
                    Logger.Info($"IDM object: {idmObject.Name} already mapped");
                }
            }
            Logger.Info($"Processed: {end + 1} {processedObjectType} objects, total: {objectsCount}");

            if (end == objectsCount - 1)
            {
                Logger.Info($"All objects of type {processedObjectType} mapped as CS Entries");

                if (this.ImportEntriesPagination.LastObjectTypeIndex == this.ObjectTypesImported.Count - 1)
                {
                    Logger.Info($"All object types processed");
                    importEntries.MoreToImport = false;
                }
                else
                {
                    this.ImportEntriesPagination.NextObjectType();
                    importEntries.MoreToImport = true;
                }
                importEntries.CSEntries = entries;
                return importEntries;
            }

            this.ImportEntriesPagination.NextPage();

            importEntries.MoreToImport = true;
            importEntries.CSEntries = entries;
            return importEntries;
        }

        /// <summary>
        /// Tells the extensible MA that the session is over and allows customer code to clean up.
        /// Assumes that OpenImportConnection has been called successfully.
        /// </summary>
        public CloseImportConnectionResults CloseImportConnection(
          CloseImportConnectionRunStep importRunStepInfo)
        {
            Logger.Info("************************** CloseImportConnection operation started **************************");
            var results = new CloseImportConnectionResults();

            if (importRunStepInfo.Reason != CloseReason.Normal)
            {
                return results;
            }

            // Save TimeStamp of the last import for future Delta
            if (!string.IsNullOrEmpty(_deltaTimeStamp))
            {
                Logger.Info($"Saving new timestamp of the last import: {_deltaTimeStamp}");
                results.CustomData = _deltaTimeStamp;
            }

            return results;
        }

        public int ImportMaxPageSize => this.m_importMaxPageSize;

        public int ImportDefaultPageSize => this.m_importDefaultPageSize;

        /// <summary>
        /// Reads and prepares data from the configuration for the export run.
        /// </summary>
        public void OpenExportConnection(
          KeyedCollection<string, ConfigParameter> configParameters,
          Schema types,
          OpenExportConnectionRunStep exportRunStep)
        {
            Logger.Info("************************** OpenExportConnection operation started **************************");
            SetupFromConfiguration(configParameters);
        }

        /// <summary>
        /// Exports a batch of entries in the portal.
        /// It can be assumed OpenExportConnection has been called successfully.
        /// </summary>
        public PutExportEntriesResults PutExportEntries(IList<CSEntryChange> csentries)
        {
            var baseUrl = GetBaseUrl();
            var clientSecret = GetClientSecret();

            Logger.Info("************************** PutExportEntries operation started **************************");
            Logger.Info($"Number of CSEntries for export: {csentries.Count}");

            var putExportEntriesResults = new PutExportEntriesResults();
            var asyncExportProgress = new Dictionary<string, CSEntryChange>(); // key = export progress URL, value = CSEntry

            var errorOccured = false;
            foreach (var csEntry in csentries)
            {
                string csEntryId = null;
                if (csEntry.AnchorAttributes.Count > 0)
                {
                    csEntryId = csEntry.AnchorAttributes[0].Value.ToString();
                }
                Logger.Info($"Proccessing CSEntry, DN: {csEntry.DN}, Anchor: {csEntryId ?? "null"}");
                
                // Get IDM object type of CSEntry
                var idmObjectTypeName = csEntry.ObjectType;
                string idmObjectTypeId;
                try
                {
                    idmObjectTypeId = _connectorConfiguration.MapIDMObjectTypeNameToId(idmObjectTypeName);
                }
                catch (Exception e)
                {
                    var errorDescription = "Failed to get IDM object type from CSEntry";
                    var errorChangeResult = CSEntryChangeResult.Create(csEntry.Identifier, csEntry.AttributeChanges, MAExportError.ExportErrorInvalidAttributeValue,"Export error",errorDescription);
                    putExportEntriesResults.CSEntryChangeResults.Add(errorChangeResult);
                    Logger.Error(e, errorDescription);
                    continue;
                }
                Logger.Info($"IDM object type of CSEntry: {idmObjectTypeName}");
                
                // Map CSEntry to IDM object
                var attributes = _connectorConfiguration.GetAttributesByObjectType(idmObjectTypeName);
                var idmObject = new IDMObject(attributes);
                try
                {
                    idmObject.MapFromCSEntry(csEntry, idmObjectTypeId);
                }
                catch (Exception e)
                {
                    const string errorDescription = "Error during mapping of CSEntry to IDM Object";
                    var errorChangeResult = CSEntryChangeResult.Create(csEntry.Identifier, csEntry.AttributeChanges, MAExportError.ExportErrorInvalidAttributeValue,"Export error",errorDescription);
                    putExportEntriesResults.CSEntryChangeResults.Add(errorChangeResult);
                    Logger.Error(e, errorDescription);
                    continue;
                }
                Logger.Info("Mapped CSEntry to IDM object");

                // Begin export of IDM object mapped from CSEntry
                var exportUrl = GetUrl(OperationMode.Full, OperationDirection.Export, idmObjectTypeId);
                if (_connectorConfiguration.IsExportAsync())
                {
                    try
                    {
                        Logger.Info($"Start async export of IDM object {idmObject.IDMObjectId}");
                        var asyncExportParameters = new AsyncExportParameters
                        {
                            ConnectorSchema = this._connectorConfiguration.ConnectorSchema,
                            ClientSecret = clientSecret,
                            BaseUrl = baseUrl,
                            ExportUrl = exportUrl,
                            ExportId = csEntry.Identifier.ToString(),
                            IdmObject = idmObject
                        };
                        var asyncExportResponse = ExportService.ExportToPortalAsync(asyncExportParameters);
                        var asyncExportStatusItem = asyncExportResponse.FirstOrDefault(i => i.Id == csEntry.Identifier.ToString());
                        if (asyncExportStatusItem == null)
                        {
                            var serverErrorDescription = $"No async export staus response received from Portal for IDM object {idmObject.IDMObjectId}";
                            var serverErrorChangeResult = CSEntryChangeResult.Create(csEntry.Identifier, csEntry.AttributeChanges, MAExportError.ExportErrorCustomContinueRun, "eidm-export-error", serverErrorDescription);
                            putExportEntriesResults.CSEntryChangeResults.Add(serverErrorChangeResult);
                            Logger.Error(serverErrorDescription);
                            continue;
                        }
                        asyncExportProgress[asyncExportStatusItem.Url] = csEntry;
                    }
                    catch (Exception e)
                    {
                        var errorDescription = $"Error while exporting IDM object {idmObject.IDMObjectId}";
                        var errorChangeResult = CSEntryChangeResult.Create(csEntry.Identifier, csEntry.AttributeChanges, MAExportError.ExportErrorCustomStopRun, "failed-connection", errorDescription + "\r\n" + e.Message);
                        putExportEntriesResults.CSEntryChangeResults.Add(errorChangeResult);
                        Logger.Error(e, errorDescription);
                        errorOccured = true; // stops export for remaining CSEntries
                    }
                }
                else
                {
                    Logger.Info($"Start export of IDM object {idmObject.IDMObjectId}");
                    var dataForExport = ExportMapper.MapIdmObjectsToExportData(new List<IDMObject> { idmObject });
                    var exportParameters = new ExportParameters
                    {
                        ConnectorSchema = this._connectorConfiguration.ConnectorSchema,
                        ClientSecret = clientSecret,
                        BaseUrl = baseUrl,
                        ExportUrl = exportUrl,
                        Data = dataForExport
                    };
                    errorOccured = ExportToPortal(csEntry, exportParameters, idmObject, putExportEntriesResults);
                }
                
                if (errorOccured)
                {
                    break;
                }
            }

            // Check async export progress of running exports
            if (_connectorConfiguration.IsExportAsync())
            {
                var exportParameters = new ExportParameters()
                {
                    ClientSecret = clientSecret,
                    BaseUrl = baseUrl,
                };
                CheckProgressOfRunningExports(exportParameters, putExportEntriesResults, asyncExportProgress);
            }

            return putExportEntriesResults;
        }

        /// <summary>
        /// Export CSEntry to Portal and add result to putExportEntriesResults.
        /// </summary>
        private bool ExportToPortal(CSEntryChange csEntry, ExportParameters exportParameters, IDMObject idmObject, PutExportEntriesResults putExportEntriesResults)
        {
            var errorOccured = false;
            try
            {
                Logger.Info($"Exporting IDM object {idmObject.IDMObjectId} of CSEntry");
                var exportResponse = ExportService.ExportToPortal(exportParameters);
                switch (exportResponse.ResultType)
                {
                    case ExportResultType.Success:
                        var successChangeResult = CSEntryChangeResult.Create(csEntry.Identifier, null, MAExportError.Success);
                        putExportEntriesResults.CSEntryChangeResults.Add(successChangeResult);
                        Logger.Info("Object successfully exported");
                        break;
                    case ExportResultType.ServerError:
                        var serverErrorDescription = $"Error response from portal while exporting IDM object {idmObject.IDMObjectId} of CSEntry:";
                        var serverErrorChangeResult = CSEntryChangeResult.Create(csEntry.Identifier, csEntry.AttributeChanges, MAExportError.ExportErrorCustomContinueRun, "eidm-export-error", serverErrorDescription + "\r\n" + exportResponse.ErrorMessage);
                        putExportEntriesResults.CSEntryChangeResults.Add(serverErrorChangeResult);
                        Logger.Error(serverErrorDescription + "\r\n" + exportResponse.ErrorMessage);
                        break;
                    case ExportResultType.ConnectionError:
                        var connectionErrorDescription = $"Error while exporting IDM object {idmObject.IDMObjectId} of CSEntry:";
                        var connectionErrorChangeResult = CSEntryChangeResult.Create(csEntry.Identifier, csEntry.AttributeChanges, MAExportError.ExportErrorCustomStopRun, "failed-connection", connectionErrorDescription + "\r\n" + exportResponse.ErrorMessage);
                        putExportEntriesResults.CSEntryChangeResults.Add(connectionErrorChangeResult);
                        Logger.Error(connectionErrorDescription + "\r\n" + exportResponse.ErrorMessage);
                        errorOccured = true; // stops export for remaining CSEntries
                        break;
                }
            }
            catch (Exception e)
            {
                var errorDescription = $"Error while exporting IDM object {idmObject.IDMObjectId} of CSEntry:";
                var errorChangeResult = CSEntryChangeResult.Create(csEntry.Identifier, csEntry.AttributeChanges, MAExportError.ExportErrorCustomStopRun, "failed-connection", errorDescription + "\r\n" + e.Message);
                putExportEntriesResults.CSEntryChangeResults.Add(errorChangeResult);
                Logger.Error(e, errorDescription);
                errorOccured = true; // stops export for remaining CSEntries
            }
            return errorOccured;
        }

        /// <summary>
        /// Check export progress of running exports and set export results of CSEntries.
        /// </summary>
        private void CheckProgressOfRunningExports(ExportParameters exportParameters, PutExportEntriesResults putExportEntriesResults, Dictionary<string, CSEntryChange> asyncExportProgress)
        {
            if (asyncExportProgress.Count > 0)
            {
                Logger.Info("Check progress of running async exports");
                var count = asyncExportProgress.Count;
                var current = 1;
                foreach (var progressPair in asyncExportProgress)
                {
                    Logger.Info($"Check running export: {current}/{count}");
                    var exportProgressUrl = progressPair.Key;
                    var exportedCSEntry = progressPair.Value;
                    var exportedIdmObjectId = exportedCSEntry.AnchorAttributes[0].Value.ToString();
                    var checkExportProgressUrl = $"{exportParameters.BaseUrl}{exportProgressUrl}";
                    var parameters = new ExportParameters
                    {
                        BaseUrl = exportParameters.BaseUrl,
                        ClientSecret = exportParameters.ClientSecret,
                    };

                    var asyncExportResult = ExportService.CheckExportProgress(checkExportProgressUrl, exportedCSEntry, parameters, exportedIdmObjectId);
                    putExportEntriesResults.CSEntryChangeResults.Add(asyncExportResult);

                    current++;
                }
            }
            
            Logger.Info("Finished checking progress of running exports");
        }

        /// <summary>
        /// Called at the end of an export run to disconnect the connected directory and to release resources.
        /// </summary>
        public void CloseExportConnection(CloseExportConnectionRunStep exportRunStep)
        {
            Logger.Info($"************************** CloseExportConnection operation started **************************");
        }

        public int ExportDefaultPageSize
        {
            get => this.m_exportDefaultPageSize;
            set => this.m_exportDefaultPageSize = value;
        }

        public int ExportMaxPageSize
        {
            get => this.m_exportMaxPageSize;
            set => this.m_exportMaxPageSize = value;
        }

        void IMAExtensible2Password.OpenPasswordConnection(KeyedCollection<string, ConfigParameter> configParameters, Partition partition)
        {
            throw new NotImplementedException();
        }

        void IMAExtensible2Password.ClosePasswordConnection()
        {
            throw new NotImplementedException();
        }

        ConnectionSecurityLevel IMAExtensible2Password.GetConnectionSecurityLevel()
        {
            throw new NotImplementedException();
        }

        void IMAExtensible2Password.SetPassword(CSEntry csentry, SecureString newPassword, PasswordOptions options)
        {
            throw new NotImplementedException();
        }

        void IMAExtensible2Password.ChangePassword(CSEntry csentry, SecureString oldPassword, SecureString newPassword)
        {
            throw new NotImplementedException();
        }

        /// <summary>
        /// Initializes the rules extension object.
        /// </summary>
        public void Initialize()
        {
            Logger.Info($"************************** Initialize operation started **************************");
        }

        public void Terminate()
        {
            throw new NotImplementedException();
        }

        public bool FilterForDisconnection(CSEntry csentry)
        {
            throw new NotImplementedException();
        }

        public bool ResolveJoinSearch(string joinCriteriaName, CSEntry csentry, MVEntry[] rgmventry, out int imventry, ref string MVObjectType)
        {
            throw new NotImplementedException();
        }

        public bool ShouldProjectToMV(CSEntry csentry, out string MVObjectType)
        {
            throw new NotImplementedException();
        }

        /// <summary>
        /// Maps attributes from a connector space entry to a metaverse entry based on rule.
        /// </summary>
        public void MapAttributesForImport(string flowRuleName, CSEntry csentry, MVEntry mventry)
        {
            Logger.Info($"************************** MapAttributesForImport operation started **************************");
            
            var rule = MappingAttributesRuleParser.ParseRule(flowRuleName);
            var mventryAttribute = mventry[rule.MetaverseAttribute];
            var csentryAttribute = csentry[rule.ConnectorSpaceAttribute];

            // CSEntry attribute in rule is not multivalued and is not present in CsEntry
            if (!rule.ConnectorSpaceAttribute.Contains(",") && csentryAttribute != null && !csentryAttribute.IsPresent)
            {
                if (mventryAttribute != null && mventryAttribute.IsPresent)
                {
                    mventryAttribute.Delete();
                }
                return;
            }

            HandleMapAttributesForImportRule(flowRuleName, csentry, mventry, rule, mventryAttribute, csentryAttribute);
        }

        protected virtual void HandleMapAttributesForImportRule(string flowRuleName, CSEntry csentry, MVEntry mventry, MappingAttributesRule rule, Attrib mvEntryAttribute, Attrib csEntryAttribute)
        {
            // Default case
            mvEntryAttribute.StringValue = csEntryAttribute.StringValue;
        }

        /// <summary>
        /// Generates a list of values based on the CSEntry attribute values that will be used to search the metaverse.
        /// </summary>
        public void MapAttributesForJoin(string FlowRuleName, CSEntry csentry, ref ValueCollection values)
        {
            Logger.Info($"************************** MapAttributesForJoin operation started **************************");
        }

        /// <summary>
        /// Maps attributes from a metaverse entry to a connector space entry based on rule.
        /// </summary>
        public void MapAttributesForExport(string flowRuleName, MVEntry mventry, CSEntry csentry)
        {
            Logger.Info($"************************** MapAttributesForExport operation started **************************");

            var rule = MappingAttributesRuleParser.ParseRule(flowRuleName);
            var mventryAttribute = mventry[rule.MetaverseAttribute];
            var csentryAttribute = csentry[rule.ConnectorSpaceAttribute];

            HandleMapAttributesForExportRule(flowRuleName, mventry, csentry, rule, mventryAttribute, csentryAttribute);
        }
        
        protected virtual void HandleMapAttributesForExportRule(string flowRuleName, MVEntry mventry, CSEntry csentry, MappingAttributesRule rule, Attrib mvEntryAttribute, Attrib csEntryAttribute)
        {
            // Default case
            csEntryAttribute.StringValue = mvEntryAttribute.StringValue;
        }

        public DeprovisionAction Deprovision(CSEntry csentry)
        {
            throw new NotImplementedException();
        }
    }
}
