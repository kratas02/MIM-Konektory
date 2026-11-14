# Životní cyklus objektů

Zdroj je export synchronizace ze serveru `SMD1MGIDM01P` (4. 11. 2026) a synchronizační pravidla v `Policy.xml`. Portálové workflow, které objektu doplní účet, schránku a stav, jsou v [Procesy-portalu.md](Procesy-portalu.md).

Žádné synchronizační pravidlo nemá zapnuté odpojení objektu v konektoru (`DisconnectConnectedSystemObject` je všude nepravda). Když objekt přestane splňovat filtr pravidla, řádek v cílovém systému zůstane a pravidlo ho přestane aktualizovat. Smazání v cíli nastává jen tehdy, když zmizí objekt v metaverse a konektor má akci `delete-object`.

| Konektor | Co je | Když zmizí objekt v metaverse |
| --- | --- | --- |
| `HUMANETv2` | SQL, zdroj zaměstnanců. `usrID` `eon-slovakia.sk` nebo `ad.vse.sk` | `delete-object`, export smaže řádek v HR |
| `Externisti` | zdroj externistů | objekt v konektoru se jen odpojí, řádek ve zdroji se nemaže |
| `eon-sk` | Active Directory `eon-slovakia.sk` | účet i skupina v AD zůstanou, jen se odpojí |
| `ad.vse.sk` | Active Directory `ad.vse.sk` | účet zůstane a znovu se sám nenaváže (`make-explicit-disconnector`) |
| `SAPCUA` | SAP CUA | uživatel v SAP zůstane |
| `VSE_LDAP_prod` | LDAP `DC=ad,DC=vse,DC=sk` | objekt v LDAP se smaže |
| `HUMANET_OUT` | zpětný zápis do Humanetu | řádek se smaže |
| `Externisti_SAP_OUT` | zpětný zápis externisty do SAP | řádek se smaže |
| `ZSEEUser` | tabulka `ZSEE_Identita` | řádek se smaže |
| `KOC` | tabulka `KOC_PERSON` | řádek se smaže |
| `MIMMA` | portál | objekt portálu se jen odpojí |

## Zaměstnanec ZSE

Zakládá ho `HUMANETv2`. Pravidlo `User HUMANET v2 Provisioning` vezme osobu s `usrID = eon-slovakia.sk`, páruje ji podle `employeeID` a založí osobu v portálu. Samo o sobě do portálu nese jen osobní číslo. Jméno, stav `stat2`, společnost a další HR atributy doplňují navazující inbound pravidla téhož konektoru (`User HUMANETv2 join EmployeeID`, `User HUMANETv2 sync only`).

Portál pak, při `stat2 = 3` a vyplněném osobním čísle, vygeneruje KID (`AccountName`, `distinguishedName`, `userPrincipalName`, `scriptPath`). Dohodář (`persk` na `D`) dostane KID až když má `CreateADAccount = True`. Primární účet se společností 4212–4218 nebo 4255 dostane schránku (`eonMailEnabled`, licence E5, přípona podle `bukrs`).

Kam účet pokračuje:

| Podmínka na osobě v portálu | Konektor | Co se založí |
| --- | --- | --- |
| `distinguishedName` je v `DC=eon-slovakia,DC=sk` | `eon-sk`, pravidlo `Users eon-sk in-out` | uživatel v AD. Při vzniku je účet zakázaný (`userAccountControl` 514), jazyk `sk-SK` a jednorázové počáteční heslo. DN se bere z portálu, párování je `AccountName` = `sAMAccountName` |
| `humanet` začíná `hum` a `stat2 = 3` | `HUMANET_OUT` | řádek Person: KID, jméno, platnost, e-mail, telefony. Klíč je osobní číslo |
| `eonStatus = A`, účel `regular`, typ `primary`, `humanet` začíná `hum` | `ZSEEUser`, pravidlo `User ZSEE` | řádek identity pro běžného zaměstnance |
| `IsSAPCUA = true` a `domain = EON-SK` | `SAPCUA` | uživatel SAP. Jméno, telefony, platnost, stav, KID |
| `EmployeeType = I` a `KOC = 1` | `KOC` | řádek `KOC_PERSON` |
| DN v `OU=services,OU=org`, `OU=ITServices` nebo `OU=Users,OU=BU0390,OU=MU22` domény `eon-slovakia.sk`, případně `VSENoSync = ZSEOnly` | `VSE_LDAP_prod` | uživatel v LDAP VSE |

