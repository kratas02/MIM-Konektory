
using Microsoft.MetadirectoryServices;
using System;
using System.Globalization;

namespace Mms_ManagementAgent_CZURulesExtension
{
    /// <summary>
    /// Summary description for MAExtensionObject.
	/// </summary>
	public class MAExtensionObject : IMASynchronization
    {
        public MAExtensionObject()
        {
            //
            // TODO: Add constructor logic here
            //
        }
        void IMASynchronization.Initialize()
        {
            //
            // TODO: write initialization code
            //
        }

        void IMASynchronization.Terminate()
        {
            //
            // TODO: write termination code
            //
        }

        bool IMASynchronization.ShouldProjectToMV(CSEntry csentry, out string MVObjectType)
        {
            //
            // TODO: Remove this throw statement if you implement this method
            //
            throw new EntryPointNotImplementedException();
        }

        DeprovisionAction IMASynchronization.Deprovision(CSEntry csentry)
        {
            //
            // TODO: Remove this throw statement if you implement this method
            //
            throw new EntryPointNotImplementedException();
        }

        bool IMASynchronization.FilterForDisconnection(CSEntry csentry)
        {
            //
            // TODO: write connector filter code
            //
            throw new EntryPointNotImplementedException();
        }

        void IMASynchronization.MapAttributesForJoin(string FlowRuleName, CSEntry csentry, ref ValueCollection values)
        {
            if (FlowRuleName == "cd.user#1:cn->PK_ZAM")
            {
                try
                {
                    if (csentry["cn"].IsPresent)
                    {
                        if (csentry["cn"].StringValue.Contains("_"))
                        {
                            string[] items = csentry["cn"].StringValue.Split('_');
                            values.Add(items[items.Length - 1]);
                        }
                    }

                }
                catch (Exception)
                {
                }
            }
            else if (FlowRuleName == "cd.Identity#1:techName->PK_ZAM")
            {
                if (csentry["techName"].IsPresent)
                {
                    values.Add(csentry["techName"].StringValue);
                }
            }

            //
            else
            {
                throw new EntryPointNotImplementedException();
            }

            //
            // TODO: write join mapping code
            //

        }

        bool IMASynchronization.ResolveJoinSearch(string joinCriteriaName, CSEntry csentry, MVEntry[] rgmventry, out int imventry, ref string MVObjectType)
        {
            //
            // TODO: write join resolution code
            //
            throw new EntryPointNotImplementedException();
        }

        void IMASynchronization.MapAttributesForImport(string FlowRuleName, CSEntry csentry, MVEntry mventry)
        {
            switch (FlowRuleName)
            {
                case "cd.BusinessRole:AccountName->mv.BRMagion:accountName":
                    if (csentry["AccountName"].IsPresent)
                    {
                        mventry["accountName"].StringValue = csentry["AccountName"].StringValue.Replace("ROL", "BRC");
                    }
                    break;
                case "cd.Card:passnumber,uic->mv.card:displayName":

                    if (csentry["passnumber"].IsPresent && (csentry["uic"].IsPresent))
                        {
                        mventry["displayName"].StringValue = csentry["uic"].StringValue + " - " + csentry["passnumber"].StringValue;
                    }
                    break;

                case "cd.ApplicationRole:Vyjimka->mv.ARMagion:vyjimka":
                    if (csentry["Vyjimka"].IsPresent)
                    {
                        if (csentry["Vyjimka"].StringValue == "ne")
                        {
                            mventry["vyjimka"].BooleanValue = false;
                        }
                        else
                        {
                            mventry["vyjimka"].BooleanValue = true;
                        }
                    }
                    else
                    {
                        mventry["vyjimka"].Delete();
                    }
                    break;
                case "cd.ApplicationRole:isParentGroup->mv.ARMagion:isParentGroup":
                    if (csentry["isParentGroup"].IsPresent)
                    {
                        if (csentry["isParentGroup"].StringValue == "false")
                        {
                            mventry["isParentGroup"].BooleanValue = false;
                        }
                        else
                        {
                            mventry["isParentGroup"].BooleanValue = true;
                        }
                    }
                    else
                    {
                        mventry["isParentGroup"].Delete();
                    }
                    break;
                case "cd.PPV:FUNKCE,POZICE,ZARAZENI->mv.PPV:claFunkcePPV":
                    string fn = null;
                    if (csentry["FUNKCE"].IsPresent)
                    {
                        fn = csentry["FUNKCE"].StringValue;
                    }
                    else if (csentry["POZICE"].IsPresent)
                    {
                        fn = csentry["POZICE"].StringValue;
                    }
                    else if (csentry["ZARAZENI"].IsPresent)
                    {
                        fn = csentry["ZARAZENI"].StringValue;
                    }
                    if (fn != null)
                    {
                        mventry["claFunkcePPV"].StringValue = fn;
                    }
                    else
                    {
                        mventry["claFunkcePPV"].Delete();
                    }
                    break;
                case "cd.PPV:FUNKCE_KOD,ZARAZENI_KOD->mv.PPV:claFunkceKodPPV":
                    object kfn = null;
                    if (csentry["FUNKCE_KOD"].IsPresent)
                    {
                        kfn = csentry["FUNKCE_KOD"].Value;
                    }
                    else if (csentry["ZARAZENI_KOD"].IsPresent)
                    {
                        kfn = csentry["ZARAZENI_KOD"].Value;
                    }
                    if (kfn != null)
                    {
                        mventry["claFunkceKodPPV"].Value = kfn.ToString();
                    }
                    else
                    {
                        mventry["claFunkceKodPPV"].Delete();
                    }
                    break;

                case "cd.user:sAMAccountName->mv.person:windowsUserName":
                    if (csentry["sAMAccountName"].IsPresent)
                    {
                        mventry["windowsUserName"].StringValue = "CZU\\" + csentry["sAMAccountName"].StringValue;
                    }

                    break;
                case "cd.Person:<dn>->mv.person:employeeEndDate":
                    {
                        mventry["employeeEndDate"].Delete();
                    }
                    break;
                default:
                    throw new EntryPointNotImplementedException();
            }
        }

