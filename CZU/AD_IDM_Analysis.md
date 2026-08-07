# Active Directory – IDM Flow Analysis
## CZU (Czech University of Life Sciences Prague)

> **Zdroj**: `CZUIdm_FullInventory.pdf` (4005 stran), sekce 3.1.1.50–3.1.1.55, Appendix A (policy detail str. 2506–2613)

---

## 1. Přehled AD driverů

| Driver | Sekce | Typ | Směr | Start | Účel |
|--------|-------|-----|------|-------|------|
| **OUT-AD-Stud** | 3.1.1.50 | AD 4.0.0.4 | IDM → AD | Auto | Studentské účty v AD |
| **OUT-AD-Zam** | 3.1.1.51 | AD 4.0.0.4 | IDM → AD | Auto | Zaměstnanecké účty v AD |
| **OUT-AD-Dok** | 3.1.1.52 | AD 4.0.0.4 | IDM → AD | Auto | Doktorandské účty v AD |
| **OUT-AD-Origin-ShareGroups** | 3.1.1.55 | AD 4.0.0.4 | IDM → AD | Auto | Share skupiny pro Origin |
| **Scripting-AD-HomeDirs** | - | Scripting | IDM → AD | Auto | Vytvoření home adresářů STU |
| **Scripting-AD-HomeDirs-Zam** | - | Scripting | IDM → AD | Auto | Vytvoření home adresářů ZAM/DOK |

### Pomocné (podpůrné) drivery

| Driver | Účel |
|--------|------|
| **LoopBack-generateLoginInfo** | Generování login jmen ZAM/DOK, přiřazení e-mail domény, notifikace |
| **LoopBack-notifyLoginInfo** | Notifikace uživatele o novém hesle (SMS + e-mail HelpDesk) |

---

## 2. Společné connection informace

| Parametr | Hodnota |
|----------|---------|
| **AD doména** | `CZU.CZ` (`DC=czu,DC=cz`) |
| **Remote Shim host** | `SWIDM001.czu.cz` |
| **AD LDAP** (load balancer) | `ldapad.czu.cz` |
| **AD LDAPS** (přímé) | `ldaps://SWAD001.czu.cz` |
| **IDM auth účet** | `CZU\svc.idmsync` |
| **Vault LDAP** (pro dotazy) | `ldap://slxvault01.oikt.czu.cz/ou=IDENTITIES,o=czu,c=cz` |
| **Vault read účet** | `cn=IDMRead,ou=mgmt,o=czu,c=cz` |
| **Driver verze** | AD 4.0.0.4 |
| **Java modul** | `com.novell.nds.dirxml.remote.driver.DriverShimImpl` |
| **Auth metoda shim** | Negotiate; signing=no; sealing=no; ssl=no; impersonation=yes |

### Porty remote shim

| Driver | Port |
|--------|------|
| OUT-AD-Stud | 8090 |
| OUT-AD-Zam | 8092 |
| OUT-AD-Dok | (stejný server) |
| OUT-AD-Origin-ShareGroups | (stejný server) |

---

## 3. OUT-AD-Stud – Studentské účty

### 3.1 Účel a cílová struktura

Synchronizuje **studentské uživatelské účty** z Identity Vault do AD. Zdrojová data pocházejí primárně z IN-UIS-Stud driveru (studijní informační systém).

- **Cílová OU**: `OU=Users,OU=Students,OU=_Production,DC=czu,DC=cz`
- **GCV**: žádné definovány
- **Password subscribe**: `enable-password-subscribe = false` (výchozí)

### 3.2 Schema Mapping (OUT-AD-Stud)

Třída: `user` (AD) → `User` (IDM)

> Žádné explicitní atributové mapování v SchemaMapping. AD nativní jména atributů jsou mapována přes Command policy.

### 3.3 Output Transform

| Atribut | Transformace |
|---------|-------------|
| `accountExpires` | Epoch → AD FileTime (`jadutil:translateEpoch2FileTime`) |
| `czuLastStateChange` | Epoch → AD FileTime |

### 3.4 Filter atributy (OUT-AD-Stud)

**Třída: User**

| IDM Atribut | Token | Poznámka |
|-------------|-------|---------|
| GUID | default | AD GUID |
| CN | default | Common Name |
| Given Name | edir | Jméno (edir = eDir jako autorita) |
| workforceID | default | UIC (ID osoby) |
| Surname | default | Příjmení |
| nspmDistributionPassword | none | Distribuční heslo (nesledováno) |
| claStudentID | default | Student ID (číslo studia) |
| claStudentUIC | default | Student UIC |
| claLoginNameSTU | default | Login jméno studenta |
| claDegreePrefix | default | Titul před jménem |
| claDegreeSuffix | default | Titul za jménem |
| claChipLongCARD | default | Číslo čipové karty |
| claStavOd | default | Datum zahájení stavu |
| claCitizenShipID | default | Kód státního občanství |

**Třída: Group** (Studijní skupiny)

