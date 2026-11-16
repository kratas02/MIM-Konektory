using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using Microsoft.MetadirectoryServices;
using Oracle.ManagedDataAccess.Client;
using MmsSchemaType = Microsoft.MetadirectoryServices.SchemaType;

namespace UisECMA2MA
{
    /// <summary>
    /// ECMA2 konektor pro cteni objektu z UIS (Oracle). Jen full import.
    /// UIS publikuje triggerless, zadna delta tabulka tu neni.
    /// </summary>
    public class UisConnector :
        IMAExtensible2GetCapabilities,
        IMAExtensible2GetSchema,
        IMAExtensible2GetParameters,
        IMAExtensible2GetPartitions,
        IMAExtensible2CallImport
    {
        private const string CP_USER     = "Username";
        private const string CP_PASSWORD = "Password";

        // Obsah, ktery se zapise do DataSource.txt, kdyz soubor jeste neni.
        private const string UIS_DATASOURCE =
            "(DESCRIPTION=(ADDRESS_LIST=(ADDRESS=(COMMUNITY=tcp.world)(PROTOCOL=TCP)(Host=magi.oradb-t.czu.cz)(Port=1523)))(CONNECT_DATA=(SERVER=DEDICATED)(SERVICE_NAME=UISTEST.oradb.czu.cz)))";

        private KeyedCollection<string, ConfigParameter> _config;
        private ObjectTypeConfig _currentConfig;
        private OracleConnection _conn;
        private OracleDataReader _reader;

        public int ImportDefaultPageSize => 500;
        public int ImportMaxPageSize     => 1000;

        public MACapabilities Capabilities => new MACapabilities
        {
            SupportImport          = true,
            SupportExport          = false,
            DeltaImport            = false,
            SupportPartitions      = true,
            DistinguishedNameStyle = MADistinguishedNameStyle.Ldap,
            IsDNAsAnchor           = true,
            ConcurrentOperation    = false
        };

        public IList<Partition> GetPartitions(KeyedCollection<string, ConfigParameter> configParameters)
        {
            var list = new List<Partition>();
            foreach (var cfg in UisConfig.All)
            {
                list.Add(Partition.Create(
                    UisConfig.NameToGuid(cfg.Name),
                    "CN=" + cfg.Name,
                    cfg.Name));
            }
            Log("INFO", "GetPartitions: " + list.Count + " partitions");
            return list;
        }

        public IList<ConfigParameterDefinition> GetConfigParameters(
            KeyedCollection<string, ConfigParameter> configParameters,
            ConfigParameterPage page)
        {
            var list = new List<ConfigParameterDefinition>();
            if (page == ConfigParameterPage.Connectivity)
            {
                list.Add(ConfigParameterDefinition.CreateStringParameter(CP_USER, "", "IDM"));
                list.Add(ConfigParameterDefinition.CreateEncryptedStringParameter(CP_PASSWORD, ""));
            }
            return list;
        }

        public ParameterValidationResult ValidateConfigParameters(
            KeyedCollection<string, ConfigParameter> configParameters,
            ConfigParameterPage page)
        {
            return new ParameterValidationResult();
        }

        public Schema GetSchema(KeyedCollection<string, ConfigParameter> configParameters)
        {
            var schema = Schema.Create();
            var problems = new List<string>();
            Log("INFO", "GetSchema: " + UisConfig.All.Length + " object types");

            using (var conn = BuildConnection(configParameters))
            {
                try
                {
                    conn.Open();
                }
                catch (Exception ex)
                {
                    Log("ERROR", "GetSchema Open: " + ex);
                    throw new InvalidOperationException("Pripojeni k Oracle selhalo: " + ex.Message, ex);
                }

                string sessionUser = Scalar(conn, "SELECT USER FROM DUAL");
                Log("INFO", "GetSchema: pripojeno jako " + sessionUser);
                var viewOverrides = ReadViewOverrides();
                string catalog = null;

                foreach (var cfg in UisConfig.All)
                {
                    try
                    {
                        string qualified = ResolveView(conn, cfg, viewOverrides);
                        if (qualified == null)
                        {
                            if (catalog == null) catalog = LogVisibleCatalog(conn);
                            string msg = sessionUser + " nevidi " + cfg.View +
                                " (ORA-00942). " + catalog;
                            Log("WARN", "GetSchema [" + cfg.Name + "]: " + msg);
                            problems.Add(cfg.Name + ": " + msg);
                            continue;
                        }

                        SplitQualified(qualified, out string owner, out string table);
                        var schemaType = MmsSchemaType.Create(cfg.Name, false);
                        bool anchorFound = false;
                        var columns = LoadColumns(conn, owner, table);

                        foreach (var column in columns)
                        {
                            if (UisConfig.IsPasswordColumn(column.Name))
                            {
                                Log("INFO", "GetSchema [" + cfg.Name + "]: sloupec '" + column.Name + "' preskocen (heslo)");
                                continue;
                            }

                            bool isWholeNumber = (column.DataType.StartsWith("NUMBER") || column.DataType == "INTEGER")
                                && column.Scale == 0;
                            var attrType = isWholeNumber ? AttributeType.Integer : AttributeType.String;
                            schemaType.Attributes.Add(SchemaAttribute.CreateSingleValuedAttribute(column.Name, attrType));
                            if (column.Name.Equals(cfg.AnchorColumn, StringComparison.OrdinalIgnoreCase))
                                anchorFound = true;
                        }

                        if (schemaType.Attributes.Count == 0)
                        {
                            string msg = sessionUser + " vidi 0 sloupcu ve " + owner + "." + table + ". " +
                                DescribeMissing(conn, table);
                            Log("WARN", "GetSchema: " + msg);
                            problems.Add(cfg.Name + ": " + msg);
                            continue;
                        }
                        if (!anchorFound)
                        {
                            string msg = "anchor '" + cfg.AnchorColumn + "' nenalezen v " + owner + "." + table;
                            Log("WARN", "GetSchema: " + msg);
                            problems.Add(cfg.Name + ": " + msg);
                            continue;
                        }

                        if (cfg.ReferenceAttribute != null)
                        {
                            schemaType.Attributes.Add(SchemaAttribute.CreateSingleValuedAttribute(
                                cfg.ReferenceAttribute, AttributeType.Reference));
                        }

                        schema.Types.Add(schemaType);
                        Log("INFO", "GetSchema OK: " + cfg.Name + " (" + owner + "." + table + ") - " +
                            schemaType.Attributes.Count + " atributu, anchor=" + cfg.AnchorColumn);
                    }
                    catch (Exception ex)
                    {
                        Log("ERROR", "GetSchema [" + cfg.Name + "]: " + ex.Message);
                        problems.Add(cfg.Name + ": " + ex.Message);
                    }
                }
            }

            if (schema.Types.Count == 0)
                throw new InvalidOperationException(
                    "Zadny object type nebyl nacten. " + string.Join(" | ", problems));

            return schema;
        }

        private sealed class ColumnDef
        {
            public string Name;
            public string DataType;
            public int Scale;
        }

        private static List<ColumnDef> LoadColumns(OracleConnection conn, string owner, string table)
        {
            var columns = ReadCatalog(conn, owner, table);
            if (columns.Count > 0) return columns;

            Log("WARN", "ALL_TAB_COLUMNS je prazdne pro " + owner + "." + table + ", ctu SELECT * WHERE 1=0");
            using (var cmd = new OracleCommand("SELECT * FROM " + owner + "." + table + " WHERE 1 = 0", conn))
            using (var rdr = cmd.ExecuteReader(System.Data.CommandBehavior.SchemaOnly))
            {
                var schemaTable = rdr.GetSchemaTable();
                for (int i = 0; i < rdr.FieldCount; i++)
                {
                    int scale = -1;
                    if (schemaTable != null && schemaTable.Columns.Contains("NumericScale") && i < schemaTable.Rows.Count)
                    {
                        object scaleValue = schemaTable.Rows[i]["NumericScale"];
                        if (scaleValue != null && scaleValue != DBNull.Value)
                            scale = Convert.ToInt32(scaleValue);
                    }
                    columns.Add(new ColumnDef
                    {
                        Name = rdr.GetName(i).ToUpper(),
                        DataType = (rdr.GetDataTypeName(i) ?? "").ToUpper(),
                        Scale = scale
                    });
                }
            }
            return columns;
        }

        private static List<ColumnDef> ReadCatalog(OracleConnection conn, string owner, string table)
        {
            var columns = new List<ColumnDef>();
            using (var cmd = new OracleCommand(
                "SELECT COLUMN_NAME, DATA_TYPE, DATA_SCALE FROM ALL_TAB_COLUMNS " +
                "WHERE OWNER = :owner AND TABLE_NAME = :tbl ORDER BY COLUMN_ID", conn))
            {
                cmd.BindByName = true;
                var pOwner = cmd.Parameters.Add("owner", OracleDbType.Varchar2);
                pOwner.Value = owner;
                pOwner.Size = 128;
                var pTable = cmd.Parameters.Add("tbl", OracleDbType.Varchar2);
                pTable.Value = table;
                pTable.Size = 128;

                using (var rdr = cmd.ExecuteReader())
                {
                    while (rdr.Read())
                    {
                        int scale = -1;
                        if (!rdr.IsDBNull(2)) scale = Convert.ToInt32(rdr.GetDecimal(2));
                        columns.Add(new ColumnDef
                        {
                            Name = rdr.GetString(0),
                            DataType = rdr.GetString(1).ToUpper(),
                            Scale = scale
                        });
                    }
                }
            }
            return columns;
        }

        private static string ResolveView(
            OracleConnection conn,
            ObjectTypeConfig cfg,
            Dictionary<string, string> overrides)
        {
            if (overrides != null && overrides.TryGetValue(cfg.Name, out string configured) &&
                !string.IsNullOrWhiteSpace(configured))
            {
                Log("INFO", cfg.Name + ": view z DataSource.txt = " + configured);
                return configured.Trim();
            }

            cfg.SplitView(out string owner, out string table);
            if (ReadCatalog(conn, owner, table).Count > 0)
                return owner + "." + table;

            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var found = new List<string>();
            using (var cmd = new OracleCommand(
                "SELECT OWNER, OBJECT_NAME FROM ALL_OBJECTS " +
                "WHERE OBJECT_NAME = :tbl AND OBJECT_TYPE IN ('TABLE','VIEW') " +
                "UNION " +
                "SELECT TABLE_OWNER, TABLE_NAME FROM ALL_SYNONYMS WHERE SYNONYM_NAME = :syn", conn))
            {
                cmd.BindByName = true;
                var pTable = cmd.Parameters.Add("tbl", OracleDbType.Varchar2);
                pTable.Value = table;
                pTable.Size = 128;
                var pSyn = cmd.Parameters.Add("syn", OracleDbType.Varchar2);
                pSyn.Value = table;
                pSyn.Size = 128;
                using (var rdr = cmd.ExecuteReader())
                {
                    while (rdr.Read())
                    {
                        string altOwner = rdr.GetString(0);
                        string altName = rdr.GetString(1);
                        string key = altOwner + "." + altName;
                        if (!seen.Add(key)) continue;
                        found.Add(key);
                    }
                }
            }

            foreach (string candidate in found)
            {
                SplitQualified(candidate, out string altOwner, out string altName);
                if (ReadCatalog(conn, altOwner, altName).Count > 0)
                {
                    Log("INFO", cfg.Name + ": " + owner + "." + table + " neni, pouzivam " + candidate);
                    return candidate;
                }
            }
            return null;
        }

        private static string LogVisibleCatalog(OracleConnection conn)
        {
            try
            {
                int own = LogQuery(conn,
                    "SELECT OBJECT_TYPE || ' ' || OBJECT_NAME FROM (" +
                    "SELECT OBJECT_TYPE, OBJECT_NAME FROM USER_OBJECTS " +
                    "WHERE OBJECT_TYPE IN ('TABLE','VIEW','SYNONYM') " +
                    "ORDER BY OBJECT_TYPE, OBJECT_NAME) WHERE ROWNUM <= 80",
                    "Vlastni objekt");

                int schemas = LogQuery(conn,
                    "SELECT OWNER || ' ' || OBJECT_TYPE || ' ' || CNT FROM (" +
                    "SELECT OWNER, OBJECT_TYPE, COUNT(*) CNT FROM ALL_OBJECTS " +
                    "WHERE OBJECT_TYPE IN ('TABLE','VIEW','SYNONYM') " +
                    "AND OWNER NOT IN (" + OracleBuiltInOwners + ") " +
                    "GROUP BY OWNER, OBJECT_TYPE ORDER BY OWNER, OBJECT_TYPE)",
                    "Schema");

                int names = 0;
                if (schemas > 0)
                {
                    names = LogQuery(conn,
                        "SELECT OWNER || ' ' || OBJECT_TYPE || ' ' || OBJECT_NAME FROM (" +
                        "SELECT OWNER, OBJECT_TYPE, OBJECT_NAME FROM ALL_OBJECTS " +
                        "WHERE OBJECT_TYPE IN ('TABLE','VIEW','SYNONYM') " +
                        "AND OWNER NOT IN (" + OracleBuiltInOwners + ") " +
                        "ORDER BY OWNER, OBJECT_TYPE, OBJECT_NAME) WHERE ROWNUM <= 150",
                        "Objekt");
                }

                if (own == 0 && schemas == 0)
                {
                    string total = Scalar(conn,
                        "SELECT COUNT(*) FROM ALL_OBJECTS WHERE OBJECT_TYPE IN ('TABLE','VIEW','SYNONYM')");
                    Log("WARN", "Mimo systemova schemata neni videt zadny objekt. ALL_OBJECTS celkem " + total);
                    return "mimo systemova schemata zadny objekt (ALL_OBJECTS celkem " + total + ").";
                }

                return "vlastnich " + own + ", schemat " + schemas + ", vypsanych objektu " + names + ". Seznam je v logu.";
            }
            catch (Exception ex)
            {
                Log("ERROR", "Katalog objektu: " + ex.Message);
                return "katalog se nepodarilo nacist: " + ex.Message;
            }
        }

        private const string OracleBuiltInOwners =
            "'SYS','SYSTEM','XDB','CTXSYS','MDSYS','OLAPSYS','ORDSYS','WMSYS','OUTLN','DBSNMP'," +
            "'APPQOSSYS','GSMADMIN_INTERNAL','DVSYS','AUDSYS','OJVMSYS','LBACSYS','ORDDATA'," +
            "'SI_INFORMTN_SCHEMA','DBSFWUSER','REMOTE_SCHEDULER_AGENT','GGSYS','GSMCATUSER','GSMUSER'";

        private static int LogQuery(OracleConnection conn, string sql, string label)
        {
            int count = 0;
            using (var cmd = new OracleCommand(sql, conn))
            using (var rdr = cmd.ExecuteReader())
            {
                while (rdr.Read())
                {
                    count++;
                    string text = "";
                    for (int i = 0; i < rdr.FieldCount; i++)
                    {
                        if (i > 0) text += " ";
                        text += rdr.IsDBNull(i) ? "" : rdr.GetValue(i).ToString();
                    }
                    Log("WARN", label + ": " + text);
                }
            }
            if (count == 0) Log("WARN", label + ": nic");
            return count;
        }

        private static void SplitQualified(string qualified, out string owner, out string table)
        {
            int dot = qualified.IndexOf('.');
            if (dot > 0)
            {
                owner = qualified.Substring(0, dot).Trim().ToUpper();
                table = qualified.Substring(dot + 1).Trim().ToUpper();
            }
            else
            {
                owner = "UIS";
                table = qualified.Trim().ToUpper();
            }
        }

        private static string DescribeMissing(OracleConnection conn, string table)
        {
            try
            {
                var found = new List<string>();
                using (var cmd = new OracleCommand(
                    "SELECT OWNER, OBJECT_TYPE FROM ALL_OBJECTS WHERE OBJECT_NAME = :tbl AND ROWNUM <= 8", conn))
                {
                    cmd.BindByName = true;
                    var p = cmd.Parameters.Add("tbl", OracleDbType.Varchar2);
                    p.Value = table;
                    p.Size = 128;
                    using (var rdr = cmd.ExecuteReader())
                    {
                        while (rdr.Read())
                            found.Add(rdr.GetString(0) + " " + rdr.GetString(1));
                    }
                }
                if (found.Count == 0) return "V ALL_OBJECTS objekt " + table + " neni.";
                return "V ALL_OBJECTS: " + string.Join(", ", found) + ".";
            }
            catch (Exception ex)
            {
                return ex.Message;
            }
        }

        private static string Scalar(OracleConnection conn, string sql)
        {
            using (var cmd = new OracleCommand(sql, conn))
            {
                object value = cmd.ExecuteScalar();
                return value == null || value == DBNull.Value ? "" : value.ToString();
            }
        }

        public OpenImportConnectionResults OpenImportConnection(
            KeyedCollection<string, ConfigParameter> configParameters,
            Schema types,
            OpenImportConnectionRunStep runStep)
        {
            _config = configParameters;
            _conn = BuildConnection(configParameters);
            _conn.Open();

            var step = runStep.StepPartition;
            string partitionName = step == null ? null : step.Name;
            string partitionDn = step == null ? null : step.DN;
            Guid partitionId = step == null ? Guid.Empty : step.Identifier;
            Log("INFO", "OpenImportConnection: Name='" + partitionName + "' DN='" + partitionDn + "' Id=" + partitionId);

            _currentConfig = ResolvePartition(partitionName, partitionDn, partitionId);
            string qualified = ResolveView(_conn, _currentConfig, ReadViewOverrides());
            if (qualified == null)
                throw new InvalidOperationException(
                    "Ucet nevidi " + _currentConfig.View + ". " + LogVisibleCatalog(_conn));
            Log("INFO", "OpenImportConnection: partition=" + _currentConfig.Name + " view=" + qualified +
                " anchor=" + _currentConfig.AnchorColumn);

            var cmd = new OracleCommand("SELECT * FROM " + qualified, _conn);
            _reader = cmd.ExecuteReader();
            return new OpenImportConnectionResults();
        }

        public GetImportEntriesResults GetImportEntries(GetImportEntriesRunStep runStep)
        {
            var entries = new List<CSEntryChange>();
            int count = 0;
            int skipped = 0;
            bool firstLog = true;
            string anchorCol = _currentConfig.AnchorColumn;

            while (count < ImportDefaultPageSize && _reader.Read())
            {
                var entry = CSEntryChange.Create();
                entry.ObjectModificationType = ObjectModificationType.Add;
                entry.ObjectType = _currentConfig.Name;

                string anchorValue = null;
                string referenceValue = null;

                for (int i = 0; i < _reader.FieldCount; i++)
                {
                    if (_reader.IsDBNull(i)) continue;
                    string col = _reader.GetName(i).ToUpper();
                    if (UisConfig.IsPasswordColumn(col)) continue;

                    string value = TrimLoaded(_reader.GetValue(i));
                    if (value == null) continue;

                    if (col.Equals(anchorCol, StringComparison.OrdinalIgnoreCase))
                        anchorValue = value;

                    if (_currentConfig.ReferenceColumn != null &&
                        col.Equals(_currentConfig.ReferenceColumn, StringComparison.OrdinalIgnoreCase))
                        referenceValue = value;

                    entry.AttributeChanges.Add(AttributeChange.CreateAttributeAdd(col, value));
                }

                if (string.IsNullOrEmpty(anchorValue))
                {
                    skipped++;
                    count++;
                    continue;
                }

                entry.DN = ToDN(anchorValue);

                if (!string.IsNullOrEmpty(referenceValue) && _currentConfig.ReferenceAttribute != null)
                {
                    entry.AttributeChanges.Add(AttributeChange.CreateAttributeAdd(
                        _currentConfig.ReferenceAttribute,
                        "CN=" + referenceValue + ",CN=" + _currentConfig.ReferenceObjectType));
                }

                if (firstLog)
                {
                    firstLog = false;
                    Log("INFO", "GetImportEntries [" + _currentConfig.Name + "]: prvni anchor='" + anchorValue + "'");
                }

                entries.Add(entry);
                count++;
            }

            if (skipped > 0)
                Log("WARN", "GetImportEntries [" + _currentConfig.Name + "]: preskoceno " + skipped +
                    " radku bez anchoru " + anchorCol);

            return new GetImportEntriesResults
            {
                CSEntries = entries,
                MoreToImport = count == ImportDefaultPageSize
            };
        }

        public CloseImportConnectionResults CloseImportConnection(CloseImportConnectionRunStep runStep)
        {
            Log("INFO", "CloseImportConnection: partition=" + (_currentConfig == null ? "" : _currentConfig.Name));
            if (_reader != null)
            {
                _reader.Close();
                _reader.Dispose();
                _reader = null;
            }
            if (_conn != null)
            {
                _conn.Dispose();
                _conn = null;
            }
            return new CloseImportConnectionResults(string.Empty);
        }

        /// <summary>
        /// Partition se pozna podle GUID (MIM ho posila i kdyz je Name prazdne),
        /// pak podle Name a DN. Bez partition import nespadne na jiny object type.
        /// </summary>
        private static ObjectTypeConfig ResolvePartition(string partitionName, string partitionDn, Guid partitionId)
        {
            ObjectTypeConfig byId = UisConfig.FindByGuid(partitionId);
            if (byId != null) return byId;

            foreach (string candidate in new[] { partitionName, partitionDn })
            {
                if (string.IsNullOrWhiteSpace(candidate)) continue;
                string name = candidate.Trim();
                ObjectTypeConfig cfg = UisConfig.FindByName(name);
                if (cfg != null) return cfg;

                if (name.StartsWith("CN=", StringComparison.OrdinalIgnoreCase))
                {
                    string rest = name.Substring(3);
                    int comma = rest.IndexOf(',');
                    if (comma >= 0) rest = rest.Substring(0, comma);
                    cfg = UisConfig.FindByName(rest.Trim());
                    if (cfg != null) return cfg;
                }
            }

            Log("ERROR", "ResolvePartition: Name='" + partitionName + "' DN='" + partitionDn + "' Id=" + partitionId);
            throw new InvalidOperationException(
                "Neznama partition '" + partitionName + "' / '" + partitionDn + "' / " + partitionId +
                ". V MA udelej Refresh Partitions a v run profilu vyber Student, Studium, Doktorand nebo DoktStudium.");
        }

        private static readonly string ConfigDirectory =
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "UisECMA2MA");

