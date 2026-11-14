
using System;
using System.IO;
using System.Xml;
using System.Text;
using System.Collections.Specialized;
using Microsoft.MetadirectoryServices;
using System.Collections.Generic;
using System.Xml.Serialization;
using System.Linq;
using System.Data.SqlClient;
using System.Data;

namespace FimSync_Ezma
{
    public class EzmaExtension :
    IMAExtensible2CallExport,
    IMAExtensible2CallImport,
    //IMAExtensible2FileImport,
    //IMAExtensible2FileExport,
    //IMAExtensible2GetHierarchy,
    IMAExtensible2GetSchema,
    IMAExtensible2GetCapabilities,
    IMAExtensible2GetParameters
    //IMAExtensible2GetPartitions
    {
        string datePortalFormat = "yyyy-MM-ddTHH:mm:ss.000";
        //
        // Constructor
        //
        public EzmaExtension()
        {
            //
            // TODO: Add constructor logic here
            //
        }
        #region schema
        /// <summary>
        /// Implementace IMAExtensible2GetSchema
        /// Datové schéma ConnectorSpace
        /// </summary>
        /// <param name="configParameters"></param>
        /// <returns></returns>
        public Schema GetSchema(System.Collections.ObjectModel.KeyedCollection<string, ConfigParameter> configParameters)
        {
            Microsoft.MetadirectoryServices.SchemaType personType = Microsoft.MetadirectoryServices.SchemaType.Create("Person", false);
            personType.Attributes.Add(SchemaAttribute.CreateAnchorAttribute("ID", AttributeType.String));
            personType.Attributes.Add(SchemaAttribute.CreateSingleValuedAttribute("EmployeeID", AttributeType.String));
            personType.Attributes.Add(SchemaAttribute.CreateSingleValuedAttribute("FirstName", AttributeType.String));
            personType.Attributes.Add(SchemaAttribute.CreateSingleValuedAttribute("LastName", AttributeType.String));
            personType.Attributes.Add(SchemaAttribute.CreateSingleValuedAttribute("FullName", AttributeType.String));
            personType.Attributes.Add(SchemaAttribute.CreateSingleValuedAttribute("ValidFrom", AttributeType.String));
            personType.Attributes.Add(SchemaAttribute.CreateSingleValuedAttribute("ValidTo", AttributeType.String));
            personType.Attributes.Add(SchemaAttribute.CreateSingleValuedAttribute("NickName", AttributeType.String));
            personType.Attributes.Add(SchemaAttribute.CreateSingleValuedAttribute("EMail", AttributeType.String));
            personType.Attributes.Add(SchemaAttribute.CreateSingleValuedAttribute("LastModificationDate", AttributeType.String));
            personType.Attributes.Add(SchemaAttribute.CreateSingleValuedAttribute("ModDate", AttributeType.String));
            personType.Attributes.Add(SchemaAttribute.CreateSingleValuedAttribute("LTime", AttributeType.String));
            personType.Attributes.Add(SchemaAttribute.CreateSingleValuedAttribute("Password", AttributeType.String));
            personType.Attributes.Add(SchemaAttribute.CreateSingleValuedAttribute("WRNG_LOGON", AttributeType.String));
            personType.Attributes.Add(SchemaAttribute.CreateSingleValuedAttribute("LOCAL_LOCK", AttributeType.String));
            personType.Attributes.Add(SchemaAttribute.CreateSingleValuedAttribute("GLOB_LOCK", AttributeType.String));
            personType.Attributes.Add(SchemaAttribute.CreateSingleValuedAttribute("NO_USER_PW", AttributeType.String));
            personType.Attributes.Add(SchemaAttribute.CreateSingleValuedAttribute("FUNCTION", AttributeType.String));
            personType.Attributes.Add(SchemaAttribute.CreateSingleValuedAttribute("DEPARTMENT", AttributeType.String));
            personType.Attributes.Add(SchemaAttribute.CreateSingleValuedAttribute("USERGROUP", AttributeType.String));
            personType.Attributes.Add(SchemaAttribute.CreateSingleValuedAttribute("SPLD", AttributeType.String));
            personType.Attributes.Add(SchemaAttribute.CreateSingleValuedAttribute("SPDB", AttributeType.String));
            personType.Attributes.Add(SchemaAttribute.CreateSingleValuedAttribute("LANGU", AttributeType.String));
            personType.Attributes.Add(SchemaAttribute.CreateSingleValuedAttribute("BAPIEnabled", AttributeType.Boolean));
            personType.Attributes.Add(SchemaAttribute.CreateMultiValuedAttribute("ACTIVITYGROUPS", AttributeType.String));
            personType.Attributes.Add(SchemaAttribute.CreateSingleValuedAttribute("ALIAS", AttributeType.String));
            personType.Attributes.Add(SchemaAttribute.CreateSingleValuedAttribute("GroupsCount", AttributeType.Integer));

            Schema schema = Schema.Create();
            schema.Types.Add(personType);

            Microsoft.MetadirectoryServices.SchemaType groupType = Microsoft.MetadirectoryServices.SchemaType.Create("Group", false);

            groupType.Attributes.Add(SchemaAttribute.CreateAnchorAttribute("ID", AttributeType.String));
            groupType.Attributes.Add(SchemaAttribute.CreateSingleValuedAttribute("EmployeeID", AttributeType.String));
            groupType.Attributes.Add(SchemaAttribute.CreateSingleValuedAttribute("ValidFrom", AttributeType.String));
            groupType.Attributes.Add(SchemaAttribute.CreateSingleValuedAttribute("ValidTo", AttributeType.String));
            groupType.Attributes.Add(SchemaAttribute.CreateSingleValuedAttribute("Description", AttributeType.String));
            groupType.Attributes.Add(SchemaAttribute.CreateSingleValuedAttribute("FullName", AttributeType.String));
            groupType.Attributes.Add(SchemaAttribute.CreateSingleValuedAttribute("BAPIEnabled", AttributeType.Boolean));

            schema.Types.Add(groupType);

            return schema;

        }

        /// <summary>
        /// Implementace IMAExtensible2GetCapabilities
        /// Možnosti MA pro konfiguračního průvodce
        /// </summary>
        public MACapabilities Capabilities
        {
            get
            {
                MACapabilities myCapabilites = new MACapabilities
                {
                    ConcurrentOperation = true,
                    DeleteAddAsReplace = false,
                    DeltaImport = true,
                    DistinguishedNameStyle = MADistinguishedNameStyle.None,
                    ExportPasswordInFirstPass = false,
                    ExportType = MAExportType.AttributeUpdate,
                    FullExport = true,
                    NoReferenceValuesInFirstExport = false,
                    IsDNAsAnchor = false,
                    Normalizations = MANormalizations.None,
                    ObjectConfirmation = MAObjectConfirmation.NoAddAndDeleteConfirmation,
                    ObjectRename = false,
                    SupportExport = false,
                    SupportHierarchy = false,
                    SupportImport = true,
                    SupportPartitions = false,
                    SupportPassword = false
                };
                return myCapabilites;
            }
        }
        /// <summary>
        /// Implementace IMAExtensible2GetParameters
        /// Konfigurovatelné parametry MA pro průvodce
        /// </summary>
        /// <param name="configParameters"></param>
        /// <param name="page"></param>
        /// <returns></returns>
        public System.Collections.Generic.IList<ConfigParameterDefinition> GetConfigParameters(System.Collections.ObjectModel.KeyedCollection<string, ConfigParameter> configParameters, ConfigParameterPage page)
        {
            List<ConfigParameterDefinition> configParametersDefinitions = new List<ConfigParameterDefinition>();

            switch (page)
            {
                case ConfigParameterPage.Connectivity:
                    //configParametersDefinitions.Add(ConfigParameterDefinition.CreateStringParameter("Server host", ""));
                    //configParametersDefinitions.Add(ConfigParameterDefinition.CreateStringParameter("System ID", ""));
                    //configParametersDefinitions.Add(ConfigParameterDefinition.CreateStringParameter("User", ""));
                    //configParametersDefinitions.Add(ConfigParameterDefinition.CreateEncryptedStringParameter("Password", ""));
                    configParametersDefinitions.Add(ConfigParameterDefinition.CreateStringParameter("LogFile", ""));
                    configParametersDefinitions.Add(ConfigParameterDefinition.CreateStringParameter("SAPTools path", ""));
                    configParametersDefinitions.Add(ConfigParameterDefinition.CreateStringParameter("SQL server connectionstring", "", "Data Source=.;initial catalog=FIMSynchronizationService;Integrated Security=SSPI"));
                    break;


                case ConfigParameterPage.Global:
                    //configParametersDefinitions.Add(ConfigParameterDefinition.CreateStringParameter("Kód organizace", ""));
                    //configParametersDefinitions.Add(ConfigParameterDefinition.CreateCheckBoxParameter("Před importem spustit výpočet v SAP"));
                    break;

                case ConfigParameterPage.Partition:
                    break;

                case ConfigParameterPage.RunStep:

                    break;
            }

            return configParametersDefinitions;

        }

        /// <summary>
        /// Implementace IMAExtensible2GetParameters
        /// Validace parametrů, nevyužito
        /// </summary>
        /// <param name="configParameters"></param>
        /// <param name="page"></param>
        /// <returns></returns>
        public ParameterValidationResult ValidateConfigParameters(System.Collections.ObjectModel.KeyedCollection<string, ConfigParameter> configParameters, ConfigParameterPage page)
        {
            ParameterValidationResult myResults = new ParameterValidationResult();

            return myResults;
        }

        #endregion

        #region Import

        /// //////////////////////////////////////////////////////////////////////////////////////////////////////
        /// IMPORT
        /// //////////////////////////////////////////////////////////////////////////////////////////////////////

        int pageSize;
        int pageSizeUsers = 100;
        int importCount = 0;
        int lastProcessed = 0;
        List<Person> persons = new List<Person>();
        //SAPUtils sapUtils = null;
        public string logFile = null;
        public string binFile = "";
        List<string> BAPIRoles = new List<string>();


        /// <summary>
        /// Spuštění importu. Je zavolána webová služba a stažena data.
        /// Data uložena do privátní proměnné pro další zpracování.
        /// </summary>
        /// <param name="configParameters"></param>
        /// <param name="types"></param>
        /// <param name="importRunStep"></param>
        /// <returns></returns>
        public OpenImportConnectionResults OpenImportConnection(System.Collections.ObjectModel.KeyedCollection<string, ConfigParameter> configParameters, Schema types, OpenImportConnectionRunStep importRunStep)
        {

            logFile = configParameters["LogFile"].Value;
            binFile = configParameters["SAPTools path"].Value;
            string constring = configParameters["SQL server connectionstring"].Value;

            //string constring = $"Data Source={SQLServer};initial catalog=FIMSynchronizationService;Integrated Security=SSPI";
            //if (Environment.MachineName.ToUpper() == "VMSEWMIMT01V")
            //{
            //    constring = "Data Source=seweidmt1v;initial catalog=FIMSynchronizationService;Integrated Security=SSPI";

            //}
            //string constring = "Data Source=DBEIDMT.seas.sk,1436;Database=EasyIDM;User Id=sys_IDM.Appool;Password=qLOt4})Eb!063y;MultipleActiveResultSets=true;TrustServerCertificate=True;Persist Security Info=True;Encrypt=False";
            //string constring = "Data Source=.;initial catalog=FIMSynchronizationService;Integrated Security=SSPI";

            SqlConnection conn = new SqlConnection(constring);
            conn.Open();
            SqlCommand cmd = new SqlCommand("SELECT externalRoleCode FROM [FIMSynchronizationService].[dbo].[mms_metaverse] WITH (NOLOCK) where  externalRoleCode is not null and  object_type = 'Group' and   applicationCode LIKE 'SAP%' and  (applicationModulCode NOT LIKE 'RDPV%' or applicationModulCode is null)", conn);
            var dataReader = cmd.ExecuteReader();
            var dataTable = new DataTable();
            dataTable.Load(dataReader);
            foreach (DataRow dr in dataTable.Rows)
            {
                BAPIRoles.Add(dr["externalRoleCode"].ToString().ToLower());
            }
            conn.Close();

            Starter.RunResult res = null;
            res = Starter.RunBinary(binFile, "getAll");

            //File.WriteAllText(@"c:\temp\base64.txt", res.Output);
            var PersonsFromB64 = Convert.FromBase64String(res.Output);
            string strPersons = System.Text.Encoding.UTF8.GetString(PersonsFromB64);

            //var xmldoc = new XmlDocument();
            //xmldoc.Load(@"C:\Program Files\Microsoft Forefront Identity Manager\2010\Synchronization Service\MaData\SAP_BAPI\SAP.XML");

            List<Person> p = new List<Person>();
            using (var stringReader = new System.IO.StringReader(strPersons))
            {
                var serializer = new XmlSerializer(typeof(List<Person>));
                persons = serializer.Deserialize(stringReader) as List<Person>;
            }

            //Person p1 = persons.Where(x => x.EmployeeID == "U812228").FirstOrDefault();
            //Log("p1:" + p1.EmployeeID);

            //for (int i = 0; i < persons.Count; i++)
            //{
            //    Log(i.ToString() + ": " + persons[i].EmployeeID);
            //}

            pageSize = importRunStep.PageSize;
            return new OpenImportConnectionResults();
        }



        public GetImportEntriesResults GetImportEntries(GetImportEntriesRunStep importRunStep)
        {
            //int firstRecord = importCount * pageSize;
            //int lastRecord = importCount * pageSize + pageSize - 1;
            int firstRecord = lastProcessed;
            int lastRecord = lastProcessed + pageSize - 1;


            if (lastRecord > persons.Count - 1)
            {
                lastRecord = persons.Count - 1;
            }
            lastProcessed = lastRecord;
            List<CSEntryChange> csentries = new List<CSEntryChange>();

            Log("-------First: " + firstRecord.ToString() + ", last: " + lastRecord + " Celkem: " + persons.Count + "----------------------");
            for (int i = firstRecord; i <= lastRecord; i++)
            {
                //Log("Processing " + i.ToString() + ": " + persons[i].EmployeeID);
                //SAPReference.ZskseMimOutput item = persons[i];
                Person item = persons[i];
                if (csentries.Count + 1 + item.UserGroups.Count > pageSize)
                {
                    lastProcessed = i;
                    Log("Break " + i.ToString() + " " + persons[i].EmployeeID);
                    break;
                }


                CSEntryChange csentry = CSEntryChange.Create();
                csentry.ObjectModificationType = ObjectModificationType.Add;
                csentry.ObjectType = "Person";

                //DateTime validFrom = DateTime.MinValue;
                //DateTime validTo = DateTime.MaxValue;
                string ID = i.ToString();
                List<string> ugroups = new List<string>();
                foreach (Group gr in item.UserGroups)
                {
                    Log("ACTIVITYGROUP: " + item.EmployeeID + ": '" + gr.FullName + "'");
                    ugroups.Add(gr.FullName);
                }

                if (ugroups.Count > 0)
                {
                    csentry.AttributeChanges.Add(AttributeChange.CreateAttributeAdd("ACTIVITYGROUPS", ugroups.ToArray()));
                }
                csentry.AttributeChanges.Add(AttributeChange.CreateAttributeAdd("GroupsCount", ugroups.Count));

                foreach (var prop in item.GetType().GetProperties())
                {

                    string value = null;

                    switch (prop.Name)
                    {

                        case "EmployeeID":
                            try
                            {
                                //value = ChangeFormat(item.GetType().GetProperty(prop.Name).GetValue(item).ToString(), "yyyy-MM-dd", datePortalFormat, true);
                                value = item.GetType().GetProperty(prop.Name).GetValue(item).ToString().Trim();
                                ID = value;
                            }
                            catch (Exception)
                            {
                            }
                            break;
                        default:
                            try
                            {
                                if (item.GetType().GetProperty(prop.Name) != null && item.GetType().GetProperty(prop.Name).GetValue(item) != null)
                                    value = item.GetType().GetProperty(prop.Name).GetValue(item).ToString().Trim();
                            }
                            catch (Exception e)
                            {
                                string a = "";
                                throw;
                            }
                            break;
                    }
                    // Log(prop.Name + ": " + value);
                    if (value != null && value.Length > 0 && prop.Name != "GroupsCount")
                    {
                        csentry.AttributeChanges.Add(AttributeChange.CreateAttributeAdd(prop.Name, value));
                    }
                }
                //Log("ID: " + ID);
                csentry.AttributeChanges.Add(AttributeChange.CreateAttributeAdd("ID", ID));

                //                csentry.AttributeChanges.Add(AttributeChange.CreateAttributeAdd("EmployeeStatusCounted", EmployeeStatusCounted));
                //csentries.Add(csentry);
                //if (validTo > DateTime.Now.AddDays(-7))
                //{

                //}
                bool userBAPIEnabled = false;
                foreach (Group group in item.UserGroups)
                {
                    CSEntryChange csentryG = CSEntryChange.Create();
                    csentryG.ObjectModificationType = ObjectModificationType.Add;
                    csentryG.ObjectType = "Group";
                    string GroupName = "";
                    string UserID = "";

                    foreach (var prop in group.GetType().GetProperties())
                    {
                        string value = null;

                        switch (prop.Name)
                        {

                            default:
                                try
                                {
                                    if (group.GetType().GetProperty(prop.Name) != null && group.GetType().GetProperty(prop.Name).GetValue(group) != null)
                                    {
                                        value = group.GetType().GetProperty(prop.Name).GetValue(group).ToString().Trim();
                                        if (prop.Name == "EmployeeID")
                                        {
                                            UserID = value;
                                        }
                                        if (prop.Name == "FullName")
                                        {
                                            GroupName = value;
                                        }

                                    }

                                }
                                catch (Exception e)
                                {
                                    throw;
                                }
                                break;
                        }
                        //Log("Group " + prop.Name + ": " + value);

                        if (value != null && value.Length > 0)
                        {
                            csentryG.AttributeChanges.Add(AttributeChange.CreateAttributeAdd(prop.Name, value));
                        }
                    }
                    //Log("GroupID: " + GroupName + "_" + UserID);
                    csentryG.AttributeChanges.Add(AttributeChange.CreateAttributeAdd("BAPIEnabled", true));
                    csentryG.AttributeChanges.Add(AttributeChange.CreateAttributeAdd("ID", GroupName + "_" + UserID));
                    if (BAPIRoles.Contains(GroupName.ToLower()))
                    {
                        csentries.Add(csentryG);
                        //userBAPIEnabled = true;
                    }
                }

                csentry.AttributeChanges.Add(AttributeChange.CreateAttributeAdd("BAPIEnabled", true));
                csentries.Add(csentry);

                /*
                             personType.Attributes.Add(SchemaAttribute.CreateSingleValuedAttribute("BAPIEnabled", AttributeType.Boolean));
            personType.Attributes.Add(SchemaAttribute.CreateMultiValuedAttribute("ACTIVITYGROUPS", AttributeType.String));
            personType.Attributes.Add(SchemaAttribute.CreateSingleValuedAttribute("GroupsCount", AttributeType.Integer));
                 */
            }

            importCount += 1;
            GetImportEntriesResults importReturnInfo = new GetImportEntriesResults();
            importReturnInfo.MoreToImport = persons.Count - 1 > lastProcessed;
            importReturnInfo.CSEntries = csentries;
            return importReturnInfo;
        }

        /// <summary>
        /// Ukončení importu, nevyužito
        /// </summary>
        /// <param name="importRunStep"></param>
        /// <returns></returns>
        public CloseImportConnectionResults CloseImportConnection(CloseImportConnectionRunStep importRunStep)
        {
            return new CloseImportConnectionResults();
        }

        /// <summary>
        /// Výchozí velikost stránky pro průvodce Sync Run
        /// </summary>
        public int ImportDefaultPageSize
        {
            get { return 1000; }
        }

        /// <summary>
        /// Maximální povolená velikost stránky
        /// </summary>
        public int ImportMaxPageSize
        {
            get { return 1000; }
        }

        #endregion

        #region Export
        /// //////////////////////////////////////////////////////////////////////////////////////////////////////
        /// EXPORT
        /// //////////////////////////////////////////////////////////////////////////////////////////////////////

        public void CloseExportConnection(CloseExportConnectionRunStep exportRunStep)
        {

        }

        public int ExportDefaultPageSize
        {
            get { return 1000; }
        }

        public int ExportMaxPageSize
        {
            get { return 1000; }
        }

        public void OpenExportConnection(System.Collections.ObjectModel.KeyedCollection<string, ConfigParameter> configParameters, Schema types, OpenExportConnectionRunStep exportRunStep)
        {
            logFile = configParameters["LogFile"].Value;
            binFile = configParameters["SAPTools path"].Value;

            //SetUpWSClient(configParameters);
        }

        public PutExportEntriesResults PutExportEntries(IList<CSEntryChange> csentries)
        {
            List<Person> persons = new List<Person>();
            List<Group> groups = new List<Group>();
            //Person p = new Person();
            //p.EmployeeID = "U001432";
            //p.LastName = "Szczęsna";
            //persons.Add(p);
            //string strPersons = Serializer.WriteToXmlString(persons);
            //string B64Persons = Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(strPersons));

            foreach (CSEntryChange item in csentries.Where(x => x.ObjectType == "Person"))
            {
                Log("Test: " + item.AnchorAttributes[0].Value.ToString() + " > " + item.ObjectModificationType.ToString());
            }


            foreach (CSEntryChange item in csentries.Where(x => x.ObjectType == "Person" && x.ObjectModificationType == ObjectModificationType.Add))
            {
                Log("Creating " + item.AnchorAttributes[0].Value.ToString());
                Person personChange = new Person();
                personChange.EmployeeID = item.AnchorAttributes[0].Value.ToString();
                foreach (string attribName in item.ChangedAttributeNames)
                {
                    string attribValue = "";
                    attribValue = item.AttributeChanges[attribName].ValueChanges[0].Value.ToString();
                    personChange.GetType().GetProperty(attribName).SetValue(personChange, attribValue, null);
                    Log(attribName + "=" + attribValue);
                }

                List<Person> personsToUpdate = new List<Person>();
                personsToUpdate.Add(personChange);
                string strPersons = null;
                using (var stringwriter = new System.IO.StringWriter())
                {
                    var serializer = new XmlSerializer(personsToUpdate.GetType());
                    serializer.Serialize(stringwriter, personsToUpdate);
                    strPersons = stringwriter.ToString();
                }
                Log(strPersons);
                string B64Persons = Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(strPersons));
                Starter.RunResult res = Starter.RunBinary(binFile, "create " + B64Persons);
                Log(res.Output);
            }

            foreach (CSEntryChange item in csentries.Where(x => x.ObjectType == "Person" && x.ObjectModificationType == ObjectModificationType.Update))
            {
                Log("Updating " + item.AnchorAttributes[0].Value.ToString());
                Person personChange = new Person();
                bool isChange = false;
                foreach (string attribName in item.ChangedAttributeNames)
                {
                    string attribValue = "";
                    personChange.EmployeeID = item.AnchorAttributes[0].Value.ToString();
                    Log("ChangeType:" + item.AttributeChanges[attribName].ModificationType.ToString());
                    switch (item.AttributeChanges[attribName].ModificationType)
                    {
                        case AttributeModificationType.Add:
                            foreach (var attr in item.AttributeChanges[attribName].ValueChanges)
                            {
                                Log(attribName + "=" + attr.Value.ToString() + ":: " + attr.ModificationType.ToString());
                            }
                            attribValue = item.AttributeChanges[attribName].ValueChanges[0].Value.ToString();
                            personChange.GetType().GetProperty(attribName).SetValue(personChange, attribValue, null);
                            Log(attribName + "=" + attribValue);
                            isChange = true;
                            break;
                        case AttributeModificationType.Update:
                            //attribValue = item.AttributeChanges[attribName].ValueChanges[1].Value.ToString();
                            attribValue = item.AttributeChanges[attribName].ValueChanges.First(x => x.ModificationType == ValueModificationType.Add).Value.ToString();
                            foreach (var attr in item.AttributeChanges[attribName].ValueChanges)
                            {
                                Log(attribName + "=" + attr.Value.ToString() + ": " + attr.ModificationType.ToString());
                            }


                            try
                            {
                                if (attribName == "GroupsCount")
                                {
                                    personChange.GetType().GetProperty(attribName).SetValue(personChange, Convert.ToInt32(attribValue), null);
                                }
                                else
                                {
                                    personChange.GetType().GetProperty(attribName).SetValue(personChange, attribValue, null);
                                }
                                Log(attribName + "=" + attribValue.ToString());
                                isChange = true;
                            }
                            catch (Exception e)
                            {
                                Log("BAPI Error: " + attribName + "=" + attribValue + ";Error:" + e.Message);
                                Log("BAPI Error: " + attribName + "=" + attribValue + ";Error:" + e.StackTrace);
                            }
                            break;
                        case AttributeModificationType.Delete:
                            personChange.GetType().GetProperty(attribName).SetValue(personChange, null, null);
                            Log(attribName + "=" + attribValue);
                            isChange = true;
                            break;
                        default:
                            throw new NotImplementedException("ModificationType=" + item.AttributeChanges[attribName].ModificationType.ToString());
                    }
                }
                if (isChange)
                {
                    List<Person> personsToUpdate = new List<Person>();
                    personsToUpdate.Add(personChange);
                    string strPersons = null;
                    using (var stringwriter = new System.IO.StringWriter())
                    {
                        var serializer = new XmlSerializer(personsToUpdate.GetType());
                        serializer.Serialize(stringwriter, personsToUpdate);
                        strPersons = stringwriter.ToString();
                    }
                    Log(strPersons);
                    string B64Persons = Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(strPersons));
                    Starter.RunResult res = Starter.RunBinary(binFile, "update " + B64Persons);
                    Log(res.Output);
                    //Starter.RunResult res = Starter.RunBinary(@"C:\Projects\SAP_BAPI2\SAPTools\bin\Debug\SAPTools.exe", "update " + strPersons);

                }
            }

