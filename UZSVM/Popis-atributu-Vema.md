# Atributy konektoru Vema (ADAM)

Popis atributů personálního systému, ve kterém uživatelé vznikají, a kam se synchronizují.

Export konektorů a metaverza je ze serveru SRVAAAMIMDB ze dne 23. 10. 2026. Výpočty jsou ze zdrojových knihoven `UZSVMSyncExtension` a `UZSVMExtension`. Názvy polí v portálu jsou ze schématu a politiky MIM.

## Odkud se uživatelé berou

Konektor se jmenuje Vema a je typu ADAM. Připojuje se na `localhost`, port `20000`, účtem `uzsvm.cz\40IlmService`.

Čte se jen oddíl `DC=uzsvm,DC=cz`. Uživatelé jsou v `OU=UzsvmUser,DC=uzsvm,DC=cz`, skupiny v `OU=UzsvmGroup,DC=uzsvm,DC=cz`.

Objekt `user` se v metaverzu stane osobou (`person`). K existující osobě se připojí podle `employeeID`. Stejné párování má synchronizační pravidlo portálu **(SR003) Vema Import Rule**. To pravidlo samo přidává jen složení atributu `description`. Ostatní toky z Vema jsou klasické toky konektoru.

Konektor má vybraných 37 atributů. Z nich se 16 čte do metaverza, 3 se do Vema zapisují zpět a 20 vybraných atributů synchronizace nepoužívá.

## Jak se rozhoduje, která hodnota platí

U řazené priority platí první zdroj. Hodnota z dalšího zdroje se použije, teprve když vyšší zdroj atribut nemá. U rovnocenné priority rozhoduje poslední změna. Recall je zapnutý u Vema, u AD `uzsvm.cz` i u portálu: když vyšší zdroj o hodnotu přijde, může nastoupit nižší.

Pořadí zdrojů je u každého atributu uvedeno explicitně.

## Jak vzniká účet v AD

Nový účet v AD `uzsvm.cz` se založí, jen když zároveň platí toto:

- osoba v AD ještě nemá konektor,
- v metaverzu je `AD_OU` z portálu (`uzsvmAD_OU`), kód lokality, útvar, datum nástupu, `accountName` a typ `Zaměstnanec`,
- datum nástupu je dnes nebo později.

Účet se založí v OU, ve které je v cestě z portálu slovo Běžní nahrazené slovem Zablokovaní. Účet je zakázaný (`userAccountControl` 514). Když je podle dat z Vema `UserState` rovno 1 a tento údaj není starší než stav načtený z AD, export účet povolí a zruší skrytí v adresáři Exchange. Povolený účet se stavem `zam` se pak přesune do cílové `AD_OU`.

Domovský adresář, disk, přihlašovací skript a profil Vema nedodává. Berou se z portálu, a když tam nejsou, z AD.

## Atributy, které se z Vema čtou

### employeeID

Osobní číslo a jediný párovací klíč uživatele Vema na existující osobu.

V metaverzu plní `person.employeeID` s prioritou 1. Pokračuje do AD `employeeID` a do portálu Employee ID. Mazání hodnoty v cíli je potlačené.

### givenName

Křestní jméno.

V metaverzu plní `person.givenName` s prioritou 1. Pokračuje do AD `givenName` a do portálu First Name. Spolu s příjmením skládá zobrazované jméno a iniciály.

### sn

Příjmení. Do metaverza teče jen z Vema.

Plní `person.sn`. Pokračuje do AD `sn` a do portálu Last Name.

### title

Funkce nebo titul uložený v atributu `title`. Nejde o atributy `Tituly`, `Titulyp` a `Titulyz`.

V metaverzu je jediným zdrojem `person.title`. Pokračuje jen do AD `title`. Do portálu Job Title se tento atribut neposílá.

### employeeType

Text pracovního zařazení z personálního systému. Do `person.employeeType` se nekopíruje. Tam se vkládá konstanta `Zaměstnanec`, popsaná níže.

Text zařazení plní `person.jobTitle` jako druhý zdroj. Přednost má AD `employeeType`. Když AD zařazení nemá, jde hodnota do portálu Job Title. Při založení účtu se zapíše do AD `employeeType` a za pomlčku do popisu účtu.

