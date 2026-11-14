using System;

namespace SAPBAPI_Direct
{
    public class Group
    {
        public string EmployeeID { get; set; }
        public string FullName { get; set; }
        public string ValidFrom { get; set; }
        public string ValidTo { get; set; }
        public string Description { get; set; }

        /// <summary>
        /// ORG_FLAG: prazdne = primo, C = composit (pri importe se zahazuje).
        /// </summary>
        public string AssignmentType { get; set; }

        public static string BuildId(string roleName, string employeeId)
        {
            return roleName + "_" + employeeId;
        }

        public static void ParseId(string anchor, out string roleName, out string employeeId)
        {
            if (string.IsNullOrEmpty(anchor))
                throw new InvalidOperationException("Group anchor je prazdny.");

            int sep = anchor.LastIndexOf('_');
            if (sep <= 0 || sep == anchor.Length - 1)
                throw new InvalidOperationException("Neplatny Group anchor '" + anchor + "'. Ocekavano Role_User.");

            roleName = anchor.Substring(0, sep);
            employeeId = anchor.Substring(sep + 1);
        }
    }
}
