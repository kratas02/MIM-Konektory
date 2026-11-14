# Atributy MIM portálu ZSDIS

Zdroj: `Schema.xml` a `Policy.xml` ze služby `http://smd1mgidm02p:5725`. Osoba má 161 navázaných atributů, skupina 68. Popisné názvy jsou tak, jak je má portál. Standardní pole jsou anglicky, u zakázkových je popisný název často shodný se systémovým.

## Kdo smí atribut změnit

Právo dává request MPR s `GrantRight = True`, které není vypnuté. Tabulka říká, kdo smí změnit už existující objekt. PAM Admins mají na všechny osoby a skupiny parametr `*`, tedy každý atribut. V buňkách je neopakuji.

Další plošná práva, která taky nejsou v každém řádku:

- Service Desk smí osobu nebo skupinu založit a při založení poslat libovolný atribut. MPR se jmenuje `_ZSE_Service_Desk_User_Permission_Read`, operace je Create a parametr je `*`. Formulář vytvoření přesto ukazuje jen standardní pole, takže z portálu ty ostatní při zakládání stejně nevyplní.
- Správci skupin `OU=AzureESK` smí takovou skupinu založit s libovolnými atributy. Při úpravě už jen `AccountName`, `DisplayName`, `Description` a členy.
- Běžný uživatel nemá žádné MPR na změnu vlastní osoby. Smí jen číst.
- Synchronizace není člověk na portálu. V tabulce je uvedená tam, kde její MPR atribut skutečně obsahuje. Není to hvězdička na všechno.

Sloupec **Počítané** říká, jestli hodnotu zapíše action workflow. `ano` znamená zápis na tentýž objekt (`[//Target/atribut]`). `při založení objektu` je Create Resource nové osoby nebo skupiny. `na navázanou kopii` je zápis na objekt v odkazu, typicky `LDAPTarget`. `na objekt z dotazu` je zápis na jinou osobu, skupinu nebo sadu, kterou si workflow nejdřív najde.

Workflow běží jako služba s vypnutou autorizací, takže hodnotu zapíše i když ji na formuláři nikdo měnit nesmí. Formulář úpravy má tato pole stejně editovatelná. Portál zápis odmítne, dokud na něj není MPR.

## Formulář uživatele

RCDC uživatele jsou tři: vytvoření, úprava a zobrazení. Záložky vytvoření jsou šablona Microsoftu. Úprava má navíc schránku na první záložce, záložku ResetUser a záložku LDAPInfo. Zobrazení je ke čtení a má i seznam bezpečnostních skupin. Názvy záložek na portálu jsou anglicky, v závorce je co na nich je. U pole je typ ovládacího prvku.

## Vytvoření uživatele

### General (základní údaje)

- `FirstName` (UocTextBox)
- `LastName` (UocTextBox)
- `MiddleName` (UocTextBox)
- `DisplayName` (UocTextBox)
- `AccountName` (UocTextBox)
- `Domain` (UocDropDownList)
- `MailNickname` (UocTextBox)
- `Photo` (UocFileUpload)
- `JobTitle` (UocTextBox)
- `Department` (UocTextBox)
- `IsRASEnabled` (UocRadioButtonList)
- `TimeZone` (UocDropDownList)

### Work Info (pracovní údaje)

- `EmployeeStartDate` (UocDateTimeControl)
- `EmployeeEndDate` (UocDateTimeControl)
- `EmployeeType` (UocDropDownList)
- `EmployeeID` (UocTextBox)
- `Manager` (UocIdentityPicker)
- `Assistant` (UocIdentityPicker)
- `CostCenter` (UocTextBox)
- `CostCenterName` (UocTextBox)

### Contact Info (kontakt)

- `OfficePhone` (UocTextBox)
- `OfficeFax` (UocTextBox)
- `MobilePhone` (UocTextBox)
- `OfficeLocation` (UocTextBox)
- `Address` (UocTextBox)
- `City` (UocTextBox)
- `PostalCode` (UocTextBox)
- `Country` (UocDropDownList)

### Summary (souhrn)

Souhrn skládá náhled z ostatních polí, vlastní atribut nemá.

## Úprava uživatele

### General (základní údaje)

- `Photo` (UocFileUpload)
- `FirstName` (UocTextBox)
- `LastName` (UocTextBox)
- `DisplayName` (UocTextBox)
- `AccountName` (UocTextBox)
- `Domain` (UocDropDownList)
- `eonMailEnabled` (UocCheckBox)
- `eonMailSuffix` (UocDropDownList)
- `eonLicenseGroup` (UocDropDownList)
- `MailNickname` (UocTextBox)
- `Email` (UocTextBox)
- `JobTitle` (UocTextBox)
- `Department` (UocTextBox)
- `IsRASEnabled` (UocRadioButtonList)

### Work Info (pracovní údaje)

- `EmployeeStartDate` (UocDateTimeControl)
- `EmployeeEndDate` (UocDateTimeControl)
- `EmployeeType` (UocDropDownList)
- `EmployeeID` (UocTextBox)
- `ext_applicant_kid` (UocTextBox)
- `Manager` (UocIdentityPicker)
- `Assistant` (UocIdentityPicker)
- `CostCenter` (UocTextBox)
- `CostCenterName` (UocTextBox)

### Contact Info (kontakt)

- `OfficePhone` (UocTextBox)
- `OfficeFax` (UocTextBox)
- `MobilePhone` (UocTextBox)
- `IPphone` (UocTextBox)
- `OfficeLocation` (UocTextBox)
- `Address` (UocTextBox)
- `City` (UocTextBox)
- `PostalCode` (UocTextBox)
- `Country` (UocDropDownList)

### Group Membership (členství ve skupinách)

- Seznam bezpečnostních skupin, ve kterých osoba je. Filtr je `/Group[(Type='Security') and ((ComputedMember='%ObjectID%') or (ExplicitMember='%ObjectID%'))]`.

### ResetUser (účet, heslo a vznik účtu)

- `Enabled` (UocCheckBox)
- `Locked` (UocCheckBox)
- `ForceChangePassword` (UocCheckBox)
- `ResetPassword` (UocTextBox)
- `CreateADAccount` (UocCheckBox)
- `CreateSecondaryKID` (UocCheckBox)

### LDAPInfo (LDAP a kopie VSE/ZSE)

- `accountNameVSE` (UocTextBox)
- `displayNameVSE` (UocTextBox)
- `distinguishedName` (UocTextBox)
- `distinguishedNameLDAP` (UocTextBox)
- `distinguishedNameVSE` (UocTextBox)
- `LDAPSource` (UocIdentityPicker)
- `LDAPReplace` (UocIdentityPicker)
- `LDAPTarget` (UocIdentityPicker)
- `LDAPTargetZSE` (UocIdentityPicker)
- `advse` (UocTextBox)
- `LDAPdev` (UocTextBox)
- `LDAPprod` (UocTextBox)
- `Externisti` (UocTextBox)
- `humanet` (UocTextBox)
- `upn` (UocTextBox)
- `usrID` (UocTextBox)
- `VSENoSync` (UocTextBox)
- `LDAPsync` (UocTextBox)

### Summary (souhrn)

Souhrn skládá náhled z ostatních polí, vlastní atribut nemá.

## Zobrazení uživatele

Celá stránka je ke čtení. Není na ní ResetUser, LDAPInfo ani schránka. Seznam bezpečnostních skupin na ní je. `IPphone` je na zobrazení i na úpravě, na vytvoření ne.

### General (základní údaje)

- `Photo` (UocPictureBox, jen čtení)
- `FirstName` (UocLabel, jen čtení)
- `LastName` (UocLabel, jen čtení)
- `MiddleName` (UocLabel, jen čtení)
- `DisplayName` (UocLabel, jen čtení)
- `AccountName` (UocLabel, jen čtení)
- `Domain` (UocLabel, jen čtení)
- `MailNickname` (UocLabel, jen čtení)
- `Email` (UocLabel, jen čtení)
- `JobTitle` (UocLabel, jen čtení)
- `Department` (UocLabel, jen čtení)

