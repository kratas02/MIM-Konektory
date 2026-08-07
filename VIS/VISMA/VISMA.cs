using Microsoft.MetadirectoryServices;
using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Reflection;
using System.Text;
using System.Text.Json;

namespace FimSync_Ezma
{
    public class EzmaExtension :
    IMAExtensible2CallImport,
    IMAExtensible2GetSchema,
    IMAExtensible2GetCapabilities,
    IMAExtensible2GetParameters
    {
        public EzmaExtension() { }

        // ----------------------------------------------------------------
        // IMAExtensible2GetCapabilities
        // ----------------------------------------------------------------
        public MACapabilities Capabilities
        {
            get
            {
                return new MACapabilities
                {
                    ConcurrentOperation = true,
                    DeleteAddAsReplace = false,
                    DeltaImport = false,
                    DistinguishedNameStyle = MADistinguishedNameStyle.None,
                    ExportPasswordInFirstPass = false,
                    ExportType = MAExportType.AttributeUpdate,
                    FullExport = true,
                    NoReferenceValuesInFirstExport = false,
                    IsDNAsAnchor = false,
                    Normalizations = MANormalizations.None,
                    ObjectConfirmation = MAObjectConfirmation.NoAddAndDeleteConfirmation,
                    ObjectRename = false,
                    SupportExport = false,
                    SupportHierarchy = false,
                    SupportImport = true,
                    SupportPartitions = false,
                    SupportPassword = false
                };
            }
        }

        // ----------------------------------------------------------------
        // IMAExtensible2GetParameters
        // ----------------------------------------------------------------
        public IList<ConfigParameterDefinition> GetConfigParameters(
            System.Collections.ObjectModel.KeyedCollection<string, ConfigParameter> configParameters,
            ConfigParameterPage page)
        {
            var defs = new List<ConfigParameterDefinition>();
            if (page == ConfigParameterPage.Connectivity)
            {
                defs.Add(ConfigParameterDefinition.CreateStringParameter("UserName", ""));
                defs.Add(ConfigParameterDefinition.CreateStringParameter("ApiKeyFile", ""));
                defs.Add(ConfigParameterDefinition.CreateStringParameter("URL", ""));
                defs.Add(ConfigParameterDefinition.CreateStringParameter("Log folder", ""));
            }
            return defs;
        }

        public ParameterValidationResult ValidateConfigParameters(
            System.Collections.ObjectModel.KeyedCollection<string, ConfigParameter> configParameters,
            ConfigParameterPage page)
        {
            return new ParameterValidationResult();
        }

        // ----------------------------------------------------------------
        // IMAExtensible2GetSchema
        // ----------------------------------------------------------------
        public Schema GetSchema(
            System.Collections.ObjectModel.KeyedCollection<string, ConfigParameter> configParameters)
        {
            var schema = Schema.Create();

            // Person
            var personType = SchemaType.Create("Person", false);
            personType.Attributes.Add(SchemaAttribute.CreateAnchorAttribute("employeeId", AttributeType.String));
            var referenceAttrs = new System.Collections.Generic.HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                "manager", "departmentId", "jobId"
            };
            foreach (var prop in typeof(IdmUser).GetProperties(BindingFlags.Public | BindingFlags.Instance))
            {
                if (prop.Name.ToLower() != "employeeid" && prop.Name != "ExtensionData")
                {
                    string attrName = char.ToLower(prop.Name[0]) + prop.Name.Substring(1);
                    AttributeType attrType = referenceAttrs.Contains(prop.Name) ? AttributeType.Reference : AttributeType.String;
                    personType.Attributes.Add(SchemaAttribute.CreateSingleValuedAttribute(attrName, attrType));
                }
            }
            schema.Types.Add(personType);

            // Department
            var deptType = SchemaType.Create("Department", false);
            deptType.Attributes.Add(SchemaAttribute.CreateAnchorAttribute("id", AttributeType.String));
            deptType.Attributes.Add(SchemaAttribute.CreateSingleValuedAttribute("number", AttributeType.String));
            deptType.Attributes.Add(SchemaAttribute.CreateSingleValuedAttribute("name", AttributeType.String));
            deptType.Attributes.Add(SchemaAttribute.CreateSingleValuedAttribute("validFrom", AttributeType.String));
            deptType.Attributes.Add(SchemaAttribute.CreateSingleValuedAttribute("validTo", AttributeType.String));
            schema.Types.Add(deptType);

            // Location
            var locType = SchemaType.Create("Location", false);
            locType.Attributes.Add(SchemaAttribute.CreateAnchorAttribute("id", AttributeType.String));
            locType.Attributes.Add(SchemaAttribute.CreateSingleValuedAttribute("name", AttributeType.String));
            schema.Types.Add(locType);

            // JobTitle
            var jobType = SchemaType.Create("JobTitle", false);
            jobType.Attributes.Add(SchemaAttribute.CreateAnchorAttribute("id", AttributeType.String));
            jobType.Attributes.Add(SchemaAttribute.CreateSingleValuedAttribute("number", AttributeType.String));
            jobType.Attributes.Add(SchemaAttribute.CreateSingleValuedAttribute("name", AttributeType.String));
            jobType.Attributes.Add(SchemaAttribute.CreateSingleValuedAttribute("validFrom", AttributeType.String));
            jobType.Attributes.Add(SchemaAttribute.CreateSingleValuedAttribute("validTo", AttributeType.String));
            schema.Types.Add(jobType);

            return schema;
        }

