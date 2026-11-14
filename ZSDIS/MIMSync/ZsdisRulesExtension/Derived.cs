using System;
using System.Collections.Generic;
using System.DirectoryServices;
using System.Globalization;
using System.IO;
using System.Reflection;
using Microsoft.MetadirectoryServices;

namespace Zsdis.Sync
{
    /// <summary>
    /// Náhrada portálových workflow, která dopočítávala atributy osoby a skupiny.
    /// Běží v Provision, tedy až po importu ze zdrojového konektoru.
    /// </summary>
    internal static class Derived
    {
        private const int KidSeed = 201001;
        private static readonly object KidGate = new object();
        private static readonly HashSet<string> IssuedKids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        private static readonly string[] KocCostCenters =
        {
            "1300050401", "0000132602", "1300070100", "1300070102", "0000132200", "0000132203"
        };

        private static readonly Dictionary<string, string> MailSuffixByBukrs = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            { "4212", "ppc-malzenice.sk" },
            { "4213", "zsdis.sk" },
            { "4214", "zse.sk" },
            { "4216", "zse-es.sk" },
            { "4217", "zse-bs.sk" },
            { "4218", "zsees.sk" }
        };

        public static void ResetRunState()
        {
            lock (KidGate)
                IssuedKids.Clear();
        }

        public static void Person(MVEntry mv)
        {
            ApplyExternist(mv);
            EnsureKid(mv);
            EnsureZseDirectoryAttributes(mv);
            EnsureMailbox(mv);
            EnsureCountry(mv);
            EnsureKoc(mv);
            EnsureSapFlag(mv);
        }

