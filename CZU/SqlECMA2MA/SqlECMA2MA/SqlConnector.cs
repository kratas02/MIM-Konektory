using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Data;
using System.Data.SqlClient;
using System.Globalization;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.MetadirectoryServices;

// Alias pro SchemaType - vyhne se konfliktu s System.Data.SchemaType
using MmsSchemaType = Microsoft.MetadirectoryServices.SchemaType;

namespace SqlECMA2MA
{
    /// <summary>
    /// Read-only ECMA2 konektor nad SQL Server pohledem idm.vwIDM_Cards.
    /// Person: jeden objekt na UIC, multivalued reference card na Card.
    /// Card: jeden objekt na pk_id, vsechny sloupce pohledu.
    /// </summary>
    public class SqlConnector :
        IMAExtensible2GetCapabilities,
        IMAExtensible2GetSchema,
        IMAExtensible2GetParameters,
        IMAExtensible2GetPartitions,
        IMAExtensible2CallImport
    {
        private const string CP_SERVER     = "SQL Server";
        private const string CP_DATABASE   = "Database";
        private const string CP_INTEGRATED = "Windows authentication";
        private const string CP_USER       = "SQL login";
        private const string CP_PASSWORD   = "SQL password";
        private const string CP_VIEW       = "Cards view";

        private const string CP_MV_FILTER       = "Only metaverse UIC";
        private const string CP_SYNC_SERVER     = "Sync SQL Server";
        private const string CP_SYNC_DATABASE   = "Sync database";
        private const string CP_SYNC_INTEGRATED = "Sync Windows authentication";
        private const string CP_SYNC_USER       = "Sync SQL login";
        private const string CP_SYNC_PASSWORD   = "Sync SQL password";
        private const string CP_MV_COLUMN       = "Metaverse UIC column";
        private const string CP_MV_OBJECT_TYPE  = "Metaverse object type";

        // ECMA2 schema nema DateTime. Datum se uklada jako string v tomto tvaru,
        // aby ho inbound flow mohl prevest na DateTime v metaverse.
        private const string DateTimeFormat = "yyyy-MM-dd HH:mm:ss";

        private const int CommandTimeoutSeconds = 300;

        private static readonly Regex ViewNameRegex = new Regex(
            @"^[A-Za-z_][A-Za-z0-9_]*(\.[A-Za-z_][A-Za-z0-9_]*){0,2}$",
            RegexOptions.CultureInvariant | RegexOptions.Compiled);

        private static readonly Regex IdentifierRegex = new Regex(
            @"^[A-Za-z_][A-Za-z0-9_]*$",
            RegexOptions.CultureInvariant | RegexOptions.Compiled);

        // ── Stav importu ──────────────────────────────────────────────────────────
        private KeyedCollection<string, ConfigParameter> _config;
        private SqlConnection _conn;
        private string _objectType;
        private string _viewSql;
        private bool _filterToMetaverse;
        private int _viewRows;

        // Card: stream radku
        private SqlDataReader _reader;

        // Person: UIC -> pk_id karet, strankovane z pameti
        private List<string> _personUics;
        private Dictionary<string, List<string>> _cardsByUic;
        private int _personIndex;

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

        // ── Partitions ────────────────────────────────────────────────────────────

        public IList<Partition> GetPartitions(KeyedCollection<string, ConfigParameter> configParameters)
        {
            var list = new List<Partition>();
            foreach (string name in SqlConfig.ObjectTypes)
            {
                list.Add(Partition.Create(
                    SqlConfig.NameToGuid(name),
                    "CN=" + name,
                    name));
            }

            Log("INFO", "GetPartitions: " + list.Count + " partitions");
            return list;
        }

        // ── Parametry ─────────────────────────────────────────────────────────────

        public IList<ConfigParameterDefinition> GetConfigParameters(
            KeyedCollection<string, ConfigParameter> configParameters,
            ConfigParameterPage page)
        {
            var list = new List<ConfigParameterDefinition>();

            if (page == ConfigParameterPage.Connectivity)
            {
                list.Add(ConfigParameterDefinition.CreateStringParameter(CP_SERVER, "", ""));
                list.Add(ConfigParameterDefinition.CreateStringParameter(
                    CP_DATABASE, "", SqlConfig.DefaultDatabase));
                list.Add(ConfigParameterDefinition.CreateCheckBoxParameter(CP_INTEGRATED, false));
                list.Add(ConfigParameterDefinition.CreateStringParameter(CP_USER, "", ""));
                list.Add(ConfigParameterDefinition.CreateEncryptedStringParameter(CP_PASSWORD, ""));
            }
            else if (page == ConfigParameterPage.Global)
            {
                list.Add(ConfigParameterDefinition.CreateStringParameter(
                    CP_VIEW, "", SqlConfig.DefaultView));
                list.Add(ConfigParameterDefinition.CreateDividerParameter());
                list.Add(ConfigParameterDefinition.CreateLabelParameter(
                    "Person: anchor UIC, atribut uic, ciselne PK_ZAM a multivalued reference card. " +
                    "Card: anchor pk_id (GUID) a sloupce pohledu. " +
                    "validFrom a validTo jsou ve schema string (ECMA2 nema DateTime), format " +
                    DateTimeFormat + "."));
                list.Add(ConfigParameterDefinition.CreateDividerParameter());
                list.Add(ConfigParameterDefinition.CreateCheckBoxParameter(CP_MV_FILTER, true));
                list.Add(ConfigParameterDefinition.CreateLabelParameter(
                    "Pri zapnutem filtru se z metaverse nacte sloupec UIC (PK_ZAM) " +
                    "a pohled vrati jen Person a karty s timto UIC."));
                list.Add(ConfigParameterDefinition.CreateStringParameter(
                    CP_SYNC_SERVER, "", SqlConfig.DefaultSyncServer));
                list.Add(ConfigParameterDefinition.CreateStringParameter(
                    CP_SYNC_DATABASE, "", SqlConfig.DefaultSyncDatabase));
                list.Add(ConfigParameterDefinition.CreateCheckBoxParameter(CP_SYNC_INTEGRATED, true));
                list.Add(ConfigParameterDefinition.CreateStringParameter(CP_SYNC_USER, "", ""));
                list.Add(ConfigParameterDefinition.CreateEncryptedStringParameter(CP_SYNC_PASSWORD, ""));
                list.Add(ConfigParameterDefinition.CreateStringParameter(
                    CP_MV_COLUMN, "", SqlConfig.DefaultMvColumn));
                list.Add(ConfigParameterDefinition.CreateStringParameter(
                    CP_MV_OBJECT_TYPE, "", SqlConfig.DefaultMvObjectType));
            }

            return list;
        }

        public ParameterValidationResult ValidateConfigParameters(
            KeyedCollection<string, ConfigParameter> configParameters,
            ConfigParameterPage page)
        {
            if (page == ConfigParameterPage.Connectivity)
            {
                string server = GetParam(configParameters, CP_SERVER, "").Trim();
                if (server.Length == 0)
                    return Failure(CP_SERVER, "SQL Server musi byt vyplneny.");
                if (server.IndexOf(';') >= 0 || server.IndexOf('=') >= 0)
                    return Failure(CP_SERVER, "SQL Server nesmi obsahovat ';' ani '='.");

                string database = GetParam(configParameters, CP_DATABASE, "").Trim();
                if (database.Length == 0)
                    return Failure(CP_DATABASE, "Database musi byt vyplnena.");
                if (!ViewNameRegex.IsMatch(database))
                    return Failure(CP_DATABASE, "Database smi obsahovat jen pismena, cislice a podtrzitko.");

                if (!GetBool(configParameters, CP_INTEGRATED, false))
                {
                    if (GetParam(configParameters, CP_USER, "").Trim().Length == 0)
                        return Failure(CP_USER, "Pri SQL autentizaci musi byt vyplneny SQL login.");
                }
            }
            else if (page == ConfigParameterPage.Global)
            {
                string view = GetParam(configParameters, CP_VIEW, SqlConfig.DefaultView).Trim();
                if (!ViewNameRegex.IsMatch(view))
                    return Failure(CP_VIEW, "Cards view musi byt ve tvaru view, schema.view nebo db.schema.view.");

                if (GetBool(configParameters, CP_MV_FILTER, true))
                {
                    string syncServer = GetParam(configParameters, CP_SYNC_SERVER, "").Trim();
                    if (syncServer.Length == 0)
                        return Failure(CP_SYNC_SERVER, "Sync SQL Server musi byt vyplneny.");
                    if (syncServer.IndexOf(';') >= 0 || syncServer.IndexOf('=') >= 0)
                        return Failure(CP_SYNC_SERVER, "Sync SQL Server nesmi obsahovat ';' ani '='.");

                    string syncDatabase = GetParam(configParameters, CP_SYNC_DATABASE, "").Trim();
                    if (!ViewNameRegex.IsMatch(syncDatabase))
                        return Failure(CP_SYNC_DATABASE, "Sync database smi obsahovat jen pismena, cislice a podtrzitko.");

                    if (!GetBool(configParameters, CP_SYNC_INTEGRATED, true))
                    {
                        if (GetParam(configParameters, CP_SYNC_USER, "").Trim().Length == 0)
                            return Failure(CP_SYNC_USER, "Pri SQL autentizaci metaverse musi byt vyplneny Sync SQL login.");
                    }

                    string column = GetParam(configParameters, CP_MV_COLUMN, SqlConfig.DefaultMvColumn).Trim();
                    if (!IdentifierRegex.IsMatch(column))
                        return Failure(CP_MV_COLUMN, "Metaverse UIC column smi obsahovat jen pismena, cislice a podtrzitko.");

                    string objectType = GetParam(configParameters, CP_MV_OBJECT_TYPE, "").Trim();
                    if (objectType.Length == 0 || objectType.Length > 100)
                        return Failure(CP_MV_OBJECT_TYPE, "Metaverse object type musi byt vyplneny.");
                }
            }

            return new ParameterValidationResult();
        }

        // ── Schema ────────────────────────────────────────────────────────────────

        public Schema GetSchema(KeyedCollection<string, ConfigParameter> configParameters)
        {
            var schema = Schema.Create();

            var person = MmsSchemaType.Create(SqlConfig.PersonType, false);
            person.Attributes.Add(SchemaAttribute.CreateSingleValuedAttribute("uic", AttributeType.String));
            person.Attributes.Add(SchemaAttribute.CreateSingleValuedAttribute("PK_ZAM", AttributeType.Integer));
            person.Attributes.Add(SchemaAttribute.CreateMultiValuedAttribute(
                SqlConfig.CardReference, AttributeType.Reference));
            schema.Types.Add(person);

            var card = MmsSchemaType.Create(SqlConfig.CardType, false);
            card.Attributes.Add(SchemaAttribute.CreateSingleValuedAttribute("pk_id", AttributeType.String));
            card.Attributes.Add(SchemaAttribute.CreateSingleValuedAttribute("cardType", AttributeType.String));
            card.Attributes.Add(SchemaAttribute.CreateSingleValuedAttribute("uic", AttributeType.String));
            card.Attributes.Add(SchemaAttribute.CreateSingleValuedAttribute("passnumber", AttributeType.String));
            card.Attributes.Add(SchemaAttribute.CreateSingleValuedAttribute("pass", AttributeType.String));
            card.Attributes.Add(SchemaAttribute.CreateSingleValuedAttribute("validFrom", AttributeType.String));
            card.Attributes.Add(SchemaAttribute.CreateSingleValuedAttribute("validTo", AttributeType.String));
            card.Attributes.Add(SchemaAttribute.CreateSingleValuedAttribute("deleted", AttributeType.Boolean));
            schema.Types.Add(card);

            Log("INFO", "GetSchema: Person (uic, PK_ZAM, card), Card (pk_id, cardType, uic, passnumber, pass, validFrom, validTo, deleted)");
            return schema;
        }

        // ── Import ────────────────────────────────────────────────────────────────

        public OpenImportConnectionResults OpenImportConnection(
            KeyedCollection<string, ConfigParameter> configParameters,
            Schema types,
            OpenImportConnectionRunStep runStep)
        {
            _config     = configParameters;
            _objectType = runStep.StepPartition != null ? runStep.StepPartition.Name : null;
            if (string.IsNullOrEmpty(_objectType))
                throw new InvalidOperationException("Import nema partition.");

            bool known = false;
            foreach (string name in SqlConfig.ObjectTypes)
            {
                if (name.Equals(_objectType, StringComparison.OrdinalIgnoreCase))
                {
                    _objectType = name;
                    known = true;
                    break;
                }
            }
            if (!known)
                throw new InvalidOperationException("Neznama partition '" + _objectType + "'.");

            _viewSql = QuoteView(GetParam(configParameters, CP_VIEW, SqlConfig.DefaultView));
            _filterToMetaverse = GetBool(configParameters, CP_MV_FILTER, true);
            _viewRows = 0;
            _conn     = BuildConnection(configParameters);

            try
            {
                _conn.Open();
                Log("INFO", "OpenImportConnection: partition=" + _objectType + " view=" + _viewSql +
                    " metaverseFilter=" + _filterToMetaverse);

                if (_filterToMetaverse)
                    LoadConnectedUics();

                if (_objectType == SqlConfig.PersonType)
                    OpenPersonImport();
                else
                    OpenCardImport();
            }
            catch
            {
                DisposeImport();
                throw;
            }

            return new OpenImportConnectionResults();
        }

        public GetImportEntriesResults GetImportEntries(GetImportEntriesRunStep runStep)
        {
            return _objectType == SqlConfig.PersonType
                ? GetPersonEntries()
                : GetCardEntries();
        }

        public CloseImportConnectionResults CloseImportConnection(CloseImportConnectionRunStep runStep)
        {
            Log("INFO", "CloseImportConnection: partition=" + _objectType + " rows=" + _viewRows);
            DisposeImport();
            return new CloseImportConnectionResults(string.Empty);
        }

        private void OpenCardImport()
        {
            string sql = _filterToMetaverse
                ? "SELECT v.[pk_id], v.[type] AS [cardType], v.[uic], v.[passnumber], v.[pass], " +
                  "v.[validFrom], v.[validTo], v.[deleted] FROM " + _viewSql + " v " +
                  "INNER JOIN #ConnectedUic u ON u.uic = CONVERT(nvarchar(128), v.[uic]) " +
                  "ORDER BY v.[pk_id]"
                : "SELECT [pk_id], [type] AS [cardType], [uic], [passnumber], [pass], " +
                  "[validFrom], [validTo], [deleted] FROM " + _viewSql + " ORDER BY [pk_id]";

            Log("INFO", "OpenCardImport: " + sql);
            var cmd = new SqlCommand(sql, _conn) { CommandTimeout = CommandTimeoutSeconds };
            _reader = cmd.ExecuteReader();
        }

        private void OpenPersonImport()
        {
            // Jeden pruchod pohledem. Person bez pk_id se take zalozi, jen nema reference.
            string sql = _filterToMetaverse
                ? "SELECT v.[uic], v.[pk_id] FROM " + _viewSql + " v " +
                  "INNER JOIN #ConnectedUic u ON u.uic = CONVERT(nvarchar(128), v.[uic]) " +
                  "WHERE v.[uic] IS NOT NULL ORDER BY v.[uic], v.[pk_id]"
                : "SELECT [uic], [pk_id] FROM " + _viewSql +
                  " WHERE [uic] IS NOT NULL ORDER BY [uic], [pk_id]";

            Log("INFO", "OpenPersonImport: " + sql);
            _cardsByUic = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);

            int rows = 0;
            using (var cmd = new SqlCommand(sql, _conn) { CommandTimeout = CommandTimeoutSeconds })
            using (var rdr = cmd.ExecuteReader())
            {
                while (rdr.Read())
                {
                    rows++;
                    string uic = ReadString(rdr, 0);
                    if (uic == null) continue;

                    List<string> cards;
                    if (!_cardsByUic.TryGetValue(uic, out cards))
                    {
                        cards = new List<string>();
                        _cardsByUic[uic] = cards;
                    }

                    string pk = ReadGuid(rdr, 1);
                    if (pk != null && !cards.Contains(pk))
                        cards.Add(pk);
                }
            }

            foreach (var cards in _cardsByUic.Values)
                cards.Sort(StringComparer.Ordinal);

            _personUics  = new List<string>(_cardsByUic.Keys);
            _personUics.Sort(StringComparer.OrdinalIgnoreCase);
            _personIndex = 0;
            _viewRows    = rows;

            Log("INFO", "OpenPersonImport: " + _personUics.Count + " UIC, " + rows + " radku z pohledu");
        }

