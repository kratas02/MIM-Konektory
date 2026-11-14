using System;
using System.Collections.Generic;
using System.Data;
using System.Data.SqlClient;
using System.Linq;
using Microsoft.MetadirectoryServices;

namespace FimServiceDbMA
{
    /// <summary>Jeden atribut ze [fim].[AttributeInternal] navazany na konkretni object type.</summary>
    internal sealed class FimAttribute
    {
        public short Key;
        public string Name;

        /// <summary>DataType z MIM Service: String, Text, Integer, Boolean, DateTime, Reference, Binary, Identifier, Xml.</summary>
        public string DataType;

        public bool Multivalued;
        public AttributeType MimType;
    }

    /// <summary>Jeden object type ze [fim].[ObjectTypeInternal] vcetne navazanych atributu.</summary>
    internal sealed class FimObjectType
    {
        public short Key;
        public string Name;
        public readonly List<FimAttribute> Attributes = new List<FimAttribute>();
    }

    /// <summary>
    /// Cteni metadat schematu z MIM Service databaze. Schema je v DB ulozene v internal tabulkach:
    ///   [fim].[ObjectTypeInternal] - object types (Key, Name)
    ///   [fim].[AttributeInternal]  - atributy (Key, Name, DataType, Multivalued)
    ///   [fim].[BindingInternal]    - vazba atribut &lt;-&gt; object type
    /// </summary>
    internal static class FimDbMetadata
    {
        /// <summary>Anchor konektoru = [fim].[Objects].[ObjectID].</summary>
        internal const string AnchorAttributeName = "ObjectID";

        /// <summary>LocaleKey neutralni (invariant) lokalizace - viz default constraint na [fim].[ObjectValueString].</summary>
        internal const short InvariantLocaleKey = 127;

        private static readonly string[] RequiredTables =
        {
            "Objects", "ObjectTypeInternal", "AttributeInternal", "BindingInternal", "ObjectValueString",
            "Request", "RequestLog"
        };

        /// <summary>
        /// Overi, ze pripojena databaze je opravdu MIM Service DB a ze na ni mame prava cist.
        /// Vraci null kdyz je vse v poradku, jinak popis problemu.
        /// </summary>
        internal static string Verify(SqlConnection conn, int commandTimeout)
        {
            const string sql =
                "SELECT [t].[name] " +
                "FROM [sys].[tables] AS [t] " +
                "INNER JOIN [sys].[schemas] AS [s] ON [s].[schema_id] = [t].[schema_id] " +
                "WHERE [s].[name] = N'fim'";

            var found = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            using (var cmd = new SqlCommand(sql, conn))
            {
                cmd.CommandTimeout = commandTimeout;
                using (SqlDataReader rdr = cmd.ExecuteReader())
                {
                    while (rdr.Read())
                        found.Add(rdr.GetString(0));
                }
            }

            var missing = RequiredTables.Where(t => !found.Contains(t)).ToList();
            if (missing.Count > 0)
            {
                return "V databazi chybi tabulky [fim].[" + string.Join("], [fim].[", missing) +
                       "]. Neni to MIM Service databaze, nebo chybi prava na cteni.";
            }

            // Skutecny test prav na cteni dat (samotny pristup do sys.tables prava neoveri)
            using (var cmd = new SqlCommand("SELECT TOP (1) [ObjectKey] FROM [fim].[Objects]", conn))
            {
                cmd.CommandTimeout = commandTimeout;
                cmd.ExecuteScalar();
            }

            return null;
        }

        /// <summary>
        /// Vrati interni klic atributu podle nazvu. Pouziva se pro ObjectID, ktery ma
        /// v ruznych instalacich stejny nazev, ale klic nemusi byt vzdy 0.
        /// </summary>
        internal static short ResolveAttributeKey(SqlConnection conn, int commandTimeout, string attributeName)
        {
            using (var cmd = new SqlCommand(
                "SELECT TOP (1) [Key] FROM [fim].[AttributeInternal] WHERE [Name] = @name", conn))
            {
                cmd.CommandTimeout = commandTimeout;
                cmd.Parameters.Add("@name", SqlDbType.NVarChar, 448).Value = attributeName;

                object result = cmd.ExecuteScalar();
                if (result == null || result == DBNull.Value)
                    throw new InvalidOperationException(
                        "V [fim].[AttributeInternal] neexistuje atribut '" + attributeName + "'.");

                return Convert.ToInt16(result);
            }
        }

