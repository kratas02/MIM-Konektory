using System;
using System.Security.Cryptography;
using System.Text;

namespace UisECMA2MA
{
    /// <summary>
    /// Jeden object type = jedno view ve schematu UIS.
    /// Sloupce se cteji cele (krome hesel). Anchor je primarni klic, kterym se
    /// objekt paroval v puvodnim IN-UIS driveru.
    /// </summary>
    public class ObjectTypeConfig
    {
        public string Name         { get; }
        public string View         { get; }
        public string AnchorColumn { get; }

        /// <summary>Vypocitany referencni atribut (napr. Student na Studium). Null = zadny.</summary>
        public string ReferenceAttribute  { get; }
        /// <summary>Sloupec view, ze ktereho se reference sklada.</summary>
        public string ReferenceColumn     { get; }
        /// <summary>Cilovy object type reference (DN CN=&lt;hodnota&gt;,CN=&lt;typ&gt;).</summary>
        public string ReferenceObjectType { get; }

        public ObjectTypeConfig(
            string name,
            string view,
            string anchorColumn,
            string referenceAttribute  = null,
            string referenceColumn     = null,
            string referenceObjectType = null)
        {
            Name                = name;
            View                = view;
            AnchorColumn        = anchorColumn.ToUpper();
            ReferenceAttribute  = referenceAttribute;
            ReferenceColumn     = referenceColumn == null ? null : referenceColumn.ToUpper();
            ReferenceObjectType = referenceObjectType;
        }

        public void SplitView(out string owner, out string table)
        {
            string v = View.ToUpper();
            int dot = v.IndexOf('.');
            if (dot > 0)
            {
                owner = v.Substring(0, dot);
                table = v.Substring(dot + 1);
            }
            else
            {
                owner = "UIS";
                table = v;
            }
        }
    }

    /// <summary>
    /// Objekty, ktere puvodni IN-UIS-Stud a IN-UIS-Dokt ctou z Oracle.
    /// Relace (IN-UIS-StudRelation / IN-UIS-DoktRelation) jedou nad stejnymi view.
    /// Hesla (UIS.CZU_MONITOR_IM, sloupec INITPWD) se neimportuji.
    /// </summary>
    public static class UisConfig
    {
        public static readonly ObjectTypeConfig[] All = new[]
        {
            // IN-UIS-Stud: osoba studenta. Anchor OIDOS = workforceID.
            new ObjectTypeConfig(
                name:         "Student",
                view:         "UIS.CZU_OSOBA_IM",
                anchorColumn: "OIDOS"),

            // IN-UIS-Stud: jedno studium. Anchor PK_SIDENT = claStudiumID.
            // OIDOS odkazuje na Student.
            new ObjectTypeConfig(
                name:                "Studium",
                view:                "UIS.CZU_STUDIUM_IM",
                anchorColumn:        "PK_SIDENT",
                referenceAttribute:  "Student",
                referenceColumn:     "OIDOS",
                referenceObjectType: "Student"),

            // IN-UIS-Dokt: osoba doktoranda. Anchor UIC = workforceID.
            new ObjectTypeConfig(
                name:         "Doktorand",
                view:         "UIS.CZU_IM_STUDENTS",
                anchorColumn: "UIC"),

            // IN-UIS-Dokt: studium doktoranda. Anchor INDEX_ID = claStudiumDokID.
            // STUDENT_UIC odkazuje na Doktorand.
            new ObjectTypeConfig(
                name:                "DoktStudium",
                view:                "UIS.CZU_IM_INDICES",
                anchorColumn:        "INDEX_ID",
                referenceAttribute:  "Doktorand",
                referenceColumn:     "STUDENT_UIC",
                referenceObjectType: "Doktorand"),
        };

        /// <summary>Sloupce s heslem. Do schematu ani do importu se nedostanou.</summary>
        public static bool IsPasswordColumn(string column)
        {
            if (string.IsNullOrEmpty(column)) return false;
            return column.Equals("HESLO", StringComparison.OrdinalIgnoreCase)
                || column.Equals("LHESLO", StringComparison.OrdinalIgnoreCase)
                || column.Equals("INITPWD", StringComparison.OrdinalIgnoreCase)
                || column.Equals("INIT_PWD", StringComparison.OrdinalIgnoreCase);
        }

        public static ObjectTypeConfig FindByName(string name)
        {
            if (string.IsNullOrEmpty(name)) return null;
            foreach (var cfg in All)
                if (cfg.Name.Equals(name, StringComparison.OrdinalIgnoreCase))
                    return cfg;
            return null;
        }

        public static ObjectTypeConfig FindByGuid(Guid id)
        {
            if (id == Guid.Empty) return null;
            foreach (var cfg in All)
                if (NameToGuid(cfg.Name) == id)
                    return cfg;
            return null;
        }

        public static Guid NameToGuid(string name)
        {
            using (var md5 = MD5.Create())
            {
                byte[] hash = md5.ComputeHash(Encoding.UTF8.GetBytes("UisECMA2_" + name));
                return new Guid(hash);
            }
        }
    }
}