Konec zaměstnance není smazání. Humanet přinese `stat2 = 0`. Portálové MPR, které z toho mělo zapsat `userAccountControl = 514`, je vypnuté, a MPR, které mělo přepsat stav na neaktivní při ztrátě společnosti, je také vypnuté a navíc zapisuje neexistující atribut `eonStatis`. Jediné, co synchronizace při `stat2 = 0` udělá, je export aktuální hodnoty `userAccountControl` z portálu do `eon-sk` (pravidlo `Users eon-sk out - disable when stat2-0`). Dokud portál tu hodnotu nezmění na 514, účet v AD zůstane v tom stavu, v jakém je. Uživatel v SAP CUA se při odpojení nemaže. Odpojení ze SAP řeší portálové workflow: nastaví `EmployeeEndDate` a osobu odebere ze skupiny `PassSyncCUA`. Osoba v portálu zůstává.

Smazání osoby v portálu (smí ho administrátor) by kvůli akci `delete-object` smazalo i řádek v `HUMANETv2`, `HUMANET_OUT`, `ZSEEUser`, `KOC` a odpovídající objekt v `VSE_LDAP_prod`. Účet v `eon-sk` a uživatel v SAP CUA by zůstali.

## Zaměstnanec VSE

Zakládají ho dva zdroje, oba založí osobu v portálu:

- `HUMANETv2`, pravidlo `User HUMANET v2 Provisioning VSE`, filtr `usrID = ad.vse.sk`, párování `employeeID`.
- `ad.vse.sk`, pravidlo `user_AD.VSE.SK`. Bere uživatele, jejichž `userAccountControl` není 514, a nese do portálu účet, DN, SID, e-mail, UPN a zobrazované jméno do atributů s příponou `VSE`. Skupiny z `OU=Autorizacia,OU=GPOGroups,DC=ad,DC=vse,DC=sk` zakládá pravidlo `Groups_AD.VSE.SK`.

KID pro tyto účty skládá workflow `_VSE_Generate_KID`. Účty, jejichž DN VSE končí v `OU=Spolocnosti,DC=ad,DC=vse,DC=sk` a zároveň mají protějšek v `OU=org` domény `eon-slovakia.sk`, dostanou v portálu kopii s prefixem `copy-`.

Kam pokračují:

- `VSE_LDAP_prod`, pravidlo `User_VSE_ADLDS_prod`: osoba má DN v `DC=ad,DC=vse,DC=sk`, `VSENoSync` není `No` ani `ZSEOnly` a zobrazované jméno nezačíná `copy-`. V LDAP se zakládá uživatel.
- Kopie a účty `VSENoSync = ZSEOnly` jdou do LDAP jinými pravidly (`User_VSE_ADLDS_copy_prod`, `User_VSE_ADLDS(ZSE only ucty) prod`). `ZSEOnly` se do LDAP nedostane, když má `extensionAttribute8 = 1`.
- `SAPCUA`, pravidlo `User SAPCUA-outbound VSE`: `IsSAPCUA = true` a společnost obsahuje `Vychodoslovenska`. Klíč je `msDS-SourceAnchor`, ne KID ZSE.
- Do `eon-sk` se dostanou stejně jako ostatní osoby, jakmile mají `distinguishedName` v `DC=eon-slovakia,DC=sk`.

Když zmizí objekt v metaverse, účet v `ad.vse.sk` zůstane a znovu se nenaváže. Objekt v `VSE_LDAP_prod` se smaže. Portálové mazání kopie po smazání originálu (`_VSE_User_Only_delete_when_deleted` a mazání skupinových kopií) je ve filtru sady omezené na `DisplayName` začínající `xxx`, takže v provozu prakticky neběží.

## Externista

Zakládá ho konektor `Externisti`. Pravidlo `User Externisti Provisioning` páruje `employeeID` na `personal_number` a založí osobu v portálu. Další pravidla téhož konektoru (`User Externisti Join Id`, `Join KID`, `Sync Only`, `predlzenie platnosti`) už osobu nezakládají, jen připojují existující účet podle KID nebo prodlužují platnost, když je `ext_applicant_platnost = true`.