### Work Info (pracovní údaje)

- `EmployeeStartDate` (UocDateTimeControl, jen čtení)
- `EmployeeEndDate` (UocDateTimeControl, jen čtení)
- `EmployeeType` (UocLabel, jen čtení)
- `EmployeeID` (UocLabel, jen čtení)
- `Manager` (UocHyperLink)
- `Assistant` (UocHyperLink)
- `CostCenter` (UocLabel, jen čtení)
- `CostCenterName` (UocLabel, jen čtení)
- `IsRASEnabled` (UocRadioButtonList, jen čtení)

### Contact Info (kontakt)

- `OfficePhone` (UocLabel, jen čtení)
- `OfficeFax` (UocLabel, jen čtení)
- `MobilePhone` (UocLabel, jen čtení)
- `IPphone` (UocLabel, jen čtení)
- `OfficeLocation` (UocLabel, jen čtení)
- `Address` (UocLabel, jen čtení)
- `City` (UocLabel, jen čtení)
- `PostalCode` (UocLabel, jen čtení)
- `Country` (UocLabel, jen čtení)

### Group Membership (členství ve skupinách)

- Seznam bezpečnostních skupin, ve kterých osoba je. Filtr je `/Group[(Type='Security') and ((ComputedMember='%ObjectID%') or (ExplicitMember='%ObjectID%'))]`.

## Formulář skupiny

RCDC skupiny je šablona Microsoftu. Zakázkové atributy (DN, VSE, LDAP, `adminDescription`) na formuláři nejsou. Na vytvoření je zaškrtávátko e-mailu a přepínač typu členství bez vlastního atributu. E-mail a doména jsou při úpravě jen ke čtení. Členové se při vytvoření vybírají do `ExplicitMember`. Při úpravě jdou přes seznamy MemberToAdd a MemberToRemove a portál z toho udělá Add nebo Remove na `ExplicitMember`. Vlastníci jdou do `Owner` a `DisplayedOwner`.

Záložka Members je v RCDC dvakrát za sebou, na portálu je to jedna záložka. První blok jsou ručně vedení členové, druhý je dynamický filtr.

## Vytvoření skupiny

### General (základní údaje)

- `DisplayName` (UocTextBox)
- `MailNickname` (UocTextBox)
- `Domain` (UocDropDownList)
- `AccountName` (UocTextBox)
- `Scope` (UocDropDownList)
- `Description` (UocTextBox)

### Members (členové)

- `ExplicitMember` (UocIdentityPicker)

### Members (členové)

- Seznam dopočítaných členů
- Seznam členů, které dynamická skupina nebere
- `Filter` (UocFilterBuilder)

### Owners (vlastníci)

- `Owner` (UocIdentityPicker)
- `DisplayedOwner` (UocIdentityPicker)
- `MembershipAddWorkflow` (UocRadioButtonList)

### Summary (souhrn)

Souhrn skládá náhled z ostatních polí, vlastní atribut nemá.

## Úprava skupiny

### General (základní údaje)

- `DisplayName` (UocTextBox)
- `MailNickname` (UocTextBox)
- `Email` (UocTextBox, jen čtení)
- `Domain` (UocLabel, jen čtení)
- `AccountName` (UocTextBox)
- `Scope` (UocDropDownList)
- `Description` (UocTextBox)

### Members (členové)

- Seznam ručně přidaných členů
- Seznam členů, které statická skupina nebere

### Members (členové)

- Seznam dopočítaných členů
- Seznam členů, které dynamická skupina nebere
- `Filter` (UocFilterBuilder)

### Owners (vlastníci)

- `Owner` (UocIdentityPicker)
- `DisplayedOwner` (UocIdentityPicker)
- `MembershipAddWorkflow` (UocRadioButtonList)

### Summary (souhrn)

Souhrn skládá náhled z ostatních polí, vlastní atribut nemá.

## Zobrazení skupiny

### General (základní údaje)

- `DisplayName` (UocLabel, jen čtení)
- `MailNickname` (UocLabel, jen čtení)
- `Email` (UocLabel, jen čtení)
- `Domain` (UocLabel, jen čtení)
- `AccountName` (UocLabel, jen čtení)
- `Scope` (UocLabel, jen čtení)
- `Description` (UocLabel, jen čtení)

### Members (členové)

- Seznam ručně přidaných členů
- Seznam členů, které statická skupina nebere

### Members (členové)

- Seznam dopočítaných členů
- Seznam členů, které dynamická skupina nebere
- `Filter` (UocFilterBuilder, jen čtení)

### Owners (vlastníci)

- `Owner` (UocIdentityPicker, jen čtení)
- `DisplayedOwner` (UocHyperLink)
- `MembershipAddWorkflow` (UocLabel, jen čtení)

## Atributy osoby

