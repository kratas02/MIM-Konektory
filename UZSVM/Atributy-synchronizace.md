# Atributy v průběhu synchronizace

Co se s atributem osoby a skupiny stane v jednom běhu synchronizace a v běhu následujícím. Katalog polí Vema je v `Popis-atributu-Vema.md`.

Pořadí v běhu je pořád stejné. Nejdřív se hodnoty načtou do metaverza. U řazené priority platí první zdroj, který hodnotu má. U rovnocenné priority platí poslední změna. Recall je zapnutý, takže když vyšší zdroj o hodnotu přijde, může nastoupit nižší. Pak se metaverzum exportuje do AD, na portál a třemi atributy zpět do Vema. Provisioning nový účet založí mezi tím, jen když konektor do AD ještě není.

Mazání je u části exportů potlačené. Prázdné metaverzum cílovou hodnotu nesmaže. Kde potlačené není, prázdná hodnota v metaverzu cíl vymaže.

Atributy vybrané na konektoru Vema, které do metaverza nevstoupí, synchronizace v běhu nepoužije. Jsou vypsané v katalogu Vema.

## Jméno

| Atribut v metaverzu | Co se v běhu stane |
| --- | --- |
| sn | Jediný zdroj je Vema. Export jde do AD `sn` a na portál Last Name. Mazání v AD je potlačené. Změna příjmení ve Vema se do AD zapíše. |
| givenName | První zdroj je Vema, pak AD, pak portál First Name. Export jde do AD `givenName` a na portál. Mazání v AD je potlačené. Dokud Vema jméno posílá, přepíše hodnotu z AD. |
| displayName | První zdroj je AD. Než účet v AD vznikne, složí se z Vema jako příjmení, mezera a jméno. Atribut `displayName` ve Vema se nečte. Při založení účtu se zapíše do AD a do CN. V každém dalším exportu se vrací do Vema. Na portál jde jako Display Name. Mazání v AD i ve Vema je potlačené. |
| initials | Jediný zdroj je výpočet z Vema: první znak jména a první znak příjmení. Atribut `initials` ve Vema se nečte. Export jde jen do AD. Mazání je potlačené. Na portál se neposílá. |
| title | Jediný zdroj je Vema `title`. Export jde jen do AD `title`. Mazání je potlačené. Do portálu Job Title se tento atribut nedostane. |

Po vzniku účtu tedy příjmení, křestní jméno, funkce a iniciály dál řídí Vema. Zobrazované jméno řídí AD a do Vema se jen vrací.

## Účet a přihlášení

| Atribut v metaverzu | Co se v běhu stane |
| --- | --- |
| employeeID | První zdroj je Vema, pak AD, pak portál. Je to párovací klíč na osobu. Export jde do AD a na portál Employee ID. Mazání v AD je potlačené. Dokud Vema číslo posílá, platí to. |
| accountName | První zdroj je AD `sAMAccountName`, pak Vema `sAMAccountName`, pak portál Account Name. Než účet vznikne, platí jméno z Vema a je podmínkou založení. Při založení se zapíše do AD. Klasický export ho do AD dál neudržuje. Zpět do Vema se zapisuje v každém exportu. Po vzniku účtu tedy přejmenování ve Vema účet v AD nezmění. Přejmenování v AD se do Vema vrátí. |
| userPrincipalName | První zdroj je AD. Než AD hodnotu má, složí se z Vema `mail`: část před zavináčem a `@uzsvm.cz`. Při založení se zapíše do AD a dál se klasickým exportem neudržuje. Do Vema se v každém exportu vrací tato výsledná hodnota, ne surový `userPrincipalName` z Vema. |
| mail | První zdroj je AD `mail`, pak Vema `mail`. Export jde na portál E-mail. Do AD `mail` se neexportuje. Jakmile schránka nebo AD mail pošle, změna e-mailu ve Vema se do metaverza nedostane. Pořád se z něj ale skládá UPN, dokud UPN nepošle AD. |
| mailNickname | První zdroj je AD `mailNickname`, pak hodnota Vema `userPrincipalName`. Nikam se neexportuje. V metaverzu zůstane a další běh ji nepřepíše jinam. |
| unicodePwd | Z data narození ve Vema `extensionAttribute4` se složí text `Heslo` a datum `ddMMyyyy`. Zapíše se jen při založení účtu. `pwdLastSet` se nastaví na 0. Další běh heslo do AD neposílá. |
| domain | Z přítomnosti účtu v AD se vloží konstanta `UZSVM`. Export jde na portál Domain. Mazání na portále je potlačené. |
| objectSid | Jediný zdroj je AD. Export jde na portál ObjectSID. Mazání je potlačené. |
| cn | Jediný zdroj je AD `cn`. Nikam se neexportuje. CN nového účtu se skládá při založení ze zobrazovaného jména, ne z tohoto atributu. |