        private static readonly string DataSourceFile = Path.Combine(ConfigDirectory, "DataSource.txt");

        private static readonly string LogFile = Path.Combine(ConfigDirectory, "UisECMA2MA.log");

        private static void Log(string level, string message)
        {
            try
            {
                string dir = Path.GetDirectoryName(LogFile);
                if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);
                File.AppendAllText(LogFile,
                    DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + " [" + level + "] " + message + Environment.NewLine);
            }
            catch { }
        }

        private string ToDN(string anchorValue)
            => "CN=" + anchorValue + ",CN=" + _currentConfig.Name;

        private static string ResolveDataSource()
        {
            if (!Directory.Exists(ConfigDirectory)) Directory.CreateDirectory(ConfigDirectory);
            if (!File.Exists(DataSourceFile))
            {
                File.WriteAllText(DataSourceFile,
                    "# Oracle Data Source pro UisECMA2MA. Prazdne radky a radky zacinajici # se ignoruji." + Environment.NewLine +
                    UIS_DATASOURCE + Environment.NewLine);
                Log("INFO", "Vytvoren " + DataSourceFile);
            }

            var text = new System.Text.StringBuilder();
            foreach (string raw in File.ReadAllLines(DataSourceFile))
            {
                string line = raw.Trim();
                if (line.Length == 0 || line.StartsWith("#", StringComparison.Ordinal)) continue;
                if (IsViewOverride(line)) continue;
                text.Append(line);
            }

            string value = text.ToString();
            if (value.IndexOf("SERVICE_NAME=", StringComparison.OrdinalIgnoreCase) < 0)
                throw new InvalidOperationException(
                    "V " + DataSourceFile + " chybi uplny descriptor (SERVICE_NAME).");

            Log("INFO", "DataSource z " + DataSourceFile);
            return value;
        }

        private static Dictionary<string, string> ReadViewOverrides()
        {
            var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            if (!File.Exists(DataSourceFile)) return map;
            foreach (string raw in File.ReadAllLines(DataSourceFile))
            {
                string line = raw.Trim();
                if (!IsViewOverride(line)) continue;
                int eq = line.IndexOf('=');
                map[line.Substring(0, eq).Trim()] = line.Substring(eq + 1).Trim();
            }
            return map;
        }

        private static bool IsViewOverride(string line)
        {
            int eq = line.IndexOf('=');
            if (eq <= 0) return false;
            return UisConfig.FindByName(line.Substring(0, eq).Trim()) != null;
        }

        private static string GetParam(KeyedCollection<string, ConfigParameter> cfg, string key, string defaultValue)
        {
            return cfg != null && cfg.Contains(key) ? cfg[key].Value : defaultValue;
        }

        private static string TrimLoaded(object value)
        {
            if (value == null || value == DBNull.Value) return null;
            string text = value.ToString();
            if (string.IsNullOrWhiteSpace(text)) return null;
            return text.Trim();
        }

        private OracleConnection BuildConnection(KeyedCollection<string, ConfigParameter> cfg)
        {
            string pass = cfg != null && cfg.Contains(CP_PASSWORD) && cfg[CP_PASSWORD].SecureValue != null
                ? new System.Net.NetworkCredential("", cfg[CP_PASSWORD].SecureValue).Password
                : GetParam(cfg, CP_PASSWORD, "");

            string dataSource = ResolveDataSource();
            string connStr =
                "Data Source=" + dataSource + ";" +
                "User Id=" + GetParam(cfg, CP_USER, "IDM") + ";Password=" + pass + ";" +
                "Pooling=false;Connection Timeout=30;";

            return new OracleConnection(connStr);
        }
    }
}
