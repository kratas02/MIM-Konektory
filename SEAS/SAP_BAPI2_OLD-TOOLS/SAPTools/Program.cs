using SAP.Middleware.Connector;
using System;
using System.Collections.Generic;
using System.Data;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Xml;
using System.Xml.Serialization;

namespace SAPTools
{
    class Program
    {
        // static Configuration cfg = null;
        public static string LogFile = null;
        static void Main(string[] args)
        {
            LogFile = Properties.Settings.Default.LogFile;
            LogFile = LogFile.Substring(0, LogFile.Length - 4) + "_" + DateTime.Now.ToString("yyyyMMdd") + LogFile.Substring(LogFile.Length - 4);
            // cfg = new Configuration();
            //  RfcDestinationManager.RegisterDestinationConfiguration(cfg);
            List<Person> persons = new List<Person>();



            //var xmldoc = new XmlDocument();
            //xmldoc.Load(@"C:\Program Files\Microsoft Forefront Identity Manager\2010\Synchronization Service\MaData\SAP_BAPI\SAP.XML");

            //List<Person> p = new List<Person>();
            //using (var stringReader = new System.IO.StringReader(xmldoc.OuterXml))
            //{
            //    var serializer = new XmlSerializer(typeof(List<Person>));
            //    persons = serializer.Deserialize(stringReader) as List<Person>;
            //}
            //Person p1 = persons.Where(x => x.EmployeeID == "U812228").FirstOrDefault();
            //string a = "1";

            // getAll();
            // return;           


            //
            //Person p = new Person();
            //p.EmployeeID = "u812401";
            ////DataSet userDetail = new Employees().GetEmployeeDetail("U301283");
            ////Dump(userDetail);
            //p.LastName = "Belušáková";
            //p.FirstName = "Silvia";
            //p.Password = "u8124011451";
            //persons.Add(p);
            //string strPersons = Serializer.WriteToXmlString(persons);
            //string B64Persons = Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(strPersons));
            //create(B64Persons);
            //gr.EmployeeID = "U104022";
            //gr.FullName = "WIMMP_SK_READ_ONLY";

            //DataSet userDetail = new Employees().GetEmployeeDetail("U104022");
            //Dump(userDetail);
            //updateRole();
            //return;
            //string b64 = "PD94bWwgdmVyc2lvbj0iMS4wIiBlbmNvZGluZz0idXRmLTE2Ij8+DQo8QXJyYXlPZkdyb3VwIHhtbG5zOnhzZD0iaHR0cDovL3d3dy53My5vcmcvMjAwMS9YTUxTY2hlbWEiIHhtbG5zOnhzaT0iaHR0cDovL3d3dy53My5vcmcvMjAwMS9YTUxTY2hlbWEtaW5zdGFuY2UiPg0KICA8R3JvdXA+DQogICAgPEVtcGxveWVlSUQ+dTEwNDAyMjwvRW1wbG95ZWVJRD4NCiAgICA8RnVsbE5hbWU+V0lNTVBfU0tfUkVBRF9PTkxZPC9GdWxsTmFtZT4NCiAgICA8VmFsaWRGcm9tPjIwMjItMDgtMTQ8L1ZhbGlkRnJvbT4NCiAgICA8VmFsaWRUbz4yMDIyLTEyLTMxPC9WYWxpZFRvPg0KICAgIDxEZXNjcmlwdGlvbj5UZXN0IFNBUCAoSHJvbWFkbsOhIMW+aWFkb3PFpSk8L0Rlc2NyaXB0aW9uPg0KICA8L0dyb3VwPg0KPC9BcnJheU9mR3JvdXA+";
            //string t = addRole(b64);


            //DataSet userDetail2 = new Employees().Activate("U409383",DateTime.Now.AddDays(10).ToString("yyyy-MM-dd"));
            //Dump(userDetail2);

            /*DataSet userDetail3 = new Employees().GetEmployeeDetail("U409383");
            Dump(userDetail3);

            // Update(person.EmployeeID, person.FirstName, person.LastName, person.EMail, null, person.ValidFrom, person.ValidTo, person.DEPARTMENT, person.FUNCTION);
            */
            var techName = "U409383_";
            /*var result = CreatePersons(new List<Person> { new Person
            {
                LastName = "Test",
                EmployeeID = techName,
                FUNCTION = "Senior účtovník",
                DEPARTMENT = "dpt.",
                Password = "x"
            } });
            Console.WriteLine(result);
            return;*/

            /*var userDetail3 = new Employees().GetEmployeeDetail(techName);
            Dump(userDetail3);

            return;*/

            //DataSet userDetail4 = new Employees().Deactivate("U409383", DateTime.Now.AddDays(-1).ToString("yyyy-MM-dd"));
            //Dump(userDetail4);

            //DataSet userDetail5 = new Employees().GetEmployeeDetail("U409383");
            //Dump(userDetail5);
            //string a = updateRole("PD94bWwgdmVyc2lvbj0iMS4wIiBlbmNvZGluZz0idXRmLTE2Ij8+DQo8QXJyYXlPZkdyb3VwIHhtbG5zOnhzZD0iaHR0cDovL3d3dy53My5vcmcvMjAwMS9YTUxTY2hlbWEiIHhtbG5zOnhzaT0iaHR0cDovL3d3dy53My5vcmcvMjAwMS9YTUxTY2hlbWEtaW5zdGFuY2UiPg0KICA8R3JvdXA+DQogICAgPEVtcGxveWVlSUQ+VTMwODgzOTwvRW1wbG95ZWVJRD4NCiAgICA8VmFsaWRGcm9tPjIwMjIwODE3PC9WYWxpZEZyb20+DQogICAgPFZhbGlkVG8+MjAyMzAxMDQ8L1ZhbGlkVG8+DQogIDwvR3JvdXA+DQo8L0FycmF5T2ZHcm91cD4=");

            //string a = removeRole("PD94bWwgdmVyc2lvbj0iMS4wIiBlbmNvZGluZz0idXRmLTE2Ij8+DQo8QXJyYXlPZkdyb3VwIHhtbG5zOnhzZD0iaHR0cDovL3d3dy53My5vcmcvMjAwMS9YTUxTY2hlbWEiIHhtbG5zOnhzaT0iaHR0cDovL3d3dy53My5vcmcvMjAwMS9YTUxTY2hlbWEtaW5zdGFuY2UiPg0KICA8R3JvdXA+DQogICAgPEVtcGxveWVlSUQ+VTMwODgzOTwvRW1wbG95ZWVJRD4NCiAgICA8RnVsbE5hbWU+V0lNTVBfU0tfUkVBRF9PTkxZPC9GdWxsTmFtZT4NCiAgPC9Hcm91cD4NCjwvQXJyYXlPZkdyb3VwPg==");
            //   updateRole("PD94bWwgdmVyc2lvbj0iMS4wIiBlbmNvZGluZz0idXRmLTE2Ij8+DQo8QXJyYXlPZkdyb3VwIHhtbG5zOnhzZD0iaHR0cDovL3d3dy53My5vcmcvMjAwMS9YTUxTY2hlbWEiIHhtbG5zOnhzaT0iaHR0cDovL3d3dy53My5vcmcvMjAwMS9YTUxTY2hlbWEtaW5zdGFuY2UiPg0KICA8R3JvdXA+DQogICAgPEVtcGxveWVlSUQ+VTUwNTA3NTwvRW1wbG95ZWVJRD4NCiAgICA8RnVsbE5hbWU+V0lNTVBfU0tfUkVBRF9PTkxZPC9GdWxsTmFtZT4NCiAgICA8VmFsaWRUbz4yMDIzMDgxNjwvVmFsaWRUbz4NCiAgPC9Hcm91cD4NCjwvQXJyYXlPZkdyb3VwPg==");
            //return;
            if (args.Length == 0)
            {
                getAll();
            }
            else
            {
                //Log("arg0:" + args[0]);
                switch (args[0].ToLower())
                {
                    case "zrfccall":
                        string user = args[1];
                        checkZRFCCALL(user);
                        break;
                    case "test":
                        Person person = new Person();
                        person.EmployeeID = "u109246";
                        //                        person.USERGROUP = "MES";
                        var result = new Employees().TestDefaults(person.EmployeeID, person.FirstName, person.LastName, person.EMail, null, person.ValidFrom, person.ValidTo, person.DEPARTMENT, person.FUNCTION, person.USERGROUP);
                        var testmessage = result.Tables["RETURN"].Rows[0]["MESSAGE"].ToString() + Environment.NewLine;
                        break;
                    case "getall":
                        Log("Spusten Full Import");
                        getAll();
                        break;
                    case "delta":
                        getAll(("u203454").Split(';'));
                        break;
                    case "setpassword":
                        //Log("setpassword:" + args[1] + ";" + args[2]);

                        Person p = new Person(args[1]);
                        p.Password = args[2];
                        string retmessage2 = "";
                        //Console.WriteLine(p.EmployeeID + " " + p.Password);
                        if (args.Length > 3 && args[3].ToLower() == "new")
                        {
                            retmessage2 = SetPassword(p, true, args[2]);
                        }
                        else
                        {
                            retmessage2 = SetPassword(p, false, args[2]);
                        }

                        //string retmessage2 = "OK";
                        //Log(retmessage2);
                        Console.WriteLine(retmessage2);
                        Environment.Exit(0);
                        break;


                    case "update":
                        Log("update:" + args[1]);
                        string retmessage = update(args[1]);
                        Log(retmessage);
                        Environment.Exit(0);
                        break;
                    case "create":
                        Log("create:" + args[1]);
                        string retmessageCreate = create(args[1]);
                        Log("return:" + retmessageCreate);
                        Console.WriteLine(retmessageCreate);
                        Environment.Exit(0);
                        break;
                    case "addrole":
                        Log("addrole:" + args[1]);
                        string retmessageAddRole = AssignRole(args[1]);
                        Log(retmessageAddRole);
                        //Console.WriteLine(retmessageAddRole);
                        Environment.Exit(0);
                        break;
                    case "removerole":
                        Log("removerole:" + args[1]);
                        string retmessageRemoveRole = removeRole(args[1]);
                        Log(retmessageRemoveRole);
                        //Console.WriteLine(retmessageAddRole);
                        Environment.Exit(0);
                        break;
                    case "updaterole":
                        Log("updaterole:" + args[1]);
                        string retmessageUpdateRole = AssignRole(args[1]);
                        Log(retmessageUpdateRole);
                        //Console.WriteLine(retmessageAddRole);
                        Environment.Exit(0);
                        break;
                    default:
                        break;
                }
            }

            //var PersonsFromB64 = Convert.FromBase64String(B64Persons);
            //string strPersons2 = System.Text.Encoding.UTF8.GetString(PersonsFromB64);

            //List<Person> p = Serializer.LoadFromXMLString(strPersons2);
            //            Serializer.WriteToXmlFile(@"C:\Temp\sap.xml", persons);
        }

