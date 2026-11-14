using Microsoft.MetadirectoryServices;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Text;
using System.Threading.Tasks;

namespace FimSync_Ezma
{
    public class RetObject
    {
        public string errText;
        public bool isSuccess;
        public string js;
        public string retjs;
        public Exception e;
        public RetObject()
        {
            isSuccess = false;
        }
    }
    public class Person
    {
        public string userName { get; set; }
        public string upn { get; set; }
        //public string id { get; set; }
        public bool active { get; set; }
        public string firstName { get; set; }
        public string lastName { get; set; }
        public string mail { get; set; }
        public string MA { get; set; }
        public string[] UserGroups { get; set; }

        private string _APIURL;
        private string _proxy;
        private string _accessToken;

        public Person()
        {
        }

        public Person(string apiURL, string accessToken, string proxy = null)
        {
            _APIURL = apiURL;
            _proxy = proxy;
            _accessToken = accessToken;
        }

        public RetObject updateUser(User user)
        {
            BTPFunctions.Log("---------- updateUser start -------------");
            RetObject ret = new RetObject();
            ret.isSuccess = false;
            try
            {
                if (user.id != null)
                {
                    string sURL = _APIURL + $"Users/{user.id}";
                    var request = System.Net.HttpWebRequest.CreateHttp(sURL);

                    if (_proxy != null)
                        request.Proxy = new WebProxy(_proxy, true);

                    BTPFunctions.Log("url: " + sURL);

                    request.Method = "Put";
                    request.ContentType = "application/json";
                    request.Accept = "application/json";
                    request.Headers.Add("Authorization", "Bearer " + _accessToken);
                    //request.Headers.Add(HttpRequestHeader.IfMatch, user.meta.version.ToString());
                    request.Headers.Add(HttpRequestHeader.IfMatch, "*");
                    string js = JsonConvert.SerializeObject(user);
                    //js = js.Replace("", "\"id\":null,");
                    BTPFunctions.Log("js: " + js);
                    ret.js = js;
                    byte[] bodyBytes = Encoding.UTF8.GetBytes(js);
                    //request.SendChunked = false;
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
                        BTPFunctions.Log("strResult: " + strResult);

                        // close the stream object
                        loResponseStream.Close();
                        webresponse.Close();
                        //User res = Deserialize<User>(strResult);
                        ret.retjs = strResult;
                        ret.isSuccess = true;
                    }
                    catch (WebException wex)
                    {
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
                        }
                        else
                        {
                            ret.errText = (string.Format(
                                "Remote server call {0} {1} resulted in an error.",
                                "Put",
                                sURL));
                        }
                    }
                    catch (Exception e)
                    {
                        ret.errText = (string.Format(
                               "Remote server call {0} {1} resulted in an error.",
                               "Put",
                               sURL));
                        ret.e = e;
                    }

                    return ret;

                    //res.totalResults
                }
                else
                {
                    BTPFunctions.Log("User Id not specified");
                    ret.errText = "User Id not specified";
                }
            }
            catch (Exception e1)
            {
                ret.errText = "Unkown error updating user";
                ret.e = e1;
            }
            return ret;
        }

        public RetObject deleteUser(User userToDelete)
        {
            RetObject ret = new RetObject();

//            UserCore user = new UserCore(userToCreate);
            try
            {
                string sURL = _APIURL + $"Users/{userToDelete.id}";
                BTPFunctions.Log($"Deleting user, URL: {sURL}");




                var request = System.Net.HttpWebRequest.CreateHttp(sURL);

                if (_proxy != null)
                    request.Proxy = new WebProxy(_proxy, true);

                request.Method = "DELETE";
                request.ContentType = "application/json";
                request.Accept = "application/json";
                request.Headers.Add("Authorization", "Bearer " + _accessToken);

                try
                {
                    var webresponse = request.GetResponse();
                    Encoding enc = System.Text.Encoding.GetEncoding("utf-8");
                    // read response stream from response object
                    StreamReader loResponseStream = new StreamReader(webresponse.GetResponseStream(), enc);
                    // read string from stream data
                    string strResult = loResponseStream.ReadToEnd();
                    // close the stream object
                    loResponseStream.Close();
                    // close the response object
                    webresponse.Close();
                    ret.retjs = strResult;
                    BTPFunctions.Log($"response js: {strResult}");
                    ret.isSuccess = true;

                }
                catch (WebException wex)
                {
                    BTPFunctions.Log($"wex: {wex.Message}");

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
                        BTPFunctions.Log(httpResponse.StatusDescription);
                    }
                    else
                    {
                        ret.errText = (string.Format(
                            "Remote server call {0} {1} resulted in an error.",
                            "Put",
                            sURL));
                    }
                }
                catch (Exception e)
                {
                    BTPFunctions.Log($"ex: {e.Message}");

                    ret.errText = (string.Format(
                           "Remote server call {0} {1} resulted in an error.",
                           "Put",
                           sURL));
                    ret.e = e;
                }

                return ret;
            }
            catch (Exception e1)
            {
                BTPFunctions.Log($"e1: {e1.Message}");
                ret.errText = "Unkown error updating user";
                ret.e = e1;
            }
            return ret;
        }

        public RetObject createUser(User userToCreate)
        {
            RetObject ret = new RetObject();
            
            User foundUser = this.GetUser(userToCreate.userName,false);
            if (foundUser == null)
            {
                BTPFunctions.Log($"User already exists");
                ret.isSuccess = true;
                return ret;
            }

            UserCore user = new UserCore(userToCreate);
            try
            {
                string sURL = _APIURL + $"Users";
                string js   = JsonConvert.SerializeObject(user);
                BTPFunctions.Log($"Creating user, URL: {sURL}");
                BTPFunctions.Log($"sending js: {js}");
                ret.js = js;
                byte[] bodyBytes = Encoding.UTF8.GetBytes(js);

                const int maxRetries = 3;
                for (int attempt = 1; attempt <= maxRetries; attempt++)
                {
                    // HttpWebRequest nelze znovu pouzit po GetRequestStream() – vytvorit novy pro kazdy pokus
                    var request = System.Net.HttpWebRequest.CreateHttp(sURL);
                    if (_proxy != null)
                        request.Proxy = new WebProxy(_proxy, true);
                    request.Method      = "Post";
                    request.ContentType = "application/json";
                    request.Accept      = "application/json";
                    request.Headers.Add("Authorization", "Bearer " + _accessToken);

                    var stream = request.GetRequestStream();
                    stream.Write(bodyBytes, 0, bodyBytes.Length);
                    stream.Flush();
                    stream.Close();

                    try
                    {
                        var webresponse = request.GetResponse();
                        Encoding enc = System.Text.Encoding.GetEncoding("utf-8");
                        StreamReader loResponseStream = new StreamReader(webresponse.GetResponseStream(), enc);
                        string strResult = loResponseStream.ReadToEnd();
                        loResponseStream.Close();
                        webresponse.Close();
                        ret.retjs     = strResult;
                        ret.isSuccess = true;
                        BTPFunctions.Log($"response js: {strResult}");
                        break; // uspech – ukoncit retry smycku
                    }
                    catch (WebException wex)
                    {
                        BTPFunctions.Log($"wex (attempt {attempt}/{maxRetries}): {wex.Message}");
                        var httpResponse = wex.Response as HttpWebResponse;

                        if (httpResponse != null && (int)httpResponse.StatusCode == 429 && attempt < maxRetries)
                        {
                            // Precti Retry-After hlavicku, jinak pockej vychozi dobu
                            int waitSeconds = 10;
                            string retryAfterHeader = httpResponse.Headers["Retry-After"];
                            if (!string.IsNullOrEmpty(retryAfterHeader) && int.TryParse(retryAfterHeader, out int ra))
                                waitSeconds = ra;
                            BTPFunctions.Log($"429 Too Many Requests – cekam {waitSeconds}s pred dalsim pokusem");
                            System.Threading.Thread.Sleep(waitSeconds * 1000);
                            continue; // zkusit znovu
                        }

                        ret.e = wex;
                        ret.errText = httpResponse != null
                            ? string.Format("Remote server call Post {0} resulted in a http error {1} {2}.",
                                sURL, httpResponse.StatusCode, httpResponse.StatusDescription)
                            : string.Format("Remote server call Post {0} resulted in an error.", sURL);
                        if (httpResponse != null)
                            BTPFunctions.Log(httpResponse.StatusDescription);
                        break;
                    }
                    catch (Exception e)
                    {
                        BTPFunctions.Log($"ex: {e.Message}");
                        ret.errText = string.Format("Remote server call Post {0} resulted in an error.", sURL);
                        ret.e = e;
                        break;
                    }
                }

                return ret;
            }
            catch (Exception e1)
            {
                BTPFunctions.Log($"e1: {e1.Message}");
                ret.errText = "Unkown error creating user";
                ret.e = e1;
            }
            return ret;
        }


        public User GetUser(string id, bool searchByID)
        {
            //bool isValid = Guid.TryParse(id, out _);
            try
            {

                string sURL = _APIURL;
                if (searchByID)
                    sURL += $"Users/{id}";
                else
                    sURL += $"Users?filter=userName eq '{id}'";
                BTPFunctions.Log("GetUser URL: " + sURL);

                string strResult = BTPFunctions.HttpGet(sURL, _accessToken, _proxy);
                BTPFunctions.Log("strResult: " + strResult);

                if (searchByID)
                {
                    User user = Deserialize<User>(strResult);
                    BTPFunctions.Log("found user: " + user.userName);
                    return user;
                }
                else
                {
                    WebUserResult res = Deserialize<WebUserResult>(strResult);

                    if (res.totalResults == 1)
                    {
                        BTPFunctions.Log("found 1 user: " + (User)res.resources[0]);
                        return (User)res.resources[0];
                    }
                    else if (res.totalResults == 0)
                    {
                        User user = new User();
                        BTPFunctions.Log("0 results");
                        return user;
                    }
                    else
                    {
                        BTPFunctions.Log("null");
                        return null;
                    }
                }
            }
            catch (Exception ex)
            {
                BTPFunctions.Log("error searching user: " + ex.Message) ;
                BTPFunctions.Log(ex.StackTrace);
                return null;
            }
        }
        private static T Deserialize<T>(string responseBody)
        {
            //responseBody = responseBody.Replace("\"", "'");
            try
            {
                var toReturns = JsonConvert.DeserializeObject<T>(responseBody);
                return toReturns;
            }
            catch (Exception ex)
            {
                string errores;
                errores = ex.Message;
            }
            var toReturn = JsonConvert.DeserializeObject<T>(responseBody);
            return toReturn;
        }
    }

    public class Persons
    {

        public List<CSEntryChange> csentries = new List<CSEntryChange>();
        //public List<Person> Users = new List<Person>();
        public WebUserResult webUserResult;
        private string MAName;
        //public List<string> ugroups = null;
       // public List<Assigment> ucasts = null;
        public Persons(string maname)
        {
           // ugroups = UGroups;
            //ucasts = UCasts;
            MAName = maname;
        }
        public void GetUsersByStartIndex(string accessToken, int index, string APIURL, string proxy, string ExporFilePath, Dictionary<string, HashSet<string>> roleMembersFromUsers = null)
        {
            string sURL = APIURL + "Users?startIndex=" + index.ToString();
            string strResult = BTPFunctions.HttpGet(sURL, accessToken, proxy);
            //webUserResult = BTPFunctions.Deserialize<WebUserResult>("[" + strResult + "]");
            webUserResult = BTPFunctions.Deserialize<WebUserResult>(strResult);
            //BTPFunctions.Log("Deserialize: " + strResult);
            int counter = 0;
            foreach (User user in webUserResult.resources)
            {
               // BTPFunctions.Log("User:" + user.userName);
                //if (user.groups != null)
                //     BTPFunctions.Log("skupiny:" + user.groups.Length);

                CSEntryChange csentry = CSEntryChange.Create();
                csentry.ObjectModificationType = ObjectModificationType.Add;
                csentry.ObjectType = "Person";
                //csentry.DN = user.id;
                csentry.AttributeChanges.Add(AttributeChange.CreateAttributeAdd("MA", MAName));
                csentry.AttributeChanges.Add(AttributeChange.CreateAttributeAdd("ObjectID", user.id));
                csentry.AttributeChanges.Add(AttributeChange.CreateAttributeAdd("upn", user.userName));
                csentry.AttributeChanges.Add(AttributeChange.CreateAttributeAdd("userName", user.userName.Split('@')[0]));
                //csentry.AttributeChanges.Add(AttributeChange.CreateAttributeAdd("id", user.id));
                csentry.AttributeChanges.Add(AttributeChange.CreateAttributeAdd("active", user.active));
                if (user.name != null && user.name.givenName != null)
                    csentry.AttributeChanges.Add(AttributeChange.CreateAttributeAdd("firstName", user.name.givenName));
                if (user.name != null && user.name.familyName != null)
                    csentry.AttributeChanges.Add(AttributeChange.CreateAttributeAdd("lastName", user.name.familyName));
                string ret = null;
                foreach (email item in user.emails)
                {
                    if (ret == null)
                    {
                        ret = item.value;
                        if (item.primary)
                        {
                            break;
                        }
                    }
                }
                //Log("mail:" + user.mail);
                if (ret != null)
                {
                    csentry.AttributeChanges.Add(AttributeChange.CreateAttributeAdd("mail", ret));
                }




                List<string> groups = new List<string>();

                if (user.groups != null)
                {
                    foreach (var usergroup in user.groups)
                    {
                        groups.Add(usergroup.value);

                        File.AppendAllText(ExporFilePath, usergroup.value + ";" + user.userName.Split('@')[0] + Environment.NewLine);

                        if (roleMembersFromUsers != null && !string.IsNullOrEmpty(usergroup?.value) && !string.IsNullOrEmpty(user.id))
                        {
                            HashSet<string> members;
                            if (!roleMembersFromUsers.TryGetValue(usergroup.value, out members))
                            {
                                members = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                                roleMembersFromUsers[usergroup.value] = members;
                            }
                            members.Add(user.id);
                        }
                    }
                }
                if (groups.Count > 0)
                {
                    csentry.AttributeChanges.Add(AttributeChange.CreateAttributeAdd("UserGroups", groups.ToArray()));
                }

                counter++;
                csentries.Add(csentry);
                //Log("counter: " + counter);

            }

        }

    }
}
