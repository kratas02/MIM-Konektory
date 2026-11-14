using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace FimSync_Ezma
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
        public int GroupsCount { get; set; }

        public string ModDate;
        public string ModTime;
        public string LTime;
        /*
        public string ValidFrom2
        {
            get
            {
                //datePortalFormat = "yyyy-MM-ddTHH:mm:ss.000";
                // value = ChangeFormat(item.GetType().GetProperty(prop.Name).GetValue(item).ToString(), "yyyy-MM-dd", datePortalFo//rmat, true);
                try
                {
                    return ChangeFormat(ValidFrom, "yyyy-MM-dd", "yyyy-MM-ddTHH:mm:ss.000");
                }
                catch (Exception)
                {

                    return null;
                }


                //return DateTime.Now;
            }
        }

        public string ValidTo2
        {
            get
            {
                //datePortalFormat = "yyyy-MM-ddTHH:mm:ss.000";
                // value = ChangeFormat(item.GetType().GetProperty(prop.Name).GetValue(item).ToString(), "yyyy-MM-dd", datePortalFo//rmat, true);
                try
                {
                    return ChangeFormat(ValidTo, "yyyy-MM-dd", "yyyy-MM-ddTHH:mm:ss.000", true);
                }
                catch (Exception)
                {

                    return null;
                }

                //return DateTime.Now;
            }
        }
        */
        public string LastModificationDate
        {
            get
            {
                if (ModDate != null && ModTime != null)
                {
                    try
                    {
                        return ChangeFormat(ModDate + " " + ModTime, "yyyy-MM-dd HH:mm:ss", "yyyy-MM-ddTHH:mm:ss.000", false);
                    }
                    catch (Exception)
                    {

                        return null;
                    }

                }
                else
                {
                    return null;
                }

                //MODDATE = 2022-07-19
                //                MODTIME = 14:19:34
                //datePortalFormat = "yyyy-MM-ddTHH:mm:ss.000";
                // value = ChangeFormat(item.GetType().GetProperty(prop.Name).GetValue(item).ToString(), "yyyy-MM-dd", datePortalFo//rmat, true);

                //return DateTime.Now;
            }
        }



        public List<Group> UserGroups = new List<Group>();
        public Person()
        {

        }

        public Person(string id)
        {
            EmployeeID = id;
        }


        private string ChangeFormat(string date, string fromFormat, string toFormat, bool IsEndDate = false)
        {
            DateTime dt;
            if (!DateTime.TryParseExact(date, fromFormat, System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.None, out dt))
            {
                return "";
            }
            if (IsEndDate)
                dt = dt.AddHours(23).AddMinutes(59);
            return dt.ToString(toFormat);
        }

        private DateTime getDate(string date, string fromFormat, DateTime current, bool IsEndDate = false)
        {
            DateTime dt;
            if (!DateTime.TryParseExact(date, fromFormat, System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.None, out dt))
            {
                if (IsEndDate)
                    current = current.AddHours(23).AddMinutes(59);

                return current;
            }
            if (IsEndDate)
                dt = dt.AddHours(23).AddMinutes(59);
            return dt;
        }

    }
}
