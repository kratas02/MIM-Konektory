# Toky na konektorech po zrušení portálu

Zdroj je export `Policy.xml` z http://smd1mgidm02p:5725, 46 synchronization rules. Po odpojení MIMMA se tato deklarativní pravidla přestanou počítat. Každé dnešní pravidlo je níže vlastní kapitola. V tabulce je tok, který se nastaví přímo na konektoru.

U importu je Zdroj atribut konektoru a Cíl atribut metaverse. U exportu je to obráceně: Zdroj je metaverse, Cíl je konektor.

Sloupec Typ:

- `přímý` je Direct mapping v designeru.
- `konstanta` je Constant.
- `výraz` je podmínka, kterou klasický designer neumí naklikat. Patří do rules extension. Kde už ji má knihovna `ZsdisRulesExtension`, je ve sloupci název advanced flow. Ten název se do designeru píše přesně tak.
- `při provisioningu` není attribute flow v designeru. Distinguished name a počáteční hodnoty nastavuje `MVExtensionObject` ve chvíli, kdy provisioning zakládá objekt v konektoru.

Klasický konektor má pro jeden atribut cílového systému jen jeden export flow. Když dnes více pravidel píše stejný atribut pod jiným filtrem, v tabulce zůstávají jako samostatné řádky pod svým původním názvem. Řádek jen při provisioningu se v designeru zapíná jako Only once.

Heslo `unicodePwd` je v exportu portálu jako konstanta. Tady není. Při založení účtu ho čte `initial-password.txt` vedle DLL.

## Jak poznat projekci a provisioning

U každého pravidla jsou dva příznaky, ve významu synchronizační služby MIM 2016:

- **Projekce: ano** znamená, že pravidlo smí založit objekt v metaverse (`CreateILMObject`). Název pravidla v portálu přitom někdy říká provisioning, například `User HUMANET v2 Provisioning`. U něj je provisioning ne, do Humanetu se nic nezakládá.
- **Provisioning: ano** znamená, že pravidlo smí z metaverse založit nový objekt v cílovém konektoru (`CreateConnectedSystemObject`). Filtr téhož pravidla říká, kdo se založí. Toky označené jen při provisioningu se zapíšou jen v tu chvíli, typicky distinguished name.

Pravidla s provisioningem, tedy s novým objektem v konektoru:

| Konektor | Pravidlo |
| --- | --- |
| eon-sk | `Users eon-sk in-out` (zároveň projekce), `Groups_eon-sk_Create_in_eon`, skupina v `OU=AzureESK` |
| VSE_LDAP_prod | `User_VSE_ADLDS_prod`, `User_VSE_ADLDS_copy_prod`, `User_VSE_ADLDS(ZSE only ucty) prod`, `User_VSE_ADLDS(ZSE_spolocnpsti)_prod`, `User_VSE_ADLDS(ZSE_Servisne_ucty)_prod`, `User_VSE_ADLDS(ZSE_org_Servisne_ucty)_prod`, `Groups_VSE_LDAP_prod`, `Groups_VSE_LDAP_(ZSE v2 Only)_prod` |
| SAPCUA | `User SAPCUA - outbound`, `User SAPCUA-outbound VSE` |
| ZSEEUser | `User ZSEE`, `User ZSEE Externisti` |
| KOC | `User KOC int`, `User KOC ext` |
| HUMANET_OUT | `User HUMANET OUt` |
| Externisti_SAP_OUT | `Users Externisti SAP OUT` |

## HUMANETv2

Třída `MaHumanet`. Projekce osoby, když `usrID` je `eon-slovakia.sk` nebo `ad.vse.sk`.

### User HUMANETv2  sync only

- Objekt: metaverse `person`, konektor `person`. Směr `IN+OUT`, precedence `1`.
- Projekce: ne. Provisioning: ne.
- Join: `employeeID = employeeID`.
- Filtr: žádný.

| Směr | Kdy | Zdroj | Cíl | Typ |
| --- | --- | --- | --- | --- |
| import | trvale | `accountName` | `accountName1` | přímý |
| import | trvale | `begda` | `begda` | přímý |
| import | trvale | `begda1` | `endda` | přímý |
| import | trvale | `bukrs` | `bukrs` | přímý |
| import | trvale | `employeeEndDate` | `employeeEndDate` | přímý |
| import | trvale | `employeeStartDate` | `employeeStartDate` | přímý |
| import | trvale | `idNS` | `idNS` | přímý |
| import | trvale | `krstneMeno` | `firstName` | přímý |
| import | trvale | `persg` | `persg` | přímý |
| import | trvale | `persk` | `persk` | přímý |
| import | trvale | `priezvisko` | `lastName` | přímý |
| import | trvale | `IIF(IsPresent(priezvisko), IIF(IsPresent(krstneMeno), +(priezvisko, ", ", krstneMeno), priezvisko), "")` | `displayName` | advanced displayName |
| import | trvale | `IIF(IsPresent(bukrs), bukrs, "")` | `eonBusinessUnitID` | advanced eonBusinessUnitID |
| import | trvale | `IIF(IsPresent(idOJ2), idOJ2, "NO-OM-INT")` | `department` | advanced department |
| import | trvale | `mobilPhone` | `mobilePhone1` | přímý |
| import | trvale | `IIF(Eq(stat2, "3"), "A", "I")` | `eonStatus` | advanced eonStatus |
| import | trvale | `stat2` | `stat2` | přímý |
| import | trvale | `primary` | `eonUserType` | konstanta |
| import | trvale | `regular` | `eonUserPurpose` | konstanta |
| import | trvale | `displayNamePlne` | `displayNameFull` | přímý |
| import | trvale | `endda` | `employeeEndDateCUA` | přímý |
| import | trvale | `manager` | `manager` | přímý |
| import | trvale | `werks` | `Werks` | přímý |
| import | trvale | `menoOJ` | `menoOJ` | přímý |
| import | trvale | `idNS` | `Kostl` | přímý |
| import | trvale | `I` | `employeeType` | konstanta |
| import | trvale | `Slovakia` | `country` | konstanta |
| import | trvale | `SK` | `c` | konstanta |
| import | trvale | `nazovPlanovanehoMiesta` | `jobTitle` | přímý |
| import | trvale | `idOJPerson` | `departmentNumber` | přímý |
| import | trvale | `humjoin` | `humanet` | konstanta |
| import | trvale | `employeeID` | `employeeID` | přímý |
| import | trvale | `ou` | `ou` | přímý |
| import | trvale | `usrID` | `usrID` | přímý |
| import | trvale | `nazovOJ` | `company` | přímý |
| import | trvale | `ouldap` | `ouldap` | přímý |
| import | trvale | `upn` | `upn` | přímý |
| import | trvale | `personalTitle` | `personalTitle` | přímý |

### User HUMANETv2  join EmployeeID

- Objekt: metaverse `person`, konektor `person`. Směr `IN+OUT`, precedence `2`.
- Projekce: ne. Provisioning: ne.
- Join: `employeeID = employeeID`.
- Filtr: žádný.

| Směr | Kdy | Zdroj | Cíl | Typ |
| --- | --- | --- | --- | --- |
| import | trvale | `humjoin` | `humanet` | konstanta |

