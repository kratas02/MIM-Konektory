# FimServiceDbMA

Read-only ECMA2 konektor pro MIM 2016, který čte objekty a atributy **přímo z databáze MIM Service
(`FIMService`)**. Vznikl jako náhrada standardního FIM Service MA v prostředí, kde se liší build
Synchronization Service a MIM Service/Portálu, a standardní konektor proto odmítne pracovat.

Konektor **pouze čte**. Neobsahuje žádný export ani jiný zápis do databáze.

## Co konektor umí

- Načte kompletní schéma z databáze: všechny object types a jejich atributy podle vazeb
  v `[fim].[BindingInternal]`. Nic není hardcoded, takže vlastní atributy a resource typy
  se objeví automaticky.
- Full import objektů libovolného typu (`Person`, `Group`, `Set`, `SynchronizationRule`, vlastní typy…).
- **Jedna partition na object type.** Import i watermark jdou po partition, takže Full/Delta
  `Person` nebere `Group` ani ostatní typy.
- **Delta import** postavený na commitnutých requestech (`[fim].[Request]`, `[fim].[RequestLog]`) –
  viz [Delta import](#delta-import).
- Všechny datové typy MIM Service: `String`, `Text`, `Xml`, `Integer`, `Boolean`, `DateTime`,
  `Reference`, `Binary`, `Identifier`, včetně multivalue atributů.
- Referenční atributy (`Reference`) se vrací jako DN cílového resource
  (`CN=<ObjectID>,CN=<object type>`), takže je synchronizace umí spárovat s cílovým objektem
  v témže connector space.
- Objekty, které v DB nemají žádnou hodnotu vybraného atributu, se neimportují. V connector space
  by měly jen `ObjectID` a portál je nezná. Už naimportované odstraní následující Full Import
  (synchronizace je vezme jako zastaralé). Když se takový objekt objeví v delta okně, jde do CS jako `Delete`.

## Co konektor neumí (a proč)

| Funkce | Stav | Důvod |
| --- | --- | --- |
| Export | ne | Zápis do MIM Service DB mimo službu není podporovaný a rozbil by workflow, MPR i historii požadavků. Zápis dělejte přes web service nebo portál. |
| Password sync, hierarchie | ne | Pro čtení resource dat nejsou potřeba. |
| Partitions | ano | Jedna partition na object type. Vybrané partition určují, co se importuje. |
| Import jednoho objektu na vyžádání (`FullObject`) | ne | Konektor odmítne s jasnou chybou; použijte Full nebo Delta Import. |

`DateTime` jde do CS jako string v ISO 8601 (`2026-08-31T19:51:34.850`), protože ECMA2 vlastní
datumový typ nemá. Hodnoty jsou v UTC, stejně jako je ukládá MIM Service.

## Požadavky

- MIM 2016 Synchronization Service (konektor cílí na .NET Framework 4.6.2).
- Účet, pod kterým běží synchronizační služba (nebo zadaný SQL login), potřebuje na databázi
  `FIMService` pouze **`db_datareader`**. Žádná write práva nejsou potřeba.
- .NET SDK na buildovacím stroji (`dotnet build`).

## Build a nasazení

```powershell
# jen zkompilovat
.\Deploy.ps1 -Build

# zkompilovat, nakopírovat do Extensions a restartovat službu
.\Deploy.ps1 -RestartService
```

Deploy kopíruje jediný soubor `FimServiceDbMA.dll` – konektor nemá žádné externí závislosti
(`System.Data.SqlClient` je součástí .NET Frameworku).

## Vytvoření MA

1. Synchronization Service Manager → **Management Agents** → **Create**.
2. Management agent for: **Extensible Connectivity 2.0** (ECMA 2.2).
3. Jako assembly vybrat `FimServiceDbMA.dll`.
4. **Connectivity**:
   - `SQL Server` – např. `MIMSQL` nebo `MIMSQL\INST` nebo `mimsql,1433`
   - `Database` – `FIMService`
   - `Windows authentication` – zapnuto (použije se účet synchronizační služby), nebo vypnout
     a zadat `SQL login` + `SQL password`
   - `Connection string options` – volitelně, např. `Encrypt=True;TrustServerCertificate=True`
     nebo `ApplicationIntent=ReadOnly` při čtení z AlwaysOn repliky

   Tlačítko **Next** validuje připojení – ověří existenci `fim` tabulek i právo `SELECT`.
5. **Global**:
   - `Command timeout (s)` – výchozí 300
   - `Locale` – prázdné = neutrální (invariant) hodnoty, což je to, co chce synchronizace.
     Vyplňte jen když potřebujete lokalizované texty (`cs`, `en`, …).
   - `ObjectID format` – `guid` (`3f2b…`) nebo `urn:uuid` (`urn:uuid:3f2b…`). Ovlivňuje
     anchor **i** hodnoty referencí. Pokud budete objekty joinovat na CS jiného MA, zvolte
     formát, který odpovídá druhé straně.
   - `Delta overlap (s)`, `Delta rescan object types` – viz [Delta import](#delta-import)
   - `Partition ID seed` – nechte prázdný. Identifikátory partition se odvozují z názvu object
     type, takže jsou u každého MA stejné a MIM dvě MA se stejnými identifikátory nepřijme
     (vytvoření druhého MA skončí `0x80040E2F` v `partition.cpp`). Vyplňte jen tehdy, když
     má stejnou databázi číst víc MA současně. Změna seedu u existujícího MA rozbije jeho
     connector space.
   - `Log file`, `Verbose log` – viz Diagnostika
6. **Configure Partitions** – jedna partition na object type (`Person`, `Group`, …).
   Zaškrtněte jen typy, které chcete importovat. `Request` a `WorkflowInstance` bývají
   v řádu milionů, ty nechte nezaškrtnuté, pokud je vyloženě nepotřebujete.
   Tlačítko **Refresh** seznam znovu načte z databáze; identifikátor partition je odvozený
   z názvu object type, takže refresh nerozbije už naimportovaný connector space.
7. **Configure Object Types / Attributes** – u vybraných partitions zaškrtněte stejný object type
   a jen atributy, které opravdu potřebujete. Import partition selže, když object type
   se stejným názvem není ve schématu MA vybraný.
8. Run profily: **Full Import** a **Delta Import**, každý krok profilu je navázaný na jednu
   partition. Full Import dané partition musí proběhnout jako první – zakládá její watermark.

## Anchor

Styl DN je `Ldap` a **DN je zároveň anchor** (`IsDNAsAnchor`). DN objektu je
`CN=<ObjectID>,CN=<object type>`, tedy přímo pod DN své partition, kde `ObjectID` je
`[fim].[Objects].[ObjectID]` – GUID resource, stejný identifikátor, jaký ukazuje portál v URL.
Je stabilní po celou existenci objektu, takže se DN nikdy nemění.

Samostatný anchor atribut konektor nedeklaruje ani neposílá. Při stylu `Ldap` se anchor
posílaný mimo DN do obrazu objektu nedostane a staging skončí `0x80230302`
(*The image or delta doesn't have an anchor*). `ObjectID` je ve schématu navíc jako běžný
atribut, aby bylo k dispozici pro join pravidla.

Stejný tvar DN mají hodnoty referencí, aby je synchronizace našla v partition cílového typu.
Typ, na který se odkazuje (`Person` u `Creator`, `DomainConfiguration` u `DomainConfiguration`
a podobně), proto musí mít v MA zaškrtnutý object type **i** partition. MIM si pro cíl
reference, který ještě není v connector space, zakládá zástupný objekt (placeholder) – a pokud
pro něj nemá partition, skončí synchronizace na `0x80230405`
(*CS to MV to CS synchronization failed*).

Konektor se proti tomu brání sám: hodnotu reference na object type, který není v MA vybraný,
zahodí a na konci importu vypíše do logu varování s výčtem chybějících typů a počtem
zahozených hodnot. Objekt tedy projde, jen bez té reference. Když ji potřebujete, zaškrtněte
příslušný object type i jeho partition a udělejte Full Import.

Connector space naplněný starší verzí konektoru je potřeba jednou smazat a znovu udělat
Full Import – staré objekty mají jiné DN, tedy i jiný anchor.

### Pozor na výchozí hodnoty `MACapabilities`

`new MACapabilities()` **není** vynulované. `SupportExport`, `SupportPassword`,
`SupportHierarchy`, `ObjectRename`, `DeleteAddAsReplace` a `ConcurrentOperation` jsou ve
výchozím stavu `true`. Vlastnost, kterou konektor nenastaví, se tím zapne – a MIM pak MA
vůbec nevytvoří (`0x80070057` v `cdext.cpp`, následovaný `0x80040e2f` v `partition.cpp`),
protože čeká rozhraní, které konektor nemá (`IMAExtensible2GetHierarchy`,
`IMAExtensible2Password`). Proto se v `Capabilities` vyplňuje **všechno explicitně**,
včetně exportních vlastností, které konektor nepoužívá.

## Delta import

### Jak funguje

MIM Service stampuje u každého dokončeného požadavku `[fim].[Request].[CommittedTime]`
(`GETUTCDATE()` v okamžiku commitu) a cíl požadavku v `[TargetKey]`. Delta import si drží
watermark (poslední zpracovaný čas) v `CustomData` run stepu. Každá partition má watermark
vlastní. Při každém běhu:

1. vezme okno od `watermark − overlap` do aktuálního času SQL serveru;
2. z `[fim].[Request]` vybere cíle requestů commitnutých v tomto okně;
3. z `[fim].[RequestLog]` přidá objekty, které v okně změnily hodnotu atributu – tím se doberou
   i další cíle multi-target requestů, na které `[TargetKey]` neukazuje;
4. u každého takového objektu **znovu přečte aktuální stav** a pošle ho jako `Replace`;
5. smazané objekty pošle jako `Delete`;
6. watermark posune až po úspěšném dokončení celého běhu.

Delta tedy nepřehrává historii hodnot – používá request log jen jako seznam „co se hýbalo“
a data čte z aktuálního stavu. Každý delta běh proto sám opravuje případné rozdíly z předchozích
běhů a opakované přečtení téhož objektu nic nerozbije (`Replace` je idempotentní).

Před prvním delta importem dané partition musí proběhnout její **Full Import** – ten watermark založí.
Bez něj delta skončí jasnou chybou. Full import `Person` nezaloží watermark pro `Group`.

### Mazání a Synchronization Filter

Po smazání resource zmizí jeho řádek z `[fim].[Objects]` včetně hodnot, takže jeho GUID už se dá
dohledat jedině v `[fim].[RequestLog]`. Tuto tabulku ale MIM Service plní **jen pro object types
uvedené v resource `Synchronization Filter`** (atribut `SynchronizeObjectType`) – ten normálně
nastavuje FIM Service MA, který v tomto prostředí není.

Když se v okně objeví `Delete` requesty, ke kterým `[fim].[RequestLog]` nemá záznam, konektor to
zapíše do logu jako `WARN`. Chování je v takovém případě bezpečné, ale ne úplné: objekt se
z connector space odstraní až při nejbližším **Full Importu**. Kontrola stavu je v `Diagnostics.sql`
(body 6 a 7). Doplnit typy do Synchronization Filteru jde přes portál nebo PowerShell
(`Set-Resource`/`Import-RMConfig`) – je to konfigurace MIM Service, ne zápis do DB konektorem.

### Přepočítané členství (`ComputedMember`)

Když se přepočítá členství Setu nebo Group, MIM Service commituje request nad **členem**, ne nad
skupinou. Změna `ComputedMember` u skupiny se tedy v `[fim].[Request]` neobjeví. Proto má konektor
parametr `Delta rescan object types` (výchozí `Set,Group`): tyto typy se při každém delta importu
načtou celé znovu jako `Replace`. Setů a skupin bývá řádově málo, takže je to levné; kdyby jich
byly desetitisíce, seznam zužte a spoléhejte u nich na Full Import.

### Parametry (stránka Global)

- `Delta overlap (s)` – o kolik sekund se watermark posouvá dozadu, výchozí `120`. Kryje requesty,
  které se commitnou mimo časové pořadí (dlouhá transakce, schvalování). Důsledek: změna se
  v rámci overlap okna může naimportovat víc než jednou, což je bez následků.
- `Delta rescan object types` – viz výše, prázdná hodnota rescan vypne.

### Limity

- Jeden delta běh vezme nejvýš 100 000 změněných objektů. Když se strop vyčerpá, watermark se
  posune jen tam, kam se opravdu došlo, do logu jde `WARN` a zbytek dobere další delta běh.
- `[fim].[Request]` nemá index na `CommittedTime`, takže dotaz tabulku prochází. U velkých
  instalací (miliony requestů) může DBA přidat nepovinný index – jde o změnu schématu
  `FIMService`, kterou Microsoft nedokumentuje, takže je to rozhodnutí zákazníka:

```sql
CREATE NONCLUSTERED INDEX [IX_Request_CommittedTime] ON [fim].[Request] ([CommittedTime])
    INCLUDE ([TargetKey], [TargetObjectTypeKey]);
```

- Delta nezachytí změnu, kterou neudělal request (přímý UPDATE v DB, obnova ze zálohy).
  Na to je Full Import.
- Ruční změna `Delta rescan object types` nebo rozsahu atributů se v CS projeví až po Full Importu.

## Výkon

- Stránkuje se podle rostoucího `ObjectKey`, hodnoty pro celou stránku se čtou jedním dotazem
  přes `UNION ALL` nad tabulkami `[fim].[ObjectValue*]`. Na stránku tedy padnou 2 dotazy
  bez ohledu na počet atributů.
- Čte se v `READ UNCOMMITTED`, aby import nezamykal řádky běžící MIM Service.
- Před prvním importem doporučuji projet `Diagnostics.sql` (bod 2) a podívat se na počty objektů
  po typech. `Request`, `RequestLog` a `WorkflowInstance` bývají v řádu milionů – ty do MA
  nevybírejte, pokud je vyloženě nepotřebujete.
- Velikost stránky bere konektor z run profilu (výchozí 200, max 2000).

## Diagnostika

Log konektoru: `%ProgramData%\FimServiceDbMA\FimServiceDbMA.log`, případně cesta zadaná
v parametru `Log file` (lze zadat soubor i složku). `Verbose log` přidá detaily po stránkách
a po jednotlivých neprevedených hodnotách.

`Diagnostics.sql` obsahuje read-only dotazy, které odpovídají tomu, co dělá konektor:
kontrola práv, počty objektů po typech, náhled schématu a kompletní výpis jednoho resource
podle `ObjectID`.

## Struktura projektu

| Soubor | Obsah |
| --- | --- |
| `FimServiceDbMA/FimServiceDbConnector.cs` | ECMA2 rozhraní, partitions, konfigurace, stránkovaný import, převod hodnot |
| `FimServiceDbMA/FimDbMetadata.cs` | čtení schématu z `fim` internal tabulek, mapování datových typů |
| `FimServiceDbMA/Log.cs` | zápis do logu |
| `Diagnostics.sql` | read-only dotazy pro kontrolu prostředí |
| `Deploy.ps1` | build + kopie do Extensions + restart služby |

## Použité tabulky MIM Service DB

Metadata schématu: `fim.ObjectTypeInternal`, `fim.AttributeInternal`, `fim.BindingInternal`,
`fim.LocaleInternal`. Objekty: `fim.Objects`. Hodnoty: `fim.ObjectValueString`,
`ObjectValueText`, `ObjectValueXml`, `ObjectValueInteger`, `ObjectValueBoolean`,
`ObjectValueDateTime`, `ObjectValueIdentifier`, `ObjectValueReference`, `ObjectValueBinary`.
Delta import: `fim.Request` (`CommittedTime`, `TargetKey`, `TargetObjectTypeKey`, `Operation`)
a `fim.RequestLog` (`EventTime`, `ObjectKey`, `ObjectID`, `AttributeKey`, `Added`).

Jde o interní schéma MIM Service. Microsoft ho nedokumentuje jako veřejné API, takže po
service packu nebo hotfixu je vhodné spustit `Diagnostics.sql` a ověřit, že struktura sedí.
Čtení je ale bezpečné – konektor nikdy nezapisuje.