        /// <summary>
        /// Jednou nacte DISTINCT PK_ZAM z metaverse a vlozi ho do #ConnectedUic
        /// na spojeni pohledu karet. Prazdny seznam import prerusi.
        /// </summary>
        private void LoadConnectedUics()
        {
            string column = QuoteIdentifier(GetParam(_config, CP_MV_COLUMN, SqlConfig.DefaultMvColumn));
            string objectType = GetParam(_config, CP_MV_OBJECT_TYPE, SqlConfig.DefaultMvObjectType).Trim();
            string sql =
                "SELECT DISTINCT " + column +
                " FROM dbo.mms_metaverse WITH (NOLOCK)" +
                " WHERE object_type = @objectType AND " + column + " IS NOT NULL";

            var uics = new List<string>();
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            using (var sync = BuildSyncConnection(_config))
            {
                sync.Open();
                using (var cmd = new SqlCommand(sql, sync) { CommandTimeout = CommandTimeoutSeconds })
                {
                    cmd.Parameters.Add("@objectType", SqlDbType.NVarChar, 100).Value = objectType;
                    using (var rdr = cmd.ExecuteReader())
                    {
                        while (rdr.Read())
                        {
                            string uic = ReadString(rdr, 0);
                            if (uic == null) continue;
                            if (uic.Length > 128)
                                throw new InvalidOperationException(
                                    "Hodnota " + column + " je delsi nez 128 znaku.");
                            if (seen.Add(uic))
                                uics.Add(uic);
                        }
                    }
                }
            }

            if (uics.Count == 0)
                throw new InvalidOperationException(
                    "Metaverse nevratilo zadne hodnoty sloupce " + column +
                    " pro object type '" + objectType +
                    "'. Import je prerusen, aby full import nesmazal connector space.");

            string collation = ReadCardsCollation();
            string create =
                "CREATE TABLE #ConnectedUic (" +
                "uic nvarchar(128) COLLATE " + collation + " NOT NULL PRIMARY KEY)";
            using (var cmd = new SqlCommand(create, _conn) { CommandTimeout = CommandTimeoutSeconds })
                cmd.ExecuteNonQuery();

            using (var table = new DataTable())
            {
                table.Columns.Add("uic", typeof(string)).MaxLength = 128;
                foreach (string uic in uics)
                    table.Rows.Add(uic);

                using (var bulk = new SqlBulkCopy(_conn))
                {
                    bulk.DestinationTableName = "#ConnectedUic";
                    bulk.BatchSize = 5000;
                    bulk.BulkCopyTimeout = CommandTimeoutSeconds;
                    bulk.ColumnMappings.Add("uic", "uic");
                    bulk.WriteToServer(table);
                }
            }

            Log("INFO", "LoadConnectedUics: " + uics.Count + " hodnot " + column +
                " pro object type '" + objectType + "'");
        }