### User HUMANET v2 Provisioning

- Objekt: metaverse `person`, konektor `person`. Směr `IN+OUT`, precedence `3`.
- Projekce: ano. Provisioning: ne.
- Join: `employeeID = employeeID`.
- Filtr: žádný.

| Směr | Kdy | Zdroj | Cíl | Typ |
| --- | --- | --- | --- | --- |
| import | trvale | `employeeID` | `employeeID` | přímý |

### User HUMANETv2  join kid VSE

- Objekt: metaverse `person`, konektor `person`. Směr `IN+OUT`, precedence `4`.
- Projekce: ne. Provisioning: ne.
- Join: `accountName = accountName`.
- Filtr: žádný.

| Směr | Kdy | Zdroj | Cíl | Typ |
| --- | --- | --- | --- | --- |
| import | trvale | `humjoin` | `humanet` | konstanta |

### User HUMANETv2  join msdsAnchore VSE

- Objekt: metaverse `person`, konektor `person`. Směr `IN+OUT`, precedence `5`.
- Projekce: ne. Provisioning: ne.
- Join: `msDS-SourceAnchor = accountName`.
- Filtr: žádný.

| Směr | Kdy | Zdroj | Cíl | Typ |
| --- | --- | --- | --- | --- |
| import | trvale | `humjoin` | `humanet` | konstanta |

### User HUMANET v2 Provisioning VSE

- Objekt: metaverse `person`, konektor `person`. Směr `IN+OUT`, precedence `6`.
- Projekce: ano. Provisioning: ne.
- Join: `employeeID = employeeID`.
- Filtr: žádný.

| Směr | Kdy | Zdroj | Cíl | Typ |
| --- | --- | --- | --- | --- |
| import | trvale | `employeeID` | `employeeID` | přímý |

## Externisti

Třída `MaExternisti`. Projekce osoby, když je vyplněné `personal_number`.

### User Externisti Provisioning

- Objekt: metaverse `person`, konektor `Person`. Směr `IN+OUT`, precedence `1`.
- Projekce: ano. Provisioning: ne.
- Join: `employeeID = personal_number`.
- Filtr: žádný.

| Směr | Kdy | Zdroj | Cíl | Typ |
| --- | --- | --- | --- | --- |
| import | trvale | `+("idcreate", id)` | `externisti` | advanced externisti |

### User Externisti Join Id

- Objekt: metaverse `person`, konektor `Person`. Směr `IN+OUT`, precedence `2`.
- Projekce: ne. Provisioning: ne.
- Join: `accountName = kid`.
- Filtr: žádný.
- Toky: žádné, pravidlo jen spojuje.

### User Externisti Join KID

- Objekt: metaverse `person`, konektor `Person`. Směr `IN+OUT`, precedence `3`.
- Projekce: ne. Provisioning: ne.
- Join: `accountName = kid`.
- Filtr: žádný.

| Směr | Kdy | Zdroj | Cíl | Typ |
| --- | --- | --- | --- | --- |
| import | trvale | `+("idjoin", id)` | `externisti` | advanced externisti |

### User Externisti Sync Only

- Objekt: metaverse `person`, konektor `Person`. Směr `IN+OUT`, precedence `4`.
- Projekce: ne. Provisioning: ne.
- Join: `employeeID = personal_number`.
- Filtr: žádný.

| Směr | Kdy | Zdroj | Cíl | Typ |
| --- | --- | --- | --- | --- |
| export | trvale | `accountName` | `kid` | přímý |
| export | trvale | `employeeID` | `personal_number` | přímý |
| export | trvale | `externisti_processed` | `processed` | přímý |
| import | trvale | `company_name` | `company` | přímý |
| import | trvale | `extern_name` | `firstName` | přímý |
| import | trvale | `extern_surname` | `lastName` | přímý |
| import | trvale | `personal_number` | `employeeID` | přímý |
| import | trvale | `processed` | `externisti_processed` | přímý |
| import | trvale | `E` | `employeeType` | konstanta |
| import | trvale | `Slovakia` | `country` | konstanta |
| import | trvale | `+(extern_surname, ", ", extern_name)` | `displayName` | advanced displayName |
| import | trvale | `applicant_kid` | `ext_applicant_kid` | přímý |
| import | trvale | `applicant_name` | `ext_applicant_name` | přímý |
| import | trvale | `company` | `bukrs` | přímý |
| import | trvale | `company` | `eonBusinessUnitID` | přímý |
| export | trvale | `email` | `extern_email` | přímý |
| import | trvale | `ReplaceString(formatted_contract_from, "-", "")` | `begda` | advanced begda |
| import | trvale | `ReplaceString(formatted_contract_to, "-", "")` | `endda` | advanced endda |
| import | trvale | `+(formatted_contract_from, "T00:00:00.000")` | `employeeStartDate` | advanced employeeStartDate |
| import | trvale | `+(formatted_contract_to, "T00:00:00.000")` | `employeeEndDate` | advanced employeeEndDate |
| import | trvale | `SK` | `c` | konstanta |
| import | trvale | `primary` | `eonUserType` | konstanta |
| import | trvale | `regular` | `eonUserPurpose` | konstanta |
| import | trvale | `extern_email` | `ext_extern_email` | přímý |
| import | trvale | `applicant_kid` | `ext_applicant_kid1` | přímý |
| import | trvale | `E2` | `persk` | konstanta |
| import | trvale | `extern_phone` | `mobilePhone1` | přímý |
| import | trvale | `id` | `ext_id` | přímý |

### User Externisti predlzenie platnosti

- Objekt: metaverse `person`, konektor `Person`. Směr `IN+OUT`, precedence `5`.
- Projekce: ne. Provisioning: ne.
- Join: `accountName = kid`.
- Filtr: `ext_applicant_platnost EQUAL true`.

| Směr | Kdy | Zdroj | Cíl | Typ |
| --- | --- | --- | --- | --- |
| export | trvale | `Left(employeeEndDate, 10)` | `formatted_contract_to` | advanced formatted_contract_to |
| export | trvale | `ext_applicant_kid` | `applicant_kid` | přímý |
| export | trvale | `ext_applicant_name` | `applicant_name` | přímý |
| export | trvale | `+(Left(employeeEndDate, 10), " 00:00:00Z")` | `extern_contract_to` | advanced extern_contract_to |

## eon-sk

Třída `MaEonSk`. Projekce uživatele v `DC=eon-slovakia,DC=sk`. Projekce skupiny, když `adminDescription` nezačíná na `TYP`. Deprovisioning je Disconnect.

### Group eon-sk in

- Objekt: metaverse `group`, konektor `group`. Směr `IN`, precedence `1`.
- Projekce: ano. Provisioning: ne.
- Join: `accountName = sAMAccountName`.
- Filtr: žádný.

| Směr | Kdy | Zdroj | Cíl | Typ |
| --- | --- | --- | --- | --- |
| import | trvale | `false` | `membershipLocked` | konstanta |
| import | trvale | `None` | `membershipAddWorkflow` | konstanta |

### Group eon-sk in-out proxy