Portál doplní žadatele, e-mail, telefon, KID a stav. V okně smlouvy nastaví `stat2 = 3` a `eonStatus = A`. Po konci smlouvy běží dvě stejně pojmenovaná MPR `_ZSE_Externisti_expirovany`: jedno nastaví `stat2 = 0` a `eonStatus = I`, druhé naopak `stat2 = 3` a `eonStatus = A`.

Kam pokračuje zpracovaný externista (`externisti` začíná `id`, `externisti_processed = 1`):

| Podmínka | Konektor |
| --- | --- |
| DN v `DC=eon-slovakia,DC=sk` | `eon-sk`, stejné pravidlo jako zaměstnanec. Účet vzniká zakázaný (514) |
| a navíc `extensionAttribute8` není `1` | `Externisti_SAP_OUT` (osobní číslo, platnost, společnost, žadatel) a `ZSEEUser` pravidlo `User ZSEE Externisti`, to jen při `eonStatus = A` |
| `EmployeeType = E`, `eonStatus = A`, `KOC = 1` | `KOC` |

`extensionAttribute8 = 1` se u žadatele s KID od `K27788` nastavuje schválně, aby se externista do SAP a do ZSEE nepropsal. Zmizení řádku ve zdroji `Externisti` osobu v portálu nesmaže, konektor ji jen odpojí. Smazání osoby v portálu smaže její řádky v `Externisti_SAP_OUT`, `ZSEEUser` a `KOC`. Účet v `eon-sk` zůstane.

## Sekundární účet

Nezakládá ho žádný zdrojový konektor. Portálové workflow z osoby založí novou osobu (`eonUserType = secondary`) a vygeneruje jí vlastní KID s UPN `AccountName@e-ssi05.net`. Service Desk takovou osobu smí založit ručně. Dál platí stejné pravidlo `Users eon-sk in-out`: jakmile má DN v `DC=eon-slovakia,DC=sk`, vznikne uživatel v AD, opět nejdřív zakázaný. Do Humanetu, ZSEE, SAP CUA ani KOC nepatří, protože nesplní jejich filtry (`eonUserType = primary`, `humanet` začíná `hum`, `IsSAPCUA`).

## Skupina

Distribuční skupina nemá vlastní konektor. `eon-sk` při importu rozliší typ podle `groupType`: bez security bitu je `Distribution`, se security bitem a vyplněným `mailNickname` je `MailEnabledSecurity`, jinak `Security`. Oprávnění zakládat a spravovat distribuční listy v portálu jsou vypnutá. Správci zakládají statické bezpečnostní skupiny.

### Skupina v eon-sk

Do portálu přichází dvěma pravidly:

- `Group eon-sk in` založí skupinu v portálu, když `adminDescription` nezačíná na `TYP`. Skupiny označené portálem (`TYPMIMManualne`, `TYPMIMDynamicke`, `TYPMIMVSE`) se tímto pravidlem z AD znovu nezaloží.
- `Group eon-sk in-out` skupinu propojí podle `sAMAccountName`, načte členy, e-mail a typ a zpět exportuje `adminDescription`. Vynechává jen `cn = _ZSE_SAPCUA_Users`.

Z portálu do AD skupinu zakládá `Groups_eon-sk_Create_in_eon`, když DN končí v `OU=Groups,DC=eon-slovakia,DC=sk`, zobrazované jméno neobsahuje `copy-` a `accountName` obsahuje `.sk`. Exportuje jméno, popis, členy, `groupType` a `sAMAccountName`. Skupiny `copy-`, Azure ESK a `sf-dfsroot` tímto pravidlem nevznikají, mají vlastní cestu níže.

Když portál skupinu smaže (temporal workflow `Expiration Workflow` maže objekt po `ExpirationTime`, 14 dní předem jde vlastníkovi upozornění), skupina v `eon-sk` zůstane. Konektor ji jen odpojí.

### Skupina VSE a kopie

