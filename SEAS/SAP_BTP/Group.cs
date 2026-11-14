using Microsoft.MetadirectoryServices;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Reflection;
using System.Runtime.Remoting.Messaging;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;

namespace FimSync_Ezma
{
    public class Group
    {
        public string id { get; set; }
        public string displayName { get; set; }
        public string zoneId { get; set; }
        public string description { get; set; }
        //        public member[] members { get; set; }
        public List<member> members { get; set; }
        public string[] schemas { get; set; }
        public umeta meta { get; set; }

        public User user;

        private string accessToken;
        private string APIURL;
        private string proxy;
        public Role role;

        public List<Group> primaryRoles = new List<Group>();

        public bool ToUpdate=false;
        // List<DependentRoles> primaryRolesToCheck = new List<DependentRoles>();


        public List<CSEntryChange> csentries = new List<CSEntryChange>();
        //public List<Person> Users = new List<Person>();
        public WebRoleResult webRoleResult;
        private string MAName;
        public Group(string maname, string _accesstoken, string _apiurl, string _proxy, string groupAnchor = null)
        {
            this.accessToken = _accesstoken;
            this.APIURL = _apiurl;
            this.proxy = _proxy;
            MAName = maname;

            if (groupAnchor != null)
            {
                role = GetRole(groupAnchor);
            }

        }


        public Group(string maname, string _accesstoken, string _apiurl, string _proxy, List<DependentRoles> dependentRoles, string groupAnchor)
        {
            this.accessToken = _accesstoken;
            this.APIURL = _apiurl;
            this.proxy = _proxy;
            MAName = maname;

            if (groupAnchor != null)
            {
                role = GetRole(groupAnchor);
                BTPFunctions.Log("Group constructor: found role: " + role.id);
                List<DependentRoles> primaryRolesToCheck = dependentRoles.Where(x => string.Equals((x.roleName ?? "").Trim(), (role.id ?? "").Trim(), StringComparison.OrdinalIgnoreCase)).ToList<DependentRoles>();
                BTPFunctions.Log("Found " + primaryRolesToCheck.Count + " dependet roles");
                foreach (DependentRoles dr in primaryRolesToCheck)
                {
                    BTPFunctions.Log($"  Loading base role: {dr.baseRole} (for primary role: {dr.roleName})");
                    Group depGroup = new Group(maname, _accesstoken, _apiurl, _proxy, groupAnchor);

                    depGroup.role = this.GetRole(dr.baseRole);
                    if (depGroup.role != null)
                    {
                        string baseMembers = depGroup.role.members == null ? "(empty)" : string.Join(", ", depGroup.role.members.Select(m => m.value));
                        BTPFunctions.Log($"  Base role {dr.baseRole} loaded, members: [{baseMembers}]");
                        primaryRoles.Add(depGroup);
                    }
                    else
                    {
                        BTPFunctions.Log($"  WARNING: Base role {dr.baseRole} not found!");
                    }
                }
            }
        }

        public void GetRolesByStartIndex(int index, string ExporFilePath, Dictionary<string, HashSet<string>> roleMembersFromUsers = null)
        {
            string sURL = APIURL + "Groups?startIndex=" + index.ToString();
            string strResult = BTPFunctions.HttpGet(sURL, accessToken, proxy);

            webRoleResult = BTPFunctions.Deserialize<WebRoleResult>(strResult);

            int counter = 0;
            foreach (Role role in webRoleResult.resources)
            {
                //BTPFunctions.Log("Role:" + role.displayName);
                if (!role.id.EndsWith("_BASE"))
                {
                    CSEntryChange csentry = CSEntryChange.Create();
                    csentry.ObjectModificationType = ObjectModificationType.Add;
                    csentry.ObjectType = "Group";
                    csentry.AttributeChanges.Add(AttributeChange.CreateAttributeAdd("ObjectID", role.id));
                    if (!string.IsNullOrEmpty(role.displayName))
                    {
                        csentry.AttributeChanges.Add(AttributeChange.CreateAttributeAdd("displayName", role.displayName));
                    }
                    if (!string.IsNullOrEmpty(role.description))
                    {
                        csentry.AttributeChanges.Add(AttributeChange.CreateAttributeAdd("description", role.description));
                    }
                    // Seznam Groups v BTP nevrací members – složíme je z user.groups (už staženo při importu uživatelů)
                    List<string> groups = new List<string>();
                    if (role.members != null && role.members.Length > 0)
                    {
                        foreach (var member in role.members)
                        {
                            if (!string.IsNullOrEmpty(member?.value))
                                groups.Add(member.value);
                        }
                    }
                    else if (roleMembersFromUsers != null && roleMembersFromUsers.TryGetValue(role.id, out HashSet<string> inverted))
                    {
                        groups.AddRange(inverted);
                    }

                    if (groups.Count > 0)
                    {
                        csentry.AttributeChanges.Add(AttributeChange.CreateAttributeAdd("members", groups.ToArray()));
                    }
                    BTPFunctions.Log($"Import role {role.id}: {groups.Count} members");

                    counter++;
                    csentries.Add(csentry);
                }
                //Log("counter: " + counter);

            }

        }