- Objekt: metaverse `group`, konektor `group`. Směr `IN+OUT`, precedence `3`.
- Projekce: ne. Provisioning: ne.
- Join: `accountName = sAMAccountName`.
- Filtr: `description STARTSWITH proxy-policy:`.

| Směr | Kdy | Zdroj | Cíl | Typ |
| --- | --- | --- | --- | --- |
| export | trvale | `member` | `member` | přímý |

### Group eon-sk - TYPMIMDynamicke

- Objekt: metaverse `group`, konektor `group`. Směr `IN+OUT`, precedence `4`.
- Projekce: ne. Provisioning: ne.
- Join: `accountName = sAMAccountName`.
- Filtr: žádný.

| Směr | Kdy | Zdroj | Cíl | Typ |
| --- | --- | --- | --- | --- |
| import | trvale | `true` | `membershipLocked` | konstanta |

### Group eon-sk in-out

- Objekt: metaverse `group`, konektor `group`. Směr `IN+OUT`, precedence `5`.
- Projekce: ano. Provisioning: ne.
- Join: `accountName = sAMAccountName`.
- Filtr: `cn NOTEQUAL _ZSE_SAPCUA_Users`.

| Směr | Kdy | Zdroj | Cíl | Typ |
| --- | --- | --- | --- | --- |
| import | trvale | `sAMAccountName` | `accountName` | přímý |
| import | trvale | `description` | `description` | přímý |
| import | trvale | `IIF(IsPresent(displayName), displayName, cn)` | `displayName` | advanced group.displayName |
| import | trvale | `IIF(Eq(BitAnd(2, groupType), 2), "Global", IIF(Eq(BitAnd(4, groupType), 4), "DomainLocal", "Universal"))` | `scope` | advanced group.scope |
| import | trvale | `member` | `member` | přímý |
| import | trvale | `objectSid` | `objectSid` | přímý |
| import | trvale | `EON-SK` | `domain` | konstanta |
| import | trvale | `mail` | `email` | přímý |
| import | trvale | `mailNickname` | `mailNickname` | přímý |
| import | trvale | `IIF(Eq(BitOr(14, groupType), 14), "Distribution", IIF(IsPresent(mailNickname), "MailEnabledSecurity", "Security"))` | `type` | advanced group.type |
| import | trvale | `dn` | `distinguishedNameAD` | přímý |
| export | trvale | `adminDescription` | `adminDescription` | přímý |
| import | trvale | `None` | `membershipAddWorkflow` | konstanta |
| import | trvale | `false` | `membershipLocked` | konstanta |
| import | trvale | `groupType` | `groupType` | přímý |

### Group eon-sk in-out dfs

- Objekt: metaverse `group`, konektor `group`. Směr `IN+OUT`, precedence `6`.
- Projekce: ne. Provisioning: ne.
- Join: `accountName = sAMAccountName`.
- Filtr: `displayName STARTSWITH sf-dfsroot`.

| Směr | Kdy | Zdroj | Cíl | Typ |
| --- | --- | --- | --- | --- |
| export | trvale | `member` | `member` | přímý |

### Group eon-sk out-xecm

- Objekt: metaverse `group`, konektor `group`. Směr `IN+OUT`, precedence `8`.
- Projekce: ne. Provisioning: ne.
- Join: `accountName = sAMAccountName`.
- Filtr: žádný.

| Směr | Kdy | Zdroj | Cíl | Typ |
| --- | --- | --- | --- | --- |
| export | trvale | `member` | `member` | přímý |

### Groups_eon-sk_Create_in_eon

- Objekt: metaverse `group`, konektor `group`. Směr `IN+OUT`, precedence `10`.
- Projekce: ne. Provisioning: ano.
- Join: `accountName = sAMAccountName`.
- Filtr: `distinguishedName ENDSWITH OU=Groups,DC=eon-slovakia,DC=sk a displayName NOTCONTAINS copy- a accountName CONTAINS .sk`.

| Směr | Kdy | Zdroj | Cíl | Typ |
| --- | --- | --- | --- | --- |
| export | jen při provisioningu | `distinguishedName` | `dn` | při provisioningu |
| export | trvale | `adminDescription` | `adminDescription` | přímý |
| export | trvale | `displayName` | `displayName` | přímý |
| export | trvale | `member` | `member` | přímý |
| export | trvale | `groupType` | `groupType` | přímý |
| export | trvale | `accountName` | `sAMAccountName` | přímý |

### _ZSE_Groups_OU=AzureESK,OU=authz,OU=Groups,DC=eon-slovakia,DC=sk

- Objekt: metaverse `group`, konektor `group`. Směr `OUT`, precedence `11`.
- Projekce: ne. Provisioning: ano.
- Join: žádný, objekt se páruje jen při provisioningu.
- Filtr: `distinguishedName CONTAINS OU=AzureESK`.

| Směr | Kdy | Zdroj | Cíl | Typ |
| --- | --- | --- | --- | --- |
| export | jen při provisioningu | `distinguishedName` | `dn` | při provisioningu |
| export | trvale | `accountName` | `sAMAccountName` | přímý |
| export | trvale | `adminDescription` | `adminDescription` | přímý |
| export | trvale | `description` | `description` | přímý |
| export | trvale | `displayName` | `displayName` | přímý |
| export | trvale | `groupType` | `groupType` | přímý |
| export | trvale | `member` | `member` | přímý |

### Users eon-sk in-out

- Objekt: metaverse `person`, konektor `user`. Směr `IN+OUT`, precedence `2`.
- Projekce: ano. Provisioning: ano.
- Join: `accountName = sAMAccountName`.
- Filtr: `distinguishedName STARTSWITH CN a distinguishedName ENDSWITH DC=eon-slovakia,DC=sk`.

