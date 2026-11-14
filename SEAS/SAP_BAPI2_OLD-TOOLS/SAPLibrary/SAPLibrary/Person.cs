using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace SAPLibrary
{
    public class Person
    {
        public string EmployeeID { get; set; }
        public string FirstName { get; set; }
        public string LastName { get; set; }
        public string FullName { get; set; }
        public string strValidFrom;
        public string strValidTo;
        public string NickName { get; set; }
        public string EMail { get; set; }
        public int GroupsCount { get; set; }
        public string ModDate;
        public string ModTime;
        public string LTime;

        public string ValidFrom
        {
            get
            {
                //datePortalFormat = "yyyy-MM-ddTHH:mm:ss.000";
                // value = ChangeFormat(item.GetType().GetProperty(prop.Name).GetValue(item).ToString(), "yyyy-MM-dd", datePortalFo//rmat, true);
                try
                {
                    return ChangeFormat(strValidFrom, "yyyy-MM-dd", "yyyy-MM-ddTHH:mm:ss.000");
                }
                catch (Exception)
                {

                    return null;
                }


                //return DateTime.Now;
            }
        }

        public string ValidTo
        {
            get
            {
                //datePortalFormat = "yyyy-MM-ddTHH:mm:ss.000";
                // value = ChangeFormat(item.GetType().GetProperty(prop.Name).GetValue(item).ToString(), "yyyy-MM-dd", datePortalFo//rmat, true);
                try
                {
                    return ChangeFormat(strValidTo, "yyyy-MM-dd", "yyyy-MM-ddTHH:mm:ss.000", true);
                }
                catch (Exception)
                {

                    return null;
                }

                //return DateTime.Now;
            }
        }

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

        public void update()
        {
            //DataSet userDetail = new Employees().GetEmployeeDetail(EmployeeID);
            //LastName = userDetail.Tables["ADDRESS"].Rows[0]["FIRSTNAME"].ToString();
            //FirstName = userDetail.Tables["ADDRESS"].Rows[0]["LASTNAME"].ToString();
            //FullName = userDetail.Tables["ADDRESS"].Rows[0]["FULLNAME"].ToString();
            //NickName = userDetail.Tables["ADDRESS"].Rows[0]["NICKNAME"].ToString();
            //EMail = userDetail.Tables["ADDRESS"].Rows[0]["E_MAIL"].ToString();
            //ModDate = userDetail.Tables["LASTMODIFIED"].Rows[0]["MODDATE"].ToString();
            //ModTime = userDetail.Tables["LASTMODIFIED"].Rows[0]["MODTIME"].ToString();
            //strValidFrom = userDetail.Tables["LOGONDATA"].Rows[0]["GLTGV"].ToString();
            //strValidTo = userDetail.Tables["LOGONDATA"].Rows[0]["GLTGB"].ToString();
            //LTime = userDetail.Tables["LOGONDATA"].Rows[0]["LTIME"].ToString();

            //var ACTIVITYGROUPS = userDetail.Tables["ACTIVITYGROUPS"].Rows.Cast<DataRow>();
            //foreach (DataRow row in ACTIVITYGROUPS)
            //{
            //    UserGroups.Add(new Group(row, EmployeeID));
            //}
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