        public static void Log(string text)
        {
            // Console.WriteLine(text);
            File.AppendAllText(LogFile, DateTime.Now.ToString("HH:mm:ss") + ": " + text + Environment.NewLine);
        }

        static string update(string B64Person)
        {
            var PersonsFromB64 = Convert.FromBase64String(B64Person);
            string strPersons = System.Text.Encoding.UTF8.GetString(PersonsFromB64);

            List<Person> persons = new List<Person>();
            using (var stringReader = new System.IO.StringReader(strPersons))
            {
                var serializer = new XmlSerializer(typeof(List<Person>));
                persons = serializer.Deserialize(stringReader) as List<Person>;
            }
            /*
            if (!strPersons.ToUpper().Contains("<USERGROUP>"))
            {
                foreach (var person in persons)
                {
                    person.UpdateUserGroup = false;
                    Log(person.EmployeeID + ": UpdateUserGroup=false");
                }
            }
            */

            //File.WriteAllText(@"C:\Batch\Logs\BAPI\Users\person_" + persons[0].EmployeeID + ".xml", strPersons);
            Log(strPersons);
            //return "OK";
            return Update(persons);
        }

        static string SetPassword(Person person, bool isInitial, string pass)
        {
            string message = "";
            person.update();


            if (isInitial)
            {
                if (person.CODVN.ToLower() != "x")
                {
                    return "User " + person.EmployeeID + " has password already set";
                }
            }

            message = new Employees().SetPassword(person.EmployeeID, pass);
            //message += result.Tables["RETURN"].Rows[0]["MESSAGE"].ToString() + Environment.NewLine;

            return message;


        }

