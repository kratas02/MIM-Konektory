using System;
using System.Collections.Generic;

namespace SAPBAPI_Direct
{
    public class Person
    {
        public string EmployeeID { get; set; }
        public string FirstName { get; set; }
        public string LastName { get; set; }
        public string FullName { get; set; }
        public string ValidFrom { get; set; }
        public string ValidTo { get; set; }
        public string NickName { get; set; }
        public string Password { get; set; }
        public string EMail { get; set; }
        public string WRNG_LOGON { get; set; }
        public string LOCAL_LOCK { get; set; }
        public string GLOB_LOCK { get; set; }
        public string NO_USER_PW { get; set; }
        public string FUNCTION { get; set; }
        public string DEPARTMENT { get; set; }
        public string ALIAS { get; set; }
        public string USERGROUP { get; set; }
        public string CODVN { get; set; }
        public string LastModificationDate { get; set; }
        public string ModDate { get; set; }
        public string LTime { get; set; }
        public string SPLD { get; set; }
        public string SPDB { get; set; }
        public string LANGU { get; set; }
        public int GroupsCount { get; set; }

        public List<Group> UserGroups = new List<Group>();

        public Person()
        {
        }

        public Person(string id)
        {
            EmployeeID = id;
        }
    }
}
