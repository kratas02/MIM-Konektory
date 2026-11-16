using System;

namespace OracleECMA2MA
{
    /// <summary>
    /// Konfigurace jednoho object type (view + anchor + delta podpora).
    /// </summary>
    public class ObjectTypeConfig
    {
        public string   Name            { get; }   // "Person", "PPV", "BaseUnit" ...
        public string   View            { get; }   // "IDM.V_IDM_ZAM"
        public string   AnchorColumn    { get; }   // "UIC" - primarni/prvni sloupec anchoru (zpetna kompatibilita)
        public string[] AnchorColumns   { get; }   // vsechny sloupce tvorici anchor/DN (obvykle jen 1, viz Komise)
        public bool     SupportDelta    { get; }   // true = delta import pres IDM_CHANGES
        public string   DeltaObjectType { get; }   // hodnota IDM_CHANGES.OBJECT_TYPE ("User"/"Group")
        public string[] DistinctColumns         { get; }   // pokud vyplneno: import cte "SELECT DISTINCT <cols>"
                                                            // misto "SELECT *" (deduplikace radku view na tyto sloupce)
        public bool     IncludeAnchorAsAttribute { get; }   // true = anchor sloupec se prida i jako bezny atribut
        public bool     IsDerived               { get; }   // true = objekty se pocitaji (RoleBuilder), nectou se z View
        public bool     DerivedBusinessOnly     { get; }   // true = jen ne-nadrazene role zamestnanec/technik/pedagog

        public ObjectTypeConfig(
            string   name,
            string   view,
            string   anchorColumn,
            bool     supportDelta             = false,
            string   deltaObjectType          = null,
            string[] distinctColumns          = null,
            string[] anchorColumns            = null,
            bool     includeAnchorAsAttribute = false,
            bool     isDerived                = false,
            bool     derivedBusinessOnly      = false)
        {
            Name            = name;
            View            = view;
            AnchorColumn    = anchorColumn.ToUpper();
            SupportDelta    = supportDelta;
            DeltaObjectType = deltaObjectType;
            DistinctColumns = distinctColumns;
            IsDerived       = isDerived;
            DerivedBusinessOnly = derivedBusinessOnly;

            if (anchorColumns != null && anchorColumns.Length > 0)
            {
                AnchorColumns = new string[anchorColumns.Length];
                for (int i = 0; i < anchorColumns.Length; i++)
                    AnchorColumns[i] = anchorColumns[i].ToUpper();
            }
            else
            {
                AnchorColumns = new[] { AnchorColumn };
            }
            IncludeAnchorAsAttribute = includeAnchorAsAttribute;
        }

        /// <summary>Rozlozi View na owner a table (pr. "IDM.V_IDM_ZAM" → owner="IDM", table="V_IDM_ZAM").</summary>
        public void SplitView(string defaultOwner, out string owner, out string table)
        {
            string v = View.ToUpper();
            if (v.Contains("."))
            {
                var parts = v.Split(new[] { '.' }, 2);
                owner = parts[0];
                table = parts[1];
            }
            else
            {
                owner = defaultOwner.ToUpper();
                table = v;
            }
        }
    }

    /// <summary>
    /// Staticka konfigurace vsech Magion object types.
    /// </summary>
    public static class MagionConfig
    {
        public const string DefaultDeltaTable = "MAGION_CZU.IDM_CHANGES";

        public static readonly ObjectTypeConfig[] All = new[]
        {
            // IN-Magion: zamestnanci
            new ObjectTypeConfig(
                name:            "Person",
                view:            "IDM.V_IDM_ZAM",
                anchorColumn:    "UIC",
                supportDelta:    true,
                deltaObjectType: "User"),

            // IN-Magion: pracovnepravni vztahy
            new ObjectTypeConfig(
                name:            "PPV",
                view:            "IDM.V_IDM_PPV",
                anchorColumn:    "OSPOM",
                supportDelta:    true,
                deltaObjectType: "Group"),

            // IN-Magion-BaseUnit: organizacni jednotky
            new ObjectTypeConfig(
                name:                    "BaseUnit",
                view:                    "IDM.V_IDM_UNIT",
                anchorColumn:            "PK_UTVAR",
                includeAnchorAsAttribute: true),

            // IN-Magion-PPVRelation: PPV pro Origin
            new ObjectTypeConfig(
                name:         "PPVRelation",
                view:         "IDM.V_IDM_PPV",
                anchorColumn: "PK_PPV"),

            // IN-Magion-Komise: komise (jeden objekt = jedna komise, distinct pres nasledujici sloupce;
            // clenove jsou pripojeni pres multivalued referencni atribut "member" - viz OracleConnector).
            // Anchor/DN je kombinovany z RK_ID + RK_KOD (samotne RK_ID neni v ramci distinct radku jednoznacne).
            new ObjectTypeConfig(
                name:            "Komise",
                view:            "IDM.V_IDM_KOMISE",
                anchorColumn:    "RK_ID",
                anchorColumns:   new[] { "RK_ID", "RK_KOD" },
                distinctColumns: new[] { "RK_ID", "RK_KOD", "RK_NAZEV", "RK_SOUCAST_KOD" }),

            // IN-Magion-PracMista: pracovni mista (jeden objekt = jedno pracovni misto, distinct pres
            // nasledujici sloupce; clenove jsou pripojeni pres multivalued referencni atribut "member")
            new ObjectTypeConfig(
                name:            "PracMista",
                view:            "IDM.V_IDM_MISTA",
                anchorColumn:    "PMIS_CISLO",
                distinctColumns: new[] { "PMIS_NAZEV", "PMIS_ALTERNATIV", "PMIS_CISLO", "PMIS_NADRIZ",
                                          "PMIS_TYP_VED", "PMIS_UNAZEV", "SOJ_CISLO" }),

            // Vypocitane role z unikatnich kombinaci platnych PPV (AKTIVNI = 1,
            // FAKULTA + STRED_KMEN_ZKRATKA).
            // Anchor je AccountName; DN = CN=<AccountName>,CN=ApplicationRole (IsDNAsAnchor).
            new ObjectTypeConfig(
                name:         "ApplicationRole",
                view:         "IDM.V_IDM_PPV",
                anchorColumn: "AccountName",
                isDerived:    true),

            // Stejne jako ApplicationRole, ale jen isParentGroup=false a suffixy
            // zamestnanec / technik / pedagog (vcetne vyjimky KTV/LIB/KAM/STATKY/VIN).
            new ObjectTypeConfig(
                name:                "BusinessRole",
                view:                "IDM.V_IDM_PPV",
                anchorColumn:        "AccountName",
                isDerived:           true,
                derivedBusinessOnly: true),
        };

        /// <summary>Najde konfiguraci podle nazvu (case-insensitive).</summary>
        public static ObjectTypeConfig FindByName(string name)
            => Array.Find(All, c => c.Name.Equals(name, StringComparison.OrdinalIgnoreCase));

        /// <summary>Deterministicky GUID z nazvu partition (stabilni mezi restarty).</summary>
        public static Guid NameToGuid(string name)
        {
            using (var md5 = System.Security.Cryptography.MD5.Create())
            {
                byte[] hash = md5.ComputeHash(System.Text.Encoding.UTF8.GetBytes("MagionECMA2_" + name));
                return new Guid(hash);
            }
        }
    }
}
