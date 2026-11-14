using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Data;
using System.Data.SqlClient;
using System.Globalization;
using System.Text;
using Microsoft.MetadirectoryServices;

using MmsSchemaType = Microsoft.MetadirectoryServices.SchemaType;

namespace EasyIdmCastMA
{
    /// <summary>
    /// Read-only ECMA2 konektor: SAP casty z EasyIDM (server MIM, DB EasyIDM).
    /// Full Import = validni casty puvodniho dotazu.
    /// Delta = zmena identity/role/castu (Created/Modified) + smazane casty
    /// (IsArchived=1, IsValid=0) + vyprsene ValidTo.
    /// </summary>
    public class EasyIdmCastConnector :
        IMAExtensible2GetCapabilities,
        IMAExtensible2GetParameters,
        IMAExtensible2GetSchema,
        IMAExtensible2CallImport
    {
        public const string ObjectTypeName = "Cast";

        private const string CP_SERVER = "SQL Server";
        private const string CP_DATABASE = "Database";
        private const string CP_INTEGRATED = "Windows authentication";
        private const string CP_USER = "SQL login";
        private const string CP_PASSWORD = "SQL password";
        private const string CP_CONNOPTIONS = "Connection string options";
        private const string CP_TIMEOUT = "Command timeout (s)";
        private const string CP_DELTA_OVERLAP = "Delta overlap (s)";
        private const string CP_LOGFILE = "Log file";
        private const string CP_VERBOSE = "Verbose log";

        private const string WatermarkPrefix = "EasyIdmCastMA/1|";
        private const int MaxDeltaObjectsPerRun = 100000;

        private SqlConnection _conn;
        private int _commandTimeout = 300;
        private int _pageSize = 200;

        private bool _isDelta;
        private string _incomingWatermark;
        private DateTime _newWatermark;
        private int _lastCastId;
        private bool _fullDone;

        private List<int> _deltaIds;
        private int _deltaIndex;
        private int _imported;
        private int _deleted;

        public int ImportDefaultPageSize { get { return 200; } }
        public int ImportMaxPageSize { get { return 2000; } }

        public MACapabilities Capabilities
        {
            get
            {
                return new MACapabilities
                {
                    SupportImport = true,
                    SupportExport = false,
                    SupportPassword = false,
                    SupportPartitions = false,
                    SupportHierarchy = false,
                    DeltaImport = true,
                    ConcurrentOperation = false,
                    DistinguishedNameStyle = MADistinguishedNameStyle.Ldap,
                    IsDNAsAnchor = true,
                    ObjectRename = false,
                    ObjectConfirmation = MAObjectConfirmation.Normal,
                    Normalizations = MANormalizations.None,
                    ExportType = MAExportType.AttributeUpdate,
                    DeleteAddAsReplace = false,
                    FullExport = false,
                    NoReferenceValuesInFirstExport = false,
                    ExportPasswordInFirstPass = false
                };
            }
        }

        public IList<ConfigParameterDefinition> GetConfigParameters(
            KeyedCollection<string, ConfigParameter> configParameters,
            ConfigParameterPage page)
        {
            var list = new List<ConfigParameterDefinition>();

            if (page == ConfigParameterPage.Connectivity)
            {
                list.Add(ConfigParameterDefinition.CreateStringParameter(CP_SERVER, "", "MIM"));
                list.Add(ConfigParameterDefinition.CreateStringParameter(CP_DATABASE, "", "EasyIDM"));
                list.Add(ConfigParameterDefinition.CreateDividerParameter());
                list.Add(ConfigParameterDefinition.CreateCheckBoxParameter(CP_INTEGRATED, true));
                list.Add(ConfigParameterDefinition.CreateStringParameter(CP_USER, ""));
                list.Add(ConfigParameterDefinition.CreateEncryptedStringParameter(CP_PASSWORD, ""));
                list.Add(ConfigParameterDefinition.CreateDividerParameter());
                list.Add(ConfigParameterDefinition.CreateStringParameter(CP_CONNOPTIONS, ""));
                list.Add(ConfigParameterDefinition.CreateLabelParameter(
                    "Staci ucet s pravem SELECT (db_datareader) na EasyIDM. " +
                    "Connection string options napr.: Encrypt=True;TrustServerCertificate=True"));
            }
            else if (page == ConfigParameterPage.Global)
            {
                list.Add(ConfigParameterDefinition.CreateStringParameter(CP_TIMEOUT, "", "300"));
                list.Add(ConfigParameterDefinition.CreateStringParameter(CP_DELTA_OVERLAP, "", "120"));
                list.Add(ConfigParameterDefinition.CreateLabelParameter(
                    "Delta overlap = o kolik sekund se watermark posouva dozadu. " +
                    "Kryje zmeny commitnute mimo poradi a rozdil hodin."));
                list.Add(ConfigParameterDefinition.CreateDividerParameter());
                list.Add(ConfigParameterDefinition.CreateStringParameter(CP_LOGFILE, "", Log.DefaultFile));
                list.Add(ConfigParameterDefinition.CreateCheckBoxParameter(CP_VERBOSE, false));
            }

            return list;
        }

        public ParameterValidationResult ValidateConfigParameters(
            KeyedCollection<string, ConfigParameter> configParameters,
            ConfigParameterPage page)
        {
            if (page != ConfigParameterPage.Connectivity)
                return new ParameterValidationResult();

            ApplyLogSettings(configParameters);

            if (string.IsNullOrWhiteSpace(GetString(configParameters, CP_SERVER, "")))
                return Failure(CP_SERVER, "Zadejte nazev SQL serveru (napr. MIM).");

            if (!GetBool(configParameters, CP_INTEGRATED, true)
                && string.IsNullOrWhiteSpace(GetString(configParameters, CP_USER, "")))
            {
                return Failure(CP_USER, "Pri SQL autentizaci je nutne zadat SQL login.");
            }

            try
            {
                using (SqlConnection conn = OpenConnection(configParameters))
                {
                    string problem = VerifySchema(conn, GetTimeout(configParameters));
                    if (problem != null)
                    {
                        Log.Error("ValidateConfigParameters: " + problem);
                        return Failure(CP_DATABASE, problem);
                    }
                }
            }
            catch (Exception ex)
            {
                Log.Error("ValidateConfigParameters: pripojeni selhalo.", ex);
                return Failure(CP_SERVER, "Pripojeni k databazi selhalo: " + ex.Message);
            }

            Log.Info("ValidateConfigParameters: pripojeni k EasyIDM je v poradku.");
            return new ParameterValidationResult();
        }

        public Schema GetSchema(KeyedCollection<string, ConfigParameter> configParameters)
        {
            ApplyLogSettings(configParameters);

            Schema schema = Schema.Create();
            MmsSchemaType type = MmsSchemaType.Create(ObjectTypeName, false);

            type.Attributes.Add(SchemaAttribute.CreateSingleValuedAttribute("CastId", AttributeType.String));
            type.Attributes.Add(SchemaAttribute.CreateSingleValuedAttribute("applicationCode", AttributeType.String));
            type.Attributes.Add(SchemaAttribute.CreateSingleValuedAttribute("applicationModulCode", AttributeType.String));
            type.Attributes.Add(SchemaAttribute.CreateSingleValuedAttribute("IDMObjectId", AttributeType.String));
            type.Attributes.Add(SchemaAttribute.CreateSingleValuedAttribute("isValidUser", AttributeType.Boolean));
            type.Attributes.Add(SchemaAttribute.CreateSingleValuedAttribute("IsValidRole", AttributeType.Boolean));
            type.Attributes.Add(SchemaAttribute.CreateSingleValuedAttribute("IdentityTechName", AttributeType.String));
            type.Attributes.Add(SchemaAttribute.CreateSingleValuedAttribute("IdentityName", AttributeType.String));
            type.Attributes.Add(SchemaAttribute.CreateSingleValuedAttribute("RoleId", AttributeType.String));
            type.Attributes.Add(SchemaAttribute.CreateSingleValuedAttribute("RoleTechName", AttributeType.String));
            type.Attributes.Add(SchemaAttribute.CreateSingleValuedAttribute("RoleName", AttributeType.String));
            type.Attributes.Add(SchemaAttribute.CreateSingleValuedAttribute("category", AttributeType.String));
            type.Attributes.Add(SchemaAttribute.CreateSingleValuedAttribute("IsArchived", AttributeType.Boolean));
            type.Attributes.Add(SchemaAttribute.CreateSingleValuedAttribute("isValidCast", AttributeType.Boolean));
            type.Attributes.Add(SchemaAttribute.CreateSingleValuedAttribute("ValidFrom", AttributeType.String));
            type.Attributes.Add(SchemaAttribute.CreateSingleValuedAttribute("ValidTo", AttributeType.String));

            schema.Types.Add(type);
            Log.Info("GetSchema: object type Cast, 16 atributu.");
            return schema;
        }

        public OpenImportConnectionResults OpenImportConnection(
            KeyedCollection<string, ConfigParameter> configParameters,
            Schema types,
            OpenImportConnectionRunStep importRunStep)
        {
            ApplyLogSettings(configParameters);

            if (importRunStep.ImportType != OperationType.Full && importRunStep.ImportType != OperationType.Delta)
                throw new NotSupportedException(
                    "Konektor podporuje jen Full Import a Delta Import, ne " + importRunStep.ImportType + ".");

            _isDelta = importRunStep.ImportType == OperationType.Delta;
            _commandTimeout = GetTimeout(configParameters);
            _pageSize = importRunStep.PageSize > 0
                ? Math.Min(importRunStep.PageSize, ImportMaxPageSize)
                : ImportDefaultPageSize;

            _conn = OpenConnection(configParameters);
            _incomingWatermark = importRunStep.CustomData;
            _lastCastId = 0;
            _fullDone = false;
            _deltaIds = new List<int>();
            _deltaIndex = 0;
            _imported = 0;
            _deleted = 0;

            DateTime runStart = GetDatabaseNow();
            _newWatermark = runStart;

            if (_isDelta)
            {
                DateTime last = ParseWatermark(_incomingWatermark);
                DateTime from = last.AddSeconds(-GetDeltaOverlap(configParameters));
                LoadDeltaIds(from);

                Log.Info(string.Format(
                    CultureInfo.InvariantCulture,
                    "OpenImportConnection (delta): okno {0:o} - {1:o} (watermark {2:o}), kandidatu {3}",
                    from, runStart, last, _deltaIds.Count));
            }
            else
            {
                Log.Info(string.Format(
                    CultureInfo.InvariantCulture,
                    "OpenImportConnection (full): watermark {0:o}, pageSize {1}",
                    runStart, _pageSize));
            }

            return new OpenImportConnectionResults(_incomingWatermark);
        }

        public GetImportEntriesResults GetImportEntries(GetImportEntriesRunStep importRunStep)
        {
            List<CSEntryChange> entries = _isDelta
                ? GetDeltaPage()
                : GetFullPage();

            bool more = _isDelta
                ? _deltaIndex < _deltaIds.Count
                : !_fullDone;

            return new GetImportEntriesResults
            {
                CSEntries = entries,
                MoreToImport = more,
                CustomData = more ? _incomingWatermark : FormatWatermark(_newWatermark)
            };
        }

        public CloseImportConnectionResults CloseImportConnection(CloseImportConnectionRunStep importRunStepInfo)
        {
            bool completed = _isDelta
                ? _deltaIndex >= (_deltaIds == null ? 0 : _deltaIds.Count)
                : _fullDone;

            Log.Info(string.Format(
                CultureInfo.InvariantCulture,
                "CloseImportConnection: importovano {0}, smazano {1}, reason={2}, dokonceno={3}",
                _imported, _deleted, importRunStepInfo.Reason, completed));

            if (_conn != null)
            {
                _conn.Dispose();
                _conn = null;
            }

            bool advance = completed && importRunStepInfo.Reason == CloseReason.Normal;
            string watermark = advance ? FormatWatermark(_newWatermark) : _incomingWatermark;
            if (!advance)
                Log.Warn("CloseImportConnection: watermark se neposouva, import nedobehl cely.");

            _deltaIds = null;
            return new CloseImportConnectionResults(watermark);
        }

        private List<CSEntryChange> GetFullPage()
        {
            var entries = new List<CSEntryChange>();
            int lastInPage = _lastCastId;

            using (var cmd = new SqlCommand(Queries.FullImportPage, _conn))
            {
                cmd.CommandTimeout = _commandTimeout;
                cmd.Parameters.Add("@lastCastId", SqlDbType.Int).Value = _lastCastId;
                cmd.Parameters.Add("@pageSize", SqlDbType.Int).Value = _pageSize;

                using (SqlDataReader reader = cmd.ExecuteReader())
                {
                    while (reader.Read())
                    {
                        CastRow row = ReadRow(reader);
                        entries.Add(CreateAddOrReplace(row, ObjectModificationType.Add));
                        lastInPage = row.CastId;
                        _imported++;
                    }
                }
            }

            if (entries.Count < _pageSize)
                _fullDone = true;
            else
                _lastCastId = lastInPage;

            Log.Debug(string.Format(
                CultureInfo.InvariantCulture,
                "GetFullPage: {0} radku, lastCastId={1}, done={2}",
                entries.Count, _lastCastId, _fullDone));

            return entries;
        }

        private List<CSEntryChange> GetDeltaPage()
        {
            var entries = new List<CSEntryChange>();
            if (_deltaIds.Count == 0)
                return entries;

            int take = Math.Min(_pageSize, _deltaIds.Count - _deltaIndex);
            var pageIds = new List<int>(take);
            for (int i = 0; i < take; i++)
                pageIds.Add(_deltaIds[_deltaIndex + i]);
            _deltaIndex += take;

            var current = new Dictionary<int, CastRow>();
            string idList = string.Join(",", pageIds);

            using (var cmd = new SqlCommand(Queries.CurrentByCastIds, _conn))
            {
                cmd.CommandTimeout = _commandTimeout;
                cmd.Parameters.Add("@castIds", SqlDbType.NVarChar, -1).Value = idList;

                using (SqlDataReader reader = cmd.ExecuteReader())
                {
                    while (reader.Read())
                    {
                        CastRow row = ReadRow(reader);
                        current[row.CastId] = row;
                    }
                }
            }

            foreach (int castId in pageIds)
            {
                CastRow row;
                if (current.TryGetValue(castId, out row))
                {
                    entries.Add(CreateAddOrReplace(row, ObjectModificationType.Replace));
                    _imported++;
                }
                else
                {
                    entries.Add(CreateDelete(castId));
                    _deleted++;
                }
            }

            Log.Debug(string.Format(
                CultureInfo.InvariantCulture,
                "GetDeltaPage: ids={0}, replace={1}, delete={2}, index={3}/{4}",
                pageIds.Count, current.Count, pageIds.Count - current.Count, _deltaIndex, _deltaIds.Count));

            return entries;
        }

        private void LoadDeltaIds(DateTime fromTime)
        {
            using (var cmd = new SqlCommand(Queries.DeltaCandidateIds, _conn))
            {
                cmd.CommandTimeout = _commandTimeout;
                cmd.Parameters.Add("@fromTime", SqlDbType.DateTime2).Value = fromTime;
                cmd.Parameters.Add("@maxRows", SqlDbType.Int).Value = MaxDeltaObjectsPerRun;

                using (SqlDataReader reader = cmd.ExecuteReader())
                {
                    while (reader.Read())
                        _deltaIds.Add(reader.GetInt32(0));
                }
            }

            if (_deltaIds.Count >= MaxDeltaObjectsPerRun)
            {
                Log.Warn(string.Format(
                    CultureInfo.InvariantCulture,
                    "Delta: dosazen strop {0} castu na jeden run. Zbytek dobere dalsi delta.",
                    MaxDeltaObjectsPerRun));
            }
        }

        private static CSEntryChange CreateAddOrReplace(CastRow row, ObjectModificationType modification)
        {
            CSEntryChange entry = CSEntryChange.Create();
            entry.ObjectModificationType = modification;
            entry.ObjectType = ObjectTypeName;
            entry.DN = ObjectDn(row.CastId);

            AddString(entry, "CastId", row.CastId.ToString(CultureInfo.InvariantCulture));
            AddString(entry, "applicationCode", row.ApplicationCode);
            AddString(entry, "applicationModulCode", row.ApplicationModulCode);
            AddString(entry, "IDMObjectId", row.IdentityId);
            AddBool(entry, "isValidUser", row.IsValidUser);
            AddBool(entry, "IsValidRole", row.IsValidRole);
            AddString(entry, "IdentityTechName", row.IdentityTechName);
            AddString(entry, "IdentityName", row.IdentityName);
            AddString(entry, "RoleId", row.RoleId);
            AddString(entry, "RoleTechName", row.RoleTechName);
            AddString(entry, "RoleName", row.RoleName);
            AddString(entry, "category", row.Category);
            AddBool(entry, "IsArchived", row.IsArchived);
            AddBool(entry, "isValidCast", row.IsValidCast);
            AddString(entry, "ValidFrom", FormatDate(row.ValidFrom, "00:00:00.0000000"));
            AddString(entry, "ValidTo", FormatDate(row.ValidTo, "23:59:59.0000000"));
            return entry;
        }

        private static CSEntryChange CreateDelete(int castId)
        {
            CSEntryChange entry = CSEntryChange.Create();
            entry.ObjectModificationType = ObjectModificationType.Delete;
            entry.ObjectType = ObjectTypeName;
            entry.DN = ObjectDn(castId);
            return entry;
        }

        private static void AddString(CSEntryChange entry, string name, string value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return;
            entry.AttributeChanges.Add(AttributeChange.CreateAttributeAdd(name, value));
        }

        private static void AddBool(CSEntryChange entry, string name, bool? value)
        {
            if (!value.HasValue)
                return;
            entry.AttributeChanges.Add(AttributeChange.CreateAttributeAdd(name, value.Value));
        }

        private static string FormatDate(DateTime? value, string timeOfDay)
        {
            if (!value.HasValue)
                return null;
            return value.Value.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) + " " + timeOfDay;
        }