        public void GetUser(string userID)
        {
            user = null;
            Person person = new Person(APIURL, accessToken, proxy);
            user = person.GetUser(userID, true);
        }
        public Role GetRole(string id, bool logResponse = true)
        {
            //bool isValid = Guid.TryParse(id, out _);

            string sURL = $"{APIURL}Groups/{id}";
            if (logResponse)
                BTPFunctions.Log("URL: " + sURL);
            try
            {
                string strResult = BTPFunctions.HttpGet(sURL, accessToken, proxy);
                if (logResponse)
                    BTPFunctions.Log("strResult:\n" + strResult);

                //webRoleResult = BTPFunctions.Deserialize<Role>(strResult);
                //if (webRoleResult != null && webRoleResult.resources.Length > 0)
                //{
                return BTPFunctions.Deserialize<Role>(strResult);

                //}
                //else
                //{
                //    return null;
                //}
                //Group retgroup = new Group(webRoleResult);

                // return BTPFunctions.Deserialize<Group>(strResult);
            }
            catch (Exception e)
            {
                BTPFunctions.Log("Chyba GetRole:" + e.Message);
                BTPFunctions.Log(e.StackTrace);
                return null;
            }
        }

        public RetObject ADDUserToRole()
        {
            RetObject r = new RetObject();
            BTPFunctions.Log($"Adding user {user.userName} / {user.id} to role {role.id}");
            member membership = role.members.Where(x => x.value == user.id).FirstOrDefault();
            if (membership == null)
            {
                ToUpdate = true;
                BTPFunctions.Log("User is not member - adding...");
                try
                {
                    List<member> lmembers = new List<member>();
                    foreach (member member in role.members)
                    {
                        lmembers.Add(member);
                    }
                    member m = new member();
                    m.origin = "sap.custom";
                    m.otype = "USER";
                    m.value = user.id;
                    lmembers.Add(m);
                    role.members = lmembers.ToArray();
                    //string js = JsonConvert.SerializeObject(role);
                    string membersBefore = role.members == null ? "" : string.Join(", ", role.members.Select(q => q.value));
                    BTPFunctions.Log("ADDUserToRole result:" + membersBefore);
                    BTPFunctions.Log($"----------- Added user -------------\n {user.id} ");
                    //return updateRole(js);
                    r.isSuccess = true;
                    return r;
                }
                catch (Exception e)
                {

                    r.e = e;
                    r.errText = $"Error adding {user.userName} / {user.id} to role {role.id}\n{e.Message})";
                    r.isSuccess = false;
                    BTPFunctions.Log(e.Message);
                    BTPFunctions.Log(e.StackTrace);
                    return r;
                }
            }
            else
            {
                r.isSuccess = true;
                BTPFunctions.Log("User is already member of this group");
                return r;
            }

        }