        /// <summary>
        /// Prelozi nazev lokalizace (napr. "cs") na LocaleKey. Prazdna hodnota = invariant (127).
        /// </summary>
        internal static short ResolveLocaleKey(SqlConnection conn, int commandTimeout, string locale)
        {
            if (string.IsNullOrWhiteSpace(locale))
                return InvariantLocaleKey;

            locale = locale.Trim();

            using (var cmd = new SqlCommand(
                "SELECT TOP (1) [Key] FROM [fim].[LocaleInternal] WHERE [Name] = @name", conn))
            {
                cmd.CommandTimeout = commandTimeout;
                cmd.Parameters.Add("@name", SqlDbType.NVarChar, 16).Value = locale;

                object result = cmd.ExecuteScalar();
                if (result == null || result == DBNull.Value)
                {
                    Log.Warn(string.Format(
                        "Locale '{0}' neni v [fim].[LocaleInternal], pouzivam invariant ({1}).",
                        locale, InvariantLocaleKey));
                    return InvariantLocaleKey;
                }

                return Convert.ToInt16(result);
            }
        }

        /// <summary>Nacte vsechny object types vcetne navazanych atributu.</summary>
        internal static List<FimObjectType> Load(SqlConnection conn, int commandTimeout)
        {
            const string sql =
                "SELECT [oti].[Key] AS [ObjectTypeKey], [oti].[Name] AS [ObjectTypeName], " +
                "       [ai].[Key] AS [AttributeKey], [ai].[Name] AS [AttributeName], " +
                "       [ai].[DataType], [ai].[Multivalued] " +
                "FROM [fim].[BindingInternal] AS [bi] " +
                "INNER JOIN [fim].[ObjectTypeInternal] AS [oti] ON [oti].[Key] = [bi].[ObjectTypeKey] " +
                "INNER JOIN [fim].[AttributeInternal] AS [ai] ON [ai].[Key] = [bi].[AttributeKey] " +
                "ORDER BY [oti].[Name], [ai].[Name]";

            var byName = new Dictionary<string, FimObjectType>(StringComparer.Ordinal);
            var unsupported = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            using (var cmd = new SqlCommand(sql, conn))
            {
                cmd.CommandTimeout = commandTimeout;
                using (SqlDataReader rdr = cmd.ExecuteReader())
                {
                    while (rdr.Read())
                    {
                        short typeKey = rdr.GetInt16(0);
                        string typeName = rdr.GetString(1);
                        short attrKey = rdr.GetInt16(2);
                        string attrName = rdr.GetString(3);
                        string dataType = rdr.GetString(4);
                        bool multivalued = rdr.GetBoolean(5);

                        FimObjectType objectType;
                        if (!byName.TryGetValue(typeName, out objectType))
                        {
                            objectType = new FimObjectType { Key = typeKey, Name = typeName };
                            byName.Add(typeName, objectType);
                        }

                        // ObjectID je anchor konektoru, jako bezny atribut se neexportuje do schematu
                        if (string.Equals(attrName, AnchorAttributeName, StringComparison.OrdinalIgnoreCase))
                            continue;

                        AttributeType mimType;
                        if (!TryMapDataType(dataType, out mimType))
                        {
                            unsupported.Add(dataType);
                            continue;
                        }

                        objectType.Attributes.Add(new FimAttribute
                        {
                            Key = attrKey,
                            Name = attrName,
                            DataType = dataType,
                            Multivalued = multivalued,
                            MimType = mimType
                        });
                    }
                }
            }

            if (unsupported.Count > 0)
                Log.Warn("Preskocene nezname DataType: " + string.Join(", ", unsupported));

            return byName.Values.OrderBy(t => t.Name, StringComparer.OrdinalIgnoreCase).ToList();
        }

        /// <summary>Namapuje DataType z MIM Service na typ atributu v metaverse konektoru.</summary>
        internal static bool TryMapDataType(string fimDataType, out AttributeType mimType)
        {
            switch ((fimDataType ?? string.Empty).Trim())
            {
                case "String":
                case "Text":
                case "Xml":
                case "Identifier":
                    // DateTime nema v ECMA2 vlastni typ - jde jako ISO 8601 string
                    mimType = AttributeType.String;
                    return true;
                case "DateTime":
                    mimType = AttributeType.String;
                    return true;
                case "Integer":
                    mimType = AttributeType.Integer;
                    return true;
                case "Boolean":
                    mimType = AttributeType.Boolean;
                    return true;
                case "Reference":
                    mimType = AttributeType.Reference;
                    return true;
                case "Binary":
                    mimType = AttributeType.Binary;
                    return true;
                default:
                    mimType = AttributeType.Undefined;
                    return false;
            }
        }
    }
}