`Groups_AD.VSE.SK` založí v portálu skupinu z `OU=Autorizacia,OU=GPOGroups,DC=ad,DC=vse,DC=sk`. `Groups_VSE_LDAP_prod` ji pak založí v `VSE_LDAP_prod`, když má DN v `DC=ad,DC=vse,DC=sk` a `VSENoSync` není `No`. `Groups_VSE_LDAP_(ZSE v2 Only)_prod` zakládá skupiny s `LDAPsync = ZSEOnlyYes`. Když se skupina smaže v metaverse, její objekt v `VSE_LDAP_prod` se smaže, objekt v `ad.vse.sk` zůstane.

## Azure ESK

Skupinu nezakládá import z AD. Vznikne v portálu a do `eon-sk` ji založí odchozí pravidlo `_ZSE_Groups_OU=AzureESK,OU=authz,OU=Groups,DC=eon-slovakia,DC=sk`. Filtr je `distinguishedName` obsahuje `OU=AzureESK`. Při vzniku se zapíše jen DN. Trvale se exportuje `accountName` na `sAMAccountName`, `adminDescription`, `description`, `displayName`, `groupType` a `member`.

DN a typ skupině před zápisem do AD složí portál:

- `_ZSE_Groups_OU=AzureESK,…` když DN končí v `OU=AzureESK,OU=authz,…`, nebo `Description` začíná na `AzureESK` a není `AzureESKCS`. DN je `CN=` + `AccountName` + `,OU=AzureESK,OU=authz,OU=Groups,DC=eon-slovakia,DC=sk`.
- `_ZSE_Groups_OU=AzureESKCS,…` když DN leží v `OU=AzureESKCS`, nebo `Description` začíná na `AzureESKCS`. DN končí v `OU=AzureESKCS,OU=authz,OU=Groups,DC=eon-slovakia,DC=sk`.

Obě workflow nastaví `groupType = -2147483646`, tedy globální bezpečnostní skupinu. Nejsou to distribuční skupiny. Právo zakládat je a měnit jejich členy mají správci těchto OU.

Když skupina přijde zpět z AD, `Group eon-sk in` ji znovu nezaloží, pokud má `adminDescription` na `TYP`. Smazání v portálu skupinu v `eon-sk` nemaže, konektor ji jen odpojí. Do `VSE_LDAP_prod` ani do `ad.vse.sk` Azure ESK skupina nepatří.

## DFS skupiny

Jsou dvě různé skupiny a každá má jiný život.

**Kořen `sf-dfsroot`.** Skupina už je v AD `eon-sk`. Do metaverse se dostane běžným importem skupiny (`Group eon-sk in` a `Group eon-sk in-out`), pokud `adminDescription` nezačíná na `TYP`. Samostatné pravidlo `Group eon-sk in-out dfs` ji nezakládá ani v metaverse, ani v AD. Bere jen skupinu, jejíž `displayName` začíná na `sf-dfsroot`, napáruje ji podle `accountName` = `sAMAccountName` a trvale exportuje `member`. Jméno, popis ani `groupType` tímto pravidlem do AD nejdou. Smazání objektu v portálu skupinu v AD nemaže.

**Oprávnění ve `OU=DFS rights`.** Skupiny `mg`, `r` a `rw` v `OU=DFS rights` nejsou `sf-dfsroot`. Do portálu přijdou stejným importem z `eon-sk` jako ostatní skupiny. Portál jim neskládá DN a do AD je znovu nezakládá. Po změně členství skupiny končící na `mg` workflow `_ZSE_DFS_Update_Owners` nastaví `Owner` a `DisplayedOwner` podle členů `mg` a totéž zapíše na skupiny `r` a `rw` se stejným kmenem názvu. Když vlastník `mg` skupiny chybí nebo je to Service Desk, `_ZSE_DFS_Update_Service_Desk_Owner_when_Empty_or_Service_Desk_Change` doplní členy ze sady `_ZSE_Service_Desk`. Service Desk smí měnit sady `_ZSE_DFS_r` a `_ZSE_DFS_rw`.