        // ----------------------------------------------------------------
        // Private state
        // ----------------------------------------------------------------
        private string _logFile;
        private int _pageSize = 1000;

        // Import state – reset at OpenImportConnection
        private int _typeIndex = 0;   // 0=Person 1=Department 2=Location 3=JobTitle
        private int _pageIndex = 0;   // page counter within the current type

        private IdmUser[]       _users;
        private IdmDepartment[] _departments;
        private IdmLocation[]   _locations;
        private IdmJobTitle[]   _jobTitles;

        private void Log(string text)
        {
            try
            {
                File.AppendAllText(_logFile, $"[{DateTime.Now:HH:mm:ss}] {text}{Environment.NewLine}");
            }
            catch { }
        }

        // ----------------------------------------------------------------
        // IMAExtensible2CallImport – OpenImportConnection
        // ----------------------------------------------------------------
        public OpenImportConnectionResults OpenImportConnection(
            System.Collections.ObjectModel.KeyedCollection<string, ConfigParameter> configParameters,
            Schema types,
            OpenImportConnectionRunStep importRunStep)
        {
            string username    = configParameters["UserName"].Value;
            string apiKeyFile  = configParameters["ApiKeyFile"].Value;
            string usersUrl    = configParameters["URL"].Value;
            string logFolder   = configParameters["Log folder"].Value;

            _logFile = Path.Combine(logFolder, "VIS" + DateTime.Now.ToString("yyyyMMdd") + ".txt");
            if (!Directory.Exists(logFolder))
                Directory.CreateDirectory(logFolder);
            _typeIndex = 0;
            _pageIndex = 0;

            string password = File.ReadAllText(apiKeyFile).Trim();

            string baseUrl = DeriveBaseUrl(usersUrl);
            Log($"Base URL: {baseUrl}");

            _users       = GetObjects<IdmUser>      (usersUrl,                       username, password);
            _departments = GetObjects<IdmDepartment>(baseUrl + "/Departments",        username, password);
            _locations   = GetObjects<IdmLocation>  (baseUrl + "/Locations",          username, password);
            _jobTitles   = GetObjects<IdmJobTitle>  (baseUrl + "/JobTitles",          username, password);

            Log($"Nacteno: Users={_users.Length} Departments={_departments.Length} Locations={_locations.Length} JobTitles={_jobTitles.Length}");

            return new OpenImportConnectionResults();
        }

        // ----------------------------------------------------------------
        // IMAExtensible2CallImport – GetImportEntries
        // ----------------------------------------------------------------
        public GetImportEntriesResults GetImportEntries(GetImportEntriesRunStep importRunStep)
        {
            var csentries = new List<CSEntryChange>();
            bool moreToImport = false;

            switch (_typeIndex)
            {
                case 0:
                    ProcessPage(_users, "Person", ref csentries, out moreToImport, "employeeId");
                    break;
                case 1:
                    ProcessPage(_departments, "Department", ref csentries, out moreToImport, "id");
                    break;
                case 2:
                    ProcessPage(_locations, "Location", ref csentries, out moreToImport, "id");
                    break;
                case 3:
                    ProcessPage(_jobTitles, "JobTitle", ref csentries, out moreToImport, "id");
                    break;
            }

            var result = new GetImportEntriesResults();
            result.CSEntries = csentries;
            result.MoreToImport = moreToImport;
            return result;
        }

