# Proces z Vema do Active Directory

Popis cesty zaměstnance z personálního systému Vema do AD `uzsvm.cz`. Atributový katalog je v `Popis-atributu-Vema.md`.

Zdroje: export konektorů a metaverza ze SRVAAAMIMDB ze dne 23. 10. 2026, knihovny `UZSVMSyncExtension` a `UZSVMExtension`, schéma a politika portálu.

## 1. Přehled

Vema je zdroj osobních údajů. Synchronizace z nich složí jméno, účet, data a stav a zapíše osobu do portálu. Portál podle kódu lokality dopočítá umístění v AD, domovský adresář a přihlašovací skript. Tyto hodnoty se vrátí do metaverza a provisioning založí zakázaný účet. Povolení, schránka, složka a skupiny jsou další krok, část z nich běží znovu na portále.

Účet se zakládá jen pro nástup dnes nebo v budoucnu. Zaměstnanec, který už nastoupil, se v AD sám nevytvoří. Pokud v AD je, připojí se k osobě podle `employeeID`, `sAMAccountName` nebo `mail`.

```text
Vema OU=UzsvmUser
        |  import, join podle employeeID
        v
   metaverzum (výpočty synchronizace)
        |  export osoby
        v
   portál (číselník lokality, schránka, složka, skupiny)
        |  import AD_OU, disku a skriptu
        v
   provisioning do AD uzsvm.cz
        |  export osobních údajů, později povolení účtu
        v
   AD a zpětný zápis jména, sAMAccountName a UPN do Vema
```

## 2. Atributy Vema

Konektor má vybraných 37 atributů uživatele. Šestnáct se čte do metaverza, tři se do Vema zapisují. Dva z těch tří se zároveň čtou. Dvacet vybraných atributů se nečte ani nezapisuje.

### Čtou se

Hodnota jde z Vema do metaverza. Když má jiný systém přednost, použije se hodnota z Vema až ve chvíli, kdy vyšší zdroj atribut nemá.

| Atribut Vema | Význam | Cíl v metaverzu | Přednost |
| --- | --- | --- | --- |
| employeeID | osobní číslo, párovací klíč | employeeID | Vema |
| givenName | křestní jméno | givenName | Vema |
| sn | příjmení | sn | jen Vema |
| title | funkce | title | jen Vema |
| employeeType | pracovní zařazení | jobTitle | nejdřív AD, pak Vema |
| extensionAttribute1 | kód lokality | costCenter | jen Vema |
| extensionAttribute2 | datum nástupu, text `d.M.yyyy` | employeeStartDate | jen Vema |
| extensionAttribute3 | datum odchodu, stejný tvar | employeeEndDate | jen Vema |
| extensionAttribute4 | datum narození | extensionAttribute4 a počáteční heslo | jen Vema |
| sAMAccountName | přihlašovací jméno | accountName | nejdřív AD, pak Vema, pak portál |
| mail | e-mail | mail, a z něj UPN | nejdřív AD, pak Vema |
| userPrincipalName | hodnota uložená ve Vema | mailNickname | nejdřív AD, pak Vema |
| department | útvar | department | nejdřív AD, pak Vema |
| l | město | l | rovnocenně s AD a portálem |
| streetAddress | ulice | streetAddress | rovnocenně s AD a portálem |
| postalCode | PSČ | postalCode | rovnocenně s AD a portálem |

Z přítomnosti uživatele na konektoru se ještě vloží konstanty, bez čtení stejnojmenného atributu: firma `ÚZSVM`, typ `Zaměstnanec`, stav `zam` a značka `VEMA`. Zobrazované jméno, iniciály, popis a UPN se skládají z přečtených polí.

### Zapisují se

Export do uživatele ve Vema má tři atributy. Mazání hodnoty ve Vema je potlačené.

| Atribut Vema | Odkud se zapíše |
| --- | --- |
| displayName | person.displayName. Nejdřív hodnota z AD, jinak příjmení a jméno. Atribut displayName ve Vema se nečte. |
| sAMAccountName | person.accountName. Po vzniku účtu hodnota, kterou vrátilo AD. |
| userPrincipalName | person.userPrincipalName. Po vzniku účtu hodnota z AD, před ním UPN složené z mailu. |

`sAMAccountName` a `userPrincipalName` se tedy čtou i zapisují. `displayName` se jen zapisuje.