        static string Update(List<Person> persons)
        {
            string message = "";

            foreach (Person person in persons)
            {
                //var result = new Employees().Update(person.EmployeeID, person.FirstName, person.LastName, person.EMail, null, person.ValidFrom, person.ValidTo, person.DEPARTMENT, person.FUNCTION, person.USERGROUP);
                var result = new Employees().Update(person, null);
                message += result.Tables["RETURN"].Rows[0]["MESSAGE"].ToString() + Environment.NewLine;
                //Dump(result2);
            }

            foreach (Person person in persons)
            {
                if (person.GLOB_LOCK == "L" || person.LOCAL_LOCK == "L")
                {
                    var result = new Employees().Deactivate(person.EmployeeID, person.ValidTo);
                    message += result.Tables["RETURN"].Rows[0]["MESSAGE"].ToString() + Environment.NewLine;
                }
                else if (person.LOCAL_LOCK == "U")
                {
                    var result = new Employees().Activate(person.EmployeeID, person.ValidTo);
                    message += result.Tables["RETURN"].Rows[0]["MESSAGE"].ToString() + Environment.NewLine;
                }
                //Dump(result2);
                if (!string.IsNullOrEmpty(Properties.Settings.Default.DependantRoles))
                {
                    person.update();
                    List<string> dependantRoles = Properties.Settings.Default.DependantRoles.Split(';').ToList<string>();
                    bool foundRole = false;
                    foreach (Group role in person.UserGroups)
                    {
                        if (!dependantRoles.Contains(role.FullName))
                        {
                            foundRole = true;
                        }
                    }

                    foreach (string role in dependantRoles)
                    {
                        if (!foundRole )
                        {
                            message += "Odebirani zavisle role: " + role + " uzivateli " + person.EmployeeID + Environment.NewLine;
                            var result = new Employees().RemoveApplicationRole(person.EmployeeID, role);
                            message += result + Environment.NewLine;
                        }
                    }
                }

            }

            return message;
            /*
            		TYPE = S
		            ID = 01
		            NUMBER = 039
		            MESSAGE = User U000009 has changed
		            LOG_NO = 
		            LOG_MSG_NO = 000000
		            MESSAGE_V1 = U000009
		            MESSAGE_V2 = 
		            MESSAGE_V3 = 
		            MESSAGE_V4 = 
		            PARAMETER = 
		            ROW = 0
		            FIELD = 
		            SYSTEM = K001W1P010 
            
             */

        }

