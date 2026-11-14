using System;
using System.Globalization;
using System.Text;
using Microsoft.MetadirectoryServices;

namespace Zsdis.Sync
{
    internal static class EntryUtil
    {
        public static bool Has(MVEntry mv, string attribute)
        {
            return mv[attribute].IsPresent;
        }

        public static string Get(MVEntry mv, string attribute)
        {
            if (!mv[attribute].IsPresent)
                return null;
            object value = mv[attribute].Value;
            return value == null ? null : Convert.ToString(value, CultureInfo.InvariantCulture);
        }

        public static string Get(CSEntry cs, string attribute)
        {
            if (!cs[attribute].IsPresent)
                return null;
            object value = cs[attribute].Value;
            return value == null ? null : value.ToString();
        }

        public static void Set(MVEntry mv, string attribute, string value)
        {
            if (string.IsNullOrEmpty(value))
            {
                if (mv[attribute].IsPresent)
                    mv[attribute].Delete();
                return;
            }

            if (!mv[attribute].IsPresent || !string.Equals(Get(mv, attribute), value, StringComparison.Ordinal))
                mv[attribute].Value = value;
        }

        public static void SetIfEmpty(MVEntry mv, string attribute, string value)
        {
            if (!Has(mv, attribute))
                Set(mv, attribute, value);
        }

        public static bool Eq(MVEntry mv, string attribute, string expected)
        {
            return string.Equals(Get(mv, attribute), expected, StringComparison.OrdinalIgnoreCase);
        }

        public static bool Starts(MVEntry mv, string attribute, string prefix)
        {
            string value = Get(mv, attribute);
            return value != null && value.StartsWith(prefix, StringComparison.OrdinalIgnoreCase);
        }

        public static bool Ends(MVEntry mv, string attribute, string suffix)
        {
            string value = Get(mv, attribute);
            return value != null && value.EndsWith(suffix, StringComparison.OrdinalIgnoreCase);
        }

        public static bool Contains(MVEntry mv, string attribute, string fragment)
        {
            string value = Get(mv, attribute);
            return value != null && value.IndexOf(fragment, StringComparison.OrdinalIgnoreCase) >= 0;
        }

        public static void SetBool(MVEntry mv, string attribute, bool value)
        {
            if (!mv[attribute].IsPresent || mv[attribute].BooleanValue != value)
                mv[attribute].BooleanValue = value;
        }

        public static void SetInt(CSEntry cs, string attribute, long value)
        {
            cs[attribute].IntegerValue = value;
        }

        public static string NormalizeName(string value)
        {
            if (string.IsNullOrEmpty(value))
                return string.Empty;

            string formD = value.Normalize(NormalizationForm.FormD);
            var builder = new StringBuilder(formD.Length);
            foreach (char character in formD)
            {
                UnicodeCategory category = CharUnicodeInfo.GetUnicodeCategory(character);
                if (category != UnicodeCategory.NonSpacingMark)
                    builder.Append(character);
            }

            return builder.ToString().Normalize(NormalizationForm.FormC).ToUpperInvariant();
        }

        public static string Left(string value, int length)
        {
            if (string.IsNullOrEmpty(value) || length <= 0)
                return string.Empty;
            return value.Length <= length ? value : value.Substring(0, length);
        }

        public static string DigitsFromDate(string value)
        {
            if (string.IsNullOrEmpty(value))
                return null;
            string head = Left(value, 10);
            return head.Replace("-", string.Empty);
        }

        public static bool TryParseDate(string value, out DateTime date)
        {
            date = DateTime.MinValue;
            if (string.IsNullOrEmpty(value))
                return false;
            string head = value.Length >= 10 ? value.Substring(0, 10) : value;
            return DateTime.TryParseExact(head, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out date)
                || DateTime.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out date);
        }

        public static ConnectedMA FindMa(MVEntry mv, string name)
        {
            foreach (ConnectedMA ma in mv.ConnectedMAs)
            {
                if (string.Equals(ma.Name, name, StringComparison.OrdinalIgnoreCase))
                    return ma;
            }

            return null;
        }

        public static void Provision(MVEntry mv, string maName, string objectType, string dn, Action<CSEntry> initial)
        {
            ConnectedMA ma = FindMa(mv, maName);
            if (ma == null || ma.Connectors.Count > 0 || string.IsNullOrEmpty(dn))
                return;

            CSEntry cs = ma.Connectors.StartNewConnector(objectType);
            cs.DN = cs.MA.CreateDN(dn);
            if (initial != null)
                initial(cs);
        }
    }
}