        string convertToPortalFormat(string input, string format = "M/d/yyyy h:mm:ss tt")
        {

            DateTime dt = DateTime.ParseExact(
                input,
                format,
                CultureInfo.InvariantCulture);
            string result = dt.Date.ToString("yyyy-MM-dd'T'HH:mm:ss", CultureInfo.InvariantCulture);
            return result;
        }
        void IMASynchronization.MapAttributesForExport(string FlowRuleName, MVEntry mventry, CSEntry csentry)
        {
            switch (FlowRuleName)
            {
                case "cd.Identity:claSex<-mv.person:claSex":
                    if (mventry["claSex"].IsPresent)
                    {
                        if (mventry["claSex"].StringValue == "M")
                        {
                            csentry["claSex"].StringValue = "300";
                        }
                        else if (mventry["claSex"].StringValue == "Z")
                        {
                            csentry["claSex"].StringValue = "301";
                        }

                    }

                    break;
                case "cd.BaseUnit:claBaseUnitCode<-mv.baseUnit:claBaseUnitCode":
                    if (mventry["claBaseUnitCode"].IsPresent)
                    {
                        csentry["claBaseUnitCode"].StringValue = mventry["claBaseUnitCode"].StringValue.Trim();
                    }
                    else
                    {
                        csentry["claBaseUnitCode"].Delete();
                    }
                    break;
                case "cd.BaseUnit:claParentBaseUnitCode<-mv.baseUnit:claParentBaseUnitCode":
                    if (mventry["claParentBaseUnitCode"].IsPresent)
                    {
                        csentry["claParentBaseUnitCode"].StringValue = mventry["claParentBaseUnitCode"].StringValue.Trim();
                    }
                    else
                    {
                        csentry["claParentBaseUnitCode"].Delete();
                    }
                    break;
                case "cd.Identity:validFrom<-mv.person:employeeStartDate":
                    if (mventry["employeeStartDate"].IsPresent)
                    {
                        csentry["validFrom"].StringValue = convertToPortalFormat(mventry["employeeStartDate"].Value.ToString());
                    }
                    else
                    {
                        csentry["validFrom"].Delete();
                    }

                    break;
                case "cd.Identity:validTo<-mv.person:employeeEndDate":
                    if (mventry["employeeEndDate"].IsPresent)
                    {
                        csentry["validTo"].StringValue = convertToPortalFormat(mventry["employeeEndDate"].Value.ToString());
                    }
                    else
                    {
                        csentry["validTo"].Delete();
                    }

                    break;

                case "cd.Card:validFrom<-mv.card:validFrom":
                    if (mventry["validFrom"].IsPresent)
                    {//none,validFrom,string,2013-01-08 08:06:55
                        csentry["validFrom"].StringValue = convertToPortalFormat(mventry["validFrom"].Value.ToString(), "yyyy-MM-dd HH:mm:ss");
                    }
                    else
                    {
                        csentry["validFrom"].Delete();
                    }
                    break;
                case "cd.Card:validTo<-mv.card:ValidTo":
                    if (mventry["ValidTo"].IsPresent)
                    {
                        csentry["ValidTo"].StringValue = convertToPortalFormat(mventry["ValidTo"].Value.ToString(), "yyyy-MM-dd HH:mm:ss");
                    }
                    else
                    {
                        csentry["ValidTo"].Delete();
                    }
                    break;
                case "cd.Identity:outFrom<-mv.person:outFrom":
                    if (mventry["claEndDatePPV"].IsPresent)
                    {
                        csentry["outFrom"].StringValue = convertToPortalFormat(mventry["outFrom"].Value.ToString());
                    }
                    else
                    {
                        csentry["outFrom"].Delete();
                    }

                    break;
                case "cd.Identity:inFrom<-mv.person:inFrom":
                    if (mventry["claEndDatePPV"].IsPresent)
                    {
                        csentry["inFrom"].StringValue = convertToPortalFormat(mventry["inFrom"].Value.ToString());
                    }
                    else
                    {
                        csentry["inFrom"].Delete();
                    }

                    break;

                case "cd.PPV:validTo<-mv.PPV:claEndDatePPV":
                    if (mventry["claEndDatePPV"].IsPresent)
                    {
                        csentry["validTo"].StringValue = convertToPortalFormat(mventry["claEndDatePPV"].Value.ToString());
                    }
                    else
                    {
                        csentry["validTo"].Delete();
                    }

                    break;
                case "cd.PPV:validFrom<-mv.PPV:claStartDatePPV":
                    if (mventry["claStartDatePPV"].IsPresent)
                    {
                        csentry["validFrom"].StringValue = convertToPortalFormat(mventry["claStartDatePPV"].Value.ToString());
                    }
                    break;
                case "cd.PPV:outFrom<-mv.PPV:claVynetiOd":
                    if (mventry["claVynetiOd"].IsPresent)
                    {
                        csentry["outFrom"].StringValue = convertToPortalFormat(mventry["claVynetiOd"].Value.ToString());
                    }
                    else
                    {
                        csentry["outFrom"].Delete();
                    }
                    break;
                case "cd.PPV:inFrom<-mv.PPV:claVynetiDo":
                    if (mventry["claVynetiDo"].IsPresent)
                    {
                        csentry["inFrom"].StringValue = convertToPortalFormat(mventry["claVynetiDo"].Value.ToString());
                    }
                    else
                    {
                        csentry["inFrom"].Delete();
                    }
                    break;
                case "cd.Identity:workforceID<-mv.person:PK_ZAM":
                    if (mventry["PK_ZAM"].IsPresent)
                    {
                        csentry["workforceID"].StringValue = mventry["PK_ZAM"].Value.ToString();
                    }
                    else
                    {
                        csentry["workforceID"].Delete();
                    }
                    break;

                case "cd.Identity:techName<-mv.person:accountName,PK_ZAM":
                    if (mventry["accountName"].IsPresent)
                    {
                        csentry["techName"].StringValue = mventry["accountName"].StringValue;
                    }
                    else if (mventry["PK_ZAM"].IsPresent)
                    {
                        csentry["techName"].StringValue = mventry["PK_ZAM"].Value.ToString();
                    }
                    break;
                case "cd.BaseUnit:validFrom<-mv.baseUnit:claBaseUnitValidFrom":
                    if (mventry["claBaseUnitValidFrom"].IsPresent)
                    {
                        string[] parts = mventry["claBaseUnitValidFrom"].StringValue.Split('/');
                        csentry["validFrom"].StringValue = $"{parts[2].Split(' ')[0]}-{parts[0].PadLeft(2, '0')}-{parts[1].PadLeft(2, '0')}" + "T00:00:00";
                    }
                    else
                    {
                        csentry["validFrom"].Delete();
                    }
                    break;

                case "cd.PPV:name<-mv.PPV:claOrderPPV,claStrediskoKodPPV,claUICPPV":
                    string PPVName = mventry["claUICPPV"].Value.ToString();
                    if (mventry["claOrderPPV"].IsPresent)
                    {
                        string order = "000" + mventry["claOrderPPV"].IntegerValue.ToString();
                        order = order.Substring(order.Length - 3);


                        PPVName = PPVName + "-" + order;
                    }
                    if (mventry["claStrediskoKodPPV"].IsPresent)
                    {
                        PPVName = PPVName + "-" + mventry["claStrediskoKodPPV"].StringValue;
                    }

                    csentry["name"].StringValue = PPVName;

                    break;
                case "cd.PPV:name<-mv.PPV:claDruhPPV,claOrderPPV,claOsCisZAM":
                    string PPVName2 = mventry["PK_PPV"].StringValue;
                    if (mventry["claDruhPPV"].IsPresent)
                    {
                        csentry["name"].StringValue = PPVName2 + " " + mventry["claDruhPPV"].Value.ToString();
                    }
                    else
                    {
                        csentry["name"].StringValue = PPVName2;
                    }
                    break;

                //claStrediskoZkratkaPPV,KARP,MAGION,string,8/5/2026 7:18:30 PM
                case "cd.Identity:name<-mv.person:firstName,lastName,PK_ZAM":

                    if (mventry["firstName"].IsPresent && mventry["lastName"].IsPresent && mventry["PK_ZAM"].IsPresent)
                    {
                        csentry["name"].StringValue = mventry["lastName"].Value.ToString() + " " + mventry["firstName"].Value.ToString() + " (" + mventry["PK_ZAM"].Value.ToString() + ")";
                    }
                    else
                    {
                        csentry["name"].StringValue = mventry["PK_ZAM"].Value.ToString();
                    }
                    break;
                default:
                    throw new EntryPointNotImplementedException();
            }
        }
    }
}
