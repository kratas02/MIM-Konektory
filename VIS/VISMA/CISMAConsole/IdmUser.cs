
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Text.Json.Serialization;

namespace FimSync_Ezma
{
    /// <summary>
    /// Model uživatele z API Idm/Users. Vlastnosti neznámé v době kompilace se ukládají do ExtensionData.
    /// </summary>
    public class IdmUser
    {
        [JsonPropertyName("userId")]
        public int UserId { get; set; }

        [JsonPropertyName("company")]
        public string Company { get; set; }

        [JsonPropertyName("employeeType")]
        public string EmployeeType { get; set; }

        [JsonPropertyName("employeeId")]
        public string EmployeeId { get; set; }

        [JsonPropertyName("firstName")]
        public string FirstName { get; set; }

        [JsonPropertyName("middleName")]
        public string MiddleName { get; set; }

        [JsonPropertyName("lastName")]
        public string LastName { get; set; }

        [JsonPropertyName("titleBefore")]
        public string TitleBefore { get; set; }

        [JsonPropertyName("titleAfter")]
        public string TitleAfter { get; set; }

        [JsonPropertyName("mobilePhone")]
        public string MobilePhone { get; set; }

        [JsonPropertyName("validFrom")]
        public string ValidFrom { get; set; }

        [JsonPropertyName("validTo")]
        public string ValidTo { get; set; }

        [JsonPropertyName("outFrom")]
        public string OutFrom { get; set; }

        [JsonPropertyName("inFrom")]
        public string InFrom { get; set; }

        [JsonPropertyName("mail")]
        public string Mail { get; set; }

        [JsonPropertyName("emailAlias")]
        public string EmailAlias { get; set; }

        [JsonPropertyName("physicalDeliveryOfficeName")]
        public string PhysicalDeliveryOfficeName { get; set; }

        [JsonPropertyName("locality")]
        public string Locality { get; set; }

        [JsonPropertyName("idLocality")]
        public int? IdLocality { get; set; }

        [JsonPropertyName("department")]
        public string Department { get; set; }

        [JsonPropertyName("departmentCode")]
        public string DepartmentCode { get; set; }

        [JsonPropertyName("departmentID")]
        public long? DepartmentId { get; set; }

        [JsonPropertyName("uperUnitCode")]
        public string UperUnitCode { get; set; }

        [JsonPropertyName("accountName")]
        public string AccountName { get; set; }

        [JsonPropertyName("userPrincipalName")]
        public string UserPrincipalName { get; set; }

        [JsonPropertyName("localityOU")]
        public string LocalityOu { get; set; }

        [JsonPropertyName("jobTitle")]
        public string JobTitle { get; set; }

        [JsonPropertyName("jobNumber")]
        public string JobNumber { get; set; }

        [JsonPropertyName("jobId")]
        public long? JobId { get; set; }

        [JsonPropertyName("isDirectory")]
        public bool IsDirectory { get; set; }

        [JsonPropertyName("hasCar")]
        public bool HasCar { get; set; }

        [JsonPropertyName("description")]
        public string Description { get; set; }

        [JsonPropertyName("manager")]
        public string Manager { get; set; }

        [JsonPropertyName("otherCompanies")]
        public string OtherCompanies { get; set; }

        /// <summary>
        /// Budoucí nebo neznámé atributy z API – zde se uloží vše, co nemá vlastní property.
        /// </summary>
        [JsonExtensionData]
        public Dictionary<string, System.Text.Json.JsonElement> ExtensionData { get; set; }
    }

}