### extensionAttribute1

Kód lokality. V portálu odpovídá nákladovému středisku a poli Kód lokality.

Je jediným zdrojem `person.costCenter`. V portálu plní Cost Center a `uzsvmKodLokality`. Při založení účtu je začátkem description. Bez něj se účet v AD nezaloží.

### extensionAttribute2

Datum nástupu. Text ve tvaru `d.M.yyyy`, například `5.3.2026`.

V metaverzu je z něj `person.employeeStartDate` ve tvaru `yyyy-MM-ddT00:00:00.000`. V portálu je to Employee Start Date. Účet v AD se zakládá jen při nástupu dnes nebo později. Datum je také vstupem pro `UserState` a pro příznaky `toCreate`, `placedAD` a `DeleteStatus`.

### extensionAttribute3

Datum odchodu. Stejný textový tvar jako nástup.

V metaverzu je z něj `person.employeeEndDate`. V portálu je to Employee End Date. Spolu s nástupem určuje `UserState`.

V knihovně je i podmínka, která by uživatele s odchodem včera a dříve do metaverza neprojektovala. Projekce konektoru je ale declared, takže se tato podmínka nespouští.

### extensionAttribute4

Datum narození. Stejný textový tvar `d.M.yyyy`.

V metaverzu zůstává beze změny v `person.extensionAttribute4`. Zároveň se z něj skládá `person.unicodePwd`: počáteční heslo je text `Heslo` a datum ve tvaru `ddMMyyyy`. Heslo se zapíše jen při založení účtu a `pwdLastSet` se nastaví na 0, takže uživatel ho musí změnit. Klasický export heslo dál neudržuje. Datum narození se z metaverza nikam neposílá.

### sAMAccountName

Přihlašovací jméno připravené ve Vema.

Plní `person.accountName` jako druhý zdroj. Přednost má AD `sAMAccountName`, třetí zdroj je portál Account Name. Odtud jde hodnota do portálu Account Name, zpět do Vema `sAMAccountName` a při založení do AD `sAMAccountName`. Bez něj se účet nezaloží.

### mail

E-mail z personálního systému.

Plní `person.mail` jako druhý zdroj. Přednost má AD `mail`. V portálu je to E-mail. Není-li v AD `userPrincipalName`, složí se UPN z části před zavináčem a domény `@uzsvm.cz`.

### userPrincipalName

Hodnota uložená ve Vema. Není to výsledný UPN účtu v AD.

Jako druhý zdroj plní `person.mailNickname`. Přednost má AD `mailNickname`. Do AD `mailNickname` se tedy dostane jen tehdy, když ho AD samo ještě nemá.

Zpětný zápis do Vema `userPrincipalName` bere `person.userPrincipalName`. To je hodnota z AD, a dokud AD UPN nemá, UPN složené z `mail`.

Skript, který by k hodnotě Vema `userPrincipalName` jen připojil `@uzsvm.cz`, v konfiguraci navázaný není.

### department

Útvar.

Plní `person.department` jako druhý zdroj. Přednost má AD `department`. Pokračuje do AD `department` a do portálu Department. Při založení účtu se zapíše. Bez něj se účet nezaloží.

### l

Město.

Plní `person.l` rovnocenně s AD `l` a s portálem City. Pokračuje do AD `l` a do portálu City. Mazání hodnoty v AD není potlačené.

Skript, který zahazuje hodnoty začínající `202`, na tento tok navázaný není. Město se kopíruje přímo.

### streetAddress

Ulice.

Plní `person.streetAddress` rovnocenně s portálem Address a s AD `streetAddress`. Pokračuje do AD `streetAddress` a do portálu Address.

### postalCode

PSČ.

Plní `person.postalCode` rovnocenně s portálem Postal Code a s AD `postalCode`. Pokračuje do obou cílů.

## Hodnoty, které vzniknou proto, že objekt přišel z Vema

Tyto atributy metaverza nekopírují stejnojmennou hodnotu z Vema. Přítomnost uživatele na konektoru do nich vloží konstantu nebo složený text.

### company

Atribut `company` ve Vema se nečte. Do `person.company` se vloží konstanta `ÚZSVM` s prioritou 1, tedy před hodnotou z AD. Pokračuje do AD `company` a do portálu Company.

