using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace SAPBAPI_Direct
{
    /// <summary>
    /// Kompaktni index clenstvi pro Delta. XML se nepouziva.
    /// Radek: USERNAME|AGR_NAME|FROM|TO  nebo  USERNAME||  (ucet bez filtrovanych roli).
    /// </summary>
    internal sealed class RoleIndex
    {
        internal readonly Dictionary<string, Dictionary<string, RoleDates>> Users =
            new Dictionary<string, Dictionary<string, RoleDates>>(StringComparer.OrdinalIgnoreCase);

        internal static string FilePath(string xmlPath)
        {
            if (string.IsNullOrWhiteSpace(xmlPath))
                return null;

            string dir = Path.GetDirectoryName(xmlPath);
            string name = Path.GetFileNameWithoutExtension(xmlPath);
            if (string.IsNullOrEmpty(name))
                name = "SAP_BAPI";

            return string.IsNullOrEmpty(dir)
                ? name + ".roles.idx"
                : Path.Combine(dir, name + ".roles.idx");
        }

        internal static RoleIndex Load(string path)
        {
            if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
                return null;

            var index = new RoleIndex();
            foreach (string raw in File.ReadAllLines(path, Encoding.UTF8))
            {
                if (string.IsNullOrWhiteSpace(raw) || raw[0] == '#')
                    continue;

                string[] parts = raw.Split('|');
                if (parts.Length < 1)
                    continue;

                string user = parts[0].Trim();
                if (user.Length == 0)
                    continue;

                Dictionary<string, RoleDates> roles;
                if (!index.Users.TryGetValue(user, out roles))
                {
                    roles = new Dictionary<string, RoleDates>(StringComparer.OrdinalIgnoreCase);
                    index.Users[user] = roles;
                }

                string role = parts.Length > 1 ? parts[1].Trim() : "";
                if (role.Length == 0)
                    continue;

                roles[role] = new RoleDates
                {
                    From = parts.Length > 2 ? parts[2].Trim() : "",
                    To = parts.Length > 3 ? parts[3].Trim() : ""
                };
            }

            return index;
        }

        internal static RoleIndex FromPersons(IEnumerable<Person> persons, HashSet<string> bapiRoles)
        {
            var index = new RoleIndex();
            if (persons == null)
                return index;

            foreach (Person person in persons)
            {
                if (person == null || string.IsNullOrWhiteSpace(person.EmployeeID))
                    continue;

                var roles = new Dictionary<string, RoleDates>(StringComparer.OrdinalIgnoreCase);
                if (person.UserGroups != null)
                {
                    foreach (Group group in person.UserGroups)
                    {
                        if (string.IsNullOrWhiteSpace(group.FullName))
                            continue;
                        if (bapiRoles != null && !bapiRoles.Contains(group.FullName))
                            continue;

                        roles[group.FullName] = new RoleDates
                        {
                            From = group.ValidFrom ?? "",
                            To = group.ValidTo ?? ""
                        };
                    }
                }

                index.Users[person.EmployeeID] = roles;
            }

            return index;
        }

        internal void Save(string path)
        {
            string dir = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                Directory.CreateDirectory(dir);

            using (var writer = new StreamWriter(path, false, Encoding.UTF8))
            {
                foreach (KeyValuePair<string, Dictionary<string, RoleDates>> user in Users)
                {
                    if (user.Value.Count == 0)
                    {
                        writer.Write(user.Key);
                        writer.WriteLine("||");
                        continue;
                    }

                    foreach (KeyValuePair<string, RoleDates> role in user.Value)
                    {
                        writer.Write(user.Key);
                        writer.Write('|');
                        writer.Write(role.Key);
                        writer.Write('|');
                        writer.Write(role.Value.From ?? "");
                        writer.Write('|');
                        writer.Write(role.Value.To ?? "");
                        writer.WriteLine();
                    }
                }
            }
        }

        internal static List<RoleChange> Diff(RoleIndex previous, RoleIndex current)
        {
            var changes = new List<RoleChange>();
            if (current == null)
                return changes;

            if (previous == null)
            {
                foreach (KeyValuePair<string, Dictionary<string, RoleDates>> user in current.Users)
                {
                    foreach (KeyValuePair<string, RoleDates> role in user.Value)
                        changes.Add(RoleChange.Add(user.Key, role.Key, role.Value));
                }
                return changes;
            }

            foreach (KeyValuePair<string, Dictionary<string, RoleDates>> user in current.Users)
            {
                Dictionary<string, RoleDates> oldRoles;
                if (!previous.Users.TryGetValue(user.Key, out oldRoles))
                    oldRoles = null;

                foreach (KeyValuePair<string, RoleDates> role in user.Value)
                {
                    RoleDates old;
                    if (oldRoles == null || !oldRoles.TryGetValue(role.Key, out old))
                    {
                        changes.Add(RoleChange.Add(user.Key, role.Key, role.Value));
                        continue;
                    }

                    if (!Same(old.From, role.Value.From) || !Same(old.To, role.Value.To))
                        changes.Add(RoleChange.Update(user.Key, role.Key, role.Value));
                }
            }

            foreach (KeyValuePair<string, Dictionary<string, RoleDates>> user in previous.Users)
            {
                Dictionary<string, RoleDates> now;
                current.Users.TryGetValue(user.Key, out now);
                foreach (KeyValuePair<string, RoleDates> role in user.Value)
                {
                    if (now == null || !now.ContainsKey(role.Key))
                        changes.Add(RoleChange.Delete(user.Key, role.Key));
                }
            }

            return changes;
        }

        private static bool Same(string a, string b)
        {
            return string.Equals((a ?? "").Trim(), (b ?? "").Trim(), StringComparison.OrdinalIgnoreCase);
        }
    }

    internal sealed class RoleDates
    {
        public string From;
        public string To;
    }

    internal enum RoleChangeKind
    {
        Add,
        Update,
        Delete
    }

    internal sealed class RoleChange
    {
        public RoleChangeKind Kind;
        public string User;
        public string Role;
        public string From;
        public string To;

        internal static RoleChange Add(string user, string role, RoleDates dates)
        {
            return new RoleChange
            {
                Kind = RoleChangeKind.Add,
                User = user,
                Role = role,
                From = dates.From,
                To = dates.To
            };
        }

        internal static RoleChange Update(string user, string role, RoleDates dates)
        {
            return new RoleChange
            {
                Kind = RoleChangeKind.Update,
                User = user,
                Role = role,
                From = dates.From,
                To = dates.To
            };
        }

        internal static RoleChange Delete(string user, string role)
        {
            return new RoleChange
            {
                Kind = RoleChangeKind.Delete,
                User = user,
                Role = role
            };
        }
    }
}
