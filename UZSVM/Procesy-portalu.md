# Procesy na MIM portálu

Kdy portál sám něco spustí a co při tom nastaví. Cesta z Vema do AD je v `Proces-Vema-do-AD.md`.

Portál nespouští synchronizaci. Synchronizace mu dodá osobu a atributy. Portál reaguje dvěma způsoby.

- **Přechod do sady.** Osoba nebo skupina začne vyhovovat filtru. U časové sady se filtr přepočítává sám, typicky jednou denně. Přechod spustí akční workflow.
- **Žádost.** Někdo v portálu přidá, odebere nebo změní atribut. Pravidlo žádost pustí a případně ji pošle ke schválení.

Většina sad používá filtr `starts-with(atribut, '%')`. V XPath to znamená hodnotu, která začíná znakem procento. Názvy sad přitom mluví o vyplněném kódu, e-mailu nebo osobním čísle. Když hodnoty procentem nezačínají, přechod nenastane a navázané workflow se nespustí.

## Nástup zaměstnance

Pořadí řídí data na osobě, ne jedno společné workflow.

| Kdy | Pravidlo | Co se nastaví nebo udělá |
| --- | --- | --- |
| Osoba vstoupí mezi zaměstnance s kódem lokality (SET100) | MPR100, WF100 | Z číselníku `uzsvmPolicy`, jehož název je kód lokality, se doplní firma, popis, domovský adresář (`cesta\účet`), písmeno disku, přihlašovací skript a cílová OU `uzsvmAD_OU`. Běží jen při vstupu do sady. |
| Nástup je v budoucnu, v AD už je účet, typ je Zaměstnanec a sada SET005 osobu přijme | MPR203, WF203 | V Exchange se zapne schránka (`Enable-Mailbox`) a přidá se alias. Při chybě se před `uzsvmAD_DN` připíše `Err-`. |
| Stejná sada nových zaměstnanců | MPR205, WF205 | Účet se přidá do skupin eSSL podle zařazení, do skupiny útvaru z `C:\Script\depatments.txt` a do tří VPN skupin. |
| Je vyplněná cesta domovského adresáře a účet v AD už má DN (SET007) | MPR204, WF204 | Na disku se založí složka se jménem účtu. |
| Nástup je později než teď a dřív než za dva dny (SET106, časová sada) | MPR206, WF206 | Účet se povolí (`userAccountControl` 512) a zruší se skrytí v adresáři. |
| Nový zaměstnanec s budoucím nástupem do sady s kódem lokality nevstoupí (SET300) | MPR300, WF300 | Odešle se oznámení. Atributy se nemění. |

OU, disk a skript z WF100 se dalším během synchronizace načtou do metaverza. Bez `uzsvmAD_OU` se účet v AD nezaloží. Samotné založení účtu dělá synchronizace, ne portál.

## Odchod zaměstnance

Datum odchodu na portál zapisuje synchronizace z Vema.

| Kdy | Pravidlo | Co se nastaví nebo udělá |
| --- | --- | --- |
| Odchod je starší než včera (SET008, časová sada) | MPR007, WF008 | Schránka se zakáže, účet se zakáže a přesune do OU, kde je Běžní nebo Neaktivní nahrazené slovem Zablokovaní. |
| Odchod už proběhl a účet je pořád povolený (SET010 Kontrola, časová sada) | MPR010, WF010 | `ExpirationTime` se nastaví na sedm dní od teď. |
| Vypršela expirace a účet je pořád povolený (SET011, časová sada) | MPR011 Opětovné zakázání, WF008 | Znovu se spustí odchod: zákaz schránky, zákaz účtu a přesun do Zablokovaní. |
| Někdo změní `EmployeeEndDate` | MPR011 Zrušení expirace, WF011 | `ExpirationTime` se smaže. |
| Od odchodu uplynulo 90 dní a DN obsahuje `OU=Zabloko` (časová sada) | MPR205 přesun, WF208 a WF207 SQL | Účet se přesune do `OU=UsersForDel,OU=Uživatelé,OU=Správa,DC=uzsvm,DC=cz` a skryje se v adresáři. SQL workflow je na pravidle uvedené, v exportu ale nemá skript. |

Mazání domovské složky (WF207) a profilu (WF209) má příkazy `Remove-Item` zakomentované. Složka ani profil se nesmažou. WF009 umí účet v AD smazat, žádné zapnuté pravidlo ho nespouští.

## Skupiny

| Kdy | Pravidlo | Co se nastaví nebo udělá |
| --- | --- | --- |
| Skupina vstoupí mezi skupiny bez vlastníka (SET012) | MPR012, WF012 | `Owner` a `DisplayedOwner` se nastaví na pevnou osobu `fb89aefa-5ea1-47f1-8890-abe7797d6497`. |
| Někdo přidá člena do ISMS skupiny (SET001) | MPR001 ISMS Approval | Žádost jde ke schválení workflow WF001. Členství se zapíše až po schválení. |
| Někdo odebere člena statické bezpečnostní skupiny | MPR001 ISMS Removal Approval | Odebrání jde ke schválení druhým workflow WF001. |
| Někdo přidá nebo odebere vlastníka v sadě ISMS Owners | MPR002, WF002 | Běží aktualizace vlastníků ISMS skupin. |
| Skupině se blíží konec platnosti | vestavěné pravidlo Group Expiration Notification | Odešle se upozornění. Atributy skupiny se nemění. |

MPR200, které přepisovalo příznak `uzsvmIsISMSGroup`, je vypnuté.

## Co je na portále připravené a nespouští se

- WF201 a WF202 nastaví `uzsvmUserState` na 1 nebo 0. Žádné pravidlo je nevolá. Stav pro AD počítá synchronizace z data nástupu a odchodu.
- WF009 maže účet v AD a není navázané na pravidlo.
- WF209 maže profil a není navázané na pravidlo. I v něm je smazání souborů zakomentované.
- Pravidlo `$test mazani homefolderu` je zapnuté a míří na testovací sadu a na WF207 SQL, které nemá skript.

## Co portál nenastavuje

Jméno, příjmení, osobní číslo, e-mail, zařazení, kód lokality a obě data přicházejí synchronizací z Vema. Portál je při nástupu použije jako vstup číselníku a sad. Sám je z Vema nepřepočítává. Povolení účtu v den nástupu dělá portál (WF206) a znovu ho umí udělat synchronizace podle `UserState`.