| IDM Atribut | Token |
|-------------|-------|
| GUID | default |
| claFaculty | default |
| claEduSpecializationCode | default |
| claStudentID | default |
| claStudentUIC | default |
| claStavStudiaKod | default |
| CN | default |

### 3.5 Policy sety (OUT-AD-Stud)

#### Driver Policy Set
| Typ | Název |
|-----|-------|
| OUTPUT | `OutputTransform` |
| INPUT | (none) |
| MAPPING | `SchemaMapping` |

#### Publisher Channel
| Typ | Policy |
|-----|--------|
| Command | `Command` |

#### Subscriber Channel
| Typ | Policy |
|-----|--------|
| Creation | `Creation` |
| Event | `Event` |
| Matching | `Matching` |
| Placement | `Placement` |
| Command | `Command` |

### 3.6 Subscriber Channel – detailní flow

```
IDM Vault Event
    │
    ▼
[Creation] ─── Veto if nspmDistributionPassword missing (when enable-password-subscribe=true)
    │
    ▼
[Event] ─── Init local variables (validGroup=0, validStudent=0)
    │        USER: Check claLoginNameSTU (sets validStudent=1)
    │        USER: Check active STUDIUM via query to ~studiumOU~ (claStavStudiaKod: A,Z,S,O,R,V,P)
    │        GROUP: Check claLoginNameSTU existence
    │        GROUP: Check active STUDIUM (claStavStudiaKod status)
    │        USER on Delete: disable + veto
    │        Veto: non active User or Group (validGroup=0 OR validStudent=0)
    │        Set driver global properties (faculty, specialization sets)
    ▼
[Matching] ─── Match by object in AD subtree
    │
    ▼
[Placement] ─── Set DN: CN=Surname GivenName,OU=Users,OU=Students,OU=_Production,DC=czu,DC=cz
    │
    ▼
[Command] ─── Set local properties (studentID, faculty sets, etc.)
              USER: Check for active studium (query to ~studiumOU~)
              USER: Veto zombies (user not associated AND no active faculty)
              USER: Move disabled user → vrátit do správné OU, zrušit Login Expiration Time
              User: Change in CN → přejmenování v AD
              User: If nspmDistributionPassword empty → dohledat z vault a nastavit
              USER: Placement (locTargetDN = CN=...,OU=Users,OU=Students...)
              User: nspmDistributionPassword → update czuChipLongCard
              USER: Set password on add
              USER: Convert modify nspmDistributionPassword → modify-password
              USER: Create User objects:
                  - Full Name = DegreePrefix GivenName Surname DegreeSuffix
                  - displayName = Surname GivenName (FacultyCode-FacultyCode)
                  - DirXML-ADAliasName = lower(claLoginNameSTU)  ← sAMAccountName
                  - userPrincipalName = claLoginNameSTU@studenti.czu.cz
                  - homeDirectory = \\CZU.CZ\DATASTUD\HOME\{workforceID}
                  - homeDrive = H:
                  - dirxml-uACDontExpirePassword = true
                  - dirxml-uACPasswordNotRequired = false
                  - Login Disabled = true (pokud chybí heslo) / false (pokud heslo existuje)
                  - countryCode = 203 (CZ) nebo 840 (jiné)
                  - claFaculty (z studijních skupin)
                  - claEduSpecializationCode
                  - czuStudyProgramAbbr
                  - extensionAttribute7 = "O365_Import"
                  - proxyAddresses: smtp:loginSTU@czuvpraze.mail.onmicrosoft.com
                                    SMTP:loginSTU@studenti.czu.cz
                  - targetAddress: SMTP:loginSTU@czuvpraze.mail.onmicrosoft.com
                  - msExchHideFromAddressLists = FALSE/TRUE
              USER: Remove all AD groups (dle FacultyADGroupMapping)
              USER: Assign Group membership:
                  - Faculty skupiny (dle FacultyADGroupMapping tabulky)
                  - CN=IDM_IDENTITY,OU=Groups,OU=_Global,OU=_Production,DC=czu,DC=cz
                  - CN=ROL-IDM-Microsoft_365_A5_student,...
              GROUP: Zpracování změn studijního stavu (claStavStudiaKod)
              GROUP: Přiřazení/odebrání skupin
              USER on Remove (delete):
                  - Clear czuStudyProgramAbbr
                  - Login Disabled = true
                  - Login Expiration Time = now
                  - msExchHideFromAddressLists = TRUE
    │
    ▼
AD – Student účet vytvořen/aktualizován
```

#### Publisher Command Policy

| Pravidlo | Podmínka | Akce |
|---------|----------|------|
| USER: Remove association | delete + Destination DN available | Přidá DirXML-Associations, odstraní asociaci, Veto |
| USER: Fix association loopback | add + Destination DN not available | Nastaví dest-dn |
| Group: Veto | class=Group | Veto |
| Veto on move/rename | operation=move or rename | Veto |

### 3.7 Klíčové podmínky (OUT-AD-Stud)