        public RetObject updateRole(string js)
        {
            BTPFunctions.Log("---------- updateRole start -------------");
            //string membersLog = role?.members == null ? "" : string.Join(", ", role.members.Select(m => m?.value ?? ""));
            //BTPFunctions.Log($"Role {role?.id} – členství při odeslání na API: [{membersLog}]");
            RetObject ret = new RetObject();
            try
            {
                if (role != null && role.id != null)
                {
                    string sURL = APIURL + $"Groups/{role.id}";
                    var request = System.Net.HttpWebRequest.CreateHttp(sURL);

                    if (proxy != null)
                        request.Proxy = new WebProxy(proxy, true);

                    BTPFunctions.Log("url: " + sURL);

                    request.Method = "Put";
                    request.ContentType = "application/json";
                    request.Accept = "application/json";
                    request.Headers.Add("Authorization", "Bearer " + accessToken);
                    request.Headers.Add(HttpRequestHeader.IfMatch, role.metadata != null ? role.metadata.version.ToString() : "*");
                    BTPFunctions.Log("updateRole js: " + js);
                    byte[] bodyBytes = Encoding.UTF8.GetBytes(js);
                    var stream = request.GetRequestStream();
                    stream.Write(bodyBytes, 0, bodyBytes.Length);
                    stream.Flush();
                    stream.Close();

                    try
                    {
                        var webresponse = request.GetResponse();
                        Encoding enc = System.Text.Encoding.GetEncoding("utf-8");
                        // read response stream from response object
                        StreamReader loResponseStream = new StreamReader(webresponse.GetResponseStream(), enc);
                        // read string from stream data
                        string strResult = loResponseStream.ReadToEnd();
                        BTPFunctions.Log("--------- updateRole Result---------\n" + strResult);
                        // close the stream object
                        loResponseStream.Close();
                        webresponse.Close();
                        ret.isSuccess = true;
                    }
                    catch (WebException wex)
                    {
                        BTPFunctions.Log(wex.Message);
                        BTPFunctions.Log(wex.StackTrace);
                        ret.e = wex;
                        var httpResponse = wex.Response as HttpWebResponse;
                        if (httpResponse != null)
                        {
                            ret.errText = (string.Format(
                                "Remote server call {0} {1} resulted in a http error {2} {3}.",
                                "Put",
                                sURL,
                                httpResponse.StatusCode,
                                httpResponse.StatusDescription));
                            BTPFunctions.Log(ret.errText);
                            // close the stream object
                        }
                        else
                        {
                            ret.errText = (string.Format(
                                "Remote server call {0} {1} resulted in an error.",
                                "Put",
                                sURL));
                            BTPFunctions.Log(ret.errText);
                        }
                    }
                    catch (Exception e)
                    {
                        ret.errText = (string.Format(
                               "Remote server call {0} {1} resulted in an error.",
                               "Put",
                               sURL));
                        ret.e = e;
                        BTPFunctions.Log(ret.errText);
                        BTPFunctions.Log(e.Message);
                        BTPFunctions.Log(e.StackTrace);
                    }

                    return ret;

                    //res.totalResults
                }
                else
                {
                    BTPFunctions.Log("Group Id not specified");
                    ret.errText = "Group Id not specified";

                }
            }
            catch (Exception e1)
            {
                ret.errText = "Unkown error updating role";
                ret.e = e1;
                BTPFunctions.Log(ret.errText);
                BTPFunctions.Log(e1.Message);
                BTPFunctions.Log(e1.StackTrace);
            }
            return ret;
        }

        public RetObject RemoveUserFromRole(bool isBaseRole, List<DependentRoles> dependentRoles, string primaryrole)
        {
            RetObject r = new RetObject();
            r.isSuccess = false;

            try
            {
                BTPFunctions.Log($"Removing user {user.userName} / {user.id} from role {role.id}");
                member membership = role.members.Where(x => x.value == user.id).FirstOrDefault();
                if (membership == null)
                {
                    BTPFunctions.Log("User is not member ...");
                    r.isSuccess = true;
                }
                else
                {
                    bool toRemove = true;
                    if (!isBaseRole)
                    {
                        BTPFunctions.Log("User is member of group - removing from role...");
                    }
                    else
                    {
                        BTPFunctions.Log("User is member of group - checking  dependent role...");
                        // Pokud API nevrátilo skupiny uživatele, neodebíráme z dependent role – nelze ověřit, že uživatel nemá jinou primary roli
                        if (user.groups == null || user.groups.Length == 0)
                        {
                            toRemove = false;
                            BTPFunctions.Log($"Dependent role {role.id} will not be removed - user.groups is null or empty, cannot verify other primary roles.");
                        }
                        else
                        {
                            string allUserGroups = string.Join(", ", user.groups.Select(g => g.value));
                            BTPFunctions.Log($"User groups for dependency check: [{allUserGroups}]");
                            var primaryroles = dependentRoles.Where(x => string.Equals((x.baseRole ?? "").Trim(), (role.id ?? "").Trim(), StringComparison.OrdinalIgnoreCase)
                                && !string.Equals((x.roleName ?? "").Trim(), (primaryrole ?? "").Trim(), StringComparison.OrdinalIgnoreCase)).ToList();
                            BTPFunctions.Log($"Other primary roles sharing base {role.id} (excluding {primaryrole}): [{string.Join(", ", primaryroles.Select(p => p.roleName))}]");
                            foreach (userGroup ug in user.groups)
                            {
                                string ugValue = (ug?.value ?? "").Trim();
                                var existingRole = primaryroles.FirstOrDefault(x => string.Equals((x.roleName ?? "").Trim(), ugValue, StringComparison.OrdinalIgnoreCase));
                                if (existingRole != null)
                                {
                                    toRemove = false;
                                    BTPFunctions.Log($"Dependent role {role.id} will not be removed. This group is dependant for another primary group {existingRole.roleName}");
                                }
                            }
                        }
                    }
                    BTPFunctions.Log($"ToRemove={toRemove.ToString()}");

                    if (toRemove)
                    {
                        this.ToUpdate = true;
                        BTPFunctions.Log($"Removing user from role {role.id} ...");
                        List<member> members = new List<member>();
                        members.AddRange(role.members);
                        BTPFunctions.Log($"Members = {role.members.Length}");

                        BTPFunctions.Log($"Members2 = {members.Count}");
                        foreach (member m in role.members)
                        {
                            BTPFunctions.Log($" - {m.value} == {user.id}");
                            if (m.value == user.id)
                            {
                                BTPFunctions.Log($"Removing {m.value}");
                                members.Remove(m);
                            }
                        }

                        role.members = members.ToArray();
                        BTPFunctions.Log($"Members3 = {role.members.Length}");

                    }

                }
            }
            catch (Exception e)
            {
                BTPFunctions.Log($"Error removing role {role.id} ..." + e.Message);
                r.isSuccess = false;
                r.e = e;
                r.errText = e.Message;
                return r;
            }

            r.isSuccess = true;
            return r;
        }
    }