        public static void Group(MVEntry mv)
        {
            string description = EntryUtil.Get(mv, "description") ?? string.Empty;
            string dn = EntryUtil.Get(mv, "distinguishedName") ?? string.Empty;

            if (description.StartsWith("AzureESKCS", StringComparison.OrdinalIgnoreCase)
                || dn.IndexOf("OU=AzureESKCS", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                BuildAzureGroup(mv, "OU=AzureESKCS,OU=authz,OU=Groups,DC=eon-slovakia,DC=sk");
            }
            else if (description.StartsWith("AzureESK", StringComparison.OrdinalIgnoreCase)
                || dn.IndexOf("OU=AzureESK,", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                BuildAzureGroup(mv, "OU=AzureESK,OU=authz,OU=Groups,DC=eon-slovakia,DC=sk");
            }

            if (description.StartsWith(@"\\eon-slovakia.sk\", StringComparison.OrdinalIgnoreCase))
            {
                EntryUtil.Set(mv, "adminDescription", "TYPMIMManualne");
                EntryUtil.SetBool(mv, "membershipLocked", false);
                EntryUtil.Set(mv, "membershipAddWorkflow", "None");
            }

            string dnVse = EntryUtil.Get(mv, "distinguishedNameVSE") ?? string.Empty;
            if (dnVse.IndexOf("OU=Diamond,OU=Autorizacia,", StringComparison.OrdinalIgnoreCase) >= 0
                && !EntryUtil.Starts(mv, "displayName", "copy-"))
            {
                EntryUtil.Set(mv, "adminDescription", "TYPMIMVSE");
            }
        }

        private static void ApplyExternist(MVEntry mv)
        {
            if (!EntryUtil.Starts(mv, "externisti", "id"))
                return;

            if (!EntryUtil.Has(mv, "employeeID"))
                EntryUtil.Set(mv, "employeeID", NextEmployeeId());

            EntryUtil.SetIfEmpty(mv, "employeeType", "E");
            EntryUtil.SetIfEmpty(mv, "eonUserType", "primary");
            EntryUtil.SetIfEmpty(mv, "eonUserPurpose", "regular");
            EntryUtil.SetIfEmpty(mv, "persk", "E2");
            EntryUtil.SetIfEmpty(mv, "country", "Slovakia");
            EntryUtil.SetIfEmpty(mv, "c", "SK");

            if (EntryUtil.Has(mv, "ext_applicant_kid1"))
                EntryUtil.SetIfEmpty(mv, "ext_applicant_kid", EntryUtil.Get(mv, "ext_applicant_kid1"));

            if (EntryUtil.Has(mv, "ext_extern_email") && EntryUtil.Has(mv, "userPrincipalName"))
                EntryUtil.SetIfEmpty(mv, "email", EntryUtil.Get(mv, "ext_extern_email"));

            if (EntryUtil.Has(mv, "accountName") && EntryUtil.Has(mv, "userPrincipalName") && EntryUtil.Has(mv, "mobilePhone1"))
                EntryUtil.SetIfEmpty(mv, "mobilePhone", EntryUtil.Get(mv, "mobilePhone1"));

            CopyFromApplicant(mv);
            ApplyExternistStatus(mv);

            if (EntryUtil.Has(mv, "employeeID") && EntryUtil.Has(mv, "accountName"))
                EntryUtil.Set(mv, "externisti_processed", "1");

            if (EntryUtil.Starts(mv, "ext_applicant_kid", "K27788"))
                EntryUtil.Set(mv, "extensionAttribute8", "1");

            if (EntryUtil.Has(mv, "accountName") && EntryUtil.Has(mv, "distinguishedName"))
            {
                EntryUtil.Set(mv, "distinguishedNameLDAP",
                    "CN=" + EntryUtil.Get(mv, "accountName") + ",OU=ext.zse.sk,OU=Spolocnosti,DC=ad,DC=vse,DC=sk");
                EntryUtil.Set(mv, "VSENoSync", "ZSEOnly");
            }

            if (!EntryUtil.Has(mv, "usrID") && EntryUtil.Starts(mv, "persk", "E2") && EntryUtil.Eq(mv, "employeeType", "E"))
                EntryUtil.Set(mv, "usrID", "eon-slovakia.sk");
        }

        private static void CopyFromApplicant(MVEntry mv)
        {
            string applicantKid = EntryUtil.Get(mv, "ext_applicant_kid");
            if (string.IsNullOrEmpty(applicantKid))
                return;

            MVEntry[] found = Utils.FindMVEntries("accountName", applicantKid);
            if (found == null || found.Length == 0)
                return;

            MVEntry applicant = found[0];
            if (applicant["manager"].IsPresent)
                mv["manager"].Value = applicant["manager"].Value;

            EntryUtil.SetIfEmpty(mv, "Kostl", EntryUtil.Get(applicant, "Kostl"));
            EntryUtil.SetIfEmpty(mv, "Werks", EntryUtil.Get(applicant, "Werks"));
            EntryUtil.SetIfEmpty(mv, "idNS", EntryUtil.Get(applicant, "idNS"));
            EntryUtil.SetIfEmpty(mv, "ext_applicant_empliyeeid", EntryUtil.Get(applicant, "employeeID"));
            EntryUtil.SetIfEmpty(mv, "ext_applicant_name", EntryUtil.Get(applicant, "displayName"));
            EntryUtil.SetIfEmpty(mv, "email", EntryUtil.Get(applicant, "email"));
        }

        /// <summary>
        /// Platné okno smlouvy nastaví stat2=3 a eonStatus=A.
        /// Po konci smlouvy portál spouští dvě stejně pojmenovaná MPR s opačným výsledkem.
        /// Tady zůstává neaktivní stav. Druhou větev nezapisujeme.
        /// </summary>
        private static void ApplyExternistStatus(MVEntry mv)
        {
            DateTime start;
            DateTime end;
            bool hasStart = EntryUtil.TryParseDate(EntryUtil.Get(mv, "employeeStartDate"), out start);
            bool hasEnd = EntryUtil.TryParseDate(EntryUtil.Get(mv, "employeeEndDate"), out end);
            DateTime today = DateTime.Today;

            if (hasEnd && end.Date <= today.AddDays(-1))
            {
                EntryUtil.Set(mv, "stat2", "0");
                EntryUtil.Set(mv, "eonStatus", "I");
                return;
            }

            if (hasStart && start.Date <= today.AddDays(-1) && hasEnd && end.Date >= today.AddDays(1))
            {
                EntryUtil.Set(mv, "stat2", "3");
                EntryUtil.Set(mv, "eonStatus", "A");
            }
        }

        private static void EnsureKid(MVEntry mv)
        {
            if (EntryUtil.Has(mv, "accountName"))
                return;
            if (!ShouldHaveZseAccount(mv))
                return;

            if (EntryUtil.Has(mv, "accountName1"))
            {
                EntryUtil.Set(mv, "accountName", EntryUtil.Get(mv, "accountName1"));
                return;
            }

            string letter = EntryUtil.Left(EntryUtil.NormalizeName(EntryUtil.Get(mv, "firstName")), 1);
            if (string.IsNullOrEmpty(letter))
                letter = "X";

            EntryUtil.Set(mv, "accountName", NextKid(letter, EntryUtil.Get(mv, "employeeID")));
        }

        private static void EnsureZseDirectoryAttributes(MVEntry mv)
        {
            if (!EntryUtil.Has(mv, "accountName") || EntryUtil.Has(mv, "distinguishedName"))
                return;
            if (!ShouldHaveZseAccount(mv))
                return;

            string account = EntryUtil.Get(mv, "accountName");
            string ou = EntryUtil.Eq(mv, "ext_applicant_kid", "K27788")
                ? "OU=noSync,OU=Users,OU=BU0390,OU=MU22,DC=eon-slovakia,DC=sk"
                : "OU=Migrated Users,OU=Users,OU=BU0390,OU=MU22,DC=eon-slovakia,DC=sk";

            EntryUtil.Set(mv, "distinguishedName", "CN=" + account + "," + ou);
            EntryUtil.SetIfEmpty(mv, "userPrincipalName", account + "@e-ssi05.net");
            EntryUtil.SetIfEmpty(mv, "scriptPath", @"\ZSE\LogOnZSE.cmd");
            EntryUtil.SetIfEmpty(mv, "domain", "EON-SK");
        }

        private static bool ShouldHaveZseAccount(MVEntry mv)
        {
            if (EntryUtil.Starts(mv, "persk", "D"))
                return false;

            bool zseEmployee = EntryUtil.Has(mv, "employeeID")
                && EntryUtil.Eq(mv, "stat2", "3")
                && EntryUtil.Starts(mv, "usrID", "eon-slovakia");

            bool vseEmployee = EntryUtil.Starts(mv, "usrID", "ad.vse.sk")
                && EntryUtil.Starts(mv, "humanet", "hum");

            bool externist = EntryUtil.Starts(mv, "externisti", "id")
                && EntryUtil.Has(mv, "employeeID");

            return zseEmployee || vseEmployee || externist;
        }

        private static void EnsureMailbox(MVEntry mv)
        {
            if (!EntryUtil.Eq(mv, "eonUserType", "primary"))
                return;
            if (!EntryUtil.Eq(mv, "usrID", "eon-slovakia.sk"))
                return;
            if (EntryUtil.Starts(mv, "externisti", "id"))
                return;

            string suffix;
            if (!MailSuffixByBukrs.TryGetValue(EntryUtil.Get(mv, "bukrs") ?? string.Empty, out suffix))
                return;

            EntryUtil.SetBool(mv, "eonMailEnabled", true);
            EntryUtil.Set(mv, "eonLicenseGroup", "M365-LIC-E5");
            EntryUtil.Set(mv, "eonMailSuffix", suffix);
        }

        private static void EnsureCountry(MVEntry mv)
        {
            if (EntryUtil.Has(mv, "employeeID"))
                EntryUtil.SetIfEmpty(mv, "country", "Slovakia");
        }

        private static void EnsureKoc(MVEntry mv)
        {
            string idNs = EntryUtil.Get(mv, "idNS");
            bool match = false;
            if (!string.IsNullOrEmpty(idNs))
            {
                foreach (string center in KocCostCenters)
                {
                    if (string.Equals(idNs, center, StringComparison.Ordinal))
                    {
                        match = true;
                        break;
                    }
                }
            }

            if (match)
                EntryUtil.Set(mv, "KOC", "1");
            else if (EntryUtil.Eq(mv, "KOC", "1"))
                EntryUtil.Set(mv, "KOC", "0");
        }

        private static void EnsureSapFlag(MVEntry mv)
        {
            if (EntryUtil.Has(mv, "IsSAPCUA") && mv["IsSAPCUA"].BooleanValue)
                return;

            MVEntry[] groups = Utils.FindMVEntries("accountName", "PassSyncCUA");
            if (groups == null)
                return;

            foreach (MVEntry group in groups)
            {
                if (!group["member"].IsPresent)
                    continue;
                foreach (Value value in group["member"].Values)
                {
                    string raw = value.ToString();
                    string id = mv.ObjectID.ToString();
                    if (string.Equals(raw, id, StringComparison.OrdinalIgnoreCase)
                        || string.Equals(raw, "{" + id + "}", StringComparison.OrdinalIgnoreCase))
                    {
                        EntryUtil.SetBool(mv, "IsSAPCUA", true);
                        return;
                    }
                }
            }
        }

        private static void BuildAzureGroup(MVEntry mv, string ou)
        {
            if (EntryUtil.Has(mv, "accountName") && !EntryUtil.Has(mv, "distinguishedName"))
                EntryUtil.Set(mv, "distinguishedName", "CN=" + EntryUtil.Get(mv, "accountName") + "," + ou);
            if (!mv["groupType"].IsPresent)
                mv["groupType"].IntegerValue = -2147483646;
        }

        private static string NextKid(string letter, string employeeId)
        {
            lock (KidGate)
            {
                int start = ReadKidSeed();
                for (int number = start; number < start + 100000; number++)
                {
                    string candidate = letter + number.ToString(CultureInfo.InvariantCulture);
                    if (IssuedKids.Contains(candidate))
                        continue;
                    if (Utils.FindMVEntries("accountName", candidate).Length > 0)
                        continue;
                    if (Utils.FindMVEntries("msDS-SourceAnchor", candidate).Length > 0)
                        continue;
                    if (SamAccountNameTaken(candidate, employeeId))
                        continue;

                    IssuedKids.Add(candidate);
                    WriteKidSeed(number + 1);
                    return candidate;
                }
            }

            throw new InvalidOperationException("Došla řada KID. Zvyšte číslo v kid-seed.txt.");
        }

        private static string ExtensionDirectory()
        {
            return Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);
        }

        private static int ReadKidSeed()
        {
            string path = Path.Combine(ExtensionDirectory(), "kid-seed.txt");
            int number;
            if (File.Exists(path) && int.TryParse(File.ReadAllText(path).Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out number))
                return number;
            return KidSeed;
        }

        private static void WriteKidSeed(int next)
        {
            File.WriteAllText(Path.Combine(ExtensionDirectory(), "kid-seed.txt"), next.ToString(CultureInfo.InvariantCulture));
        }

        private static string NextEmployeeId()
        {
            lock (KidGate)
            {
                for (int number = 900000; number < 990000; number++)
                {
                    string candidate = number.ToString(CultureInfo.InvariantCulture);
                    if (Utils.FindMVEntries("employeeID", candidate).Length == 0)
                        return candidate;
                }
            }

            throw new InvalidOperationException("Došla řada EmployeeID pro externisty.");
        }

        private static bool SamAccountNameTaken(string sam, string employeeId)
        {
            try
            {
                using (var root = new DirectoryEntry("GC://DC=eon-slovakia,DC=sk"))
                using (var searcher = new DirectorySearcher(root))
                {
                    searcher.Filter = "(&(objectClass=user)(objectCategory=person)(sAMAccountName=" + EscapeLdap(sam) + "))";
                    searcher.SearchScope = SearchScope.Subtree;
                    searcher.PropertiesToLoad.Add("employeeID");
                    SearchResult found = searcher.FindOne();
                    if (found == null)
                        return false;
                    if (!string.IsNullOrEmpty(employeeId) && found.Properties["employeeID"].Count > 0
                        && string.Equals(found.Properties["employeeID"][0].ToString(), employeeId, StringComparison.OrdinalIgnoreCase))
                        return false;
                    return true;
                }
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException("Kontrola KID proti GC://DC=eon-slovakia,DC=sk selhala pro " + sam + ".", ex);
            }
        }

        private static string EscapeLdap(string value)
        {
            return value.Replace("\\", "\\5c").Replace("*", "\\2a").Replace("(", "\\28").Replace(")", "\\29");
        }

        public static string InitialPassword()
        {
            string fromEnv = Environment.GetEnvironmentVariable("ZSDIS_INITIAL_PASSWORD");
            if (!string.IsNullOrEmpty(fromEnv))
                return fromEnv.Trim();

            string path = Path.Combine(ExtensionDirectory(), "initial-password.txt");
            if (File.Exists(path))
                return File.ReadAllText(path).Trim();

            return null;
        }
    }
}
