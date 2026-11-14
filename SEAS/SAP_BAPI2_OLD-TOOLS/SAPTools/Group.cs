using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace SAPTools
{
    public class Group
    {
        public string EmployeeID { get; set; }
        public string FullName { get; set; }
        public string ValidFrom { get; set; }
        public string ValidTo { get; set; }
        public string Description { get; set; }
        //"" Direct Assigment
        //X HR Organization Assigment
        //C Composit Role
        //E Enterprise Portal Role
        public string AssigmentType { get; set; }
        public DateTime dtValidTo
        {
            get {
                //99991231
                return getDate(ValidTo, "yyyyMMdd");
            }
        }

        public DateTime dtValidFrom
        {
            get
            {
                //99991231
                return getDate(ValidFrom, "yyyyMMdd");
            }
        }
        public Group()
        {

        }

        public Group(DataRow GroupRow, string Id)
        {
            EmployeeID = Id;
            FullName = GroupRow["AGR_NAME"].ToString();
            ValidFrom = GroupRow["FROM_DAT"].ToString();
            ValidTo = GroupRow["TO_DAT"].ToString();
            Description = GroupRow["AGR_TEXT"].ToString();
            AssigmentType = GroupRow["ORG_FLAG"].ToString();
        }

        private DateTime getDate(string date, string fromFormat)
        {
            DateTime dt;
            if (!DateTime.TryParseExact(date, fromFormat, System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.None, out dt))
            {
                return dt;
            }
            return dt;
        }
    }


}
