# Co se děje po vytvoření na portále

Export synchronizace právě založil objekt Person. Na osobě jsou údaje z Vema: účet, zobrazované jméno, jméno, příjmení, osobní číslo, e-mail, útvar, zařazení, typ `Zaměstnanec`, firma `ÚZSVM`, kód lokality, popis a obě data. Cílová OU, disk, skript, DN v AD a příznak povoleného účtu ještě nejsou.

Portál dál nespouští synchronizaci. Reaguje na vstup do sady. Účet v AD založí až další běh synchronizace, a jen když portál mezitím doplní `uzsvmAD_OU`.

```text
osoba na portále
    → číselník doplní OU, disk a skript
    → synchronizace založí zakázaný účet v OU Zablokovaní
    → portál zapne schránku, složku a skupiny
    → v okně dvou dnů před nástupem se účet povolí
    → synchronizace ho přesune do cílové OU
```

## Číselník

Pravidlo **(MPR100)** spustí **(WF100) Nastaví iniciální hodnoty z číselníku**, když osoba vstoupí do **(SET100) Zaměstnanci s kódem lokality**. Filtr sady je `starts-with(CostCenter, '%')`. V XPath procento není zástupný znak. Sada přijme jen kód lokality, který znakem `%` začíná. Když kódy procentem nezačínají, číselník se nespustí a účet v AD nevznikne.

Číselník `uzsvmPolicy` se hledá podle zobrazovaného názvu. Ten se musí rovnat kódu lokality. Když záznam existuje, portál nastaví:

| Pole osoby | Výpočet |
| --- | --- |
| Company | organizace z číselníku |
| Description | popis z číselníku a typ `Zaměstnanec` |
| uzsvmHomeDirectory | cesta z číselníku, lomítko a jméno účtu |
| uzsvmHomeDrive | písmeno disku |
| uzsvmScriptPath | přihlašovací skript |
| uzsvmAD_OU | cílová OU |

OU se skládá z kódu úřadu v číselníku. Když kód začíná `AA`, první dva znaky se doplní písmenem `B` (`AA` se stane `AAB`). Základ je `,OU=Uživatelé,OU=` plus první písmeno kódu plus `AA,DC=uzsvm,DC=cz`. Před něj se přidá kód úřadu a kontejner účtů. Když je kód úřadu přesně `AAA`, cesta je natvrdo `OU=Běžní,OU=Uživatelé,OU=UZSVM,DC=uzsvm,DC=cz`.

WF100 běží jen při vstupu do sady. Pozdější změna kódu lokality OU, disk ani skript znovu nespočítá.

Další synchronizace tato pole načte do metaverza. `uzsvmAD_OU` má jediný zdroj na portále. Firma na portále se tím během přepíše zpět na konstantu `ÚZSVM`. Popis vydrží, dokud popis nezačne posílat AD.

## Když osoba do číselníku nevstoupí

Časová sada **(SET300) Noví uživatelé bez čísla lokality a oddělení** vezme zaměstnance s budoucím nástupem, jejichž kód lokality nezačíná `%`. Pravidlo **(MPR300)** pošle oznámení. Atributy se nemění a účet se nezaloží.

Když kódy lokality procentem nezačínají, vyhoví této sadě každý nový zaměstnanec s budoucím nástupem, i když kód vyplněný má.

## Zakázaný účet v AD

Tohle už dělá synchronizace, ne portál. Provisioning účet založí, jen když osoba v AD ještě nemá konektor a v metaverzu jsou OU z portálu, kód lokality, útvar, datum nástupu, přihlašovací jméno a typ `Zaměstnanec`. Nástup musí být dnes nebo později. Zaměstnanec, který ve Vema přijde až po nástupu, se v AD sám nevytvoří.

Účet vznikne zakázaný, `userAccountControl` je 514. DN je `CN=` zobrazované jméno v OU z portálu, ve které je slovo Běžní nahrazené slovem Zablokovaní. Zapíší se účet, UPN, zobrazované jméno, útvar, zařazení do `employeeType`, popis ve tvaru kód lokality, pomlčka a zařazení, domovský adresář, disk, skript a umístění schránky `CZ`. Řádek přibude do `c:\Batch\newUsers.txt`.

