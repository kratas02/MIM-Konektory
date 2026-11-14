using Microsoft.MetadirectoryServices;

namespace Zsdis.Sync
{
    /// <summary>
    /// Metaverse extension. V Synchronization Service Manageru zapněte
    /// Enable metaverse rules extension a vyberte tuto knihovnu.
    /// Provisioning type musí být scripted. Dokud běží deklarativní sync rules z portálu,
    /// tuto knihovnu nezapínejte, jinak se objekty založí dvakrát.
    /// </summary>
    public class MVExtensionObject : IMVSynchronization
    {
        public void Initialize()
        {
            Derived.ResetRunState();
        }

        public void Terminate()
        {
        }

        public void Provision(MVEntry mventry)
        {
            if (mventry.ObjectType == "person")
            {
                Derived.Person(mventry);
                ProvisionPerson(mventry);
                return;
            }

            if (mventry.ObjectType == "group")
            {
                Derived.Group(mventry);
                ProvisionGroup(mventry);
            }
        }

        public bool ShouldDeleteFromMV(CSEntry csentry, MVEntry mventry)
        {
            if (mventry.ObjectType == "person")
                return false;

            if (mventry.ObjectType != "group")
                return false;

            int authoritative = ConnectorCount(mventry, "eon-sk") + ConnectorCount(mventry, "ad.vse.sk");
            return authoritative <= 1;
        }

        private static void ProvisionPerson(MVEntry mv)
        {
            if (EntryUtil.Starts(mv, "distinguishedName", "CN")
                && EntryUtil.Ends(mv, "distinguishedName", "DC=eon-slovakia,DC=sk"))
            {
                string password = Derived.InitialPassword();
                EntryUtil.Provision(mv, "eon-sk", "user", EntryUtil.Get(mv, "distinguishedName"), cs =>
                {
                    cs["sAMAccountName"].Value = EntryUtil.Get(mv, "accountName");
                    cs["userAccountControl"].IntegerValue = 514;
                    cs["preferredLanguage"].Value = "sk-SK";
                    if (!string.IsNullOrEmpty(password))
                        cs["unicodePwd"].Value = password;
                });
            }

            if (EntryUtil.Starts(mv, "humanet", "hum") && EntryUtil.Eq(mv, "stat2", "3") && EntryUtil.Has(mv, "employeeID"))
            {
                string id = EntryUtil.Get(mv, "employeeID");
                EntryUtil.Provision(mv, "HUMANET_OUT", "Person", "CN=" + id + ",OBJECT=Person", cs =>
                {
                    cs["roleNum"].Value = id;
                });
            }

            if (EntryUtil.Eq(mv, "eonStatus", "A")
                && EntryUtil.Eq(mv, "eonUserPurpose", "regular")
                && EntryUtil.Eq(mv, "eonUserType", "primary")
                && EntryUtil.Starts(mv, "humanet", "hum")
                && EntryUtil.Has(mv, "employeeID"))
            {
                string id = EntryUtil.Get(mv, "employeeID");
                EntryUtil.Provision(mv, "ZSEEUser", "person", id, cs => cs["OsobneCislo"].Value = id);
            }

            if (EntryUtil.Starts(mv, "externisti", "id")
                && EntryUtil.Eq(mv, "eonStatus", "A")
                && EntryUtil.Eq(mv, "externisti_processed", "1")
                && !EntryUtil.Eq(mv, "extensionAttribute8", "1")
                && EntryUtil.Has(mv, "employeeID"))
            {
                string id = EntryUtil.Get(mv, "employeeID");
                EntryUtil.Provision(mv, "ZSEEUser", "person", id, cs => cs["OsobneCislo"].Value = id);
            }

            bool sap = mv["IsSAPCUA"].IsPresent && mv["IsSAPCUA"].BooleanValue;
            if (sap && EntryUtil.Contains(mv, "company", "Vychodoslovenska") && EntryUtil.Has(mv, "msDS-SourceAnchor"))
            {
                string anchor = EntryUtil.Get(mv, "msDS-SourceAnchor");
                EntryUtil.Provision(mv, "SAPCUA", "User", anchor, cs => cs["KID"].Value = anchor);
            }
            else if (sap && EntryUtil.Eq(mv, "domain", "EON-SK") && EntryUtil.Has(mv, "accountName"))
            {
                string kid = EntryUtil.Get(mv, "accountName");
                EntryUtil.Provision(mv, "SAPCUA", "User", kid, cs => cs["KID"].Value = kid);
            }

            bool koc = EntryUtil.Eq(mv, "KOC", "1") && EntryUtil.Has(mv, "employeeID");
            if (koc && (EntryUtil.Eq(mv, "employeeType", "I") || (EntryUtil.Eq(mv, "employeeType", "E") && EntryUtil.Eq(mv, "eonStatus", "A"))))
            {
                string id = EntryUtil.Get(mv, "employeeID");
                EntryUtil.Provision(mv, "KOC", "person", id, cs => cs["PERNR"].Value = id);
            }

            if (EntryUtil.Starts(mv, "externisti", "id")
                && EntryUtil.Eq(mv, "externisti_processed", "1")
                && !EntryUtil.Eq(mv, "extensionAttribute8", "1")
                && EntryUtil.Has(mv, "employeeID"))
            {
                string id = EntryUtil.Get(mv, "employeeID");
                EntryUtil.Provision(mv, "Externisti_SAP_OUT", "person", id, cs => cs["PERNR"].Value = id);
            }

            ProvisionVseLdap(mv);
        }

