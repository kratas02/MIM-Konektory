using SAP.Middleware.Connector;
using System;
using System.Collections.Generic;
using System.Data;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;


namespace SAPTools
{
    public class Employees
    {
        public DataTable GetAll()
        {
            try
            {
                //RfcDestination dest = RfcDestinationManager.GetDestination("mySAPdestination");
                RfcDestination dest = RfcDestinationManager.GetDestination("mySAPdestination");

                RfcRepository repo = dest.Repository;

                IRfcFunction testfn = repo.CreateFunction("BAPI_USER_GETLIST");
                testfn.Invoke(dest);

                var employeesList = testfn.GetTable("USERLIST");

                // companyCodeList now contains a table with companies and codes
                return employeesList.ToDataTable("Employeees");

            }
            catch (Exception e)
            {
                Program.Log("GetAll Error: " + e.Message);
                Program.Log("GetAll Error: " + e.StackTrace);
                string a = "";
                return null;
            }
            //var par = new RfcConfigParameters();
            //var dest = RfcDestinationManager.GetDestination(par);

        }

        public DataSet GetEmployeeDetail(string userName)
        {
            RfcDestination dest = RfcDestinationManager.GetDestination("mySAPdestination");

            RfcRepository repo = dest.Repository;

            IRfcFunction testfn = repo.CreateFunction("BAPI_USER_GET_DETAIL");
            testfn.SetParameterActive("USERNAME", true);
            testfn.SetValue("USERNAME", userName);

            testfn.Invoke(dest);

            return testfn.ToDataSet();
        }

        public DataSet Create(Person person)
        {
            //public DataSet Create(string employeeId, string firstName, string lastName, string email, string password, string validFrom, string validTo,string DEPARTMENT, string FUNCTION)
            // , , person.LastName, person.EMail, person.Password, person.ValidFrom, person.ValidTo, person.DEPARTMENT, person.FUNCTION
            RfcDestination dest = RfcDestinationManager.GetDestination("mySAPdestination");

            RfcRepository repo = dest.Repository;

            IRfcFunction getUserDetailFn = repo.CreateFunction("BAPI_USER_GET_DETAIL");
            getUserDetailFn.SetParameterActive("USERNAME", true);
            getUserDetailFn.SetValue("USERNAME", person.EmployeeID);

            try
            {
                RfcSessionManager.BeginContext(dest);

                IRfcFunction userCreateFn = repo.CreateFunction("BAPI_USER_CREATE1");
                userCreateFn.SetValue("USERNAME", person.EmployeeID);

                // address
                var addressTable = getUserDetailFn.GetStructure("ADDRESS");
                userCreateFn.SetValue("ADDRESS", addressTable);
                var defaultsTable = getUserDetailFn.GetStructure("DEFAULTS");
                userCreateFn.SetValue("DEFAULTS", defaultsTable);


                addressTable.SetValue("FIRSTNAME", person.FirstName);
                addressTable.SetValue("LASTNAME", person.LastName);
                addressTable.SetValue("E_MAIL", person.EMail);



                    //addressTable.SetValue("DEPARTMENT", person.DEPARTMENT);
                    //addressTable.SetValue("FUNCTION", person.FUNCTION);
                    addressTable.SetValue("LANGUP_ISO", "SK");

                //defaultsTable.SetValue("SPLD", "LOCL");
                defaultsTable.SetValue("SPLD", "LOCM");
                defaultsTable.SetValue("SPDB", "G");
                defaultsTable.SetValue("LANGU", "Q");

                if (person.ALIAS != null)
                {
                    var aliasDataTable = userCreateFn.GetStructure("ALIAS");
                    userCreateFn.SetValue("ALIAS", aliasDataTable);
                    aliasDataTable.SetValue("USERALIAS", person.ALIAS);

                }

                // logondata
                var logonDataTable = userCreateFn.GetStructure("LOGONDATA");
                userCreateFn.SetValue("LOGONDATA", logonDataTable);

                if (!string.IsNullOrEmpty(person.ValidFrom))
                    logonDataTable.SetValue("GLTGV", person.ValidFrom);
                else
                    logonDataTable.SetValue("GLTGV", DateTime.Now.ToString("yyyy-MM-dd"));

                if (!string.IsNullOrEmpty(person.ValidTo))
                {
                    logonDataTable.SetValue("GLTGB", person.ValidTo);
                }
                else
                {
                    logonDataTable.SetValue("GLTGB", "9999-12-31");
                }

                // password
                var passwordTable = userCreateFn.GetStructure("PASSWORD"); // data type BAPIPWD
                userCreateFn.SetValue("PASSWORD", passwordTable);
                var password = person.Password;
                if (password.ToLower() != "x")
                {
                    passwordTable.SetValue("BAPIPWD", password);
                }
                else
                {
                    logonDataTable.SetValue("CODVN", "X");
                    passwordTable.SetValue("BAPIPWD", string.Empty);
                }

                userCreateFn.Invoke(dest);

                return userCreateFn.ToDataSet();

                // https://community.sailpoint.com/t5/IdentityNow-Wiki/IdentityNow-Rule-Guide-SAP-HR-Provisioning-Modify-Rule/ta-p/77306
                /*BAPI_EMPLOYEE_ENQUEUE

    HR_INFOTYPE_OPERATION

    BAPI_EMPLOYEE_DEQUEUE.

    BAPI_PERSDATA_CHANGE https://dev-workbench.com/en/sap-dictionary/function-module/bapi_persdata_change

    ,
                */
            }
            catch (Exception ex)
            {
                SAPTools.Program.Log(person.EmployeeID + "error: " + ex.Message);

                return null;
            }
            finally
            {
                RfcSessionManager.EndContext(dest); // BAPI_TRANSACTION_COMMIT
            }
        }

