using System;
using Microsoft.MetadirectoryServices;

namespace Zsdis.Sync
{
    public abstract class MaExtensionBase : IMASynchronization
    {
        public virtual void Initialize()
        {
        }

        public virtual void Terminate()
        {
        }

        public virtual bool ShouldProjectToMV(CSEntry csentry, out string mvObjectType)
        {
            mvObjectType = null;
            return false;
        }

        public virtual DeprovisionAction Deprovision(CSEntry csentry)
        {
            return DeprovisionAction.Disconnect;
        }

        public virtual bool FilterForDisconnection(CSEntry csentry)
        {
            return false;
        }

        public virtual void MapAttributesForJoin(string flowRuleName, CSEntry csentry, ref ValueCollection values)
        {
            throw new EntryPointNotImplementedException();
        }

        public virtual bool ResolveJoinSearch(string joinCriteriaName, CSEntry csentry, MVEntry[] rgMvEntry, out int imventry, ref string mvObjectType)
        {
            imventry = -1;
            mvObjectType = null;
            return false;
        }

        public virtual void MapAttributesForImport(string flowRuleName, CSEntry csentry, MVEntry mventry)
        {
            throw new UnexpectedDataException("Neznámý import flow " + flowRuleName);
        }

        public virtual void MapAttributesForExport(string flowRuleName, MVEntry mventry, CSEntry csentry)
        {
            throw new UnexpectedDataException("Neznámý export flow " + flowRuleName);
        }

        protected static string Get(CSEntry cs, string attribute)
        {
            return EntryUtil.Get(cs, attribute);
        }
    }

    public sealed class MaHumanet : MaExtensionBase
    {
        public override bool ShouldProjectToMV(CSEntry csentry, out string mvObjectType)
        {
            mvObjectType = "person";
            string usr = Get(csentry, "usrID");
            return string.Equals(usr, "eon-slovakia.sk", StringComparison.OrdinalIgnoreCase)
                || string.Equals(usr, "ad.vse.sk", StringComparison.OrdinalIgnoreCase);
        }

        public override void MapAttributesForImport(string flowRuleName, CSEntry csentry, MVEntry mventry)
        {
            switch (flowRuleName)
            {
                case "displayName":
                    string last = Get(csentry, "priezvisko");
                    string first = Get(csentry, "krstneMeno");
                    if (!string.IsNullOrEmpty(last) && !string.IsNullOrEmpty(first))
                        mventry["displayName"].Value = last + ", " + first;
                    else if (!string.IsNullOrEmpty(last))
                        mventry["displayName"].Value = last;
                    else
                        mventry["displayName"].Value = string.Empty;
                    break;
                case "eonBusinessUnitID":
                    mventry["eonBusinessUnitID"].Value = Get(csentry, "bukrs") ?? string.Empty;
                    break;
                case "department":
                    mventry["department"].Value = string.IsNullOrEmpty(Get(csentry, "idOJ2")) ? "NO-OM-INT" : Get(csentry, "idOJ2");
                    break;
                case "eonStatus":
                    mventry["eonStatus"].Value = string.Equals(Get(csentry, "stat2"), "3", StringComparison.Ordinal) ? "A" : "I";
                    break;
                default:
                    base.MapAttributesForImport(flowRuleName, csentry, mventry);
                    break;
            }
        }
    }

    public sealed class MaExternisti : MaExtensionBase
    {
        public override bool ShouldProjectToMV(CSEntry csentry, out string mvObjectType)
        {
            mvObjectType = "person";
            return !string.IsNullOrEmpty(Get(csentry, "personal_number"));
        }

