using System;
using System.Collections.Generic;
using System.Linq;
using SAP.Middleware.Connector;

namespace SAPBAPI_Direct
{
    /// <summary>
    /// Primé BAPI volani. Chovani SEAS (defaults, ZRFCCALL, lock L/U) +
    /// OmniSync speciality (RETURN E/A, COMMIT, ASSIGN jako replace-all).
    /// </summary>
    internal sealed class SapRfc
    {
        private readonly RfcDestination _dest;
        private readonly HashSet<string> _dependantRoles;

        internal SapRfc(string destinationName, IEnumerable<string> dependantRoles)
        {
            _dest = RfcDestinationManager.GetDestination(destinationName);
            _dependantRoles = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (dependantRoles != null)
            {
                foreach (string role in dependantRoles)
                {
                    if (!string.IsNullOrWhiteSpace(role))
                        _dependantRoles.Add(role.Trim());
                }
            }
        }

        internal void Ping()
        {
            _dest.Ping();
        }

        internal List<string> GetAllUsernames()
        {
            IRfcFunction fn = _dest.Repository.CreateFunction("BAPI_USER_GETLIST");
            fn.Invoke(_dest);
            IRfcTable table = fn.GetTable("USERLIST");
            var result = new List<string>(table.RowCount);
            foreach (IRfcStructure row in table)
            {
                string name = GetString(row, "USERNAME");
                if (!string.IsNullOrWhiteSpace(name))
                    result.Add(name.Trim());
            }
            return result;
        }

        internal Person LoadPerson(string username)
        {
            IRfcFunction fn = GetUserDetail(username);
            var person = new Person(username);

            TryRead(fn, "ADDRESS", s =>
            {
                person.LastName = GetString(s, "LASTNAME");
                person.FirstName = GetString(s, "FIRSTNAME");
                person.FullName = GetString(s, "FULLNAME");
                person.NickName = GetString(s, "NICKNAME");
                person.EMail = GetString(s, "E_MAIL");
                person.DEPARTMENT = GetString(s, "DEPARTMENT");
                person.FUNCTION = GetString(s, "FUNCTION");
            });

            TryRead(fn, "LASTMODIFIED", s =>
            {
                string date = GetString(s, "MODDATE");
                string time = GetString(s, "MODTIME");
                person.ModDate = date;
                if (!string.IsNullOrEmpty(date) && !string.IsNullOrEmpty(time))
                    person.LastModificationDate = date + " " + time;
            });

            TryRead(fn, "LOGONDATA", s =>
            {
                person.ValidFrom = GetString(s, "GLTGV");
                person.ValidTo = GetString(s, "GLTGB");
                person.CODVN = GetString(s, "CODVN");
                person.LTime = GetString(s, "LTIME");
                person.USERGROUP = GetString(s, "CLASS");
                if (string.IsNullOrEmpty(person.USERGROUP))
                    person.USERGROUP = "X";
            });

            TryRead(fn, "ISLOCKED", s =>
            {
                person.WRNG_LOGON = GetString(s, "WRNG_LOGON");
                person.LOCAL_LOCK = GetString(s, "LOCAL_LOCK");
                person.GLOB_LOCK = GetString(s, "GLOB_LOCK");
                person.NO_USER_PW = GetString(s, "NO_USER_PW");
            });

            TryRead(fn, "ALIAS", s => { person.ALIAS = GetString(s, "USERALIAS"); });

            TryRead(fn, "DEFAULTS", s =>
            {
                person.SPLD = GetString(s, "SPLD");
                person.SPDB = GetString(s, "SPDB");
                person.LANGU = GetString(s, "LANGU");
            });

            TryReadTable(fn, "ACTIVITYGROUPS", table =>
            {
                var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                foreach (IRfcStructure row in table)
                {
                    string agr = GetString(row, "AGR_NAME");
                    if (string.IsNullOrEmpty(agr))
                        continue;

                    string orgFlag = GetString(row, "ORG_FLAG");
                    if (string.Equals(orgFlag, "C", StringComparison.OrdinalIgnoreCase))
                        continue;

                    if (!seen.Add(agr))
                        continue;

                    person.UserGroups.Add(new Group
                    {
                        EmployeeID = username,
                        FullName = agr,
                        ValidFrom = GetString(row, "FROM_DAT"),
                        ValidTo = GetString(row, "TO_DAT"),
                        Description = GetString(row, "AGR_TEXT"),
                        AssignmentType = orgFlag
                    });
                }
            });

            person.GroupsCount = person.UserGroups.Count;
            return person;
        }

