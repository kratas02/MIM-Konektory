using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace OracleECMA2MA
{
    /// <summary>
    /// Jeden radek PPV pro vypocet roli: fakulta, zkratka strediska, kod strediska.
    /// </summary>
    public sealed class PpvOrgRow
    {
        public string Faculty;
        public string StrediskoZkratka;
        public string StrediskoKod;
    }

    /// <summary>
    /// Vypoctena role (ApplicationRole / BusinessRole). AccountName je anchor.
    /// </summary>
    public sealed class DerivedRole
    {
        public string AccountName;
        public string Faculty;
        public string StrediskoZkratka;
        public string DistinguishedNamePart;
        public bool   IsParent;
        public bool   Vyjimka;
    }

    /// <summary>
    /// Z unikatnich kombinaci PPV (FAKULTA + STRED_KMEN_ZKRATKA) sestavi role.
    ///
    /// List (nejnizsi uroven, isParent=false), pro kazdou kombinaci:
    ///   ROL-{fakulta}_{zkratka}_{suffix}
    ///   DN: OU={zkratka},OU={fakulta}
    ///   REK v nazvu role je "Rektorat", v OU "REKTORAT".
    ///   Zkratka s pomlckou se do nazvu bere jen jako text za posledni pomlckou
    ///   (FTZ-AS → ROL-FTZ_AS_{suffix}). DistinguishedNamePart zustava cely,
    ///   vcetne diakritiky. Z nazvu a z atributu claFaculty / claStrediskoZkratkaPPV
    ///   se diakritika odebere (FŽP → FZP).
    ///   Suffix vedouci se u techto listovych skupin nevytvari
    ///   (ROL-{fakulta}_{zkratka}_vedouci). Vyjimky ho maji dal.
    ///   Zkratka DEK se v nazvu pise DEKANAT u kazde fakulty
    ///   (ROL-{fakulta}_DEKANAT_{suffix}).
    ///   DistinguishedNamePart a claStrediskoZkratkaPPV zustavaji DEK.
    ///
    /// Pokud zkratka obsahuje '-' a stejny prefix (text pred prvni pomlckou) ma
    /// v ramci fakulty vic nez jednu zkratku, vlozi se dalsi uroven OU:
    ///   OU=OIKT-OPS,OU=OIKT,OU=REKTORAT
    /// Jedina zkratka s danym prefixem zustava bez mezilehle OU.
    /// Meziuroven (OIKT) nema vlastni role — mění jen DistinguishedNamePart listu.
    ///
    /// Nadrazene role (isParent=true), jedna sada na fakultu, ktera ma aspon jednu zkratku:
    ///   ROL-{fakulta}_{suffix}
    ///   DN: OU={fakulta}  (REK → OU=REKTORAT)
    ///
    /// Vyjimka jen pro REK, podle prefixu STRED_KMEN_KOD. Sada listovych roli
    /// ROL-{vyjimka}_{suffix}, DN OU={vyjimka}, bez nadrazenych skupin.
    /// 996→KAM, 994→STATKY, 92→LIB, 8→VIN, 7→KTV (delsi prefix ma prednost).
    /// Radek vyjimky se nepocita jako bezna zkratka rektoratu, takze
    /// kod 7… nevytvori jeste ROL-Rektorat_KTV_{suffix}.
    ///
    /// BusinessRole = stejny vypocet, ale jen isParent=false a suffix
    /// zamestnanec / technik / pedagog (list strediska i vyjimka).
    /// </summary>
    public static class RoleBuilder
    {
        private static readonly string[] LeafSuffixes =
        {
            "zastupceVT", "zamestnanec", "vedouci", "vedeni", "technik",
            "tajemnik", "sekretariat", "pedagog", "externista", "clen",
        };

        private static readonly string[] ParentSuffixes =
        {
            "zamestnanec", "vedouci_vsichni", "vedeni", "vedecka_rada", "technik",
            "tajemnik", "tajemnici", "sekretariat", "prodekan", "pedagog",
            "externista", "doktorand", "dekan", "clen", "zastupceVT",
        };

        private static readonly HashSet<string> BusinessSuffixes =
            new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                "zamestnanec", "technik", "pedagog",
            };

        // Delsi prefix prvni.
        private static readonly string[][] ExceptionRules =
        {
            new[] { "996", "KAM" },
            new[] { "994", "STATKY" },
            new[] { "92",  "LIB" },
            new[] { "8",   "VIN" },
            new[] { "7",   "KTV" },
        };

        public static List<DerivedRole> Build(IEnumerable<PpvOrgRow> rows, bool businessOnly)
        {
            var faculties = new Dictionary<string, FacultyGroup>(StringComparer.OrdinalIgnoreCase);
            if (rows != null)
            {
                foreach (PpvOrgRow row in rows)
                {
                    if (row == null) continue;
                    string faculty = NormalizeCode(row.Faculty);
                    if (faculty == null) continue;

                    FacultyGroup group;
                    if (!faculties.TryGetValue(faculty, out group))
                    {
                        group = new FacultyGroup(faculty);
                        faculties.Add(faculty, group);
                    }

                    string zkratka = NormalizeCode(row.StrediskoZkratka);
                    string vyjimka = IsRek(faculty) ? MatchVyjimka(row.StrediskoKod) : null;
                    if (vyjimka != null)
                        group.Vyjimky.Add(vyjimka);
                    else if (zkratka != null)
                        group.Zkratky[zkratka] = zkratka;
                }
            }

            var result = new List<DerivedRole>();
            var seen   = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            var facultyKeys = new List<string>(faculties.Keys);
            facultyKeys.Sort(StringComparer.OrdinalIgnoreCase);

            foreach (string facultyKey in facultyKeys)
            {
                FacultyGroup group = faculties[facultyKey];
                string token = StripDiacritics(FacultyToken(group.Faculty));
                string facultyAttr = StripDiacritics(group.Faculty);
                string ou    = FacultyOu(group.Faculty);
                var prefixCounts = CountHyphenPrefixes(group);

                var zkratky = new List<string>(group.Zkratky.Keys);
                zkratky.Sort(StringComparer.OrdinalIgnoreCase);

                foreach (string zkratka in zkratky)
                {
                    string dn = LeafDn(zkratka, ou, prefixCounts);
                    string nameZkratka = NameZkratkaToken(StripDiacritics(NameZkratka(zkratka)));
                    foreach (string suffix in LeafSuffixes)
                    {
                        // ApplicationRole: ROL-{fakulta}_{zkratka}_vedouci se nevytvari.
                        if (suffix.Equals("vedouci", StringComparison.OrdinalIgnoreCase))
                            continue;

                        AddRole(result, seen, businessOnly, new DerivedRole
                        {
                            AccountName           = "ROL-" + token + "_" + nameZkratka + "_" + suffix,
                            Faculty               = facultyAttr,
                            StrediskoZkratka      = StripDiacritics(zkratka),
                            DistinguishedNamePart = dn,
                            IsParent              = false,
                            Vyjimka               = false,
                        }, suffix);
                    }
                }

                if (zkratky.Count > 0)
                {
                    foreach (string suffix in ParentSuffixes)
                    {
                        AddRole(result, seen, businessOnly, new DerivedRole
                        {
                            AccountName           = "ROL-" + token + "_" + suffix,
                            Faculty               = facultyAttr,
                            StrediskoZkratka      = null,
                            DistinguishedNamePart = "OU=" + ou,
                            IsParent              = true,
                            Vyjimka               = false,
                        }, suffix);
                    }
                }

                var vyjimky = new List<string>(group.Vyjimky);
                vyjimky.Sort(StringComparer.OrdinalIgnoreCase);
                foreach (string vyjimka in vyjimky)
                {
                    foreach (string suffix in LeafSuffixes)
                    {
                        AddRole(result, seen, businessOnly, new DerivedRole
                        {
                            AccountName           = "ROL-" + vyjimka + "_" + suffix,
                            Faculty               = facultyAttr,
                            StrediskoZkratka      = null,
                            DistinguishedNamePart = "OU=" + vyjimka,
                            IsParent              = false,
                            Vyjimka               = true,
                        }, suffix);
                    }
                }
            }

            return result;
        }

        private static void AddRole(
            List<DerivedRole> result,
            HashSet<string> seen,
            bool businessOnly,
            DerivedRole role,
            string suffix)
        {
            if (businessOnly)
            {
                if (role.IsParent) return;
                if (!BusinessSuffixes.Contains(suffix)) return;
            }

            if (!seen.Add(role.AccountName))
                return;

            result.Add(role);
        }

        private static bool IsRek(string faculty)
        {
            return faculty.Equals("REK", StringComparison.OrdinalIgnoreCase);
        }

        private static string FacultyToken(string faculty)
        {
            return IsRek(faculty) ? "Rektorat" : faculty;
        }

        private static string FacultyOu(string faculty)
        {
            return IsRek(faculty) ? "REKTORAT" : faculty;
        }

        /// <summary>
        /// Cast zkratky do nazvu role. Obsahuje-li pomlcku, vrati text za posledni pomlckou
        /// (FTZ-AS → AS). Bez pomlcky, nebo kdyz za ni nic neni, vrati celou zkratku.
        /// </summary>
        private static string NameZkratka(string zkratka)
        {
            int i = zkratka.LastIndexOf('-');
            if (i < 0 || i >= zkratka.Length - 1)
                return zkratka;
            return zkratka.Substring(i + 1);
        }

        /// <summary>
        /// Prejmenovani tokenu zkratky v AccountName. DEK → DEKANAT u kazde fakulty.
        /// </summary>
        private static string NameZkratkaToken(string nameZkratka)
        {
            if (nameZkratka.Equals("DEK", StringComparison.OrdinalIgnoreCase))
                return "DEKANAT";
            return nameZkratka;
        }

        /// <summary>
        /// Text pred prvni pomlckou. Null, pokud zkratka pomlcku nema nebo na ni zacina.
        /// </summary>
        private static string HyphenPrefix(string zkratka)
        {
            int i = zkratka.IndexOf('-');
            if (i <= 0) return null;
            return zkratka.Substring(0, i);
        }

        private static Dictionary<string, int> CountHyphenPrefixes(FacultyGroup group)
        {
            var counts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            foreach (string zkratka in group.Zkratky.Keys)
            {
                string prefix = HyphenPrefix(zkratka);
                if (prefix == null) continue;
                int n;
                counts.TryGetValue(prefix, out n);
                counts[prefix] = n + 1;
            }
            return counts;
        }

        private static string LeafDn(string zkratka, string facultyOu, Dictionary<string, int> prefixCounts)
        {
            string prefix = HyphenPrefix(zkratka);
            int n = 0;
            if (prefix != null)
                prefixCounts.TryGetValue(prefix, out n);

            if (n > 1)
                return "OU=" + zkratka + ",OU=" + prefix + ",OU=" + facultyOu;

            return "OU=" + zkratka + ",OU=" + facultyOu;
        }

        private static string MatchVyjimka(string kod)
        {
            kod = NormalizeKod(kod);
            if (kod == null) return null;

            for (int i = 0; i < ExceptionRules.Length; i++)
            {
                if (kod.StartsWith(ExceptionRules[i][0], StringComparison.Ordinal))
                    return ExceptionRules[i][1];
            }
            return null;
        }

        private static string NormalizeCode(string value)
        {
            if (string.IsNullOrWhiteSpace(value)) return null;
            return value.Trim().ToUpperInvariant();
        }

        /// <summary>
        /// Odebere diakritiku (FŽP → FZP, ŘÍZENÍ → RIZENI). Pouziva se na nazev role
        /// a na atributy claFaculty / claStrediskoZkratkaPPV, ne na DistinguishedNamePart.
        /// </summary>
        private static string StripDiacritics(string value)
        {
            if (string.IsNullOrEmpty(value)) return value;

            string formD = value.Normalize(NormalizationForm.FormD);
            var builder = new StringBuilder(formD.Length);
            foreach (char character in formD)
            {
                if (CharUnicodeInfo.GetUnicodeCategory(character) != UnicodeCategory.NonSpacingMark)
                    builder.Append(character);
            }
            return builder.ToString().Normalize(NormalizationForm.FormC);
        }

        /// <summary>
        /// Kod strediska jako text. NUMBER z Oracle obcas prijde jako "7123.0" / "7123,0".
        /// </summary>
        private static string NormalizeKod(string value)
        {
            if (string.IsNullOrWhiteSpace(value)) return null;
            string kod = value.Trim();
            int sep = kod.IndexOfAny(new[] { '.', ',' });
            if (sep > 0)
            {
                bool zeros = true;
                for (int i = sep + 1; i < kod.Length; i++)
                {
                    if (kod[i] != '0')
                    {
                        zeros = false;
                        break;
                    }
                }
                if (zeros)
                    kod = kod.Substring(0, sep);
            }
            return kod;
        }

        private sealed class FacultyGroup
        {
            public readonly string Faculty;
            public readonly Dictionary<string, string> Zkratky =
                new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            public readonly HashSet<string> Vyjimky =
                new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            public FacultyGroup(string faculty)
            {
                Faculty = faculty;
            }
        }
    }
}