        public override void MapAttributesForImport(string flowRuleName, CSEntry csentry, MVEntry mventry)
        {
            string from = Get(csentry, "formatted_contract_from");
            string to = Get(csentry, "formatted_contract_to");
            switch (flowRuleName)
            {
                case "externisti":
                    mventry["externisti"].Value = "idcreate" + Get(csentry, "id");
                    break;
                case "displayName":
                    mventry["displayName"].Value = Get(csentry, "extern_surname") + ", " + Get(csentry, "extern_name");
                    break;
                case "begda":
                    mventry["begda"].Value = (from ?? string.Empty).Replace("-", string.Empty);
                    break;
                case "endda":
                    mventry["endda"].Value = (to ?? string.Empty).Replace("-", string.Empty);
                    break;
                case "employeeStartDate":
                    mventry["employeeStartDate"].Value = from + "T00:00:00.000";
                    break;
                case "employeeEndDate":
                    mventry["employeeEndDate"].Value = to + "T00:00:00.000";
                    break;
                default:
                    base.MapAttributesForImport(flowRuleName, csentry, mventry);
                    break;
            }
        }

        public override void MapAttributesForExport(string flowRuleName, MVEntry mventry, CSEntry csentry)
        {
            switch (flowRuleName)
            {
                case "formatted_contract_to":
                    csentry["formatted_contract_to"].Value = EntryUtil.Left(EntryUtil.Get(mventry, "employeeEndDate"), 10);
                    break;
                case "extern_contract_to":
                    csentry["extern_contract_to"].Value = EntryUtil.Left(EntryUtil.Get(mventry, "employeeEndDate"), 10) + " 00:00:00Z";
                    break;
                default:
                    base.MapAttributesForExport(flowRuleName, mventry, csentry);
                    break;
            }
        }
    }

    public sealed class MaEonSk : MaExtensionBase
    {
        public override bool ShouldProjectToMV(CSEntry csentry, out string mvObjectType)
        {
            string dn = csentry.DN.ToString();
            if (csentry.ObjectType == "user")
            {
                mvObjectType = "person";
                return dn.EndsWith("DC=eon-slovakia,DC=sk", StringComparison.OrdinalIgnoreCase);
            }

            if (csentry.ObjectType == "group")
            {
                mvObjectType = "group";
                string admin = Get(csentry, "adminDescription") ?? string.Empty;
                return !admin.StartsWith("TYP", StringComparison.OrdinalIgnoreCase);
            }

            mvObjectType = null;
            return false;
        }

        public override DeprovisionAction Deprovision(CSEntry csentry)
        {
            return DeprovisionAction.Disconnect;
        }

        public override void MapAttributesForImport(string flowRuleName, CSEntry csentry, MVEntry mventry)
        {
            switch (flowRuleName)
            {
                case "group.displayName":
                    mventry["displayName"].Value = string.IsNullOrEmpty(Get(csentry, "displayName"))
                        ? Get(csentry, "cn")
                        : Get(csentry, "displayName");
                    break;
                case "group.scope":
                    mventry["scope"].Value = GroupScope(csentry);
                    break;
                case "group.type":
                    int groupType = csentry["groupType"].IsPresent ? (int)csentry["groupType"].IntegerValue : 0;
                    // BitOr(groupType, 14) == 14 je distribuční skupina, stejně jako dnešní sync rule.
                    mventry["type"].Value = ((groupType | 14) == 14)
                        ? "Distribution"
                        : (string.IsNullOrEmpty(Get(csentry, "mailNickname")) ? "Security" : "MailEnabledSecurity");
                    break;
                case "enabled":
                    // Zachováno z pravidla Users eon-sk in-out: true, když je v userAccountControl nastavený bit 2.
                    long uac = csentry["userAccountControl"].IsPresent ? csentry["userAccountControl"].IntegerValue : 0;
                    mventry["enabled"].BooleanValue = (uac | 2) == uac;
                    break;
                case "email":
                    string mail = Get(csentry, "mail");
                    mventry["email"].Value = mail == null ? string.Empty : mail.Trim();
                    break;
                default:
                    base.MapAttributesForImport(flowRuleName, csentry, mventry);
                    break;
            }
        }

        private static string GroupScope(CSEntry csentry)
        {
            int groupType = csentry["groupType"].IsPresent ? (int)csentry["groupType"].IntegerValue : 0;
            if ((groupType & 2) == 2)
                return "Global";
            if ((groupType & 4) == 4)
                return "DomainLocal";
            return "Universal";
        }
    }