| Systémový název | Popisný název | Typ | Kdo smí měnit | Počítané | Záložka |
| --- | --- | --- | --- | --- | --- |
| `Photo` | Photo | Binary | administrátoři, synchronizace | ne | General |
| `FirstName` | First Name | String | administrátoři, synchronizace | ano: _VSE_Generate_KID_AD_Account_X; při založení objektu: _VSE_Copy_users_create, _ZSE_Create_Secondary_Account; na navázanou kopii: _VSE_Copy_users_update přes LDAPTarget | General |
| `LastName` | Last Name | String | administrátoři, synchronizace | ano: _VSE_Generate_KID_AD_Account_X; při založení objektu: _VSE_Copy_users_create, _ZSE_Create_Secondary_Account; na navázanou kopii: _VSE_Copy_users_update přes LDAPTarget | General |
| `DisplayName` | Display Name | String | administrátoři, synchronizace | ano: _VSE_Generate_KID_AD_Account_X, _VSE_pridaj_atributy_pre_vse_only_ucty, _VSE_Users_Add_Display_Name; při založení objektu: _VSE_Copy_groups_create(VSE), _VSE_Copy_groups_create(ZSE) a dalších 3; na navázanou kopii: _VSE_Copy_groups_update přes LDAPTarget, _VSE_Copy_users_update přes LDAPTarget | General |
| `AccountName` | Account Name. User's log on name | String | administrátoři, synchronizace | ano: _ZSE_Generate_KID, _VSE_Generate_KID_AD_Account, _VSE_Generate_KID_AD_Account_X; při založení objektu: _VSE_Copy_groups_create(VSE), _VSE_Copy_groups_create(ZSE) a dalších 2; na navázanou kopii: _VSE_Copy_groups_update přes LDAPTarget, _VSE_Copy_users_update přes LDAPTarget | General |
| `Domain` | Domain. Choose the domain where you want to create the user account for this user | String | administrátoři, synchronizace | při založení objektu: _VSE_Copy_groups_create(VSE), _VSE_Copy_groups_create(ZSE) a dalších 1; na navázanou kopii: _VSE_Copy_groups_update přes LDAPTarget | General |
| `eonMailEnabled` | E-mail enabled | Boolean | správa schránek | ano: _ZSE_Create_Mailbox | jen úprava: General |
| `eonMailSuffix` | E-mail suffix | String | správa schránek | ano: _ZSE_Create_Mailbox | jen úprava: General |
| `eonLicenseGroup` | License group | String | správa schránek | ano: _ZSE_Create_Mailbox | jen úprava: General |
| `MailNickname` | E-mail Alias. E-mail alias. It is used to create the e-mail address | String | administrátoři, synchronizace | ne | General |
| `Email` | E-mail. Primary e-mail address for the user | String | Service Desk (osoba I nebo E), administrátoři, synchronizace | ano: _ZSE_Externisti_doplnenie_udajov_od_ziadatela, _ZSE_Externisti_email; na navázanou kopii: _VSE_Copy_users_update přes LDAPTarget | jen úprava: General |
| `JobTitle` | Job Title | String | administrátoři, synchronizace | na navázanou kopii: _VSE_Copy_users_update přes LDAPTarget | General |
| `Department` | Department | String | administrátoři, synchronizace | na navázanou kopii: _VSE_Copy_users_update přes LDAPTarget | General |
| `IsRASEnabled` | RAS Access Permission | Boolean | administrátoři, synchronizace | ne | General |
| `EmployeeStartDate` | Employee Start Date | DateTime | administrátoři, synchronizace | ne | Work Info |
| `EmployeeEndDate` | Employee End Date | DateTime | Service Desk (osoba I nebo E), administrátoři, synchronizace | ano: _ZSE_SAPCUA_Unsync | Work Info |
| `EmployeeType` | Employee Type | String | administrátoři, synchronizace | ano: _VSE_Users_XVSB; při založení objektu: _VSE_Copy_users_create; na navázanou kopii: _VSE_Copy_users_update přes LDAPTarget | Work Info |
| `EmployeeID` | Employee ID | String | administrátoři, synchronizace | ano: _VSE_Generate_KID_AD_Account_X; při založení objektu: _VSE_Copy_users_create; na navázanou kopii: _VSE_Copy_users_update přes LDAPTarget | Work Info |
| `ext_applicant_kid` | Externisti Applicant KID | String | Service Desk (externista), synchronizace | ano: _ZSE_Externisti_dopln_ext_applicant_kid | jen úprava: Work Info |
| `Manager` | Manager | Reference | administrátoři, synchronizace | ano: _ZSE_Externisti_doplnenie_udajov_od_ziadatela, _ZSE_Externisti_doplnenie_udajov_od_zmena_manazera | Work Info |
| `Assistant` | Assistant | Reference | administrátoři, synchronizace | ne | Work Info |
| `CostCenter` | Cost Center | String | administrátoři, synchronizace | ne | Work Info |
| `CostCenterName` | Cost Center Name | String | administrátoři, synchronizace | ne | Work Info |
| `OfficePhone` | Office Phone | String | Service Desk (osoba I nebo E), Service Desk (sekundární účet), administrátoři, synchronizace | ne | Contact Info |
| `OfficeFax` | Fax | String | Service Desk (osoba I nebo E), Service Desk (sekundární účet), administrátoři, synchronizace | ne | Contact Info |
| `MobilePhone` | Mobile Phone | String | Service Desk (osoba I nebo E), Service Desk (sekundární účet), administrátoři, synchronizace | ano: _ZSE_Externisti_phone; při založení objektu: _VSE_Copy_users_create; na navázanou kopii: _VSE_Copy_users_update přes LDAPTarget | Contact Info |
| `IPphone` | IP phone. telefon pre Teams | String | Service Desk (osoba I nebo E), synchronizace | ne | jen úprava: Contact Info |
| `OfficeLocation` | Office Location | String | administrátoři, synchronizace | ne | Contact Info |
| `Address` | Address | String | administrátoři, synchronizace | ne | Contact Info |
| `City` | City | String | administrátoři, synchronizace | ne | Contact Info |
| `PostalCode` | Postal Code | String | administrátoři, synchronizace | ne | Contact Info |
| `Country` | Country/Region | String | administrátoři, synchronizace | ano: _ZSE_set_Country | Contact Info |
| `Enabled` | Enabled. User is enabled in AD | Boolean | Service Desk (osoba I nebo E), Service Desk (sekundární účet), synchronizace | ne | jen úprava: ResetUser |
| `Locked` | Locked. user is locked in AD | Boolean | Service Desk (osoba I nebo E), Service Desk (sekundární účet), synchronizace | ne | jen úprava: ResetUser |
| `ForceChangePassword` | ForceChangePassword. Force change password after password reset | Boolean | Service Desk (osoba I nebo E), Service Desk (sekundární účet) | ne | jen úprava: ResetUser |
| `ResetPassword` | Reset Password. This attribute is used to trigger a password reset process. | String | Service Desk (osoba I nebo E), Service Desk (sekundární účet), administrátoři, synchronizace | ne | jen úprava: ResetUser |
| `CreateADAccount` | CreateADAccount | Boolean | Service Desk (osoba I nebo E), administrátoři | ne | jen úprava: ResetUser |
| `CreateSecondaryKID` | CreateSecondaryKID | Boolean | Service Desk (osoba I nebo E) | ano: _ZSE_Create_Secondary_Account | jen úprava: ResetUser |
| `accountNameVSE` | accountNameVSE | String | synchronizace | ano: _VSE_copy_groups_VSEIntranet; při založení objektu: _VSE_Copy_groups_create(VSE), _VSE_Copy_groups_create(ZSE) a dalších 2; na navázanou kopii: _VSE_Copy_groups_update přes LDAPTarget, _VSE_Copy_users_update přes LDAPTarget | jen úprava: LDAPInfo |
| `displayNameVSE` | displayNameVSE | String | synchronizace | při založení objektu: _VSE_Copy_users_create | jen úprava: LDAPInfo |
| `distinguishedName` | distinguishedName. ZSE Active directory Distinguished Name | String | administrátoři, synchronizace | ano: _ZSE_Generate_KID, _VSE_Generate_KID_AD_Account, _VSE_Generate_KID_AD_Account_X a dalších 3 | jen úprava: LDAPInfo |
| `distinguishedNameLDAP` | distinguishedNameLDAP | String | synchronizace | ano: _VSE_user_no_ad.vse.sk, _ZSE_users_Externisti_update_VSE_LDAP_distinguishedName, _ZSE_users_update_VSE_LDAP_distinguishedName | jen úprava: LDAPInfo |
| `distinguishedNameVSE` | distinguishedNameVSE | String | synchronizace | při založení objektu: _VSE_Copy_groups_create(VSE), _VSE_Copy_groups_create(ZSE) a dalších 2; na navázanou kopii: _VSE_Copy_groups_update přes LDAPTarget | jen úprava: LDAPInfo |
| `LDAPSource` | LDAPSource | Reference | jen PAM Admins | při založení objektu: _VSE_Copy_groups_create(VSE), _VSE_Copy_users_create; na objekt z dotazu: _VSE_copy_fix_group_missing_LDAPTarget | jen úprava: LDAPInfo |
| `LDAPReplace` | LDAPReplace | Reference | jen PAM Admins | ano: _VSE_copy_fix_group_missing_LDAPTarget, _VSE_Copy_groups_create(ZSE v2 back link), _VSE_Copy_groups_ZSE_Only_stage0 a dalších 4; na navázanou kopii: _VSE_Copy_groups_create(ZSE v2 back link) přes LDAPSourceZSE, _VSE_Copy_groups_ZSE_Only_stage0 přes LDAPSource a dalších 1; na objekt z dotazu: _VSE_copy_fix_group_missing_LDAPTarget | jen úprava: LDAPInfo |
| `LDAPTarget` | LDAPTarget | Reference | jen PAM Admins | ano: _VSE_copy_fix_group_missing_LDAPTarget; na navázanou kopii: _VSE_Copy_groups_ZSE_Only_stage0 přes LDAPSource, _VSE_Copy_users_write_back přes LDAPSource | jen úprava: LDAPInfo |
| `LDAPTargetZSE` | LDAPTargetZSE | Reference | jen PAM Admins | ano: _VSE_Copy_groups_create(ZSE), _VSE_Copy_groups_create(ZSE v2 back link), _VSE_Copy_groups_create(ZSE v2) a dalších 3; na navázanou kopii: _VSE_Copy_groups_create(ZSE v2 back link) přes LDAPSourceZSE, _VSE_Copy_users_write_back přes LDAPSource | jen úprava: LDAPInfo |
| `advse` | advse | String | synchronizace | ne | jen úprava: LDAPInfo |
| `LDAPdev` | LDAPdev | String | synchronizace | ne | jen úprava: LDAPInfo |
| `LDAPprod` | LDAPprod | String | synchronizace | ne | jen úprava: LDAPInfo |
| `Externisti` | Externisti. priznak externistu vytvoreny z externistov alebo pripojeny z externistov | String | synchronizace | ne | jen úprava: LDAPInfo |
| `humanet` | humanet | String | synchronizace | ne | jen úprava: LDAPInfo |
| `upn` | upn | String | synchronizace | ano: _VSE_Users_XVSB; při založení objektu: _VSE_Copy_users_create; na navázanou kopii: _VSE_Copy_users_update přes LDAPTarget | jen úprava: LDAPInfo |
| `usrID` | usrID | String | synchronizace | ano: VSE_CUA_usrID, Externisti_CUA_zse_usrID, ZSE_CUA_usrID | jen úprava: LDAPInfo |
| `VSENoSync` | VSENoSync | String | synchronizace | ano: _VSE_user_no_ad.vse.sk, _VSE_Users_Transition(ZSEOnly - VSEZSE), _ZSE_users_Externisti_update_VSE_LDAP_distinguishedName; při založení objektu: _VSE_Copy_groups_create(ZSE), _VSE_Copy_groups_create(ZSE v2) | jen úprava: LDAPInfo |
| `LDAPsync` | LDAPsync | String | jen PAM Admins | ne | jen úprava: LDAPInfo |
| `MiddleName` | Middle Name | String | administrátoři, synchronizace | ne | vytvoření a zobrazení, ne úprava |
| `TimeZone` | Time Zone. Reference to timezone configuration | Reference | administrátoři, synchronizace | ne | jen vytvoření: General |
| `AccountName1` | Account Name1 | String | synchronizace | ne | není na formuláři |
| `AD_UserCannotChangePassword` | AD User Cannot Change Password. Will sync from AD to track whether the user is locked out from changing their AD password | Boolean | administrátoři, synchronizace | ne | není na formuláři |
| `AuthNLockoutRegistrationID` | Lockout Gate Registration Data Ids. This is the list of gate registration ids used by the lockout gate | Reference, více hodnot | administrátoři, synchronizace | ne | není na formuláři |
| `AuthNWFLockedOut` | AuthN Workflow Locked Out. This is the list of AuthN Processes a user is locked out of | Reference, více hodnot | administrátoři, synchronizace | ne | není na formuláři |
| `AuthNWFRegistered` | AuthN Workflow Registered. This is the list of AuthN Processes a user is registered for | Reference, více hodnot | administrátoři, synchronizace | ne | není na formuláři |
| `bukrs` | bukrs | String | administrátoři, synchronizace | ne | není na formuláři |
| `c` | c | String | synchronizace | ne | není na formuláři |
| `cn` | cn | String | synchronizace | ne | není na formuláři |
| `cnVSE` | cnVSE | String | synchronizace | při založení objektu: _VSE_Copy_groups_create(VSE); na navázanou kopii: _VSE_Copy_groups_update přes LDAPTarget | není na formuláři |
| `Company` | Company | String | administrátoři, synchronizace | ne | není na formuláři |
| `CreatedTime` | Created Time | DateTime, povinný | jen PAM Admins | ne | není na formuláři |
| `Creator` | Creator | Reference | jen PAM Admins | ne | není na formuláři |
| `DeletedTime` | Deleted Time | DateTime | jen PAM Admins | ne | není na formuláři |
| `DepartmentNumber` | DepartmentNumber | String | synchronizace | ne | není na formuláři |
| `Description` | Description | String | administrátoři, synchronizace | ano: __test_fix_externisti; na objekt z dotazu: _VSE_Copy_groups_create(ZSE v2) | není na formuláři |
| `DetectedRulesList` | Detected Rules List. The synchronization rules detected for resources in external systems. | Reference, více hodnot | synchronizace | ne | není na formuláři |
| `displayNameFull` | displayNameFull | String | synchronizace | ne | není na formuláři |
| `distinguishedNameLDAPdev` | distinguishedNameLDAPdev | String | synchronizace | ne | není na formuláři |
| `distinguishedNameLDAPprod` | distinguishedNameLDAPprod | String | synchronizace | ne | není na formuláři |
| `DomainConfiguration` | Domain Configuration. A reference to a the parent Domain resource for this resource. | Reference | administrátoři, synchronizace | ne | není na formuláři |
| `EmailVSE` | EmailVSE | String | synchronizace | ne | není na formuláři |
| `EmployeeEndDateCUA` | Employee End Date (CUA) | String | synchronizace | ano: _ZSE_SAPCUA_Unsync | není na formuláři |
| `EmployeeIDVSE` | EmployeeIDVSE | String | synchronizace | při založení objektu: _VSE_Copy_users_create | není na formuláři |
| `employeeNumber` | employeeNumber | String | synchronizace | ne | není na formuláři |
| `eonBUShort` | eonBUShort | String | synchronizace | ne | není na formuláři |
| `eonBusinessUnitID` | eonBusinessUnitID | String | synchronizace | ne | není na formuláři |
| `eonCID` | eonCID | String | synchronizace | ne | není na formuláři |
| `eonCIDs` | eonCIDs | String, více hodnot | synchronizace | ne | není na formuláři |
| `eonDepartmentID` | eonDepartmentID | String | synchronizace | ne | není na formuláři |
| `eonExtensionAttribute35` | eonExtensionAttribute35 | String | synchronizace | ne | není na formuláři |
| `eonExtensionAttribute36` | eonExtensionAttribute36 | String | synchronizace | ne | není na formuláři |
| `eonMUShort` | eonMUShort | String | synchronizace | ne | není na formuláři |
| `eonOrganization` | eonOrganization | String | synchronizace | ne | není na formuláři |
| `eonOrganizationID` | eonOrganizationID | String | synchronizace | ne | není na formuláři |
| `eonStatus` | eonStatus | String | synchronizace | ano: _ZSE_Externisti_Aktivny_stat2_3_eonStatus_A, _ZSE_Externisti_Expirovany_stat2_0_eonStatus_I; při založení objektu: _VSE_Copy_users_create; na navázanou kopii: _VSE_Copy_users_update přes LDAPTarget | není na formuláři |
| `eonStatusChangeTime` | eonStatusChangeTime | DateTime | synchronizace | ne | není na formuláři |
| `eonUserPurpose` | eonUserPurpose | String | synchronizace | ne | není na formuláři |
| `eonUserType` | eonUserType | String | synchronizace | ano: _VSE_Generate_KID_AD_Account_X; při založení objektu: _ZSE_Create_Secondary_Account | není na formuláři |
| `ExpectedRulesList` | Expected Rules List | Reference, více hodnot | synchronizace | ne | není na formuláři |
| `ExpirationTime` | Expiration Time | DateTime | administrátoři, synchronizace | ne | není na formuláři |
| `ExportToSAPCUA` | SAPCUA. vytvori uzivatel v SAPCUA | Boolean | administrátoři, synchronizace | ano: _ZSE_SAPCUA_Sync | není na formuláři |
| `ext_applicant_email` | ext_applicant_email | String | jen PAM Admins | ano: _ZSE_Externisti_doplnenie_udajov_od_ziadatela, _ZSE_Externisti_doplnenie_udajov_od_zmena_manazera | není na formuláři |
| `ext_applicant_empliyeeid` | ext_applicant_empliyeeid | String | synchronizace | ano: _ZSE_Externisti_doplnenie_udajov_od_ziadatela, _ZSE_Externisti_doplnenie_udajov_od_zmena_manazera | není na formuláři |
| `ext_applicant_kid1` | ext_applicant_kid1 | String | Service Desk (externista), synchronizace | ne | není na formuláři |
| `ext_applicant_name` | ext_applicant_name | String | synchronizace | ano: _ZSE_Externisti_doplnenie_udajov_od_zmena_manazera | není na formuláři |
| `ext_applicant_platnost` | ext_applicant_platnost | Boolean | jen PAM Admins | ano: _ZSE_Externisti_doplnenie_udajov_od_zmena_manazera | není na formuláři |
| `ext_extern_email` | ext_extern_email | String | synchronizace | ne | není na formuláři |
| `ext_id` | ext_id | Integer | synchronizace | ne | není na formuláři |
| `extensionAttribute10` | extensionAttribute10 | String | synchronizace | při založení objektu: _VSE_Copy_users_create; na navázanou kopii: _VSE_Copy_users_update přes LDAPTarget | není na formuláři |
| `extensionAttribute8` | extensionAttribute8 | String | synchronizace | ano: _ZSE_Externisti_ESK_Azure | není na formuláři |
| `ExternistiProcessed` | Externisti Processed | String | synchronizace | ano: _ZSE_Externisti_Update_Procesed | není na formuláři |
| `FirstNameEN` | FirstNameEN | String | jen PAM Admins | ano: _ZSE_update_FirstNameEN_LastNameEN | není na formuláři |
| `FirstNameVSE` | FirstNameVSE | String | synchronizace | při založení objektu: _VSE_Copy_users_create; na navázanou kopii: _VSE_Copy_users_update přes LDAPTarget | není na formuláři |
| `FreezeCount` | Freeze Count | Integer | administrátoři, synchronizace | ne | není na formuláři |
| `FreezeLevel` | Freeze Level. Tracks the number of times the user has unsuccessfully attempted to run an AuthN WF | String | administrátoři, synchronizace | ne | není na formuláři |
| `homeDirectory` | homeDirectory | String | synchronizace | ne | není na formuláři |
| `idNS` | idNS | String | synchronizace | ano: _ZSE_Externisti_doplnenie_udajov_od_ziadatela | není na formuláři |
| `KOC` | KOC | String | synchronizace | ano: _ZSE_externisti_set_KOC_To_1, _ZSE_externisti_set_KOC_To_0 | není na formuláři |
| `Kostl` | Kostl | String | synchronizace | ano: _ZSE_Externisti_doplnenie_udajov_od_ziadatela | není na formuláři |
| `LastNameEN` | LastNameEN | String | jen PAM Admins | ano: _ZSE_update_FirstNameEN_LastNameEN | není na formuláři |
| `LastNameVSE` | LastNameVSE | String | synchronizace | při založení objektu: _VSE_Copy_users_create; na navázanou kopii: _VSE_Copy_users_update přes LDAPTarget | není na formuláři |
| `LastResetAttemptTime` | Last Reset Attempt Time | DateTime | administrátoři, synchronizace | ne | není na formuláři |
| `LDAPkontrola` | LDAPkontrola | String | jen PAM Admins | ano: _VSE_users_group_kontrola_Provisionovany_do_dev_aj_prod | není na formuláři |
| `LDAPnew` | LDAPnew | String | synchronizace | ne | není na formuláři |
| `LDAPZSEOnly` | LDAPZSEOnly | String | jen PAM Admins | ne | není na formuláři |
| `Locale` | Locale. The region and language for which the representation of a resource has been adapted. | String | jen PAM Admins | ne | není na formuláři |
| `LoginName` | Login Name. This is a combination for domain/Alias | String | administrátoři, synchronizace | ne | není na formuláři |
| `menoOJ` | menoOJ | String | synchronizace | ne | není na formuláři |
| `mobile` | mobile | String | synchronizace | při založení objektu: _VSE_Copy_users_create; na navázanou kopii: _VSE_Copy_users_update přes LDAPTarget | není na formuláři |
| `MobilePhone1` | MobilePhone1 | String | synchronizace | ne | není na formuláři |
| `msDS-SourceAnchor` | msDS-SourceAnchor. kdi-vse | String | synchronizace | ne | není na formuláři |
| `msidmDomain` | Domain FQDN. The FQDN of the domain where the user is located | String | jen PAM Admins | ne | není na formuláři |
| `msidmMFAPINCode` | MFA PIN Code. The PIN code that should be used during Multi-Factor Authentication process | String | administrátoři | ne | není na formuláři |
| `msidmOneTimePasswordEmailAddress` | One-Time Password Email Address. Email address used to deliver a one-time password to the user. | String | jen PAM Admins | ne | není na formuláři |
| `msidmOneTimePasswordMobilePhone` | One-Time Password Mobile Phone. Mobile phone number used to deliver a one-time password to the user. | String | jen PAM Admins | ne | není na formuláři |
| `msidmPamLinkedUser` | PAM Linked User. The User Identity for PAM requests to use | Reference | administrátoři | ne | není na formuláři |
| `msidmPhoneGatePhoneNumber` | Phone Gate Phone Number. Phone number used to Phone Gate Authentication. | String | administrátoři | ne | není na formuláři |
| `MVObjectID` | MV Resource ID. The GUID of an entry in the FIM metaverse corresponding to this resource. | String | synchronizace | ne | není na formuláři |
| `ObjectID` | Resource ID | Reference | jen PAM Admins | ne | není na formuláři |
| `ObjectSID` | Resource SID | Binary | administrátoři, synchronizace | ne | není na formuláři |
| `ObjectSIDVSE` | ObjectSIDVSE | Binary | synchronizace | při založení objektu: _VSE_Copy_users_create; na navázanou kopii: _VSE_Copy_users_update přes LDAPTarget | není na formuláři |
| `ObjectType` | Resource Type | String, povinný | administrátoři, synchronizace | ne | není na formuláři |
| `OrgOJParent` | OrgOJParent | String | jen PAM Admins | ne | není na formuláři |
| `ou` | ou | String | synchronizace | ne | není na formuláři |
| `ouldap` | ouldap | String | synchronizace | ne | není na formuláři |
| `password` | password. reset password value | String | synchronizace | ne | není na formuláři |
| `persg` | persg | String | administrátoři, synchronizace | ne | není na formuláři |
| `persk` | persk | String | administrátoři, synchronizace | při založení objektu: _VSE_Copy_users_create; na navázanou kopii: _VSE_Copy_users_update přes LDAPTarget | není na formuláři |
| `personalTitle` | personalTitle | String | synchronizace | při založení objektu: _VSE_Copy_users_create; na navázanou kopii: _VSE_Copy_users_update přes LDAPTarget | není na formuláři |
| `Primary` | Primary | Reference | jen PAM Admins | ano: _VSE_Generate_KID_AD_Account_X | není na formuláři |
| `proxy` | proxy. attribute for proxy policy | String | jen PAM Admins | na objekt z dotazu: _ZSE_Update_proxy_settings_In_Out | není na formuláři |
| `ProxyAddressCollection` | Proxy Address Collection | String, více hodnot | administrátoři, synchronizace | ne | není na formuláři |
| `Register` | Register | Boolean | administrátoři, synchronizace | ne | není na formuláři |
| `RegistrationRequired` | Registration Required. Tracks if the user must register for SSPR | Boolean | administrátoři, synchronizace | ne | není na formuláři |
| `ResourceTime` | Resource Time | DateTime | jen PAM Admins | ne | není na formuláři |
| `scriptPath` | scriptPath | String | administrátoři, synchronizace | ano: __test_Update_scriptPath, _ZSE_Generate_KID, _VSE_Generate_KID_AD_Account a dalších 1 | není na formuláři |
| `SIDHistory` | SID History. Contains previous SIDs used for the resource if the resource was moved from another domain. | Binary, více hodnot | synchronizace | ne | není na formuláři |
| `stat2` | stat2 | String | administrátoři, synchronizace | ano: _ZSE_Externisti_Aktivny_stat2_3_eonStatus_A, _ZSE_Externisti_Expirovany_stat2_0_eonStatus_I | není na formuláři |
| `userAccountControl` | userAccountControl | Integer | synchronizace | ano: _ZSE_ALL_Zamestnanci_stat2-0_userAccountControl-512; při založení objektu: _VSE_Copy_users_create; na navázanou kopii: _VSE_Copy_users_update přes LDAPTarget | není na formuláři |
| `userAccountControlVSE` | userAccountControlVSE | Integer | synchronizace | při založení objektu: _VSE_Copy_users_create; na navázanou kopii: _VSE_Copy_users_update přes LDAPTarget | není na formuláři |
| `userPrincipalName` | userPrincipalName | String | synchronizace | ano: __test_fix_userPrincipalName, _ZSE_Generate_KID, _VSE_Generate_KID_AD_Account a dalších 2; při založení objektu: _VSE_Copy_users_create; na navázanou kopii: _VSE_Copy_users_update přes LDAPTarget | není na formuláři |
| `userPrincipalNameVSE` | userPrincipalNameVSE | String | synchronizace | při založení objektu: _VSE_Copy_users_create; na navázanou kopii: _VSE_Copy_users_update přes LDAPTarget | není na formuláři |
| `Werks` | Werks | String | synchronizace | ano: _ZSE_Externisti_doplnenie_udajov_od_ziadatela | není na formuláři |

