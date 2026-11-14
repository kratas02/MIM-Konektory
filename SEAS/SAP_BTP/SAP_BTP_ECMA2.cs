
using Microsoft.MetadirectoryServices;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.IO;
using System.Linq;
using System.Text;
using System.Xml;

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
        public string proxy = "";
        string importURL = "";
        // public string logFolder = "";
        public AccessToken accessToken = null;
        private int _exportDelayMs = 1000;
        public int TotalUsers = 0;
        public int TotalGroups = 0;
        public int startIndex = 0;
        public int pagesize = 50;
        public List<string> ugroups = new List<string>();
        public List<Assigment> ucasts = new List<Assigment>();
        public bool usersDone = false;
        public bool groupsDone = false;
        public List<Assigment> assigments = new List<Assigment>();
        public string MAName = "";
        public List<DependentRoles> dependentRoles = new List<DependentRoles>();
        public string ExporFilePath;
        public Dictionary<string, HashSet<string>> importedRoleMembers = new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase);


        private Dictionary<string, User> _exportUserCache = new Dictionary<string, User>();
        private void AddGroupToUserCache(User u, string groupId)
        {
            if (u == null) return;
            var list = u.groups != null ? u.groups.ToList() : new List<userGroup>();
            if (!list.Any(g => string.Equals((g.value ?? "").Trim(), (groupId ?? "").Trim(), StringComparison.OrdinalIgnoreCase)))
            {
                list.Add(new userGroup { value = groupId, display = groupId });
                u.groups = list.ToArray();
            }
        }
        private void RemoveGroupFromUserCache(User u, string groupId)
        {
            if (u == null || u.groups == null) return;
            var list = u.groups.ToList();
            list.RemoveAll(g => string.Equals((g.value ?? "").Trim(), (groupId ?? "").Trim(), StringComparison.OrdinalIgnoreCase));
            u.groups = list.ToArray();
        }

        //
        // Constructor
        //

        private int m_importDefaultPageSize = 12;
        private int m_importMaxPageSize = 200;
        private int m_exportDefaultPageSize = 150;
        private int m_exportMaxPageSize = 1000;



        public EzmaExtension()
        {
            //
            // TODO: Add constructor logic here
            //
        }

        public MACapabilities Capabilities
        {
            get
            {
                MACapabilities myCapabilities = new MACapabilities();

                myCapabilities.ConcurrentOperation = false;
                myCapabilities.ObjectRename = false;
                myCapabilities.DeleteAddAsReplace = true;
                myCapabilities.DeltaImport = true;
                myCapabilities.DistinguishedNameStyle = MADistinguishedNameStyle.None;
                myCapabilities.ExportType = MAExportType.AttributeUpdate;
                myCapabilities.NoReferenceValuesInFirstExport = false;
                myCapabilities.Normalizations = MANormalizations.None;
                myCapabilities.ExportPasswordInFirstPass = false;
                myCapabilities.FullExport = false;
                myCapabilities.NoReferenceValuesInFirstExport = false;
                myCapabilities.IsDNAsAnchor = false;
                myCapabilities.ObjectConfirmation = MAObjectConfirmation.NoAddAndDeleteConfirmation;
                myCapabilities.ObjectRename = false;
                myCapabilities.SupportExport = true;
                myCapabilities.SupportHierarchy = false;
                myCapabilities.SupportImport = true;
                myCapabilities.SupportPartitions = false;
                myCapabilities.SupportPassword = false;

                return myCapabilities;
            }
        }
        public IList<ConfigParameterDefinition> GetConfigParameters(KeyedCollection<string, ConfigParameter> configParameters,
                                                           ConfigParameterPage page)
        {
            List<ConfigParameterDefinition> configParametersDefinitions = new List<ConfigParameterDefinition>();

            switch (page)
            {
                case ConfigParameterPage.Connectivity:

                    configParametersDefinitions.Add(ConfigParameterDefinition.CreateStringParameter("BTP API URL", ""));
                    configParametersDefinitions.Add(ConfigParameterDefinition.CreateStringParameter("Token URL", ""));
                    configParametersDefinitions.Add(ConfigParameterDefinition.CreateStringParameter("Auth string", ""));
                    configParametersDefinitions.Add(ConfigParameterDefinition.CreateStringParameter("Auth string2", ""));
                    configParametersDefinitions.Add(ConfigParameterDefinition.CreateStringParameter("Proxy server", ""));
                    configParametersDefinitions.Add(ConfigParameterDefinition.CreateStringParameter("LogFolder", ""));
                    configParametersDefinitions.Add(ConfigParameterDefinition.CreateStringParameter("Zoznam clenstva", ""));
                    configParametersDefinitions.Add(ConfigParameterDefinition.CreateStringParameter("Export delay (ms)", "1000"));
                    //configParametersDefinitions.Add(ConfigParameterDefinition.CreateEncryptedStringParameter("Password", ""));
                    break;

                case ConfigParameterPage.Global:


                    break;

                case ConfigParameterPage.Partition:
                    break;

                case ConfigParameterPage.RunStep:

                    break;
            }

            return configParametersDefinitions;
        }

        public ParameterValidationResult ValidateConfigParameters(KeyedCollection<string, ConfigParameter> configParameters,
                                                                   ConfigParameterPage page)
        {


            ParameterValidationResult myResults = new ParameterValidationResult();

            return myResults;

        }

        public Schema GetSchema(KeyedCollection<string, ConfigParameter> configParameters)
        {
            #region Person
            Microsoft.MetadirectoryServices.SchemaType personType = Microsoft.MetadirectoryServices.SchemaType.Create("Person", false);

            var attr = new Person();
            personType.Attributes.Add(SchemaAttribute.CreateAnchorAttribute("ObjectID", AttributeType.String));
            // personType.Attributes.Add(SchemaAttribute.CreateMultiValuedAttribute("UserGroups", AttributeType.String));
            //  personType.Attributes.Add(SchemaAttribute.CreateSingleValuedAttribute("mail", AttributeType.String));
            foreach (var prop in attr.GetType().GetProperties())
            {
                if (prop.Name == "UserGroups")
                {
                    personType.Attributes.Add(SchemaAttribute.CreateMultiValuedAttribute(prop.Name, AttributeType.String));
                }
                else
                {

                    if (prop.PropertyType.FullName.Contains("System.Boolean"))
                    {
                        personType.Attributes.Add(SchemaAttribute.CreateSingleValuedAttribute(prop.Name, AttributeType.Boolean));
                    }
                    else if (prop.PropertyType.FullName.Contains("System.Int32"))
                    {
                        personType.Attributes.Add(SchemaAttribute.CreateSingleValuedAttribute(prop.Name, AttributeType.Integer));
                    }
                    else
                    {
                        personType.Attributes.Add(SchemaAttribute.CreateSingleValuedAttribute(prop.Name, AttributeType.String));
                    }
                }
            }

            #endregion

            #region Group
            Microsoft.MetadirectoryServices.SchemaType groupType = Microsoft.MetadirectoryServices.SchemaType.Create("Group", false);
            groupType.Attributes.Add(SchemaAttribute.CreateAnchorAttribute("ObjectID", AttributeType.String));
            groupType.Attributes.Add(SchemaAttribute.CreateSingleValuedAttribute("displayName", AttributeType.String));
            groupType.Attributes.Add(SchemaAttribute.CreateSingleValuedAttribute("description", AttributeType.String));
            groupType.Attributes.Add(SchemaAttribute.CreateMultiValuedAttribute("members", AttributeType.Reference));



            #endregion
            /*
            #region Assigment
            Microsoft.MetadirectoryServices.SchemaType assignmentType = Microsoft.MetadirectoryServices.SchemaType.Create("Assignment", false);

            assignmentType.Attributes.Add(SchemaAttribute.CreateAnchorAttribute("ObjectID", AttributeType.String));
            assignmentType.Attributes.Add(SchemaAttribute.CreateSingleValuedAttribute("DisplayName", AttributeType.String));

            //assignmentType.Attributes.Add(SchemaAttribute.CreateAnchorAttribute("DisplayName", AttributeType.String));
            // assignmentType.Attributes.Add(SchemaAttribute.CreateSingleValuedAttribute("ObjectID", AttributeType.String));

            assignmentType.Attributes.Add(SchemaAttribute.CreateSingleValuedAttribute("UserID", AttributeType.String));
            assignmentType.Attributes.Add(SchemaAttribute.CreateSingleValuedAttribute("GroupID", AttributeType.String));
            assignmentType.Attributes.Add(SchemaAttribute.CreateSingleValuedAttribute("userName", AttributeType.String));
            assignmentType.Attributes.Add(SchemaAttribute.CreateSingleValuedAttribute("groupActivation", AttributeType.String));
            //            assigmentType.Attributes.Add(SchemaAttribute.CreateMultiValuedAttribute("dependentRolesToRemove", AttributeType.String));


            #endregion

            */


            Schema schema = Schema.Create();
            schema.Types.Add(personType);
            schema.Types.Add(groupType);

            return schema;
        }



        public OpenImportConnectionResults OpenImportConnection(
                                       KeyedCollection<string, ConfigParameter> configParameters,
                                       Schema types,
                                       OpenImportConnectionRunStep importRunStep)
        {
            BTPFunctions.logFolder = configParameters["LogFolder"].Value;
            proxy = configParameters["Proxy server"].Value;
            Log("proxy: " + proxy);
            string authString = configParameters["Auth string"].Value;
            authString+= configParameters["Auth string2"].Value;


            importURL = configParameters["BTP API URL"].Value;
            Log("importURL: " + importURL);

            string tokenURL = configParameters["Token URL"].Value;
            Log("tokenURL: " + tokenURL);


            accessToken = BTPFunctions.GetToken(proxy, tokenURL, authString);
            Log("accessToken: " + accessToken.expires_in);
            Log("MAFolder: " + MAUtils.MAFolder);
            string[] items = MAUtils.MAFolder.Split('\\');
            MAName = items[items.Length - 1];
            ExporFilePath = configParameters["Zoznam clenstva"].Value;
            if (File.Exists(ExporFilePath))
            { 
                File.Delete(ExporFilePath);
            }

            Log("MAName: " + MAName);
            importedRoleMembers.Clear();

            string delayStr = configParameters.Contains("Export delay (ms)") ? configParameters["Export delay (ms)"].Value : "1000";
            _exportDelayMs = int.TryParse(delayStr, out int d) ? d : 1000;

            return new OpenImportConnectionResults();
        }
        public GetImportEntriesResults GetImportEntries(GetImportEntriesRunStep importRunStep)
        {
            GetImportEntriesResults importReturnInfo = new GetImportEntriesResults();

            if (!usersDone)
            {
                Persons persons = new Persons(MAName);
                persons.GetUsersByStartIndex(accessToken.access_token, TotalUsers + 1, importURL, proxy, ExporFilePath, importedRoleMembers);
                TotalUsers += persons.webUserResult.resources.Length;
                //Log("TotalUsers:" + TotalUsers.ToString() + ", resources: " + persons.webUserResult.resources.Length.ToString() + ", totalResults: " + persons.webUserResult.totalResults.ToString());


                if (TotalUsers < persons.webUserResult.totalResults)
                {
                    importReturnInfo.MoreToImport = true;
                   // Log("importReturnInfo: true");
                }
                else
                {
                    importReturnInfo.MoreToImport = true;
                    usersDone = true;
                }


                if (_exportDelayMs > 0)
                    System.Threading.Thread.Sleep(_exportDelayMs);

                importReturnInfo.CSEntries = persons.csentries;
                return importReturnInfo;
            }

            if (!groupsDone && usersDone)
            {
                Group roles = new Group(MAName, accessToken.access_token, importURL, proxy);
                roles.GetRolesByStartIndex(TotalGroups + 1, ExporFilePath, importedRoleMembers);
                TotalGroups += roles.webRoleResult.resources.Length;
               // Log("TotalGroups:" + TotalGroups.ToString() + ", resources: " + roles.webRoleResult.resources.Length.ToString() + ", totalResults: " + roles.webRoleResult.totalResults.ToString());

                if (TotalGroups < roles.webRoleResult.totalResults)
                {
                    importReturnInfo.MoreToImport = true;
                    //Log("importReturnInfo Group: true");
                }
                else
                {
                    importReturnInfo.MoreToImport = false;
                    groupsDone = true;
                }


                if (_exportDelayMs > 0)
                    System.Threading.Thread.Sleep(_exportDelayMs);

                importReturnInfo.CSEntries = roles.csentries;
                return importReturnInfo;

            }

            //try
            //{


            //}
            //catch (Exception e)
            //{
            //    Log("importReturnInfo error" + e.Message);
            //    Log("importReturnInfo error" + e.StackTrace);
            //}
            return importReturnInfo;

        }

        public CloseImportConnectionResults CloseImportConnection(CloseImportConnectionRunStep importRunStepInfo)
        {
            // conn.Close();
            return new CloseImportConnectionResults();
        }

        #region Export
        /// //////////////////////////////////////////////////////////////////////////////////////////////////////
        /// EXPORT
        /// //////////////////////////////////////////////////////////////////////////////////////////////////////
        public void OpenExportConnection(System.Collections.ObjectModel.KeyedCollection<string, ConfigParameter> configParameters, Schema types, OpenExportConnectionRunStep exportRunStep)
        {
            BTPFunctions.logFolder = configParameters["LogFolder"].Value;
            proxy = configParameters["Proxy server"].Value;
            Log("proxy: " + proxy);

            importURL = configParameters["BTP API URL"].Value;
            Log("importURL: " + importURL);
            string tokenURL = configParameters["Token URL"].Value;
            Log("tokenURL: " + tokenURL);

            string authString = configParameters["Auth string"].Value;
            authString += configParameters["Auth string2"].Value;

            Log("authString: " + authString);
            accessToken = BTPFunctions.GetToken(proxy, tokenURL, authString);
            Log("accessToken: " + accessToken.expires_in);
            Log("MAFolder: " + MAUtils.MAFolder);
            string[] items = MAUtils.MAFolder.Split('\\');
            MAName = items[items.Length - 1];

            string[] lines = File.ReadAllLines(Path.Combine(MAUtils.MAFolder, "dependentRoles.txt"));
            foreach (string line in lines)
            {
                if (string.IsNullOrWhiteSpace(line) || line.TrimStart().StartsWith("//")) continue;
                var dr = new DependentRoles(line);
                if (!string.IsNullOrEmpty(dr.roleName) && !string.IsNullOrEmpty(dr.baseRole))
                    dependentRoles.Add(dr);
            }

            Log("DependetRoles count: " + dependentRoles.Count);
            _exportUserCache.Clear();

            string delayStr = configParameters.Contains("Export delay (ms)") ? configParameters["Export delay (ms)"].Value : "1000";
            _exportDelayMs = int.TryParse(delayStr, out int d) ? d : 1000;
            Log($"Export delay: {_exportDelayMs} ms");
        }

        public PutExportEntriesResults PutExportEntries(IList<CSEntryChange> csentries)
        {
            PutExportEntriesResults exportEntriesResults = new PutExportEntriesResults();
            Log($"PutExportEntries - count: {csentries.Count}");
            foreach (CSEntryChange item in csentries)
            {
                Log($"Exporting {item.ObjectType}");

                try
                {
                    if (item.ObjectType == "Person")
                    {
                        Log("Processing person");
                        Person person = new Person(importURL, accessToken.access_token, proxy);

                        User user = null;
                        if (item.ObjectModificationType == ObjectModificationType.Add)
                        {
                            user = new User();
                        }
                        else
                        {
                            Log("ObjectModificationType: " + item.ObjectModificationType + ", anchor=" + item.AnchorAttributes[0].Value.ToString());
                            user = person.GetUser(item.AnchorAttributes[0].Value.ToString(), true);
                        }

                        if (user == null)
                        {
                            continue;
                        }
                        foreach (string attribName in item.ChangedAttributeNames)
                        {
                            var attribValue = item.AttributeChanges[attribName].ValueChanges[0].Value;
                            Log(attribName + "=" + attribValue);
                            switch (attribName)
                            {
                                case "upn":
                                    user.externalId = attribValue.ToString();
                                    user.userName = attribValue.ToString();
                                    break;
                                case "active":
                                    user.active = Convert.ToBoolean(attribValue);
                                    break;
                                case "firstName":
                                    user.name.givenName = attribValue.ToString();
                                    break;
                                case "lastName":
                                    user.name.familyName = attribValue.ToString();
                                    break;
                                case "mail":
                                    user.emails[0].value = attribValue.ToString();
                                    break;
                                default:
                                    break;
                            }
                        }
                        if (item.ObjectModificationType == ObjectModificationType.Add)
                        {
                            Log("Creating person");
                            RetObject ret = person.createUser(user);
                            Log("sending js: " + ret.js);
                            Log("received js: " + ret.retjs);
                            if (ret.isSuccess)
                            {
                                Log("Successfully created user");
                                var successChangeResult = CSEntryChangeResult.Create(item.Identifier, null, MAExportError.Success);
                                exportEntriesResults.CSEntryChangeResults.Add(successChangeResult);
                            }
                            else
                            {
                                Log("Error creating user");
                                Log(ret.errText);
                                //Log(ret.e.Message);

                                var errorDescription = $"Error while exporting object of CSEntry {item.Identifier}";
                                if (ret.e != null)
                                    errorDescription += "\r\n\r\n" + ret.e.Message + "\r\n\r\n" + ret.e.StackTrace;
                                var errorChangeResult = CSEntryChangeResult.Create(item.Identifier, item.AttributeChanges, MAExportError.ExportErrorCustomContinueRun, "btp-export-error", errorDescription);
                                exportEntriesResults.CSEntryChangeResults.Add(errorChangeResult);

                            }
                        }
                        else if (item.ObjectModificationType == ObjectModificationType.Delete)
                        {
                            Log("Deleting person");
                            RetObject ret = person.deleteUser(user);
                            if (ret.isSuccess)
                            {
                                Log("Successfully deleted user");
                                var successChangeResult = CSEntryChangeResult.Create(item.Identifier, null, MAExportError.Success);
                                exportEntriesResults.CSEntryChangeResults.Add(successChangeResult);
                            }
                            else
                            {
                                Log("Error deleting user");
                                Log(ret.errText);
                                //Log(ret.e.Message);

                                var errorDescription = $"Error while exporting object of CSEntry {item.Identifier}";
                                if (ret.e != null)
                                    errorDescription += "\r\n\r\n" + ret.e.Message + "\r\n\r\n" + ret.e.StackTrace;
                                var errorChangeResult = CSEntryChangeResult.Create(item.Identifier, item.AttributeChanges, MAExportError.ExportErrorCustomContinueRun, "btp-export-error", errorDescription);
                                exportEntriesResults.CSEntryChangeResults.Add(errorChangeResult);

                            }
                        }
                        else
                        {
                            Log("Updating person " + user.id);
                            RetObject ret = person.updateUser(user);
                            Log("sending js: " + ret.js);
                            Log("received js: " + ret.retjs);
                            if (ret.isSuccess)
                            {
                                Log("Successfully updated user");
                                var successChangeResult = CSEntryChangeResult.Create(item.Identifier, null, MAExportError.Success);
                                exportEntriesResults.CSEntryChangeResults.Add(successChangeResult);
                            }
                            else
                            {
                                Log("Error updating user");
                                Log(ret.errText);
                                //Log(ret.e.Message);

                                var errorDescription = $"Error while exporting object of CSEntry {item.Identifier}";
                                if (ret.e != null)
                                    errorDescription += "\r\n\r\n" + ret.e.Message + "\r\n\r\n" + ret.e.StackTrace;
                                var errorChangeResult = CSEntryChangeResult.Create(item.Identifier, item.AttributeChanges, MAExportError.ExportErrorCustomContinueRun, "btp-export-error", errorDescription);
                                exportEntriesResults.CSEntryChangeResults.Add(errorChangeResult);

                            }
                        }

                    }

                    if (item.ObjectType == "Group")
                    {
                        //string groupAnchor = item.Identifier.ToString();
                        string groupAnchor = item.DN.ToString();
                        Log("Processing Group");
                        
                        Log("ObjectModificationType: " + item.ObjectModificationType + ", anchor=" + groupAnchor);
                        Group group = new Group(MAName, accessToken.access_token, importURL, proxy, dependentRoles, groupAnchor);
                        //group.GetRole(item.Identifier.ToString());
                        if (group.role.id == null)
                            throw new Exception($"Role '{groupAnchor}' not found");
                        foreach (string attribName in item.ChangedAttributeNames)
                        {
                            if (attribName == "members")
                            {
                                Log("Processing attribute members");
                                AttributeChange membersChange = item.AttributeChanges[attribName];
                                string membersBefore = group.role.members == null ? "" : string.Join(", ", group.role.members.Select(m => m.value));
                                Log($"-------- Role {groupAnchor} – členství před úpravou: [{membersBefore}] --------");

                                // Replace/Delete = MIM posílá cílový stav členství, ne přírůstkové změny,
                                // takže členy, které v něm nejsou, musíme z role odebrat.
                                bool isFullReplace = item.ObjectModificationType == ObjectModificationType.Replace
                                                     || membersChange.ModificationType == AttributeModificationType.Replace
                                                     || membersChange.ModificationType == AttributeModificationType.Delete;
                                Log($"ModificationType: object={item.ObjectModificationType}, attribute={membersChange.ModificationType}, fullReplace={isFullReplace}");

                                var memberOperations = new List<KeyValuePair<string, ValueModificationType>>();
                                var targetMembers = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                                foreach (ValueChange val in membersChange.ValueChanges)
                                {
                                    string changedUid = (val.Value ?? "").ToString().Trim();
                                    if (changedUid.Length == 0)
                                        continue;

                                    if (val.ModificationType == ValueModificationType.Delete)
                                    {
                                        memberOperations.Add(new KeyValuePair<string, ValueModificationType>(changedUid, ValueModificationType.Delete));
                                    }
                                    else
                                    {
                                        targetMembers.Add(changedUid);
                                        memberOperations.Add(new KeyValuePair<string, ValueModificationType>(changedUid, ValueModificationType.Add));
                                    }
                                }

                                if (isFullReplace && group.role.members != null)
                                {
                                    var currentMembers = group.role.members
                                        .Select(m => (m.value ?? "").Trim())
                                        .Where(v => v.Length > 0)
                                        .Distinct(StringComparer.OrdinalIgnoreCase)
                                        .ToList();
                                    foreach (string currentUid in currentMembers)
                                    {
                                        if (!targetMembers.Contains(currentUid))
                                        {
                                            Log($"   {membersChange.ModificationType}: member {currentUid} není v cílovém seznamu z MIM – bude odebrán");
                                            memberOperations.Add(new KeyValuePair<string, ValueModificationType>(currentUid, ValueModificationType.Delete));
                                        }
                                    }
                                }

                                foreach (var memberOperation in memberOperations)
                                {
                                    string uid = memberOperation.Key;
                                    Log($">> ValueChange: ModificationType={memberOperation.Value}, UserID={uid}");
                                    if (!_exportUserCache.ContainsKey(uid))
                                    {
                                        Log($"   User {uid} not in cache – fetching from API...");
                                        group.GetUser(uid);
                                        if (group.user != null)
                                        {
                                            _exportUserCache[uid] = group.user;
                                            string userGroups = group.user.groups != null ? string.Join(", ", group.user.groups.Select(g => g.value)) : "(null)";
                                            Log($"   Fetched user {group.user.userName} / {uid} – groups: [{userGroups}]");
                                        }
                                    }
                                    else
                                    {
                                        Log($"   User {uid} found in cache");
                                    }
                                    if (_exportUserCache.TryGetValue(uid, out User cachedUser) && cachedUser != null)
                                    {
                                        group.user = cachedUser;
                                    }
                                    if (group.user == null || group.user.id == null)
                                    {
                                        Log($"WARNING: User '{uid}' not found (404) – skipping this user, continuing with remaining members.");
                                        continue;
                                    }

                                    if (memberOperation.Value == ValueModificationType.Add)
                                    {
                                        Log($"   ADD: Adding user {group.user.userName} / {uid} to primary role {group.role.id}");
                                        group.ADDUserToRole();
                                        if (group.ToUpdate)
                                            AddGroupToUserCache(group.user, group.role.id);
                                        
                                        foreach (var gr in group.primaryRoles)
                                        {
                                            Log($"   ADD: Adding user {group.user.userName} / {uid} to dependent/base role {gr.role?.id}");
                                            gr.user = group.user;
                                            gr.ADDUserToRole();
                                            if (gr.ToUpdate)
                                                AddGroupToUserCache(gr.user, gr.role.id);
                                        }
                                        //UsersToAdd.Add(val.Value.ToString());
                                    }
                                    if (memberOperation.Value == ValueModificationType.Delete)
                                    {
                                        Log($"   DELETE: Removing user {group.user.userName} / {uid} from primary role {group.role.id}");
                                        group.RemoveUserFromRole(false, null, null);
                                        if (group.ToUpdate)
                                            RemoveGroupFromUserCache(group.user, group.role.id);

                                        foreach (var gr in group.primaryRoles)
                                        {
                                            Log($"   DELETE: Removing user {group.user.userName} / {uid} from dependent/base role {gr.role?.id}");
                                            gr.user = group.user;
                                            gr.RemoveUserFromRole(true, dependentRoles,group.role.id);
                                            if (gr.ToUpdate)
                                                RemoveGroupFromUserCache(gr.user, gr.role.id);
                                        }
                                    }
                                }
                                //string membersAfter = group.role.members == null ? "" : string.Join(", ", group.role.members.Select(m => m.value));
                                //Log($"-------- Role {groupAnchor} – členství po úpravě: [{membersAfter}] --------");
                            }

                        }


                        string membersAfter = group.role.members == null ? "" : string.Join(", ", group.role.members.Select(m => m.value));
                        Log($"-------- Role {groupAnchor} – členství PO úpravě (před odesláním): [{membersAfter}] --------");
                        Log($"Updating primary role {group.displayName}");
                        string js = JsonConvert.SerializeObject(group.role);
                        RetObject ret =  group.updateRole(js);
                        Log("Ret:" + ret.isSuccess);

                        foreach (var gr in group.primaryRoles)
                        {
                            string membersBeforeBase = gr.role.members == null ? "" : string.Join(", ", gr.role.members.Select(m => m.value));
                            Log($"-------- Role {gr.role?.id} – členství BASE role před odesláním: [{membersBeforeBase}] --------");
                            Log($"   BASE role ToUpdate={gr.ToUpdate}");
                            if (gr.ToUpdate)
                            {
                                Log($"Updating base role {gr.role?.id}");
                                string js2 = JsonConvert.SerializeObject(gr.role);
                                RetObject ret2 = gr.updateRole(js2);
                                Log("Ret:" + ret2.isSuccess);

                                if (!ret2.isSuccess)
                                {
                                    ret.isSuccess = false;
                                    ret.errText = ret.errText + "\n" + ret2.errText;
                                }
                            }
                            else
                            {
                                Log($"   BASE role {gr.role?.id} – no changes needed (ToUpdate=false), skipping update.");
                            }

                        }


                        if (!ret.isSuccess)
                        {

                            var errorDescription = $"Error while exporting object of CSEntry {item.Identifier}\n\n{ret.errText}";
                            Log(errorDescription);
                            var errorChangeResult = CSEntryChangeResult.Create(item.Identifier, item.AttributeChanges, MAExportError.ExportErrorCustomStopRun, "btp-export-error", errorDescription);
                            exportEntriesResults.CSEntryChangeResults.Add(errorChangeResult);
                        }
                        else
                        {
                            Log("Successfully updated Group");
                            var successChangeResult = CSEntryChangeResult.Create(item.Identifier, null, MAExportError.Success);
                            exportEntriesResults.CSEntryChangeResults.Add(successChangeResult);

                        }

                        //var errorChangeResult2 = CSEntryChangeResult.Create(item.Identifier, item.AttributeChanges, MAExportError.ExportErrorCustomStopRun, "btp-export-error", "TestError");
                        //exportEntriesResults.CSEntryChangeResults.Add(errorChangeResult2);
                    }

                    if (item.ObjectType == "Assignment")
                    {
                        //modify,groupActivation,string,1;WIPE_N_SE_AKOBOJE_ADMIN_PA02;27a5be52-8216-406e-9390-7845cf589f2b,0;WIPE_N_SE_AKOBOJE_ADMIN_PA02;27a5be52-8216-406e-9390-7845cf589f2b
                        Log("Processing Assignment");
                        Log("ObjectModificationType: " + item.ObjectModificationType + ", anchor=" + item.AnchorAttributes[0].Value.ToString());
                        foreach (string attribName in item.ChangedAttributeNames)
                        {
                            var attribValue = item.AttributeChanges[attribName].ValueChanges[0].Value;
                            Log(attribName + "=" + attribValue);

                            if (attribName == "groupActivation")
                            {
                                Assigment assigment = new Assigment(importURL, accessToken.access_token, attribValue.ToString(), dependentRoles, proxy);
                                assigment.GetRole();
                                if (assigment.role.id == null)
                                    throw new Exception($"Role '{assigment.groupID}' not found");
                                string js = Newtonsoft.Json.JsonConvert.SerializeObject(assigment.role);
                                BTPFunctions.Log("Found role:\n" + js);

                                assigment.GetUser(item.ObjectModificationType != ObjectModificationType.Add);

                                if (assigment.user.id == null)
                                    throw new Exception($"User '{assigment.userID}' not found");

                                RetObject ret = assigment.UpdateMembers();
                                if (!ret.isSuccess)
                                {

                                    var errorDescription = $"Error while exporting object of CSEntry {item.Identifier}";
                                    Log(errorDescription);
                                    var errorChangeResult = CSEntryChangeResult.Create(item.Identifier, item.AttributeChanges, MAExportError.ExportErrorCustomStopRun, "btp-export-error", errorDescription);
                                    exportEntriesResults.CSEntryChangeResults.Add(errorChangeResult);
                                }
                                else
                                {
                                    Log("Successfully updated Group");
                                    var successChangeResult = CSEntryChangeResult.Create(item.Identifier, null, MAExportError.Success);
                                    exportEntriesResults.CSEntryChangeResults.Add(successChangeResult);

                                }

                            }

                        }

                    }
                }
                catch (Exception e)
                {
                    var errorDescription = $"Error while exporting object of CSEntry {item.Identifier}";
                    errorDescription += "\r\n\r\n" + e.Message + "\r\n\r\n" + e.StackTrace;

                    var errorChangeResult = CSEntryChangeResult.Create(item.Identifier, item.AttributeChanges, MAExportError.ExportErrorCustomStopRun, "btp-export-error", errorDescription);
                    exportEntriesResults.CSEntryChangeResults.Add(errorChangeResult);
                }

                // Throttling – pauza mezi objekty aby nedoslo k pretizeni SAP BTP API (429)
                if (_exportDelayMs > 0)
                    System.Threading.Thread.Sleep(_exportDelayMs);
            }
            return exportEntriesResults;

        }
        public void CloseExportConnection(CloseExportConnectionRunStep exportRunStep)
        {

        }

        public int ExportDefaultPageSize
        {
            get => this.m_exportDefaultPageSize;
            set => this.m_exportDefaultPageSize = value;
        }

        public int ExportMaxPageSize
        {
            get => this.m_exportMaxPageSize;
            set => this.m_exportMaxPageSize = value;
        }

        #endregion
        public int ImportMaxPageSize
        {
            get
            {
                return 100; // m_importMaxPageSize;
            }
        }

        public int ImportDefaultPageSize
        {
            get
            {
                return 50; // m_importDefaultPageSize;
            }
        }
        //public void Log(String msg)
        //{
        //    if (!string.IsNullOrEmpty(logFolder))
        //    {
        //        string logfile = Path.Combine(logFolder, DateTime.Now.ToString("yyyyMMdd") + "_MAlog.txt");
        //        File.AppendAllText(logfile, msg + Environment.NewLine);
        //    }
        //}

        public void Log(String msg)
        {
            BTPFunctions.Log(msg);
        }
    };

}