    public sealed class MaAdVse : MaExtensionBase
    {
        public override bool ShouldProjectToMV(CSEntry csentry, out string mvObjectType)
        {
            string dn = csentry.DN.ToString();
            if (csentry.ObjectType == "user")
            {
                mvObjectType = "person";
                long uac = csentry["userAccountControl"].IsPresent ? csentry["userAccountControl"].IntegerValue : 0;
                return dn.EndsWith("DC=ad,DC=vse,DC=sk", StringComparison.OrdinalIgnoreCase) && uac != 514;
            }

            if (csentry.ObjectType == "group")
            {
                mvObjectType = "group";
                return dn.EndsWith("OU=Autorizacia,OU=GPOGroups,DC=ad,DC=vse,DC=sk", StringComparison.OrdinalIgnoreCase);
            }

            mvObjectType = null;
            return false;
        }

        public override DeprovisionAction Deprovision(CSEntry csentry)
        {
            return DeprovisionAction.ExplicitDisconnect;
        }

        public override void MapAttributesForImport(string flowRuleName, CSEntry csentry, MVEntry mventry)
        {
            switch (flowRuleName)
            {
                case "group.displayName":
                    mventry["displayName"].Value = string.IsNullOrEmpty(Get(csentry, "displayName"))
                        ? Get(csentry, "cn")
                        : Get(csentry, "displayName");
                    break;
                case "group.scope":
                    int groupType = csentry["groupType"].IsPresent ? (int)csentry["groupType"].IntegerValue : 0;
                    if ((groupType & 2) == 2)
                        mventry["scope"].Value = "Global";
                    else if ((groupType & 4) == 4)
                        mventry["scope"].Value = "DomainLocal";
                    else
                        mventry["scope"].Value = "Universal";
                    break;
                case "group.accountName":
                    mventry["accountName"].Value = "VSE-" + Get(csentry, "sAMAccountName");
                    break;
                default:
                    base.MapAttributesForImport(flowRuleName, csentry, mventry);
                    break;
            }
        }

        public override void MapAttributesForJoin(string flowRuleName, CSEntry csentry, ref ValueCollection values)
        {
            if (flowRuleName == "employeeNumber-when-enabled")
            {
                long uac = csentry["userAccountControl"].IsPresent ? csentry["userAccountControl"].IntegerValue : 0;
                if (uac != 514 && csentry["employeeNumber"].IsPresent)
                    values.Add(csentry["employeeNumber"].Value);
                return;
            }

            base.MapAttributesForJoin(flowRuleName, csentry, ref values);
        }
    }

    public sealed class MaVseLdap : MaExtensionBase
    {
        public override void MapAttributesForExport(string flowRuleName, MVEntry mventry, CSEntry csentry)
        {
            switch (flowRuleName)
            {
                case "cn":
                    csentry["cn"].Value = EntryUtil.Has(mventry, "cnVSE")
                        ? EntryUtil.Get(mventry, "cnVSE")
                        : EntryUtil.Get(mventry, "accountNameVSE");
                    break;
                case "title":
                    string source = mventry.ObjectType == "person" && EntryUtil.Starts(mventry, "displayName", "copy-")
                        ? EntryUtil.Get(mventry, "jobTitle")
                        : EntryUtil.Get(mventry, "title");
                    csentry["title"].Value = EntryUtil.Left(source, 60);
                    break;
                case "userAccountControl":
                    csentry["userAccountControl"].IntegerValue = mventry["userAccountControl"].IsPresent
                        ? mventry["userAccountControl"].IntegerValue
                        : 514;
                    break;
                default:
                    base.MapAttributesForExport(flowRuleName, mventry, csentry);
                    break;
            }
        }
    }

    public sealed class MaSapCua : MaExtensionBase
    {
        public override void MapAttributesForImport(string flowRuleName, CSEntry csentry, MVEntry mventry)
        {
            if (flowRuleName != "IsSAPCUA")
            {
                base.MapAttributesForImport(flowRuleName, csentry, mventry);
                return;
            }

            string klass = Get(csentry, "CLASS");
            bool enabled = string.IsNullOrEmpty(klass) || !string.Equals(klass, "DISABLED", StringComparison.OrdinalIgnoreCase);
            mventry["IsSAPCUA"].BooleanValue = enabled;
        }

