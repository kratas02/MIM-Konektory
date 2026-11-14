using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace Mms_Metaverse
{
    public class LdapHelper
    {
        public static string EscapeWholeDN(string fullDN)
        {
            if (string.IsNullOrEmpty(fullDN)) return fullDN;

            // Rozdělí DN na atribut=hodnota. Čárka uvnitř hodnoty (např. "Smith, John")
            // zůstane součástí hodnoty, dokud za ní není další RDN.
            // (?>...) je atomická skupina: .NET Framework v MIM nezná possessive *+.
            return Regex.Replace(fullDN, @"(?<attribute>(?:CN|OU|DC|O|C|L|ST|STREET|UID|SERIALNUMBER))=(?<value>(?>[^,]*)(?>(?:,(?!\s*(?:CN|OU|DC|O|C|L|ST|STREET|UID|SERIALNUMBER)=)[^,]*)*))", m =>
            {
                string attr = m.Groups["attribute"].Value;
                string val = m.Groups["value"].Value;

                // Escapujeme hodnotu atributu
                string escapedVal = EscapeLdapDNValue(val);

                return $"{attr}={escapedVal}";
            }, RegexOptions.IgnoreCase);
        }

        private static string EscapeLdapDNValue(string value)
        {
            if (string.IsNullOrEmpty(value)) return value;

            // RFC 4514: " + , ; < > \ vždy. Mezera na začátku i na konci a # na začátku
            // se prefixují lomítkem. NUL, CR a LF jen jako hex (\00, \0D, \0A).
            // Lomítko se přidává po znaku, takže nově vložené escape se znovu neescapují.
            var sb = new StringBuilder(value.Length);
            for (int i = 0; i < value.Length; i++)
            {
                char c = value[i];
                switch (c)
                {
                    case '\\':
                    case ',':
                    case '+':
                    case '"':
                    case '<':
                    case '>':
                    case ';':
                        sb.Append('\\').Append(c);
                        break;
                    case '\0':
                        sb.Append("\\00");
                        break;
                    case '\r':
                        sb.Append("\\0D");
                        break;
                    case '\n':
                        sb.Append("\\0A");
                        break;
                    case '#':
                        if (i == 0)
                            sb.Append('\\');
                        sb.Append(c);
                        break;
                    case ' ':
                        if (i == 0 || i == value.Length - 1)
                            sb.Append('\\');
                        sb.Append(c);
                        break;
                    default:
                        sb.Append(c);
                        break;
                }
            }

            return sb.ToString();
        }
    }
}
