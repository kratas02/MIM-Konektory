# Toky atributů do AD

Mezi Vema a AD ani mezi portálem a AD není přímý tok. Hodnota jde do metaverza a z metaverza do AD. Průběžný export do AD má jen atributy v první tabulce každé kapitoly. Účet, UPN, zobrazované jméno, popis, zařazení, heslo, domovský adresář, disk a skript se do AD zapíší jen při založení účtu v provisioningu.

Když má atribut víc zdrojů, u řazeného toku vyhraje první zdroj, který hodnotu pošle. U rovnocenného toku vyhraje poslední změna.

## Vema → AD

| Atribut Vema | Metaverzum | Atribut AD | Kdy se zapíše |
| --- | --- | --- | --- |
| givenName | givenName | givenName | průběžně |
| sn | sn | sn | průběžně |
| title | title | title | průběžně |
| givenName a sn, první písmena | initials | initials | průběžně |
| employeeID | employeeID | employeeID | průběžně |
| konstanta ÚZSVM | company | company | průběžně |
| l | l | l | průběžně, poslední změna z Vema, AD nebo portálu |
| streetAddress | streetAddress | streetAddress | průběžně, poslední změna |
| postalCode | postalCode | postalCode | průběžně, poslední změna |
| department | department | department | průběžně, jen dokud útvar neposílá samotné AD |
| extensionAttribute2 a extensionAttribute3 | UserState | userAccountControl | průběžně. UserState 1 povolí účet, 0 ho kvůli Vema znovu nepovolí |
| extensionAttribute2 a extensionAttribute3 | UserState | msExchHideFromAddressLists | průběžně, skrytí se zruší, když je UserState 1 |
| sAMAccountName | accountName | sAMAccountName | jen při založení. Pak má přednost AD |
| mail, část před zavináčem a @uzsvm.cz | userPrincipalName | userPrincipalName | jen při založení. Pak má přednost AD |
| givenName a sn, tvar příjmení a jméno | displayName | displayName | jen při založení. Pak má přednost AD |
| employeeType | jobTitle | employeeType | jen při založení |
| extensionAttribute1 a employeeType | costCenter a jobTitle | description | jen při založení, text je kód lokality, pomlčka a zařazení |
| extensionAttribute4, datum narození | unicodePwd, text Heslo a datum ddMMyyyy | unicodePwd | jen při založení, pwdLastSet je 0 |

Prázdná hodnota z Vema jméno, příjmení, funkci, iniciály, firmu, zobrazované jméno, osobní číslo, ulici a PSČ v AD nesmaže. Město a útvar smazat může.

### Z Vema se načte a do AD se nezapíše

| Atribut Vema | Kam jde | Proč ne do AD |
| --- | --- | --- |
| mail | mail v metaverzu, pak e-mail na portále | po vzniku účtu má přednost mail z AD a export mailu do AD není |
| userPrincipalName | mailNickname | export do AD není |
| extensionAttribute1 | costCenter a kód lokality na portále | do atributu AD se nezapisuje, při založení je jen začátkem popisu |
| extensionAttribute2 | employeeStartDate na portále | v AD není, řídí jen UserState a podmínku založení |
| extensionAttribute3 | employeeEndDate na portále | v AD není, řídí jen UserState |
| extensionAttribute4 | i přímo extensionAttribute4 v metaverzu | do AD jde jen spočítané heslo při založení |
| konstanta Zaměstnanec | employeeType, na portále Employee Type | do AD employeeType se při založení zapisuje text zařazení, ne tato konstanta |
| konstanta zam | employeeStatus | v AD není, používá ji přesun z OU Zablokovaní |
| konstanta VEMA | extensionAttribute9 | nikam dál nepokračuje |

## Portál → AD

| Atribut portálu | Metaverzum | Atribut AD | Kdy se zapíše |
| --- | --- | --- | --- |
| Address | streetAddress | streetAddress | průběžně, poslední změna |
| City | l | l | průběžně, poslední změna |
| PostalCode | postalCode | postalCode | průběžně, poslední změna |
| Office Location | physicalDeliveryOfficeName | physicalDeliveryOfficeName | průběžně, poslední změna |
| Mobile Phone | mobile | mobile | průběžně, poslední změna. Prázdná hodnota mobil v AD smaže |
| Office Phone | telephoneNumber | telephoneNumber | průběžně, poslední změna. Prázdná hodnota telefon v AD smaže |
| uzsvmHomeDirectory | homeDirectory | homeDirectory | jen při založení účtu |
| uzsvmHomeDrive | homeDrive | homeDrive | jen při založení účtu |
| uzsvmScriptPath | scriptPath | scriptPath | jen při založení účtu |
| uzsvmAD_OU | AD_OU | OU v DN | při založení se v cestě nahradí Běžní za Zablokovaní, po povolení se účet přesune do této OU |

Ulice, PSČ a kancelář se při prázdné hodnotě v AD nemažou. Město ano.

`uzsvmProfilePath` se do metaverza načte, do AD se nezapíše. Zápis `profilePath` při založení účtu je v kódu zakomentovaný.

### Na portále je a do AD neteče

Portál tyto atributy nesmí do metaverza jako zdroj, nebo pro ně není export do AD. Další synchronizace hodnotu na portále přepíše ze zdroje, který přednost má.

| Atribut portálu | Co se stane |
| --- | --- |
| First Name, Last Name, Employee ID | zdroj je Vema, portál je až třetí |
| Account Name, Display Name, E-mail | po vzniku účtu je zdroj AD |
| Department, Job Title, Company | zdroj je AD nebo Vema, portálový atribut se do metaverza nenačítá |
| Description | portál je druhý zdroj metaverza, export description do AD ale není. Při založení se popis počítá z kódu lokality a zařazení |
| Cost Center, obě data, Employee Type | v AD se jako atributy neexportují |
| uzsvmADEnabled | směr je opačný, z AD na portál |

### Skupina z portálu do AD

Skupinu MIM v AD nezakládá. Tok platí pro skupinu, která už je v AD připojená podle `sAMAccountName`.

| Atribut portálu | Metaverzum | Atribut AD | Pro které skupiny |
| --- | --- | --- | --- |
| Member | member | member | ISMS skupina a skupina s členstvím spravovaným z MIM |
| Displayed Owner | manager | managedBy | jen ISMS skupina |
| konstanta ISMS Group | — | adminDescription | jen ISMS skupina |
