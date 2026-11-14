using SAP.Middleware.Connector;
using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace SAPLibrary
{
    public class Employees
    {
        public DataTable GetAll()
        {
            RfcDestination dest = RfcDestinationManager.GetDestination("mySAPdestination");

            RfcRepository repo = dest.Repository;

            IRfcFunction testfn = repo.CreateFunction("BAPI_USER_GETLIST");
            testfn.Invoke(dest);

            var employeesList = testfn.GetTable("USERLIST");

            // companyCodeList now contains a table with companies and codes
            return employeesList.ToDataTable("Employeees");
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

        public DataSet Create(string employeeId, string firstName, string lastName, string email, string password, DateTime validFrom, DateTime? validThrough)
        {
            RfcDestination dest = RfcDestinationManager.GetDestination("mySAPdestination");

            RfcRepository repo = dest.Repository;

            IRfcFunction getUserDetailFn = repo.CreateFunction("BAPI_USER_GET_DETAIL");
            getUserDetailFn.SetParameterActive("USERNAME", true);
            getUserDetailFn.SetValue("USERNAME", employeeId);

            var addressTable = getUserDetailFn.GetStructure("ADDRESS");
            
            try
            {
                RfcSessionManager.BeginContext(dest);

                IRfcFunction userCreateFn = repo.CreateFunction("BAPI_USER_CREATE1");
                userCreateFn.SetValue("USERNAME", employeeId);

                var passwordTable = userCreateFn.GetStructure("PASSWORD");
                passwordTable.SetValue("BAPIPWD", password);

                addressTable.SetValue("FIRSTNAME", firstName);
                addressTable.SetValue("LASTNAME", lastName);
                addressTable.SetValue("E_MAIL", email);
                //addressTable.SetValue("PASSWORD", "X");
                addressTable.SetValue("LANGUP_ISO", "SK");

                var logonDataTable = userCreateFn.GetStructure("LOGONDATA");
                logonDataTable.SetValue("GLTGV", validFrom.ToString("yyyy-MM-dd"));
                if (validThrough.HasValue)
                {
                    logonDataTable.SetValue("GLTGB", validThrough.Value.ToString("yyyy-MM-dd"));
                }
                else
                {
                    logonDataTable.SetValue("GLTGB", "");
                }

                userCreateFn.SetValue("ADDRESS", addressTable);
                userCreateFn.SetValue("PASSWORD", passwordTable);
                userCreateFn.SetValue("LOGONDATA", logonDataTable);

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
            finally
            {
                RfcSessionManager.EndContext(dest); // BAPI_TRANSACTION_COMMIT
            }
        }

        public DataSet Update(string employeeId, string firstName, string lastName, string email, string initialPassword, DateTime validFrom, DateTime? validThrough)
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
                addressTableX.SetValue("FIRSTNAME", "X");
                addressTableX.SetValue("LASTNAME", "X");
                addressTableX.SetValue("E_MAIL", "X");

                addressTable.SetValue("FIRSTNAME", firstName);
                addressTable.SetValue("LASTNAME", lastName);
                addressTable.SetValue("E_MAIL", email);

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

                userChangeFn.SetValue("ADDRESS", addressTable);
                userChangeFn.SetValue("ADDRESSX", addressTableX);
                userChangeFn.SetValue("LOGONDATA", logonDataTable);
                userChangeFn.SetValue("LOGONDATAX", logonDataTableX);

                userChangeFn.Invoke(dest);

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

        public DataSet Deactivate(string employeeId, DateTime? validThrough)
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
                
                if (validThrough.HasValue)
                {
                    logonDataTable.SetValue("GLTGB", validThrough.Value.ToString("yyyy-MM-dd"));
                }
                else
                {
                    logonDataTable.SetValue("GLTGB", "");
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

            var message = response.Tables["RETURN"].Rows[0]["MESSAGE"].ToString();
            if (message.ToLowerInvariant() == $"User {employeeId} does not exist".ToLowerInvariant())
            {
                throw new Exception(message);
            }
        }

        /// <summary>
        /// Aktivuje zamestnance nastavenim Data platnosti do (GLTGB) na prazdnou hodnotu
        /// </summary>
        /// <param name="employeeId"></param>
        /// <returns></returns>
        public DataSet Activate(string employeeId, DateTime? activationDate)
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

                if (activationDate.HasValue)
                {
                    logonDataTable.SetValue("GLTGB", activationDate.Value.ToString("yyyy-MM-dd"));
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
        public DataSet AddApplicationRole(string employeeId, string applicationRole, DateTime? from, DateTime? to)
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
                activitygroupsTable.Insert();
                var row = activitygroupsTable.CurrentRow;
                row.SetValue("AGR_NAME", applicationRole);
                if (from.HasValue)
                {
                    row.SetValue("FROM_DAT", from.Value.ToString("yyyy-MM-dd"));
                }

                if (to.HasValue)
                {
                    row.SetValue("TO_DAT", to.Value.ToString("yyyy-MM-dd"));
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
                        rowsToRemove.Add(i);
                    }
                }

                foreach (var index in rowsToRemove.OrderByDescending(i => i))
                {
                    activitygroupsTable.Delete(index);
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