Sdílené složky, jejichž `Description` začíná na `\\eon-slovakia.sk\`, jsou třetí větev. Workflow `_ZSE_Group_Set_adminDescription` jim nastaví `adminDescription` = `TYPMIMManualne`, `MembershipLocked` = false a `MembershipAddWorkflow` = `None`. Tím se z běžného importu vyřadí, protože `Group eon-sk in` skupiny s `adminDescription` na `TYP` znovu nezakládá.

## Skupiny copy-

Skupinu `copy-` nezakládá import. V portálu ji jako druhý objekt založí workflow `CreateResource`. Do názvu se vloží prefix `copy-`.

Dvě zapnutá pravidla:

- `_VSE_Copy_groups_create(VSE only)` se spustí, když skupina má `accountNameVSE`, DN ve VSE končí v `OU=Autorizacia\GPOGroups`, ještě nemá DN v ZSE a sama není kopie. `DisplayName` je `copy-` + jméno originálu, `AccountName` je `copy-` + účet zkrácený na 62 znaků. Z originálu se vezme `accountNameVSE`, `distinguishedNameVSE`, `Domain`, `Scope`, `Type`, `ExplicitMember` a `cnVSE`. `LDAPSource` ukazuje na originál.
- `_VSE_Copy_groups_create(ZSE v2)` se spustí pro skupinu v `OU=hrgroups` nebo `OU=authz\Groups` v `eon-slovakia.sk`, mimo `AzureESK` a `AzureESKCS`. Kopie dostane `DisplayName` = `copy-` + jméno, `VSENoSync` = `Yes`, `LDAPPrepared` = `stageZSE0` a vlastníka `miminstall`. Na originál se zapíše `LDAPTargetZSE`. Ve fázi `stageZSE0` navazující pravidlo doplní zpětné odkazy `LDAPReplace` a `LDAPTargetZSE`.

Starší zakládání `_VSE_Copy_groups_create(ZSE v OU=authz…)` a `_VSE_Copy_groups_create(ZSE_vytvorene manualne)` je vypnuté.

Členové se na kopii nepřenesou při založení. Nejdřív se nastaví `LDAPUpdate` = `stage1`, potom se členové přemapují přes `LDAPTargetZSE` a fáze se posune na `stage2`.

Do AD `eon-sk` se kopie nezapíše. `Groups_eon-sk_Create_in_eon` má filtr, že `displayName` neobsahuje `copy-`. Do `VSE_LDAP_prod` se dostane, jen když má `distinguishedNameVSE` v `DC=ad,DC=vse,DC=sk` a `VSENoSync` není `No`. Kopie ze ZSE má `VSENoSync` = `Yes`, takže do LDAP jde až ve chvíli, kdy jí workflow doplní VSE DN a sync pravidlo `Groups_VSE_LDAP_prod` filtr splní.

Mazání kopie po smazání originálu (`_VSE_Groups_Original_advse_VSE-Out-delete`) prakticky neběží. Sada originálů je zúžená na `DisplayName` začínající `xxx`. Když se objekt kopie smaže v metaverse, řádek v `VSE_LDAP_prod` se smaže. Skupina v `ad.vse.sk` zůstane a skupina v `eon-sk` se jen odpojí, pokud v něm vůbec byla.

## Co z toho plyne pro konec života

Účet v `eon-slovakia.sk` ani uživatel v SAP CUA se odebráním ze zdroje ani smazáním osoby v portálu nemažou. Zaměstnanec končí změnou `stat2` a případně ručním nebo workflow zápisem `userAccountControl`. Externista končí překlopením `stat2` a `eonStatus`, přičemž dvě expirace si protiřečí. Distribuční i bezpečnostní skupina v AD po smazání v portálu zůstane. Totéž platí pro Azure ESK, `sf-dfsroot` a skupiny v `OU=DFS rights`. Kopie `copy-` v `eon-sk` nevzniká. Její řádek v `VSE_LDAP_prod` se smaže až se smazáním objektu v metaverse, a mazání kopie spolu s originálem portálové workflow prakticky nespouští. Mažou se řádky v SQL výstupech (`HUMANET_OUT`, `Externisti_SAP_OUT`, `ZSEEUser`, `KOC`) a objekty v `VSE_LDAP_prod`, a to až se smazáním objektu v metaverse. Smazání osoby v portálu by navíc smazalo i zdrojový řádek v `HUMANETv2`.