            foreach (CSEntryChange item in csentries.Where(x => x.ObjectType == "Group" && x.ObjectModificationType == ObjectModificationType.Add))
            {
                Log("Creating request " + item.AnchorAttributes[0].Value.ToString());
                Group groupChange = new Group();
                foreach (string attribName in item.ChangedAttributeNames)
                {
                    string attribValue = "";
                    attribValue = item.AttributeChanges[attribName].ValueChanges[0].Value.ToString();
                    groupChange.GetType().GetProperty(attribName).SetValue(groupChange, attribValue, null);
                    Log(attribName + "=" + attribValue);
                }

                List<Group> personsToUpdate = new List<Group>();
                personsToUpdate.Add(groupChange);
                string strGroups = null;
                using (var stringwriter = new System.IO.StringWriter())
                {
                    var serializer = new XmlSerializer(personsToUpdate.GetType());
                    serializer.Serialize(stringwriter, personsToUpdate);
                    strGroups = stringwriter.ToString();
                }
                Log(strGroups);
                string B64Groups = Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(strGroups));
                Log(B64Groups);
                Starter.RunResult res = Starter.RunBinary(binFile, "addrole " + B64Groups);
                Log(res.Output);
            }

            foreach (CSEntryChange item in csentries.Where(x => x.ObjectType == "Group" && x.ObjectModificationType == ObjectModificationType.Update))
            {
                Log("Updating request " + item.AnchorAttributes[0].Value.ToString());
                Group groupChange = new Group();
                foreach (string attribName in item.ChangedAttributeNames)
                {
                    string attribValue = "";
                    //attribValue = item.AttributeChanges[attribName].ValueChanges[0].Value.ToString();
                    attribValue = item.AttributeChanges[attribName].ValueChanges.First(x => x.ModificationType == ValueModificationType.Add).Value.ToString();
                    groupChange.GetType().GetProperty(attribName).SetValue(groupChange, attribValue, null);
                    Log(attribName + "=" + attribValue);
                }

                string anchor = item.AnchorAttributes[0].Value.ToString();
                string[] items = anchor.Split('_');
                groupChange.EmployeeID = items[items.Length - 1];
                groupChange.FullName = anchor.Replace("_" + groupChange.EmployeeID, "");

                List<Group> personsToUpdate = new List<Group>();
                personsToUpdate.Add(groupChange);
                string strGroups = null;
                using (var stringwriter = new System.IO.StringWriter())
                {
                    var serializer = new XmlSerializer(personsToUpdate.GetType());
                    serializer.Serialize(stringwriter, personsToUpdate);
                    strGroups = stringwriter.ToString();
                }
                Log(strGroups);
                string B64Groups = Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(strGroups));
                Log(B64Groups);
                Starter.RunResult res = Starter.RunBinary(binFile, "updaterole " + B64Groups);
                Log(res.Output);
            }

            foreach (CSEntryChange item in csentries.Where(x => x.ObjectType == "Group" && x.ObjectModificationType == ObjectModificationType.Delete))
            {
                Log("Deleting request " + item.AnchorAttributes[0].Value.ToString());
                Group groupChange = new Group();
                foreach (string attribName in item.ChangedAttributeNames)
                {
                    string attribValue = "";
                    attribValue = item.AttributeChanges[attribName].ValueChanges[0].Value.ToString();
                    groupChange.GetType().GetProperty(attribName).SetValue(groupChange, attribValue, null);
                    Log(attribName + "=" + attribValue);
                }

                string anchor = item.AnchorAttributes[0].Value.ToString();
                string[] items = anchor.Split('_');
                groupChange.EmployeeID = items[items.Length - 1];
                groupChange.FullName = anchor.Replace("_" + groupChange.EmployeeID, "");

                List<Group> personsToUpdate = new List<Group>();
                personsToUpdate.Add(groupChange);
                string strGroups = null;
                using (var stringwriter = new System.IO.StringWriter())
                {
                    var serializer = new XmlSerializer(personsToUpdate.GetType());
                    serializer.Serialize(stringwriter, personsToUpdate);
                    strGroups = stringwriter.ToString();
                }
                Log(strGroups);
                string B64Groups = Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(strGroups));
                Log(B64Groups);
                Starter.RunResult res = Starter.RunBinary(binFile, "removerole " + B64Groups);
                Log(res.Output);
            }

            PutExportEntriesResults exportEntriesResults = new PutExportEntriesResults();
            return exportEntriesResults;

        }
        #endregion

        #region Funkce
        public void Log(string text)
        {
            if (!string.IsNullOrEmpty(logFile))
            {
                File.AppendAllText(logFile, text + Environment.NewLine);
            }
        }
        private string ChangeFormat(string date, string fromFormat, string toFormat, bool IsEndDate = false)
        {
            DateTime dt;
            if (!DateTime.TryParseExact(date, fromFormat, System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.None, out dt))
            {
                return "";
            }
            if (IsEndDate)
                dt = dt.AddHours(23).AddMinutes(59);
            return dt.ToString(toFormat);
        }

        #endregion
    };
}
