# SAPBAPI_Direct

Nový MIM ECMA2 konektor pro SAP BAPI. Volá RFC přímo přes sapnco 3.1, **bez sapTools.exe**.
Původní projekt `SAPBAPI` se nemění.

Schéma zůstává SEAS (`Person` + `Group` jako přiřazení role, anchor `Role_User`).
Z OmniSync konektoru (`OmniSync.Connectors.SAP`) jsou převzaté RFC speciality,
ne OmniSync model `User` / `Role` / `Cast`.

## Proč nový projekt

Původní MA:

- `SupportExport = false` a `PutExportEntries` vrací prázdný výsledek.
- sapTools.exe loguje BAPI chyby, ale do MIM je neposílá.
- BAPI `RETURN` typu E/A často neshodí RFC výjimku, takže export skončí úspěchem.

Nový MA:

- `SupportExport = true`.
- Každý objekt vrací `CSEntryChangeResult`. Při chybě
  `MAExportError.ExportErrorCustomContinueRun` s textem z `RETURN` nebo exception.

## Co je z OmniSync a co je SEAS

| Chování | Zdroj | V Direct |
| --- | --- | --- |
| Kontrola `RETURN` E/A/X | OmniSync `CheckReturn` | ano |
| `BAPI_TRANSACTION_COMMIT` s `WAIT=X` | OmniSync | ano |
| `BAPI_USER_ACTGROUPS_ASSIGN` je **replace-all** (nejprve načíst stávající role) | OmniSync | ano |
| Ověření po assignu, že role opravdu existuje | OmniSync | ano |
| `ALIASX.BAPIALIAS` při změně aliasu | OmniSync / SAPTools | ano |
| Process-wide NCo destination (`SID_client`) | OmniSync | ano |
| Schema User / Role / Cast, `AGR_DEFINE`, child roles | OmniSync | ne |
| Filtr `SAP_*` standardních rolí, `UserFilter` | OmniSync | ne |
| Person + Group `Role_User` | SEAS | ano |
| Import zahazuje `ORG_FLAG=C` (composite) | SEAS | ano |
| Dependant role (výchozí `ZRFCCALL`) – přidat s první rolí, odebrat když žádná jiná nezbyde | SEAS | ano |
| Filtr Group z MV (`applicationCode LIKE 'SAP%'`, bez RDPV) | SEAS | ano |
| Create defaults `SPLD=LOCM`, `SPDB=G`, `LANGU=Q`, `LANGUP_ISO=SK` | SEAS | ano |
| Lock `L` / unlock `U` přes `BAPI_USER_LOCK` / `UNLOCK` | SEAS | ano |
| `USERGROUP=X` znamená prázdnou CLASS | SEAS | ano |
| Delete Person | ani jeden | ne (SAP účet se lockuje) |

## Požadavky

- MIM 2016 Synchronization Service (.NET Framework 4.8, x64).
- SAP NCo 3.1 v GAC_64 (`sapnco` + `sapnco_utils`, ověřeno `v4.0_3.1.0.42`).
- RFC účet s právy na `BAPI_USER_GETLIST`, `GET_DETAIL`, `CREATE1`, `CHANGE`,
  `LOCK`, `UNLOCK`, `ACTGROUPS_ASSIGN`, `BAPI_TRANSACTION_COMMIT`.
- SQL účet s `SELECT` na `FIMSynchronizationService.dbo.mms_metaverse` (jen filtr Group; při chybě se importují všechny role).

## Build a nasazení

```powershell
.\Deploy.ps1 -Build
.\Deploy.ps1 -RestartService
```

Deploy kopíruje `SAPBAPI_Direct.dll`, `sapnco.dll` a `sapnco_utils.dll` do Extensions
(x64 NCo 3.1 — `miiserver.exe` je 64bit, GAC u synchronizační služby nestačí).

## Vytvoření MA

1. Synchronization Service Manager → **Create** → **Extensible Connectivity 2.0**.
2. Assembly: `SAPBAPI_Direct.dll`.
3. **Connectivity**
   - App server host, System number, System ID, Client, Language
   - RFC user + RFC password
4. **Global**
   - Log file (výchozí `%ProgramData%\SAPBAPI_Direct\SAPBAPI_Direct.log`)
   - XML file – prázdné = bez archivu i bez role indexu (Delta se chová jako Full).
     Jinak se po importu přepíše XML archiv a vedle něj `{název}.roles.idx` (členství pro deltu).
   - SQL server connectionstring na `FIMSynchronizationService`
   - Dependant roles (středníkem), výchozí `ZRFCCALL`

Join a flow pravidla z původního SAP MA jdou použít – object types a atributy jsou stejné
(`Person.ID`, `Group.ID` = `AGR_NAME_USERNAME`).

## Delta import

SAP se stahuje celý (GETLIST + GET_DETAIL). Do CS jdou jen změny:

- **Person** – `LASTMODIFIED` větší než watermark v `CustomData`, nový účet, nebo účet zmizel z GETLIST
- **Group** – diff proti `{XML}.roles.idx` (`USERNAME|ROLE|FROM|TO`)

První běh, prázdné XML / chybějící index / prázdný watermark = Full emit. XML se pro porovnání nečte.

## Export chyby v MIM

V Operations / CS export errors uvidíte kód `sap-bapi-error` a text z BAPI, např.:

`BAPI_USER_CHANGE[U123]: E SU123: User U123 does not exist`

Run pokračuje (`ExportErrorCustomContinueRun`), ostatní objekty v dávce se zpracují.