        private static string ObjectDn(int castId)
        {
            return "CN=" + castId.ToString(CultureInfo.InvariantCulture) + ",CN=" + ObjectTypeName;
        }

        private static CastRow ReadRow(IDataRecord reader)
        {
            return new CastRow
            {
                CastId = reader.GetInt32(reader.GetOrdinal("CastId")),
                ApplicationCode = ReadString(reader, "applicationCode"),
                ApplicationModulCode = ReadString(reader, "applicationModulCode"),
                IdentityId = ReadGuid(reader, "IDMObjectId"),
                IsValidUser = ReadBool(reader, "isValidUser"),
                IsValidRole = ReadBool(reader, "IsValidRole"),
                IdentityTechName = ReadString(reader, "IdentityTechName"),
                IdentityName = ReadString(reader, "IdentityName"),
                RoleId = ReadGuid(reader, "RoleId"),
                RoleTechName = ReadString(reader, "RoleTechName"),
                RoleName = ReadString(reader, "RoleName"),
                Category = ReadString(reader, "category"),
                IsArchived = ReadBool(reader, "IsArchived"),
                IsValidCast = ReadBool(reader, "isValidCast"),
                ValidFrom = ReadDate(reader, "ValidFrom"),
                ValidTo = ReadDate(reader, "ValidTo")
            };
        }