        private string ReadCardsCollation()
        {
            using (var cmd = new SqlCommand(
                "SELECT collation_name FROM sys.databases WHERE name = DB_NAME()", _conn))
            {
                object result = cmd.ExecuteScalar();
                string collation = result as string;
                if (string.IsNullOrEmpty(collation) || !IdentifierRegex.IsMatch(collation))
                    throw new InvalidOperationException("Nelze nacist collation databaze karet.");
                return collation;
            }
        }

        private GetImportEntriesResults GetCardEntries()
        {
            var entries = new List<CSEntryChange>();
            int count   = 0;
            int skipped = 0;
            bool first  = true;

            while (count < ImportDefaultPageSize && _reader.Read())
            {
                count++;
                _viewRows++;
                string pk = ReadGuid(_reader, 0);
                if (pk == null)
                {
                    skipped++;
                    continue;
                }

                var entry = CSEntryChange.Create();
                entry.ObjectModificationType = ObjectModificationType.Add;
                entry.ObjectType             = SqlConfig.CardType;
                entry.DN                     = ToDn(pk, SqlConfig.CardType);

                AddString(entry, "pk_id", pk);
                AddString(entry, "cardType", ReadString(_reader, 1));
                AddString(entry, "uic", ReadString(_reader, 2));
                AddString(entry, "passnumber", ReadString(_reader, 3));
                AddString(entry, "pass", ReadString(_reader, 4));
                AddString(entry, "validFrom", ReadDate(_reader, 5));
                AddString(entry, "validTo", ReadDate(_reader, 6));

                bool? deleted = ReadBool(_reader, 7);
                if (deleted.HasValue)
                    entry.AttributeChanges.Add(AttributeChange.CreateAttributeAdd("deleted", deleted.Value));

                if (first)
                {
                    first = false;
                    Log("INFO", "GetCardEntries: prvni radek pk_id=" + pk);
                }

                entries.Add(entry);
            }

            if (skipped > 0)
                Log("WARN", "GetCardEntries: preskoceno " + skipped + " radku bez pk_id");

            return new GetImportEntriesResults
            {
                CSEntries    = entries,
                MoreToImport = count == ImportDefaultPageSize
            };
        }

