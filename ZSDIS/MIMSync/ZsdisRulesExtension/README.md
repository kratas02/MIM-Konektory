# ZSDIS: synchronizace bez portálu

Knihovna nahrazuje deklarativní synchronization rules a ta portálová workflow, která dopočítávají atributy nebo zakládají objekt v cílovém systému. Seznam toků po konektorech, včetně joinu, filtru a výrazu, je v [Toky-bez-portalu.md](../Toky-bez-portalu.md).

## Nasazení

1. Sestavte `ZsdisRulesExtension.csproj`. Výstup je `ZsdisRulesExtension.dll`.
2. Zkopírujte DLL do složky Extensions synchronizační služby.
3. Vedle DLL položte `kid-seed.txt` s dalším volným číslem KID (dnešní seed je 201001, do souboru patří aktuální poslední použitá hodnota + 1) a `initial-password.txt` se stejným počátečním heslem, jaké dnes nastavuje pravidlo `Users eon-sk in-out`. Heslo do projektu nepatří. Alternativa hesla je proměnná `ZSDIS_INITIAL_PASSWORD`.
4. V Metaverse Designeru zapněte rules extension, vyberte tuto DLL a třídu `MVExtensionObject`. Provisioning přepněte na scripted až ve chvíli, kdy se vypnou synchronization rules. Souběh založí objekt dvakrát.
5. Na každém konektoru níže přepněte projection na rules extension a vyberte uvedenou třídu. Join a přímé attribute flow nastavte v designeru. Advanced flow musí mít název přesně podle tabulky, ten název je `FlowRuleName` v kódu.
6. Konektor MIMMA odpojte z běhových profilů jako poslední, až preview synchronizace sedí. Odpojení MIMMA je dnes jediný důvod, proč se osoba v metaverse smaže. Po přepnutí se osoba při ztrátě zdroje nemaže.

Deprovisioning na konektoru nechte tak, jak je teď. Knihovna při výpadku z filtru existující konektor neruší, protože dnešní sync rules mají odpojení objektu vypnuté.

## Třída extension na konektoru

| Konektor | Třída | Projekce |
| --- | --- | --- |
| HUMANETv2 | `MaHumanet` | ano, `usrID` je `eon-slovakia.sk` nebo `ad.vse.sk` |
| Externisti | `MaExternisti` | ano, když je `personal_number` |
| eon-sk | `MaEonSk` | uživatel v `DC=eon-slovakia,DC=sk`, skupina bez `adminDescription` na `TYP` |
| ad.vse.sk | `MaAdVse` | uživatel s `userAccountControl` jiným než 514, skupina v `OU=Autorizacia,OU=GPOGroups` |
| VSE_LDAP_prod | `MaVseLdap` | ne |
| SAPCUA | `MaSapCua` | ne |
| ZSEEUser | `MaZsee` | ne |
| KOC | `MaKoc` | ne |
| HUMANET_OUT | žádná, stačí přímé toky | ne |
| Externisti_SAP_OUT | žádná, stačí přímé toky | ne |

## Advanced flow, název se píše do designeru

HUMANETv2 import: `displayName`, `eonBusinessUnitID`, `department`, `eonStatus`.

Externisti import: `externisti`, `displayName`, `begda`, `endda`, `employeeStartDate`, `employeeEndDate`. Export: `formatted_contract_to`, `extern_contract_to`.

eon-sk import: `group.displayName`, `group.scope`, `group.type`, `enabled`, `email`.

ad.vse.sk import: `group.displayName`, `group.scope`, `group.accountName`. Join: `employeeNumber-when-enabled` (na `employeeID`, jen když účet není 514).

VSE_LDAP_prod export: `cn`, `title`, `userAccountControl`.

SAPCUA import: `IsSAPCUA`. Export: `COMPANY`, `NAME1`, `STAT2`, `CLASS`, `CLASS-from-stat2` (jen větev VSE), `SMTP_ADDR`, `GLTGB`, `USRID`.

KOC export: `Type`.

ZSEEUser export: `Nastup`, `Vystup`.

Ostatní páry atributů jsou přímé nebo konstanta. Berou se ze současných synchronization rules beze změny směru.

## Co knihovna vědomě nepřebírá

Kopie identity `copy-` je druhý objekt. Jeden objekt metaverse nemůže mít v eon-sk ani ve VSE LDAP dva konektory. Sekundární účet vznikal z příznaku `CreateSecondaryKID` na portálu a dohodář (`persk` na `D`) čeká na `CreateADAccount`. Ani jeden příznak v metaverse není. Notifikace, úpravy Service Desku a schvalování členství jsou funkce portálu, synchronizace je nemá kam zapsat.

U externisty po konci smlouvy se zapisuje `stat2 = 0` a `eonStatus = I`. V portálu běží dvě stejně pojmenovaná MPR a druhá ho naopak nechává aktivního. Tady zůstává neaktivní stav.

`IsSAPCUA` se nově nastaví členstvím skupiny `PassSyncCUA`. Ruční sada `_ZSE_SAPCUA_Users` s portálem skončí.