| Podmínka | Akce |
|---------|------|
| `claLoginNameSTU` chybí | `validStudent=0` → Veto |
| Žádné aktivní studium (claStavStudiaKod ∉ {A,Z,S,O,R,V,P}) | `validGroup=0` → Veto |
| Student není přiřazen a nemá aktivní fakultu | Veto zombie |
| Delete event | Login Disabled=true, Login Expiration Time=now, msExchHideFromAddressLists=TRUE, Veto |
| Email @studenti.czu.cz chybí | Login Disabled=true |
| Jiný e-mail (@studenti.czu.cz) – veto pro ZAM driver | Veto v OUT-AD-Zam/Dok |

### 3.8 AD atributy nastavované pro Student

| AD atribut | Zdroj v IDM | Hodnota / Logika |
|-----------|-------------|-----------------|
| `sAMAccountName` | `claLoginNameSTU` (lower) | Login jméno studenta |
| `userPrincipalName` | `claLoginNameSTU` | `{login}@studenti.czu.cz` |
| `mail` | `claLoginNameSTU` | `{login}@studenti.czu.cz` |
| `displayName` | Surname, Given Name, Faculty | `Příjmení Jméno (FakultaKódy)` |
| `Full Name` | Degree+Given Name+Surname | Celé jméno s tituly |
| `CN` | Surname + Given Name | Příjmení Jméno |
| `homeDirectory` | `workforceID` | `\\CZU.CZ\DATASTUD\HOME\{UIC}` |
| `homeDrive` | – | `H:` |
| `proxyAddresses` | `claLoginNameSTU` | smtp/SMTP varianty |
| `targetAddress` | `claLoginNameSTU` | SMTP onmicrosoft.com |
| `extensionAttribute7` | – | `O365_Import` |
| `countryCode` | `claCitizenShipID` | 203=CZ, 840=jiné |
| `claFaculty` | Studijní skupiny | Kód fakulty |
| `claEduSpecializationCode` | Studijní skupiny | Kód specializace |
| `czuStudyProgramAbbr` | Studijní skupiny | Zkratka studijního programu |
| `msExchHideFromAddressLists` | Aktivita | FALSE=aktivní, TRUE=neaktivní |
| `Login Disabled` | Heslo | true=chybí heslo, false=má heslo |
| `czuChipLongCARD` | `claChipLongCARD` | Číslo čipové karty |
| `workforceID` | `workforceID` | UIC |

---

## 4. OUT-AD-Zam – Zaměstnanecké účty

### 4.1 Účel a cílová struktura

Synchronizuje **zaměstnanecké uživatelské účty** z Identity Vault do AD. Zdrojová data pocházejí z IN-Magion driveru (zaměstnanecká data z Magionu).

- **Cílová OU**: `OU=Users,{StrediskoOU},OU=_Production,DC=czu,DC=cz`
  - StrediskoOU se určuje mapovací tabulkou `StrediskoZkratkaToADUsers` dle `claStrediskoZkratkaPPV`
- **GCV**: `enable-password-subscribe = true` (ostatní false)

### 4.2 Schema Mapping (OUT-AD-Zam)

| AD atribut | IDM atribut (Vault) |
|-----------|---------------------|
| `user` (třída) | `User` (třída) |
| `mail` | `Internet EMail Address` |
| `czuChipLongCard` | `claChipLongCARD` |
| `czuLastStateChange` | `claModified` |
| `physicalDeliveryOfficeName` | `claOffice` |
| `czuUnitCode` | `claStrediskoKodPPV` |
| `czuUnitAbbr` | `claStrediskoZkratkaPPV` |
| `czuHiddenMobile` | `claVPNPhone` |
| `telephoneNumber` | `claWorkPhone` |
| `cn` | `CN` |
| `sAMAccountName` | `DirXML-ADAliasName` |
| `DirXML-EntitlementRef` | `DirXML-EntitlementRef` |
| `DirXML-EntitlementResult` | `DirXML-EntitlementResult` |
| `DirXML-SPEntitlements` | `DirXML-SPEntitlements` |
| `extensionAttribute12` | `Full Name` |
| `givenName` | `Given Name` |
| `dirxml-uACAccountDisable` | `Login Disabled` |
| `accountExpires` | `Login Expiration Time` |
| `lockoutTime` | `Login Intruder Reset Time` |
| `member` | `Member` |
| `sn` | `Surname` |
| `czuUIC` | `workforceID` |

### 4.3 Filter atributy (OUT-AD-Zam)

**Třída: User**

| IDM Atribut | Popis |
|-------------|-------|
| claChipLongCARD | Čipová karta |
| claDegreePrefix | Titul před jménem |
| claDegreeSuffix | Titul za jménem |
| claLoginNameZAM | Login jméno zaměstnance |
| claOffice | Číslo kanceláře |
| claUICPPV | UIC z PPV záznamu |
| claVPNPhone | VPN telefon |
| claWorkPhone | Pracovní telefon |
| CN | Common Name |
| Given Name | Jméno |
| GUID | GUID |
| Internet EMail Address | E-mail |
| nspmDistributionPassword | Distribuční heslo |
| Surname | Příjmení |
| workforceID | UIC |
| claCitizenShipCode | Státní občanství (CZ/jiné) |
| claMobilePhone | Mobilní telefon |
| claOsCisZAM | Osobní číslo zaměstnance |