| Směr | Kdy | Zdroj | Cíl | Typ |
| --- | --- | --- | --- | --- |
| export | jen při provisioningu | `distinguishedName` | `dn` | při provisioningu |
| export | jen při provisioningu | `514` | `userAccountControl` | při provisioningu |
| export | jen při provisioningu | `heslo ze souboru initial-password.txt` | `unicodePwd` | při provisioningu |
| export | jen při provisioningu | `sk-SK` | `preferredLanguage` | při provisioningu |
| import | trvale | `department` | `department` | přímý |
| import | trvale | `co` | `country` | přímý |
| export | trvale | `c` | `c` | přímý |
| import | trvale | `c` | `c` | přímý |
| export | trvale | `company` | `company` | přímý |
| export | trvale | `country` | `co` | přímý |
| import | trvale | `company` | `company` | přímý |
| export | trvale | `departmentNumber` | `departmentNumber` | přímý |
| import | trvale | `departmentNumber` | `departmentNumber` | přímý |
| export | trvale | `accountName` | `sAMAccountName` | přímý |
| import | trvale | `sAMAccountName` | `accountName` | přímý |
| import | trvale | `displayName` | `displayName` | přímý |
| import | trvale | `givenName` | `firstName` | přímý |
| import | trvale | `sn` | `lastName` | přímý |
| export | trvale | `eonMUShort` | `eonMUShort` | přímý |
| export | trvale | `eonOrganization` | `eonOrganization` | přímý |
| export | trvale | `eonOrganizationID` | `eonOrganizationID` | přímý |
| import | trvale | `eonMUShort` | `eonMUShort` | přímý |
| import | trvale | `eonOrganization` | `eonOrganization` | přímý |
| import | trvale | `eonOrganizationID` | `eonOrganizationID` | přímý |
| export | trvale | `firstName` | `givenName` | přímý |
| export | trvale | `mobilePhone` | `mobile` | přímý |
| export | trvale | `telephoneNumber` | `telephoneNumber` | přímý |
| import | trvale | `mobile` | `mobilePhone` | přímý |
| export | trvale | `department` | `department` | přímý |
| export | trvale | `displayName` | `displayName` | přímý |
| import | trvale | `objectSid` | `objectSid` | přímý |
| import | trvale | `EON-SK` | `domain` | konstanta |
| import | trvale | `description` | `description` | přímý |
| import | trvale | `mailNickname` | `mailNickname` | přímý |
| import | trvale | `employeeType` | `employeeType` | přímý |
| import | trvale | `l` | `city` | přímý |
| import | trvale | `street` | `officeLocation` | přímý |
| import | trvale | `postalCode` | `postalCode` | přímý |
| import | trvale | `physicalDeliveryOfficeName` | `physicalDeliveryOfficeName` | přímý |
| import | trvale | `eonBusinessUnitID` | `eonBusinessUnitID` | přímý |
| import | trvale | `eonCID` | `eonCID` | přímý |
| import | trvale | `eonCIDs` | `eonCIDs` | přímý |
| import | trvale | `eonDepartmentID` | `eonDepartmentID` | přímý |
| import | trvale | `eonExtensionAttribute35` | `eonExtensionAttribute35` | přímý |
| import | trvale | `eonExtensionAttribute36` | `eonExtensionAttribute36` | přímý |
| import | trvale | `eonStatus` | `eonStatus` | přímý |
| import | trvale | `eonStatusChangeTime` | `eonStatusChangeTime` | přímý |
| import | trvale | `eonUserPurpose` | `eonUserPurpose` | přímý |
| import | trvale | `eonUserType` | `eonUserType` | přímý |
| import | trvale | `IIF(Eq(BitOr(userAccountControl, 2), userAccountControl), "true", "false")` | `enabled` | advanced enabled |
| import | trvale | `false` | `locked` | konstanta |
| import | trvale | `dn` | `distinguishedName` | přímý |
| import | trvale | `userPrincipalName` | `userPrincipalName` | přímý |
| export | trvale | `userPrincipalName` | `userPrincipalName` | přímý |
| export | trvale | `eonUserPurpose` | `eonUserPurpose` | přímý |
| export | trvale | `eonUserType` | `eonUserType` | přímý |
| export | trvale | `lastName` | `sn` | přímý |
| import | trvale | `userAccountControl` | `userAccountControl` | přímý |
| export | trvale | `eonStatus` | `eonStatus` | přímý |
| export | trvale | `employeeType` | `employeeType` | přímý |
| import | trvale | `streetAddress` | `street` | přímý |
| export | trvale | `eonBusinessUnitID` | `eonBusinessUnitID` | přímý |
| export | trvale | `jobTitle` | `title` | přímý |
| export | trvale | `email` | `mail` | přímý |
| export | trvale | `departmentNumber` | `extensionAttribute7` | přímý |
| import | trvale | `scriptPath` | `scriptPath` | přímý |
| export | trvale | `scriptPath` | `scriptPath` | přímý |
| export | trvale | `physicalDeliveryOfficeName` | `physicalDeliveryOfficeName` | přímý |
| export | trvale | `idNS` | `extensionAttribute6` | přímý |
| export | trvale | `manager` | `manager` | přímý |
| import | trvale | `Trim(mail)` | `email` | advanced email |
| export | trvale | `IPphone` | `ipPhone` | přímý |
| export | trvale | `employeeID` | `extensionAttribute15` | přímý |
| export | trvale | `postalCode` | `postalCode` | přímý |
| export | trvale | `city` | `l` | přímý |
| export | trvale | `accountNameVSE` | `extensionAttribute10` | přímý |
| export | trvale | `employeeID` | `employeeNumber` | přímý |
| export | trvale | `extensionAttribute8` | `extensionAttribute8` | přímý |
| import | trvale | `extensionAttribute8` | `extensionAttribute8` | přímý |

### Users eon-sk out - disable when stat2-0

- Objekt: metaverse `person`, konektor `user`. Směr `OUT`, precedence `7`.
- Projekce: ne. Provisioning: ne.
- Join: žádný, objekt se páruje jen při provisioningu.
- Filtr: `stat2 EQUAL 0`.

| Směr | Kdy | Zdroj | Cíl | Typ |
| --- | --- | --- | --- | --- |
| export | trvale | `userAccountControl` | `userAccountControl` | přímý |

### Users eon-sk in-out Ms-ds-consistency-gUID

- Objekt: metaverse `person`, konektor `user`. Směr `OUT`, precedence `9`.
- Projekce: ne. Provisioning: ne.
- Join: žádný, objekt se páruje jen při provisioningu.
- Filtr: `distinguishedNameVSE STARTSWITH CN`.

| Směr | Kdy | Zdroj | Cíl | Typ |
| --- | --- | --- | --- | --- |
| export | trvale | `mS-DS-ConsistencyGuid` | `mS-DS-ConsistencyGuid` | přímý |

## ad.vse.sk

Třída `MaAdVse`. Projekce uživatele v `DC=ad,DC=vse,DC=sk`, jehož `userAccountControl` není 514. Projekce skupiny v `OU=Autorizacia,OU=GPOGroups,DC=ad,DC=vse,DC=sk`. Deprovisioning je ExplicitDisconnect. Join `employeeID` má advanced název `employeeNumber-when-enabled` a bere `employeeNumber` jen když účet není 514.

### Groups_AD.VSE.SK

- Objekt: metaverse `group`, konektor `group`. Směr `IN+OUT`, precedence `2`.
- Projekce: ano. Provisioning: ne.
- Join: `objectSIDVSE = objectSid`.
- Filtr: žádný.

| Směr | Kdy | Zdroj | Cíl | Typ |
| --- | --- | --- | --- | --- |
| import | trvale | `cn` | `cn` | přímý |
| import | trvale | `dn` | `distinguishedNameVSE` | přímý |
| import | trvale | `objectSid` | `objectSIDVSE` | přímý |
| import | trvale | `groupType` | `groupType` | přímý |
| import | trvale | `IIF(IsPresent(displayName), displayName, cn)` | `displayName` | advanced group.displayName |
| import | trvale | `description` | `description` | přímý |
| import | trvale | `member` | `member` | přímý |
| import | trvale | `sAMAccountName` | `accountNameVSE` | přímý |
| import | trvale | `false` | `membershipLocked` | konstanta |
| import | trvale | `None` | `membershipAddWorkflow` | konstanta |
| import | trvale | `IIF(Eq(BitAnd(2, groupType), 2), "Global", IIF(Eq(BitAnd(4, groupType), 4), "DomainLocal", "Universal"))` | `scope` | advanced group.scope |
| import | trvale | `Security` | `type` | konstanta |
| import | trvale | `EON-SK` | `domain` | konstanta |
| import | trvale | `+("VSE-", sAMAccountName)` | `accountName` | advanced group.accountName |
| import | trvale | `No` | `VSENoSync` | konstanta |
| import | trvale | `advse` | `advse` | konstanta |
| import | trvale | `cn` | `cnVSE` | přímý |