    /// <summary>
    /// Záznam ze souboru dependentRoles.txt (roleName;baseRole).
    /// roleName a baseRole jsou přesné identifikátory rolí v BTP – znak * je součást názvu, není wildcard/pattern.
    /// </summary>
    public class DependentRoles
    {
        public string roleName;
        public string baseRole;

        public DependentRoles(string line)
        {
            string[] items = line.Split(';');
            if (items.Length >= 2)
            {
                roleName = items[0].Trim();
                baseRole = items[1].Trim();
            }
        }

    }

    public class Assigment
    {
        //public string id { get; set; }
        public string userID { get; set; }
        public string upn { get; set; }
        public string groupID { get; set; }
        public string userName { get; set; }
        public string displayName { get; set; }
        public string groupActivation { get; set; }

        public Group role;
        public Group dependetRole;
        public User user;

        private string APIURL;
        private string proxy;
        private string accessToken;

        public string assigmentAction;
        private List<DependentRoles> dependentRoles = new List<DependentRoles>();

        public Assigment(string _uid, string _upn, string _gid)
        {
            userID = _uid;
            upn = _upn;
            userName = _upn.Split('@')[0];
            groupID = _gid;
            displayName = _gid + "_" + userName;
            groupActivation = $"1;{_gid};{userID}";
        }
        public Assigment(string _apiURL, string _accessToken, string _MIMgroupActivation, List<DependentRoles> _dependentRoles, string _proxy = null)
        {
            APIURL = _apiURL;
            this.proxy = _proxy;
            this.accessToken = _accessToken;
            groupActivation = _MIMgroupActivation;
            dependentRoles = _dependentRoles;

            var assignmentItems = groupActivation.Split(';');
            if (assignmentItems.Length == 3)
            {
                assigmentAction = assignmentItems[0];
                groupID = assignmentItems[1];
                userID = assignmentItems[2];
            }
        }

        public void GetUser(bool searchByID)
        {
            Person person = new Person(APIURL, accessToken, proxy);
            user = person.GetUser(userID, searchByID);
        }
        public void GetRole()
        {
            GetRole(groupID);
        }
        public void GetRole(string id)
        {
            //bool isValid = Guid.TryParse(id, out _);

            string sURL = $"{APIURL}Groups/{id}";
            BTPFunctions.Log("URL: " + sURL);
            try
            {
                WebRequest wrGETURL;
                wrGETURL = WebRequest.Create(sURL);
                if (proxy != null)
                {
                    wrGETURL.Proxy = new WebProxy(proxy, true);
                }
                wrGETURL.Method = "GET";
                wrGETURL.ContentType = @"application/json";
                wrGETURL.Headers.Add("Authorization", "Bearer " + accessToken);

                HttpWebResponse webresponse = wrGETURL.GetResponse() as HttpWebResponse;

                Encoding enc = System.Text.Encoding.GetEncoding("UTF-8");
                // read response stream from response object
                StreamReader loResponseStream = new StreamReader(webresponse.GetResponseStream(), enc);
                // read string from stream data
                string strResult = loResponseStream.ReadToEnd();
                //strResult = ConvertToUTF8(strResult);
                BTPFunctions.Log("strResult:\n" + strResult);

                loResponseStream.Close();
                webresponse.Close();

                role = BTPFunctions.Deserialize<Group>(strResult);
            }
            catch (Exception e)
            {
                BTPFunctions.Log("Chyba GetRole:" + e.Message);
                BTPFunctions.Log(e.StackTrace);
            }
        }