Další export doplní funkci, firmu, iniciály, jméno, příjmení, osobní číslo, město, ulici a PSČ. Import pak na portál vrátí DN, identifikátor zabezpečení, legacy Exchange DN a příznak, že účet je zakázaný.

## Schránka, složka a skupiny

Tyto kroky čekají, až účet v AD existuje a jeho DN na portále začíná `CN`. Sahají do AD a na disk, atributy osoby z nich nepřepočítávají.

| Kdy | Pravidlo | Co udělá |
| --- | --- | --- |
| Vstup do (SET005) Noví zaměstnanci | (MPR203), (WF203) | Zapne schránku a přidá alias. |
| Stejná sada | (MPR205), (WF205) | Přidá účet do skupin přímo v AD. |
| Vstup do (SET007) Vytvorit homeFolder | (MPR204), (WF204) | Založí domovskou složku a nastaví práva. |

**(SET005)** je časová. Osoba není `ILMSync`, DN začíná `CN`, legacy Exchange DN nezačíná lomítkem, typ je `Zaměstnanec`, nástup je pořád v budoucnu a e-mail vyhovuje `starts-with(Email, '%')`. I tady procento není zástupný znak. Když e-mail procentem nezačíná, schránka ani skupiny se tímto vstupem nespustí.

**(WF203)** se připojí na `http://aexch19-1/PowerShell/` a zavolá `Enable-Mailbox`. Databáze se vybere podle zařazení a první číslice útvaru. Zařazení s textem ředitel, náměstek nebo vedoucí jde do nejmenší databáze `19DB_Vedouci*`. Jinak první číslice útvaru určí databázi: 9 `19DB_UZSVM`, 6 `19DB_BAA`, 8 `19DB_CAA`, 5 `19DB_HAA`, 2 `19DB_SAA`, 3 `19DB_PAA`, 4 `19DB_UAA`, 7 `19DB_OAA`. Když útvar chybí nebo taková databáze není, použije se `19DB_UZSVM`. Primární adresa je e-mail z portálu. K té se přidá alias ve tvaru místní část a `@uzsvm.cz`. Když se schránku nepodaří založit, před `uzsvmAD_DN` se připíše `Err-`.

**(WF205)** najde účet v AD a přidá ho do skupin. Členství se nezapíše na portál.

- vždy `UZSVM_eSSL-Modul-Sekretariat_G`,
- `UZSVM_eSSL-Role-Vedouci_G`, když zařazení obsahuje „editel“, „ved“ nebo „nám“, jinak `UZSVM_eSSL-Role-Referent_G`,
- skupinu útvaru ze souboru `C:\Script\depatments.txt`, sloupec Skupina podle sloupce Department,
- `VPN Wi-Fi Auth Internal Template Users`, `VPN_ALL_G` a `VPN_ALLOW_G`.

Starší výběr role podle lokality (`ReditelUP`, `Namestek` a další) je ve skriptu v komentáři a nespouští se.

**(SET007)** chce cestu domovského adresáře, která začíná `%`, a DN, který začíná `CN`. **(WF204)** založí složku se jménem účtu v nadřazené složce této cesty a účtu `uzsvm\jméno` dá `FullControl` s děděním na podsložky a soubory. Když cesta procentem nezačíná, osoba do sady nevstoupí a složka se nezaloží.

## Povolení a přesun do cílové OU

Časová sada **(SET106) Uživatelé v den nástupu** vezme osobu, jejíž nástup je později než teď a dřív než za dva dny. **(MPR206)** spustí **(WF206)**. Skript nastaví `userAccountControl` na 512 a zruší skrytí v adresáři. Účet se tedy povolí v okně dvou dnů před nástupem. V den nástupu, kdy je datum už v minulosti, tahle sada osobu nepřijme.

Stejný zákaz umí zrušit i synchronizace. Udělá to, když je `UserState` 1 a tento výpočet není starší než stav načtený z AD. `UserState` je 1 od dne nástupu do dne odchodu. V den nástupu proto účet povolí synchronizace, i když ho portál v okně dvou dnů nepovolil.

Když je účet povolený, stav zaměstnance je `zam` a DN pořád leží v OU Zablokovaní, provisioning ho přesune na `CN=` zobrazované jméno v cílové OU z portálu.

Workflow **(WF201)** a **(WF202)**, která umí na portále nastavit `uzsvmUserState`, žádné pravidlo nespouští. Stav pro AD počítá synchronizace z obou dat.