### employeeType v metaverzu

Do `person.employeeType` se vloží konstanta `Zaměstnanec`. Text zařazení z Vema jde do `jobTitle`, ne sem. Konstanta pokračuje do portálu Employee Type a je podmínkou založení účtu v AD.

### employeeStatus

Konstanta `zam`. Nikam se neexportuje. Provisioning podle ní pouští povolený účet z OU Zablokovaní do cílové `AD_OU`. Portálové pole `uzsvmEmployeeStatus` synchronizace neplní.

### extensionAttribute9

Konstanta `VEMA`. Značka, že osoba přišla z tohoto konektoru. Nikam se neexportuje.

### displayName

Atribut `displayName` ve Vema se nečte. `person.displayName` bere nejdřív AD `displayName`. Když AD hodnotu nemá, složí se příjmení, mezera a jméno.

Pokračuje do portálu Display Name, do AD `displayName`, do CN nového účtu a zpět do Vema `displayName`.

### initials

Atribut `initials` ve Vema se nečte. `person.initials` je první znak `givenName` a první znak `sn`. Jediný zdroj je tento výpočet z Vema. Pokračuje do AD `initials`.

### description

Atribut `description` ve Vema se nečte. Pravidlo SR003 složí `extensionAttribute1`, mezeru, pomlčku, mezeru a `employeeType`. Příklad: `1234 - referent`.

V metaverzu je tento text až třetí zdroj. Přednost má AD `description`, pak portál Description. Při založení účtu se do AD zapíše stejný text přímo z kódu lokality a z `jobTitle`, ne z atributu `description` v metaverzu.

### userPrincipalName v metaverzu

První zdroj je AD `userPrincipalName`. Druhý zdroj je skript z Vema `mail`: část před zavináčem a doména `@uzsvm.cz`. Při založení se zapíše do AD. Zpět do Vema se zapisuje tato výsledná hodnota.

### UserState

Číslo vypočtené z data nástupu a odchodu.

- `1`, když nástup už nastal a odchod ještě nenastal,
- chybějící odchod se bere jako datum za tisíc let, takže nástup bez odchodu dává stav 1,
- chybějící nástup dává stav 0.

Vema je první zdroj. Druhý zdroj je konstanta `10` z AD a uplatní se jen tehdy, když Vema `UserState` nepřispěje.

Když je stav 1 a příspěvek z Vema není starší než příspěvek `adEnabled` z AD, export povolí účet: smaže bit zakázaného účtu a nastaví běžný účet. Stejné porovnání času u `msExchHideFromAddressLists` skrytí v adresáři zruší, pokud bylo zapnuté.

Do portálu `uzsvmUserState` se neexportuje.

### toCreate

Pravda jen tehdy, když je nástup dnes nebo později a odchod chybí, nebo odchod už včera a dříve proběhl. Nikdo tento příznak nečte. Založení účtu rozhoduje přímo `employeeStartDate`.

### placedAD

Pravda jen tehdy, když nástup byl před dneškem a odchod chybí, nebo odchod už včera a dříve proběhl. Budoucí odchod příznak shodí. Nikdo ho nečte. Portálové pole Vytvořit v AD (`uzsvmPlacedAD`) synchronizace neplní.

### DeleteStatus

Hodnota `3`, když od data odchodu uplynuly aspoň tři měsíce, jinak `0`. Datum nástupu se načte a nepoužije. Nikdo příznak nečte.

## Zápis z metaverza zpět do Vema

Na uživateli se exportují jen tři atributy. Mazání hodnoty ve Vema je potlačené, prázdné metaverzum tedy Vema nevymaže.

| Atribut Vema | Odkud se zapíše |
| --- | --- |
| displayName | `person.displayName`. Přednost má hodnota z AD, jinak složené příjmení a jméno. |
| sAMAccountName | `person.accountName`. Po vzniku účtu obvykle hodnota, kterou vrátilo AD. |
| userPrincipalName | `person.userPrincipalName`. Po vzniku účtu hodnota z AD, před ním UPN složené z mailu. |