U skupiny je připravený zápis `displayName`, `mailNickname`, `member`, `groupType` a `mail`. Nové skupiny se do Vema nezakládají, zakládání je v kódu vypnuté. Ze skupiny ve Vema se do metaverza nic nečte.

### Vybrané, bez čtení a bez zápisu

Konektor je načte do svého prostoru a dál je nepoužije: `cn`, `company`, `description`, `initials`, `extensionAttribute5`, `extensionAttribute6`, `extensionAttribute7`, `extensionAttribute10`, `homeDirectory`, `homeDrive`, `profilePath`, `scriptPath`, `mailNickname`, `postalAddress`, `name`, `member`, `groupType`, `Tituly`, `Titulyp`, `Titulyz`.

Ve schématu ADAM jsou ještě `Oscis`, `Utvar`, `Utvart`, `Mistot`, `Ppvko` a `Enabled`. Konektor je nemá vybrané, takže je nenačte.

## 3. Vznik uživatele

### 3.1 Načtení z Vema

Konektor čte uživatele z `OU=UzsvmUser,DC=uzsvm,DC=cz`. Objekt `user` se projektuje na osobu. K existující osobě se připojí podle `employeeID`. Stejné párování má pravidlo portálu SR003.

Z Vema se berou údaje uvedené v kapitole 2. Bez kódu lokality, útvaru, data nástupu a přihlašovacího jména se účet v AD nezaloží.

### 3.2 Co dopočítá synchronizace

Tyto hodnoty ve Vema nejsou. Vzniknou v metaverzu při synchronizaci.

- Zobrazované jméno je `příjmení jméno`. Jakmile displayName pošle AD, má AD přednost.
- Iniciály jsou první znak jména a první znak příjmení.
- UPN je část mailu před zavináčem a doména `@uzsvm.cz`. Jakmile UPN pošle AD, má AD přednost.
- Popis je `kód lokality - pracovní zařazení`. Pravidlo SR003. Jakmile popis pošle AD, má AD přednost.
- Firma je konstanta `ÚZSVM`.
- Typ zaměstnance je konstanta `Zaměstnanec`. Text zařazení z Vema je v `jobTitle`.
- Stav zaměstnance je konstanta `zam`. Řídí pozdější přesun z OU Zablokovaní.
- Značka zdroje je konstanta `VEMA` v `extensionAttribute9`. Nikam se neexportuje.
- Počáteční heslo je text `Heslo` a datum narození ve tvaru `ddMMyyyy`. `pwdLastSet` se nastaví na 0.
- `UserState` je 1, když nástup už nastal a odchod ještě nenastal. Chybějící odchod se bere jako vzdálená budoucnost. Chybějící nástup dává 0.

`toCreate`, `placedAD` a `DeleteStatus` se z dat také spočítají, ale založení účtu ani export je nečtou.

### 3.3 Zápis osoby na portál

Export do portálu založí objekt Person. Z metaverza na něj jdou jméno, příjmení, zobrazované jméno, osobní číslo, e-mail, účet, útvar, firma, typ `Zaměstnanec`, pracovní zařazení, kód lokality, popis, adresa, město, PSČ a obě data.

V tuto chvíli portál ještě nemá cílovou OU, disk ani skript. Ty doplní číselník v další kapitole.

### 3.4 Založení účtu v AD

Provisioning v `UZSVMExtension` účet založí, když osoba v AD ještě není a v metaverzu už jsou `AD_OU`, kód lokality, útvar, datum nástupu, `accountName` a typ `Zaměstnanec`, a nástup je dnes nebo později.

`AD_OU` v té chvíli musí přijít z portálu. Bez proběhlého číselníku se účet nezaloží.

Účet vznikne takto:

- DN je `CN=` zobrazované jméno v OU z portálu, ve které je slovo Běžní nahrazené slovem Zablokovaní.
- Účet je zakázaný, `userAccountControl` je 514.
- `sAMAccountName`, `userPrincipalName`, `displayName`, `department` a `employeeType` se zapíší hned.
- Popis účtu je kód lokality, pomlčka a pracovní zařazení.
- Domovský adresář, disk a skript se zapíší, pokud už je portál dopočítal.
- `msExchUsageLocation` je `CZ`.
- Když objekt v AD už existuje, nový konektor se nezaloží.
- Řádek se přidá do `c:\Batch\newUsers.txt`.