## Zařazení a organizace

| Atribut v metaverzu | Co se v běhu stane |
| --- | --- |
| employeeType | Z přítomnosti uživatele ve Vema se vloží konstanta `Zaměstnanec`. Text zařazení z Vema sem nejde. Export jde na portál Employee Type a mazání tam potlačené není. Konstanta je podmínka založení účtu. Do AD `employeeType` se nezapíše. |
| jobTitle | První zdroj je AD `employeeType`, druhý je text Vema `employeeType`. Než účet vznikne, platí text z Vema a exportuje se na portál Job Title. Při založení se tento text zapíše do AD `employeeType` a za pomlčku do popisu účtu. Od dalšího importu má přednost hodnota, která už v AD je. Změna zařazení ve Vema se do AD nedostane, protože klasický export `employeeType` ani `jobTitle` do AD neposílá. |
| department | První zdroj je AD, pak Vema. Než účet vznikne, platí útvar z Vema a je podmínkou založení. Při založení se zapíše do AD. Od dalšího importu platí útvar z AD a export ho do AD jen opisuje. Změna ve Vema projde, teprve když AD útvar přestane posílat. Mazání v AD potlačené není, prázdná hodnota útvar v AD smaže. Na portál jde Department a mazání tam také potlačené není. |
| costCenter | Jediný zdroj je Vema `extensionAttribute1`. Export jde na portál Cost Center a zároveň do `uzsvmKodLokality`. Mazání na portále potlačené není. Při založení účtu je začátkem popisu. Bez něj se účet nezaloží. Číselník na portále podle něj hledá záznam `uzsvmPolicy`, to ale není krok synchronizace. |
| company | Z přítomnosti uživatele ve Vema se vloží konstanta `ÚZSVM` s předností před AD `company`. Atribut `company` ve Vema se nečte. Export jde do AD, mazání je potlačené, a na portál Company, kde mazání potlačené není. Hodnotu, kterou na portál zapíše číselník, další běh přepíše zpět na `ÚZSVM`. |
| description | První zdroj je AD `description`, pak portál Description, pak výraz z Vema: kód lokality, mezera, pomlčka, mezera a text zařazení. Atribut `description` ve Vema se nečte. Při založení účtu se do AD zapíše kód lokality, pomlčka a `jobTitle`, ne atribut `description` z metaverza. Od dalšího importu platí popis z AD. Klasický export popis do AD neposílá, takže pozdější změna kódu nebo zařazení ve Vema popis v AD nezmění. Na portál se exportuje a mazání tam potlačené není. |
| employeeStatus | Z přítomnosti uživatele ve Vema se vloží konstanta `zam`. Nikam se neexportuje. Provisioning ji čte, když má povolený účet přesunout z OU Zablokovaní do cílové OU. |
| extensionAttribute9 | Z přítomnosti uživatele ve Vema se vloží konstanta `VEMA`. Nikam se neexportuje. |
| manager | Jediný zdroj je AD `manager`. Export jde na portál Manager. Mazání na portále potlačené není. Do AD se nevrací. |

## Adresa a kontakt

Město, ulice a PSČ jsou rovnocenné. V jednom běhu vyhraje ten zdroj, který hodnotu změnil jako poslední. Telefon, mobil a kancelář jsou také rovnocenné, mezi portálem a AD.