Skupiny mají připravený export `displayName`, `mailNickname`, `member`, `groupType` a `mail`. Zakládání skupin do Vema je v `UZSVMExtension` zakomentované, takže se nové skupiny nezakládají. `groupType` skupiny v metaverzu navíc nemá zdroj, export tohoto atributu by neměl co zapsat.

## Vybrané atributy bez toku

Konektor je importuje do prostoru konektoru. Na uživatele z nich žádné příchozí ani odchozí pravidlo nečte.

| Atribut | Poznámka |
| --- | --- |
| cn | Běžné jméno objektu. Identita v MIM jde přes `employeeID`. |
| company | Text ve Vema se ignoruje. Do AD a portálu jde konstanta `ÚZSVM`. |
| description | Text ve Vema se ignoruje. Popis se skládá z kódu lokality a zařazení a většinou ho přepíše AD. |
| initials | Text ve Vema se nečte. Iniciály se počítají z jména a příjmení a jdou jen do AD. |
| extensionAttribute5 | Bez mapování a bez zmínky v kódu. |
| extensionAttribute6 | Bez mapování a bez zmínky v kódu. |
| extensionAttribute7 | Bez mapování a bez zmínky v kódu. |
| extensionAttribute10 | Bez mapování a bez zmínky v kódu. Stejnojmenný atribut v metaverzu plní AD. |
| homeDirectory | Domovský adresář bere portál `uzsvmHomeDirectory`, pak AD. Provisioning ho do nového účtu zapíše, z Vema ne. |
| homeDrive | Stejná cesta jako adresář. Zdroj je portál `uzsvmHomeDrive`. |
| profilePath | Zdroj je portál `uzsvmProfilePath`, pak AD. Zápis při založení účtu je v kódu zakomentovaný. |
| scriptPath | Zdroj je portál `uzsvmScriptPath`, pak AD. Při založení účtu se do AD zapíše. |
| mailNickname | Atribut ve Vema se nečte. Do `mailNickname` v metaverzu jde Vema `userPrincipalName`, pokud `mailNickname` nemá AD. |
| postalAddress | Víceřádková adresa se nepoužívá. Ulice jde přes `streetAddress`. |
| name | RDN objektu. Pro skupinu je v kódu náhradní `displayName` z `name`, tok na něj není navázaný. |
| member | Na uživateli bez významu. Členství skupin v metaverzu bere portál. |
| groupType | Na uživateli bez toku. Export `groupType` na skupinu ve Vema nemá v metaverzu zdroj. |
| Tituly | Vybraný vlastní atribut ADAM, bez toku. |
| Titulyp | Podle názvu tituly před jménem. Bez toku a bez zmínky v kódu. |
| Titulyz | Podle názvu tituly za jménem. Bez toku a bez zmínky v kódu. |

`displayName` ve Vema se nečte, ale stejný atribut se do Vema zapisuje. Proto v této tabulce není.

## Atributy ve schématu ADAM, které konektor nečte

Jsou na třídě uživatele, ale nejsou ve výběru atributů konektoru. Do prostoru konektoru se nedostanou.

| Atribut | Co z názvu plyne |
| --- | --- |
| Oscis | Osobní číslo. Párování používá `employeeID`. |
| Utvar | Útvar. Používá se `department`. |
| Utvart | Druhý atribut téhož významu. |
| Mistot | Místo. Město se čte z atributu `l`. |
| Ppvko | Vlastní kód bez použití v synchronizaci. |
| Enabled | Příznak. Stav účtu v AD se odvozuje z dat nástupu a odchodu. |

## Co kód obsahuje a konfigurace nespouští

Projekce uživatele je declared. Metoda `ShouldProjectToMV` podle data odchodu proto neběží. Odmítla by uživatele s odchodem včera a dříve a také s nečitelným datem.

Ve větvi `UserState` v `UZSVM\UZSVMSyncExtension\UZSVMSyncExtension.cs` je přiřazení do `DeleteStatus`, které v této třídě není deklarované. Soubor v této podobě nejde přeložit. Nasazená knihovna může být starší než dodaný zdroj. Kopie pravidel ve složce `UZSVMExtension` je prázdná kostra a nic nepočítá.

Oba konektory, Vema i AD `uzsvm.cz`, používají knihovnu `UZSVMSyncExtension.dll`. Provisioning metaverza je v `UZSVMExtension.dll`.
