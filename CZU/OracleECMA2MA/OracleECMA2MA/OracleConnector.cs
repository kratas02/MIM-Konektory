using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using Microsoft.MetadirectoryServices;
using Oracle.ManagedDataAccess.Client;

// Alias pro SchemaType - vyhne se konfliktu s System.Data.SchemaType
using MmsSchemaType = Microsoft.MetadirectoryServices.SchemaType;

namespace OracleECMA2MA
{
    public class OracleConnector :
        IMAExtensible2GetCapabilities,
        IMAExtensible2GetSchema,
        IMAExtensible2GetParameters,
        IMAExtensible2GetPartitions,
        IMAExtensible2CallImport
    {
        // ── Nazvy konfiguracnich parametru ────────────────────────────────────────
        private const string CP_DATASOURCE  = "DataSource";
        private const string CP_USER        = "Username";
        private const string CP_PASSWORD    = "Password";
        private const string CP_DELTA_TABLE = "DeltaTable";

        // Konfigurace PPV – pouziva se pro krizove reference z Person (PRIMARNI_PPV, AKTIVNI_PPV)
        private static readonly ObjectTypeConfig PpvConfig = MagionConfig.FindByName("PPV");

        // Konfigurace BaseUnit – pouziva se pro krizovou referenci z Person (UTVAR)
        private static readonly ObjectTypeConfig BaseUnitConfig = MagionConfig.FindByName("BaseUnit");

        // Konfigurace Person – pouziva se pro referenci "member" z Komise/PracMista
        private static readonly ObjectTypeConfig PersonConfig = MagionConfig.FindByName("Person");

        // Magion Oracle - TNS descriptor testovaci prostredi
        private const string MAGION_DATASOURCE =
            "(DESCRIPTION=" +
              "(ADDRESS_LIST=" +
                "(ADDRESS=(COMMUNITY=tcp.world)(PROTOCOL=TCP)(Host=magi.oradb-t.czu.cz)(Port=1523))" +
              ")" +
              "(CONNECT_DATA=(SERVER=DEDICATED)(SERVICE_NAME=MACV.oradb.czu.cz))" +
            ")";

        // ── Stav importu ──────────────────────────────────────────────────────────
        private KeyedCollection<string, ConfigParameter> _config;
        private ObjectTypeConfig _currentConfig;
        private OracleConnection _conn;

        // Full import state
        private OracleDataReader _reader;

        // Delta import state
        private bool             _isDelta;
        private List<DeltaItem>  _deltaItems;
        private int              _deltaIndex;

        private class DeltaItem
        {
            public string Uic;
            public string ActionType;  // add, modify, delete
            public string ObjectDn;    // OBJECT_DN z IDM_CHANGES
        }

        // ── IMAExtensible2CallImport ──────────────────────────────────────────────

        public int ImportDefaultPageSize => 500;
        public int ImportMaxPageSize     => 1000;

        // ── Capabilities ─────────────────────────────────────────────────────────

        public MACapabilities Capabilities => new MACapabilities
        {
            SupportImport          = true,
            SupportExport          = false,
            DeltaImport            = true,
            SupportPartitions      = true,
            DistinguishedNameStyle = MADistinguishedNameStyle.Ldap,
            IsDNAsAnchor           = true,
            ConcurrentOperation    = false
        };

        // ── Partitions ────────────────────────────────────────────────────────────

        public IList<Partition> GetPartitions(KeyedCollection<string, ConfigParameter> configParameters)
        {
            var list = new List<Partition>();
            foreach (var cfg in MagionConfig.All)
            {
                list.Add(Partition.Create(
                    MagionConfig.NameToGuid(cfg.Name),
                    $"CN={cfg.Name}",   // LDAP-format DN (required by Ldap DN style)
                    cfg.Name));         // display name
            }
            Log("INFO", $"GetPartitions: {list.Count} partitions");
            return list;
        }

        // ── Parametry konfigurace ─────────────────────────────────────────────────

        public IList<ConfigParameterDefinition> GetConfigParameters(
            KeyedCollection<string, ConfigParameter> configParameters,
            ConfigParameterPage page)
        {
            var list = new List<ConfigParameterDefinition>();

            if (page == ConfigParameterPage.Connectivity)
            {
                list.Add(ConfigParameterDefinition.CreateStringParameter(
                    CP_DATASOURCE, "", MAGION_DATASOURCE));
                list.Add(ConfigParameterDefinition.CreateStringParameter(
                    CP_USER, "", "IDM"));
                list.Add(ConfigParameterDefinition.CreateEncryptedStringParameter(
                    CP_PASSWORD, ""));
            }
            else if (page == ConfigParameterPage.Global)
            {
                list.Add(ConfigParameterDefinition.CreateStringParameter(
                    CP_DELTA_TABLE, "", MagionConfig.DefaultDeltaTable));
            }

            return list;
        }

        public ParameterValidationResult ValidateConfigParameters(
            KeyedCollection<string, ConfigParameter> configParameters,
            ConfigParameterPage page) => new ParameterValidationResult();

        // ── Schema ────────────────────────────────────────────────────────────────

        public Schema GetSchema(KeyedCollection<string, ConfigParameter> configParameters)
        {
            var schema   = Schema.Create();
            string dbUser = GetParam(configParameters, CP_USER, "IDM").ToUpper();

            Log("INFO", $"GetSchema: nacitam schemata pro {MagionConfig.All.Length} object types");

            using (var conn = BuildConnection(configParameters))
            {
                conn.Open();

                foreach (var cfg in MagionConfig.All)
                {
                    // Chyba u jednoho object type (napr. spatny nazev sloupce, duplicitni atribut)
                    // nesmi shodit GetSchema pro VSECHNY ostatni typy – proto try/catch per-iteraci.
                    try
                    {
                        cfg.SplitView(dbUser, out string owner, out string table);

                        // Pokud je definovano DistinctColumns, omez schema jen na tyto sloupce
                        // (napr. Komise/PracMista: jeden objekt = jedna komise/pracovni misto,
                        // ne jeden radek clenstvi)
                        string columnFilter = cfg.DistinctColumns != null
                            ? " AND COLUMN_NAME IN ('" + string.Join("','", cfg.DistinctColumns) + "')"
                            : "";

                        var cmd = new OracleCommand(
                            "SELECT COLUMN_NAME, DATA_TYPE, DATA_SCALE FROM ALL_TAB_COLUMNS " +
                            "WHERE OWNER = :owner AND TABLE_NAME = :tbl" + columnFilter + " ORDER BY COLUMN_ID", conn);
                        cmd.Parameters.Add("owner", OracleDbType.Varchar2).Value = owner;
                        cmd.Parameters.Add("tbl",   OracleDbType.Varchar2).Value = table;

                        var schemaType = MmsSchemaType.Create(cfg.Name, false);

                        // Sleduje vsechny realne sloupce view – pouziva se k tomu, aby se predesli
                        // pokusu pridat vypocitany/referencni atribut se stejnym nazvem jako existujici
                        // sloupec (Attributes.Add() na duplicitni klic vyhodi vyjimku a shodi cely
                        // GetSchema => "Unable to retrieve schema" v MIM pro VSECHNY object types).
                        var rawColumns = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

                        using (var rdr = cmd.ExecuteReader())
                        {
                            while (rdr.Read())
                            {
                                string col       = rdr.GetString(0);
                                string dbType    = rdr.GetString(1).ToUpper();
                                int    dataScale = rdr.IsDBNull(2) ? -1 : rdr.GetInt32(2);

                                // Integer pouze pro NUMBER bez desetinnych mist (DATA_SCALE = 0)
                                // Desetinna cisla (VYSE_UVAZKU = 0.5 apod.) jako String
                                bool isWholeNumber = (dbType.StartsWith("NUMBER") || dbType == "INTEGER")
                                                     && dataScale == 0;
                                var attrType = isWholeNumber ? AttributeType.Integer : AttributeType.String;

                                schemaType.Attributes.Add(
                                    SchemaAttribute.CreateSingleValuedAttribute(col, attrType));
                                rawColumns.Add(col);
                            }
                        }

                        // Anchor muze byt kombinovany z vice sloupcu (viz Komise: RK_ID + RK_KOD) –
                        // vsechny musi existovat jako realne sloupce view.
                        bool anchorFound = true;
                        foreach (string anchorCol in cfg.AnchorColumns)
                        {
                            if (!rawColumns.Contains(anchorCol))
                            {
                                anchorFound = false;
                                break;
                            }
                        }

                        // Prida vypocitany/referencni atribut, ale jen pokud stejny nazev jiz neni
                        // pouzity jako realny sloupec view (viz komentar u rawColumns vyse).
                        void AddComputedAttribute(string name, AttributeType type, bool multiValued = false)
                        {
                            if (rawColumns.Contains(name))
                            {
                                Log("WARN", $"GetSchema [{cfg.Name}]: vypocitany atribut '{name}' preskocen - " +
                                    "stejny nazev uz existuje jako realny sloupec view");
                                return;
                            }
                            schemaType.Attributes.Add(multiValued
                                ? SchemaAttribute.CreateMultiValuedAttribute(name, type)
                                : SchemaAttribute.CreateSingleValuedAttribute(name, type));
                        }

                        // Pridej vypocitany atribut AKTIVNI pro PPV typy – jen pokud neni uz v tabulce
                        if (cfg.Name == "PPV" || cfg.Name == "PPVRelation")
                        {
                            AddComputedAttribute("AKTIVNI", AttributeType.String);
                        }

                        // Pridej vypocitane referencni atributy pro Person – vazba na PPV a BaseUnit
                        // (PRIMARNI_PPV, AKTIVNI_PPV, AKTIVNI, UTVAR) – viz AddPpvCrossReferences()
                        // Pozn.: AKTIVNI je String ("0"/"1"), ne Boolean – nekterym verzim/implementacim
                        // ECMA2 (vc. FIM/MIM) dela Boolean atribut ve schematu problemy pri Refresh
                        // Interfaces (0x80231343/0x80231344), i kdyz neni anchor ani multivalued.
                        if (cfg.Name == "Person")
                        {
                            AddComputedAttribute("PRIMARNI_PPV", AttributeType.Reference);
                            AddComputedAttribute("AKTIVNI_PPV", AttributeType.Reference, multiValued: true);
                            AddComputedAttribute("AKTIVNI", AttributeType.String);
                            AddComputedAttribute("UTVAR", AttributeType.Reference);
                        }

                        // Pridej multivalued referencni atribut "member" pro distinct object types
                        // (Komise, PracMista) – reference na Person podle UIC (viz AddMemberReferences())
                        if (cfg.DistinctColumns != null)
                        {
                            AddComputedAttribute("member", AttributeType.Reference, multiValued: true);
                        }

                        if (schemaType.Attributes.Count == 0)
                        {
                            Log("WARN", $"GetSchema: view '{owner}.{table}' nenalezeno nebo prazdne - preskoceno");
                            continue;
                        }

                        if (!anchorFound)
                        {
                            Log("WARN", $"GetSchema: anchor '{string.Join("+", cfg.AnchorColumns)}' nenalezen v '{owner}.{table}' - preskoceno");
                            continue;
                        }

                        schema.Types.Add(schemaType);
                        Log("INFO", $"GetSchema OK: {cfg.Name} ({owner}.{table}) - {schemaType.Attributes.Count} atributu, anchor='{string.Join("+", cfg.AnchorColumns)}'");
                    }
                    catch (Exception ex)
                    {
                        Log("ERROR", $"GetSchema [{cfg.Name}]: object type preskocen kvuli chybe: {ex}");
                    }
                }
            }

            if (schema.Types.Count == 0)
                throw new InvalidOperationException(
                    "Zadny object type nebyl nacten. Overit pripojeni a pristupova prava.");

            return schema;
        }

        // ── Import ────────────────────────────────────────────────────────────────

        public OpenImportConnectionResults OpenImportConnection(
            KeyedCollection<string, ConfigParameter> configParameters,
            Schema types,
            OpenImportConnectionRunStep runStep)
        {
            _config = configParameters;
            _conn   = BuildConnection(configParameters);
            _conn.Open();

            // Urcit aktualni partition
            string partitionName = runStep.StepPartition?.Name;
            _currentConfig = string.IsNullOrEmpty(partitionName)
                ? MagionConfig.All[0]
                : MagionConfig.FindByName(partitionName) ?? MagionConfig.All[0];

            _isDelta = runStep.ImportType == OperationType.Delta && _currentConfig.SupportDelta;

            Log("INFO", $"OpenImportConnection: partition={_currentConfig.Name} view={_currentConfig.View} " +
                $"anchor={string.Join("+", _currentConfig.AnchorColumns)} importType={runStep.ImportType} delta={_isDelta}");

            if (_isDelta)
                OpenDeltaImport(configParameters);
            else
                OpenFullImport();

            return new OpenImportConnectionResults();
        }

        private void OpenFullImport()
        {
            string sql = _currentConfig.DistinctColumns != null
                ? $"SELECT DISTINCT {string.Join(", ", _currentConfig.DistinctColumns)} FROM {_currentConfig.View}"
                : $"SELECT * FROM {_currentConfig.View}";

            var cmd = new OracleCommand(sql, _conn);
            _reader = cmd.ExecuteReader(System.Data.CommandBehavior.Default);
            Log("INFO", $"OpenFullImport: {sql}");
        }

        private void OpenDeltaImport(KeyedCollection<string, ConfigParameter> cfg)
        {
            string deltaTable  = GetParam(cfg, CP_DELTA_TABLE, MagionConfig.DefaultDeltaTable);
            string objectType  = _currentConfig.DeltaObjectType;

            _deltaItems = new List<DeltaItem>();
            _deltaIndex = 0;

            try
            {
                // Nacti vsechny neudalosti ze STATUS='N' pro tento object type
                var cmd = new OracleCommand(
                    $"SELECT UIC, ACTION_TYPE, OBJECT_DN " +
                    $"FROM {deltaTable} " +
                    $"WHERE STATUS = 'N' AND OBJECT_TYPE = :ot", _conn);
                cmd.Parameters.Add("ot", OracleDbType.Varchar2).Value = objectType;

                using (var rdr = cmd.ExecuteReader())
                {
                    while (rdr.Read())
                    {
                        _deltaItems.Add(new DeltaItem
                        {
                            Uic        = rdr.IsDBNull(0) ? null : rdr.GetString(0),
                            ActionType = rdr.IsDBNull(1) ? "modify" : rdr.GetString(1).ToLower(),
                            ObjectDn   = rdr.IsDBNull(2) ? null : rdr.GetString(2),
                        });
                    }
                }

                Log("INFO", $"OpenDeltaImport: {_deltaItems.Count} zmen v '{deltaTable}' pro OBJECT_TYPE='{objectType}'");
            }
            catch (Exception ex)
            {
                Log("WARN", $"OpenDeltaImport: nelze nacist delta tabulku '{deltaTable}': {ex.Message}. " +
                    "Pokracuji s prazdnym delta.");
                _deltaItems = new List<DeltaItem>();
            }
        }

        public GetImportEntriesResults GetImportEntries(GetImportEntriesRunStep runStep)
        {
            return _isDelta
                ? GetDeltaEntries()
                : GetFullEntries();
        }

        // ── Full Import ───────────────────────────────────────────────────────────

        private GetImportEntriesResults GetFullEntries()
        {
            var      entries       = new List<CSEntryChange>();
            string[] anchorColumns = _currentConfig.AnchorColumns;
            string   objType       = _currentConfig.Name;
            int      count         = 0;
            int      skipped       = 0;
            bool     firstLog      = true;

            while (count < ImportDefaultPageSize && _reader.Read())
            {
                var entry = CSEntryChange.Create();
                entry.ObjectModificationType = ObjectModificationType.Add;
                entry.ObjectType             = objType;

                // Anchor muze byt slozeny z vice sloupcu (napr. Komise: RK_ID + RK_KOD) – hodnoty
                // jednotlivych anchor sloupcu se sbiraji zvlast a DN se sklada az po nacteni radku.
                var    anchorValues = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                string vystupValue  = null;
                string zaklPpValue  = null;
                bool   aktivniAdded = false;

                for (int i = 0; i < _reader.FieldCount; i++)
                {
                    if (_reader.IsDBNull(i)) continue;
                    string col   = _reader.GetName(i).ToUpper();
                    string value = _reader.GetValue(i).ToString();
                    if (string.IsNullOrWhiteSpace(value)) continue;

                    bool isAnchorCol = false;
                    foreach (string ac in anchorColumns)
                    {
                        if (col.Equals(ac, StringComparison.OrdinalIgnoreCase))
                        {
                            isAnchorCol = true;
                            break;
                        }
                    }

                    if (isAnchorCol)
                    {
                        anchorValues[col] = value;

                        // U slozeneho anchoru (napr. Komise: RK_ID + RK_KOD) se jednotlive
                        // slozky ponechaji i jako bezne atributy – u jednoduseho anchoru (1 sloupec,
                        // typicky primo UIC/PK_UTVAR/OSPOM apod.) se chovani nemeni.
                        if (anchorColumns.Length > 1)
                            entry.AttributeChanges.Add(AttributeChange.CreateAttributeAdd(col, value));
                    }
                    else
                    {
                        entry.AttributeChanges.Add(AttributeChange.CreateAttributeAdd(col, value));
                    }

                    if (col.Equals("VYSTUP",  StringComparison.OrdinalIgnoreCase)) vystupValue  = value;
                    if (col.Equals("ZAKL_PP", StringComparison.OrdinalIgnoreCase)) zaklPpValue  = value;
                    if (col.Equals("AKTIVNI", StringComparison.OrdinalIgnoreCase)) aktivniAdded = true;
                }

                // anchorSet jen pokud maji hodnotu VSECHNY sloupce anchoru
                bool anchorSet = true;
                foreach (string ac in anchorColumns)
                {
                    if (!anchorValues.ContainsKey(ac)) { anchorSet = false; break; }
                }

                string anchorValue = null;
                if (anchorSet)
                {
                    var parts = new string[anchorColumns.Length];
                    for (int i = 0; i < anchorColumns.Length; i++)
                        parts[i] = anchorValues[anchorColumns[i]];
                    anchorValue = string.Join("-", parts);
                    entry.DN    = ToDN(anchorValue);
                }

                // Pridej vypocitany atribut AKTIVNI – jen pokud neni primo ve view
                if (ColumnTransform.ShouldComputeAktivni(objType, "VYSTUP") && !aktivniAdded)
                {
                    entry.AttributeChanges.Add(AttributeChange.CreateAttributeAdd(
                        "AKTIVNI", ColumnTransform.ComputeAktivni(vystupValue)));
                }

                // Person: dohledej PRIMARNI_PPV / AKTIVNI_PPV / AKTIVNI (boolean) z V_IDM_PPV
                if (anchorSet && objType == "Person")
                {
                    AddPpvCrossReferences(entry, anchorValue, zaklPpValue);
                }

                // Distinct object types (Komise, PracMista): dohledej clenstvi
                // (multivalued reference "member") podle anchor sloupcu
                if (anchorSet && _currentConfig.DistinctColumns != null)
                {
                    AddMemberReferences(entry, anchorValues);
                }

                if (firstLog)
                {
                    firstLog = false;
                    Log("INFO", $"GetFullEntries [{objType}]: prvni radek anchor='{anchorValue}' fields={_reader.FieldCount}");
                }

                if (anchorSet)
                    entries.Add(entry);
                else
                    skipped++;

                count++;
            }

            if (skipped > 0)
                Log("WARN", $"GetFullEntries [{objType}]: preskoceno {skipped} radku bez anchoru '{string.Join("+", anchorColumns)}'");

            return new GetImportEntriesResults
            {
                CSEntries    = entries,
                MoreToImport = count == ImportDefaultPageSize
            };
        }

        // ── Delta Import ──────────────────────────────────────────────────────────

        private GetImportEntriesResults GetDeltaEntries()
        {
            var    entries = new List<CSEntryChange>();
            string anchor  = _currentConfig.AnchorColumn;
            string objType = _currentConfig.Name;
            int    added   = 0;

            while (added < ImportDefaultPageSize && _deltaIndex < _deltaItems.Count)
            {
                var item = _deltaItems[_deltaIndex++];

                if (string.IsNullOrWhiteSpace(item.Uic)) continue;

                if (item.ActionType == "delete" && objType == "Person")
                {
                    // Smazat zaznamy Person primo (UIC je anchor)
                    var del = CSEntryChange.Create();
                    del.ObjectModificationType = ObjectModificationType.Delete;
                    del.ObjectType             = objType;
                    del.DN                     = ToDN(item.Uic);
                    entries.Add(del);
                    added++;
                }
                else if (item.ActionType != "delete")
                {
                    // ADD nebo MODIFY: nacti aktualni data z view
                    var fetched = FetchDeltaRecords(item.Uic, anchor, objType);
                    foreach (var e in fetched)
                    {
                        entries.Add(e);
                        added++;
                        if (added >= ImportDefaultPageSize) break;
                    }
                }
                // PPV delete: preskocime – plne smazani v CS probehne az pri Full Import
                // (IDM_CHANGES neobsahuje OSPOM – anchor PPV)
            }

            return new GetImportEntriesResults
            {
                CSEntries    = entries,
                MoreToImport = _deltaIndex < _deltaItems.Count
            };
        }

        /// <summary>
        /// Dotaz na view pro dany UIC a vrati seznam Replace CSEntryChange.
        /// Pro Person: WHERE UIC = :uic
        /// Pro PPV:    WHERE UIC = :uic  (vsechna PPV daneho cloveka)
        /// </summary>
        private List<CSEntryChange> FetchDeltaRecords(string uic, string anchor, string objType)
        {
            var result   = new List<CSEntryChange>();
            string view  = _currentConfig.View;

            try
            {
                var cmd = new OracleCommand(
                    $"SELECT * FROM {view} WHERE UIC = :uic", _conn);
                cmd.Parameters.Add("uic", OracleDbType.Varchar2).Value = uic;

                using (var rdr = cmd.ExecuteReader())
                {
                    while (rdr.Read())
                    {
                        var entry = CSEntryChange.Create();
                        entry.ObjectModificationType = ObjectModificationType.Replace;
                        entry.ObjectType             = objType;

                        bool   anchorSet    = false;
                        string vystupValue  = null;
                        string zaklPpValue  = null;
                        bool   aktivniAdded = false;

                        for (int i = 0; i < rdr.FieldCount; i++)
                        {
                            if (rdr.IsDBNull(i)) continue;
                            string col   = rdr.GetName(i).ToUpper();
                            string value = rdr.GetValue(i).ToString();
                            if (string.IsNullOrWhiteSpace(value)) continue;

                            if (col.Equals(anchor, StringComparison.OrdinalIgnoreCase))
                            {
                                entry.DN  = ToDN(value);
                                anchorSet = true;
                            }
                            else
                            {
                                entry.AttributeChanges.Add(
                                    AttributeChange.CreateAttributeAdd(col, value));
                            }

                            if (col.Equals("VYSTUP",  StringComparison.OrdinalIgnoreCase)) vystupValue  = value;
                            if (col.Equals("ZAKL_PP", StringComparison.OrdinalIgnoreCase)) zaklPpValue  = value;
                            if (col.Equals("AKTIVNI", StringComparison.OrdinalIgnoreCase)) aktivniAdded = true;
                        }

                        // Pridej vypocitany AKTIVNI – jen pokud neni primo ve view
                        if (ColumnTransform.ShouldComputeAktivni(objType, "VYSTUP") && !aktivniAdded)
                        {
                            entry.AttributeChanges.Add(AttributeChange.CreateAttributeAdd(
                                "AKTIVNI", ColumnTransform.ComputeAktivni(vystupValue)));
                        }

                        // Person: dohledej PRIMARNI_PPV / AKTIVNI_PPV / AKTIVNI (boolean) z V_IDM_PPV
                        if (anchorSet && objType == "Person")
                        {
                            AddPpvCrossReferences(entry, uic, zaklPpValue);
                        }

                        if (anchorSet) result.Add(entry);
                    }
                }
            }
            catch (Exception ex)
            {
                Log("WARN", $"FetchDeltaRecords [{objType}] UIC={uic}: {ex.Message}");
            }

            return result;
        }

        /// <summary>
        /// Dohleda pro Person zaznam navazane PPV/BaseUnit a prida vypocitane referencni atributy:
        ///   PRIMARNI_PPV – reference na PPV kde CISLO_PP = ZAKL_PP a UIC = UIC (zakladni prac. pomer)
        ///   AKTIVNI_PPV  – reference na vsechna PPV kde UIC = UIC a AKTIVNI = 1
        ///   AKTIVNI      – boolean, prevzato z AKTIVNI primarniho PPV
        ///   UTVAR        – reference na BaseUnit, kde UTVAR_ZKRATKA = STRED_KMEN_ZKRATKA primarniho PPV
        /// Pozn.: V_IDM_PPV ma sloupec UIC (FK na zamestnance) stejne jako V_IDM_ZAM - viz FetchDeltaRecords.
        /// Pokud sloupec v danem prostredi neexistuje, dotaz selze a chyba se pouze zaloguje – import
        /// Person zaznamu tim neni ovlivnen.
        /// </summary>
        private void AddPpvCrossReferences(CSEntryChange entry, string uic, string zaklPp)
        {
            if (string.IsNullOrWhiteSpace(uic) || PpvConfig == null) return;

            string sql = string.IsNullOrWhiteSpace(zaklPp)
                ? $"SELECT OSPOM, AKTIVNI, CISLO_PP, STRED_KMEN_ZKRATKA FROM {PpvConfig.View} WHERE UIC = :uic AND AKTIVNI = 1"
                : $"SELECT OSPOM, AKTIVNI, CISLO_PP, STRED_KMEN_ZKRATKA FROM {PpvConfig.View} WHERE UIC = :uic AND (AKTIVNI = 1 OR CISLO_PP = :zaklpp)";

            try
            {
                var cmd = new OracleCommand(sql, _conn);
                cmd.Parameters.Add("uic", OracleDbType.Varchar2).Value = uic;
                if (!string.IsNullOrWhiteSpace(zaklPp))
                    cmd.Parameters.Add("zaklpp", OracleDbType.Varchar2).Value = zaklPp;

                string primarniDn        = null;
                bool   primarniAkt       = false;
                bool   primarniFound     = false;
                string primarniStredZkr  = null;
                var    aktivniDns        = new List<object>();

                using (var rdr = cmd.ExecuteReader())
                {
                    while (rdr.Read())
                    {
                        string ospom      = rdr.IsDBNull(0) ? null : rdr.GetValue(0).ToString();
                        bool   aktivni    = !rdr.IsDBNull(1) && rdr.GetValue(1).ToString() == "1";
                        string cisloPp    = rdr.IsDBNull(2) ? null : rdr.GetValue(2).ToString();
                        string stredZkr   = rdr.IsDBNull(3) ? null : rdr.GetValue(3).ToString();

                        if (string.IsNullOrWhiteSpace(ospom)) continue;
                        string dn = $"CN={ospom},CN={PpvConfig.Name}";

                        if (aktivni)
                            aktivniDns.Add(dn);

                        if (!primarniFound && !string.IsNullOrWhiteSpace(zaklPp) &&
                            cisloPp != null && cisloPp.Equals(zaklPp, StringComparison.OrdinalIgnoreCase))
                        {
                            primarniDn       = dn;
                            primarniAkt      = aktivni;
                            primarniStredZkr = stredZkr;
                            primarniFound    = true;
                        }
                    }
                }

                if (primarniFound)
                {
                    entry.AttributeChanges.Add(AttributeChange.CreateAttributeAdd("PRIMARNI_PPV", primarniDn));
                    entry.AttributeChanges.Add(AttributeChange.CreateAttributeAdd("AKTIVNI", primarniAkt ? "1" : "0"));

                    string utvarDn = FindBaseUnitDn(primarniStredZkr);
                    if (utvarDn != null)
                        entry.AttributeChanges.Add(AttributeChange.CreateAttributeAdd("UTVAR", utvarDn));
                }

                if (aktivniDns.Count > 0)
                    entry.AttributeChanges.Add(AttributeChange.CreateAttributeAdd("AKTIVNI_PPV", aktivniDns));
            }
            catch (Exception ex)
            {
                Log("WARN", $"AddPpvCrossReferences UIC={uic} ZAKL_PP={zaklPp}: {ex.Message}");
            }
        }

        /// <summary>
        /// Najde BaseUnit (IDM.V_IDM_UNIT), kde UTVAR_ZKRATKA odpovida zadane zkratce, a vrati jeho DN
        /// v connector space (format "CN=&lt;PK_UTVAR&gt;,CN=BaseUnit"). Vraci null pokud nenalezeno/chyba.
        /// </summary>
        private string FindBaseUnitDn(string utvarZkratka)
        {
            if (string.IsNullOrWhiteSpace(utvarZkratka) || BaseUnitConfig == null) return null;

            try
            {
                var cmd = new OracleCommand(
                    $"SELECT {BaseUnitConfig.AnchorColumn} FROM {BaseUnitConfig.View} WHERE UTVAR_ZKRATKA = :zkr",
                    _conn);
                cmd.Parameters.Add("zkr", OracleDbType.Varchar2).Value = utvarZkratka;

                object result = cmd.ExecuteScalar();
                if (result != null && result != DBNull.Value)
                    return $"CN={result},CN={BaseUnitConfig.Name}";
            }
            catch (Exception ex)
            {
                Log("WARN", $"FindBaseUnitDn UTVAR_ZKRATKA={utvarZkratka}: {ex.Message}");
            }

            return null;
        }

        /// <summary>
        /// Dohleda vsechny cleny daneho distinct objektu (Komise/PracMista) – tj. vsechny UIC
        /// v puvodnim (nededuplikovanem) view, kde vsechny anchor sloupce odpovidaji zadanym
        /// hodnotam (napr. u Komise RK_ID + RK_KOD) – a prida multivalued referencni atribut
        /// "member" s odkazy na odpovidajici Person objekty (DN ve tvaru "CN=&lt;UIC&gt;,CN=Person").
        /// </summary>
        private void AddMemberReferences(CSEntryChange entry, Dictionary<string, string> anchorValues)
        {
            if (anchorValues == null || anchorValues.Count == 0 || PersonConfig == null) return;

            string anchorDesc = "";
            try
            {
                var cmd        = new OracleCommand { Connection = _conn };
                var conditions = new List<string>();
                int idx        = 0;

                foreach (string anchorCol in _currentConfig.AnchorColumns)
                {
                    if (!anchorValues.TryGetValue(anchorCol, out string val)) continue;
                    string paramName = "anchorVal" + idx++;
                    conditions.Add($"{anchorCol} = :{paramName}");
                    cmd.Parameters.Add(paramName, OracleDbType.Varchar2).Value = val;
                }
                anchorDesc = string.Join(",", conditions);

                cmd.CommandText = $"SELECT DISTINCT UIC FROM {_currentConfig.View} " +
                    $"WHERE {string.Join(" AND ", conditions)} AND UIC IS NOT NULL";

                var members = new List<object>();
                using (var rdr = cmd.ExecuteReader())
                {
                    while (rdr.Read())
                    {
                        if (rdr.IsDBNull(0)) continue;
                        string uic = rdr.GetValue(0).ToString();
                        if (string.IsNullOrWhiteSpace(uic)) continue;
                        members.Add($"CN={uic},CN={PersonConfig.Name}");
                    }
                }

                if (members.Count > 0)
                    entry.AttributeChanges.Add(AttributeChange.CreateAttributeAdd("member", members));
            }
            catch (Exception ex)
            {
                Log("WARN", $"AddMemberReferences [{_currentConfig.Name}] {anchorDesc}: {ex.Message}");
            }
        }

        // ── Close Import ──────────────────────────────────────────────────────────

        public CloseImportConnectionResults CloseImportConnection(
            CloseImportConnectionRunStep runStep)
        {
            Log("INFO", $"CloseImportConnection: partition={_currentConfig?.Name} isDelta={_isDelta}");

            if (_isDelta && _deltaItems != null && _deltaItems.Count > 0)
            {
                MarkDeltaProcessed();
            }

            _reader?.Close();
            _reader?.Dispose();
            _conn?.Dispose();
            _deltaItems = null;

            return new CloseImportConnectionResults(string.Empty);
        }

        /// <summary>Oznaci vsechny zpracovane delta zaznamy jako STATUS='P'.</summary>
        private void MarkDeltaProcessed()
        {
            string deltaTable = GetParam(_config, CP_DELTA_TABLE, MagionConfig.DefaultDeltaTable);
            string objectType = _currentConfig.DeltaObjectType;

            try
            {
                var cmd = new OracleCommand(
                    $"UPDATE {deltaTable} SET STATUS = 'P' " +
                    $"WHERE STATUS = 'N' AND OBJECT_TYPE = :ot", _conn);
                cmd.Parameters.Add("ot", OracleDbType.Varchar2).Value = objectType;
                int rows = cmd.ExecuteNonQuery();
                Log("INFO", $"MarkDeltaProcessed: oznaceno {rows} zaznamu jako STATUS='P' pro OBJECT_TYPE='{objectType}'");
            }
            catch (Exception ex)
            {
                Log("WARN", $"MarkDeltaProcessed: chyba pri update STATUS: {ex.Message}");
            }
        }

        // ── Helper ────────────────────────────────────────────────────────────────

        private static readonly string LogFile =
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
                         "OracleECMA2MA", "OracleECMA2MA.log");