| Atribut v metaverzu | Zdroje | Export |
| --- | --- | --- |
| l | AD, Vema, portál City | AD `l`, mazání není potlačené. Portál City, mazání není potlačené. |
| streetAddress | portál Address, AD, Vema | AD `streetAddress`, mazání je potlačené. Portál Address, mazání není potlačené. |
| postalCode | portál Postal Code, Vema, AD | AD `postalCode`, mazání je potlačené. Portál Postal Code, mazání není potlačené. |
| telephoneNumber | AD, portál Office Phone | AD, mazání není potlačené. Portál Office Phone, mazání není potlačené. |
| mobile | portál Mobile Phone, AD | AD, mazání není potlačené. Portál Mobile Phone, mazání není potlačené. |
| physicalDeliveryOfficeName | portál Office Location, AD | AD, mazání je potlačené. Portál Office Location, mazání není potlačené. |

Z Vema tedy město, ulice a PSČ do běhu vstupují. Telefon, mobil a kancelář z Vema ne. Skript, který by zahazoval města začínající `202`, na tok navázaný není.

## Data a stav účtu

| Atribut v metaverzu | Co se v běhu stane |
| --- | --- |
| employeeStartDate | Z Vema `extensionAttribute2`, text `d.M.yyyy`, se uloží `yyyy-MM-ddT00:00:00.000`. Jediný zdroj. Export jde na portál Employee Start Date a mazání tam potlačené není. Provisioning podle něj rozhoduje, jestli účet založit: nástup musí být dnes nebo později. |
| employeeEndDate | Stejný převod z Vema `extensionAttribute3`. Export jde na portál Employee End Date. Od tohoto data portálové odchodové sady účet zakážou. Samotný export do AD datum odchodu nezapíše. |
| extensionAttribute4 | Datum narození z Vema, beze změny tvaru, který ve Vema je. Nikam se neexportuje. Slouží jen jako vstup hesla při založení účtu. |
| UserState | Výpočet z nástupu a odchodu. `1`, když nástup už nastal a odchod ještě ne. Chybějící odchod se bere jako vzdálená budoucnost, chybějící nástup dává `0`. Druhý zdroj je konstanta `10` z AD a použije se, jen když Vema stav nepřispěje. Na portál se neexportuje. Export do AD ho použije u `userAccountControl` a u skrytí v adresáři. |
| adEnabled | Skript z AD `userAccountControl`. Na portál jde jako `uzsvmADEnabled` a mazání tam potlačené není. Zpět do AD se nekopíruje přímo. Exportní skript povolí účet, když je `UserState` 1 a tento příspěvek není starší než příspěvek `adEnabled`. Ruční zákaz v AD tím v dalším běhu vydrží. |
| userAccountControl | Přímá hodnota z AD. Nikam se neexportuje. Provisioning podle ní pozná, že účet už není zakázaný, a smí ho přesunout z OU Zablokovaní. Při založení se do AD zapíše 514, bez ohledu na tento atribut. |
| msExchHideFromAddressLists | Skript z AD. Když atribut v AD chybí, do metaverza se uloží nepravda. Exportní skript skrytí zruší, když je `UserState` 1, příspěvek není starší než hodnota z AD a v AD je skrytí zapnuté. Mazání exportu je potlačené. |
| toCreate | Spočítá se z nástupu a odchodu. Žádný export ani provisioning ho nečte. Založení účtu rozhoduje `employeeStartDate`. |
| placedAD | Spočítá se z nástupu a odchodu. Nikdo ho nečte. Portálové Vytvořit v AD synchronizace neplní. |
| DeleteStatus | Má být `3`, když od odchodu uplynuly tři měsíce, jinak `0`. Nikdo ho nečte. Ve zdroji je navíc přiřazení do proměnné, která v třídě není deklarovaná, takže tato větev v dodaném souboru nejde přeložit. |

## Umístění, které se vrací z portálu