## Atributy skupiny

| Systémový název | Popisný název | Typ | Kdo smí měnit | Počítané | Záložka |
| --- | --- | --- | --- | --- | --- |
| `DisplayName` | Display Name | String | správci skupin, správci Azure ESK, synchronizace | ano: _VSE_Generate_KID_AD_Account_X, _VSE_pridaj_atributy_pre_vse_only_ucty, _VSE_Users_Add_Display_Name; při založení objektu: _VSE_Copy_groups_create(VSE), _VSE_Copy_groups_create(ZSE) a dalších 3; na navázanou kopii: _VSE_Copy_groups_update přes LDAPTarget, _VSE_Copy_users_update přes LDAPTarget | General |
| `MailNickname` | E-mail Alias. E-mail alias. It is used to create the e-mail address | String | správci skupin, synchronizace | ne | General |
| `Email` | E-mail. Primary email address for the group. | String | správci skupin, synchronizace | ano: _ZSE_Externisti_doplnenie_udajov_od_ziadatela, _ZSE_Externisti_email; na navázanou kopii: _VSE_Copy_users_update přes LDAPTarget | jen úprava: General, jen čtení |
| `Domain` | Domain. Choose the domain where you want to create the user account for this user | String, povinný | správci skupin, synchronizace | při založení objektu: _VSE_Copy_groups_create(VSE), _VSE_Copy_groups_create(ZSE) a dalších 1; na navázanou kopii: _VSE_Copy_groups_update přes LDAPTarget | General, při úpravě jen čtení |
| `AccountName` | Account Name. User's log on name | String | správci skupin, správci Azure ESK, synchronizace | ano: _ZSE_Generate_KID, _VSE_Generate_KID_AD_Account, _VSE_Generate_KID_AD_Account_X; při založení objektu: _VSE_Copy_groups_create(VSE), _VSE_Copy_groups_create(ZSE) a dalších 2; na navázanou kopii: _VSE_Copy_groups_update přes LDAPTarget, _VSE_Copy_users_update přes LDAPTarget | General |
| `Scope` | Scope | String, povinný | správci skupin, synchronizace | při založení objektu: _VSE_Copy_groups_create(VSE), _VSE_Copy_groups_create(ZSE) a dalších 1; na navázanou kopii: _VSE_Copy_groups_update přes LDAPTarget | General |
| `Description` | Description | String | správci skupin, správci Azure ESK, synchronizace | ano: __test_fix_externisti; na objekt z dotazu: _VSE_Copy_groups_create(ZSE v2) | General |
| `Filter` | Filter. A predicate defining a subset of the resources. | Text | správci skupin, synchronizace | ne | Members |
| `Owner` | Owner | Reference, více hodnot | správci skupin, synchronizace | ano: _ZSE_DFS_Owner; při založení objektu: _VSE_Copy_groups_create(ZSE v2); na navázanou kopii: _VSE_Copy_groups_create(ZSE v2 back link) přes LDAPSourceZSE; na objekt z dotazu: _ZSE_DFS_Owner | Owners |
| `DisplayedOwner` | Displayed Owner | Reference | správci skupin, synchronizace | ano: _ZSE_DFS_Owner; při založení objektu: _VSE_Copy_groups_create(ZSE v2); na navázanou kopii: _VSE_Copy_groups_create(ZSE v2 back link) přes LDAPSourceZSE; na objekt z dotazu: _ZSE_DFS_Owner | Owners |
| `MembershipAddWorkflow` | Membership Add Workflow | String, povinný | správci skupin, synchronizace | ano: _ZSE_Group_Set_adminDescription_MembershipLocked_; při založení objektu: _VSE_Copy_groups_create(VSE), _VSE_Copy_groups_create(ZSE) a dalších 1 | Owners |
| `ExplicitMember` | Manually-managed Membership. Members in the group that are manually managed. | Reference, více hodnot | správci skupin, uživatelé bezpečnostních skupin (otevřené a owner-approved), vlastník bezpečnostní skupiny, Service Desk (řídicí skupiny, DFS r a DFS rw), správci Azure ESK CS (jen členové), účet MIM Action (všechny skupiny), správci Azure ESK, synchronizace | ano: _VSE_Copy_group_update_members_LDAPTarget_flag, _VSE_Copy_group_final_update, _VSE_Copy_groups_ZSE_Only_stage1 a dalších 1; při založení objektu: _VSE_Copy_groups_create(VSE), _VSE_Copy_groups_create(ZSE) a dalších 1; na navázanou kopii: _VSE_Copy_groups_stage1_copy_members(ZSE v2 Only) přes LDAPTargetZSEOnly, _VSE_Copy_groups_stage1_copy_members(ZSE v2 Both) přes LDAPTargetZSE a dalších 2; na objekt z dotazu: Add_to_SAP, _ZSE_SAPCUA_Sync a dalších 2 | vytvoření: Members; úprava přes seznam členů |
| `accountNameVSE` | accountNameVSE | String | synchronizace | ano: _VSE_copy_groups_VSEIntranet; při založení objektu: _VSE_Copy_groups_create(VSE), _VSE_Copy_groups_create(ZSE) a dalších 2; na navázanou kopii: _VSE_Copy_groups_update přes LDAPTarget, _VSE_Copy_users_update přes LDAPTarget | není na formuláři |
| `adminDescription` | adminDescription. skupina rozdelenie | String | synchronizace | ano: _VSE_Copy_create_OU=Diamond,OU=Autorizacia,OU=GPOGroups,DC=ad,DC=vse,DC=sk, _ZSE_Group_Set_adminDescription_MembershipLocked_, _ZSE_group_Set_AdminDescription-TYPMIMManualne_from_AD | není na formuláři |
| `advse` | advse | String | synchronizace | ne | není na formuláři |
| `cn` | cn | String | synchronizace | ne | není na formuláři |
| `cnVSE` | cnVSE | String | synchronizace | při založení objektu: _VSE_Copy_groups_create(VSE); na navázanou kopii: _VSE_Copy_groups_update přes LDAPTarget | není na formuláři |
| `ComputedMember` | Computed Member. Resources in the set that are computed from the membership filter | Reference, více hodnot | synchronizace | ne | není na formuláři |
| `CreatedTime` | Created Time | DateTime, povinný | jen PAM Admins | ne | není na formuláři |
| `Creator` | Creator | Reference | jen PAM Admins | ne | není na formuláři |
| `DeletedTime` | Deleted Time | DateTime | jen PAM Admins | ne | není na formuláři |
| `DetectedRulesList` | Detected Rules List. The synchronization rules detected for resources in external systems. | Reference, více hodnot | synchronizace | ne | není na formuláři |
| `distinguishedName` | distinguishedName. ZSE Active directory Distinguished Name | String | synchronizace | ano: _ZSE_Generate_KID, _VSE_Generate_KID_AD_Account, _VSE_Generate_KID_AD_Account_X a dalších 3 | není na formuláři |
| `distinguishedNameLDAP` | distinguishedNameLDAP | String | jen PAM Admins | ano: _VSE_user_no_ad.vse.sk, _ZSE_users_Externisti_update_VSE_LDAP_distinguishedName, _ZSE_users_update_VSE_LDAP_distinguishedName | není na formuláři |
| `distinguishedNameLDAPdev` | distinguishedNameLDAPdev | String | synchronizace | ne | není na formuláři |
| `distinguishedNameLDAPprod` | distinguishedNameLDAPprod | String | synchronizace | ne | není na formuláři |
| `distinguishedNameVSE` | distinguishedNameVSE | String | synchronizace | při založení objektu: _VSE_Copy_groups_create(VSE), _VSE_Copy_groups_create(ZSE) a dalších 2; na navázanou kopii: _VSE_Copy_groups_update přes LDAPTarget | není na formuláři |
| `DomainConfiguration` | Domain Configuration. A reference to a the parent Domain resource for this resource. | Reference | správci skupin, synchronizace | ne | není na formuláři |
| `EmailVSE` | EmailVSE | String | synchronizace | ne | není na formuláři |
| `ExpectedRulesList` | Expected Rules List | Reference, více hodnot | synchronizace | ne | není na formuláři |
| `ExpirationTime` | Expiration Time | DateTime | správci skupin, synchronizace | ne | není na formuláři |
| `groupType` | groupType | Integer | synchronizace | ano: _ZSE_Groups_OU=AzureESK,OU=authz,OU=Groups,DC=eon-slovakia,DC=sk, _ZSE_Groups_OU=AzureESKCS,OU=authz,OU=Groups,DC=eon-slovakia,DC=sk | není na formuláři |
| `LDAPdev` | LDAPdev | String | synchronizace | ne | není na formuláři |
| `LDAPExc` | LDAPExc | Reference | jen PAM Admins | ne | není na formuláři |
| `LDAPkontrola` | LDAPkontrola | String | jen PAM Admins | ano: _VSE_users_group_kontrola_Provisionovany_do_dev_aj_prod | není na formuláři |
| `LDAPnew` | LDAPnew | String | jen PAM Admins | ne | není na formuláři |
| `LDAPPrepared` | LDAPPrepared | String | jen PAM Admins | ano: _VSE_Copy_groups_create(ZSE), _VSE_Copy_groups_ZSE_Only_stage0, _VSE_Copy_groups_ZSE_Only_stage1; při založení objektu: _VSE_Copy_groups_create(ZSE), _VSE_Copy_groups_create(ZSE v2) | není na formuláři |
| `LDAPprod` | LDAPprod | String | synchronizace | ne | není na formuláři |
| `LDAPReplace` | LDAPReplace | Reference | jen PAM Admins | ano: _VSE_copy_fix_group_missing_LDAPTarget, _VSE_Copy_groups_create(ZSE v2 back link), _VSE_Copy_groups_ZSE_Only_stage0 a dalších 4; na navázanou kopii: _VSE_Copy_groups_create(ZSE v2 back link) přes LDAPSourceZSE, _VSE_Copy_groups_ZSE_Only_stage0 přes LDAPSource a dalších 1; na objekt z dotazu: _VSE_copy_fix_group_missing_LDAPTarget | není na formuláři |
| `LDAPSource` | LDAPSource | Reference | jen PAM Admins | při založení objektu: _VSE_Copy_groups_create(VSE), _VSE_Copy_users_create; na objekt z dotazu: _VSE_copy_fix_group_missing_LDAPTarget | není na formuláři |
| `LDAPSourceVSE` | LDAPSourceVSE | Reference | jen PAM Admins | ne | není na formuláři |
| `LDAPSourceZSE` | LDAPSourceZSE | Reference | jen PAM Admins | při založení objektu: _VSE_Copy_groups_create(ZSE), _VSE_Copy_groups_create(ZSE v2); na objekt z dotazu: _VSE_Copy_groups_create(ZSE) | není na formuláři |
| `LDAPsync` | LDAPsync | String | synchronizace | ne | není na formuláři |
| `LDAPTarget` | LDAPTarget | Reference | jen PAM Admins | ano: _VSE_copy_fix_group_missing_LDAPTarget; na navázanou kopii: _VSE_Copy_groups_ZSE_Only_stage0 přes LDAPSource, _VSE_Copy_users_write_back přes LDAPSource | není na formuláři |
| `LDAPTargetZSE` | LDAPTargetZSE | Reference | jen PAM Admins | ano: _VSE_Copy_groups_create(ZSE), _VSE_Copy_groups_create(ZSE v2 back link), _VSE_Copy_groups_create(ZSE v2) a dalších 3; na navázanou kopii: _VSE_Copy_groups_create(ZSE v2 back link) přes LDAPSourceZSE, _VSE_Copy_users_write_back přes LDAPSource | není na formuláři |
| `LDAPTargetZSEOnly` | LDAPTargetZSEOnly | Reference | jen PAM Admins | na navázanou kopii: _VSE_Copy_groups_create(ZSE v2 back link) přes LDAPSourceZSE | není na formuláři |
| `LDAPUpdate` | LDAPUpdate | String | jen PAM Admins | ano: _VSE_Copy_groups_create(ZSE v2 back link), _VSE_Copy_groups_stage0_copy_members, _VSE_Copy_groups_stage1_copy_members(ZSE v2 Only) a dalších 3; na objekt z dotazu: _VSE_Copy_Update_all_User_and_User_copy_groups | není na formuláři |
| `Locale` | Locale. The region and language for which the representation of a resource has been adapted. | String | jen PAM Admins | ne | není na formuláři |
| `MembershipLocked` | Membership Locked | Boolean, povinný | správci skupin, synchronizace | ano: _ZSE_Group_Set_adminDescription_MembershipLocked_; při založení objektu: _VSE_Copy_groups_create(VSE), _VSE_Copy_groups_create(ZSE) a dalších 1 | není na formuláři |
| `msidmDeferredEvaluation` | Deferred Evaluation. Determines when evaluation of the group happens with respect to request processing - real-time or deferred. | Boolean | správci skupin | ne | není na formuláři |
| `msidmDomain` | Domain FQDN. The FQDN of the domain where the group is located | String | jen PAM Admins | ne | není na formuláři |
| `msidmPamEnabled` | PAM Enabled. A boolean value that specifies if a Group is enabled for PAM. | Boolean | správci skupin | ne | není na formuláři |
| `msidmPamPrivOnlyGroup` | PAM Priv Only Group. A boolean value that specifies if a Group represents a PAM group in the PRIV forest. | Boolean | správci skupin | ne | není na formuláři |
| `msidmPamSourceDomainName` | PAM Group Source Domain Name. The Source domain of a PAM group. | String | správci skupin | ne | není na formuláři |
| `msidmPamSourceGroupName` | PAM Source Group Name. The name of the group that is shadowed by this group | String | správci skupin | ne | není na formuláři |
| `msidmPamSourceSid` | PAM Group Source SID | Binary | správci skupin | ne | není na formuláři |
| `msidmPamUsesSIDHistory` | Uses SID history. Indicates if the PAM Group uses SID history. | Boolean | správci skupin | ne | není na formuláři |
| `MVObjectID` | MV Resource ID. The GUID of an entry in the FIM metaverse corresponding to this resource. | String | synchronizace | ne | není na formuláři |
| `ObjectID` | Resource ID | Reference | jen PAM Admins | ne | není na formuláři |
| `ObjectSID` | Resource SID | Binary | správci skupin, synchronizace | ne | není na formuláři |
| `ObjectType` | Resource Type | String, povinný | synchronizace | ne | není na formuláři |
| `OrgOJ` | OrgOJ | String | jen PAM Admins | ne | není na formuláři |
| `OrgOJParent` | OrgOJParent | String | jen PAM Admins | ne | není na formuláři |
| `ResourceTime` | Resource Time | DateTime | jen PAM Admins | ne | není na formuláři |
| `SIDHistory` | SID History. Contains previous SIDs used for the resource if the resource was moved from another domain. | Binary, více hodnot | synchronizace | ne | není na formuláři |
| `Temporal` | Temporal. Defined by a filter that matches resources based on date and time attributes | Boolean | synchronizace | ne | není na formuláři |
| `Type` | Type | String, povinný | správci skupin, synchronizace | při založení objektu: _VSE_Copy_groups_create(VSE), _VSE_Copy_groups_create(ZSE) a dalších 1; na navázanou kopii: _VSE_Copy_groups_update přes LDAPTarget | není na formuláři |
| `VSENoSync` | VSENoSync | String | synchronizace | ano: _VSE_user_no_ad.vse.sk, _VSE_Users_Transition(ZSEOnly - VSEZSE), _ZSE_users_Externisti_update_VSE_LDAP_distinguishedName; při založení objektu: _VSE_Copy_groups_create(ZSE), _VSE_Copy_groups_create(ZSE v2) | není na formuláři |