**Třída: Group** (PPV záznamy)

| IDM Atribut | Popis |
|-------------|-------|
| claAktivniPPV | Aktivita PPV (1=aktivní) |
| claEndDatePPV | Datum konce PPV |
| claStrediskoKodPPV | Kód střediska |
| claStrediskoZkratkaPPV | Zkratka střediska |
| claVyseUvazku | Výše úvazku |
| CN | Common Name |
| GUID | GUID |
| claKategorie2KodPPV | Kategorie zaměstnance |
| claFunkcePPV | Název funkce |
| claFunkceKodPPV | Kód funkce |
| czuMistoPPV | Místo výkonu práce |
| claFaculty | Fakulta |

### 4.4 Policy sety (OUT-AD-Zam)

#### Driver Policy Set
| Typ | Název |
|-----|-------|
| OUTPUT | `OutputTransform` |
| INPUT | (none) |
| MAPPING | `SchemaMapping` |

#### Publisher Channel
| Typ | Policy |
|-----|--------|
| Command | `Command` |

#### Subscriber Channel
| Typ | Policy |
|-----|--------|
| Creation | (none) |
| Event | `Event` |
| Matching | `Matching` |
| Placement | (none – řeší Command) |
| Command | `Command` |
| Command | `Command-AADAtribs` (extra Azure AD atributy) |

### 4.5 Subscriber Channel – detailní flow

```
IDM Vault Event
    │
    ▼
[Event] ─── Init: O365 group names (zamestnanec, doktorand, OIKT, eDovolenka, WifiGuest)
    │        USER/GROUP: Check for active DOKTORAND (DOK- groups, claStavStudiaKod: S,V,P)
    │        USER/GROUP: Check for active ZAM (PPV- groups, claAktivniPPV=1)
    │           → locTargetDN = StrediskoZkratkaToADUsers[claStrediskoZkratkaPPV]
    │           → locStrediskoKodPPV, locStrediskoZkratkaPPV
    │           → IsOIKTMember (pokud OU=OIKT,OU=REKTORAT,...)
    │           → locLastPPVStanding (poslední aktivní PPV přechází na neaktivní)
    │
    ▼
[Matching] ─── Match v AD
    │
    ▼
[Command] ─── Set local properties
              USER: Veto zombies (not associated AND no active PPV AND no DOK AND not last standing)
              USER: Placement → CN=CN,OU=Users,{StrediskoOU},OU=_Production,DC=czu,DC=cz
              USER: Move if not in desired destination (cancel Login Expiration Time)
              User: Rename if CN changed
              User: If password missing → dohledat z vault
              USER: Set password on add
              USER: Convert modify nspmDistributionPassword → modify-password
              USER: Create User objects:
                  - Full Name = DegreePrefix GivenName Surname DegreeSuffix (normalize-space)
                  - displayName = Surname GivenName
                  - DirXML-ADAliasName = lower(claLoginNameZAM) ← sAMAccountName
                  - homeDirectory = \\CZU.CZ\DATA\HOME\{workforceID}
                  - homeDrive = H:
                  - claStrediskoKodPPV / claStrediskoZkratkaPPV (z primárního PPV)
                  - Login Disabled = true/false (dle hesla)
                  - dirxml-uACDontExpirePassword = true
                  - userPrincipalName = {employee email} (NESMÍ být @studenti.czu.cz)
                  - Veto pokud email @studenti.czu.cz
                  - telephoneNumber = "+420 22438" + claWorkPhone
                  - physicalDeliveryOfficeName = claOffice
                  - countryCode = 203(CZ) / 840(jiné) dle claCitizenShipCode
                  - employeeID = claOsCisZAM
                  - company = "#ZU"
                  - title = czuMistoPPV nebo claFunkcePPV nebo claZarazeniPPV
                  - department = claFaculty (z primárního PPV)
                  - claModified = now (na add)
              GROUP: Create User if not exists / move back from disabled:
                  - Hledá uživatele v ~identitiesOU~ dle workforceID
                  - Vytvoří AD účet nebo přesune zpět do aktivní OU
                  - Nastaví stejné atributy jako USER výše
              GROUP: Make changes in claStavStudiaKod (update faculty, specialization)
              USER/GROUPS: Remove all AD Groups
              USER/GROUP: Assign group membership:
                  - Department skupiny (dle FacultyADGroupMapping/StrediskoZkratkaToADUsers)
                  - CN=IDM_IDENTITY,...
                  - CN=ROL-IDM-Microsoft_365_A5_zamestnanec,...
                  - CN=ROL-IDM-Microsoft_365_A5_doktorand,... (pokud DOK)
                  - CN=ROL-IDM-Microsoft_365_A5_OIKT,... (pokud IsOIKTMember)
                  - CN=ROL-CZU_WIFIGUEST_user,...
                  - CN=ROL-IDM-M365_eDovolenka,...
    │
    ▼
AD – Zaměstnanecký účet vytvořen/aktualizován
```