        private void ProcessPage<T>(T[] data, string objectType, ref List<CSEntryChange> entries, out bool moreToImport, string anchorProp)
        {
            int first = _pageIndex * _pageSize;
            int last  = Math.Min(first + _pageSize - 1, data.Length - 1);

            Log($"[{objectType}] page={_pageIndex} first={first} last={last} total={data.Length}");

            for (int i = first; i <= last; i++)
            {
                var csentry = CSEntryChange.Create();
                csentry.ObjectModificationType = ObjectModificationType.Add;
                csentry.ObjectType = objectType;

                foreach (var prop in typeof(T).GetProperties(BindingFlags.Public | BindingFlags.Instance))
                {
                    if (prop.Name == "ExtensionData") continue;

                    var value = prop.GetValue(data[i]);
                    if (value == null) continue;

                    string strValue = value.ToString();
                    if (string.IsNullOrEmpty(strValue)) continue;

                    // Always use camelCase attribute names to match schema registration
                    string attrNameCamel = char.ToLower(prop.Name[0]) + prop.Name.Substring(1);

                    if (string.Equals(attrNameCamel, anchorProp, StringComparison.OrdinalIgnoreCase))
                        csentry.AttributeChanges.Add(AttributeChange.CreateAttributeAdd(anchorProp, strValue));
                    else
                        csentry.AttributeChanges.Add(AttributeChange.CreateAttributeAdd(attrNameCamel, strValue));
                }

                entries.Add(csentry);
            }

            _pageIndex++;

            if (last < data.Length - 1)
            {
                // More pages remain for the current type
                moreToImport = true;
            }
            else
            {
                // Move to next type
                _typeIndex++;
                _pageIndex = 0;
                moreToImport = (_typeIndex <= 3);
            }
        }

        // ----------------------------------------------------------------
        // IMAExtensible2CallImport – CloseImportConnection
        // ----------------------------------------------------------------
        public CloseImportConnectionResults CloseImportConnection(CloseImportConnectionRunStep importRunStep)
        {
            return new CloseImportConnectionResults();
        }

        // ----------------------------------------------------------------
        // Helpers
        // ----------------------------------------------------------------

        /// <summary>
        /// Strips the last path segment from the Users URL to get the base URL.
        /// e.g. http://host:5003/api/v1/Custom/Idm/Users -> http://host:5003/api/v1/Custom/Idm
        /// </summary>
        private static string DeriveBaseUrl(string usersUrl)
        {
            string trimmed = usersUrl.TrimEnd('/');
            int lastSlash = trimmed.LastIndexOf('/');
            return lastSlash > 0 ? trimmed.Substring(0, lastSlash) : trimmed;
        }

        private T[] GetObjects<T>(string uri, string user, string pass)
        {
            try
            {
                Log($"GET {uri} (user={user})");
                var client = new HttpClient();
                if (!string.IsNullOrEmpty(user))
                {
                    var credentials = Convert.ToBase64String(Encoding.ASCII.GetBytes(user + ":" + pass));
                    client.DefaultRequestHeaders.Authorization =
                        new AuthenticationHeaderValue("Basic", credentials);
                }

                var response = client.GetAsync(uri).GetAwaiter().GetResult();
                Log($"HTTP {(int)response.StatusCode} {response.StatusCode} <- {uri}");
                response.EnsureSuccessStatusCode();

                var json = response.Content.ReadAsStringAsync().GetAwaiter().GetResult();
                var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
                var result = JsonSerializer.Deserialize<T[]>(json, options) ?? Array.Empty<T>();
                Log($"Deserializovano {result.Length} zaznamu z {uri}");
                return result;
            }
            catch (Exception ex)
            {
                Log($"CHYBA [{uri}]: {ex.Message}");
                return Array.Empty<T>();
            }
        }

        // ----------------------------------------------------------------
        // Page size properties
        // ----------------------------------------------------------------
        public int ImportDefaultPageSize { get { return _pageSize; } }
        public int ImportMaxPageSize     { get { return _pageSize; } }
    }
}