        public string SetPassword(string employeeId, string newPassword)
        {
            RfcDestination dest = RfcDestinationManager.GetDestination("mySAPdestination");

            RfcRepository repo = dest.Repository;

            TryVerifyExistence(employeeId);

            IRfcFunction getUserDetailFn = repo.CreateFunction("BAPI_USER_GET_DETAIL");
            getUserDetailFn.SetParameterActive("USERNAME", true);
            getUserDetailFn.SetValue("USERNAME", employeeId);

            var addressTable = getUserDetailFn.GetStructure("ADDRESS");

            try
            {
                RfcSessionManager.BeginContext(dest);

                IRfcFunction userChangeFn = repo.CreateFunction("BAPI_USER_CHANGE");
                //https://answers.sap.com/questions/7509568/jco-input-parameter-structures--bapiuserchange.html
                userChangeFn.SetValue("USERNAME", employeeId);

                var addressTableX = userChangeFn.GetStructure("ADDRESSX");
                // https://www.se80.co.uk/saptables/b/bapi/bapiaddr3x.htm

                userChangeFn.SetValue("ADDRESS", addressTable);
                userChangeFn.SetValue("ADDRESSX", addressTableX);

                var logonDataTable = userChangeFn.GetStructure("LOGONDATA");
                var logonDataTableX = userChangeFn.GetStructure("LOGONDATAX");




                userChangeFn.SetValue("LOGONDATA", logonDataTable);
                userChangeFn.SetValue("LOGONDATAX", logonDataTableX);

                // password

                IRfcFunction changePasswordFn = null;
                if (newPassword != null)
                {

                    //initialPassword = "587e.KJVF.359"; // initial password
                    //var pwd = "Khjs.145.neUw7";

                    //

                    var passwordTable = userChangeFn.GetStructure("PASSWORD"); // data type BAPIPWD
                    var passwordTableX = userChangeFn.GetStructure("PASSWORDX"); // data type BAPIPWD
                    passwordTableX.SetValue("BAPIPWD", "X");
                    passwordTable.SetValue("BAPIPWD", newPassword);
                    // userChangeFn.SetValue("PASSWORDX", passwordTableX);                    
                    userChangeFn.SetValue("PASSWORD", passwordTable);
                    // logonDataTableX.SetValue("CODVN", "X");
                    // logonDataTable.SetValue("CODVN", "");

                    /*
                    changePasswordFn = repo.CreateFunction("SUSR_USER_CHANGE_PASSWORD_RFC");
                    changePasswordFn.SetValue("BNAME", employeeId);
                    changePasswordFn.SetValue("NEW_PASSWORD", initialPassword);
                    changePasswordFn.SetValue("PASSWORD", pwd);
                    */
                }

                userChangeFn.Invoke(dest);

                if (changePasswordFn != null)
                {
                    try
                    {
                        changePasswordFn.Invoke(dest);
                        //return "OK";
                    }
                    catch (RfcAbapException e)
                    {
                        //throw new Exception(e.Key);
                        return "Chyba pri nastavovani hesla: " + e.Key + ";" + e.Message;
                    }
                }


                return userChangeFn.ToDataSet().Tables["RETURN"].Rows[0]["MESSAGE"].ToString();
            }
            finally
            {
                RfcSessionManager.EndContext(dest); // BAPI_TRANSACTION_COMMIT
            }
        }


        public DataSet TestDefaults(string employeeId, string firstName, string lastName, string email, string initialPassword, string validFrom, string validTo, string DEPARTMENT, string FUNCTION, string userGroup)
        {
            RfcDestination dest = RfcDestinationManager.GetDestination("mySAPdestination");

            RfcRepository repo = dest.Repository;

            //TryVerifyExistence(employeeId);

            IRfcFunction getUserDetailFn = repo.CreateFunction("BAPI_USER_GET_DETAIL");
            getUserDetailFn.SetParameterActive("USERNAME", true);
            getUserDetailFn.SetValue("USERNAME", employeeId);

            var addressTable = getUserDetailFn.GetStructure("ADDRESS");
            var defaultsTable = getUserDetailFn.GetStructure("DEFAULTS");
            try
            {
                RfcSessionManager.BeginContext(dest);

                IRfcFunction userChangeFn = repo.CreateFunction("BAPI_USER_CHANGE");
                //https://answers.sap.com/questions/7509568/jco-input-parameter-structures--bapiuserchange.html
                userChangeFn.SetValue("USERNAME", employeeId);
                var defaultsTableX = userChangeFn.GetStructure("DEFAULTSX");


                defaultsTableX.SetValue("SPLD", "X");
                defaultsTable.SetValue("SPLD", "LOCL");
                defaultsTableX.SetValue("SPDB", "X");
                defaultsTable.SetValue("SPDB", "G");
                defaultsTableX.SetValue("LANGU", "X");
                defaultsTable.SetValue("LANGU", "Q");


                var addressTableX = userChangeFn.GetStructure("ADDRESSX");
                // https://www.se80.co.uk/saptables/b/bapi/bapiaddr3x.htm

                if (!string.IsNullOrEmpty(FUNCTION))
                {
                    addressTableX.SetValue("FUNCTION", "X");
                    addressTable.SetValue("FUNCTION", FUNCTION);
                }

                if (!string.IsNullOrEmpty(DEPARTMENT))
                {
                    addressTableX.SetValue("DEPARTMENT", "X");
                    addressTable.SetValue("DEPARTMENT", DEPARTMENT);
                }

                if (!string.IsNullOrEmpty(firstName))
                {
                    addressTableX.SetValue("FIRSTNAME", "X");
                    addressTable.SetValue("FIRSTNAME", firstName);
                }

                if (!string.IsNullOrEmpty(lastName))
                {
                    addressTableX.SetValue("LASTNAME", "X");
                    addressTable.SetValue("LASTNAME", lastName);
                }

                if (!string.IsNullOrEmpty(email))
                {
                    addressTableX.SetValue("E_MAIL", "X");
                    addressTable.SetValue("E_MAIL", email);
                }

                userChangeFn.SetValue("ADDRESS", addressTable);
                userChangeFn.SetValue("ADDRESSX", addressTableX);

                userChangeFn.SetValue("DEFAULTS", defaultsTable);
                userChangeFn.SetValue("DEFAULTSX", defaultsTableX);


                var logonDataTable = userChangeFn.GetStructure("LOGONDATA");
                var logonDataTableX = userChangeFn.GetStructure("LOGONDATAX");

                if (!string.IsNullOrEmpty(validTo))
                {
                    logonDataTable.SetValue("GLTGB", validTo);
                    logonDataTableX.SetValue("GLTGB", "X");
                }

                if (!string.IsNullOrEmpty(userGroup))
                {
                    logonDataTable.SetValue("CLASS", userGroup);
                    logonDataTableX.SetValue("CLASS", "X");
                }
                else
                {
                    logonDataTable.SetValue("CLASS", "");
                    logonDataTableX.SetValue("CLASS", "X");
                }



                /*

                var logonDataTable = userChangeFn.GetStructure("LOGONDATA");
                var logonDataTableX = userChangeFn.GetStructure("LOGONDATAX");
                logonDataTable.SetValue("GLTGV", validFrom.ToString("yyyy-MM-dd"));
                if (validThrough.HasValue)
                {
                    logonDataTable.SetValue("GLTGB", validThrough.Value.ToString("yyyy-MM-dd"));
                }
                else
                {
                    logonDataTable.SetValue("GLTGB", "");
                }

                logonDataTableX.SetValue("GLTGV", "X");
                logonDataTableX.SetValue("GLTGB", "X");
                */
                /*
                userChangeFn.SetValue("LOGONDATA", logonDataTable);
                userChangeFn.SetValue("LOGONDATAX", logonDataTableX);



                ---------------------------------
                                var passwordTable = userCreateFn.GetStructure("PASSWORD"); // data type BAPIPWD
                userCreateFn.SetValue("PASSWORD", passwordTable);
                var password = person.Password;
                if (password.ToLower() != "x")
                {
                    passwordTable.SetValue("BAPIPWD", password);
                }
                else
                {
                    logonDataTable.SetValue("CODVN", "X");
                    passwordTable.SetValue("BAPIPWD", string.Empty);
                }

                --------------------------------
                */
                userChangeFn.SetValue("LOGONDATA", logonDataTable);
                userChangeFn.SetValue("LOGONDATAX", logonDataTableX);

                // password

                IRfcFunction changePasswordFn = null;
                if (initialPassword != null)
                {

                    //initialPassword = "587e.KJVF.359"; // initial password
                    //var pwd = "Khjs.145.neUw7";

                    var pwd = initialPassword;
                    //

                    var passwordTable = userChangeFn.GetStructure("PASSWORD"); // data type BAPIPWD
                    var passwordTableX = userChangeFn.GetStructure("PASSWORDX"); // data type BAPIPWD
                    passwordTableX.SetValue("BAPIPWD", "X");
                    passwordTable.SetValue("BAPIPWD", pwd);
                    // userChangeFn.SetValue("PASSWORDX", passwordTableX);                    
                    userChangeFn.SetValue("PASSWORD", passwordTable);
                    // logonDataTableX.SetValue("CODVN", "X");
                    // logonDataTable.SetValue("CODVN", "");

                    /*
                    changePasswordFn = repo.CreateFunction("SUSR_USER_CHANGE_PASSWORD_RFC");
                    changePasswordFn.SetValue("BNAME", employeeId);
                    changePasswordFn.SetValue("NEW_PASSWORD", initialPassword);
                    changePasswordFn.SetValue("PASSWORD", pwd);
                    */
                }

                userChangeFn.Invoke(dest);

                if (changePasswordFn != null)
                {
                    try
                    {
                        changePasswordFn.Invoke(dest);
                    }
                    catch (RfcAbapException e)
                    {
                        throw new Exception(e.Key);
                    }
                }

                return userChangeFn.ToDataSet();

                // https://community.sailpoint.com/t5/IdentityNow-Wiki/IdentityNow-Rule-Guide-SAP-HR-Provisioning-Modify-Rule/ta-p/77306
                /*BAPI_EMPLOYEE_ENQUEUE

    HR_INFOTYPE_OPERATION

    BAPI_EMPLOYEE_DEQUEUE.

    BAPI_PERSDATA_CHANGE https://dev-workbench.com/en/sap-dictionary/function-module/bapi_persdata_change

    ,
                */
            }
            finally
            {
                RfcSessionManager.EndContext(dest); // BAPI_TRANSACTION_COMMIT
            }
        }