        internal void CreatePerson(Person person)
        {
            IRfcFunction fn = _dest.Repository.CreateFunction("BAPI_USER_CREATE1");
            fn.SetValue("USERNAME", person.EmployeeID);

            IRfcStructure address = fn.GetStructure("ADDRESS");
            address.SetValue("FIRSTNAME", person.FirstName ?? "");
            address.SetValue("LASTNAME", person.LastName ?? "");
            address.SetValue("E_MAIL", person.EMail ?? "");
            address.SetValue("LANGUP_ISO", "SK");
            if (!string.IsNullOrEmpty(person.DEPARTMENT))
                address.SetValue("DEPARTMENT", person.DEPARTMENT);
            if (!string.IsNullOrEmpty(person.FUNCTION))
                address.SetValue("FUNCTION", person.FUNCTION);

            IRfcStructure defaults = fn.GetStructure("DEFAULTS");
            defaults.SetValue("SPLD", string.IsNullOrEmpty(person.SPLD) ? "LOCM" : person.SPLD);
            defaults.SetValue("SPDB", string.IsNullOrEmpty(person.SPDB) ? "G" : person.SPDB);
            defaults.SetValue("LANGU", string.IsNullOrEmpty(person.LANGU) ? "Q" : person.LANGU);

            IRfcStructure logon = fn.GetStructure("LOGONDATA");
            logon.SetValue("GLTGV", ToSapDate(person.ValidFrom, DateTime.Now.ToString("yyyy-MM-dd")));
            logon.SetValue("GLTGB", ToSapDate(person.ValidTo, "9999-12-31"));
            if (!string.IsNullOrEmpty(person.USERGROUP) && person.USERGROUP != "X")
                logon.SetValue("CLASS", person.USERGROUP);

            if (!string.IsNullOrEmpty(person.ALIAS))
            {
                IRfcStructure alias = fn.GetStructure("ALIAS");
                alias.SetValue("USERALIAS", person.ALIAS);
            }

            IRfcStructure password = fn.GetStructure("PASSWORD");
            if (!string.IsNullOrEmpty(person.Password) && !string.Equals(person.Password, "x", StringComparison.OrdinalIgnoreCase))
            {
                password.SetValue("BAPIPWD", person.Password);
            }
            else
            {
                logon.SetValue("CODVN", "X");
                password.SetValue("BAPIPWD", string.Empty);
            }

            fn.Invoke(_dest);
            BapiReturn.Check(fn, "BAPI_USER_CREATE1[" + person.EmployeeID + "]");
            BapiReturn.Commit(_dest);
        }

        internal void UpdatePerson(Person person)
        {
            EnsureUserExists(person.EmployeeID);

            IRfcFunction fn = _dest.Repository.CreateFunction("BAPI_USER_CHANGE");
            fn.SetValue("USERNAME", person.EmployeeID);

            IRfcStructure address = fn.GetStructure("ADDRESS");
            IRfcStructure addressX = fn.GetStructure("ADDRESSX");
            SetChanged(address, addressX, "FIRSTNAME", person.FirstName);
            SetChanged(address, addressX, "LASTNAME", person.LastName);
            SetChanged(address, addressX, "E_MAIL", person.EMail);
            SetChanged(address, addressX, "FUNCTION", person.FUNCTION);
            SetChanged(address, addressX, "DEPARTMENT", person.DEPARTMENT);

            if (person.ALIAS != null)
            {
                IRfcStructure alias = fn.GetStructure("ALIAS");
                IRfcStructure aliasX = fn.GetStructure("ALIASX");
                alias.SetValue("USERALIAS", person.ALIAS);
                aliasX.SetValue("BAPIALIAS", "X");
            }

            IRfcStructure logon = fn.GetStructure("LOGONDATA");
            IRfcStructure logonX = fn.GetStructure("LOGONDATAX");
            if (!string.IsNullOrEmpty(person.ValidTo))
            {
                logon.SetValue("GLTGB", ToSapDate(person.ValidTo, person.ValidTo));
                logonX.SetValue("GLTGB", "X");
            }
            if (!string.IsNullOrEmpty(person.ValidFrom))
            {
                logon.SetValue("GLTGV", ToSapDate(person.ValidFrom, person.ValidFrom));
                logonX.SetValue("GLTGV", "X");
            }
            if (!string.IsNullOrEmpty(person.USERGROUP))
            {
                logon.SetValue("CLASS", person.USERGROUP == "X" ? "" : person.USERGROUP);
                logonX.SetValue("CLASS", "X");
            }

            if (!string.IsNullOrEmpty(person.Password) && !string.Equals(person.Password, "x", StringComparison.OrdinalIgnoreCase))
            {
                IRfcStructure pwd = fn.GetStructure("PASSWORD");
                IRfcStructure pwdX = fn.GetStructure("PASSWORDX");
                pwd.SetValue("BAPIPWD", person.Password);
                pwdX.SetValue("BAPIPWD", "X");
            }

            fn.Invoke(_dest);
            BapiReturn.Check(fn, "BAPI_USER_CHANGE[" + person.EmployeeID + "]");

            if (person.GLOB_LOCK == "L" || person.LOCAL_LOCK == "L")
                LockUser(person.EmployeeID, person.ValidTo, lockAccount: true);
            else if (person.LOCAL_LOCK == "U")
                LockUser(person.EmployeeID, person.ValidTo, lockAccount: false);

            BapiReturn.Commit(_dest);
            SyncDependantRolesAfterUserChange(person.EmployeeID);
        }

