using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace FimSync_Ezma
{


    #region user classes

    public class UserCore
    {
        [JsonProperty("userName")]
        public string userName { get; set; }

        //[JsonProperty("id")]
        //public string id { get; set; }

        [JsonProperty("externalId")]
        public string externalId { get; set; }

        [JsonProperty("active")]
        public bool active { get; set; }

        [JsonProperty("verified")]
        public bool verified { get; set; }

        [JsonProperty("origin")]
        public string origin { get; set; }

        [JsonProperty("name")]
        public uname name { get; set; }

        [JsonProperty("meta")]
        public umeta meta { get; set; }

        [JsonProperty("zoneId")]
        public string zoneId { get; set; }


        [JsonProperty("emails")]
        public email[] emails { get; set; }

        [JsonProperty("schemas")]
        public string[] schemas { get; set; }

        //[JsonProperty("groups")]
        //public userGroup[] groups { get; set; }

        public UserCore()
        {
            this.meta = new umeta();
            this.meta.version = "1";
            this.name = new uname();
            email Email = new email();
            Email.primary = true;
            this.emails = new email[1];
            this.emails[0] = Email;
            this.active = true;
            this.verified = true;
            this.origin = "sap.custom";
            this.zoneId = "5cef7d08-471e-47c4-9695-ad5ef44b4355";
            this.schemas = new string[1];
            this.schemas[0] = "urn:scim:schemas:core:1.0";
        }

        public UserCore(User userToCreate)
        {
            this.externalId = userToCreate.externalId;
            this.userName = userToCreate.userName;
            this.meta = new umeta();
            this.meta.version = "1";
            this.name = userToCreate.name;
            this.emails = userToCreate.emails;
            this.active = true;
            this.verified = true;
            this.origin = "sap.custom";
            this.zoneId = "5cef7d08-471e-47c4-9695-ad5ef44b4355";
            this.schemas = new string[1];
            this.schemas[0] = "urn:scim:schemas:core:1.0";
        }
    }

    public class User:UserCore
    {
        [JsonProperty("id")]
        public string id { get; set; }

        [JsonProperty("groups")]
        public userGroup[] groups { get; set; }

    }


    //public class User
    //{
    //    [JsonProperty("userName")]
    //    public string userName { get; set; }

    //    [JsonProperty("id")]
    //    public string id { get; set; }

    //    [JsonProperty("externalId")]
    //    public string externalId { get; set; }

    //    [JsonProperty("active")]
    //    public bool active { get; set; }

    //    [JsonProperty("verified")]
    //    public bool verified { get; set; }

    //    [JsonProperty("origin")]
    //    public string origin { get; set; }

    //    [JsonProperty("name")]
    //    public uname name { get; set; }

    //    [JsonProperty("meta")]
    //    public umeta meta { get; set; }

    //    [JsonProperty("zoneId")]
    //    public string zoneId { get; set; }


    //    [JsonProperty("emails")]
    //    public email[] emails { get; set; }

    //    [JsonProperty("schemas")]
    //    public string[] schemas { get; set; }

    //    [JsonProperty("groups")]
    //    public userGroup[] groups { get; set; }

    //    public User()
    //    {
    //        this.meta = new umeta();
    //        this.meta.version = "1";
    //        this.name = new uname();
    //        email Email = new email();
    //        Email.primary = true;
    //        this.emails = new email[1];
    //        this.emails[0] = Email;
    //        this.active = true;
    //        this.verified = true;
    //        this.origin = "sap.custom";
    //        this.zoneId = "5cef7d08-471e-47c4-9695-ad5ef44b4355";
    //        this.schemas = new string[1];
    //        this.schemas[0] = "urn:scim:schemas:core:1.0";
    //    }
    //}
    public class userGroup
    {
        [JsonProperty("value")]
        public string value { get; set; }

        [JsonProperty("display")]
        public string display { get; set; }

        //[JsonProperty("type")]
        //public string gType { get; set; }
    }
    public class email
    {
        [JsonProperty("value")]
        public string value { get; set; }

        [JsonProperty("primary")]
        public bool primary { get; set; }
    }
    public class uname
    {
        [JsonProperty("familyName")]
        public string familyName { get; set; }

        [JsonProperty("givenName")]
        public string givenName { get; set; }
    }
    public class umeta
    {
        [JsonProperty("version")]
        public string version { get; set; }
    }
    public class WebUserResult : webresult
    {
        [JsonProperty("resources")]
        public User[] resources { get; set; }
    }
    #endregion
    #region role classes
    public class Role
    {
        [JsonProperty("id")]
        public string id { get; set; }
        [JsonProperty("displayName")]
        public string displayName { get; set; }
        [JsonProperty("zoneId")]
        public string zoneId { get; set; }
        [JsonProperty("meta")]
        public meta metadata { get; set; }
        [JsonProperty("description")]
        public string description { get; set; }

        [JsonProperty("members")]
        public member[] members { get; set; }

    }


    public class member
    {
        [JsonProperty("origin")]
        public string origin { get; set; }
        [JsonProperty("type")]
        public string otype { get; set; }
        [JsonProperty("value")]
        public string value { get; set; }
    }
    public class WebRoleResult : webresult
    {
        [JsonProperty("resources")]
        public Role[] resources { get; set; }
    }
    #endregion


    #region common classes
    public class webresult
    {
        [JsonProperty("startIndex")]
        public Int32 startIndex { get; set; }

        [JsonProperty("itemsPerPage")]
        public Int32 itemsPerPage { get; set; }

        [JsonProperty("totalResults")]
        public Int32 totalResults { get; set; }
    }
    public class AccessToken
    {
        [JsonProperty("access_token")]
        public string access_token { get; set; }
        [JsonProperty("token_type")]
        public string token_type { get; set; }
        [JsonProperty("expires_in")]
        public string expires_in { get; set; }
        [JsonProperty("scope")]
        public string scope { get; set; }
        [JsonProperty("jti")]
        public string jti { get; set; }
    }

    public class meta
    {
        [JsonProperty("version")]
        public string version { get; set; }
        [JsonProperty("created")]
        public string created { get; set; }
    }
    #endregion

}