### 4.6 Klíčové podmínky (OUT-AD-Zam)

| Podmínka | Akce |
|---------|------|
| `claAktivniPPV = 0` a žádné aktivní PPV | locLastPPVStanding check |
| Žádné aktivní PPV a žádný DOK a ne locLastPPVStanding | Veto zombie |
| Email @studenti.czu.cz | Veto (zaměstnanci nesmí mít studentský email) |
| Email chybí | Login Disabled=true, userPrincipalName cleared |
| Delete event | Odebrat asociaci, Veto |
| Move/Rename event | Veto (z publisheru) |
| Poslední aktivní PPV deaktivuje | Login Expiration Time=nyní+6 měsíců, msExchHideFromAddressLists=TRUE |

---

## 5. OUT-AD-Dok – Doktorandské účty

### 5.1 Účel a cílová struktura

Synchronizuje **doktorandské účty** z Identity Vault do AD. Data pocházejí z IN-UIS-Dokt (doktorandský IS).

- **Cílová OU**: `OU=Users,{DepartmentOU},OU=_Production,DC=czu,DC=cz`
  - DepartmentOU se určuje dle `DepartmentToADGroup` tabulky z `claDepartmentShortName`

### 5.2 Schema Mapping (OUT-AD-Dok)

Stejné jako OUT-AD-Zam, plus:

| AD atribut | IDM atribut |
|-----------|-------------|
| `czuStudyDepartmentAbbr` | `claDepartmentShortName` |
| `czuStudySpecAbbr` | `claEduSpecializationCode` |
| `czuFaculty` | `claFaculty` |

### 5.3 Klíčové rozdíly oproti OUT-AD-Zam

| Aspekt | OUT-AD-Zam | OUT-AD-Dok |
|--------|-----------|-----------|
| Zdrojová data | PPV (Magion) | Doktorandské studium (UIS) |
| Aktivita check | `claAktivniPPV=1` (PPV groups) | `claStavStudiaKod ∈ {S,V,P}` (DOK groups) |
| Target OU mapování | `StrediskoZkratkaToADUsers` | `DepartmentToADGroup` |
| Atribut klíče | `claStrediskoZkratkaPPV` | `claDepartmentShortName` |
| Extra atributy | claStrediskoKodPPV, czuUnitCode | czuStudyDepartmentAbbr, czuStudySpecAbbr, czuFaculty |
| O365 skupina | `Microsoft_365_A5_zamestnanec` | `Microsoft_365_A5_doktorand` |
| Home dir | `\\CZU.CZ\DATA\HOME\{UIC}` | `\\CZU.CZ\DATA\HOME\{UIC}` |
| Aktivní stavy | PPV claAktivniPPV=1 | DOK S, V, P |

### 5.4 Policy sety (OUT-AD-Dok)

#### Subscriber Channel
| Typ | Policy |
|-----|--------|
| Event | `Event` |
| Matching | `Matching` |
| Command | `Command` |

#### Publisher Channel
| Typ | Policy |
|-----|--------|
| Command | `Command` |

### 5.5 Subscriber Command flow (OUT-AD-Dok)

```
IDM Vault Event
    │
    ▼
[Command] ─── Set local properties (O365 groups: student, zamestnanec, doktorand)
    │          USER/GROUP: Check for active ZAM (PPV- groups, claAktivniPPV=1)
    │          USER: Check for active DOK studium:
    │             Stavy: A, Z, S, V, P
    │             Aktivní (ne A, ne Z): S, V, P
    │             → currFaculty, currDepartmentShortName, currStudySpecAbbr
    │             → userDepartmentShortName, userFaculty, userStudySpecAbbr
    │          GROUP: Remove all AD Groups (dle DepartmentToADGroup)
    │          GROUP: Remove Office365 groups (student, dok, zamestnanec)
    │          USER/GROUP: Assign group membership:
    │             - CN=IDM_IDENTITY,...
    │             - Department skupina (DepartmentToADGroup)
    │             - CN=ROL-IDM-Microsoft_365_A5_doktorand,...
    │          GROUP: Vytvoření/aktualizace uživatele (pokud DOK, PPV aktivní)
    │             - DirXML-ADAliasName = lower(claLoginNameZAM)
    │             - homeDirectory = \\CZU.CZ\DATA\HOME\{workforceID}
    │             - czuStudyDepartmentAbbr = claDepartmentShortName
    │             - czuFaculty = claFaculty
    │             - czuStudySpecAbbr = claEduSpecializationCode
    │             - czuStudyProgramAbbr
    ▼
AD – Doktorandský účet vytvořen/aktualizován
```

---

## 6. OUT-AD-Origin-ShareGroups – Share skupiny

### 6.1 Účel