        internal void AssignRole(string employeeId, string roleName, string validFrom, string validTo)
        {
            EnsureUserExists(employeeId);
            Dictionary<string, RoleRow> current = ReadRoles(employeeId);

            RoleRow row;
            if (!current.TryGetValue(roleName, out row))
                row = new RoleRow { Name = roleName };

            if (!string.IsNullOrEmpty(validFrom))
                row.From = ToSapDate(validFrom, validFrom);
            else if (string.IsNullOrEmpty(row.From))
                row.From = DateTime.Now.ToString("yyyy-MM-dd");

            row.To = ToSapDate(validTo, "9999-12-31");
            current[roleName] = row;

            foreach (string dep in _dependantRoles)
            {
                if (!current.ContainsKey(dep))
                {
                    current[dep] = new RoleRow
                    {
                        Name = dep,
                        From = DateTime.Now.ToString("yyyy-MM-dd"),
                        To = "9999-12-31"
                    };
                }
            }

            WriteRoles(employeeId, current.Values);
            BapiReturn.Commit(_dest);
            EnsureRolePresent(employeeId, roleName);
        }

        internal void RemoveRole(string employeeId, string roleName)
        {
            EnsureUserExists(employeeId);
            Dictionary<string, RoleRow> current = ReadRoles(employeeId);
            current.Remove(roleName);

            int remainingReal = current.Keys.Count(k => !_dependantRoles.Contains(k));
            if (remainingReal < 1)
            {
                foreach (string dep in _dependantRoles.ToArray())
                    current.Remove(dep);
            }

            WriteRoles(employeeId, current.Values);
            BapiReturn.Commit(_dest);
        }

        private void SyncDependantRolesAfterUserChange(string employeeId)
        {
            if (_dependantRoles.Count == 0)
                return;

            Dictionary<string, RoleRow> current = ReadRoles(employeeId);
            bool hasReal = current.Keys.Any(k => !_dependantRoles.Contains(k));
            if (hasReal)
                return;

            bool changed = false;
            foreach (string dep in _dependantRoles.ToArray())
            {
                if (current.Remove(dep))
                    changed = true;
            }
            if (!changed)
                return;

            WriteRoles(employeeId, current.Values);
            BapiReturn.Commit(_dest);
        }

        private void LockUser(string employeeId, string validTo, bool lockAccount)
        {
            IRfcFunction change = _dest.Repository.CreateFunction("BAPI_USER_CHANGE");
            change.SetValue("USERNAME", employeeId);
            IRfcStructure logon = change.GetStructure("LOGONDATA");
            IRfcStructure logonX = change.GetStructure("LOGONDATAX");
            if (!string.IsNullOrEmpty(validTo))
                logon.SetValue("GLTGB", ToSapDate(validTo, validTo));
            else if (!lockAccount)
                logon.SetValue("GLTGB", "");
            logonX.SetValue("GLTGB", "X");
            change.Invoke(_dest);
            BapiReturn.Check(change, "BAPI_USER_CHANGE lock[" + employeeId + "]");

            IRfcFunction lockFn = _dest.Repository.CreateFunction(lockAccount ? "BAPI_USER_LOCK" : "BAPI_USER_UNLOCK");
            lockFn.SetValue("USERNAME", employeeId);
            lockFn.Invoke(_dest);
            BapiReturn.Check(lockFn, (lockAccount ? "BAPI_USER_LOCK[" : "BAPI_USER_UNLOCK[") + employeeId + "]");
        }