        public DataSet Update(string employeeId, string firstName, string lastName, string email, string initialPassword, string validFrom, string validTo, string DEPARTMENT, string FUNCTION, string userGroup, string alias)
        {
            SAPTools.Program.Log("Aktualizace uzivatele employeeId");

            RfcDestination dest = RfcDestinationManager.GetDestination("mySAPdestination");

            RfcRepository repo = dest.Repository;

            TryVerifyExistence(employeeId);

            IRfcFunction getUserDetailFn = repo.CreateFunction("BAPI_USER_GET_DETAIL");
            getUserDetailFn.SetParameterActive("USERNAME", true);
            getUserDetailFn.SetValue("USERNAME", employeeId);

            var addressTable = getUserDetailFn.GetStructure("ADDRESS");

            try
            {
                RfcSessionManager.BeginContext(dest);

                IRfcFunction userChangeFn = repo.CreateFunction("BAPI_USER_CHANGE");
                //https://answers.sap.com/questions/7509568/jco-input-parameter-structures--bapiuserchange.html
                userChangeFn.SetValue("USERNAME", employeeId);
                if (!string.IsNullOrEmpty(alias))
                {
                    userChangeFn.SetValue("ALIAS", alias);
                }


                var addressTableX = userChangeFn.GetStructure("ADDRESSX");
                // https://www.se80.co.uk/saptables/b/bapi/bapiaddr3x.htm

                if (!string.IsNullOrEmpty(FUNCTION))
                {
                    addressTableX.SetValue("FUNCTION", "X");
                    addressTable.SetValue("FUNCTION", FUNCTION);
                }

                if (!string.IsNullOrEmpty(DEPARTMENT))
                {
                    addressTableX.SetValue("DEPARTMENT", "X");
                    addressTable.SetValue("DEPARTMENT", DEPARTMENT);
                }

                if (!string.IsNullOrEmpty(firstName))
                {
                    addressTableX.SetValue("FIRSTNAME", "X");
                    addressTable.SetValue("FIRSTNAME", firstName);
                }

                if (!string.IsNullOrEmpty(lastName))
                {
                    addressTableX.SetValue("LASTNAME", "X");
                    addressTable.SetValue("LASTNAME", lastName);
                }

                if (!string.IsNullOrEmpty(email))
                {
                    addressTableX.SetValue("E_MAIL", "X");
                    addressTable.SetValue("E_MAIL", email);
                }

                userChangeFn.SetValue("ADDRESS", addressTable);
                userChangeFn.SetValue("ADDRESSX", addressTableX);

                var logonDataTable = userChangeFn.GetStructure("LOGONDATA");
                var logonDataTableX = userChangeFn.GetStructure("LOGONDATAX");

                if (!string.IsNullOrEmpty(validTo))
                {
                    logonDataTable.SetValue("GLTGB", validTo);
                    logonDataTableX.SetValue("GLTGB", "X");
                }

                if (!string.IsNullOrEmpty(userGroup))
                {
                    logonDataTable.SetValue("CLASS", userGroup);
                    logonDataTableX.SetValue("CLASS", "X");
                }
                else
                {
                    logonDataTable.SetValue("CLASS", "");
                    logonDataTableX.SetValue("CLASS", "X");
                }



                /*

                var logonDataTable = userChangeFn.GetStructure("LOGONDATA");
                var logonDataTableX = userChangeFn.GetStructure("LOGONDATAX");
                logonDataTable.SetValue("GLTGV", validFrom.ToString("yyyy-MM-dd"));
                if (validThrough.HasValue)
                {
                    logonDataTable.SetValue("GLTGB", validThrough.Value.ToString("yyyy-MM-dd"));
                }
                else
                {
                    logonDataTable.SetValue("GLTGB", "");
                }

                logonDataTableX.SetValue("GLTGV", "X");
                logonDataTableX.SetValue("GLTGB", "X");
                */
                /*
                userChangeFn.SetValue("LOGONDATA", logonDataTable);
                userChangeFn.SetValue("LOGONDATAX", logonDataTableX);



                ---------------------------------
                                var passwordTable = userCreateFn.GetStructure("PASSWORD"); // data type BAPIPWD
                userCreateFn.SetValue("PASSWORD", passwordTable);
                var password = person.Password;
                if (password.ToLower() != "x")
                {
                    passwordTable.SetValue("BAPIPWD", password);
                }
                else
                {
                    logonDataTable.SetValue("CODVN", "X");
                    passwordTable.SetValue("BAPIPWD", string.Empty);
                }

                --------------------------------
                */
                userChangeFn.SetValue("LOGONDATA", logonDataTable);
                userChangeFn.SetValue("LOGONDATAX", logonDataTableX);

                // password

                IRfcFunction changePasswordFn = null;
                if (initialPassword != null)
                {

                    //initialPassword = "587e.KJVF.359"; // initial password
                    //var pwd = "Khjs.145.neUw7";

                    var pwd = initialPassword;
                    //

                    var passwordTable = userChangeFn.GetStructure("PASSWORD"); // data type BAPIPWD
                    var passwordTableX = userChangeFn.GetStructure("PASSWORDX"); // data type BAPIPWD
                    passwordTableX.SetValue("BAPIPWD", "X");
                    passwordTable.SetValue("BAPIPWD", pwd);
                    // userChangeFn.SetValue("PASSWORDX", passwordTableX);                    
                    userChangeFn.SetValue("PASSWORD", passwordTable);
                    // logonDataTableX.SetValue("CODVN", "X");
                    // logonDataTable.SetValue("CODVN", "");

                    /*
                    changePasswordFn = repo.CreateFunction("SUSR_USER_CHANGE_PASSWORD_RFC");
                    changePasswordFn.SetValue("BNAME", employeeId);
                    changePasswordFn.SetValue("NEW_PASSWORD", initialPassword);
                    changePasswordFn.SetValue("PASSWORD", pwd);
                    */
                }

                userChangeFn.Invoke(dest);

                if (changePasswordFn != null)
                {
                    try
                    {
                        changePasswordFn.Invoke(dest);
                    }
                    catch (RfcAbapException e)
                    {
                        SAPTools.Program.Log("Chyba nastaveni hesla " + e.Key);

                        throw new Exception(e.Key);
                    }
                }

                return userChangeFn.ToDataSet();

                // https://community.sailpoint.com/t5/IdentityNow-Wiki/IdentityNow-Rule-Guide-SAP-HR-Provisioning-Modify-Rule/ta-p/77306
                /*BAPI_EMPLOYEE_ENQUEUE

    HR_INFOTYPE_OPERATION

    BAPI_EMPLOYEE_DEQUEUE.

    BAPI_PERSDATA_CHANGE https://dev-workbench.com/en/sap-dictionary/function-module/bapi_persdata_change

    ,
                */
            }
            finally
            {
                RfcSessionManager.EndContext(dest); // BAPI_TRANSACTION_COMMIT
            }
        }
        public DataSet Update(Person person, string initialPassword)
        {
            SAPTools.Program.Log("Aktualizace uzivatele " + person.EmployeeID);

            RfcDestination dest = RfcDestinationManager.GetDestination("mySAPdestination");

            RfcRepository repo = dest.Repository;

            TryVerifyExistence(person.EmployeeID);

            IRfcFunction getUserDetailFn = repo.CreateFunction("BAPI_USER_GET_DETAIL");
            getUserDetailFn.SetParameterActive("USERNAME", true);
            getUserDetailFn.SetValue("USERNAME", person.EmployeeID);

            var addressTable = getUserDetailFn.GetStructure("ADDRESS");

            try
            {
                RfcSessionManager.BeginContext(dest);

                IRfcFunction userChangeFn = repo.CreateFunction("BAPI_USER_CHANGE");
                //https://answers.sap.com/questions/7509568/jco-input-parameter-structures--bapiuserchange.html
                userChangeFn.SetValue("USERNAME", person.EmployeeID);

                var addressTableX = userChangeFn.GetStructure("ADDRESSX");
                // https://www.se80.co.uk/saptables/b/bapi/bapiaddr3x.htm

                if (!string.IsNullOrEmpty(person.FUNCTION))
                {
                    addressTableX.SetValue("FUNCTION", "X");
                    addressTable.SetValue("FUNCTION", person.FUNCTION);
                }

                if (!string.IsNullOrEmpty(person.DEPARTMENT))
                {
                    addressTableX.SetValue("DEPARTMENT", "X");
                    addressTable.SetValue("DEPARTMENT", person.DEPARTMENT);
                }

                if (!string.IsNullOrEmpty(person.FirstName))
                {
                    addressTableX.SetValue("FIRSTNAME", "X");
                    addressTable.SetValue("FIRSTNAME", person.FirstName);
                }

                //rozhrani to neumi
                //if (!string.IsNullOrEmpty(person.USERALIAS))
                //{
                //    addressTableX.SetValue("USERALIAS", "X");
                //    addressTable.SetValue("USERALIAS", person.USERALIAS);
                //}

                if (!string.IsNullOrEmpty(person.LastName))
                {
                    addressTableX.SetValue("LASTNAME", "X");
                    addressTable.SetValue("LASTNAME", person.LastName);
                }

                if (!string.IsNullOrEmpty(person.EMail))
                {
                    addressTableX.SetValue("E_MAIL", "X");
                    addressTable.SetValue("E_MAIL", person.EMail);
                }


                userChangeFn.SetValue("ADDRESS", addressTable);
                userChangeFn.SetValue("ADDRESSX", addressTableX);


                if(person.ALIAS != null)
                {
                    var aliasDataTable = userChangeFn.GetStructure("ALIAS");
                    var aliasDataTableX = userChangeFn.GetStructure("ALIASX");

                    aliasDataTable.SetValue("USERALIAS", person.ALIAS);
                    aliasDataTableX.SetValue("BAPIALIAS", "X");

                    userChangeFn.SetValue("ALIAS", aliasDataTable);
                    userChangeFn.SetValue("ALIASX", aliasDataTableX);
                }

                var logonDataTable = userChangeFn.GetStructure("LOGONDATA");
                var logonDataTableX = userChangeFn.GetStructure("LOGONDATAX");

                if (!string.IsNullOrEmpty(person.ValidTo))
                {
                    logonDataTable.SetValue("GLTGB", person.ValidTo);
                    logonDataTableX.SetValue("GLTGB", "X");
                }



                if (!string.IsNullOrEmpty(person.USERGROUP))
                {
                    if (person.USERGROUP == "X")
                    {
                        logonDataTable.SetValue("CLASS", "");
                        logonDataTableX.SetValue("CLASS", "X");
                    }
                    else
                    {
                        logonDataTable.SetValue("CLASS", person.USERGROUP);
                        logonDataTableX.SetValue("CLASS", "X");
                    }
                }

                /*

                var logonDataTable = userChangeFn.GetStructure("LOGONDATA");
                var logonDataTableX = userChangeFn.GetStructure("LOGONDATAX");
                logonDataTable.SetValue("GLTGV", validFrom.ToString("yyyy-MM-dd"));
                if (validThrough.HasValue)
                {
                    logonDataTable.SetValue("GLTGB", validThrough.Value.ToString("yyyy-MM-dd"));
                }
                else
                {
                    logonDataTable.SetValue("GLTGB", "");
                }

                logonDataTableX.SetValue("GLTGV", "X");
                logonDataTableX.SetValue("GLTGB", "X");
                */
                /*
                userChangeFn.SetValue("LOGONDATA", logonDataTable);
                userChangeFn.SetValue("LOGONDATAX", logonDataTableX);



                ---------------------------------
                                var passwordTable = userCreateFn.GetStructure("PASSWORD"); // data type BAPIPWD
                userCreateFn.SetValue("PASSWORD", passwordTable);
                var password = person.Password;
                if (password.ToLower() != "x")
                {
                    passwordTable.SetValue("BAPIPWD", password);
                }
                else
                {
                    logonDataTable.SetValue("CODVN", "X");
                    passwordTable.SetValue("BAPIPWD", string.Empty);
                }

                --------------------------------
                */
                userChangeFn.SetValue("LOGONDATA", logonDataTable);
                userChangeFn.SetValue("LOGONDATAX", logonDataTableX);
                


                // password

                IRfcFunction changePasswordFn = null;
                if (initialPassword != null)
                {

                    //initialPassword = "587e.KJVF.359"; // initial password
                    //var pwd = "Khjs.145.neUw7";

                    var pwd = initialPassword;
                    //

                    var passwordTable = userChangeFn.GetStructure("PASSWORD"); // data type BAPIPWD
                    var passwordTableX = userChangeFn.GetStructure("PASSWORDX"); // data type BAPIPWD
                    passwordTableX.SetValue("BAPIPWD", "X");
                    passwordTable.SetValue("BAPIPWD", pwd);
                    // userChangeFn.SetValue("PASSWORDX", passwordTableX);                    
                    userChangeFn.SetValue("PASSWORD", passwordTable);
                    // logonDataTableX.SetValue("CODVN", "X");
                    // logonDataTable.SetValue("CODVN", "");

                    /*
                    changePasswordFn = repo.CreateFunction("SUSR_USER_CHANGE_PASSWORD_RFC");
                    changePasswordFn.SetValue("BNAME", employeeId);
                    changePasswordFn.SetValue("NEW_PASSWORD", initialPassword);
                    changePasswordFn.SetValue("PASSWORD", pwd);
                    */
                }

                userChangeFn.Invoke(dest);

                if (changePasswordFn != null)
                {
                    try
                    {
                        changePasswordFn.Invoke(dest);
                    }
                    catch (RfcAbapException e)
                    {
                        SAPTools.Program.Log("Chyba nastaveni hesla " + e.Key);

                        throw new Exception(e.Key);
                    }
                }

                return userChangeFn.ToDataSet();

                // https://community.sailpoint.com/t5/IdentityNow-Wiki/IdentityNow-Rule-Guide-SAP-HR-Provisioning-Modify-Rule/ta-p/77306
                /*BAPI_EMPLOYEE_ENQUEUE

    HR_INFOTYPE_OPERATION

    BAPI_EMPLOYEE_DEQUEUE.

    BAPI_PERSDATA_CHANGE https://dev-workbench.com/en/sap-dictionary/function-module/bapi_persdata_change

    ,
                */
            }
            finally
            {
                RfcSessionManager.EndContext(dest); // BAPI_TRANSACTION_COMMIT
            }
        }