### user_AD.VSE.SK

- Objekt: metaverse `person`, konektor `user`. Směr `IN+OUT`, precedence `1`.
- Projekce: ano. Provisioning: ne.
- Join: `accountNameVSE = sAMAccountName`.
- Filtr: žádný.

| Směr | Kdy | Zdroj | Cíl | Typ |
| --- | --- | --- | --- | --- |
| import | trvale | `employeeID` | `employeeID` | přímý |
| import | trvale | `dn` | `distinguishedNameVSE` | přímý |
| import | trvale | `mail` | `emailVSE` | přímý |
| import | trvale | `userAccountControl` | `userAccountControlVSE` | přímý |
| import | trvale | `userPrincipalName` | `userPrincipalNameVSE` | přímý |
| import | trvale | `objectSid` | `objectSIDVSE` | přímý |
| import | trvale | `ad.vse.sk` | `usrID` | konstanta |
| import | trvale | `sAMAccountName` | `accountNameVSE` | přímý |
| import | trvale | `displayName` | `displayNameVSE` | přímý |
| import | trvale | `msDS-SourceAnchor` | `msDS-SourceAnchor` | přímý |
| import | trvale | `company` | `company1` | přímý |
| import | trvale | `department` | `department1` | přímý |
| import | trvale | `givenName` | `firstNameVSE` | přímý |
| import | trvale | `sn` | `lastNameVSE` | přímý |
| import | trvale | `mS-DS-ConsistencyGuid` | `mS-DS-ConsistencyGuid` | přímý |
| import | trvale | `employeeNumber` | `employeeNumber` | přímý |
| import | trvale | `personalTitle` | `personalTitle` | přímý |
| import | trvale | `title` | `title` | přímý |
| import | trvale | `mobile` | `mobile` | přímý |
| import | trvale | `cn` | `cnVSE` | přímý |
| import | trvale | `advse` | `advse` | konstanta |

### user_AD.VSE.SK(join employeeID)

- Objekt: metaverse `person`, konektor `user`. Směr `IN`, precedence `3`.
- Projekce: ne. Provisioning: ne.
- Join: `employeeID = employeeNumber`.
- Filtr: žádný.
- Toky: žádné, pravidlo jen spojuje.

## VSE_LDAP_prod

Třída `MaVseLdap`. Projekce není, objekty zakládá provisioning.

### Groups_VSE_LDAP_prod

- Objekt: metaverse `group`, konektor `group`. Směr `IN+OUT`, precedence `5`.
- Projekce: ne. Provisioning: ano.
- Join: `accountNameVSE = sAMAccountName`.
- Filtr: `distinguishedNameVSE ENDSWITH DC=ad,DC=vse,DC=sk a VSENoSync NOTEQUAL No`.

| Směr | Kdy | Zdroj | Cíl | Typ |
| --- | --- | --- | --- | --- |
| export | jen při provisioningu | `distinguishedNameVSE` | `dn` | při provisioningu |
| export | jen při provisioningu | `-2147483646` | `groupType` | konstanta |
| export | trvale | `accountNameVSE` | `sAMAccountName` | přímý |
| export | trvale | `description` | `description` | přímý |
| export | trvale | `member` | `member` | přímý |
| import | trvale | `prod` | `LDAPprod` | konstanta |
| import | trvale | `dn` | `distinguishedNameLDAPprod` | přímý |

### Groups_VSE_LDAP_rename_prod

- Objekt: metaverse `group`, konektor `group`. Směr `OUT`, precedence `6`.
- Projekce: ne. Provisioning: ne.
- Join: žádný, objekt se páruje jen při provisioningu.
- Filtr: `distinguishedNameVSE ENDSWITH DC=ad,DC=vse,DC=sk`.

| Směr | Kdy | Zdroj | Cíl | Typ |
| --- | --- | --- | --- | --- |
| export | trvale | `IIF(IsPresent(cnVSE), cnVSE, accountNameVSE)` | `cn` | advanced cn |

### Groups_VSE_LDAP_(ZSE v2 Only)_prod

- Objekt: metaverse `group`, konektor `group`. Směr `IN+OUT`, precedence `10`.
- Projekce: ne. Provisioning: ano.
- Join: `accountNameVSE = sAMAccountName`.
- Filtr: `LDAPsync EQUAL ZSEOnlyYes`.

| Směr | Kdy | Zdroj | Cíl | Typ |
| --- | --- | --- | --- | --- |
| export | jen při provisioningu | `distinguishedNameVSE` | `dn` | při provisioningu |
| export | trvale | `accountNameVSE` | `sAMAccountName` | přímý |
| export | trvale | `description` | `description` | přímý |
| export | trvale | `groupType` | `groupType` | přímý |
| export | trvale | `member` | `member` | přímý |

### User_VSE_ADLDS_prod

- Objekt: metaverse `person`, konektor `user`. Směr `IN+OUT`, precedence `1`.
- Projekce: ne. Provisioning: ano.
- Join: `accountNameVSE = sAMAccountName`.
- Filtr: `distinguishedNameVSE ENDSWITH DC=ad,DC=vse,DC=sk a VSENoSync NOTEQUAL No a displayName NOTSTARTSWITH copy- a VSENoSync NOTEQUAL ZSEOnly`.

| Směr | Kdy | Zdroj | Cíl | Typ |
| --- | --- | --- | --- | --- |
| export | jen při provisioningu | `distinguishedNameVSE` | `dn` | při provisioningu |
| export | jen při provisioningu | `objectSIDVSE` | `objectSid` | přímý |
| export | trvale | `accountNameVSE` | `sAMAccountName` | přímý |
| export | trvale | `department` | `department` | přímý |
| export | trvale | `displayNameVSE` | `displayName` | přímý |
| export | trvale | `emailVSE` | `mail` | přímý |
| export | trvale | `employeeNumber` | `employeeNumber` | přímý |
| export | trvale | `firstNameVSE` | `givenName` | přímý |
| export | trvale | `lastNameVSE` | `sn` | přímý |
| export | trvale | `mobile` | `mobile` | přímý |
| export | trvale | `personalTitle` | `personalTitle` | přímý |
| export | trvale | `Left(title, 60)` | `title` | advanced title |
| export | trvale | `userPrincipalNameVSE` | `userPrincipalName` | přímý |
| import | trvale | `prod` | `LDAPprod` | konstanta |
| export | trvale | `userAccountControlVSE` | `userAccountControl` | přímý |

### User_VSE_ADLDS_rename_prod

- Objekt: metaverse `person`, konektor `user`. Směr `IN+OUT`, precedence `2`.
- Projekce: ne. Provisioning: ne.
- Join: `accountNameVSE = sAMAccountName`.
- Filtr: žádný.

| Směr | Kdy | Zdroj | Cíl | Typ |
| --- | --- | --- | --- | --- |
| export | trvale | `IIF(IsPresent(cnVSE), cnVSE, accountNameVSE)` | `cn` | advanced cn |