        private static void Log(string level, string message)
        {
            try
            {
                string dir = Path.GetDirectoryName(LogFile);
                if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);
                File.AppendAllText(LogFile,
                    $"{DateTime.Now:yyyy-MM-dd HH:mm:ss} [{level}] {message}{Environment.NewLine}");
            }
            catch { }
        }

        // Vraci plny LDAP DN objektu v namespace daneho partition.
        // Format: CN=<anchorValue>,CN=<partitionName>  napr. "CN=51786,CN=Person"
        private string ToDN(string anchorValue)
            => $"CN={anchorValue},CN={_currentConfig.Name}";

        private static string GetParam(
            KeyedCollection<string, ConfigParameter> cfg,
            string key,
            string defaultValue = "")
        {
            return cfg.Contains(key) ? cfg[key].Value : defaultValue;
        }

        private OracleConnection BuildConnection(KeyedCollection<string, ConfigParameter> cfg)
        {
            string pass = cfg.Contains(CP_PASSWORD) && cfg[CP_PASSWORD].SecureValue != null
                ? new System.Net.NetworkCredential("", cfg[CP_PASSWORD].SecureValue).Password
                : GetParam(cfg, CP_PASSWORD, "");

            string connStr =
                $"Data Source={GetParam(cfg, CP_DATASOURCE, MAGION_DATASOURCE)};" +
                $"User Id={GetParam(cfg, CP_USER, "IDM")};Password={pass};" +
                $"Pooling=false;Connection Timeout=30;";

            return new OracleConnection(connStr);
        }
    }
}
