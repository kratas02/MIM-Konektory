using System;
using System.Security.Cryptography;
using System.Text;

namespace SqlECMA2MA
{
    /// <summary>
    /// Object types SQL konektoru.
    /// Card se cte z idm.vwIDM_Cards.
    /// Person je soucet UIC z karet a z idm.vwIDM_Contacts.
    /// </summary>
    public static class SqlConfig
    {
        public const string DefaultDatabase    = "CZU_IDM_TEST";
        public const string DefaultView        = "idm.vwIDM_Cards";
        public const string DefaultContactsView = "idm.vwIDM_Contacts";

        /// <summary>
        /// Sloupce kontaktu. Vice radku stejneho UIC se slozi do jedne hodnoty,
        /// unikatni hodnoty oddelene strednikem.
        /// </summary>
        public static readonly string[] ContactAttributes =
        {
            "PK_ID",
            "MV_HOME_PHONE",
            "MV_MOBILE_PHONE",
            "MV_HOME_EMAIL",
            "MV_WORK_PHONE",
            "MV_FAX",
            "MV_OFFICE_GTF",
            "MV_ICQ",
            "MV_SKYPE",
            "MV_PERSONAL_WEB",
            "MV_FB",
            "MV_TWITTER",
            "MV_BUDOVA",
            "MV_KANCELAR",
            "MV_OFFICE",
            "MV_VPN_PHONE"
        };

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