### User_VSE_ADLDS_copy_prod

- Objekt: metaverse `person`, konektor `user`. Směr `IN+OUT`, precedence `3`.
- Projekce: ne. Provisioning: ano.
- Join: `accountNameVSE = sAMAccountName`.
- Filtr: `distinguishedNameVSE ENDSWITH DC=ad,DC=vse,DC=sk a displayName STARTSWITH copy- a userAccountControl GREATERTHAN_OR_EQUAL 0`.

| Směr | Kdy | Zdroj | Cíl | Typ |
| --- | --- | --- | --- | --- |
| export | jen při provisioningu | `distinguishedNameVSE` | `dn` | při provisioningu |
| export | trvale | `accountNameVSE` | `sAMAccountName` | přímý |
| export | trvale | `department` | `department` | přímý |
| export | trvale | `email` | `mail` | přímý |
| export | trvale | `employeeID` | `employeeNumber` | přímý |
| export | trvale | `firstName` | `givenName` | přímý |
| export | trvale | `Left(jobTitle, 60)` | `title` | advanced title |
| export | trvale | `lastName` | `sn` | přímý |
| export | trvale | `mobile` | `mobile` | přímý |
| export | trvale | `objectSIDVSE` | `objectSid` | přímý |
| export | trvale | `personalTitle` | `personalTitle` | přímý |
| import | trvale | `prod` | `LDAPprod` | konstanta |
| export | trvale | `extensionAttribute10` | `extensionAttribute10` | přímý |
| export | trvale | `displayNameVSE` | `displayName` | přímý |
| export | trvale | `userPrincipalName` | `userPrincipalName` | přímý |
| export | trvale | `IIF(IsPresent(userAccountControl), userAccountControl, 514)` | `userAccountControl` | advanced userAccountControl |
| export | trvale | `eon-slovakia.sk` | `extensionAttribute14` | konstanta |

### User_VSE_ADLDS(ZSE only ucty) prod

- Objekt: metaverse `person`, konektor `user`. Směr `IN+OUT`, precedence `4`.
- Projekce: ne. Provisioning: ano.
- Join: `accountNameVSE = sAMAccountName`.
- Filtr: `VSENoSync EQUAL ZSEOnly a extensionAttribute8 NOTEQUAL 1`.

| Směr | Kdy | Zdroj | Cíl | Typ |
| --- | --- | --- | --- | --- |
| export | jen při provisioningu | `objectSid` | `objectSid` | přímý |
| export | jen při provisioningu | `distinguishedNameLDAP` | `dn` | při provisioningu |
| export | trvale | `accountName` | `sAMAccountName` | přímý |
| export | trvale | `department` | `department` | přímý |
| export | trvale | `displayName` | `displayName` | přímý |
| export | trvale | `employeeID` | `employeeNumber` | přímý |
| export | trvale | `firstName` | `givenName` | přímý |
| export | trvale | `jobTitle` | `title` | přímý |
| export | trvale | `lastName` | `sn` | přímý |
| export | trvale | `mobilePhone` | `mobile` | přímý |
| export | trvale | `personalTitle` | `personalTitle` | přímý |
| import | trvale | `prod` | `LDAPprod` | konstanta |
| export | trvale | `userAccountControl` | `userAccountControl` | přímý |
| export | trvale | `userPrincipalName` | `userPrincipalName` | přímý |

### User_VSE_ADLDS(ZSE_spolocnpsti)_prod

- Objekt: metaverse `person`, konektor `user`. Směr `IN+OUT`, precedence `7`.
- Projekce: ne. Provisioning: ano.
- Join: `accountName = sAMAccountName`.
- Filtr: `distinguishedName ENDSWITH OU=Users,OU=BU0390,OU=MU22,DC=eon-slovakia,DC=sk a usrID EQUAL eon-slovakia.sk a eonStatus EQUAL A a eonUserType EQUAL primary a VSENoSync NOTEQUAL No a distinguishedNameLDAP ENDSWITH OU=Spolocnosti,DC=ad,DC=vse,DC=sk a distinguishedName NOTENDSWITH OU=Disabled_Users,OU=Users,OU=BU0390,OU=MU22,DC=eon-slovakia,DC=sk`.

| Směr | Kdy | Zdroj | Cíl | Typ |
| --- | --- | --- | --- | --- |
| export | jen při provisioningu | `distinguishedNameLDAP` | `dn` | při provisioningu |
| export | jen při provisioningu | `objectSid` | `objectSid` | přímý |
| export | trvale | `accountName` | `sAMAccountName` | přímý |
| export | trvale | `department` | `department` | přímý |
| export | trvale | `displayName` | `displayName` | přímý |
| export | trvale | `email` | `mail` | přímý |
| export | trvale | `employeeID` | `employeeNumber` | přímý |
| export | trvale | `firstName` | `givenName` | přímý |
| export | trvale | `jobTitle` | `title` | přímý |
| export | trvale | `lastName` | `sn` | přímý |
| export | trvale | `mobilePhone` | `mobile` | přímý |
| export | trvale | `personalTitle` | `personalTitle` | přímý |
| export | trvale | `userPrincipalName` | `userPrincipalName` | přímý |
| export | trvale | `userAccountControl` | `userAccountControl` | přímý |

### User_VSE_ADLDS(ZSE_Servisne_ucty)_prod

- Objekt: metaverse `person`, konektor `user`. Směr `IN+OUT`, precedence `8`.
- Projekce: ne. Provisioning: ano.
- Join: `accountName = sAMAccountName`.
- Filtr: `distinguishedName ENDSWITH OU=ITServices,DC=eon-slovakia,DC=sk a VSENoSync NOTEQUAL No a distinguishedNameLDAP ENDSWITH OU=services.zse,OU=Spolocnosti,DC=ad,DC=vse,DC=sk`.

| Směr | Kdy | Zdroj | Cíl | Typ |
| --- | --- | --- | --- | --- |
| export | jen při provisioningu | `distinguishedNameLDAP` | `dn` | při provisioningu |
| export | jen při provisioningu | `objectSid` | `objectSid` | přímý |
| export | trvale | `accountName` | `sAMAccountName` | přímý |
| export | trvale | `displayName` | `displayName` | přímý |
| export | trvale | `firstName` | `givenName` | přímý |
| export | trvale | `lastName` | `sn` | přímý |
| export | trvale | `userPrincipalName` | `userPrincipalName` | přímý |

### User_VSE_ADLDS_All_prod

- Objekt: metaverse `person`, konektor `user`. Směr `IN+OUT`, precedence `9`.
- Projekce: ne. Provisioning: ne.
- Join: `accountName = company`.
- Filtr: žádný.

| Směr | Kdy | Zdroj | Cíl | Typ |
| --- | --- | --- | --- | --- |
| export | trvale | `eon-slovakia.sk` | `extensionAttribute14` | konstanta |
| import | trvale | `dn` | `distinguishedNameLDAPprod` | přímý |
| export | trvale | `sk-SK` | `preferredLanguage` | konstanta |

### User_VSE_ADLDS(ZSE_org_Servisne_ucty)_prod