        static string AssignRole(string B64Request)
        {
            string message = "";
            var RequestFromB64 = Convert.FromBase64String(B64Request);
            string strRequests = System.Text.Encoding.UTF8.GetString(RequestFromB64);

            List<Group> groups = new List<Group>();
            using (var stringReader = new System.IO.StringReader(strRequests))
            {
                var serializer = new XmlSerializer(typeof(List<Group>));
                groups = serializer.Deserialize(stringReader) as List<Group>;
            }


            foreach (Group group in groups)
            {
                message += "Pridavani role: " + group.FullName + " uzivateli " + group.EmployeeID + Environment.NewLine;
                string result = new Employees().AssignApplicationRole(group.EmployeeID, group.FullName, group.ValidFrom, group.ValidTo);
                //message += result.Tables["RETURN"].Rows[0]["MESSAGE"].ToString() + Environment.NewLine;
                message += result + Environment.NewLine;
                //Dump(result2);
            }

            if (!string.IsNullOrEmpty(Properties.Settings.Default.DependantRoles))
            {
                foreach (string role in Properties.Settings.Default.DependantRoles.Split(';'))
                {
                    message += "Pridavani zavisle role: " + role + " uzivateli " + groups[0].EmployeeID + Environment.NewLine;
                    var result = new Employees().AssignApplicationRole(groups[0].EmployeeID, role, null, null);
                    message += result + Environment.NewLine;
                }
            }

            return message;


        }

