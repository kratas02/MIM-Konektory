# EasyIdmCastMA

Read-only ECMA2 konektor pro MIM 2016. Čte **SAP casty** z databáze **EasyIDM**
(ověřený server `MIM`) a umí **Full Import** i **Delta Import**.

Konektor **pouze čte**. Žádný export, žádný zápis do DB.

## Co je objekt v connector space

Jeden CS objekt = jeden řádek `dbo.Casts` (`CastId`), který projde filtrem
původního Full Import dotazu. Když má stejné `IdentityTechName` + `RoleTechName`
víc platných castů, importuje se jen novější (`ValidFrom` sestupně, při shodě
vyšší `CastId`):

- `c.IsArchived = 0`
- `c.ValidTo > GETDATE() OR c.ValidTo IS NULL`
- role má SAP `category` (`SAP_BAPI_ARCH` / `SAP_BAPI_PS4` / `SAP_BTP`)
- `r.TechName NOT LIKE 'RDPV_AUTBASE%'`

DN / anchor: `CN=<CastId>,CN=Cast`.

Kategorie se počítá stejně jako ve stávajícím dotazu:

- `applicationCode` = `JSON_VALUE` resource `$.applicationCodeAP` (přes `RoleAccesses`)
- `applicationModulCode` = `JSON_VALUE` role `$.applicationModulCode`

## Delta Import

Watermark je čas SQL serveru (`SYSDATETIME`) v `CustomData` run stepu.
Před první deltou musí proběhnout Full Import.

Do delty jde cast, když se od watermarku (mínus overlap) změnilo **něco z**:

| Zdroj | Sloupce |
| --- | --- |
| Cast | `Casts.Created`, `Casts.Modified` |
| Identity | `IDMObjects.Created`, `IDMObjects.Modified` (uživatel) |
| Role | `IDMObjects.Created`, `IDMObjects.Modified` (application-role) |
| Mapování aplikace | `RoleAccesses.Created`, `RoleAccesses.Modified` |
| Vypršení platnosti | `Casts.ValidTo` padlo do okna (i bez změny `Modified`) |
| Smazaný cast | `IsArchived = 1` AND `IsValid = 0` a zároveň `Created`/`Modified` v okně |

U každého kandidáta se znovu vyhodnotí aktuální stav:

- pořád validní SAP cast → `Replace`
- už neprojde filtrem (archivace, `IsValid=0`, vypršel `ValidTo`, ztráta kategorie) → `Delete`

## Požadavky

- MIM 2016 Synchronization Service (.NET Framework 4.6.2)
- účet synchronizační služby (nebo SQL login) s `db_datareader` na `EasyIDM`

## Build a nasazení

```powershell
.\Deploy.ps1 -Build
.\Deploy.ps1 -RestartService
```

Kopíruje se jen `EasyIdmCastMA.dll`.

## Vytvoření MA

1. Synchronization Service Manager → **Create** → **Extensible Connectivity 2.0**.
2. Assembly: `EasyIdmCastMA.dll`.
3. **Connectivity**: SQL Server `MIM`, Database `EasyIDM`, Windows authentication.
4. Run profily: **Full Import**, potom **Delta Import**.

## Atributy

`CastId`, `applicationCode`, `applicationModulCode`, `IDMObjectId`, `isValidUser`,
`IsValidRole`, `IdentityTechName`, `IdentityName`, `RoleId`, `RoleTechName`,
`RoleName`, `category`, `IsArchived`, `isValidCast`, `ValidFrom`, `ValidTo`.

## Diagnostika

Log: `%ProgramData%\EasyIdmCastMA\EasyIdmCastMA.log` (nebo cesta z parametru).
SQL kontroly: `Diagnostics.sql`.
