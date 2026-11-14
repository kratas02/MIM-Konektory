using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace SAPLibrary
{
    public class Group
    {
        public string EmployeeID { get; set; }
        public string Name { get; set; }
        public string ValidFrom { get; set; }
        public string ValidTo { get; set; }
        public string Description { get; set; }

        public Group(string id)
        {

        }

        public Group(DataRow GroupRow, string Id)
        {
            EmployeeID = Id;
            Name = GroupRow["AGR_NAME"].ToString();
            ValidFrom = GroupRow["FROM_DAT"].ToString();
            ValidTo = GroupRow["TO_DAT"].ToString();
            Description = GroupRow["AGR_TEXT"].ToString();
        }
    }


}