        private static string ReadString(IDataRecord reader, string name)
        {
            int i = reader.GetOrdinal(name);
            if (reader.IsDBNull(i))
                return null;
            return Convert.ToString(reader.GetValue(i), CultureInfo.InvariantCulture);
        }

        private static string ReadGuid(IDataRecord reader, string name)
        {
            int i = reader.GetOrdinal(name);
            if (reader.IsDBNull(i))
                return null;
            object value = reader.GetValue(i);
            if (value is Guid)
                return ((Guid)value).ToString();
            return Convert.ToString(value, CultureInfo.InvariantCulture);
        }

        private static bool? ReadBool(IDataRecord reader, string name)
        {
            int i = reader.GetOrdinal(name);
            if (reader.IsDBNull(i))
                return null;
            return Convert.ToBoolean(reader.GetValue(i), CultureInfo.InvariantCulture);
        }

        private static DateTime? ReadDate(IDataRecord reader, string name)
        {
            int i = reader.GetOrdinal(name);
            if (reader.IsDBNull(i))
                return null;
            return Convert.ToDateTime(reader.GetValue(i), CultureInfo.InvariantCulture);
        }

        private DateTime GetDatabaseNow()
        {
            using (var cmd = new SqlCommand(Queries.ServerNow, _conn))
            {
                cmd.CommandTimeout = 30;
                return DateTime.SpecifyKind((DateTime)cmd.ExecuteScalar(), DateTimeKind.Unspecified);
            }
        }