        private GetImportEntriesResults GetPersonEntries()
        {
            var entries = new List<CSEntryChange>();
            int count   = 0;
            bool first  = true;

            while (count < ImportDefaultPageSize && _personIndex < _personUics.Count)
            {
                string uic = _personUics[_personIndex++];
                count++;

                var entry = CSEntryChange.Create();
                entry.ObjectModificationType = ObjectModificationType.Add;
                entry.ObjectType             = SqlConfig.PersonType;
                entry.DN                     = ToDn(uic, SqlConfig.PersonType);
                AddString(entry, "uic", uic);

                long pkZam;
                if (long.TryParse(uic, NumberStyles.Integer, CultureInfo.InvariantCulture, out pkZam))
                    entry.AttributeChanges.Add(AttributeChange.CreateAttributeAdd("PK_ZAM", pkZam));

                List<string> cards = _cardsByUic[uic];
                if (cards.Count > 0)
                {
                    var refs = new List<object>(cards.Count);
                    foreach (string pk in cards)
                        refs.Add(ToDn(pk, SqlConfig.CardType));
                    entry.AttributeChanges.Add(
                        AttributeChange.CreateAttributeAdd(SqlConfig.CardReference, refs));
                }

                if (first)
                {
                    first = false;
                    Log("INFO", "GetPersonEntries: prvni UIC=" + uic + " cards=" + cards.Count);
                }

                entries.Add(entry);
            }

            return new GetImportEntriesResults
            {
                CSEntries    = entries,
                MoreToImport = count == ImportDefaultPageSize
            };
        }