        public DataSet Deactivate(string employeeId, string validTo)
        {
            RfcDestination dest = RfcDestinationManager.GetDestination("mySAPdestination");

            RfcRepository repo = dest.Repository;

            TryVerifyExistence(employeeId);

            try
            {
                RfcSessionManager.BeginContext(dest);

                IRfcFunction userChangeFn = repo.CreateFunction("BAPI_USER_CHANGE");
                //https://answers.sap.com/questions/7509568/jco-input-parameter-structures--bapiuserchange.html
                userChangeFn.SetValue("USERNAME", employeeId);

                var logonDataTable = userChangeFn.GetStructure("LOGONDATA");
                var logonDataTableX = userChangeFn.GetStructure("LOGONDATAX");

                if (!string.IsNullOrEmpty(validTo))
                {
                    logonDataTable.SetValue("GLTGB", validTo);
                }

                logonDataTableX.SetValue("GLTGB", "X");

                userChangeFn.SetValue("LOGONDATA", logonDataTable);
                userChangeFn.SetValue("LOGONDATAX", logonDataTableX);

                userChangeFn.Invoke(dest);

                var lockUserFn = repo.CreateFunction("BAPI_USER_LOCK");
                lockUserFn.SetValue("USERNAME", employeeId);
                lockUserFn.Invoke(dest);

                return lockUserFn.ToDataSet();

                // https://community.sailpoint.com/t5/IdentityNow-Wiki/IdentityNow-Rule-Guide-SAP-HR-Provisioning-Modify-Rule/ta-p/77306
                /*BAPI_EMPLOYEE_ENQUEUE

    HR_INFOTYPE_OPERATION

    BAPI_EMPLOYEE_DEQUEUE.

    BAPI_PERSDATA_CHANGE https://dev-workbench.com/en/sap-dictionary/function-module/bapi_persdata_change

    ,
                */
            }
            finally
            {
                RfcSessionManager.EndContext(dest); // BAPI_TRANSACTION_COMMIT
            }
        }