        //static string updateRole(string B64Request)
        //{
        //    string message = "";
        //    var RequestFromB64 = Convert.FromBase64String(B64Request);
        //    string strRequests = System.Text.Encoding.UTF8.GetString(RequestFromB64);

        //    List<Group> groups = new List<Group>();
        //    using (var stringReader = new System.IO.StringReader(strRequests))
        //    {
        //        var serializer = new XmlSerializer(typeof(List<Group>));
        //        groups = serializer.Deserialize(stringReader) as List<Group>;
        //    }
        //    foreach (Group group in groups)
        //    {
        //        var result = new Employees().UpdateApplicationRole(group.EmployeeID, group.FullName, group.ValidFrom, group.ValidTo);
        //        message += result.Tables["RETURN"].Rows[0]["MESSAGE"].ToString() + Environment.NewLine;
        //        //Dump(result2);
        //    }
        //    return message;
        //}
        static string removeRole(string B64Request)
        {
            string message = "";
            var RequestFromB64 = Convert.FromBase64String(B64Request);
            string strRequests = System.Text.Encoding.UTF8.GetString(RequestFromB64);

            List<Group> groups = new List<Group>();
            using (var stringReader = new System.IO.StringReader(strRequests))
            {
                var serializer = new XmlSerializer(typeof(List<Group>));
                groups = serializer.Deserialize(stringReader) as List<Group>;
            }
            foreach (Group group in groups)
            {
                var result = new Employees().RemoveApplicationRole(group.EmployeeID, group.FullName);
                message += result.Tables["RETURN"].Rows[0]["MESSAGE"].ToString() + Environment.NewLine;
                //Dump(result2);
            }
            return message;


        }



        static string updateRole()
        {
            string message = "";

            List<Group> groups = new List<Group>();

            //Group gr = new Group();
            //gr.EmployeeID = "U104022";
            //gr.FullName = "WIMMP_SK_READ_ONLY";
            //groups.Add(gr);
            foreach (Group group in groups)
            {
                var result = new Employees().UpdateApplicationRole(group.EmployeeID, group.FullName, "", "");
                message += result.Tables["RETURN"].Rows[0]["MESSAGE"].ToString() + Environment.NewLine;
                //Dump(result2);
            }
            return message;
        }

        static string create(string B64Person)
        {
            var PersonsFromB64 = Convert.FromBase64String(B64Person);
            string strPersons = System.Text.Encoding.UTF8.GetString(PersonsFromB64);

            List<Person> persons = new List<Person>();
            using (var stringReader = new System.IO.StringReader(strPersons))
            {
                var serializer = new XmlSerializer(typeof(List<Person>));
                persons = serializer.Deserialize(stringReader) as List<Person>;
            }

            return CreatePersons(persons);
        }

        static string CreatePersons(List<Person> persons)
        {
            Log("Creating person " + persons.Count.ToString());
            string message = "";
            foreach (Person person in persons)
            {
                Log("Creating person " + person.EmployeeID);
                var result = new Employees().Create(person);
                message += result.Tables["RETURN"].Rows[0]["MESSAGE"].ToString() + Environment.NewLine;
                Log("Message: " + result.Tables["RETURN"].Rows[0]["MESSAGE"].ToString());
                //Dump(result2);
            }
            return message;
            /*
            		TYPE = S
		            ID = 01
		            NUMBER = 039
		            MESSAGE = User U000009 has changed
		            LOG_NO = 
		            LOG_MSG_NO = 000000
		            MESSAGE_V1 = U000009
		            MESSAGE_V2 = 
		            MESSAGE_V3 = 
		            MESSAGE_V4 = 
		            PARAMETER = 
		            ROW = 0
		            FIELD = 
		            SYSTEM = K001W1P010 
            
             */

        }