        private DateTime ParseWatermark(string customData)
        {
            if (!string.IsNullOrWhiteSpace(customData) && customData.StartsWith(WatermarkPrefix, StringComparison.Ordinal))
            {
                DateTime parsed;
                if (DateTime.TryParseExact(
                    customData.Substring(WatermarkPrefix.Length).Trim(),
                    "o",
                    CultureInfo.InvariantCulture,
                    DateTimeStyles.RoundtripKind,
                    out parsed))
                {
                    return parsed;
                }
            }

            throw new NotSupportedException(
                "Delta import nema watermark tohoto konektoru. Spustte nejprve Full Import.");
        }

        private static string FormatWatermark(DateTime value)
        {
            return WatermarkPrefix + value.ToString("o", CultureInfo.InvariantCulture);
        }

        private static SqlConnection OpenConnection(KeyedCollection<string, ConfigParameter> cfg)
        {
            var builder = new SqlConnectionStringBuilder
            {
                DataSource = GetString(cfg, CP_SERVER, "MIM"),
                InitialCatalog = GetString(cfg, CP_DATABASE, "EasyIDM"),
                IntegratedSecurity = GetBool(cfg, CP_INTEGRATED, true)
            };

            if (!builder.IntegratedSecurity)
            {
                builder.UserID = GetString(cfg, CP_USER, "");
                builder.Password = GetEncrypted(cfg, CP_PASSWORD);
            }

            string connectionString = builder.ConnectionString;
            string options = GetString(cfg, CP_CONNOPTIONS, "").Trim().TrimStart(';');
            if (options.Length > 0)
                connectionString = new SqlConnectionStringBuilder(connectionString + ";" + options).ConnectionString;

            var conn = new SqlConnection(connectionString);
            conn.Open();

            using (var cmd = new SqlCommand("SET TRANSACTION ISOLATION LEVEL READ UNCOMMITTED", conn))
            {
                cmd.CommandTimeout = 30;
                cmd.ExecuteNonQuery();
            }

            return conn;
        }