        // ── Cteni hodnot ──────────────────────────────────────────────────────────

        private static string ReadString(IDataReader rdr, int index)
        {
            if (rdr.IsDBNull(index)) return null;
            object value = rdr.GetValue(index);
            if (value == null || value == DBNull.Value) return null;

            string text = value is IFormattable formattable
                ? formattable.ToString(null, CultureInfo.InvariantCulture)
                : value.ToString();

            if (string.IsNullOrWhiteSpace(text)) return null;
            return text.Trim();
        }

        /// <summary>GUID ve tvaru D (male znaky), aby DN karty a reference sedely.</summary>
        private static string ReadGuid(IDataReader rdr, int index)
        {
            if (rdr.IsDBNull(index)) return null;
            object value = rdr.GetValue(index);
            if (value == null || value == DBNull.Value) return null;
            if (value is Guid guid) return guid.ToString("D");

            string text = Convert.ToString(value, CultureInfo.InvariantCulture);
            if (string.IsNullOrWhiteSpace(text)) return null;
            text = text.Trim();

            Guid parsed;
            if (Guid.TryParse(text, out parsed))
                return parsed.ToString("D");

            return text;
        }

        private static string ReadDate(IDataReader rdr, int index)
        {
            if (rdr.IsDBNull(index)) return null;
            object value = rdr.GetValue(index);
            if (value == null || value == DBNull.Value) return null;

            if (value is DateTime dateTime)
                return dateTime.ToString(DateTimeFormat, CultureInfo.InvariantCulture);

            var offset = value as DateTimeOffset?;
            if (offset.HasValue)
                return offset.Value.DateTime.ToString(DateTimeFormat, CultureInfo.InvariantCulture);

            string text = Convert.ToString(value, CultureInfo.InvariantCulture);
            if (string.IsNullOrWhiteSpace(text)) return null;

            DateTime parsed;
            if (DateTime.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.None, out parsed))
                return parsed.ToString(DateTimeFormat, CultureInfo.InvariantCulture);