- Objekt: metaverse `person`, konektor `user`. Směr `IN+OUT`, precedence `11`.
- Projekce: ne. Provisioning: ano.
- Join: `accountName = sAMAccountName`.
- Filtr: `distinguishedName ENDSWITH OU=services,OU=org,DC=eon-slovakia,DC=sk`.

| Směr | Kdy | Zdroj | Cíl | Typ |
| --- | --- | --- | --- | --- |
| export | jen při provisioningu | `objectSid` | `objectSid` | přímý |
| export | jen při provisioningu | `ReplaceString(distinguishedName, "OU=services,OU=org,DC=eon-slovakia,DC=sk", "OU=services.zse,OU=Spolocnosti,DC=ad,DC=vse,DC=sk")` | `dn` | při provisioningu |
| export | trvale | `accountName` | `sAMAccountName` | přímý |
| export | trvale | `displayName` | `displayName` | přímý |
| export | trvale | `firstName` | `givenName` | přímý |
| export | trvale | `lastName` | `sn` | přímý |
| export | trvale | `userPrincipalName` | `userPrincipalName` | přímý |

## SAPCUA

Třída `MaSapCua`. Projekce není, uživatele zakládá provisioning, když je `IsSAPCUA`. Větev VSE (firma obsahuje Vychodoslovenska) má přednost a klíč je `msDS-SourceAnchor`. Větev ZSE má klíč `accountName`.

### User SAPCUA - outbound

- Objekt: metaverse `person`, konektor `User`. Směr `IN+OUT`, precedence `1`.
- Projekce: ne. Provisioning: ano.
- Join: `accountName = KID`.
- Filtr: `IsSAPCUA EQUAL true a domain EQUAL EON-SK`.

| Směr | Kdy | Zdroj | Cíl | Typ |
| --- | --- | --- | --- | --- |
| export | jen při provisioningu | `accountName` | `KID` | přímý |
| export | trvale | `firstName` | `NAME_FIRST` | přímý |
| export | trvale | `jobTitle` | `FUNCTION` | přímý |
| export | trvale | `lastName` | `NAME_LAST` | přímý |
| export | trvale | `mobilePhone1` | `TELNR_CALL` | přímý |
| export | trvale | `IIF(IsPresent(persg), IIF(Eq(persg, "1"), bukrs, IIF(Eq(persg, "3"), departmentNumber, "0000")), "0000")` | `COMPANY` | advanced COMPANY |
| export | trvale | `IIF(IsPresent(persg), IIF(Eq(persg, "1"), IIF(IsPresent(company), company, menoOJ), IIF(Eq(persg, "3"), menoOJ, Null())), Null())` | `NAME1` | advanced NAME1 |
| export | trvale | `telephoneNumber1` | `TEL_NUMBER` | přímý |
| export | trvale | `SK` | `COUNTRY` | konstanta |
| import | trvale | `IIF(IsPresent(CLASS), IIF(Eq(CLASS, "DISABLED"), "false", "true"), "true")` | `IsSAPCUA` | advanced IsSAPCUA |
| export | trvale | `IIF(Eq(stat2, "1"), "3", stat2)` | `STAT2` | advanced STAT2 |
| export | trvale | `IIF(Eq(persk, "E2"), "EXTERNAL", Null())` | `CLASS` | advanced CLASS |
| export | trvale | `usrID` | `USRID` | přímý |
| export | trvale | `LowerCase(email)` | `SMTP_ADDR` | advanced SMTP_ADDR |
| export | trvale | `begda` | `GLTGV` | přímý |
| export | trvale | `accountName` | `KID` | přímý |
| export | trvale | `IIF(IsPresent(endda), endda, employeeEndDateCUA)` | `GLTGB` | advanced GLTGB |

### User SAPCUA-outbound VSE

- Objekt: metaverse `person`, konektor `User`. Směr `IN+OUT`, precedence `2`.
- Projekce: ne. Provisioning: ano.
- Join: `msDS-SourceAnchor = KID`.
- Filtr: `IsSAPCUA EQUAL true a company CONTAINS Vychodoslovenska`.

| Směr | Kdy | Zdroj | Cíl | Typ |
| --- | --- | --- | --- | --- |
| export | jen při provisioningu | `msDS-SourceAnchor` | `KID` | přímý |
| export | trvale | `email` | `SMTP_ADDR` | přímý |
| export | trvale | `firstName` | `NAME_FIRST` | přímý |
| export | trvale | `jobTitle` | `FUNCTION` | přímý |
| export | trvale | `lastName` | `NAME_LAST` | přímý |
| export | trvale | `mobilePhone` | `TELNR_CALL` | přímý |
| export | trvale | `telephoneNumber` | `TEL_NUMBER` | přímý |
| export | trvale | `SK` | `COUNTRY` | konstanta |
| export | trvale | `IIF(Eq(stat2, "1"), "3", stat2)` | `CLASS` | advanced CLASS-from-stat2 |
| export | trvale | `IIF(IsPresent(usrID), usrID, "")` | `USRID` | advanced USRID |

## ZSEEUser

Třída `MaZsee`. Projekce není. Jeden objekt metaverse založí ve ZSEE jen jeden řádek, první vyhovující větev v `MVExtensionObject`.

### User ZSEE

- Objekt: metaverse `person`, konektor `person`. Směr `IN+OUT`, precedence `1`.
- Projekce: ne. Provisioning: ano.
- Join: `employeeID = OsobneCislo`.
- Filtr: `eonStatus EQUAL A a eonUserPurpose EQUAL regular a eonUserType EQUAL primary a humanet STARTSWITH hum`.

| Směr | Kdy | Zdroj | Cíl | Typ |
| --- | --- | --- | --- | --- |
| export | jen při provisioningu | `employeeID` | `OsobneCislo` | přímý |
| export | trvale | `accountName` | `KID` | přímý |
| export | trvale | `email` | `Email` | přímý |
| export | trvale | `firstName` | `Meno` | přímý |
| export | trvale | `lastName` | `Priezvisko` | přímý |
| export | trvale | `mobilePhone` | `Mobil` | přímý |
| export | trvale | `city` | `Mesto` | přímý |
| export | trvale | `department` | `Oddelenie_Kod` | přímý |
| export | trvale | `physicalDeliveryOfficeName` | `Kancelaria` | přímý |
| export | trvale | `street` | `Ulica` | přímý |
| export | trvale | `manager` | `Nadriadeny` | přímý |
| export | trvale | `displayNameFull` | `Tituly` | přímý |
| export | trvale | `userPrincipalName` | `UPN` | přímý |
| export | trvale | `begda` | `Nastup` | přímý |
| export | trvale | `endda` | `Vystup` | přímý |
| export | trvale | `bukrs` | `Firma_Id` | přímý |

### User ZSEE Externisti

- Objekt: metaverse `person`, konektor `person`. Směr `IN+OUT`, precedence `2`.
- Projekce: ne. Provisioning: ano.
- Join: `employeeID = OsobneCislo`.
- Filtr: `externisti STARTSWITH id a eonStatus EQUAL A a externisti_processed EQUAL 1 a extensionAttribute8 NOTEQUAL 1`.