        private static string VerifySchema(SqlConnection conn, int timeout)
        {
            using (var cmd = new SqlCommand(Queries.VerifyObjects, conn))
            {
                cmd.CommandTimeout = timeout;
                using (SqlDataReader reader = cmd.ExecuteReader())
                {
                    if (!reader.Read())
                        return "Nelze overit schema EasyIDM.";

                    var missing = new StringBuilder();
                    for (int i = 0; i < reader.FieldCount; i++)
                    {
                        if (reader.IsDBNull(i))
                        {
                            if (missing.Length > 0)
                                missing.Append(", ");
                            missing.Append("dbo." + reader.GetName(i));
                        }
                    }

                    if (missing.Length > 0)
                        return "V databazi chybi tabulky: " + missing + ".";
                }
            }

            return null;
        }

        private static void ApplyLogSettings(KeyedCollection<string, ConfigParameter> cfg)
        {
            Log.Configure(GetString(cfg, CP_LOGFILE, ""), GetBool(cfg, CP_VERBOSE, false));
        }

        private static int GetTimeout(KeyedCollection<string, ConfigParameter> cfg)
        {
            int seconds;
            if (!int.TryParse(GetString(cfg, CP_TIMEOUT, ""), out seconds) || seconds < 1)
                seconds = 300;
            return seconds;
        }