Tyto atributy Vema nemá. V běhu, kdy už je portál doplnil, se načtou do metaverza a provisioning je použije. Klasický export je do AD většinou neudržuje. Zapíšou se při založení účtu, pokud už v metaverzu jsou.

| Atribut v metaverzu | Zdroj | Co se dál stane |
| --- | --- | --- |
| AD_OU | jen portál `uzsvmAD_OU` | Podmínka založení účtu. DN se složí jako `CN=` zobrazované jméno a tato cesta, při vzniku se v ní Běžní nahradí slovem Zablokovaní. Až je účet povolený a stav je `zam`, provisioning ho přesune do cesty beze záměny. Do AD jako atribut OU se neexportuje. |
| homeDirectory | portál `uzsvmHomeDirectory`, pak AD | Při založení se zapíše do AD. Další běh ho klasickým exportem neobnovuje. Změna na portále se do AD dostane, jen když se účet zakládá znovu. |
| homeDrive | portál `uzsvmHomeDrive`, pak AD | Stejně jako adresář. Zapíše se při založení. |
| scriptPath | portál `uzsvmScriptPath`, pak AD | Zapíše se při založení. Klasický export ho dál neposílá. |
| profilePath | portál `uzsvmProfilePath`, pak AD | V metaverzu je, zápis při založení účtu je v kódu zakomentovaný. Do AD se nedostane. |

Na portál se z AD v dalším běhu vrací ještě `AD_DN` do `uzsvmAD_DN`, `legacyExchangeDN` do `uzsvmLegacyExchangeDN` a `proxyAddresses` do Proxy Address Collection. Mazání DN a legacy DN na portále potlačené není. Proxy adresy mazání potlačené mají. `msExchUsageLocation` se při založení nastaví na `CZ` a do metaverza se nenačítá.

Z AD se do metaverza načtou také `extensionAttribute1`, `extensionAttribute5` do `extensionAttribute6`, `extensionAttribute7` a `extensionAttribute10`. Žádný z nich se neexportuje. Vema `extensionAttribute1` je mezitím v `costCenter`, ne v tomto `extensionAttribute1`.

`expectedRulesList`, `detectedRulesList`, `csObjectID` a `MVObjectID` jsou technické vazby synchronizačních pravidel a identity objektu. Osobní údaj nepřenášejí.

## Skupina

Skupina v metaverzu nemá zdroje z Vema ani z AD. Všechno, co se v běhu načte, přijde z portálu.

| Atribut v metaverzu | Co se v běhu stane |
| --- | --- |
| accountName | Z portálu Account Name. Podle něj se skupina připojí k existující skupině v AD, jejíž `sAMAccountName` se shoduje. Do AD se účet skupiny nezapíše a nová skupina v AD se nezaloží. |
| displayName | Z portálu. Export ho vrací na portál. Do AD se nezapíše. |
| description | Z portálu. Do AD se nezapíše. |
| member | Z portálu Member. U ISMS skupiny a u skupiny s příznakem spravovaného členství se v exportu zapíše do AD `member`. Zároveň se vrací na portál do `uzsvmCountedMember`. |
| manager | Ze zobrazeného vlastníka na portále. U ISMS skupiny se exportuje do AD `managedBy`. |
| isISMSGroup | Z portálu `uzsvmIsISMSGroup`. Samo se do AD nezapíše. Když je pravda, export přidá do AD `adminDescription` hodnotu `ISMS Group` a pošle členy a vlastníka. |
| MIMManagedMembership | Z portálu `uzsvmMIMManagedMembership`. Když je pravda, export pošle do AD jen členy. |

Na portál se ze skupiny vrací také účet, alias, e-mail, doména, rozsah, typ a workflow pro přidání člena. Alias, e-mail, doména, rozsah a typ v metaverzu nemají jiný zdroj než případnou hodnotu, která tam už je. Běh je z AD ani z Vema nedoplní.

Export skupiny do Vema je připravený pro název, alias, členy, typ a e-mail. Provisioning konektor do Vema nezakládá, takže se v běhu nepoužije. `groupType` v metaverzu zdroj nemá.
