using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Data;
using System.Data.SqlClient;
using System.Globalization;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using Microsoft.MetadirectoryServices;

// Alias pro SchemaType - vyhne se konfliktu s System.Data.SchemaType
using MmsSchemaType = Microsoft.MetadirectoryServices.SchemaType;

namespace FimServiceDbMA
{
    /// <summary>
    /// Read-only ECMA2 konektor, ktery cte objekty a atributy primo z MIM Service databaze
    /// (FIMService). Pouziva se tam, kde nelze nasadit standardni FIM Service MA - typicky kdyz
    /// se lisi verze Synchronization Service a MIM Service/Portalu.
    ///
    /// Konektor pouze cte. Zadny export, zadny zapis do DB.
    /// </summary>
    public class FimServiceDbConnector :
        IMAExtensible2GetCapabilities,
        IMAExtensible2GetParameters,
        IMAExtensible2GetSchema,
        IMAExtensible2GetPartitions,
        IMAExtensible2CallImport
    {
        // ── Nazvy konfiguracnich parametru ────────────────────────────────────────
        private const string CP_SERVER = "SQL Server";
        private const string CP_DATABASE = "Database";
        private const string CP_INTEGRATED = "Windows authentication";
        private const string CP_USER = "SQL login";
        private const string CP_PASSWORD = "SQL password";
        private const string CP_CONNOPTIONS = "Connection string options";
        private const string CP_TIMEOUT = "Command timeout (s)";
        private const string CP_LOCALE = "Locale";
        private const string CP_IDFORMAT = "ObjectID format";
        private const string CP_DELTA_OVERLAP = "Delta overlap (s)";
        private const string CP_DELTA_RESCAN = "Delta rescan object types";
        private const string CP_PARTITION_SEED = "Partition ID seed";
        private const string CP_LOGFILE = "Log file";
        private const string CP_VERBOSE = "Verbose log";

        private const string IDFORMAT_GUID = "guid";
        private const string IDFORMAT_URN = "urn:uuid";

        /// <summary>Kolik zmenenych objektu smi jedna delta davka vzit; zbytek dobere dalsi delta run.</summary>
        private const int MaxDeltaObjectsPerRun = 100000;

        /// <summary>Zastupny symbol, ktery se v dotazu nahradi konkretnim seznamem ObjectKey.</summary>
        private const string ObjectKeysToken = "/*OBJECTKEYS*/";

        private const string WatermarkPrefix = "FimServiceDbMA/1|";

        // ── Stav importu ──────────────────────────────────────────────────────────
        private SqlConnection _conn;
        private List<ImportScope> _scopes;

        /// <summary>
        /// Object types vybrane v konfiguraci MA. Reference na typ, ktery mezi nimi neni,
        /// se zahodi - MIM pro ni nema kam zalozit zastupny objekt a sync skonci 0x80230405.
        /// </summary>
        private HashSet<string> _selectedTypes;

        /// <summary>Kolik hodnot referenci se zahodilo, podle ciloveho object type.</summary>
        private Dictionary<string, int> _skippedReferences;

        private Dictionary<short, ImportScope> _scopeByTypeKey;
        private List<ImportScope> _fullScanQueue;
        private int _fullScanIndex;
        private ObjectModificationType _fullScanModification = ObjectModificationType.Add;
        private int _pageSize = 200;
        private short _localeKey = FimDbMetadata.InvariantLocaleKey;
        private int _commandTimeout = 300;
        private bool _urnObjectIds;

        // ── Stav delta importu ────────────────────────────────────────────────────
        private bool _isDelta;
        private string _incomingWatermark;
        private DateTime _newWatermarkUtc;
        private List<DeltaChange> _deltaChanges;
        private int _deltaChangeIndex;
        private List<DeltaDelete> _deltaDeletes;
        private int _deltaDeleteIndex;
        private int _vanishedObjects;
        private bool _loggedImportShape;

        /// <summary>Jeden object type v rozsahu importu vcetne vybranych atributu a predpocitanych dotazu.</summary>
        private sealed class ImportScope
        {
            public FimObjectType Type;
            public Dictionary<short, FimAttribute> Attributes;

            /// <summary>Dotaz na hodnoty pro rozsah ObjectKey (strankovany full scan).</summary>
            public string ValueSqlRange;

            /// <summary>Dotaz na hodnoty pro vyctovy seznam ObjectKey (delta).</summary>
            public string ValueSqlKeys;

            /// <summary>Typ se pri delta importu cte cely znovu (viz CP_DELTA_RESCAN).</summary>
            public bool RescanOnDelta;

            /// <summary>
            /// Podminka na [fim].[Objects], ktera vyradi objekty bez hodnoty vybraneho atributu.
            /// null = ve schematu neni nic krome ObjectID, takze neni co importovat.
            /// </summary>
            public string HasValueSql;

            public long LastObjectKey;
            public int Imported;

            /// <summary>Objekty, ktere mely v DB radek, ale zadnou hodnotu vybraneho atributu.</summary>
            public int Skipped;
        }

        /// <summary>Objekt, u ktereho request log ukazuje zmenu - jeho stav se docte znovu.</summary>
        private struct DeltaChange
        {
            public long ObjectKey;
            public short TypeKey;
        }

        /// <summary>Smazany objekt; ObjectID prezije smazani jen v [fim].[RequestLog].</summary>
        private struct DeltaDelete
        {
            public Guid ObjectId;
            public short TypeKey;
        }

        // ── Capabilities ──────────────────────────────────────────────────────────

        public MACapabilities Capabilities
        {
            get
            {
                // POZOR: vychozi hodnoty MACapabilities nejsou false. SupportExport, SupportPassword,
                // SupportHierarchy, ObjectRename, DeleteAddAsReplace a ConcurrentOperation jsou
                // ve vychozim stavu true. Nevyplnena vlastnost se tedy zapne a MA pak nejde
                // vytvorit (0x80070057 v cdext.cpp). Vsechno se proto nastavuje explicitne.
                return new MACapabilities
                {
                    SupportImport = true,
                    SupportExport = false,
                    SupportPassword = false,

                    // Kazdy object type je vlastni partition, aby sel importovat a drzet watermark zvlast.
                    SupportPartitions = true,
                    // Hierarchii by musel obsluhovat IMAExtensible2GetHierarchy, ktery konektor nema.
                    SupportHierarchy = false,

                    // Delta stoji na [fim].[Request].[CommittedTime] a [fim].[RequestLog].[EventTime],
                    // watermark se predava pres CustomData run stepu.
                    DeltaImport = true,

                    ConcurrentOperation = false,

                    // Ldap: DN objektu i reference je CN=<ObjectID>,CN=<object type>, tedy pod DN partition.
                    // Holé GUID ani prázdné <dn/> partcache nenajde a staging skončí 0x80230405.
                    DistinguishedNameStyle = MADistinguishedNameStyle.Ldap,

                    // Anchorem je samo DN. Anchor posilany zvlast (IsDNAsAnchor = false) se
                    // pri LDAP stylu do obrazu nedostane a staging skonci 0x80230302.
                    IsDNAsAnchor = true,

                    // DN je anchor, takze prejmenovani by znamenalo zmenu anchoru.
                    ObjectRename = false,

                    ObjectConfirmation = MAObjectConfirmation.Normal,
                    Normalizations = MANormalizations.None,

                    // Konektor neexportuje, ale vyplnit se to musi - viz poznamka o defaultech vyse.
                    ExportType = MAExportType.AttributeUpdate,
                    DeleteAddAsReplace = false,
                    FullExport = false,
                    NoReferenceValuesInFirstExport = false,
                    ExportPasswordInFirstPass = false
                };
            }
        }

        // ── Konfiguracni parametry ────────────────────────────────────────────────

        public IList<ConfigParameterDefinition> GetConfigParameters(
            KeyedCollection<string, ConfigParameter> configParameters,
            ConfigParameterPage page)
        {
            var list = new List<ConfigParameterDefinition>();

            if (page == ConfigParameterPage.Connectivity)
            {
                list.Add(ConfigParameterDefinition.CreateStringParameter(CP_SERVER, ""));
                list.Add(ConfigParameterDefinition.CreateStringParameter(CP_DATABASE, "", "FIMService"));
                list.Add(ConfigParameterDefinition.CreateDividerParameter());
                list.Add(ConfigParameterDefinition.CreateCheckBoxParameter(CP_INTEGRATED, true));
                list.Add(ConfigParameterDefinition.CreateStringParameter(CP_USER, ""));
                list.Add(ConfigParameterDefinition.CreateEncryptedStringParameter(CP_PASSWORD, ""));
                list.Add(ConfigParameterDefinition.CreateDividerParameter());
                list.Add(ConfigParameterDefinition.CreateStringParameter(CP_CONNOPTIONS, ""));
                list.Add(ConfigParameterDefinition.CreateLabelParameter(
                    "Staci ucet s pravem SELECT (db_datareader) na databazi FIMService. " +
                    "Connection string options napr.: Encrypt=True;TrustServerCertificate=True"));
            }
            else if (page == ConfigParameterPage.Global)
            {
                list.Add(ConfigParameterDefinition.CreateStringParameter(CP_TIMEOUT, "", "300"));
                list.Add(ConfigParameterDefinition.CreateStringParameter(CP_LOCALE, ""));
                list.Add(ConfigParameterDefinition.CreateDropDownParameter(
                    CP_IDFORMAT, new[] { IDFORMAT_GUID, IDFORMAT_URN }, false, IDFORMAT_GUID));
                list.Add(ConfigParameterDefinition.CreateDividerParameter());
                list.Add(ConfigParameterDefinition.CreateStringParameter(CP_DELTA_OVERLAP, "", "120"));
                list.Add(ConfigParameterDefinition.CreateStringParameter(CP_DELTA_RESCAN, "", "Set,Group"));
                list.Add(ConfigParameterDefinition.CreateLabelParameter(
                    "Delta overlap = o kolik sekund se watermark posouva dozadu (kryje requesty commitnute " +
                    "mimo poradi). Delta rescan object types = typy, ktere se pri delta importu vzdy " +
                    "prectou cele - nutne pro ComputedMember u Set/Group, protoze jejich prepocet " +
                    "negeneruje request na dany objekt."));
                list.Add(ConfigParameterDefinition.CreateDividerParameter());
                list.Add(ConfigParameterDefinition.CreateStringParameter(CP_PARTITION_SEED, "", ""));
                list.Add(ConfigParameterDefinition.CreateLabelParameter(
                    "Partition ID seed nechte prazdny. Vyplnte ho jen kdyz stejnou databazi cte vic MA " +
                    "soucasne - identifikatory partition se odvozuji z nazvu object type, takze by si " +
                    "je dve MA jinak prepisovala a druhe MA by se nevytvorilo (0x80040E2F). " +
                    "Zmena seedu u existujiciho MA rozbije jeho connector space."));
                list.Add(ConfigParameterDefinition.CreateDividerParameter());
                list.Add(ConfigParameterDefinition.CreateStringParameter(CP_LOGFILE, "", Log.DefaultFile));
                list.Add(ConfigParameterDefinition.CreateCheckBoxParameter(CP_VERBOSE, false));
                list.Add(ConfigParameterDefinition.CreateLabelParameter(
                    "Locale prazdne = neutralni (invariant) hodnoty, tak jak je pouziva synchronizace. " +
                    "ObjectID format urcuje podobu anchoru i hodnot referenci."));
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

            string server = GetString(configParameters, CP_SERVER, "");
            if (string.IsNullOrWhiteSpace(server))
                return Failure(CP_SERVER, "Zadejte nazev SQL serveru (napr. MIMSQL nebo MIMSQL\\INST).");

            if (!GetBool(configParameters, CP_INTEGRATED, true)
                && string.IsNullOrWhiteSpace(GetString(configParameters, CP_USER, "")))
            {
                return Failure(CP_USER, "Pri SQL autentizaci je nutne zadat SQL login.");
            }

            try
            {
                using (SqlConnection conn = OpenConnection(configParameters))
                {
                    string problem = FimDbMetadata.Verify(conn, GetTimeout(configParameters));
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

            Log.Info("ValidateConfigParameters: pripojeni k MIM Service DB je v poradku.");
            return new ParameterValidationResult();
        }

        // ── Schema ────────────────────────────────────────────────────────────────

        /// <summary>
        /// Postavi schema ze vsech vazeb v [fim].[BindingInternal]. Vysledkem je kompletni
        /// seznam object types a atributu, ze ktereho si uzivatel v konfiguraci MA vybere.
        /// </summary>
        public Schema GetSchema(KeyedCollection<string, ConfigParameter> configParameters)
        {
            ApplyLogSettings(configParameters);

            Schema schema = Schema.Create();
            int attributeCount = 0;

            using (SqlConnection conn = OpenConnection(configParameters))
            {
                List<FimObjectType> objectTypes = FimDbMetadata.Load(conn, GetTimeout(configParameters));

                foreach (FimObjectType objectType in objectTypes)
                {
                    // Anchorem je DN (IsDNAsAnchor), takze schema zadny anchor atribut nedeklaruje.
                    // ObjectID jde do schematu jako bezny atribut, aby slo joinovat.
                    MmsSchemaType schemaType = MmsSchemaType.Create(objectType.Name, false);
                    schemaType.Attributes.Add(SchemaAttribute.CreateSingleValuedAttribute(
                        FimDbMetadata.AnchorAttributeName, AttributeType.String));

                    foreach (FimAttribute attr in objectType.Attributes)
                    {
                        // Duplicitni nazev by shodil cely GetSchema ("Unable to retrieve schema")
                        if (schemaType.Attributes.Contains(attr.Name))
                        {
                            Log.Warn(string.Format("GetSchema: {0}.{1} je duplicitni, preskoceno.",
                                objectType.Name, attr.Name));
                            continue;
                        }

                        schemaType.Attributes.Add(attr.Multivalued
                            ? SchemaAttribute.CreateMultiValuedAttribute(attr.Name, attr.MimType)
                            : SchemaAttribute.CreateSingleValuedAttribute(attr.Name, attr.MimType));
                        attributeCount++;
                    }

                    schema.Types.Add(schemaType);
                }
            }

            if (schema.Types.Count == 0)
                throw new InvalidOperationException(
                    "Z MIM Service DB se nepodarilo nacist zadny object type. Overte prava na cteni [fim].[BindingInternal].");

            Log.Info(string.Format("GetSchema: {0} object types, {1} atributu.",
                schema.Types.Count, attributeCount));
            return schema;
        }

        // ── Partitions ────────────────────────────────────────────────────────────

        /// <summary>
        /// Jedna partition na object type. Identifikator je odvozeny z nazvu, aby obnoveni
        /// seznamu partition (Refresh) nerozbilo connector space.
        /// </summary>
        public IList<Partition> GetPartitions(KeyedCollection<string, ConfigParameter> configParameters)
        {
            ApplyLogSettings(configParameters);

            try
            {
                var list = new List<Partition>();
                string seed = GetString(configParameters, CP_PARTITION_SEED, "");

                using (SqlConnection conn = OpenConnection(configParameters))
                {
                    foreach (FimObjectType objectType in FimDbMetadata.Load(conn, GetTimeout(configParameters)))
                    {
                        if (string.IsNullOrWhiteSpace(objectType.Name))
                            continue;

                        // DN ve tvaru CN=<type>, aby ho UI partitions vzalo. Name zustava cisty nazev typu.
                        list.Add(Partition.Create(
                            PartitionIdFor(seed, objectType.Name),
                            PartitionDn(objectType.Name),
                            objectType.Name));
                    }
                }

                if (list.Count == 0)
                    throw new InvalidOperationException(
                        "Z MIM Service DB se nepodarilo nacist zadny object type, takze neni z ceho udelat partition.");

                Log.Info(string.Format("GetPartitions: {0} partitions, jedna na object type.", list.Count));
                return list;
            }
            catch (Exception ex)
            {
                Log.Error("GetPartitions selhalo.", ex);
                throw;
            }
        }

        /// <summary>DN partition. Nazev typu je v Name, tady je LDAP tvar.</summary>
        private static string PartitionDn(string objectTypeName)
        {
            return "CN=" + EscapeLdapRdn(objectTypeName);
        }

        /// <summary>
        /// DN objektu musi lezet pod DN sve partition, jinak partcache pri odkazu vrati 0x80230405.
        /// </summary>
        private static string ObjectDn(string formattedObjectId, string objectTypeName)
        {
            return "CN=" + EscapeLdapRdn(formattedObjectId) + "," + PartitionDn(objectTypeName);
        }

        private static string EscapeLdapRdn(string value)
        {
            if (string.IsNullOrEmpty(value))
                return string.Empty;

            var escaped = new StringBuilder(value.Length);
            foreach (char c in value.Trim())
            {
                if (c == '\\' || c == ',' || c == '+' || c == '"' || c == '<' || c == '>' || c == ';')
                    escaped.Append('\\');
                escaped.Append(c);
            }

            return escaped.ToString();
        }

        /// <summary>
        /// Stabilni Guid partition. Stejny nazev object type musi davat stejny identifikator,
        /// jinak Refresh partitions rozbije connector space.
        /// Seed odlisuje dve MA nad stejnou databazi - MIM nesnese dve MA se stejnymi identifikatory
        /// partition. Prazdny seed = puvodni identifikatory, aby se nezmenily uz zalozenym MA.
        /// SHA-256, ne MD5: na FIPS systemu MD5.Create() hodi InvalidOperationException a refresh partitions spadne.
        /// </summary>
        private static Guid PartitionIdFor(string seed, string objectTypeName)
        {
            using (var sha = new SHA256CryptoServiceProvider())
            {
                byte[] hash = sha.ComputeHash(Encoding.UTF8.GetBytes(
                    "FimServiceDbMA_" + (seed ?? string.Empty).Trim() + objectTypeName));
                var guidBytes = new byte[16];
                Buffer.BlockCopy(hash, 0, guidBytes, 0, guidBytes.Length);
                return new Guid(guidBytes);
            }
        }

        // ── Import ────────────────────────────────────────────────────────────────

        public int ImportDefaultPageSize
        {
            get { return 200; }
        }

        public int ImportMaxPageSize
        {
            get { return 2000; }
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
            _urnObjectIds = string.Equals(GetString(configParameters, CP_IDFORMAT, IDFORMAT_GUID).Trim(),
                IDFORMAT_URN, StringComparison.OrdinalIgnoreCase);

            _pageSize = importRunStep.PageSize > 0
                ? Math.Min(importRunStep.PageSize, ImportMaxPageSize)
                : ImportDefaultPageSize;

            _conn = OpenConnection(configParameters);
            _localeKey = FimDbMetadata.ResolveLocaleKey(_conn, _commandTimeout,
                GetString(configParameters, CP_LOCALE, ""));

            _selectedTypes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (MmsSchemaType schemaType in types.Types)
                _selectedTypes.Add(schemaType.Name);
            _skippedReferences = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

            List<FimObjectType> metadata = FimDbMetadata.Load(_conn, _commandTimeout);
            _scopes = FilterToPartition(
                BuildScopes(types, metadata, ParseRescanTypes(configParameters)),
                importRunStep.StepPartition);
            _scopeByTypeKey = new Dictionary<short, ImportScope>();
            foreach (ImportScope scope in _scopes)
                _scopeByTypeKey[scope.Type.Key] = scope;

            _incomingWatermark = importRunStep.CustomData;
            _deltaChanges = new List<DeltaChange>();
            _deltaDeletes = new List<DeltaDelete>();
            _deltaChangeIndex = 0;
            _deltaDeleteIndex = 0;
            _vanishedObjects = 0;
            _fullScanIndex = 0;

            // Cas bereme ze SQL serveru - CommittedTime i EventTime stavi MIM Service na GETUTCDATE()
            DateTime runStartUtc = GetDatabaseUtcNow();

            if (_isDelta)
            {
                _fullScanQueue = _scopes.Where(s => s.RescanOnDelta).ToList();
                _fullScanModification = ObjectModificationType.Replace;

                DateTime lastWatermarkUtc = ParseWatermark(_incomingWatermark);
                DateTime fromUtc = lastWatermarkUtc - TimeSpan.FromSeconds(GetDeltaOverlap(configParameters));

                Log.Info(string.Format(
                    "OpenImportConnection (delta): okno {0:o} - {1:o} (watermark {2:o}), rescan typy: {3}",
                    fromUtc, runStartUtc, lastWatermarkUtc,
                    _fullScanQueue.Count == 0 ? "zadne" : string.Join(", ", _fullScanQueue.Select(s => s.Type.Name))));

                _newWatermarkUtc = LoadDeltaWork(fromUtc, runStartUtc);
            }
            else
            {
                _fullScanQueue = new List<ImportScope>(_scopes);
                _fullScanModification = ObjectModificationType.Add;

                // Watermark z casu PRED zacatkem full importu - zmeny, ktere behem nej vzniknou,
                // takhle vezme prvni delta run. Radeji necist dvakrat nez neprecist vubec.
                _newWatermarkUtc = runStartUtc;
            }

            Log.Info(string.Format(
                "OpenImportConnection: type={0}, partition={1}, pageSize={2}, localeKey={3}, objectIdFormat={4}",
                importRunStep.ImportType, _scopes[0].Type.Name, _pageSize, _localeKey,
                _urnObjectIds ? IDFORMAT_URN : IDFORMAT_GUID));

            // Watermark se posouva teprve po dokonceni celeho importu (viz CloseImportConnection),
            // aby preruseny run nezpusobil ztratu zmen.
            return new OpenImportConnectionResults(_incomingWatermark);
        }

        /// <summary>
        /// Nacte seznam zmenenych a smazanych objektu za dane casove okno a vrati novy watermark.
        /// Zdrojem jsou [fim].[Request] (commitnute requesty vcetne jejich cile) a [fim].[RequestLog]
        /// (radek na kazdou zmenenou hodnotu atributu, vcetne vsech cilu multi-target requestu).
        /// </summary>
        private DateTime LoadDeltaWork(DateTime fromUtc, DateTime toUtc)
        {
            if (_scopes.Count == 0)
                return toUtc;

            string typeKeys = string.Join(",",
                _scopes.Select(s => s.Type.Key.ToString(CultureInfo.InvariantCulture)));

            // Kazdy zdroj muze narazit na strop; watermark pak posuneme jen tam, kam jsme opravdu dosli.
            DateTime watermarkUtc = toUtc;
            var seen = new HashSet<long>();

            DateTime requestLimit = LoadChangedFromRequests(fromUtc, toUtc, typeKeys, seen);
            if (requestLimit < watermarkUtc)
                watermarkUtc = requestLimit;

            DateTime logLimit = LoadChangedFromRequestLog(fromUtc, toUtc, typeKeys, seen);
            if (logLimit < watermarkUtc)
                watermarkUtc = logLimit;

            DateTime deleteLimit = LoadDeletedFromRequestLog(fromUtc, toUtc, typeKeys);
            if (deleteLimit < watermarkUtc)
                watermarkUtc = deleteLimit;

            // Cteni po typech drzi jeden dotaz na hodnoty v jednom object type
            _deltaChanges.Sort((a, b) => a.TypeKey != b.TypeKey
                ? a.TypeKey.CompareTo(b.TypeKey)
                : a.ObjectKey.CompareTo(b.ObjectKey));

            WarnOnUnloggedDeletes(fromUtc, toUtc, typeKeys);

            if (watermarkUtc < toUtc)
            {
                Log.Warn(string.Format(
                    "Delta: dosazen strop {0} objektu na jeden run, watermark posunut jen na {1:o}. " +
                    "Spustte delta import znovu, zbytek zmen se dobere.",
                    MaxDeltaObjectsPerRun, watermarkUtc));
            }

            Log.Info(string.Format("Delta: {0} zmenenych objektu, {1} smazanych objektu.",
                _deltaChanges.Count, _deltaDeletes.Count));

            return watermarkUtc;
        }

        /// <summary>Cile commitnutych requestu - jediny zdroj, ktery je vzdy zaplneny.</summary>
        private DateTime LoadChangedFromRequests(DateTime fromUtc, DateTime toUtc, string typeKeys,
            HashSet<long> seen)
        {
            string sql =
                "SELECT TOP (@top) [r].[TargetKey], [r].[TargetObjectTypeKey], MAX([r].[CommittedTime]) AS [ChangeTime] " +
                "FROM [fim].[Request] AS [r] " +
                "WHERE [r].[CommittedTime] > @fromUtc AND [r].[CommittedTime] <= @toUtc " +
                "  AND [r].[TargetKey] IS NOT NULL " +
                "  AND [r].[TargetObjectTypeKey] IN (" + typeKeys + ") " +
                "GROUP BY [r].[TargetKey], [r].[TargetObjectTypeKey] " +
                "ORDER BY MAX([r].[CommittedTime])";

            int added = 0;
            DateTime lastChange = toUtc;
            int rows = 0;

            using (SqlCommand cmd = CreateDeltaCommand(sql, fromUtc, toUtc))
            using (SqlDataReader rdr = cmd.ExecuteReader())
            {
                while (rdr.Read())
                {
                    rows++;
                    long objectKey = rdr.GetInt64(0);
                    short typeKey = rdr.GetInt16(1);
                    lastChange = rdr.GetDateTime(2);

                    if (seen.Add(objectKey))
                    {
                        _deltaChanges.Add(new DeltaChange { ObjectKey = objectKey, TypeKey = typeKey });
                        added++;
                    }
                }
            }

            Log.Debug(string.Format("Delta [fim].[Request]: {0} cilu, {1} novych.", rows, added));

            // Strop = nemuzeme rict, ze mame vse az do toUtc
            return rows >= MaxDeltaObjectsPerRun ? lastChange : toUtc;
        }

        /// <summary>
        /// Zmeny podle logu hodnot. Pokryje i objekty, ktere nejsou primarnim cilem requestu
        /// (typicky prepocet clenstvi), ale plni se jen pro typy uvedene v Synchronization Filteru.
        /// </summary>
        private DateTime LoadChangedFromRequestLog(DateTime fromUtc, DateTime toUtc, string typeKeys,
            HashSet<long> seen)
        {
            string sql =
                "SELECT TOP (@top) [l].[ObjectKey], [o].[ObjectTypeKey], MAX([l].[EventTime]) AS [ChangeTime] " +
                "FROM [fim].[RequestLog] AS [l] " +
                "INNER JOIN [fim].[Objects] AS [o] ON [o].[ObjectKey] = [l].[ObjectKey] " +
                "WHERE [l].[EventTime] > @fromUtc AND [l].[EventTime] <= @toUtc " +
                "  AND [o].[ObjectTypeKey] IN (" + typeKeys + ") " +
                "GROUP BY [l].[ObjectKey], [o].[ObjectTypeKey] " +
                "ORDER BY MAX([l].[EventTime])";

            int added = 0;
            int rows = 0;
            DateTime lastChange = toUtc;

            using (SqlCommand cmd = CreateDeltaCommand(sql, fromUtc, toUtc))
            using (SqlDataReader rdr = cmd.ExecuteReader())
            {
                while (rdr.Read())
                {
                    rows++;
                    long objectKey = rdr.GetInt64(0);
                    short typeKey = rdr.GetInt16(1);
                    lastChange = rdr.GetDateTime(2);

                    if (seen.Add(objectKey))
                    {
                        _deltaChanges.Add(new DeltaChange { ObjectKey = objectKey, TypeKey = typeKey });
                        added++;
                    }
                }
            }

            Log.Debug(string.Format("Delta [fim].[RequestLog]: {0} objektu, {1} novych.", rows, added));
            return rows >= MaxDeltaObjectsPerRun ? lastChange : toUtc;
        }

        /// <summary>
        /// Smazane objekty. Po smazani resource zmizi radek z [fim].[Objects], takze jeho GUID
        /// se da dohledat jen v [fim].[RequestLog] (Added=0 nad atributem ObjectID).
        /// </summary>
        private DateTime LoadDeletedFromRequestLog(DateTime fromUtc, DateTime toUtc, string typeKeys)
        {
            short objectIdAttributeKey = FimDbMetadata.ResolveAttributeKey(
                _conn, _commandTimeout, FimDbMetadata.AnchorAttributeName);

            string sql =
                "SELECT TOP (@top) [l].[ObjectID], [r].[TargetObjectTypeKey], MAX([l].[EventTime]) AS [ChangeTime] " +
                "FROM [fim].[RequestLog] AS [l] " +
                "INNER JOIN [fim].[Request] AS [r] ON [r].[RequestKey] = [l].[RequestKey] " +
                "WHERE [l].[EventTime] > @fromUtc AND [l].[EventTime] <= @toUtc " +
                "  AND [l].[Added] = 0 AND [l].[AttributeKey] = @objectIdAttributeKey " +
                "  AND [r].[TargetObjectTypeKey] IN (" + typeKeys + ") " +
                "  AND NOT EXISTS (SELECT 1 FROM [fim].[Objects] AS [o] WHERE [o].[ObjectKey] = [l].[ObjectKey]) " +
                "GROUP BY [l].[ObjectID], [r].[TargetObjectTypeKey] " +
                "ORDER BY MAX([l].[EventTime])";

            int rows = 0;
            DateTime lastChange = toUtc;

            using (SqlCommand cmd = CreateDeltaCommand(sql, fromUtc, toUtc))
            {
                cmd.Parameters.Add("@objectIdAttributeKey", SqlDbType.SmallInt).Value = objectIdAttributeKey;

                using (SqlDataReader rdr = cmd.ExecuteReader())
                {
                    while (rdr.Read())
                    {
                        rows++;
                        _deltaDeletes.Add(new DeltaDelete { ObjectId = rdr.GetGuid(0), TypeKey = rdr.GetInt16(1) });
                        lastChange = rdr.GetDateTime(2);
                    }
                }
            }

            Log.Debug(string.Format("Delta smazane objekty z [fim].[RequestLog]: {0}", rows));
            return rows >= MaxDeltaObjectsPerRun ? lastChange : toUtc;
        }

        /// <summary>
        /// [fim].[RequestLog] se plni jen pro object types v Synchronization Filteru. Kdyz tam
        /// dany typ neni, delta smazani nezachyti a musi je dobrat full import - na to je potreba
        /// uzivatele upozornit, jinak mu v CS zustanou nezmizele objekty.
        /// </summary>
        private void WarnOnUnloggedDeletes(DateTime fromUtc, DateTime toUtc, string typeKeys)
        {
            string sql =
                "SELECT COUNT(*) FROM [fim].[Request] AS [r] " +
                "WHERE [r].[CommittedTime] > @fromUtc AND [r].[CommittedTime] <= @toUtc " +
                "  AND [r].[Operation] = N'Delete' AND [r].[TargetObjectTypeKey] IN (" + typeKeys + ")";

            int deleteRequests;
            using (var cmd = new SqlCommand(sql, _conn))
            {
                cmd.CommandTimeout = _commandTimeout;
                cmd.Parameters.Add("@fromUtc", SqlDbType.DateTime).Value = fromUtc;
                cmd.Parameters.Add("@toUtc", SqlDbType.DateTime).Value = toUtc;
                deleteRequests = Convert.ToInt32(cmd.ExecuteScalar());
            }

            if (deleteRequests > _deltaDeletes.Count)
            {
                Log.Warn(string.Format(
                    "Delta: v okne je {0} Delete requestu, ale [fim].[RequestLog] dava jen {1} smazani. " +
                    "Chybejici objekty odstrani az Full Import. Aby je zachytila delta, musi byt dane " +
                    "object types uvedene v resource Synchronization Filter (atribut SynchronizeObjectType).",
                    deleteRequests, _deltaDeletes.Count));
            }
        }

        private SqlCommand CreateDeltaCommand(string sql, DateTime fromUtc, DateTime toUtc)
        {
            var cmd = new SqlCommand(sql, _conn);
            cmd.CommandTimeout = _commandTimeout;
            cmd.Parameters.Add("@top", SqlDbType.Int).Value = MaxDeltaObjectsPerRun;
            cmd.Parameters.Add("@fromUtc", SqlDbType.DateTime).Value = fromUtc;
            cmd.Parameters.Add("@toUtc", SqlDbType.DateTime).Value = toUtc;
            return cmd;
        }

        /// <summary>
        /// Run profil importuje vzdy jednu partition, tedy jeden object type.
        /// Watermark v CustomData je tim padem taky per partition.
        /// </summary>
        private static List<ImportScope> FilterToPartition(List<ImportScope> scopes, Partition partition)
        {
            string typeName = PartitionTypeName(partition);
            if (typeName == null)
                throw new InvalidOperationException(
                    "Import neni navazany na partition. Kazdy object type ma vlastni partition - " +
                    "v run profilu vyberte konkretni partition.");

            List<ImportScope> selected = scopes
                .Where(s => string.Equals(s.Type.Name, typeName, StringComparison.OrdinalIgnoreCase))
                .ToList();

            if (selected.Count == 0)
            {
                throw new InvalidOperationException(string.Format(
                    "Partition '{0}' neodpovida zadnemu vybranemu object type. " +
                    "V konfiguraci MA zaskrtnete object type se stejnym nazvem, nebo obnovte seznam partitions.",
                    typeName));
            }

            return selected;
        }

        /// <summary>Nazev object type z partition. UI nekdy vrati Name, nekdy jen DN (CN=Person).</summary>
        private static string PartitionTypeName(Partition partition)
        {
            if (partition == null)
                return null;

            string name = string.IsNullOrWhiteSpace(partition.Name) ? partition.DN : partition.Name;
            if (string.IsNullOrWhiteSpace(name))
                return null;

            name = name.Trim();
            if (name.StartsWith("CN=", StringComparison.OrdinalIgnoreCase))
            {
                name = name.Substring(3);
                int comma = name.IndexOf(',');
                if (comma >= 0)
                    name = name.Substring(0, comma);
                name = name.Replace("\\,", ",").Replace("\\+", "+");
            }

            name = name.Trim();
            return name.Length == 0 ? null : name;
        }

        /// <summary>
        /// Prelozi schema vybrane v konfiguraci MA na interni klice z MIM Service DB.
        /// Importuji se jen object types a atributy, ktere uzivatel v MA opravdu zaskrtl.
        /// </summary>
        private List<ImportScope> BuildScopes(Schema selected, List<FimObjectType> metadata,
            HashSet<string> rescanTypes)
        {
            var byName = metadata.ToDictionary(t => t.Name, StringComparer.OrdinalIgnoreCase);
            var scopes = new List<ImportScope>();

            foreach (MmsSchemaType schemaType in selected.Types)
            {
                FimObjectType dbType;
                if (!byName.TryGetValue(schemaType.Name, out dbType))
                {
                    Log.Warn(string.Format(
                        "Object type '{0}' uz v MIM Service DB neexistuje - preskocen. Obnovte schema MA.",
                        schemaType.Name));
                    continue;
                }

                var attrByName = dbType.Attributes.ToDictionary(a => a.Name, StringComparer.OrdinalIgnoreCase);
                var attributes = new Dictionary<short, FimAttribute>();

                foreach (SchemaAttribute schemaAttribute in schemaType.Attributes)
                {
                    if (schemaAttribute.IsAnchor ||
                        string.Equals(schemaAttribute.Name, FimDbMetadata.AnchorAttributeName,
                            StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    FimAttribute attr;
                    if (!attrByName.TryGetValue(schemaAttribute.Name, out attr))
                    {
                        Log.Warn(string.Format("Atribut '{0}.{1}' uz v MIM Service DB neexistuje - preskocen.",
                            schemaType.Name, schemaAttribute.Name));
                        continue;
                    }

                    attributes[attr.Key] = attr;
                }

                scopes.Add(new ImportScope
                {
                    Type = dbType,
                    Attributes = attributes,
                    ValueSqlRange = BuildValueSql(attributes.Keys, false),
                    ValueSqlKeys = BuildValueSql(attributes.Keys, true),
                    HasValueSql = BuildHasValueSql(attributes.Values),
                    RescanOnDelta = rescanTypes.Contains(dbType.Name)
                });

                Log.Debug(string.Format("Rozsah importu: {0} (ObjectTypeKey={1}), {2} atributu, rescanOnDelta={3}",
                    dbType.Name, dbType.Key, attributes.Count, rescanTypes.Contains(dbType.Name)));
            }

            return scopes;
        }

        public GetImportEntriesResults GetImportEntries(GetImportEntriesRunStep importRunStep)
        {
            var entries = new List<CSEntryChange>();

            AppendFullScan(entries);
            AppendDeltaChanges(entries);
            AppendDeltaDeletes(entries);

            bool more = !IsCompleted();

            return new GetImportEntriesResults
            {
                CSEntries = entries,
                MoreToImport = more,

                // Watermark se posune az poslednim volanim, aby ho MIM nezapsal driv,
                // nez se vsechny zmeny opravdu dostanou do connector space.
                CustomData = more ? _incomingWatermark : FormatWatermark(_newWatermarkUtc)
            };
        }

        private bool IsCompleted()
        {
            return _fullScanIndex >= _fullScanQueue.Count
                   && _deltaChangeIndex >= _deltaChanges.Count
                   && _deltaDeleteIndex >= _deltaDeletes.Count;
        }

        /// <summary>Strankovane cteni celych object types - full import, pri delta jen typy s rescanem.</summary>
        private void AppendFullScan(List<CSEntryChange> entries)
        {
            while (entries.Count < _pageSize && _fullScanIndex < _fullScanQueue.Count)
            {
                ImportScope scope = _fullScanQueue[_fullScanIndex];
                int want = _pageSize - entries.Count;

                List<KeyValuePair<long, Guid>> batch = ReadObjectBatch(scope, want);
                if (batch.Count == 0)
                {
                    LogScopeFinished(scope);
                    _fullScanIndex++;
                    continue;
                }

                scope.LastObjectKey = batch[batch.Count - 1].Key;

                Dictionary<long, Dictionary<short, List<object>>> values =
                    ReadValuesForRange(scope, batch[0].Key, scope.LastObjectKey);

                foreach (KeyValuePair<long, Guid> obj in batch)
                {
                    Dictionary<short, List<object>> objectValues;
                    if (!values.TryGetValue(obj.Key, out objectValues))
                        objectValues = null;

                    CSEntryChange entry = BuildPresentEntry(scope, obj.Value, objectValues, _fullScanModification);
                    if (entry == null)
                        continue;

                    entries.Add(entry);
                    if (entry.ObjectModificationType != ObjectModificationType.Delete)
                        scope.Imported++;
                }

                // Kratsi davka nez pozadovana = tento object type je vycerpany
                if (batch.Count < want)
                {
                    LogScopeFinished(scope);
                    _fullScanIndex++;
                }
            }
        }

        /// <summary>
        /// Zmenene objekty se nectou z historie hodnot, ale znovu z aktualniho stavu a posilaji se
        /// jako Replace. Delta tim zaroven opravi pripadne dive z predchozich runu.
        /// </summary>
        private void AppendDeltaChanges(List<CSEntryChange> entries)
        {
            while (entries.Count < _pageSize && _deltaChangeIndex < _deltaChanges.Count)
            {
                short typeKey = _deltaChanges[_deltaChangeIndex].TypeKey;
                int want = _pageSize - entries.Count;

                var keys = new List<long>(want);
                int next = _deltaChangeIndex;
                while (next < _deltaChanges.Count && keys.Count < want && _deltaChanges[next].TypeKey == typeKey)
                    keys.Add(_deltaChanges[next++].ObjectKey);

                _deltaChangeIndex = next;

                ImportScope scope;
                if (!_scopeByTypeKey.TryGetValue(typeKey, out scope))
                    continue;

                // Rescanovane typy uz projdou celym full scanem, podruhe je neposilame
                if (scope.RescanOnDelta)
                    continue;

                Dictionary<long, Guid> objects = ReadObjectsByKeys(scope, keys);
                Dictionary<long, Dictionary<short, List<object>>> values = ReadValuesForKeys(scope, keys);

                foreach (long objectKey in keys)
                {
                    Guid objectId;
                    if (!objects.TryGetValue(objectKey, out objectId))
                    {
                        // Objekt uz v DB neni - smazani resi faze nad [fim].[RequestLog]
                        _vanishedObjects++;
                        continue;
                    }

                    Dictionary<short, List<object>> objectValues;
                    if (!values.TryGetValue(objectKey, out objectValues))
                        objectValues = null;

                    CSEntryChange entry = BuildPresentEntry(
                        scope, objectId, objectValues, ObjectModificationType.Replace);
                    if (entry == null)
                        continue;

                    entries.Add(entry);
                    if (entry.ObjectModificationType != ObjectModificationType.Delete)
                        scope.Imported++;
                }
            }
        }

        private void AppendDeltaDeletes(List<CSEntryChange> entries)
        {
            while (entries.Count < _pageSize && _deltaDeleteIndex < _deltaDeletes.Count)
            {
                DeltaDelete deleted = _deltaDeletes[_deltaDeleteIndex++];

                ImportScope scope;
                if (!_scopeByTypeKey.TryGetValue(deleted.TypeKey, out scope))
                    continue;

                entries.Add(CreateDeleteEntry(scope, deleted.ObjectId));
            }
        }

        public CloseImportConnectionResults CloseImportConnection(CloseImportConnectionRunStep importRunStepInfo)
        {
            bool completed = _scopes != null && IsCompleted();

            if (_scopes != null)
            {
                Log.Info(string.Format(
                    "CloseImportConnection: {0} objektu, {1} smazani, {2} bez atributu, reason={3}, dokonceno={4}",
                    _scopes.Sum(s => s.Imported), _deltaDeleteIndex, _scopes.Sum(s => s.Skipped),
                    importRunStepInfo.Reason, completed));
            }

            if (_vanishedObjects > 0)
            {
                Log.Info(string.Format(
                    "Delta: {0} zmenenych objektu uz v DB neexistuje (smazane mezitim).", _vanishedObjects));
            }

            if (_skippedReferences != null && _skippedReferences.Count > 0)
            {
                Log.Warn(string.Format(
                    "Zahozeno {0} hodnot referenci na object types, ktere nejsou v MA vybrane: {1}. " +
                    "Pokud je potrebujete, zaskrtnete tyto object types i jejich partitions a udelejte Full Import.",
                    _skippedReferences.Values.Sum(),
                    string.Join(", ", _skippedReferences
                        .OrderByDescending(p => p.Value)
                        .Select(p => p.Key + " (" + p.Value + ")"))));
            }

            if (_conn != null)
            {
                _conn.Dispose();
                _conn = null;
            }

            // Watermark posouvame jen po uspesne dobehnutem importu; jinak vratime ten puvodni
            // a dalsi delta zacne znovu od nej.
            bool advance = completed && importRunStepInfo.Reason == CloseReason.Normal;
            string watermark = advance ? FormatWatermark(_newWatermarkUtc) : _incomingWatermark;

            if (!advance)
                Log.Warn("CloseImportConnection: watermark se neposouva, import nedobehl cely.");

            _scopes = null;
            _fullScanQueue = null;
            _deltaChanges = null;
            _deltaDeletes = null;

            return new CloseImportConnectionResults(watermark);
        }

        // ── Cteni dat ─────────────────────────────────────────────────────────────

        /// <summary>Nacte dalsi davku objektu daneho typu, strankuje se podle rostouciho ObjectKey.</summary>
        private List<KeyValuePair<long, Guid>> ReadObjectBatch(ImportScope scope, int top)
        {
            var result = new List<KeyValuePair<long, Guid>>(top);
            if (scope.HasValueSql == null)
            {
                Log.Warn(string.Format(
                    "Import {0}: ve schematu neni zadny atribut krome ObjectID, objekty se neimportuji.",
                    scope.Type.Name));
                return result;
            }

            string sql =
                "SELECT TOP (@top) [o].[ObjectKey], [o].[ObjectID] " +
                "FROM [fim].[Objects] AS [o] " +
                "WHERE [o].[ObjectTypeKey] = @typeKey AND [o].[ObjectKey] > @afterKey" +
                scope.HasValueSql +
                " ORDER BY [o].[ObjectKey]";

            using (var cmd = new SqlCommand(sql, _conn))
            {
                cmd.CommandTimeout = _commandTimeout;
                cmd.Parameters.Add("@top", SqlDbType.Int).Value = top;
                cmd.Parameters.Add("@typeKey", SqlDbType.SmallInt).Value = scope.Type.Key;
                cmd.Parameters.Add("@afterKey", SqlDbType.BigInt).Value = scope.LastObjectKey;
                if (scope.HasValueSql.IndexOf("@localeKey", StringComparison.Ordinal) >= 0)
                    cmd.Parameters.Add("@localeKey", SqlDbType.SmallInt).Value = _localeKey;

                using (SqlDataReader rdr = cmd.ExecuteReader())
                {
                    while (rdr.Read())
                        result.Add(new KeyValuePair<long, Guid>(rdr.GetInt64(0), rdr.GetGuid(1)));
                }
            }

            Log.Debug(string.Format("ReadObjectBatch {0}: {1} objektu (afterKey={2})",
                scope.Type.Name, result.Count, scope.LastObjectKey));
            return result;
        }

        /// <summary>Dohleda ObjectID k vyctu ObjectKey - chybejici klic znamena, ze objekt uz neexistuje.</summary>
        private Dictionary<long, Guid> ReadObjectsByKeys(ImportScope scope, List<long> objectKeys)
        {
            var result = new Dictionary<long, Guid>(objectKeys.Count);
            if (objectKeys.Count == 0)
                return result;

            string sql =
                "SELECT [o].[ObjectKey], [o].[ObjectID] FROM [fim].[Objects] AS [o] " +
                "WHERE [o].[ObjectTypeKey] = @typeKey AND [o].[ObjectKey] IN (" + FormatKeyList(objectKeys) + ")";

            using (var cmd = new SqlCommand(sql, _conn))
            {
                cmd.CommandTimeout = _commandTimeout;
                cmd.Parameters.Add("@typeKey", SqlDbType.SmallInt).Value = scope.Type.Key;

                using (SqlDataReader rdr = cmd.ExecuteReader())
                {
                    while (rdr.Read())
                        result[rdr.GetInt64(0)] = rdr.GetGuid(1);
                }
            }

            Log.Debug(string.Format("ReadObjectsByKeys {0}: {1} z {2} klicu nalezeno.",
                scope.Type.Name, result.Count, objectKeys.Count));
            return result;
        }

        private Dictionary<long, Dictionary<short, List<object>>> ReadValuesForRange(
            ImportScope scope, long fromKey, long toKey)
        {
            return ReadValues(scope, scope.ValueSqlRange, cmd =>
            {
                cmd.Parameters.Add("@fromKey", SqlDbType.BigInt).Value = fromKey;
                cmd.Parameters.Add("@toKey", SqlDbType.BigInt).Value = toKey;
            }, string.Format("ObjectKey {0}-{1}", fromKey, toKey));
        }

        private Dictionary<long, Dictionary<short, List<object>>> ReadValuesForKeys(
            ImportScope scope, List<long> objectKeys)
        {
            if (objectKeys.Count == 0 || scope.ValueSqlKeys == null)
                return new Dictionary<long, Dictionary<short, List<object>>>();

            // Klice jsou cisla ze DB, do dotazu jdou jako literal - vyctovy IN nejde parametrizovat
            string sql = scope.ValueSqlKeys.Replace(ObjectKeysToken, FormatKeyList(objectKeys));
            return ReadValues(scope, sql, null, objectKeys.Count + " klicu");
        }

        /// <summary>
        /// Nacte hodnoty vsech vybranych atributu jednim dotazem. Hodnoty jsou v MIM Service DB
        /// rozdelene do tabulek podle datoveho typu, proto UNION ALL.
        /// </summary>
        private Dictionary<long, Dictionary<short, List<object>>> ReadValues(
            ImportScope scope, string sql, Action<SqlCommand> bindParameters, string scopeDescription)
        {
            var result = new Dictionary<long, Dictionary<short, List<object>>>();
            if (sql == null)
                return result;

            int rows = 0;
            int skipped = 0;

            using (var cmd = new SqlCommand(sql, _conn))
            {
                cmd.CommandTimeout = _commandTimeout;
                cmd.Parameters.Add("@typeKey", SqlDbType.SmallInt).Value = scope.Type.Key;
                cmd.Parameters.Add("@localeKey", SqlDbType.SmallInt).Value = _localeKey;

                if (bindParameters != null)
                    bindParameters(cmd);

                using (SqlDataReader rdr = cmd.ExecuteReader())
                {
                    while (rdr.Read())
                    {
                        rows++;
                        long objectKey = rdr.GetInt64(0);
                        short attributeKey = rdr.GetInt16(1);

                        FimAttribute attr;
                        if (!scope.Attributes.TryGetValue(attributeKey, out attr))
                            continue;

                        string text = rdr.IsDBNull(2) ? null : rdr.GetString(2);
                        byte[] binary = rdr.IsDBNull(3) ? null : (byte[])rdr.GetValue(3);

                        object value = ConvertValue(attr, text, binary);
                        if (value == null)
                        {
                            skipped++;
                            Log.Debug(string.Format("Nelze prevest hodnotu {0} (DataType={1}): '{2}'",
                                attr.Name, attr.DataType, text));
                            continue;
                        }

                        Dictionary<short, List<object>> objectValues;
                        if (!result.TryGetValue(objectKey, out objectValues))
                        {
                            objectValues = new Dictionary<short, List<object>>();
                            result.Add(objectKey, objectValues);
                        }

                        List<object> list;
                        if (!objectValues.TryGetValue(attributeKey, out list))
                        {
                            list = new List<object>(1);
                            objectValues.Add(attributeKey, list);
                        }

                        list.Add(value);
                    }
                }
            }

            if (skipped > 0)
                Log.Warn(string.Format("ReadValues {0}: {1} hodnot nelze prevest, viz verbose log.",
                    scope.Type.Name, skipped));

            Log.Debug(string.Format("ReadValues {0}: {1} radku pro {2}",
                scope.Type.Name, rows, scopeDescription));
            return result;
        }

        private static string FormatKeyList(List<long> objectKeys)
        {
            return string.Join(",", objectKeys.Select(k => k.ToString(CultureInfo.InvariantCulture)));
        }

        private static void LogScopeFinished(ImportScope scope)
        {
            if (scope.Skipped > 0)
            {
                Log.Info(string.Format(
                    "Import {0}: hotovo, {1} objektu, {2} preskoceno (zadne hodnoty vybranych atributu).",
                    scope.Type.Name, scope.Imported, scope.Skipped));
            }
            else
            {
                Log.Info(string.Format("Import {0}: hotovo, {1} objektu.", scope.Type.Name, scope.Imported));
            }
        }

        /// <summary>
        /// Objekt bez hodnoty vybraneho atributu by v CS mel jen ObjectID a na portale nejde dohledat.
        /// Full import ho vynecha (synchronizace ho pak z CS smaze jako zastaraly). Delta ho posle jako Delete,
        /// aby v connector space nezustal se starym obsahem.
        /// </summary>
        private CSEntryChange BuildPresentEntry(ImportScope scope, Guid objectId,
            Dictionary<short, List<object>> values, ObjectModificationType modificationType)
        {
            if (HasAttributeValues(values))
                return BuildEntry(scope, objectId, values, modificationType);

            scope.Skipped++;
            Log.Debug(string.Format("{0}/{1}: zadne hodnoty vybranych atributu, neimportuje se.",
                scope.Type.Name, FormatObjectId(objectId)));

            return modificationType == ObjectModificationType.Replace
                ? CreateDeleteEntry(scope, objectId)
                : null;
        }

        private static bool HasAttributeValues(Dictionary<short, List<object>> values)
        {
            if (values == null)
                return false;

            foreach (List<object> list in values.Values)
            {
                if (list != null && list.Count > 0)
                    return true;
            }

            return false;
        }

        private CSEntryChange CreateDeleteEntry(ImportScope scope, Guid objectId)
        {
            string anchor = FormatObjectId(objectId);

            CSEntryChange entry = CSEntryChange.Create();
            entry.ObjectModificationType = ObjectModificationType.Delete;
            entry.ObjectType = scope.Type.Name;
            entry.DN = ObjectDn(anchor, scope.Type.Name);
            return entry;
        }

        private CSEntryChange BuildEntry(ImportScope scope, Guid objectId,
            Dictionary<short, List<object>> values, ObjectModificationType modificationType)
        {
            string anchor = FormatObjectId(objectId);
            string dn = ObjectDn(anchor, scope.Type.Name);

            CSEntryChange entry = CSEntryChange.Create();
            entry.ObjectModificationType = modificationType;
            entry.ObjectType = scope.Type.Name;
            // DN je zaroven anchor, proto se neposila ani jako AnchorAttribute, ani jako atribut "DN".
            entry.DN = dn;

            // ObjectID navic jako bezny atribut, aby bylo k dispozici pro join pravidla.
            AddChange(entry, FimDbMetadata.AnchorAttributeName, anchor, null);

            if (values == null)
                return LogFirstEntry(entry);

            foreach (KeyValuePair<short, List<object>> pair in values)
            {
                FimAttribute attr = scope.Attributes[pair.Key];
                List<object> list = pair.Value;
                if (list.Count == 0)
                    continue;

                if (attr.Multivalued)
                {
                    AddChange(entry, attr.Name, null, list);
                }
                else
                {
                    if (list.Count > 1)
                    {
                        Log.Debug(string.Format(
                            "{0}/{1}: single-valued atribut ma {2} hodnot, beru prvni.",
                            scope.Type.Name, attr.Name, list.Count));
                    }

                    AddChange(entry, attr.Name, list[0], null);
                }
            }

            return LogFirstEntry(entry);
        }

        /// <summary>
        /// Vzdy Add, i u objektu typu Replace. Pri Replace se cely obraz objektu stejne prepisuje
        /// seznamem atributu, ktery prijde, a CreateAttributeReplace vedl na missing-change-type.
        /// </summary>
        private static void AddChange(CSEntryChange entry, string name, object single, IList<object> multi)
        {
            entry.AttributeChanges.Add(multi != null
                ? AttributeChange.CreateAttributeAdd(name, multi)
                : AttributeChange.CreateAttributeAdd(name, single));
        }

        private CSEntryChange LogFirstEntry(CSEntryChange entry)
        {
            if (_loggedImportShape)
                return entry;

            _loggedImportShape = true;
            Log.Info(string.Format(
                "Prvni zaznam importu: object={0}, modification={1}, dn={2} (dn je anchor), " +
                "anchorAttributes={3}, atributu={4}",
                entry.ObjectType, entry.ObjectModificationType, entry.DN,
                entry.AnchorAttributes.Count, entry.AttributeChanges.Count));
            return entry;
        }

        /// <summary>Prevede hodnotu z DB na typ, ktery ocekava synchronizace. null = hodnotu preskocit.</summary>
        private object ConvertValue(FimAttribute attr, string text, byte[] binary)
        {
            switch (attr.MimType)
            {
                case AttributeType.Binary:
                    return binary;

                case AttributeType.Boolean:
                    return string.Equals(text, "true", StringComparison.OrdinalIgnoreCase);

                case AttributeType.Integer:
                    long number;
                    return long.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out number)
                        ? (object)number
                        : null;

                case AttributeType.Reference:
                    return FormatReference(text);

                default:
                    if (string.IsNullOrEmpty(text))
                        return null;

                    // Identifier se normalizuje na stejny tvar jako anchor
                    Guid identifier;
                    if (string.Equals(attr.DataType, "Identifier", StringComparison.Ordinal)
                        && Guid.TryParse(text, out identifier))
                    {
                        return identifier.ToString("D");
                    }

                    return text;
            }
        }

        /// <summary>
        /// Reference musi byt DN ciloveho objektu ve tvaru CN=&lt;ObjectID&gt;,CN=&lt;jeho typ&gt;.
        /// Stejny retezec, jaky ma cil jako entry.DN. Jinak partcache cil nenajde.
        /// </summary>
        private string FormatReference(string text)
        {
            if (string.IsNullOrEmpty(text))
                return null;

            int separator = text.IndexOf('|');
            string idPart = separator >= 0 ? text.Substring(0, separator) : text;
            string typeName = separator >= 0 ? text.Substring(separator + 1).Trim() : null;

            Guid reference;
            if (!Guid.TryParse(idPart, out reference) || string.IsNullOrEmpty(typeName))
                return null;

            // Cil v nevybranem object type nema v connector space kde vzniknout. MIM se o jeho
            // zalozeni presto pokusi a cela synchronizace spadne na 0x80230405, proto radeji
            // hodnotu zahodime. Chybejici typy vypise CloseImportConnection.
            if (_selectedTypes != null && !_selectedTypes.Contains(typeName))
            {
                if (_skippedReferences != null)
                {
                    int count;
                    _skippedReferences.TryGetValue(typeName, out count);
                    _skippedReferences[typeName] = count + 1;
                }
                return null;
            }

            return ObjectDn(FormatObjectId(reference), typeName);
        }

        /// <summary>
        /// Anchor i hodnoty referenci prochazi tudy, aby mely vzdy stejny tvar - jinak by
        /// synchronizace nedokazala referenci parovat na cilovy objekt.
        /// </summary>
        private string FormatObjectId(Guid id)
        {
            return _urnObjectIds
                ? "urn:uuid:" + id.ToString("D")
                : id.ToString("D");
        }

        /// <summary>
        /// Podminka, ktera v [fim].[Objects] necha jen objekty s aspon jednou hodnotou vybraneho atributu.
        /// Prazdny retezec se nepocita. Reference jen kdyz cilovy objekt jeste existuje.
        /// </summary>
        private static string BuildHasValueSql(IEnumerable<FimAttribute> attributes)
        {
            var keysByTable = new Dictionary<string, List<short>>(StringComparer.Ordinal);

            foreach (FimAttribute attr in attributes)
            {
                string table;
                switch (attr.DataType)
                {
                    case "String": table = "ObjectValueString"; break;
                    case "Text": table = "ObjectValueText"; break;
                    case "Xml": table = "ObjectValueXml"; break;
                    case "Integer": table = "ObjectValueInteger"; break;
                    case "Boolean": table = "ObjectValueBoolean"; break;
                    case "DateTime": table = "ObjectValueDateTime"; break;
                    case "Identifier": table = "ObjectValueIdentifier"; break;
                    case "Reference": table = "ObjectValueReference"; break;
                    case "Binary": table = "ObjectValueBinary"; break;
                    default:
                        continue;
                }

                List<short> keys;
                if (!keysByTable.TryGetValue(table, out keys))
                {
                    keys = new List<short>();
                    keysByTable.Add(table, keys);
                }

                keys.Add(attr.Key);
            }

            if (keysByTable.Count == 0)
                return null;

            var exists = new List<string>();
            foreach (KeyValuePair<string, List<short>> pair in keysByTable)
            {
                string keyList = string.Join(",",
                    pair.Value.Select(k => k.ToString(CultureInfo.InvariantCulture)));
                string match =
                    "[v].[ObjectTypeKey] = [o].[ObjectTypeKey] AND [v].[ObjectKey] = [o].[ObjectKey]" +
                    " AND [v].[AttributeKey] IN (" + keyList + ")";

                string extra;
                switch (pair.Key)
                {
                    case "ObjectValueString":
                        extra = " AND [v].[LocaleKey] = @localeKey AND [v].[ValueString] <> N''";
                        break;
                    case "ObjectValueText":
                        extra = " AND [v].[LocaleKey] = @localeKey AND [v].[ValueText] <> N''";
                        break;
                    case "ObjectValueXml":
                        extra = " AND [v].[LocaleKey] = @localeKey AND DATALENGTH([v].[ValueXml]) > 0";
                        break;
                    case "ObjectValueReference":
                        exists.Add(
                            "EXISTS (SELECT 1 FROM [fim].[ObjectValueReference] AS [v]" +
                            " INNER JOIN [fim].[Objects] AS [tgt] ON [tgt].[ObjectKey] = [v].[ValueReference]" +
                            " WHERE " + match + ")");
                        continue;
                    default:
                        extra = "";
                        break;
                }

                exists.Add("EXISTS (SELECT 1 FROM [fim].[" + pair.Key + "] AS [v] WHERE " + match + extra + ")");
            }

            return " AND (" + string.Join(" OR ", exists) + ")";
        }

        /// <summary>
        /// Slozi dotaz na hodnoty pres vsechny [fim].[ObjectValue*] tabulky. Vraci sjednocene
        /// sloupce ObjectKey, AttributeKey, ValueText (vse krome binarek) a ValueBinary.
        /// </summary>
        private static string BuildValueSql(IEnumerable<short> attributeKeys, bool byObjectKeyList)
        {
            string keyList = string.Join(",", attributeKeys.Select(k => k.ToString(CultureInfo.InvariantCulture)));
            if (string.IsNullOrEmpty(keyList))
                return null;

            string objectKeyFilter = byObjectKeyList
                ? " AND [v].[ObjectKey] IN (" + ObjectKeysToken + ")"
                : " AND [v].[ObjectKey] BETWEEN @fromKey AND @toKey";

            string where =
                " WHERE [v].[ObjectTypeKey] = @typeKey" +
                objectKeyFilter +
                " AND [v].[AttributeKey] IN (" + keyList + ")";

            string localizedWhere = where + " AND [v].[LocaleKey] = @localeKey";

            const string noBinary = "CAST(NULL AS VARBINARY(MAX))";

            var branches = new List<string>
            {
                // String
                "SELECT [v].[ObjectKey], [v].[AttributeKey], CAST([v].[ValueString] AS NVARCHAR(MAX)) AS [ValueText], " +
                noBinary + " AS [ValueBinary], CAST([v].[SequenceID] AS INT) AS [Seq]" +
                " FROM [fim].[ObjectValueString] AS [v]" + localizedWhere,

                // Text (nvarchar(max))
                "SELECT [v].[ObjectKey], [v].[AttributeKey], [v].[ValueText], " + noBinary +
                ", CAST([v].[SequenceID] AS INT)" +
                " FROM [fim].[ObjectValueText] AS [v]" + localizedWhere,

                // Xml
                "SELECT [v].[ObjectKey], [v].[AttributeKey], CAST([v].[ValueXml] AS NVARCHAR(MAX)), " + noBinary +
                ", CAST([v].[SequenceID] AS INT)" +
                " FROM [fim].[ObjectValueXml] AS [v]" + localizedWhere,

                // Integer
                "SELECT [v].[ObjectKey], [v].[AttributeKey], CAST([v].[ValueInteger] AS NVARCHAR(MAX)), " + noBinary +
                ", CAST([v].[SequenceID] AS INT)" +
                " FROM [fim].[ObjectValueInteger] AS [v]" + where,

                // Boolean (jen single-valued, tabulka nema SequenceID)
                "SELECT [v].[ObjectKey], [v].[AttributeKey], " +
                "CASE WHEN [v].[ValueBoolean] = 1 THEN N'true' ELSE N'false' END, " + noBinary + ", 0" +
                " FROM [fim].[ObjectValueBoolean] AS [v]" + where,

                // DateTime -> ISO 8601 (styl 126 = yyyy-mm-ddThh:mi:ss.mmm)
                "SELECT [v].[ObjectKey], [v].[AttributeKey], CONVERT(NVARCHAR(MAX), [v].[ValueDateTime], 126), " +
                noBinary + ", CAST([v].[SequenceID] AS INT)" +
                " FROM [fim].[ObjectValueDateTime] AS [v]" + where,

                // Identifier (tabulka nema SequenceID)
                "SELECT [v].[ObjectKey], [v].[AttributeKey], CONVERT(NVARCHAR(MAX), [v].[ValueIdentifier]), " +
                noBinary + ", 0" +
                " FROM [fim].[ObjectValueIdentifier] AS [v]" + where,

                // Reference -> ObjectID|ObjectType cilove resource. ConvertValue z toho slozi DN.
                "SELECT [v].[ObjectKey], [v].[AttributeKey], " +
                "CONVERT(NVARCHAR(36), [tgt].[ObjectID]) + N'|' + [tt].[Name], " +
                noBinary + ", 0" +
                " FROM [fim].[ObjectValueReference] AS [v]" +
                " INNER JOIN [fim].[Objects] AS [tgt] ON [tgt].[ObjectKey] = [v].[ValueReference]" +
                " INNER JOIN [fim].[ObjectTypeInternal] AS [tt] ON [tt].[Key] = [tgt].[ObjectTypeKey]" + where,

                // Binary
                "SELECT [v].[ObjectKey], [v].[AttributeKey], CAST(NULL AS NVARCHAR(MAX)), [v].[ValueBinary], " +
                "CAST([v].[SequenceID] AS INT)" +
                " FROM [fim].[ObjectValueBinary] AS [v]" + where
            };

            return "SELECT [ObjectKey], [AttributeKey], [ValueText], [ValueBinary] FROM (" +
                   string.Join(" UNION ALL ", branches) +
                   ") AS [x] ORDER BY [ObjectKey], [AttributeKey], [Seq]";
        }

        // ── Pripojeni a parametry ─────────────────────────────────────────────────

        private SqlConnection OpenConnection(KeyedCollection<string, ConfigParameter> cfg)
        {
            var builder = new SqlConnectionStringBuilder
            {
                DataSource = GetString(cfg, CP_SERVER, "").Trim(),
                InitialCatalog = GetString(cfg, CP_DATABASE, "FIMService").Trim(),
                ApplicationName = "MIM FimServiceDbMA",
                ConnectTimeout = 30,
                Pooling = false
            };

            if (GetBool(cfg, CP_INTEGRATED, true))
            {
                builder.IntegratedSecurity = true;
            }
            else
            {
                builder.UserID = GetString(cfg, CP_USER, "").Trim();
                builder.Password = GetPassword(cfg);
            }

            string connectionString = builder.ConnectionString;
            string options = GetString(cfg, CP_CONNOPTIONS, "").Trim().TrimStart(';');
            if (options.Length > 0)
                connectionString = new SqlConnectionStringBuilder(connectionString + ";" + options).ConnectionString;

            var conn = new SqlConnection(connectionString);
            conn.Open();

            // Cteme z produkcni DB bezici sluzby - nezamykat radky, ktere MIM Service prave meni
            using (var cmd = new SqlCommand("SET TRANSACTION ISOLATION LEVEL READ UNCOMMITTED", conn))
            {
                cmd.CommandTimeout = 30;
                cmd.ExecuteNonQuery();
            }

            return conn;
        }

        /// <summary>
        /// Cas bere z SQL serveru, ne z MIM serveru. CommittedTime i EventTime stampuje MIM Service
        /// pres GETUTCDATE(), takze i watermark musi jit z hodin SQL serveru, jinak by rozdil
        /// hodin obou stroju delta bud pretekl, nebo naopak preskocil zmeny.
        /// </summary>
        private DateTime GetDatabaseUtcNow()
        {
            using (var cmd = new SqlCommand("SELECT GETUTCDATE()", _conn))
            {
                cmd.CommandTimeout = 30;
                return DateTime.SpecifyKind((DateTime)cmd.ExecuteScalar(), DateTimeKind.Utc);
            }
        }

        private DateTime ParseWatermark(string customData)
        {
            if (!string.IsNullOrWhiteSpace(customData) && customData.StartsWith(WatermarkPrefix, StringComparison.Ordinal))
            {
                DateTime parsed;
                if (DateTime.TryParseExact(customData.Substring(WatermarkPrefix.Length).Trim(), "o",
                        CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out parsed))
                {
                    return parsed.Kind == DateTimeKind.Local
                        ? parsed.ToUniversalTime()
                        : DateTime.SpecifyKind(parsed, DateTimeKind.Utc);
                }
            }

            throw new NotSupportedException(
                "Delta import nema z ceho vyjit - v run stepu neni platny watermark tohoto konektoru. " +
                "Spustte nejprve Full Import, ten watermark zalozi.");
        }

        private static string FormatWatermark(DateTime utc)
        {
            return WatermarkPrefix +
                   DateTime.SpecifyKind(utc, DateTimeKind.Utc).ToString("o", CultureInfo.InvariantCulture);
        }

        private static int GetDeltaOverlap(KeyedCollection<string, ConfigParameter> cfg)
        {
            int seconds;
            if (!int.TryParse(GetString(cfg, CP_DELTA_OVERLAP, ""), out seconds) || seconds < 0)
                seconds = 120;

            return seconds;
        }

        /// <summary>Object types, ktere se pri delta importu ctou cele znovu.</summary>
        private static HashSet<string> ParseRescanTypes(KeyedCollection<string, ConfigParameter> cfg)
        {
            var result = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (string part in GetString(cfg, CP_DELTA_RESCAN, "").Split(',', ';'))
            {
                string name = part.Trim();
                if (name.Length > 0)
                    result.Add(name);
            }

            return result;
        }

        private static void ApplyLogSettings(KeyedCollection<string, ConfigParameter> cfg)
        {
            Log.Configure(GetString(cfg, CP_LOGFILE, ""), GetBool(cfg, CP_VERBOSE, false));
        }

        private static int GetTimeout(KeyedCollection<string, ConfigParameter> cfg)
        {
            int timeout;
            return int.TryParse(GetString(cfg, CP_TIMEOUT, ""), out timeout) && timeout > 0
                ? timeout
                : 300;
        }

        private static string GetPassword(KeyedCollection<string, ConfigParameter> cfg)
        {
            if (cfg.Contains(CP_PASSWORD) && cfg[CP_PASSWORD].SecureValue != null)
                return new System.Net.NetworkCredential(string.Empty, cfg[CP_PASSWORD].SecureValue).Password;

            return GetString(cfg, CP_PASSWORD, "");
        }

        private static string GetString(KeyedCollection<string, ConfigParameter> cfg, string name, string defaultValue)
        {
            return cfg != null && cfg.Contains(name) && cfg[name].Value != null
                ? cfg[name].Value
                : defaultValue;
        }

        private static bool GetBool(KeyedCollection<string, ConfigParameter> cfg, string name, bool defaultValue)
        {
            string value = GetString(cfg, name, null);
            if (string.IsNullOrWhiteSpace(value))
                return defaultValue;

            value = value.Trim();
            return value == "1"
                || value.Equals("true", StringComparison.OrdinalIgnoreCase)
                || value.Equals("yes", StringComparison.OrdinalIgnoreCase);
        }

        private static ParameterValidationResult Failure(string parameterName, string message)
        {
            return new ParameterValidationResult
            {
                Code = ParameterValidationResultCode.Failure,
                ErrorMessage = message,
                ErrorParameter = parameterName
            };
        }
    }
}