        private static int GetDeltaOverlap(KeyedCollection<string, ConfigParameter> cfg)
        {
            int seconds;
            if (!int.TryParse(GetString(cfg, CP_DELTA_OVERLAP, ""), out seconds) || seconds < 0)
                seconds = 120;
            return seconds;
        }

        private static string GetString(KeyedCollection<string, ConfigParameter> cfg, string name, string fallback)
        {
            if (cfg == null || !cfg.Contains(name))
                return fallback;
            string value = cfg[name].Value;
            return value == null ? fallback : value;
        }

        private static string GetEncrypted(KeyedCollection<string, ConfigParameter> cfg, string name)
        {
            if (cfg == null || !cfg.Contains(name))
                return "";
            try
            {
                return cfg[name].SecureValue == null
                    ? ""
                    : new System.Net.NetworkCredential("", cfg[name].SecureValue).Password;
            }
            catch
            {
                return cfg[name].Value ?? "";
            }
        }

        private static bool GetBool(KeyedCollection<string, ConfigParameter> cfg, string name, bool fallback)
        {
            if (cfg == null || !cfg.Contains(name))
                return fallback;
            try
            {
                return cfg[name].Value == "1" || string.Equals(cfg[name].Value, "true", StringComparison.OrdinalIgnoreCase);
            }
            catch
            {
                return fallback;
            }
        }

        private static ParameterValidationResult Failure(string parameter, string message)
        {
            return new ParameterValidationResult
            {
                ErrorMessage = message,
                Code = ParameterValidationResultCode.Failure,
                ErrorParameter = parameter
            };
        }

        private sealed class CastRow
        {
            public int CastId;
            public string ApplicationCode;
            public string ApplicationModulCode;
            public string IdentityId;
            public bool? IsValidUser;
            public bool? IsValidRole;
            public string IdentityTechName;
            public string IdentityName;
            public string RoleId;
            public string RoleTechName;
            public string RoleName;
            public string Category;
            public bool? IsArchived;
            public bool? IsValidCast;
            public DateTime? ValidFrom;
            public DateTime? ValidTo;
        }
    }
}