| Směr | Kdy | Zdroj | Cíl | Typ |
| --- | --- | --- | --- | --- |
| export | jen při provisioningu | `employeeID` | `OsobneCislo` | přímý |
| export | trvale | `menoOJ` | `ExtFirma_Nazov` | přímý |
| export | trvale | `accountName` | `KID` | přímý |
| export | trvale | `firstName` | `Meno` | přímý |
| export | trvale | `lastName` | `Priezvisko` | přímý |
| export | trvale | `begda` | `Nastup` | přímý |
| export | trvale | `city` | `Mesto` | přímý |
| export | trvale | `department` | `Oddelenie_Kod` | přímý |
| export | trvale | `email` | `Email` | přímý |
| export | trvale | `endda` | `Vystup` | přímý |
| export | trvale | `mobilePhone` | `Mobil` | přímý |
| export | trvale | `userPrincipalName` | `UPN` | přímý |
| export | trvale | `bukrs` | `Firma_Id` | přímý |

### User ZSEE Sync

- Objekt: metaverse `person`, konektor `person`. Směr `OUT`, precedence `3`.
- Projekce: ne. Provisioning: ne.
- Join: žádný, objekt se páruje jen při provisioningu.
- Filtr: žádný.

| Směr | Kdy | Zdroj | Cíl | Typ |
| --- | --- | --- | --- | --- |
| export | trvale | `email` | `Email` | přímý |
| export | trvale | `firstName` | `Meno` | přímý |
| export | trvale | `lastName` | `Priezvisko` | přímý |
| export | trvale | `bukrs` | `Firma_Id` | přímý |
| export | trvale | `ReplaceString(Left(employeeEndDate, 10), "-", "")` | `Vystup` | advanced Vystup |
| export | trvale | `ReplaceString(Left(employeeStartDate, 10), "-", "")` | `Nastup` | advanced Nastup |

## KOC

Třída `MaKoc`. Projekce není. Zakládá se, když `KOC` je 1 a typ je `I`, nebo typ `E` a `eonStatus` je A.

### User KOC int

- Objekt: metaverse `person`, konektor `person`. Směr `OUT`, precedence `1`.
- Projekce: ne. Provisioning: ano.
- Join: žádný, objekt se páruje jen při provisioningu.
- Filtr: `employeeType EQUAL I a KOC EQUAL 1`.

| Směr | Kdy | Zdroj | Cíl | Typ |
| --- | --- | --- | --- | --- |
| export | jen při provisioningu | `employeeID` | `PERNR` | přímý |
| export | trvale | `accountName` | `KID` | přímý |
| export | trvale | `IIF(Eq(employeeType, "I"), "int", "ext")` | `Type` | advanced Type |
| export | trvale | `firstName` | `GivenName` | přímý |
| export | trvale | `lastName` | `FamilyName` | přímý |
| export | trvale | `KOC` | `KOC` | přímý |

### User KOC ext

- Objekt: metaverse `person`, konektor `person`. Směr `OUT`, precedence `2`.
- Projekce: ne. Provisioning: ano.
- Join: žádný, objekt se páruje jen při provisioningu.
- Filtr: `employeeType EQUAL E a eonStatus EQUAL A a KOC EQUAL 1`.

| Směr | Kdy | Zdroj | Cíl | Typ |
| --- | --- | --- | --- | --- |
| export | jen při provisioningu | `employeeID` | `PERNR` | přímý |
| export | trvale | `accountName` | `KID` | přímý |
| export | trvale | `IIF(Eq(employeeType, "I"), "int", "ext")` | `Type` | advanced Type |
| export | trvale | `firstName` | `GivenName` | přímý |
| export | trvale | `lastName` | `FamilyName` | přímý |
| export | trvale | `KOC` | `KOC` | přímý |

### User KOC nastav KOC na 0

- Objekt: metaverse `person`, konektor `person`. Směr `OUT`, precedence `3`.
- Projekce: ne. Provisioning: ne.
- Join: žádný, objekt se páruje jen při provisioningu.
- Filtr: žádný.

| Směr | Kdy | Zdroj | Cíl | Typ |
| --- | --- | --- | --- | --- |
| export | trvale | `KOC` | `KOC` | přímý |

## HUMANET_OUT

Extension na konektoru není potřeba, toky jsou přímé. Zakládá se, když `humanet` začíná na `hum` a `stat2` je 3. DN je `CN={employeeID},OBJECT=Person`.

### User HUMANET OUt

- Objekt: metaverse `person`, konektor `Person`. Směr `IN+OUT`, precedence `1`.
- Projekce: ne. Provisioning: ano.
- Join: `employeeID = roleNum`.
- Filtr: `humanet STARTSWITH hum a stat2 EQUAL 3`.

| Směr | Kdy | Zdroj | Cíl | Typ |
| --- | --- | --- | --- | --- |
| export | jen při provisioningu | `+("CN=", employeeID, ",OBJECT=Person")` | `dn` | při provisioningu |
| export | trvale | `accountName` | `kid` | přímý |
| export | trvale | `begda` | `validFrom` | přímý |
| export | trvale | `displayName` | `displayName` | přímý |
| export | trvale | `email` | `email` | přímý |
| export | trvale | `employeeID` | `roleNum` | přímý |
| export | trvale | `endda` | `validTo` | přímý |
| export | trvale | `lastName` | `priezvisko` | přímý |
| export | trvale | `telephoneNumber` | `phone` | přímý |
| export | trvale | `mobilePhone` | `mobile` | přímý |

## Externisti_SAP_OUT

Extension na konektoru není potřeba, toky jsou přímé. Zakládá se, když `externisti` začíná na `id`, `externisti_processed` je 1 a `extensionAttribute8` není 1.

### Users Externisti SAP OUT

- Objekt: metaverse `person`, konektor `person`. Směr `IN+OUT`, precedence `1`.
- Projekce: ne. Provisioning: ano.
- Join: `employeeID = PERNR`.
- Filtr: `externisti STARTSWITH id a externisti_processed EQUAL 1 a extensionAttribute8 NOTEQUAL 1`.

| Směr | Kdy | Zdroj | Cíl | Typ |
| --- | --- | --- | --- | --- |
| export | jen při provisioningu | `employeeID` | `PERNR` | přímý |
| export | trvale | `accountName` | `KID` | přímý |
| export | trvale | `begda` | `BEGDA` | přímý |
| export | trvale | `bukrs` | `BUKRS` | přímý |
| export | trvale | `email` | `EMAIL` | přímý |
| export | trvale | `endda` | `ENDDA` | přímý |
| export | trvale | `firstName` | `VORNA` | přímý |
| export | trvale | `idNS` | `KOSTL` | přímý |
| export | trvale | `lastName` | `NACHN` | přímý |
| export | trvale | `mobilePhone` | `MOBILE` | přímý |
| export | trvale | `stat2` | `STAT2` | přímý |
| export | trvale | `city` | `ORT01` | přímý |
| export | trvale | `ext_applicant_name` | `ZIADATEL_MENO` | přímý |
| export | trvale | `postalCode` | `PSTLZ1` | přímý |
| export | trvale | `Werks` | `WERKS` | přímý |
| export | trvale | `ext_applicant_kid` | `ZIADATEL_PERNR` | přímý |