Synchronizuje skupiny z kontejneru `ORIGIN-GROUPS` v Identity Vault do AD. Slouží pro oprávnění k sdíleným složkám na file serverech.

- **Cílové OU**:
  - **ROLE skupiny** (bez prefixu FAC-): `OU=Role,OU=Origin,OU=Groups,OU=_Global,OU=_Production,DC=czu,DC=cz`
  - **FAC skupiny** (prefix FAC-): `OU=ACL_FS,OU=Origin,OU=Groups,OU=_Global,OU=_Production,DC=czu,DC=cz`
- **Třída**: Group

### 6.2 Schema Mapping

Prázdné (žádné mapování atributů – využívá native AD jmen).

### 6.3 Policy sety (OUT-AD-Origin-ShareGroups)

#### Driver Policy Set
| Typ | Policy |
|-----|--------|
| MAPPING | `SchemaMapping` |

#### Publisher Channel
| Typ | Policy |
|-----|--------|
| Command | `Command` |

#### Subscriber Channel
| Typ | Policy |
|-----|--------|
| Command | `Command` |
| Event | `Event` |
| Matching | `Matching` |

### 6.4 Subscriber Command flow

```
IDM Vault Event (Group z ORIGIN-GROUPS)
    │
    ▼
[Command] ─── Set: target_ROLE_DN, target_ACLFS_DN
    │          GROUP: Create ROLE object (pokud source DN je v cz\czu\ORIGIN-GROUPS):
    │             Pokud CN matches FAC-.*:
    │                → locTargetDN = OU=ACL_FS,OU=Origin,...
    │                → groupType = -2147483644 (Security Global)
    │                → Zachová Member atribut
    │             Ostatní:
    │                → locTargetDN = OU=Role,OU=Origin,...
    │                → groupType = -2147483646 (Distribution Global)
    │                → Odstraní Member atribut (strip)
    │             Set: sAMAccountName = CN skupiny
    │             Clear destination Member attr
    │             Pro každého člena skupiny:
    │                → Dohledá DirXML-Associations pro OUT-AD-Zam, OUT-AD-Dok, OUT-AD-Stud
    │                → Přidá jako Member s association-ref (ZAM/DOK/STU)
    ▼
AD – Share Group vytvořena/aktualizována s členy
```

---

## 7. Podpůrné drivery

### 7.1 Scripting-AD-HomeDirs (pro studenty)

**Účel**: Vytvoření home adresářů pro studenty po přiřazení asociace OUT-AD-Stud.

**Trigger**: Pokud `DirXML-Associations` obsahuje asociaci z `OUT-AD-Stud` (nameSpace=1)

**Atributy**: `claLoginNameSTU`, `workforceID`

**Podmínky**:
- Veto pokud `claLoginNameSTU` nebo `workforceID` chybí
- Veto pokud DirXML-Associations neobsahuje OUT-AD-Stud asociaci

### 7.2 Scripting-AD-HomeDirs-Zam (pro zaměstnance/doktorandy)

**Účel**: Vytvoření home adresářů pro zaměstnance a doktorandy.

**Trigger**: Pokud `DirXML-Associations` obsahuje asociaci z `OUT-AD-Zam` nebo `OUT-AD-Dok` (nameSpace=1)

**Atributy**: `workforceID`, `claLoginNameZAM`

**Podmínky**:
- Veto pokud `claLoginNameZAM` nebo `workforceID` chybí

**Speciální akce**: Přidá `company = "STU"` atribut (Add UserTypeAttribute)

### 7.3 LoopBack-generateLoginInfo

**Účel**: Generování login jména pro ZAM/DOK uživatele (`claLoginNameZAM`) a nastavení e-mailové adresy.

**Trigger podmínky** (Event Transformation):
- Přidána PPV skupina (`\CZU-VAULT\cz\czu\PPV\PPV-`)
- Přidána DOK skupina (`\CZU-VAULT\cz\czu\DOKTORAND\DOK-`)
- Přidána studijní skupina (`\CZU-VAULT\cz\czu\STUDIUM\S-`)
- Přidána EXT skupina (`\CZU-VAULT\cz\czu\EXTERNALIST\EXT-`)

**E-mail domény** (mapování z library):
| Typ | Zdroj dat | Mapování |
|-----|-----------|---------|
| STU | – | `studenti.czu.cz` |
| DOK | `claDepartmentShortName` | Library\MappingDOK tabulka |
| ZAM | `claStrediskoZkratkaPPV` | Library\MappingZAM tabulka |

**Generování login jména** (Generate claLoginNameZAM):
- Pro STU: `claLoginNameSTU` se použije přímo
- Pro ZAM/DOK:
  1. Nové heslo vygenerováno (`initpwd:newPassword`)
  2. Login jméno vygenerováno: `genloginname:newLoginName(Surname, GivenName)` (max 20 znaků)
  3. Veto pokud Surname > 20 znaků A obsahuje mezeru (notifikace HelpDesk)
  4. Uloženo do `claLoginNameZAM`
  5. E-mail = `claLoginNameZAM@{mapovana_domena}`

