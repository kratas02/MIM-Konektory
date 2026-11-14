# Životní cyklus uživatelů a skupin

Cesta objektu od vzniku po poslední krok, který tahle konfigurace udělá. Detail atributů je v `Popis-atributu-Vema.md`, cesta z Vema do AD v `Proces-Vema-do-AD.md`, jednotlivá pravidla portálu v `Procesy-portalu.md`.

Uživatel vzniká ve Vema. Skupina vzniká na portále. Ani jeden cyklus v konfiguraci nekončí smazáním účtu nebo skupiny v AD.

## Uživatel

```text
Vema  →  metaverzum  →  portál  →  zakázaný účet v AD
                                      →  schránka, složka, skupiny
                                      →  povolený účet v cílové OU
                                      →  zákaz a přesun do Zablokovaní
                                      →  po 90 dnech přesun do UsersForDel
```

### Vznik ve Vema

Zaměstnanec se objeví jako uživatel v `OU=UzsvmUser`. Synchronizace ho připojí k osobě v metaverzu podle `employeeID`. Když osoba ještě není, projekce ji založí.

Z Vema se berou osobní číslo, jméno, příjmení, funkce, pracovní zařazení, kód lokality, útvar, e-mail, přihlašovací jméno, adresa a data nástupu, odchodu a narození. Synchronizace z nich složí zobrazované jméno, iniciály, UPN, popis a počáteční heslo. Doplní konstanty firma `ÚZSVM`, typ `Zaměstnanec` a stav `zam`.

Do Vema se zpět zapíší zobrazované jméno, `sAMAccountName` a UPN. Po vzniku účtu v AD jsou to hodnoty, které vrátilo AD.

### Osoba na portále a umístění

Export založí na portále objekt Person a zapíše na něj údaje z metaverza. V tu chvíli ještě nemá cílovou OU, disk ani skript.

Ty doplní číselník `uzsvmPolicy`, když osoba vstoupí do sady zaměstnanců s kódem lokality. Název záznamu číselníku musí být kód lokality. Portál nastaví firmu, popis, domovský adresář, písmeno disku, přihlašovací skript a `uzsvmAD_OU`. Další synchronizace je načte zpět. Bez OU se účet v AD nezaloží.

Číselník běží jen při vstupu do sady. Pozdější změna kódu lokality OU ani disk znovu nespočítá.

Filtr sady používá `starts-with` na znak procento. Když kód lokality procentem nezačíná, osoba do sady nevstoupí, číselník se nespustí a účet nevznikne. Stejná podmínka je u e-mailu u sady nových zaměstnanců.

### Účet v AD

Provisioning účet založí, jen když v AD ještě není a nástup je dnes nebo v budoucnu. Musí být vyplněné OU z portálu, kód lokality, útvar, datum nástupu, přihlašovací jméno a typ `Zaměstnanec`.

Účet vznikne zakázaný v OU, která vznikne z cílové cesty záměnou slova Běžní za Zablokovaní. DN je `CN=` zobrazované jméno. Zapíší se účet, UPN, zobrazované jméno, útvar, zařazení, popis, umístění schránky `CZ` a cesta domovského adresáře, pokud už je spočítaná. Řádek přibude do `c:\Batch\newUsers.txt`.

Zaměstnanec, který ve Vema přijde až po datu nástupu, se v AD sám nezaloží. Když účet v AD už existuje, připojí se k osobě podle `employeeID`, `sAMAccountName` nebo `mail`.

### Uvedení do provozu

Až účet v AD existuje, DN začíná `CN` a nástup je pořád v budoucnu, portál v sadě nových zaměstnanců udělá tři věci. Zapne schránku a přidá alias. Založí na disku složku se jménem účtu. Přidá účet do skupin eSSL podle zařazení, do skupiny útvaru z `C:\Script\depatments.txt` a do tří VPN skupin. Členství se zapíše přímo v AD, ne jako členství skupiny na portále.

Povolení má dvě cesty. Portál povolí účet v okně dvou dnů před nástupem a zruší skrytí v adresáři. Synchronizace ho povolí znovu, když je `UserState` 1 a tento údaj není starší než stav načtený z AD. `UserState` je 1 od dne nástupu do dne odchodu.

Když je účet povolený, stav je `zam` a DN pořád leží v OU Zablokovaní, provisioning ho přesune na `CN=` zobrazované jméno v cílové OU z portálu.

### Běžný provoz

Při změně ve Vema se znovu přenesou osobní číslo, jméno, příjmení, funkce, kód lokality a obě data. Z dat se znovu spočítá `UserState`.

Jakmile účet v AD existuje, AD má přednost u zobrazovaného jména, útvaru, e-mailu, přihlašovacího jména, UPN, popisu a pracovního zařazení. Změna téhož údaje ve Vema se do metaverza nedostane, dokud ji AD posílá. Město, ulice a PSČ platí podle poslední změny z Vema, AD nebo portálu.