        static void getAll(string[] IDs)
        {
            string XMLFile = null;

            try
            {
                XMLFile = Properties.Settings.Default.XMLFile;
            }
            catch
            {
            }
            List<Person> persons = new List<Person>();

            foreach (string ID in IDs)
            {
                Person person = new Person(ID);
                persons.Add(person);
            }


            foreach (var item in persons)
            {
                item.update();
            }

            Serializer.WriteToXmlFile(XMLFile, persons);
            string strPersons = Serializer.WriteToXmlString(persons);

            string B64Persons = Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(strPersons));
            //Console.WriteLine(persons.Count);
            Console.WriteLine(B64Persons);
            Environment.Exit(0);

        }

        static void checkZRFCCALL(string ID)
        {
            Person person = new Person(ID);
            person.update();
            bool containsZRFCCALL = false;
            foreach (Group group in person.UserGroups)
            {
                if (group.FullName.ToUpper() == "ZRFCCALL")
                {
                    containsZRFCCALL = true;
                }
            }

            if (person.UserGroups.Count > 0 && !containsZRFCCALL)
            {
                Console.WriteLine("Pridavani zavisle role uzivateli " + ID);
                var result = new Employees().AssignApplicationRole(ID, "ZRFCCALL", null, null);
                Console.WriteLine(result);
            }
            else if(containsZRFCCALL)
                {
                Console.WriteLine("Uzivateli " + ID + " ma roli jiz prirazenou");
            }
        }
        static void getAll()
        {
            List<Person> persons = new List<Person>();
            string XMLFile = null;

            try
            {
                XMLFile = Properties.Settings.Default.XMLFile;
            }
            catch
            {
            }
            //nacte pouze osobni cislo uzivatele
            var dt = new Employees().GetAll();
            //Console.WriteLine(dt.Rows.Count);
            if (dt == null)
            {
                Log("Nenalezen zadny zaznam");
                Environment.Exit(0);
            }
            foreach (DataRow row in dt.Rows)
            {
                Person person = new Person(row["USERNAME"].ToString());
                persons.Add(person);
            }

            var a = persons.Where(x => x.ModDate != null);

            //aktualizuje ostatni atributy uzivatele metodou GetEmployeeDetail
            foreach (var item in persons)
            {
                item.update();
            }

            if (XMLFile != null)
            {
                Serializer.WriteToXmlFile(XMLFile, persons);
            }
            string strPersons = Serializer.WriteToXmlString(persons);

            string B64Persons = Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(strPersons));
            Console.WriteLine(B64Persons);
            //Console.WriteLine(persons.Count);
            //Log(B64Persons);
            Environment.Exit(0);

        }

        //static List<Person> GetAll()
        //{
        //    List<Person> persons = new List<Person>();

        //    var dt = new Employees().GetAll();
        //    //Console.WriteLine(dt.Rows.Count);
        //    foreach (DataRow row in dt.Rows)
        //    {
        //        Person person = new Person(row["USERNAME"].ToString());
        //        persons.Add(person);
        //    }
        //    return persons;
        //}

        public static void Dump(IEnumerable<DataRow> rows)
        {
            foreach (DataRow dr in rows)
            {
                Log("\tRow:");
                var columns = dr.Table.Columns.OfType<DataColumn>().OrderBy(c => c.ColumnName).ToList();
                foreach (DataColumn dc in columns)
                {
                    Log($"\t\t{dc.ColumnName} = {dr[dc]}");
                }
            }
        }

        public static void Dump(DataSet ds)
        {
            foreach (DataTable dt in ds.Tables)
            {
                Log("Table: " + dt.TableName);
                Dump(dt.Rows.Cast<DataRow>());
                Log("");
            }
        }

    }
}