**Podmínky veto**:
- Login jméno > 20 znaků → notifikace HelpDesk + Veto
- `claLoginNameZAM` již existuje pro ZAM/DOK → Veto

### 7.4 LoopBack-notifyLoginInfo

**Účel**: Notifikace uživatele o přiřazení login jména a hesla.

**Flow**:
```
claInitPwd změna detected
    │
    ▼
[Allow MODIFY or SYNC] ─── Veto pokud ne modify
    │                       Pokud claInitPwd = "reset" → nové heslo generováno
    │                       Nastaví nspmDistributionPassword
    ▼
[Change password to null]
    │
    ▼
[Send Notification to Origin USER]
    │
    ▼
[Sub-EventTransf-1] ─── SMS na mobil: {phoneNr}@gsm.oikt.czu.cz
    │                    E-mail HelpDesk: idm-notify@oikt.czu.cz
    │                    E-mail HR (na reset): idm-account@oikt.czu.cz
    │                    Kontrola unikátnosti claLoginNameSTU a claLoginNameZAM
    ▼
claInitPwd → "nullpassword" (reset marker)
```

---

## 8. Mapovací tabulky

| Tabulka | Používá se v | Popis |
|---------|-------------|-------|
| `FacultyADGroupMapping` | OUT-AD-Stud | Faculty kód → DN AD skupiny |
| `StrediskoZkratkaToADUsers` | OUT-AD-Zam | Zkratka střediska → OU v AD Users |
| `DepartmentToADGroup` | OUT-AD-Dok | Zkratka katedry → DN AD skupiny |
| `Library\MappingDOK` | LoopBack-generateLoginInfo | Katedra → e-mailová doména DOK |
| `Library\MappingZAM` | LoopBack-generateLoginInfo | Středisko → e-mailová doména ZAM |

---

## 9. Celkový AD datový tok

```
┌─────────────────────────────────────────────────────────────────────────┐
│                        CZU Identity Vault                               │
│                                                                         │
│  OU=IDENTITIES  OU=PPV   OU=STUDIUM  OU=DOKTORAND  OU=ORIGIN-GROUPS    │
│      │              │         │           │               │             │
└──────┼──────────────┼─────────┼───────────┼───────────────┼─────────────┘
       │              │         │           │               │
       │    ┌─────────┘         │           │               │
       │    │                   │           │               │
  ┌────▼────▼───┐          ┌────▼────┐  ┌───▼────┐    ┌────▼────┐
  │ OUT-AD-Zam  │          │OUT-AD-  │  │OUT-AD- │    │OUT-AD-  │
  │ (ZAM, port  │          │ Stud    │  │ Dok    │    │Origin-  │
  │   8092)     │          │(port    │  │        │    │ShareGr. │
  └──────┬──────┘          │  8090)  │  └───┬────┘    └────┬────┘
         │                 └────┬────┘      │               │
         │                      │           │               │
         └──────────────────────┼───────────┘               │
                                │                           │
                    ┌───────────▼────────────┐              │
                    │   Active Directory      │              │
                    │   DC=czu,DC=cz          │◄─────────────┘
                    │                         │
                    │  OU=_Production         │
                    │   ├── OU=Students       │  (OUT-AD-Stud)
                    │   │    └── OU=Users     │
                    │   ├── OU={Stredisko}    │  (OUT-AD-Zam)
                    │   │    └── OU=Users     │
                    │   ├── OU={Department}   │  (OUT-AD-Dok)
                    │   │    └── OU=Users     │
                    │   └── OU=_Global        │
                    │        └── OU=Groups    │
                    │             ├── OU=Role  │
                    │             │  ├── OU=Origin (ShareGroups)
                    │             │  └── OU=Role (O365, IDM_IDENTITY)
                    │             └── OU=ACL_FS (FAC-* groups)
                    └─────────────────────────┘
                                │
                    ┌───────────▼────────────┐
                    │   Scripting drivery    │
                    │  + LoopBack drivery    │
                    │  (home dirs, login     │
                    │   generování, notif.)  │
                    └────────────────────────┘
```

---

## 10. Přehled klíčových IDM atributů pro AD

