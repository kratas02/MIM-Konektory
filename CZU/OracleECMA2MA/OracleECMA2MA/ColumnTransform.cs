using System;
using System.Globalization;

namespace OracleECMA2MA
{
    /// <summary>
    /// Transformace hodnot sloupcu Oracle → MIM atributy.
    /// </summary>
    public static class ColumnTransform
    {
        // Formaty data pouzivane v Oracle (ODP.NET vraci ruzne)
        private static readonly string[] DateFormats =
        {
            "dd.MM.yyyy HH:mm:ss",
            "dd.MM.yyyy",
            "yyyy-MM-dd HH:mm:ss",
            "yyyy-MM-dd",
            "MM/dd/yyyy HH:mm:ss",
            "MM/dd/yyyy",
        };

        /// <summary>
        /// Vypocita AKTIVNI z hodnoty sloupce VYSTUP.
        /// VYSTUP &lt; dnes  → "0" (neaktivni)
        /// VYSTUP &gt;= dnes → "1" (aktivni)
        /// prazdna hodnota  → "1" (bez data ukonceni = aktivni)
        /// </summary>
        public static string ComputeAktivni(string vystupValue)
        {
            if (string.IsNullOrWhiteSpace(vystupValue))
                return "1";

            if (DateTime.TryParseExact(vystupValue, DateFormats,
                    CultureInfo.InvariantCulture,
                    DateTimeStyles.None,
                    out DateTime vystup))
            {
                return vystup.Date < DateTime.Today ? "0" : "1";
            }

            // Fallback: zkus obecny parse
            if (DateTime.TryParse(vystupValue, out DateTime vystupGeneral))
                return vystupGeneral.Date < DateTime.Today ? "0" : "1";

            return "1"; // nelze parsovat = predpokladame aktivni
        }

        /// <summary>
        /// Vrati true pokud ma byt pro dany object type a sloupec vypocitan atribut AKTIVNI.
        /// Tyka se PPV a PPVRelation (sloupec VYSTUP).
        /// </summary>
        public static bool ShouldComputeAktivni(string objectTypeName, string columnName)
        {
            return columnName.Equals("VYSTUP", StringComparison.OrdinalIgnoreCase)
                && (objectTypeName.Equals("PPV",         StringComparison.OrdinalIgnoreCase)
                 || objectTypeName.Equals("PPVRelation", StringComparison.OrdinalIgnoreCase));
        }
    }
}