Ruční zákaz účtu v AD vydrží, dokud Vema stav nepřepočítá později než AD.

### Odchod

Datum odchodu přijde z Vema. Od toho dne je `UserState` 0 a synchronizace účet kvůli Vema znovu nepovolí.

Portál pak, když je odchod starší než včera, zakáže schránku, zakáže účet a přesune ho do OU, kde je Běžní nebo Neaktivní nahrazené slovem Zablokovaní.

Když účet zůstane povolený i po odchodu, portál nastaví `ExpirationTime` na sedm dní. Po vypršení, pokud je účet pořád povolený, zopakuje zákaz schránky, zákaz účtu a přesun. Když někdo datum odchodu změní, expirace se smaže.

Po 90 dnech od odchodu, když DN obsahuje `OU=Zabloko`, se účet přesune do `OU=UsersForDel,OU=Uživatelé,OU=Správa,DC=uzsvm,DC=cz` a skryje se v adresáři.

### Co se na konci nesmaže

Tady cyklus v konfiguraci končí. Účet v AD zůstane v `UsersForDel`.

Mazání domovské složky a profilu je ve workflow připravené, příkazy jsou zakomentované. Workflow, které umí účet v AD smazat, žádné zapnuté pravidlo nespouští.

Osoba v metaverzu se maže, až když zmizí poslední konektor kromě portálu, tedy konektor Vema i konektor AD. Portálová osoba se při tom smaže. Účet v AD se jen odpojí a v adresáři zůstane. Odpojení ale v tomhle kódu nemá dokončenou cestu: metoda `Deprovision` v `UZSVMSyncExtension` není implementovaná a vyhazuje výjimku. Smazání uživatele ve Vema nebo v AD synchronizace touto metodou neuzavře.

## Skupina

```text
portál  →  metaverzum  →  připojení k existující skupině v AD
                              →  členové a vlastník zpět do AD
                              →  smazání na portále skupinu v AD nesmaže
```

### Vznik

Skupinu zakládá portál. Synchronizace ji neprojektuje z AD ani z Vema. Vema skupiny do metaverza nečte a zakládání skupiny ve Vema je v kódu vypnuté.

Každá skupina z portálu se projektuje na skupinu v metaverzu. Z portálu se do ní berou účet, zobrazovaný název, popis, členové, zobrazený vlastník, příznak ISMS skupiny a příznak, že členství spravuje MIM.

### Napojení na AD

Skupina v AD se připojí, když její `sAMAccountName` odpovídá účtu skupiny v metaverzu. Atributy skupiny se z AD do metaverza nenačítají. AD je cíl, ne zdroj.

Pravidla, která do AD zapisují, skupinu nezaloží. `CreateConnectedSystemObject` je u obou vypnuté. Když v AD skupina se stejným účtem není, MIM ji nevytvoří a není kam členy zapsat.

Do Vema je připravený zápis názvu, aliasu, členů, typu a e-mailu. Bez založeného konektoru se nepoužije.

### Členství

Dvě pravidla posílají členy z metaverza do AD. Platí jen pro skupiny, které už jsou připojené.

- ISMS skupina (`uzsvmIsISMSGroup`): do AD jdou členové, `managedBy` ze zobrazeného vlastníka a `adminDescription` s hodnotou `ISMS Group`.
- Skupina s členstvím spravovaným z MIM (`uzsvmMIMManagedMembership`): do AD jdou jen členové.

Přidání člena do ISMS skupiny na portále čeká na schválení. Teprve po něm se členství zapíše na portál a dalším během se dostane do AD. Odebrání člena statické bezpečnostní skupiny také čeká na schválení. Změna vlastníka v sadě vlastníků ISMS spustí aktualizaci vlastníků.

Členství z nástupu zaměstnance touto cestou nejde. Workflow nástupu přidá účet přímo do pojmenovaných skupin v AD.

### Vlastník a platnost

Skupina bez vlastníka dostane `Owner` i `DisplayedOwner` nastavené na osobu `fb89aefa-5ea1-47f1-8890-abe7797d6497`. Vlastník skupiny ji podle vestavěných pravidel portálu může měnit a smazat.

Když se blíží konec platnosti skupiny, portál pošle upozornění. Skupinu tím nesmaže a její atributy nezmění.

### Odstranění

Metaverzum skupinu smaže, jakmile se odpojí její objekt na portále. Smazání na portále je tedy konec skupiny v MIM.

Účet skupiny v AD se při tom neodstraní. Úklid konektoru AD je odpojení, objekt v adresáři zůstane. Kdyby existoval konektor do Vema, úklid Vema by objekt smazal. Provisioning ho nezakládá, takže se tahle větev za běžného běhu nespustí.

Žádné zapnuté pravidlo skupinu v AD nemaže.
