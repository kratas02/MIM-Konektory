using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace FimSync_Ezma
{
    public class Group
    {
        public string EmployeeID { get; set; }
        public string FullName { get; set; }
        public string ValidFrom { get; set; }
        public string ValidTo { get; set; }
        public string Description { get; set; }

        public Group()
        {

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