            return text.Trim();
        }

        private static bool? ReadBool(IDataReader rdr, int index)
        {
            if (rdr.IsDBNull(index)) return null;
            object value = rdr.GetValue(index);
            if (value == null || value == DBNull.Value) return null;
            if (value is bool flag) return flag;

            if (value is byte || value is short || value is int || value is long)
                return Convert.ToInt64(value, CultureInfo.InvariantCulture) != 0;

            string text = Convert.ToString(value, CultureInfo.InvariantCulture);
            if (string.IsNullOrWhiteSpace(text)) return null;
            text = text.Trim();

            if (text == "1" || text.Equals("true", StringComparison.OrdinalIgnoreCase)) return true;
            if (text == "0" || text.Equals("false", StringComparison.OrdinalIgnoreCase)) return false;
            return null;
        }

        private static void AddString(CSEntryChange entry, string name, string value)
        {
            if (string.IsNullOrEmpty(value)) return;
            entry.AttributeChanges.Add(AttributeChange.CreateAttributeAdd(name, value));
        }

        // ── DN a spojeni ──────────────────────────────────────────────────────────

        private static string ToDn(string anchor, string objectType)
        {
            return "CN=" + EscapeDn(anchor) + ",CN=" + objectType;
        }

        private static string EscapeDn(string value)
        {
            if (string.IsNullOrEmpty(value)) return value;

            var sb = new StringBuilder(value.Length + 4);
            for (int i = 0; i < value.Length; i++)
            {
                char c = value[i];
                bool escape = c == '\\' || c == ',' || c == '+' || c == '"' || c == '<' || c == '>' || c == ';'
                    || (c == ' ' && (i == 0 || i == value.Length - 1))
                    || (c == '#' && i == 0);
                if (escape) sb.Append('\\');
                sb.Append(c);
            }
            return sb.ToString();
        }

        private static string QuoteView(string view)
        {
            string trimmed = (view ?? "").Trim();
            if (!ViewNameRegex.IsMatch(trimmed))
                throw new InvalidOperationException("Neplatny nazev pohledu '" + view + "'.");

            string[] parts = trimmed.Split('.');
            var sb = new StringBuilder();
            for (int i = 0; i < parts.Length; i++)
            {
                if (i > 0) sb.Append('.');
                sb.Append('[').Append(parts[i]).Append(']');
            }
            return sb.ToString();
        }

        private static string QuoteIdentifier(string name)
        {
            string trimmed = (name ?? "").Trim();
            if (!IdentifierRegex.IsMatch(trimmed))
                throw new InvalidOperationException("Neplatny nazev '" + name + "'.");
            return "[" + trimmed + "]";
        }

        private SqlConnection BuildConnection(KeyedCollection<string, ConfigParameter> cfg)
        {
            string server   = GetParam(cfg, CP_SERVER, "").Trim();
            string database = GetParam(cfg, CP_DATABASE, SqlConfig.DefaultDatabase).Trim();
            bool integrated = GetBool(cfg, CP_INTEGRATED, false);

            var builder = new SqlConnectionStringBuilder
            {
                DataSource               = server,
                InitialCatalog           = database,
                IntegratedSecurity       = integrated,
                MultipleActiveResultSets = false,
                ConnectTimeout           = 30,
                ApplicationName          = "SqlECMA2MA"
            };

            if (!integrated)
            {
                builder.UserID   = GetParam(cfg, CP_USER, "").Trim();
                builder.Password = GetPassword(cfg, CP_PASSWORD);
            }

            return new SqlConnection(builder.ConnectionString);
        }

        private static SqlConnection BuildSyncConnection(KeyedCollection<string, ConfigParameter> cfg)
        {
            string server   = GetParam(cfg, CP_SYNC_SERVER, SqlConfig.DefaultSyncServer).Trim();
            string database = GetParam(cfg, CP_SYNC_DATABASE, SqlConfig.DefaultSyncDatabase).Trim();
            bool integrated = GetBool(cfg, CP_SYNC_INTEGRATED, true);

            var builder = new SqlConnectionStringBuilder
            {
                DataSource         = server,
                InitialCatalog     = database,
                IntegratedSecurity = integrated,
                ConnectTimeout     = 30,
                ApplicationName    = "SqlECMA2MA"
            };

            if (!integrated)
            {
                builder.UserID   = GetParam(cfg, CP_SYNC_USER, "").Trim();
                builder.Password = GetPassword(cfg, CP_SYNC_PASSWORD);
            }

            return new SqlConnection(builder.ConnectionString);
        }

        private void DisposeImport()
        {
            if (_reader != null)
            {
                _reader.Dispose();
                _reader = null;
            }
            if (_conn != null)
            {
                _conn.Dispose();
                _conn = null;
            }
            _cardsByUic = null;
            _personUics = null;
        }

        private static ParameterValidationResult Failure(string parameter, string message)
        {
            return new ParameterValidationResult
            {
                Code           = ParameterValidationResultCode.Failure,
                ErrorMessage   = message,
                ErrorParameter = parameter
            };
        }

        private static string GetPassword(KeyedCollection<string, ConfigParameter> cfg, string key)
        {
            if (cfg != null && cfg.Contains(key) && cfg[key].SecureValue != null)
                return new System.Net.NetworkCredential(string.Empty, cfg[key].SecureValue).Password;

            return GetParam(cfg, key, "");
        }

        private static string GetParam(
            KeyedCollection<string, ConfigParameter> cfg,
            string key,
            string defaultValue)
        {
            if (cfg != null && cfg.Contains(key) && cfg[key].Value != null)
                return cfg[key].Value;
            return defaultValue;
        }

        private static bool GetBool(
            KeyedCollection<string, ConfigParameter> cfg,
            string key,
            bool defaultValue)
        {
            string value = GetParam(cfg, key, null);
            if (string.IsNullOrWhiteSpace(value)) return defaultValue;
            value = value.Trim();
            return value == "1"
                || value.Equals("true", StringComparison.OrdinalIgnoreCase)
                || value.Equals("yes", StringComparison.OrdinalIgnoreCase);
        }

        private static readonly string LogFile =
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
                         "SqlECMA2MA", "SqlECMA2MA.log");

        private static void Log(string level, string message)
        {
            try
            {
                string dir = Path.GetDirectoryName(LogFile);
                if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);
                File.AppendAllText(LogFile,
                    DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture) +
                    " [" + level + "] " + message + Environment.NewLine);
            }
            catch
            {
                // Log nesmi shodit import.
            }
        }
    }
}
