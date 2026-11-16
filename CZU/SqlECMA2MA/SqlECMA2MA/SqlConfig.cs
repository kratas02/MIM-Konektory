using System;
using System.Security.Cryptography;
using System.Text;

namespace SqlECMA2MA
{
    /// <summary>
    /// Object types SQL konektoru. Oba se ctou z idm.vwIDM_Cards.
    /// Treti object type prijde jako dalsi konstanta, blok ve schematu a vetev v importu.
    /// </summary>
    public static class SqlConfig
    {
        public const string DefaultDatabase = "CZU_IDM_TEST";
        public const string DefaultView     = "idm.vwIDM_Cards";

        public const string DefaultSyncServer     = "(local)";
        public const string DefaultSyncDatabase   = "FIMSynchronizationService";
        public const string DefaultMvColumn       = "PK_ZAM";
        public const string DefaultMvObjectType   = "person";

        /// <summary>
        /// Uzivatel z pohledu karet. V zadani "User" i "person".
        /// Jmeno Person odpovida Magion konektoru: anchor je UIC, join na MV person.
        /// </summary>
        public const string PersonType = "Person";

        public const string CardType = "Card";

        /// <summary>Multivalued reference z Person na Card.</summary>
        public const string CardReference = "card";

        public static readonly string[] ObjectTypes = { PersonType, CardType };

        /// <summary>Deterministicky GUID partition (stabilni mezi restarty).</summary>
        public static Guid NameToGuid(string name)
        {
            using (var md5 = MD5.Create())
            {
                byte[] hash = md5.ComputeHash(Encoding.UTF8.GetBytes("SqlECMA2_" + name));
                return new Guid(hash);
            }
        }
    }
}