Další osobní údaje doplní klasický export do AD: `title`, `company`, `initials`, `givenName`, `sn`, `employeeID`, `department`, `l`, `streetAddress`, `postalCode`. Telefon, mobil a kancelář jdou z AD nebo z portálu, z Vema ne.

Do Vema se zpět zapíší `displayName`, `sAMAccountName` a `userPrincipalName`. Po vzniku účtu jsou to hodnoty, které vrátilo AD.

## 4. Co se dopočítá na portále

Portál osobní údaje z Vema nepřepočítává. Dopočítává umístění a prostředky podle číselníku `uzsvmPolicy` a pak v AD zakládá schránku, složku a členství.

Číselník se hledá podle zobrazovaného názvu. Ten se musí rovnat kódu lokality, tedy `extensionAttribute1` z Vema. Workflow **(WF100) Nastaví iniciální hodnoty z číselníku** spouští přechod do sady **(SET100) Zaměstnanci s kódem lokality**. Běží při vstupu do sady, ne při každé pozdější změně kódu.

Když záznam číselníku existuje, portál nastaví:

| Pole osoby | Výpočet |
| --- | --- |
| Company | `uzsvmPolicyOrganization` |
| Description | `uzsvmPolicyDescription` plus `EmployeeType`, pokud typ je vyplněný. Na portále je v tu chvíli konstanta `Zaměstnanec`, ne text zařazení. |
| uzsvmHomeDirectory | `uzsvmPolicyHomeDirectory`, lomítko a `AccountName` |
| uzsvmHomeDrive | `uzsvmPolicyHomeDrive` |
| uzsvmScriptPath | `uzsvmPolicyLogonScript` |
| uzsvmAD_OU | složená cesta níže |

Cesta OU se skládá z kódu úřadu v číselníku (`uzsvmPolicyName`):

1. Když kód začíná `AA`, první dva znaky se doplní na konec písmenem `B`. Z `AA` se stane `AAB`.
2. Základ je `,OU=Uživatelé,OU=` plus první písmeno kódu plus `AA,DC=uzsvm,DC=cz`.
3. Před základ se přidá `,OU=` a upravený kód úřadu.
4. Před to se přidá `OU=` a hodnota `uzsvmPolicyAccountContainer`.
5. Když je kód úřadu přesně `AAA`, cesta se nepočítá a je natvrdo `OU=Běžní,OU=Uživatelé,OU=UZSVM,DC=uzsvm,DC=cz`.

Další synchronizace tato pole načte zpět. `AD_OU` má na portále jediný zdroj. Disk, písmeno disku a skript mají přednost portálu před hodnotou, která už je v AD.

Firma na portále se dalším během zase přepíše konstantou `ÚZSVM`, protože portálové Company není zdroj metaverza. Popis na portále vydrží jen do chvíle, než popis začne posílat AD.

### Schránka, složka a skupiny

Tyto workflow už nic nepočítají do atributů osoby. Sahají do AD a na disk.

**(WF203) vytvořit mailbox.** Sada **(SET005) Noví zaměstnanci**: osoba není `ILMSync`, `uzsvmAD_DN` začíná `CN`, `uzsvmLegacyExchangeDN` nezačíná lomítkem, typ je `Zaměstnanec`, nástup je v budoucnu a e-mail ve filtru vyhovuje `starts-with(Email, '%')`. Skript zavolá `Enable-Mailbox` pro účet a databázi a přidá alias. Když se to nepovede, před `uzsvmAD_DN` se připíše `Err-`.

**(WF204) vytvořit home folder.** Sada **(SET007)**: je vyplněná cesta domovského adresáře a účet v AD už má DN. Skript založí adresář jména uživatele ve složce z této cesty.

**(WF205) Iniciální přidání uživatele do skupin.** Stejná sada nových zaměstnanců. Skript vezme útvar, kód lokality, účet a zařazení a uživatele přidá do skupin:

- `UZSVM_eSSL-Modul-Sekretariat_G`,
- `UZSVM_eSSL-Role-Vedouci_G`, když zařazení obsahuje „editel“, „ved“ nebo „nám“, jinak `UZSVM_eSSL-Role-Referent_G`,
- skupinu útvaru z `C:\Script\depatments.txt` podle sloupce Department,
- `VPN Wi-Fi Auth Internal Template Users`, `VPN_ALL_G` a `VPN_ALLOW_G`.