        /// <summary>
        /// Overi existenci zamestnance s danym id, pokud neexistuje, vyhodi se vyjimka.
        /// </summary>
        /// <param name="employeeId"></param>
        public void TryVerifyExistence(string employeeId)
        {
            RfcDestination dest = RfcDestinationManager.GetDestination("mySAPdestination");
            RfcRepository repo = dest.Repository;
            IRfcFunction getUserDetailFn = repo.CreateFunction("BAPI_USER_GET_DETAIL");
            getUserDetailFn.SetParameterActive("USERNAME", true);
            getUserDetailFn.SetValue("USERNAME", employeeId);
            getUserDetailFn.Invoke(dest);
            var response = getUserDetailFn.ToDataSet();
            if (response.Tables["RETURN"].Rows.Count > 0)
            {
                var message = response.Tables["RETURN"].Rows[0]["MESSAGE"].ToString();
                if (message.ToLowerInvariant() == $"User {employeeId} does not exist".ToLowerInvariant())
                {
                    throw new Exception(message);
                }
            }
        }

        /// <summary>
        /// Aktivuje zamestnance nastavenim Data platnosti do (GLTGB) na prazdnou hodnotu
        /// </summary>
        /// <param name="employeeId"></param>
        /// <returns></returns>
        public DataSet Activate(string employeeId, string validTo)
        {
            RfcDestination dest = RfcDestinationManager.GetDestination("mySAPdestination");

            RfcRepository repo = dest.Repository;

            TryVerifyExistence(employeeId);

            try
            {
                RfcSessionManager.BeginContext(dest);

                IRfcFunction userChangeFn = repo.CreateFunction("BAPI_USER_CHANGE");
                //https://answers.sap.com/questions/7509568/jco-input-parameter-structures--bapiuserchange.html
                userChangeFn.SetValue("USERNAME", employeeId);

                var logonDataTable = userChangeFn.GetStructure("LOGONDATA");
                var logonDataTableX = userChangeFn.GetStructure("LOGONDATAX");

                if (!string.IsNullOrEmpty(validTo))
                {
                    logonDataTable.SetValue("GLTGB", validTo);
                }
                else
                {
                    logonDataTable.SetValue("GLTGB", "");
                }

                logonDataTableX.SetValue("GLTGB", "X");

                userChangeFn.SetValue("LOGONDATA", logonDataTable);
                userChangeFn.SetValue("LOGONDATAX", logonDataTableX);

                userChangeFn.Invoke(dest);

                var lockUserFn = repo.CreateFunction("BAPI_USER_UNLOCK");
                lockUserFn.SetValue("USERNAME", employeeId);
                lockUserFn.Invoke(dest);

                return lockUserFn.ToDataSet();
            }
            finally
            {
                RfcSessionManager.EndContext(dest); // BAPI_TRANSACTION_COMMIT
            }
        }

