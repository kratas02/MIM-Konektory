using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Data;
using System.Data.SqlClient;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Xml.Serialization;
using Microsoft.MetadirectoryServices;

using MmsSchemaType = Microsoft.MetadirectoryServices.SchemaType;

namespace SAPBAPI_Direct
{
    /// <summary>
    /// ECMA2 konektor: primy SAP RFC (NCo), bez sapTools.exe.
    /// Export chyby vraci do MIM pres CSEntryChangeResult (RETURN E/A i exception).
    /// </summary>
    public class SapBapiConnector :
        IMAExtensible2GetCapabilities,
        IMAExtensible2GetParameters,
        IMAExtensible2GetSchema,
        IMAExtensible2CallImport,
        IMAExtensible2CallExport
    {
        private const string CP_HOST = "App server host";
        private const string CP_SYSNR = "System number";
        private const string CP_SYSID = "System ID";
        private const string CP_CLIENT = "Client";
        private const string CP_LANG = "Language";
        private const string CP_USER = "RFC user";
        private const string CP_PASSWORD = "RFC password";
        private const string CP_LOGFILE = "Log file";
        private const string CP_XML = "XML file";
        private const string CP_SQL = "SQL server connectionstring";
        private const string CP_DEP = "Dependant roles";

        private SapRfc _rfc;
        private string _xmlFile;
        private HashSet<string> _bapiRoles;
        private List<Person> _persons;
        private List<CSEntryChange> _pending;
        private string _incomingCustomData;
        private string _newWatermark;
        private int _lastProcessed;
        private int _pageSize = 200;

        public int ImportDefaultPageSize { get { return 200; } }
        public int ImportMaxPageSize { get { return 1000; } }
        public int ExportDefaultPageSize { get { return 50; } }
        public int ExportMaxPageSize { get { return 200; } }

        public MACapabilities Capabilities
        {
            get
            {
                return new MACapabilities
                {
                    SupportImport = true,
                    SupportExport = true,
                    SupportPassword = false,
                    SupportPartitions = false,
                    SupportHierarchy = false,
                    DeltaImport = true,
                    ConcurrentOperation = false,
                    DistinguishedNameStyle = MADistinguishedNameStyle.None,
                    IsDNAsAnchor = false,
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
                list.Add(ConfigParameterDefinition.CreateStringParameter(CP_HOST, ""));
                list.Add(ConfigParameterDefinition.CreateStringParameter(CP_SYSNR, "", "00"));
                list.Add(ConfigParameterDefinition.CreateStringParameter(CP_SYSID, ""));
                list.Add(ConfigParameterDefinition.CreateStringParameter(CP_CLIENT, "", "010"));
                list.Add(ConfigParameterDefinition.CreateStringParameter(CP_LANG, "", "EN"));
                list.Add(ConfigParameterDefinition.CreateStringParameter(CP_USER, ""));
                list.Add(ConfigParameterDefinition.CreateEncryptedStringParameter(CP_PASSWORD, ""));
            }
            else if (page == ConfigParameterPage.Global)
            {
                list.Add(ConfigParameterDefinition.CreateStringParameter(CP_LOGFILE, "", Log.DefaultFile));
                list.Add(ConfigParameterDefinition.CreateStringParameter(CP_XML, ""));
                list.Add(ConfigParameterDefinition.CreateStringParameter(CP_SQL, "",
                    "Data Source=.;initial catalog=FIMSynchronizationService;Integrated Security=SSPI"));
                list.Add(ConfigParameterDefinition.CreateStringParameter(CP_DEP, "", "ZRFCCALL"));
                list.Add(ConfigParameterDefinition.CreateLabelParameter(
                    "XML file: prazdne = bez archivu i bez role indexu (Delta = Full). " +
                    "Jinak XML archiv + vedle nej .roles.idx pro porovnani clenstvi. " +
                    "Dependant roles (strednikem). SQL filtruje Group z MV."));
            }
            return list;
        }

        public ParameterValidationResult ValidateConfigParameters(
            KeyedCollection<string, ConfigParameter> configParameters,
            ConfigParameterPage page)
        {
            if (page != ConfigParameterPage.Connectivity)
                return new ParameterValidationResult();

            if (string.IsNullOrWhiteSpace(GetString(configParameters, CP_HOST, "")))
                return Fail(CP_HOST, "Zadejte App server host.");
            if (string.IsNullOrWhiteSpace(GetString(configParameters, CP_USER, "")))
                return Fail(CP_USER, "Zadejte RFC user.");
            return new ParameterValidationResult();
        }

        public Schema GetSchema(KeyedCollection<string, ConfigParameter> configParameters)
        {
            Schema schema = Schema.Create();

            MmsSchemaType person = MmsSchemaType.Create("Person", false);
            person.Attributes.Add(SchemaAttribute.CreateAnchorAttribute("ID", AttributeType.String));
            person.Attributes.Add(SchemaAttribute.CreateSingleValuedAttribute("EmployeeID", AttributeType.String));
            person.Attributes.Add(SchemaAttribute.CreateSingleValuedAttribute("FirstName", AttributeType.String));
            person.Attributes.Add(SchemaAttribute.CreateSingleValuedAttribute("LastName", AttributeType.String));
            person.Attributes.Add(SchemaAttribute.CreateSingleValuedAttribute("FullName", AttributeType.String));
            person.Attributes.Add(SchemaAttribute.CreateSingleValuedAttribute("ValidFrom", AttributeType.String));
            person.Attributes.Add(SchemaAttribute.CreateSingleValuedAttribute("ValidTo", AttributeType.String));
            person.Attributes.Add(SchemaAttribute.CreateSingleValuedAttribute("NickName", AttributeType.String));
            person.Attributes.Add(SchemaAttribute.CreateSingleValuedAttribute("EMail", AttributeType.String));
            person.Attributes.Add(SchemaAttribute.CreateSingleValuedAttribute("LastModificationDate", AttributeType.String));
            person.Attributes.Add(SchemaAttribute.CreateSingleValuedAttribute("ModDate", AttributeType.String));
            person.Attributes.Add(SchemaAttribute.CreateSingleValuedAttribute("LTime", AttributeType.String));
            person.Attributes.Add(SchemaAttribute.CreateSingleValuedAttribute("Password", AttributeType.String));
            person.Attributes.Add(SchemaAttribute.CreateSingleValuedAttribute("WRNG_LOGON", AttributeType.String));
            person.Attributes.Add(SchemaAttribute.CreateSingleValuedAttribute("LOCAL_LOCK", AttributeType.String));
            person.Attributes.Add(SchemaAttribute.CreateSingleValuedAttribute("GLOB_LOCK", AttributeType.String));
            person.Attributes.Add(SchemaAttribute.CreateSingleValuedAttribute("NO_USER_PW", AttributeType.String));
            person.Attributes.Add(SchemaAttribute.CreateSingleValuedAttribute("FUNCTION", AttributeType.String));
            person.Attributes.Add(SchemaAttribute.CreateSingleValuedAttribute("DEPARTMENT", AttributeType.String));
            person.Attributes.Add(SchemaAttribute.CreateSingleValuedAttribute("USERGROUP", AttributeType.String));
            person.Attributes.Add(SchemaAttribute.CreateSingleValuedAttribute("SPLD", AttributeType.String));
            person.Attributes.Add(SchemaAttribute.CreateSingleValuedAttribute("SPDB", AttributeType.String));
            person.Attributes.Add(SchemaAttribute.CreateSingleValuedAttribute("LANGU", AttributeType.String));
            person.Attributes.Add(SchemaAttribute.CreateSingleValuedAttribute("BAPIEnabled", AttributeType.Boolean));
            person.Attributes.Add(SchemaAttribute.CreateMultiValuedAttribute("ACTIVITYGROUPS", AttributeType.String));
            person.Attributes.Add(SchemaAttribute.CreateSingleValuedAttribute("ALIAS", AttributeType.String));
            person.Attributes.Add(SchemaAttribute.CreateSingleValuedAttribute("GroupsCount", AttributeType.Integer));
            schema.Types.Add(person);

            MmsSchemaType group = MmsSchemaType.Create("Group", false);
            group.Attributes.Add(SchemaAttribute.CreateAnchorAttribute("ID", AttributeType.String));
            group.Attributes.Add(SchemaAttribute.CreateSingleValuedAttribute("EmployeeID", AttributeType.String));
            group.Attributes.Add(SchemaAttribute.CreateSingleValuedAttribute("ValidFrom", AttributeType.String));
            group.Attributes.Add(SchemaAttribute.CreateSingleValuedAttribute("ValidTo", AttributeType.String));
            group.Attributes.Add(SchemaAttribute.CreateSingleValuedAttribute("Description", AttributeType.String));
            group.Attributes.Add(SchemaAttribute.CreateSingleValuedAttribute("FullName", AttributeType.String));
            group.Attributes.Add(SchemaAttribute.CreateSingleValuedAttribute("BAPIEnabled", AttributeType.Boolean));
            schema.Types.Add(group);

            return schema;
        }

        public OpenImportConnectionResults OpenImportConnection(
            KeyedCollection<string, ConfigParameter> configParameters,
            Schema types,
            OpenImportConnectionRunStep importRunStep)
        {
            if (importRunStep.ImportType != OperationType.Full && importRunStep.ImportType != OperationType.Delta)
                throw new NotSupportedException("Podporovan je jen Full a Delta Import, ne " + importRunStep.ImportType);

            InitRuntime(configParameters);
            _pageSize = importRunStep.PageSize > 0 ? Math.Min(importRunStep.PageSize, ImportMaxPageSize) : ImportDefaultPageSize;
            _lastProcessed = 0;
            _pending = new List<CSEntryChange>();
            _incomingCustomData = importRunStep.CustomData ?? "";
            _bapiRoles = LoadBapiRoles(GetString(configParameters, CP_SQL, ""));

            bool wantDelta = importRunStep.ImportType == OperationType.Delta;
            Log.Info("---------------------------- ZACATEK " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + " ----------------------------");
            Log.Info(wantDelta ? "Import: Delta, nacteni uzivatelu z SAP..." : "Import: Full, nacteni uzivatelu z SAP...");

            var sw = System.Diagnostics.Stopwatch.StartNew();
            List<string> names = _rfc.GetAllUsernames();
            _persons = new List<Person>(names.Count);
            foreach (string name in names)
            {
                try
                {
                    _persons.Add(_rfc.LoadPerson(name));
                }
                catch (Exception ex)
                {
                    Log.Error("Import detail selhal.", ex, name);
                }
            }

            sw.Stop();
            Log.Info("Import: nacteno " + _persons.Count + " uzivatelu.", duration: sw.ElapsedMilliseconds);

            string xmlPath = ResolvedXmlPath();
            string indexPath = RoleIndex.FilePath(xmlPath);
            RoleIndex previous = null;
            if (!string.IsNullOrEmpty(indexPath))
            {
                try
                {
                    previous = RoleIndex.Load(indexPath);
                }
                catch (Exception ex)
                {
                    Log.Warn("Role index se nepodarilo nacist, posilam Full.", detail: ex.Message);
                    previous = null;
                }
            }

            RoleIndex current = RoleIndex.FromPersons(_persons, _bapiRoles);
            _newWatermark = MaxLastModified(_persons);

            bool emitFull = !wantDelta
                || previous == null
                || string.IsNullOrWhiteSpace(_incomingCustomData)
                || string.IsNullOrEmpty(indexPath);

            if (wantDelta && emitFull)
                Log.Warn("Delta prepnuta na Full emit (chybi watermark nebo role index).");

            if (emitFull)
                BuildFullEntries();
            else
                BuildDeltaEntries(previous, current);

            if (!string.IsNullOrEmpty(indexPath))
            {
                try
                {
                    current.Save(indexPath);
                    Log.Info("Role index ulozen.", detail: indexPath);
                }
                catch (Exception ex)
                {
                    Log.Error("Role index se nepodarilo ulozit.", ex, detail: indexPath);
                }
            }

            WriteImportXml();
            Log.Info(
                "Import pripraven: " + _pending.Count + " zmen do CS.",
                detail: emitFull ? "full" : "delta",
                duration: 0);
            return new OpenImportConnectionResults(_incomingCustomData);
        }

        public GetImportEntriesResults GetImportEntries(GetImportEntriesRunStep importRunStep)
        {
            if (_pending == null)
                _pending = new List<CSEntryChange>();

            int remaining = _pending.Count - _lastProcessed;
            int take = remaining > _pageSize ? _pageSize : remaining;
            var page = take > 0 ? _pending.GetRange(_lastProcessed, take) : new List<CSEntryChange>();
            _lastProcessed += take;
            bool more = _lastProcessed < _pending.Count;

            return new GetImportEntriesResults
            {
                CSEntries = page,
                MoreToImport = more,
                CustomData = more ? _incomingCustomData : (_newWatermark ?? "")
            };
        }

        public CloseImportConnectionResults CloseImportConnection(CloseImportConnectionRunStep importRunStep)
        {
            bool done = _pending == null || _lastProcessed >= _pending.Count;
            _persons = null;
            _pending = null;
            _rfc = null;
            return new CloseImportConnectionResults(done ? (_newWatermark ?? "") : (_incomingCustomData ?? ""));
        }

        public void OpenExportConnection(
            KeyedCollection<string, ConfigParameter> configParameters,
            Schema types,
            OpenExportConnectionRunStep exportRunStep)
        {
            InitRuntime(configParameters);
            Log.Info("---------------------------- ZACATEK " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + " ----------------------------");
            Log.Info("Export otevren.");
        }

        public PutExportEntriesResults PutExportEntries(IList<CSEntryChange> csentries)
        {
            var results = new PutExportEntriesResults();
            foreach (CSEntryChange item in csentries)
            {
                try
                {
                    ExportOne(item);
                    results.CSEntryChangeResults.Add(
                        CSEntryChangeResult.Create(item.Identifier, null, MAExportError.Success));
                }
                catch (Exception ex)
                {
                    string text = BapiReturn.FormatException(ex);
                    Log.Error("Export " + item.ObjectType + " selhal.", ex, SafeAnchor(item), text);
                    results.CSEntryChangeResults.Add(
                        CSEntryChangeResult.Create(
                            item.Identifier,
                            item.AttributeChanges,
                            MAExportError.ExportErrorCustomContinueRun,
                            "sap-bapi-error",
                            text));
                }
            }
            return results;
        }

        public void CloseExportConnection(CloseExportConnectionRunStep exportRunStep)
        {
            _rfc = null;
        }

        private void ExportOne(CSEntryChange item)
        {
            if (item.ObjectType == "Person")
            {
                Person person = MapPerson(item);
                if (item.ObjectModificationType == ObjectModificationType.Add)
                {
                    _rfc.CreatePerson(person);
                    Log.Info("Vytvoreni uzivatele.", person.EmployeeID);
                }
                else if (item.ObjectModificationType == ObjectModificationType.Update)
                {
                    _rfc.UpdatePerson(person);
                    Log.Info("Aktualizace uzivatele.", person.EmployeeID);
                }
                else if (item.ObjectModificationType == ObjectModificationType.Delete)
                    throw new NotSupportedException("Delete Person neni podporovan (SAP uzivatele se maze lockem).");
                else
                    throw new NotSupportedException("Nepodporovana operace Person: " + item.ObjectModificationType);
                return;
            }

            if (item.ObjectType == "Group")
            {
                string role;
                string user;
                if (item.AnchorAttributes.Count > 0)
                    Group.ParseId(item.AnchorAttributes[0].Value.ToString(), out role, out user);
                else
                    throw new InvalidOperationException("Group nema anchor ID.");

                string validFrom = GetChangeValue(item, "ValidFrom");
                string validTo = GetChangeValue(item, "ValidTo");
                string fullName = GetChangeValue(item, "FullName");
                if (!string.IsNullOrEmpty(fullName))
                    role = fullName;
                string emp = GetChangeValue(item, "EmployeeID");
                if (!string.IsNullOrEmpty(emp))
                    user = emp;

                if (item.ObjectModificationType == ObjectModificationType.Delete)
                {
                    _rfc.RemoveRole(user, role);
                    Log.Info("Odebrani role.", user, role);
                }
                else
                {
                    _rfc.AssignRole(user, role, validFrom, validTo);
                    Log.Info("Prirazeni role.", user, role);
                }
                return;
            }

            throw new NotSupportedException("Neznamy object type: " + item.ObjectType);
        }

        private Person MapPerson(CSEntryChange item)
        {
            var person = new Person();
            if (item.AnchorAttributes.Count > 0)
                person.EmployeeID = item.AnchorAttributes[0].Value.ToString();

            person.EmployeeID = FirstNonEmpty(GetChangeValue(item, "EmployeeID"), person.EmployeeID);
            person.FirstName = GetChangeValue(item, "FirstName");
            person.LastName = GetChangeValue(item, "LastName");
            person.FullName = GetChangeValue(item, "FullName");
            person.ValidFrom = GetChangeValue(item, "ValidFrom");
            person.ValidTo = GetChangeValue(item, "ValidTo");
            person.EMail = GetChangeValue(item, "EMail");
            person.Password = GetChangeValue(item, "Password");
            person.FUNCTION = GetChangeValue(item, "FUNCTION");
            person.DEPARTMENT = GetChangeValue(item, "DEPARTMENT");
            person.ALIAS = GetChangeValue(item, "ALIAS");
            person.USERGROUP = GetChangeValue(item, "USERGROUP");
            person.LOCAL_LOCK = GetChangeValue(item, "LOCAL_LOCK");
            person.GLOB_LOCK = GetChangeValue(item, "GLOB_LOCK");
            person.SPLD = GetChangeValue(item, "SPLD");
            person.SPDB = GetChangeValue(item, "SPDB");
            person.LANGU = GetChangeValue(item, "LANGU");
            return person;
        }

        private void BuildFullEntries()
        {
            foreach (Person item in _persons)
            {
                foreach (Group gr in ImportGroups(item))
                    _pending.Add(CreateGroupEntry(ObjectModificationType.Add, item.EmployeeID, gr.FullName, gr.ValidFrom, gr.ValidTo, gr.Description));
                _pending.Add(CreatePersonEntry(ObjectModificationType.Add, item));
            }
        }

        private void BuildDeltaEntries(RoleIndex previous, RoleIndex current)
        {
            var currentIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (Person item in _persons)
            {
                if (string.IsNullOrWhiteSpace(item.EmployeeID))
                    continue;
                currentIds.Add(item.EmployeeID);

                bool isNew = !previous.Users.ContainsKey(item.EmployeeID);
                if (isNew || LastModGreater(item.LastModificationDate, _incomingCustomData))
                {
                    _pending.Add(CreatePersonEntry(
                        isNew ? ObjectModificationType.Add : ObjectModificationType.Replace,
                        item));
                }
            }

            foreach (string oldUser in previous.Users.Keys)
            {
                if (currentIds.Contains(oldUser))
                    continue;

                _pending.Add(CreateDelete("Person", oldUser));
                foreach (string role in previous.Users[oldUser].Keys)
                    _pending.Add(CreateDelete("Group", Group.BuildId(role, oldUser)));
            }

            var descriptions = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (Person item in _persons)
            {
                foreach (Group gr in ImportGroups(item))
                    descriptions[Group.BuildId(gr.FullName, item.EmployeeID)] = gr.Description;
            }

            foreach (RoleChange change in RoleIndex.Diff(previous, current))
            {
                if (change.Kind == RoleChangeKind.Delete)
                {
                    if (!currentIds.Contains(change.User))
                        continue;
                    _pending.Add(CreateDelete("Group", Group.BuildId(change.Role, change.User)));
                    continue;
                }

                string id = Group.BuildId(change.Role, change.User);
                string description;
                descriptions.TryGetValue(id, out description);
                _pending.Add(CreateGroupEntry(
                    change.Kind == RoleChangeKind.Add ? ObjectModificationType.Add : ObjectModificationType.Replace,
                    change.User,
                    change.Role,
                    change.From,
                    change.To,
                    description));
            }
        }

        private IEnumerable<Group> ImportGroups(Person item)
        {
            if (item.UserGroups == null)
                yield break;

            foreach (Group gr in item.UserGroups)
            {
                if (string.IsNullOrWhiteSpace(gr.FullName))
                    continue;
                if (_bapiRoles != null && !_bapiRoles.Contains(gr.FullName))
                    continue;
                yield return gr;
            }
        }

        private CSEntryChange CreatePersonEntry(ObjectModificationType modification, Person item)
        {
            CSEntryChange person = CSEntryChange.Create();
            person.ObjectModificationType = modification;
            person.ObjectType = "Person";
            AddString(person, "ID", item.EmployeeID);
            AddString(person, "EmployeeID", item.EmployeeID);
            AddString(person, "FirstName", item.FirstName);
            AddString(person, "LastName", item.LastName);
            AddString(person, "FullName", item.FullName);
            AddString(person, "ValidFrom", item.ValidFrom);
            AddString(person, "ValidTo", item.ValidTo);
            AddString(person, "NickName", item.NickName);
            AddString(person, "EMail", item.EMail);
            AddString(person, "LastModificationDate", item.LastModificationDate);
            AddString(person, "ModDate", item.ModDate);
            AddString(person, "LTime", item.LTime);
            AddString(person, "WRNG_LOGON", item.WRNG_LOGON);
            AddString(person, "LOCAL_LOCK", item.LOCAL_LOCK);
            AddString(person, "GLOB_LOCK", item.GLOB_LOCK);
            AddString(person, "NO_USER_PW", item.NO_USER_PW);
            AddString(person, "FUNCTION", item.FUNCTION);
            AddString(person, "DEPARTMENT", item.DEPARTMENT);
            AddString(person, "USERGROUP", item.USERGROUP);
            AddString(person, "SPLD", item.SPLD);
            AddString(person, "SPDB", item.SPDB);
            AddString(person, "LANGU", item.LANGU);
            AddString(person, "ALIAS", item.ALIAS);
            person.AttributeChanges.Add(AttributeChange.CreateAttributeAdd("BAPIEnabled", true));
            person.AttributeChanges.Add(AttributeChange.CreateAttributeAdd("GroupsCount", item.UserGroups == null ? 0 : item.UserGroups.Count));

            var groups = new List<string>();
            if (item.UserGroups != null)
            {
                foreach (Group gr in item.UserGroups)
                {
                    if (!string.IsNullOrEmpty(gr.FullName))
                        groups.Add(gr.FullName);
                }
            }
            if (groups.Count > 0)
                person.AttributeChanges.Add(AttributeChange.CreateAttributeAdd("ACTIVITYGROUPS", groups.ToArray()));

            return person;
        }

        private static CSEntryChange CreateGroupEntry(
            ObjectModificationType modification,
            string user,
            string role,
            string validFrom,
            string validTo,
            string description)
        {
            CSEntryChange g = CSEntryChange.Create();
            g.ObjectModificationType = modification;
            g.ObjectType = "Group";
            AddString(g, "ID", Group.BuildId(role, user));
            AddString(g, "EmployeeID", user);
            AddString(g, "FullName", role);
            AddString(g, "ValidFrom", validFrom);
            AddString(g, "ValidTo", validTo);
            AddString(g, "Description", description);
            g.AttributeChanges.Add(AttributeChange.CreateAttributeAdd("BAPIEnabled", true));
            return g;
        }

        private static CSEntryChange CreateDelete(string objectType, string anchor)
        {
            CSEntryChange entry = CSEntryChange.Create();
            entry.ObjectModificationType = ObjectModificationType.Delete;
            entry.ObjectType = objectType;
            entry.AnchorAttributes.Add(AnchorAttribute.Create("ID", anchor));
            return entry;
        }

        private string ResolvedXmlPath()
        {
            if (string.IsNullOrWhiteSpace(_xmlFile))
                return null;

            string path = _xmlFile.Trim();
            if (Directory.Exists(path) || path.EndsWith("\\", StringComparison.Ordinal))
                path = Path.Combine(path, "SAP_BAPI.xml");
            return path;
        }

        private static string MaxLastModified(IEnumerable<Person> persons)
        {
            string max = "";
            if (persons == null)
                return max;

            foreach (Person person in persons)
            {
                if (LastModGreater(person.LastModificationDate, max))
                    max = person.LastModificationDate;
            }
            return max ?? "";
        }

        private static bool LastModGreater(string value, string watermark)
        {
            if (string.IsNullOrWhiteSpace(value))
                return true;
            if (string.IsNullOrWhiteSpace(watermark))
                return true;

            DateTime a;
            DateTime b;
            if (TryParseLastMod(value, out a) && TryParseLastMod(watermark, out b))
                return a > b;

            return string.Compare(value.Trim(), watermark.Trim(), StringComparison.Ordinal) > 0;
        }

        private static bool TryParseLastMod(string value, out DateTime result)
        {
            string[] formats =
            {
                "yyyy-MM-dd HH:mm:ss",
                "yyyy-MM-dd HH:mm:ss.fff",
                "yyyy-MM-dd",
                "yyyyMMdd HHmmss",
                "yyyyMMdd"
            };
            return DateTime.TryParseExact(
                value.Trim(),
                formats,
                CultureInfo.InvariantCulture,
                DateTimeStyles.None,
                out result);
        }

        private void WriteImportXml()
        {
            string path = ResolvedXmlPath();
            if (string.IsNullOrEmpty(path))
                return;

            try
            {
                string dir = Path.GetDirectoryName(path);
                if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                    Directory.CreateDirectory(dir);

                var serializer = new XmlSerializer(typeof(List<Person>));
                using (var writer = new StreamWriter(path, false, Encoding.UTF8))
                    serializer.Serialize(writer, _persons ?? new List<Person>());

                Log.Info("Import XML ulozen.", detail: path);
            }
            catch (Exception ex)
            {
                Log.Error("Import XML se nepodarilo ulozit.", ex, detail: path);
            }
        }

        private void InitRuntime(KeyedCollection<string, ConfigParameter> cfg)
        {
            Log.Configure(GetString(cfg, CP_LOGFILE, ""));
            _xmlFile = GetString(cfg, CP_XML, "");
            var settings = new SapSettings
            {
                AppServerHost = GetString(cfg, CP_HOST, ""),
                SystemNumber = GetString(cfg, CP_SYSNR, "00"),
                SystemId = GetString(cfg, CP_SYSID, ""),
                Client = GetString(cfg, CP_CLIENT, "010"),
                Language = GetString(cfg, CP_LANG, "EN"),
                User = GetString(cfg, CP_USER, ""),
                Password = GetPassword(cfg),
                DependantRoles = SplitRoles(GetString(cfg, CP_DEP, "ZRFCCALL"))
            };

            string dest = SapDestination.Register(settings);
            _rfc = new SapRfc(dest, settings.DependantRoles);
            _rfc.Ping();
        }

        private static HashSet<string> LoadBapiRoles(string connectionString)
        {
            var roles = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (string.IsNullOrWhiteSpace(connectionString))
                return null;

            try
            {
                using (var conn = new SqlConnection(connectionString))
                {
                    conn.Open();
                    using (var cmd = new SqlCommand(
                        "SELECT externalRoleCode FROM [FIMSynchronizationService].[dbo].[mms_metaverse] WITH (NOLOCK) " +
                        "WHERE externalRoleCode IS NOT NULL AND object_type = 'Group' " +
                        "AND applicationCode LIKE 'SAP%' " +
                        "AND (applicationModulCode NOT LIKE 'RDPV%' OR applicationModulCode IS NULL)", conn))
                    using (SqlDataReader reader = cmd.ExecuteReader())
                    {
                        while (reader.Read())
                        {
                            if (!reader.IsDBNull(0))
                                roles.Add(reader.GetValue(0).ToString().Trim());
                        }
                    }
                }
                Log.Info("BAPI role z MV: " + roles.Count);
                return roles;
            }
            catch (Exception ex)
            {
                Log.Warn("Filtr Group z MV selhal, importuji vsechny role.", detail: ex.Message);
                return null;
            }
        }

        private static void AddString(CSEntryChange entry, string name, string value)
        {
            if (!string.IsNullOrWhiteSpace(value))
                entry.AttributeChanges.Add(AttributeChange.CreateAttributeAdd(name, value));
        }

        private static string GetChangeValue(CSEntryChange item, string name)
        {
            if (!item.AttributeChanges.Contains(name))
                return null;

            AttributeChange change = item.AttributeChanges[name];
            if (change.ModificationType == AttributeModificationType.Delete)
                return null;

            ValueChange add = change.ValueChanges.Cast<ValueChange>()
                .FirstOrDefault(v => v.ModificationType == ValueModificationType.Add);
            if (add != null && add.Value != null)
                return add.Value.ToString();
            if (change.ValueChanges.Count > 0 && change.ValueChanges[0].Value != null)
                return change.ValueChanges[0].Value.ToString();
            return null;
        }

        private static string FirstNonEmpty(string a, string b)
        {
            return string.IsNullOrWhiteSpace(a) ? b : a;
        }

        private static string SafeAnchor(CSEntryChange item)
        {
            try
            {
                if (item.AnchorAttributes.Count > 0 && item.AnchorAttributes[0].Value != null)
                    return item.AnchorAttributes[0].Value.ToString();
            }
            catch
            {
            }
            return "?";
        }

        private static string[] SplitRoles(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return new string[0];
            return value.Split(new[] { ';', ',' }, StringSplitOptions.RemoveEmptyEntries);
        }

        private static string GetString(KeyedCollection<string, ConfigParameter> cfg, string name, string fallback)
        {
            if (cfg == null || !cfg.Contains(name) || cfg[name].Value == null)
                return fallback;
            return cfg[name].Value;
        }

        private static string GetPassword(KeyedCollection<string, ConfigParameter> cfg)
        {
            if (cfg == null || !cfg.Contains(CP_PASSWORD))
                return "";
            try
            {
                if (cfg[CP_PASSWORD].SecureValue != null)
                    return new System.Net.NetworkCredential("", cfg[CP_PASSWORD].SecureValue).Password;
            }
            catch
            {
            }
            return cfg[CP_PASSWORD].Value ?? "";
        }

        private static ParameterValidationResult Fail(string parameter, string message)
        {
            return new ParameterValidationResult
            {
                Code = ParameterValidationResultCode.Failure,
                ErrorMessage = message,
                ErrorParameter = parameter
            };
        }
    }
}