**(WF206) povolí uživatele v den nástupu.** Sada **(SET106)** je časová. Nástup je později než teď a dřív než za dva dny. Skript nastaví `userAccountControl` na 512 a zruší skrytí v adresáři. Povolí tedy účet v okně dvou dnů před nástupem, ne až v okamžiku, kdy je nástup v minulosti.

**(WF300)** pošle oznámení, když nový zaměstnanec s budoucím nástupem do sady s kódem lokality nevstoupí.

Filtry sad **(SET100)** a **(SET005)** obsahují `starts-with(CostCenter, '%')` a `starts-with(Email, '%')`. V XPath procento není zástupný znak. Sada podle toho chytí jen hodnoty, které procentem začínají. Názvy sad přitom mluví o vyplněném kódu a o nových zaměstnancích. Když kódy a e-maily procentem nezačínají, číselník, schránka a skupiny se při vstupu do sady nespustí.

Workflow **(WF201)** a **(WF202)** umí nastavit portálové `uzsvmUserState` na 1 nebo 0. Žádné pravidlo je nespouští. Stav účtu pro AD počítá synchronizace v metaverzu, ne tato dvě workflow.

## 5. Aktualizace

Při další změně ve Vema se znovu importuje uživatel a synchronizace přepíše jen atributy, u kterých má Vema přednost.

Vema zůstává zdrojem pro osobní číslo, jméno, příjmení, funkci v `title`, kód lokality, datum nástupu a datum odchodu. Z dat se znovu spočítá `UserState`.

Jakmile účet v AD existuje, AD má přednost u zobrazovaného jména, útvaru, e-mailu, přihlašovacího jména, UPN, popisu, `mailNickname` a pracovního zařazení. Změna stejného údaje ve Vema se do metaverza nedostane, dokud ji AD posílá.

Město, ulice a PSČ jsou rovnocenné. Platí poslední změna z Vema, AD nebo portálu.

Export do AD pak aktualizuje `title`, firmu, iniciály, zobrazované jméno, jméno, příjmení, osobní číslo, útvar, město, ulici a PSČ. Mazání je u většiny z nich potlačené: prázdná hodnota ve Vema pole v AD nesmaže. U města a útvaru mazání potlačené není.

Když je `UserState` 1 a tento údaj není starší než stav načtený z AD, export účet povolí a zruší skrytí v adresáři. Ruční zákaz v AD tím vydrží, dokud Vema stav nepřepočítá později než AD.

Když je účet povolený, stav je `zam` a DN pořád obsahuje `ou=zablokovan`, provisioning ho přesune na `CN=` zobrazované jméno v cílové `AD_OU` z portálu.

Změna kódu lokality sama číselník znovu nespustí. WF100 běží jen při vstupu do sady. Nová OU, disk a skript se dopočítají, jen když osoba ze sady odejde a znovu do ní vstoupí.

Do Vema se při aktualizaci dál vracejí jen zobrazované jméno, `sAMAccountName` a UPN.

## 6. Odchod

Datum odchodu přijde z Vema `extensionAttribute3`. Od dne odchodu je `UserState` 0, takže synchronizace účet kvůli Vema znovu nepovolí.

Na portále pak běží samostatné akce. Nesahají na atributy Vema.

- **(WF008) Odchod zamestnance.** Sada osob, jejichž odchod je starší než včera. Skript zakáže schránku (`Disable-Mailbox`), nastaví bit zakázaného účtu a přesune uživatele do OU, ve které je Běžní nebo Neaktivní nahrazené slovem Zablokovaní.
- **(WF010)** osobě s prošlým odchodem a pořád povoleným účtem nastaví `ExpirationTime` na sedm dní.
- **(MPR011)** po uplynutí této expirace, když je účet znovu povolený, spustí znovu odchodové workflow.
- **(WF208)** po 90 dnech od odchodu, když DN obsahuje `OU=Zabloko`, přesune účet do `OU=UsersForDel,OU=Uživatelé,OU=Správa,DC=uzsvm,DC=cz` a skryje ho v adresáři.
- Mazání domovské složky a profilu je ve **(WF207)** a **(WF209)** připravené. Příkazy `Remove-Item` jsou v obou skriptech zakomentované, složka ani profil se tedy nesmažou.

Sady odchodu se vyhodnocují z data na portále. To datum tam exportuje synchronizace z Vema.