        /// <summary>
        /// Zrusi zamestnance s danym id k danemu datu ukonceni pracovniho pomeru.
        /// </summary>
        /// <param name="employeeId"></param>
        /// <param name="dateOfTermination">Datum ukonceni pracovniho pomeru</param>
        /// <returns></returns>
        public DataSet Terminate(string employeeId, DateTime dateOfTermination)
        {
            RfcDestination dest = RfcDestinationManager.GetDestination("mySAPdestination");

            RfcRepository repo = dest.Repository;

            TryVerifyExistence(employeeId);

            try
            {
                RfcSessionManager.BeginContext(dest);

                IRfcFunction userChangeFn = repo.CreateFunction("BAPI_USER_CHANGE");
                //https://answers.sap.com/questions/7509568/jco-input-parameter-structures--bapiuserchange.html
                userChangeFn.SetValue("USERNAME", employeeId);

                var logonDataTable = userChangeFn.GetStructure("LOGONDATA");
                var logonDataTableX = userChangeFn.GetStructure("LOGONDATAX");

                logonDataTable.SetValue("GLTGB", dateOfTermination.ToString("yyyy-MM-dd"));
                logonDataTableX.SetValue("GLTGB", "X");

                userChangeFn.SetValue("LOGONDATA", logonDataTable);
                userChangeFn.SetValue("LOGONDATAX", logonDataTableX);

                userChangeFn.Invoke(dest);

                return userChangeFn.ToDataSet();
            }
            finally
            {
                RfcSessionManager.EndContext(dest); // BAPI_TRANSACTION_COMMIT
            }
        }