        private void WriteRoles(string employeeId, IEnumerable<RoleRow> roles)
        {
            IRfcFunction fn = _dest.Repository.CreateFunction("BAPI_USER_ACTGROUPS_ASSIGN");
            fn.SetValue("USERNAME", employeeId);
            IRfcTable table = fn.GetTable("ACTIVITYGROUPS");
            foreach (RoleRow role in roles)
            {
                table.Append();
                IRfcStructure row = table.CurrentRow;
                row.SetValue("AGR_NAME", role.Name);
                if (!string.IsNullOrEmpty(role.From))
                    row.SetValue("FROM_DAT", ToSapDate(role.From, role.From));
                row.SetValue("TO_DAT", ToSapDate(role.To, "9999-12-31"));
            }
            fn.Invoke(_dest);
            BapiReturn.Check(fn, "BAPI_USER_ACTGROUPS_ASSIGN[" + employeeId + "]");
        }

        private Dictionary<string, RoleRow> ReadRoles(string employeeId)
        {
            IRfcFunction fn = GetUserDetail(employeeId);
            var result = new Dictionary<string, RoleRow>(StringComparer.OrdinalIgnoreCase);
            TryReadTable(fn, "ACTIVITYGROUPS", table =>
            {
                foreach (IRfcStructure row in table)
                {
                    string name = GetString(row, "AGR_NAME");
                    if (string.IsNullOrEmpty(name))
                        continue;
                    result[name] = new RoleRow
                    {
                        Name = name,
                        From = GetString(row, "FROM_DAT"),
                        To = GetString(row, "TO_DAT")
                    };
                }
            });
            return result;
        }

        private void EnsureRolePresent(string employeeId, string roleName)
        {
            Dictionary<string, RoleRow> current = ReadRoles(employeeId);
            if (!current.ContainsKey(roleName))
            {
                throw new InvalidOperationException(
                    "Po BAPI_USER_ACTGROUPS_ASSIGN neni role '" + roleName +
                    "' u uzivatele '" + employeeId + "'. Zkontrolujte AGR_NAME v SAP.");
            }
        }

        private void EnsureUserExists(string employeeId)
        {
            IRfcFunction fn = GetUserDetail(employeeId);
            try
            {
                IRfcTable ret = fn.GetTable("RETURN");
                foreach (IRfcStructure row in ret)
                {
                    string message = GetString(row, "MESSAGE");
                    if (!string.IsNullOrEmpty(message)
                        && message.IndexOf("does not exist", StringComparison.OrdinalIgnoreCase) >= 0)
                    {
                        throw new InvalidOperationException(message);
                    }
                }
            }
            catch (InvalidOperationException)
            {
                throw;
            }
            catch
            {
            }
        }

        private IRfcFunction GetUserDetail(string username)
        {
            IRfcFunction fn = _dest.Repository.CreateFunction("BAPI_USER_GET_DETAIL");
            fn.SetParameterActive("USERNAME", true);
            fn.SetValue("USERNAME", username);
            fn.Invoke(_dest);
            return fn;
        }

        private static void SetChanged(IRfcStructure data, IRfcStructure flag, string field, string value)
        {
            if (string.IsNullOrEmpty(value))
                return;
            data.SetValue(field, value);
            flag.SetValue(field, "X");
        }

        private static void TryRead(IRfcFunction fn, string name, Action<IRfcStructure> read)
        {
            try
            {
                read(fn.GetStructure(name));
            }
            catch (RfcBaseException)
            {
            }
        }

        private static void TryReadTable(IRfcFunction fn, string name, Action<IRfcTable> read)
        {
            try
            {
                read(fn.GetTable(name));
            }
            catch (RfcBaseException)
            {
            }
        }

        private static string GetString(IRfcStructure row, string field)
        {
            try
            {
                object v = row.GetValue(field);
                return v == null ? "" : v.ToString().Trim().Trim('\0');
            }
            catch
            {
                return "";
            }
        }

        /// <summary>
        /// SEAS BAPI zapisuje DATS jako yyyy-MM-dd. MIM muze poslat ISO nebo yyyyMMdd.
        /// </summary>
        private static string ToSapDate(string value, string fallback)
        {
            if (string.IsNullOrWhiteSpace(value))
                return fallback;

            value = value.Trim();
            if (value.Length >= 10 && value[4] == '-' && value[7] == '-')
                return value.Substring(0, 10);

            string digits = value.Length >= 8 ? value.Substring(0, 8) : value;
            if (digits.Length == 8 && IsAllDigits(digits))
                return digits.Substring(0, 4) + "-" + digits.Substring(4, 2) + "-" + digits.Substring(6, 2);

            DateTime dt;
            if (DateTime.TryParse(value, out dt))
                return dt.ToString("yyyy-MM-dd");

            return fallback;
        }

        private static bool IsAllDigits(string value)
        {
            for (int i = 0; i < value.Length; i++)
            {
                if (value[i] < '0' || value[i] > '9')
                    return false;
            }
            return true;
        }

        private sealed class RoleRow
        {
            public string Name;
            public string From;
            public string To;
        }
    }
}