        public override void MapAttributesForExport(string flowRuleName, MVEntry mventry, CSEntry csentry)
        {
            switch (flowRuleName)
            {
                case "COMPANY":
                    csentry["COMPANY"].Value = Company(mventry) ?? "0000";
                    break;
                case "NAME1":
                    AssignOrDelete(csentry, "NAME1", Name1(mventry));
                    break;
                case "STAT2":
                    AssignOrDelete(csentry, "STAT2", EntryUtil.Eq(mventry, "stat2", "1") ? "3" : EntryUtil.Get(mventry, "stat2"));
                    break;
                case "CLASS":
                    if (EntryUtil.Eq(mventry, "persk", "E2"))
                        csentry["CLASS"].Value = "EXTERNAL";
                    else if (csentry["CLASS"].IsPresent)
                        csentry["CLASS"].Delete();
                    break;
                case "CLASS-from-stat2":
                    AssignOrDelete(csentry, "CLASS", EntryUtil.Eq(mventry, "stat2", "1") ? "3" : EntryUtil.Get(mventry, "stat2"));
                    break;
                case "SMTP_ADDR":
                    string email = EntryUtil.Get(mventry, "email");
                    AssignOrDelete(csentry, "SMTP_ADDR", email == null ? null : email.ToLowerInvariant());
                    break;
                case "GLTGB":
                    AssignOrDelete(csentry, "GLTGB", EntryUtil.Has(mventry, "endda")
                        ? EntryUtil.Get(mventry, "endda")
                        : EntryUtil.Get(mventry, "employeeEndDateCUA"));
                    break;
                case "USRID":
                    csentry["USRID"].Value = EntryUtil.Get(mventry, "usrID") ?? string.Empty;
                    break;
                default:
                    base.MapAttributesForExport(flowRuleName, mventry, csentry);
                    break;
            }
        }

        private static void AssignOrDelete(CSEntry cs, string attribute, string value)
        {
            if (string.IsNullOrEmpty(value))
            {
                if (cs[attribute].IsPresent)
                    cs[attribute].Delete();
                return;
            }

            cs[attribute].Value = value;
        }

        private static string Company(MVEntry mv)
        {
            if (!EntryUtil.Has(mv, "persg"))
                return "0000";
            if (EntryUtil.Eq(mv, "persg", "1"))
                return EntryUtil.Get(mv, "bukrs");
            if (EntryUtil.Eq(mv, "persg", "3"))
                return EntryUtil.Get(mv, "departmentNumber");
            return "0000";
        }

        private static string Name1(MVEntry mv)
        {
            if (!EntryUtil.Has(mv, "persg"))
                return null;
            if (EntryUtil.Eq(mv, "persg", "1"))
                return EntryUtil.Has(mv, "company") ? EntryUtil.Get(mv, "company") : EntryUtil.Get(mv, "menoOJ");
            if (EntryUtil.Eq(mv, "persg", "3"))
                return EntryUtil.Get(mv, "menoOJ");
            return null;
        }
    }

    public sealed class MaKoc : MaExtensionBase
    {
        public override void MapAttributesForExport(string flowRuleName, MVEntry mventry, CSEntry csentry)
        {
            if (flowRuleName == "Type")
            {
                csentry["Type"].Value = EntryUtil.Eq(mventry, "employeeType", "I") ? "int" : "ext";
                return;
            }

            base.MapAttributesForExport(flowRuleName, mventry, csentry);
        }
    }

    public sealed class MaZsee : MaExtensionBase
    {
        public override void MapAttributesForExport(string flowRuleName, MVEntry mventry, CSEntry csentry)
        {
            if (flowRuleName == "Nastup")
            {
                csentry["Nastup"].Value = EntryUtil.DigitsFromDate(EntryUtil.Get(mventry, "employeeStartDate"));
                return;
            }

            if (flowRuleName == "Vystup")
            {
                csentry["Vystup"].Value = EntryUtil.DigitsFromDate(EntryUtil.Get(mventry, "employeeEndDate"));
                return;
            }

            base.MapAttributesForExport(flowRuleName, mventry, csentry);
        }
    }
}