        /// <summary>
        /// Prida zamestnanci aplikacni roli na volitelne obdobi.
        /// </summary>
        /// <param name="employeeId"></param>
        /// <returns></returns>
        public DataSet AddApplicationRole(string employeeId, string applicationRole, string validFrom, string validTo)
        {
            RfcDestination dest = RfcDestinationManager.GetDestination("mySAPdestination");

            RfcRepository repo = dest.Repository;

            IRfcFunction getUserDetailFn = repo.CreateFunction("BAPI_USER_GET_DETAIL");
            getUserDetailFn.SetParameterActive("USERNAME", true);
            getUserDetailFn.SetValue("USERNAME", employeeId);
            getUserDetailFn.Invoke(dest);

            var response = getUserDetailFn.ToDataSet();

            var message = response.Tables["RETURN"].Rows[0]["MESSAGE"].ToString();

            if (message.ToString() == "External ID maintenance is no longer available")
            {
                //  return response;
            }

            if (message.ToLowerInvariant() == $"User {employeeId} does not exist".ToLowerInvariant())
            {
                SAPTools.Program.Log("Role " + applicationRole + "error: " + message);
                //throw new Exception(message);
            }

            try
            {
                RfcSessionManager.BeginContext(dest);

                var activitygroupsTable = getUserDetailFn.GetTable("ACTIVITYGROUPS");
                activitygroupsTable.Insert();
                var row = activitygroupsTable.CurrentRow;
                row.SetValue("AGR_NAME", applicationRole);
                if (!string.IsNullOrEmpty(validFrom))
                {
                    row.SetValue("FROM_DAT", validFrom);
                    //modify,ValidTo,string,99991231,20221231
                }
                else
                {
                    row.SetValue("FROM_DAT", DateTime.Now.ToString("yyyyMMdd"));

                }

                if (!string.IsNullOrEmpty(validTo))
                {
                    row.SetValue("TO_DAT", validTo);
                    //modify,ValidTo,string,99991231,20221231
                }
                else
                {
                    row.SetValue("TO_DAT", DateTime.Now.ToString("99991231"));
                }

                IRfcFunction userActGroupsAssignFn = repo.CreateFunction("BAPI_USER_ACTGROUPS_ASSIGN");
                userActGroupsAssignFn.SetValue("USERNAME", employeeId);
                userActGroupsAssignFn.SetValue("activitygroups", activitygroupsTable);

                userActGroupsAssignFn.Invoke(dest);

                response = userActGroupsAssignFn.ToDataSet();

                var messages = response.Tables["RETURN"].Rows.Cast<DataRow>().Select(r => r["MESSAGE"].ToString().Trim()).ToList();
                var msg = messages.FirstOrDefault(m => Regex.IsMatch(m, $"Role .+ does not exist"));
                if (msg != null)
                {
                    throw new Exception(msg);
                }

                return response;
            }
            finally
            {
                RfcSessionManager.EndContext(dest); // BAPI_TRANSACTION_COMMIT
            }
        }

        /// <summary>
        /// Prida zamestnanci aplikacni roli na volitelne obdobi.
        /// </summary>
        /// <param name="employeeId"></param>
        /// <returns></returns>
        public DataSet RemoveApplicationRole(string employeeId, string applicationRole)
        {
            RfcDestination dest = RfcDestinationManager.GetDestination("mySAPdestination");

            RfcRepository repo = dest.Repository;

            IRfcFunction getUserDetailFn = repo.CreateFunction("BAPI_USER_GET_DETAIL");
            getUserDetailFn.SetParameterActive("USERNAME", true);
            getUserDetailFn.SetValue("USERNAME", employeeId);
            getUserDetailFn.Invoke(dest);

            var response = getUserDetailFn.ToDataSet();

            if (response.Tables["RETURN"].Rows.Count > 0)
            {
                var message = response.Tables["RETURN"].Rows[0]["MESSAGE"].ToString();
                if (message.ToLowerInvariant() == $"User {employeeId} does not exist".ToLowerInvariant())
                {
                    throw new Exception(message);
                }
            }
            List<string> DependantRoles = new List<string>();

            if (!string.IsNullOrEmpty(Properties.Settings.Default.DependantRoles))
            {
                foreach (string role in Properties.Settings.Default.DependantRoles.Split(';'))
                {
                    DependantRoles.Add(role);
                }
            }

            try
            {
                RfcSessionManager.BeginContext(dest);


                var activitygroupsTable = getUserDetailFn.GetTable("ACTIVITYGROUPS");
                int currentMembershipCount = activitygroupsTable.RowCount;

                var rowsToRemove = new List<int>();
                var dependantRowsToRemove = new List<int>();
                for (int i = 0; i < activitygroupsTable.RowCount; i++)
                {
                    var row = activitygroupsTable[i];
                    string agrName = row.GetValue("AGR_NAME")?.ToString() ?? "";

                    if (DependantRoles.Contains(agrName))
                    {
                        dependantRowsToRemove.Add(i);
                        Program.Log("Zavisle role index: " + i.ToString() + " "+ agrName);
                    }

                    if (agrName.Equals(applicationRole))
                    {
                        rowsToRemove.Add(i);
                    }
                }

                if (currentMembershipCount - dependantRowsToRemove.Count - rowsToRemove.Count < 1)
                {
                    Program.Log("Odebirani zavislych roli");
                    foreach (int index in dependantRowsToRemove)
                    {
                        if (!rowsToRemove.Contains(index))
                            rowsToRemove.Add(index);
                    }
                }

                foreach (var index in rowsToRemove.OrderByDescending(i => i))
                {
                    activitygroupsTable.Delete(index);
                }
                Program.Log("Odebirano " + rowsToRemove.Count + " roli");
                IRfcFunction userActGroupsAssignFn = repo.CreateFunction("BAPI_USER_ACTGROUPS_ASSIGN");
                userActGroupsAssignFn.SetValue("USERNAME", employeeId);
                userActGroupsAssignFn.SetValue("activitygroups", activitygroupsTable);

                userActGroupsAssignFn.Invoke(dest);

                return userActGroupsAssignFn.ToDataSet();
            }
            finally
            {
                RfcSessionManager.EndContext(dest); // BAPI_TRANSACTION_COMMIT
            }
        }