| IDM Atribut | OUT-AD-Stud | OUT-AD-Zam | OUT-AD-Dok | Popis |
|-------------|-------------|-----------|-----------|-------|
| `workforceID` | ✓ | ✓ | ✓ | UIC (unikátní ID osoby) |
| `claLoginNameSTU` | ✓ | – | – | Login studenta |
| `claLoginNameZAM` | – | ✓ | ✓ | Login zaměstnance/doktoranda |
| `nspmDistributionPassword` | ✓ | ✓ | ✓ | Distribuční heslo |
| `Given Name` | ✓ | ✓ | ✓ | Jméno |
| `Surname` | ✓ | ✓ | ✓ | Příjmení |
| `claDegreePrefix` | ✓ | ✓ | ✓ | Titul před jménem |
| `claDegreeSuffix` | ✓ | ✓ | ✓ | Titul za jménem |
| `Internet EMail Address` | – | ✓ | ✓ | E-mail |
| `claOsCisZAM` | – | ✓ | – | Osobní číslo zaměstnance |
| `claStrediskoKodPPV` | – | ✓ | – | Kód střediska (z PPV) |
| `claStrediskoZkratkaPPV` | – | ✓ | – | Zkratka střediska |
| `claAktivniPPV` | – | ✓ | ✓ | Aktivní PPV příznak |
| `claFunkcePPV` | – | ✓ | – | Funkce PPV |
| `claFunkceKodPPV` | – | ✓ | – | Kód funkce PPV |
| `claFaculty` | ✓ | ✓ | ✓ | Kód fakulty |
| `claEduSpecializationCode` | ✓ | – | ✓ | Kód specializace |
| `claDepartmentShortName` | – | – | ✓ | Zkratka katedry (DOK) |
| `claChipLongCARD` | ✓ | ✓ | – | Čipová karta |
| `claCitizenShipCode/ID` | ✓ | ✓ | ✓ | Státní občanství |
| `claWorkPhone` | – | ✓ | ✓ | Pracovní telefon |
| `claOffice` | – | ✓ | ✓ | Kancelář |
| `claVPNPhone` | – | ✓ | ✓ | VPN telefon |
| `claStudentID` | ✓ | – | – | ID studijní skupiny |
| `claStavStudiaKod` | ✓ | – | ✓ | Stav studia |
| `claModified` | – | ✓ | ✓ | Čas poslední změny |
| `claInitPwd` | – | ✓ | ✓ | Počáteční heslo |

---

## 11. Porovnání AD driverů – přehledová tabulka

| Vlastnost | OUT-AD-Stud | OUT-AD-Zam | OUT-AD-Dok |
|-----------|-------------|-----------|-----------|
| **Uživatelé** | Studenti | Zaměstnanci | Doktorandi |
| **Zdroj dat** | IN-UIS-Stud | IN-Magion + LoopBack | IN-UIS-Dokt + LoopBack |
| **Login atribut** | `claLoginNameSTU` | `claLoginNameZAM` | `claLoginNameZAM` |
| **UPN doména** | `@studenti.czu.cz` | `@{fak/stred}.czu.cz` | `@{katedra}.czu.cz` |
| **Home dir** | `\\CZU.CZ\DATASTUD\HOME\` | `\\CZU.CZ\DATA\HOME\` | `\\CZU.CZ\DATA\HOME\` |
| **Aktivita check** | claStavStudiaKod (A,Z,S,O,R,V,P) | claAktivniPPV=1 | claStavStudiaKod (S,V,P) |
| **Target OU** | Students | Dle Stredisko | Dle Department |
| **O365 licence** | A5 student | A5 zamestnanec | A5 doktorand |
| **Password subscribe** | false | true | true |
| **Shim port** | 8090 | 8092 | 8092 |
| **Creation policy** | Ano | Ne | Ne |
| **Placement policy** | Ano | Ne (v Command) | Ne (v Command) |

---

## 12. E-mailová doménová pravidla

| Typ uživatele | E-mail doména | Zdroj |
|--------------|---------------|-------|
| Student | `@studenti.czu.cz` | Pevná hodnota |
| Doktorand | Per katedra (MappingDOK tabulka) | `claDepartmentShortName` |
| Zaměstnanec | Per středisko (MappingZAM tabulka) | `claStrediskoZkratkaPPV` |
| Externista | (EXT skupiny) | MappingZAM tabulka |

**Pravidlo veto**:
- Zaměstnanecký driver (OUT-AD-Zam): Veto pokud výsledný email = `@studenti.czu.cz`
- Doktorandský driver (OUT-AD-Dok): Veto pokud výsledný email = `@studenti.czu.cz`
- OUT-AD-Stud (studentský driver): Veto pokud email NENÍ `@studenti.czu.cz`

---

## 13. Office 365 skupiny v AD

| Skupina | DN | Přiřazení |
|---------|-----|----------|
| O365 Student | `CN=ROL-IDM-Microsoft_365_A5_student,...` | OUT-AD-Stud, OUT-AD-Dok |
| O365 Zaměstnanec | `CN=ROL-IDM-Microsoft_365_A5_zamestnanec,...` | OUT-AD-Zam, OUT-AD-Dok |
| O365 Doktorand | `CN=ROL-IDM-Microsoft_365_A5_doktorand,...` | OUT-AD-Dok |
| O365 OIKT | `CN=ROL-IDM-Microsoft_365_A5_OIKT,...` | OUT-AD-Zam (IsOIKTMember) |
| eDovolenka | `CN=ROL-IDM-M365_eDovolenka,...` | OUT-AD-Zam |
| WiFi Guest | `CN=ROL-CZU_WIFIGUEST_user,...` | OUT-AD-Zam |
| IDM Identity | `CN=IDM_IDENTITY,OU=Groups,...` | OUT-AD-Stud, OUT-AD-Zam, OUT-AD-Dok |