        public RetObject ADDUserToRole()
        {
            BTPFunctions.Log($"Adding user {user.userName} / {user.id} to role {role.id}");
            member membership = role.members.Where(x => x.value == user.id).FirstOrDefault();
            if (membership == null)
            {
                BTPFunctions.Log("User is not member - adding...");
                try
                {
                    member m = new member();
                    m.origin = "sap.custom";
                    m.otype = "USER";
                    m.value = user.id;
                    role.members.Add(m);
                    string js = JsonConvert.SerializeObject(role);
                    BTPFunctions.Log($"----------- Added user -------------\n {js}");
                    return updateRole(js);

                }
                catch (Exception e)
                {
                    RetObject r = new RetObject();
                    r.e = e;
                    r.errText = $"Error adding {user.userName} / {user.id} to role {role.id}\n{e.Message})";
                    r.isSuccess = false;
                    BTPFunctions.Log(e.Message);
                    BTPFunctions.Log(e.StackTrace);
                    return r;
                }
            }
            else
            {
                RetObject r = new RetObject();
                r.isSuccess = true;
                BTPFunctions.Log("User is already member of this group");
                return r;
            }

        }

        public RetObject updateRole(string js)
        {
            BTPFunctions.Log("---------- updateRole start -------------");
            string membersLog = role?.members == null ? "" : string.Join(", ", role.members.Select(m => m?.value ?? ""));
            BTPFunctions.Log($"Role {role?.id} – členství při odeslání na API: [{membersLog}]");
            RetObject ret = new RetObject();
            try
            {
                if (user.id != null)
                {
                    string sURL = APIURL + $"Groups/{role.id}";
                    var request = System.Net.HttpWebRequest.CreateHttp(sURL);

                    if (proxy != null)
                        request.Proxy = new WebProxy(proxy, true);

                    BTPFunctions.Log("url: " + sURL);

                    request.Method = "Put";
                    request.ContentType = "application/json";
                    request.Accept = "application/json";
                    request.Headers.Add("Authorization", "Bearer " + accessToken);
                    request.Headers.Add(HttpRequestHeader.IfMatch, user.meta.version.ToString());
                    BTPFunctions.Log("js: " + js);
                    byte[] bodyBytes = Encoding.UTF8.GetBytes(js);
                    var stream = request.GetRequestStream();
                    stream.Write(bodyBytes, 0, bodyBytes.Length);
                    stream.Flush();
                    stream.Close();

                    try
                    {
                        var webresponse = request.GetResponse();
                        Encoding enc = System.Text.Encoding.GetEncoding("utf-8");
                        // read response stream from response object
                        StreamReader loResponseStream = new StreamReader(webresponse.GetResponseStream(), enc);
                        // read string from stream data
                        string strResult = loResponseStream.ReadToEnd();
                        BTPFunctions.Log("--------- updateRole Result---------\n" + strResult);
                        BTPFunctions.Log("--------- updateRole Result End---------");
                        // close the stream object
                        loResponseStream.Close();
                        webresponse.Close();
                        ret.isSuccess = true;
                    }
                    catch (WebException wex)
                    {
                        BTPFunctions.Log(wex.Message);
                        BTPFunctions.Log(wex.StackTrace);
                        ret.e = wex;
                        var httpResponse = wex.Response as HttpWebResponse;
                        if (httpResponse != null)
                        {
                            ret.errText = (string.Format(
                                "Remote server call {0} {1} resulted in a http error {2} {3}.",
                                "Put",
                                sURL,
                                httpResponse.StatusCode,
                                httpResponse.StatusDescription));
                            BTPFunctions.Log(ret.errText);
                            // close the stream object
                        }
                        else
                        {
                            ret.errText = (string.Format(
                                "Remote server call {0} {1} resulted in an error.",
                                "Put",
                                sURL));
                            BTPFunctions.Log(ret.errText);
                        }
                    }
                    catch (Exception e)
                    {
                        ret.errText = (string.Format(
                               "Remote server call {0} {1} resulted in an error.",
                               "Put",
                               sURL));
                        ret.e = e;
                        BTPFunctions.Log(ret.errText);
                        BTPFunctions.Log(e.Message);
                        BTPFunctions.Log(e.StackTrace);
                    }

                    return ret;

                    //res.totalResults
                }
                else
                {
                    BTPFunctions.Log("Group Id not specified");
                    ret.errText = "Group Id not specified";

                }
            }
            catch (Exception e1)
            {
                ret.errText = "Unkown error updating role";
                ret.e = e1;
                BTPFunctions.Log(ret.errText);
                BTPFunctions.Log(e1.Message);
                BTPFunctions.Log(e1.StackTrace);
            }
            return ret;
        }