        /// <summary>
        /// Prida zamestnanci aplikacni roli na volitelne obdobi.
        /// </summary>
        /// <param name="employeeId"></param>
        /// <returns></returns>
        public DataSet UpdateApplicationRole(string employeeId, string applicationRole, string validFrom, string validTo)
        {
            RfcDestination dest = RfcDestinationManager.GetDestination("mySAPdestination");

            RfcRepository repo = dest.Repository;

            IRfcFunction getUserDetailFn = repo.CreateFunction("BAPI_USER_GET_DETAIL");
            getUserDetailFn.SetParameterActive("USERNAME", true);
            getUserDetailFn.SetValue("USERNAME", employeeId);
            getUserDetailFn.Invoke(dest);

            var response = getUserDetailFn.ToDataSet();

            var message = response.Tables["RETURN"].Rows[0]["MESSAGE"].ToString();
            if (message.ToLowerInvariant() == $"User {employeeId} does not exist".ToLowerInvariant())
            {
                throw new Exception(message);
            }

            try
            {
                RfcSessionManager.BeginContext(dest);

                var activitygroupsTable = getUserDetailFn.GetTable("ACTIVITYGROUPS");
                var rowsToRemove = new List<int>();
                for (int i = 0; i < activitygroupsTable.RowCount; i++)
                {
                    var row = activitygroupsTable[i];
                    var agrName = row.GetValue("AGR_NAME")?.ToString() ?? "";
                    if (agrName.Equals(applicationRole))
                    {
                        if (!string.IsNullOrEmpty(validFrom))
                        {
                            row.SetValue("FROM_DAT", validFrom);
                            //modify,ValidTo,string,99991231,20221231
                        }
                        else
                        {
                            row.SetValue("FROM_DAT", DateTime.Now.ToString("yyyyMMdd"));

                        }

                        if (!string.IsNullOrEmpty(validTo))
                        {
                            row.SetValue("TO_DAT", validTo);
                            //modify,ValidTo,string,99991231,20221231
                        }
                        else
                        {
                            row.SetValue("TO_DAT", DateTime.Now.ToString("99991231"));
                        }
                    }
                }

                IRfcFunction userActGroupsAssignFn = repo.CreateFunction("BAPI_USER_ACTGROUPS_ASSIGN");
                userActGroupsAssignFn.SetValue("USERNAME", employeeId);
                userActGroupsAssignFn.SetValue("activitygroups", activitygroupsTable);

                userActGroupsAssignFn.Invoke(dest);

                return userActGroupsAssignFn.ToDataSet();
            }
            finally
            {
                RfcSessionManager.EndContext(dest); // BAPI_TRANSACTION_COMMIT
            }
        }


        /// <summary>
        /// Prida zamestnanci aplikacni roli na volitelne obdobi.
        /// </summary>
        /// <param name="employeeId"></param>
        /// <returns></returns>
        public string AssignApplicationRole(string employeeId, string applicationRole, string validFrom, string validTo)
        {
            RfcDestination dest = RfcDestinationManager.GetDestination("mySAPdestination");

            RfcRepository repo = dest.Repository;

            IRfcFunction getUserDetailFn = repo.CreateFunction("BAPI_USER_GET_DETAIL");
            getUserDetailFn.SetParameterActive("USERNAME", true);
            getUserDetailFn.SetValue("USERNAME", employeeId);
            getUserDetailFn.Invoke(dest);

            var response = getUserDetailFn.ToDataSet();

            if (response.Tables["RETURN"].Rows.Count > 0)
            {
                var message = response.Tables["RETURN"].Rows[0]["MESSAGE"].ToString();
                if (message.ToLowerInvariant() == $"User {employeeId} does not exist".ToLowerInvariant())
                {
                    return message;
                    //throw new Exception(message);
                }
            }




            try
            {
                /*
                                 for (int i = 0; i < activitygroupsTable.RowCount; i++)
                {
                    var row = activitygroupsTable[i];
                    var agrName = row.GetValue("AGR_NAME")?.ToString() ?? "";
                    if (agrName.Equals(applicationRole))
                    {
                        rowsToRemove.Add(i);
                    }
                }

                foreach (var index in rowsToRemove.OrderByDescending(i => i))
                {
                    activitygroupsTable.Delete(index);
                }
                 */

                RfcSessionManager.BeginContext(dest);

                var activitygroupsTable = getUserDetailFn.GetTable("ACTIVITYGROUPS");
                IRfcStructure row = null;
                //List<IRfcStructure> rows = new List<IRfcStructure>();
                //var rowsToRemove = new List<int>();
                for (int i = 0; i < activitygroupsTable.RowCount; i++)
                {

                    var agrName = activitygroupsTable[i].GetValue("AGR_NAME")?.ToString() ?? "";
                    if (agrName.Equals(applicationRole))
                    {
                        if (row != null)
                        {
                            Program.Log("Nalezena duplicitni role - odebirani role");
                            activitygroupsTable.Delete(i);

                        }
                        else
                        {
                            row = activitygroupsTable[i];
                        }
                        //rows.Add(activitygroupsTable[i]);
                    }
                }


                if (row == null)
                {

                    activitygroupsTable.Insert();
                    row = activitygroupsTable.CurrentRow;
                    row.SetValue("AGR_NAME", applicationRole);
                }

                if (!string.IsNullOrEmpty(validFrom))
                {
                    row.SetValue("FROM_DAT", validFrom);
                    //modify,ValidTo,string,99991231,20221231
                }
                else
                {
                    //row.SetValue("FROM_DAT", DateTime.Now.ToString("yyyyMMdd"));

                }

                if (!string.IsNullOrEmpty(validTo))
                {
                    row.SetValue("TO_DAT", validTo);
                    //modify,ValidTo,string,99991231,20221231
                }
                else
                {
                    row.SetValue("TO_DAT", DateTime.Now.ToString("99991231"));
                }

                IRfcFunction userActGroupsAssignFn = repo.CreateFunction("BAPI_USER_ACTGROUPS_ASSIGN");
                userActGroupsAssignFn.SetValue("USERNAME", employeeId);
                userActGroupsAssignFn.SetValue("activitygroups", activitygroupsTable);

                userActGroupsAssignFn.Invoke(dest);
                response = userActGroupsAssignFn.ToDataSet();
                var messages = response.Tables["RETURN"].Rows.Cast<DataRow>().Select(r => r["MESSAGE"].ToString().Trim()).ToList();
                string strmmsgs = "";
                if (messages != null && messages.Count > 0)
                {
                    strmmsgs = string.Join(Environment.NewLine, messages.ToArray());
                }
                //var msg = messages.FirstOrDefault(m => Regex.IsMatch(m, $"Role .+ does not exist"));
                //if (msg != null)
                //{
                //    //throw new Exception(msg);
                //    return msg;
                //}

                return strmmsgs;
            }
            finally
            {
                RfcSessionManager.EndContext(dest); // BAPI_TRANSACTION_COMMIT
            }
        }

        private DataSet Execute(Func<RfcRepository, DataSet> execute)
        {
            RfcDestination dest = RfcDestinationManager.GetDestination("mySAPdestination");

            RfcRepository repo = dest.Repository;

            try
            {
                return execute(repo);
            }
            finally
            {
                RfcSessionManager.EndContext(dest); // BAPI_TRANSACTION_COMMIT
            }
        }
    }
}
