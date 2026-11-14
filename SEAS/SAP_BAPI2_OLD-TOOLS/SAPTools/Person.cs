using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace SAPTools
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
        public string EMail { get; set; }
        public string Password { get; set; }
        public string WRNG_LOGON { get; set; }
        public string LOCAL_LOCK { get; set; }
        public string GLOB_LOCK { get; set; }
        public string NO_USER_PW { get; set; }
        public string FUNCTION { get; set; }
        public string DEPARTMENT { get; set; }

        public string USERGROUP { get; set; }
        public string ALIAS { get; set; }
        public string CODVN { get; set; }

        public int GroupsCount { get; set; }

        public string ModDate;
        public string ModTime;
        public string LTime;

        public string SPLD;
        public string SPDB;
        public string LANGU;
        public bool UpdateUserGroup=true;

        public DateTime dtValidTo
        {
            get
            {
                //99991231
                return getDate(ValidTo, "yyyyMMdd");
            }
        }
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
            try
            {
                DataSet userDetail = new Employees().GetEmployeeDetail(EmployeeID);

                LastName = userDetail.Tables["ADDRESS"].Rows[0]["LASTNAME"].ToString();
                FirstName = userDetail.Tables["ADDRESS"].Rows[0]["FIRSTNAME"].ToString();
                FullName = userDetail.Tables["ADDRESS"].Rows[0]["FULLNAME"].ToString();
                NickName = userDetail.Tables["ADDRESS"].Rows[0]["NICKNAME"].ToString();
                EMail = userDetail.Tables["ADDRESS"].Rows[0]["E_MAIL"].ToString();
                ModDate = userDetail.Tables["LASTMODIFIED"].Rows[0]["MODDATE"].ToString();
                ModTime = userDetail.Tables["LASTMODIFIED"].Rows[0]["MODTIME"].ToString();
                ValidFrom = userDetail.Tables["LOGONDATA"].Rows[0]["GLTGV"].ToString();
                ValidTo = userDetail.Tables["LOGONDATA"].Rows[0]["GLTGB"].ToString();
                CODVN = userDetail.Tables["LOGONDATA"].Rows[0]["CODVN"].ToString();
                LTime = userDetail.Tables["LOGONDATA"].Rows[0]["LTIME"].ToString();
                WRNG_LOGON = userDetail.Tables["ISLOCKED"].Rows[0]["WRNG_LOGON"].ToString();
                LOCAL_LOCK = userDetail.Tables["ISLOCKED"].Rows[0]["LOCAL_LOCK"].ToString();
                GLOB_LOCK = userDetail.Tables["ISLOCKED"].Rows[0]["GLOB_LOCK"].ToString();
                NO_USER_PW = userDetail.Tables["ISLOCKED"].Rows[0]["NO_USER_PW"].ToString();
                DEPARTMENT = userDetail.Tables["ADDRESS"].Rows[0]["DEPARTMENT"].ToString();
                FUNCTION = userDetail.Tables["ADDRESS"].Rows[0]["FUNCTION"].ToString();
                USERGROUP = userDetail.Tables["LOGONDATA"].Rows[0]["CLASS"].ToString();
                ALIAS = userDetail.Tables["ALIAS"].Rows[0]["USERALIAS"].ToString();
                if (string.IsNullOrEmpty(USERGROUP))
                {
                    USERGROUP = "X";
                }
                SPLD = userDetail.Tables["DEFAULTS"].Rows[0]["SPLD"].ToString();
                SPDB = userDetail.Tables["DEFAULTS"].Rows[0]["SPDB"].ToString();
                LANGU = userDetail.Tables["DEFAULTS"].Rows[0]["LANGU"].ToString();

                Password = null;
                var ACTIVITYGROUPS = userDetail.Tables["ACTIVITYGROUPS"].Rows.Cast<DataRow>();
                foreach (DataRow row in ACTIVITYGROUPS)
                {
                    Group gr = new Group(row, EmployeeID);
                    if (gr.AssigmentType != "C")
                    {
                        if (UserGroups.Where(x => x.FullName == gr.FullName).ToList().Count == 0)
                        {
                            UserGroups.Add(new Group(row, EmployeeID));
                        }
                    }

                    //if (gr.dtValidTo > DateTime.Now)
                    //{
                        //if (UserGroups.Where(x => x.FullName == gr.FullName).ToList().Count == 0)
                        //{
                        //    UserGroups.Add(new Group(row, EmployeeID));
                        //}
                    //}
                    //else
                    //{
                    //    string a = "";
                    //}
                }

            }
            catch (Exception e)
            {

                SAPTools.Program.Log($"{EmployeeID}: Full Import Error: " + e.Message);
            }
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

        private DateTime getDate(string date, string fromFormat)
        {
            DateTime dt;
            if (string.IsNullOrEmpty(date))
            {
                DateTime.TryParseExact("9999-12-31", fromFormat, System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.None, out dt);
            }
            else
            {
                DateTime.TryParseExact(date, fromFormat, System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.None, out dt);
            }
            return dt;
        }

    }
}