        public RetObject RemoveUserFromRole(bool isBaseRole)
        {
            RetObject r = new RetObject();
            r.isSuccess = false;
            BTPFunctions.Log($"Removing user {user.userName} / {user.id} from role {role.id}");
            member membership = role.members.Where(x => x.value == user.id).FirstOrDefault();
            if (membership == null)
            {
                BTPFunctions.Log("User is not member ...");
                r.isSuccess = true;
            }
            else
            {
                bool toRemove = true;
                if (!isBaseRole)
                {
                    BTPFunctions.Log("User is member of group - removing from role...");
                }
                else
                {
                    List<DependentRoles> primaryRolesToCheck = dependentRoles.Where(x => string.Equals((x.baseRole ?? "").Trim(), (role.id ?? "").Trim(), StringComparison.OrdinalIgnoreCase)).ToList<DependentRoles>();
                    BTPFunctions.Log("User is member of group - removing dependent role...");
                    //BTPFunctions.Log($"Found {userDependetRole.Count} for group {groupID}");
                    foreach (userGroup ug in user.groups)
                    {
                        string ugValue = (ug?.value ?? "").Trim();
                        var existingRole = primaryRolesToCheck.FirstOrDefault(x => string.Equals((x.roleName ?? "").Trim(), ugValue, StringComparison.OrdinalIgnoreCase));
                        if (existingRole != null && !string.Equals((existingRole.roleName ?? "").Trim(), (groupID ?? "").Trim(), StringComparison.OrdinalIgnoreCase))
                        {
                            toRemove = false;
                            BTPFunctions.Log($"Dependent role {role.id} will not be removed - there is another valid assigment: {ug.value}");
                        }
                    }

                }
                if (toRemove)
                {
                    BTPFunctions.Log($"Removing role {role.id} ...");
                    List<member> members = new List<member>();
                    members.AddRange(role.members);
                    foreach (member m in members)
                    {
                        if (m.value == user.id)
                        {
                            role.members.Remove(m);
                        }
                    }
                    string js = JsonConvert.SerializeObject(role);
                    BTPFunctions.Log($"----------- Removed user -------------\n {js}");
                    r = updateRole(js);

                }

            }
            return r;
        }

        public RetObject UpdateMembers()
        {
            RetObject ret = new RetObject();
            ret.isSuccess = false;
            List<DependentRoles> userDependetRole = dependentRoles.Where(x => string.Equals((x.roleName ?? "").Trim(), (this.groupID ?? "").Trim(), StringComparison.OrdinalIgnoreCase)).ToList<DependentRoles>();

            if (assigmentAction == "1")
            {
                ret = ADDUserToRole();
                if (!ret.isSuccess)
                    return ret;
                foreach (DependentRoles dr in userDependetRole)
                {
                    this.GetRole(dr.baseRole);
                    ret = ADDUserToRole();
                    if (!ret.isSuccess)
                        return ret;
                }
                ret.isSuccess = true;
                return ret;
            }
            if (assigmentAction == "0")
            {
                ret = RemoveUserFromRole(false);
                if (!ret.isSuccess)
                    return ret;
                foreach (DependentRoles dr in userDependetRole)
                {
                    this.GetRole(dr.baseRole);
                    ret = RemoveUserFromRole(true);
                    if (!ret.isSuccess)
                        return ret;
                }
                ret.isSuccess = true;
                return ret;
            }

            ret.errText = "Unknown error";
            return ret;

            //userDependetRole.roleName


        }


    }
}