        private static void ProvisionVseLdap(MVEntry mv)
        {
            string dn = null;
            if (EntryUtil.Eq(mv, "VSENoSync", "ZSEOnly") && !EntryUtil.Eq(mv, "extensionAttribute8", "1"))
                dn = EntryUtil.Get(mv, "distinguishedNameLDAP");
            else if (EntryUtil.Ends(mv, "distinguishedName", "OU=services,OU=org,DC=eon-slovakia,DC=sk"))
                dn = (EntryUtil.Get(mv, "distinguishedName") ?? string.Empty)
                    .Replace("OU=services,OU=org,DC=eon-slovakia,DC=sk", "OU=services.zse,OU=Spolocnosti,DC=ad,DC=vse,DC=sk");
            else if (EntryUtil.Ends(mv, "distinguishedName", "OU=ITServices,DC=eon-slovakia,DC=sk")
                && !EntryUtil.Eq(mv, "VSENoSync", "No")
                && EntryUtil.Ends(mv, "distinguishedNameLDAP", "OU=services.zse,OU=Spolocnosti,DC=ad,DC=vse,DC=sk"))
                dn = EntryUtil.Get(mv, "distinguishedNameLDAP");
            else if (EntryUtil.Ends(mv, "distinguishedName", "OU=Users,OU=BU0390,OU=MU22,DC=eon-slovakia,DC=sk")
                && !EntryUtil.Ends(mv, "distinguishedName", "OU=Disabled_Users,OU=Users,OU=BU0390,OU=MU22,DC=eon-slovakia,DC=sk")
                && EntryUtil.Eq(mv, "usrID", "eon-slovakia.sk")
                && EntryUtil.Eq(mv, "eonStatus", "A")
                && EntryUtil.Eq(mv, "eonUserType", "primary")
                && !EntryUtil.Eq(mv, "VSENoSync", "No")
                && EntryUtil.Ends(mv, "distinguishedNameLDAP", "OU=Spolocnosti,DC=ad,DC=vse,DC=sk"))
                dn = EntryUtil.Get(mv, "distinguishedNameLDAP");
            else if (EntryUtil.Ends(mv, "distinguishedNameVSE", "DC=ad,DC=vse,DC=sk")
                && !EntryUtil.Eq(mv, "VSENoSync", "No")
                && !EntryUtil.Eq(mv, "VSENoSync", "ZSEOnly")
                && !EntryUtil.Starts(mv, "displayName", "copy-"))
                dn = EntryUtil.Get(mv, "distinguishedNameVSE");
            else if (EntryUtil.Ends(mv, "distinguishedNameVSE", "DC=ad,DC=vse,DC=sk")
                && EntryUtil.Starts(mv, "displayName", "copy-")
                && mv["userAccountControl"].IsPresent)
                dn = EntryUtil.Get(mv, "distinguishedNameVSE");

            EntryUtil.Provision(mv, "VSE_LDAP_prod", "user", dn, null);
        }

        private static void ProvisionGroup(MVEntry mv)
        {
            string dn = EntryUtil.Get(mv, "distinguishedName");
            bool azure = dn != null && dn.IndexOf("OU=AzureESK", System.StringComparison.OrdinalIgnoreCase) >= 0;
            bool portalGroup = EntryUtil.Ends(mv, "distinguishedName", "OU=Groups,DC=eon-slovakia,DC=sk")
                && !EntryUtil.Contains(mv, "displayName", "copy-")
                && EntryUtil.Contains(mv, "accountName", ".sk");

            if ((azure || portalGroup) && EntryUtil.Has(mv, "accountName"))
            {
                EntryUtil.Provision(mv, "eon-sk", "group", dn, cs =>
                {
                    cs["sAMAccountName"].Value = EntryUtil.Get(mv, "accountName");
                });
            }

            if (EntryUtil.Ends(mv, "distinguishedNameVSE", "DC=ad,DC=vse,DC=sk")
                && !EntryUtil.Eq(mv, "VSENoSync", "No")
                && EntryUtil.Has(mv, "accountNameVSE"))
            {
                EntryUtil.Provision(mv, "VSE_LDAP_prod", "group", EntryUtil.Get(mv, "distinguishedNameVSE"), cs =>
                {
                    cs["sAMAccountName"].Value = EntryUtil.Get(mv, "accountNameVSE");
                    cs["groupType"].IntegerValue = -2147483646;
                });
            }
            else if (EntryUtil.Eq(mv, "LDAPsync", "ZSEOnlyYes") && EntryUtil.Has(mv, "accountNameVSE"))
            {
                EntryUtil.Provision(mv, "VSE_LDAP_prod", "group", EntryUtil.Get(mv, "distinguishedNameVSE"), cs =>
                {
                    cs["sAMAccountName"].Value = EntryUtil.Get(mv, "accountNameVSE");
                });
            }
        }

        private static int ConnectorCount(MVEntry mv, string maName)
        {
            ConnectedMA ma = EntryUtil.FindMa(mv, maName);
            return ma == null ? 0 : ma.Connectors.Count;
        }
    }
}