## Jen počítané

Atributy, které workflow zapíše na cílový objekt a které přitom nejsou v žádném lidském MPR kromě PAM Admins. Synchronizace je u nich uvedená zvlášť, pokud je v jejím seznamu. Když je ve sloupci Synchronizace „ano“, hodnotu plní import. Když je „ne“, na portálu ji nezapíše ani synchronizace, jen workflow.

| Atribut | Objekt | Workflow | Synchronizace |
| --- | --- | --- | --- |
| `accountNameVSE` | osoba, skupina | _VSE_copy_groups_VSEIntranet | ano |
| `adminDescription` | skupina | _VSE_Copy_create_OU=Diamond,OU=Autorizacia,OU=GPOGroups,DC=ad,DC=vse,DC=sk, _ZSE_Group_Set_adminDescription_MembershipLocked_, _ZSE_group_Set_AdminDescription-TYPMIMManualne_from_AD | ano |
| `distinguishedNameLDAP` | osoba, skupina | _VSE_user_no_ad.vse.sk, _ZSE_users_Externisti_update_VSE_LDAP_distinguishedName, _ZSE_users_update_VSE_LDAP_distinguishedName | ano |
| `EmployeeEndDateCUA` | osoba | _ZSE_SAPCUA_Unsync | ano |
| `eonStatus` | osoba | _ZSE_Externisti_Aktivny_stat2_3_eonStatus_A, _ZSE_Externisti_Expirovany_stat2_0_eonStatus_I | ano |
| `eonUserType` | osoba | _VSE_Generate_KID_AD_Account_X | ano |
| `ext_applicant_email` | osoba | _ZSE_Externisti_doplnenie_udajov_od_ziadatela, _ZSE_Externisti_doplnenie_udajov_od_zmena_manazera | ne |
| `ext_applicant_empliyeeid` | osoba | _ZSE_Externisti_doplnenie_udajov_od_ziadatela, _ZSE_Externisti_doplnenie_udajov_od_zmena_manazera | ano |
| `ext_applicant_name` | osoba | _ZSE_Externisti_doplnenie_udajov_od_zmena_manazera | ano |
| `ext_applicant_platnost` | osoba | _ZSE_Externisti_doplnenie_udajov_od_zmena_manazera | ne |
| `extensionAttribute8` | osoba | _ZSE_Externisti_ESK_Azure | ano |
| `ExternistiProcessed` | osoba | _ZSE_Externisti_Update_Procesed | ano |
| `FirstNameEN` | osoba | _ZSE_update_FirstNameEN_LastNameEN | ne |
| `groupType` | skupina | _ZSE_Groups_OU=AzureESK,OU=authz,OU=Groups,DC=eon-slovakia,DC=sk, _ZSE_Groups_OU=AzureESKCS,OU=authz,OU=Groups,DC=eon-slovakia,DC=sk | ano |
| `idNS` | osoba | _ZSE_Externisti_doplnenie_udajov_od_ziadatela | ano |
| `KOC` | osoba | _ZSE_externisti_set_KOC_To_1, _ZSE_externisti_set_KOC_To_0 | ano |
| `Kostl` | osoba | _ZSE_Externisti_doplnenie_udajov_od_ziadatela | ano |
| `LastNameEN` | osoba | _ZSE_update_FirstNameEN_LastNameEN | ne |
| `LDAPkontrola` | osoba, skupina | _VSE_users_group_kontrola_Provisionovany_do_dev_aj_prod | ne |
| `LDAPPrepared` | skupina | _VSE_Copy_groups_create(ZSE), _VSE_Copy_groups_ZSE_Only_stage0, _VSE_Copy_groups_ZSE_Only_stage1 | ne |
| `LDAPReplace` | osoba, skupina | _VSE_copy_fix_group_missing_LDAPTarget, _VSE_Copy_groups_create(ZSE v2 back link), _VSE_Copy_groups_ZSE_Only_stage0, _VSE_Copy_users_write_back a dalších 3 | ne |
| `LDAPTarget` | osoba, skupina | _VSE_copy_fix_group_missing_LDAPTarget | ne |
| `LDAPTargetZSE` | osoba, skupina | _VSE_Copy_groups_create(ZSE), _VSE_Copy_groups_create(ZSE v2 back link), _VSE_Copy_groups_create(ZSE v2), _VSE_copy_users_add_LDAPTargetZSE a dalších 2 | ne |
| `LDAPUpdate` | skupina | _VSE_Copy_groups_create(ZSE v2 back link), _VSE_Copy_groups_stage0_copy_members, _VSE_Copy_groups_stage1_copy_members(ZSE v2 Only), _VSE_Copy_groups_stage1_copy_members(ZSE v2 Both) a dalších 2 | ne |
| `Primary` | osoba | _VSE_Generate_KID_AD_Account_X | ne |
| `upn` | osoba | _VSE_Users_XVSB | ano |
| `userAccountControl` | osoba | _ZSE_ALL_Zamestnanci_stat2-0_userAccountControl-512 | ano |
| `userPrincipalName` | osoba | __test_fix_userPrincipalName, _ZSE_Generate_KID, _VSE_Generate_KID_AD_Account, _VSE_Generate_KID_AD_Account_X a dalších 1 | ano |
| `usrID` | osoba | VSE_CUA_usrID, Externisti_CUA_zse_usrID, ZSE_CUA_usrID | ano |
| `VSENoSync` | osoba, skupina | _VSE_user_no_ad.vse.sk, _VSE_Users_Transition(ZSEOnly - VSEZSE), _ZSE_users_Externisti_update_VSE_LDAP_distinguishedName | ano |
| `Werks` | osoba | _ZSE_Externisti_doplnenie_udajov_od_ziadatela | ano |

## Na co si dát pozor

- Workflow `_ZSE_Disable_account_when_burks_is_deleted` zapisuje `[//Target/eonStatis]`. Atribut `eonStatis` ve schématu není. Stavový atribut je `eonStatus`.
- `userAccountControl` má v tabulce workflow `_ZSE_ALL_Zamestnanci_stat2-0_userAccountControl-512`. MPR, které ho spouští, je vypnuté, takže tímto workflow se účet dnes nezakazuje. Hodnotu dál píše synchronizace.
- `AccountName` a `distinguishedName` workflow generuje, ale administrátoři a synchronizace je smí přepsat. `userPrincipalName` administrátoři v MPR nemají, píše ho workflow a synchronizace.
- LDAP, schránka a příznaky účtu jsou jen na formuláři úpravy, a uloží je jen ten, kdo má MPR. Na zobrazení tyto záložky nejsou.
- Ostatní RCDC (sada, MPR, workflow, synchronizační pravidlo, PAM, schéma) jsou šablona Microsoftu pro správce portálu. Vlastní záložky nemají.

