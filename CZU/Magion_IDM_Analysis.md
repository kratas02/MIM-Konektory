# Magion – IDM Analýza: Flow, Podmínky a Atributy

> Zdroj: `CZUIdm_FullInventory.pdf` (NetIQ IDM, CZU Praha, 10.5.2023)

---

## 1. Přehled Magion driverů


| Driver                    | Sekce    | Typ         | Směr         | Tabulka/View                                       |
| ------------------------- | -------- | ----------- | ------------ | -------------------------------------------------- |
| **IN-Magion**             | 3.1.1.9  | JDBC Oracle | Magion → IDM | `V_IDM_ZAM`, `V_IDM_PPV`, `MAGION_CZU.IDM_CHANGES` |
| **OUT-Magion-Rooms**      | 3.1.1.11 | JDBC Oracle | IDM → Magion | `MAGION_EXT.T_MISTNOSTI`                           |
| **IN-Magion-BaseUnit**    | 3.1.1.28 | JDBC Oracle | Magion → IDM | `V_IDM_UNIT`                                       |
| **IN-Magion-PPVRelation** | 3.1.1.33 | JDBC Oracle | Magion → IDM | `V_IDM_PPV`                                        |
| **IN-Magion-Komise**      | 3.1.1.68 | JDBC Oracle | Magion → IDM | `V_IDM_KOMISE`                                     |
| **IN-Magion-PracMista**   | 3.1.1.69 | JDBC Oracle | Magion → IDM | `IDM.V_IDM_MISTA`                                  |


---

## 2. Připojení k Magion (Oracle RAC)

```
JDBC URL: jdbc:oracle:thin:@(DESCRIPTION =
  (ADDRESS_LIST =
    (ADDRESS = (PROTOCOL = TCP)(Host = magi2.oikt.czu.cz)(Port = 1521))
    (ADDRESS = (PROTOCOL = TCP)(Host = magi1.oikt.czu.cz)(Port = 1521))
  )
  (CONNECT_DATA = (SERVER = DEDICATED)(SERVICE_NAME = MAGI.kondor.czu.cz))
)
Driver: oracle.jdbc.driver.OracleDriver v4.2.1.0
User: IDM
```

---

## 3. IN-Magion (hlavní driver – zaměstnanci + PPV)

### 3.1 Konfigurace

- **Publikační mód:** triggerless (bez triggerů)
- **Polling interval:** 80 sekund
- **Log tabulka:** `MAGION_CZU.IDM_CHANGES`
- **GCV cfgFile:** `$listFiles/config/Cfg-JDBC-MAGION-Vault`
- **GCV LNameZAMcfg:** `/IDM/CFG/ldapCfg.xml`

### 3.2 Schema Mapping – V_IDM_ZAM → User (Zaměstnanec)


| Magion (view sloupec) | IDM atribut              | Poznámka                                             |
| --------------------- | ------------------------ | ---------------------------------------------------- |
| `JMENO`               | `Given Name`             | Křestní jméno (max 32 znaků)                         |
| `PRIJMENI`            | `Surname`                | Příjmení                                             |
| `UIC`                 | `workforceID`            | Unikátní ID osoby                                    |
| `OS_CIS`              | `claOsCisZAM`            | Osobní číslo zaměstnance                             |
| `NAROZ_DATUM`         | `claBirthDay`            | Datum narození                                       |
| `TITUL_PRED`          | `claDegreePrefix`        | Titul před jménem                                    |
| `TITUL_ZA`            | `claDegreeSuffix`        | Titul za jménem                                      |
| `RODNE_PRIJMENI`      | `claMaidenName`          | Rodné příjmení                                       |
| `ZAKL_PP`             | `claZaklPPV`             | Základní pracovněprávní vztah (číslo)                |
| `POHLAVI`             | `claSex`                 | Pohlaví (`M`=muž, `Z`=žena → mapovací tabulka)       |
| `BYDL_TRV_OBEC_NAZEV` | `city`                   | Město trvalého bydliště                              |
| `BYDL_TRV_PSC`        | `Postal Code`            | PSČ (reformatováno: odstraněny mezery)               |
| `BYDL_TRV_ULICE`      | `S`                      | Ulice + číslo popisné (BYDL_TRV_ULICE + BYDL_TRV_CP) |
| `BYDL_TRV_2`          | `claPostalState`         | Stát bydliště                                        |
| `MOBIL`               | `claPersonalMobilePhone` | Mobil (odstraněny mezery a NBSP)                     |
| `PRISLUSNOST`         | `CO`                     | Příslušnost (stát)                                   |
| `OBCANSTVI_KOD`       | `claCitizenShipCode`     | Kód státního občanství                               |


**Povinné atributy při ADD** (veto pokud chybí):

- `UIC`, `OS_CIS`, `PRIJMENI`, `JMENO`, `ZAKL_PP`

### 3.3 Schema Mapping – V_IDM_PPV → Group (Pracovněprávní vztah)


| Magion (view sloupec) | IDM atribut              | Poznámka                                 |
| --------------------- | ------------------------ | ---------------------------------------- |
| `OSPOM`               | `claOsCisPPV`            | ID pracovněprávního vztahu               |
| `OS_CIS`              | `claOsCisZAM`            | Osobní číslo zaměstnance                 |
| `CISLO_PP`            | `claOrderPPV`            | Číslo PP (pořadí)                        |
| `UIC`                 | `claUICPPV`              | UIC zaměstnance                          |
| `AKTIVNI`             | `claAktivniPPV`          | Aktivita PPV (0/1) – vypočítáno z VYSTUP |
| `DRUH_PP_KOD`         | `claDruhKodPPV`          | Kód druhu pracovního poměru              |
| `DRUH_PP`             | `claDruhPPV`             | Druh pracovního poměru (text)            |
| `FUNKCE_KOD`          | `claFunkceKodPPV`        | Kód funkce                               |
| `FUNKCE`              | `claFunkcePPV`           | Název funkce                             |
| `KZAM_KOD`            | `claKzamKodPPV`          | Kód klasifikace zaměstnání               |
| `KZAM`                | `claKzamPPV`             | Klasifikace zaměstnání (text)            |
| `PRACOVISTE`          | `claPracovistePPV`       | Pracoviště (text)                        |
| `claPracovisteKodPPV` | `claPracovisteKodPPV`    | Kód pracoviště                           |
| `STRED_KMEN_KOD`      | `claStrediskoKodPPV`     | Kód střediska                            |
| `STRED_KMEN`          | `claStrediskoPPV`        | Středisko (text)                         |
| `STRED_KMEN_ZKRATKA`  | `claStrediskoZkratkaPPV` | Zkratka střediska                        |
| `NASTUP`              | `claStartDatePPV`        | Datum nástupu                            |
| `VYSTUP`              | `claEndDatePPV`          | Datum výstupu                            |
| `ZAC`                 | `claLastChangePPV`       | Datum poslední změny                     |
| `VYNETI_DO`           | `claVynetiDo`            | Vyňetí do                                |
| `VYNETI_OD`           | `claVynetiOd`            | Vyňetí od                                |
| `VYSE_UVAZKU`         | `claVyseUvazku`          | Výše úvazku                              |
| `ZARAZENI_KOD`        | `claZarazeniKodPPV`      | Kód zařazení                             |
| `ZARAZENI`            | `claZarazeniPPV`         | Zařazení (text)                          |
| `KATEGORIE1_KOD`      | `claKategorie1KodPPV`    | Kód kategorie 1                          |
| `KATEGORIE1`          | `claKategorie1PPV`       | Kategorie 1 (text)                       |
| `KATEGORIE2_KOD`      | `claKategorie2KodPPV`    | Kód kategorie 2                          |
| `KATEGORIE2`          | `claKategorie2PPV`       | Kategorie 2 (text)                       |
| `FAKULTA`             | `claFaculty`             | Fakulta                                  |


**Povinné atributy při ADD** (veto pokud chybí):

- `OSPOM`, `OS_CIS`, `CISLO_PP`

### 3.4 Pohlaví – mapovací tabulka (TDPJDBCCFG-MappingTable-Gender)


| Magion hodnota | IDM hodnota |
| -------------- | ----------- |
| `Z`            | `žena`      |
| `M`            | `muž`       |


---

## 4. IN-Magion – Publisher Channel Flow (podmínky a akce)

```
Magion DB (V_IDM_ZAM / V_IDM_PPV)
       ↓  [Polling 80s, triggerless]
[1] Pub-InputTransf-1  (Input Transformation)
       ↓
[2] Schema Mapping Rule  (Magion sloupce → IDM atributy)
       ↓
[3] Matching-Rule  (hledání existujícího objektu v IDM)
       ↓
[4] Pub-Create-1  (kontrola povinných atributů)
       ↓
[5] Pub-Placement  (nastavení cílového DN + přiřazení skupin)
       ↓
[6] Pub-CmdTransf-GenerateLogin  (generování loginname při MODIFY)
       ↓
[7] Pub-Placement-GenerateLogin  (generování loginname při ADD)
       ↓
[8] Pub-Placement-PPV  (přiřazení PPV k zaměstnanci)
       ↓
[9] Pub-CmdTransf-PPVStatus  (aktivace/deaktivace PPV)
       ↓
[10] Pub-CmdTransf-CheckChanges  (kontrola změn + zasílání alertů)
       ↓
[11] Pub-CmdTransf-Veto  (veto operace Delete)
       ↓
[12] Pub-CmdTransf-LogAction  (zápis do MAGION_CZU.IDM_CHANGES)
       ↓
[13] Pub-Event-GenarateLogin  (generování loginname při SYNC)
       ↓
[14] Pub-CmdTransf-1P  (přejmenování objektu při MODIFY)
       ↓
[15] Pub-Placement-1P  (nastavení DirXML-TaskItem při ADD)
       ↓
[16] TDPJDBCCFG-pub-ctp  (mapování pohlaví)
       ↓
[17] Pub-Placement-UserDestName  (nastavení jména objektu)
       ↓
[18] Pub-Placement-1  (XSLT placement)
       ↓
[19] Pub-CmdTransf-1  (XSLT úprava modify operace)
       ↓
[20] Pub-Cmd-Transf-SetczuPrimarni  (nastavení czuPrimarni na PPV)
       ↓
    Identity Vault (IDM)
```

### 4.1 Klíčové podmínky a pravidla

#### Matching (Matching-Rule)


| Třída         | Podmínka | Akce                                        |
| ------------- | -------- | ------------------------------------------- |
| `User`        | vždy     | Hledej v `identitiesOU` podle `workforceID` |
| `Group` (PPV) | vždy     | Hledej v `ppvOU` podle `claOsCisPPV`        |


#### Create Policy (Pub-Create-1)


| Třída         | Podmínka | Akce                                                        |
| ------------- | -------- | ----------------------------------------------------------- |
| `User`        | –        | Veto pokud chybí `Surname`, `workforceID` nebo `Given Name` |
| `Group` (PPV) | –        | Veto pokud chybí `claOsCisPPV`                              |


#### PPV Aktivita (Pub-InputTransf-1)

```
IF operace = modify AND V_IDM_PPV
  IF VYSTUP < dnešní datum → AKTIVNI = 0
  ELSE → AKTIVNI = 1
```

#### PPV Status změna (Pub-CmdTransf-PPVStatus)

```
Deaktivace PPV:
  IF operace = delete
  OR (operace = modify AND claAktivniPPV mění se na 0)
  OR (claAktivniPPV = 0 AND Member existuje)
  → Nastav claAktivniPPV = 0
  → Odeber zaměstnance z Member
  → (při delete) Zruš asociaci

Aktivace PPV:
  IF from-merge OR (operace = modify AND claAktivniPPV mění se na 1)
  → Nastav claAktivniPPV = 1
  → Přidej zaměstnance do Member
```

#### Kontrola změn (Pub-CmdTransf-CheckChanges)

Sleduje změny těchto atributů při MODIFY User:

- `workforceID`, `claPersonalIdentityNumber`, `Surname`, `Given Name`
- `claDegreePrefix`, `claDegreeSuffix`, `S`, `city`, `Postal Code`

→ Pokud se změní `claPersonalIdentityNumber` + `Given Name` + `Surname` zároveň, odesílá e-mail upozornění do systémů MAGION, EDOKTORAND a IS STUDIUM.

#### Veto Delete (Pub-CmdTransf-Veto)

```
IF operace = delete → Veto (záznamy se v IDM nemažou)
```

#### Generování Login Name (Pub-CmdTransf-GenerateLogin)

```
Podmínka: User + MODIFY + claLoginNameZAM není k dispozici nebo = "-------"
Algoritmus: LDAPUser:createLNAM(pureFName, pureLName, workforceID)
  - Zbavení diakritiky z Given Name + Surname
  - Max délka 20 znaků (jinak alert + hodnota "-------")
```

#### Formát DN objektu User

```
CN = pureLName_pureFName_workforceID
(příjmení bez diakritiky)_(jméno bez diakritiky)_(UIC)
```

#### czuPrimarni (Pub-Cmd-Transf-SetczuPrimarni)

```
Pro každé PPV daného zaměstnance:
  IF claOrderPPV == claZaklPPV → czuPrimarni = 1
  ELSE → czuPrimarni = 0
```

---

## 5. OUT-Magion-Rooms (místnosti IDM → Magion)

### 5.1 Schema Mapping – Group → T_MISTNOSTI


| IDM atribut    | Magion (tabulka sloupec)                                   |
| -------------- | ---------------------------------------------------------- |
| `claBuildName` | `NAZEV_BUDOVY`                                             |
| `claBuildNr`   | `BUDOVA` (max 4 znaky)                                     |
| `claFloorNr`   | `PODLAZI` (max 50 znaků, reformatováno: N→číslo, P→-číslo) |
| `claRoomIdent` | `NAZEV_MISTNOSTI` (max 30 znaků)                           |
| `claRoomNr`    | `MISTNOST` (max 7 znaků)                                   |
| `claRoomUcel`  | `MIS_POZN`                                                 |


### 5.2 Subscriber Channel Flow

```
Identity Vault (IDM – místnosti z roomsOU)
       ↓
[1] MatchRule  (hledej v Magion podle claRoomIdent)
       ↓
[2] Sub-EventTransf-Veto
       - Veto pokud třída != Group
       - Veto pokud objekt není v roomsOU
       - Veto při ADD pokud chybí claRoomIdent
       ↓
[3] Sub-CmdTransf-ReformatAttribs  (úprava formátu hodnot)
       ↓
    Magion (T_MISTNOSTI)
```

### 5.3 Publisher Channel

```
IF operace = delete → Zruš asociaci + Veto
```

---

## 6. IN-Magion-BaseUnit (organizační struktura)

### 6.1 Schema Mapping – V_IDM_UNIT → Group


| Magion (view sloupec)     | IDM atribut               |
| ------------------------- | ------------------------- |
| `PK_UTVAR`                | `claBaseUnitCode`         |
| `UTVAR_NAZEV`             | `claBaseUnitName`         |
| `UTVAR_ZKRATKA`           | `claBaseUnitAbbr`         |
| `UTVAR_NAZEV_EN`          | `claBaseUnitNameEn`       |
| `UTVAR_ZKRATKA_EN`        | `claBaseUnitAbbrEn`       |
| `PLATNOST_OD`             | `claBaseUnitValidFrom`    |
| `claBaseUnitValidTo`      | `claBaseUnitValidTo`      |
| `UTVAR_VZNIK`             | `claBaseUnitOriginDate`   |
| `claBaseUnitTypeOfUnitId` | `claBaseUnitTypeOfUnitId` |
| `UTVAR_NADRIZ_KOD`        | `claParentBaseUnitCode`   |


### 6.2 Poznámky

- Subscriber je **zakázán** (jen čtení z Maqionu)
- Konfigurace: `JDBC-IS-Magion-BaseUnit`
- Data dále putují do: **OUT-Origin-BaseUnit**, **Null-ESPIS-BaseUnit**, **OUT-Origin-UnitTree**

---

## 7. IN-Magion-PPVRelation (PPV vztahy pro Origin)

### 7.1 Schema Mapping – V_IDM_PPV → Group (pro Origin)


| Magion (view sloupec) | IDM atribut           |
| --------------------- | --------------------- |
| `PK_PPV`              | `claOsCisPPV`         |
| `OSPOM`               | `claBaseRelationId`   |
| `OS_CIS`              | `claOsCisZAM`         |
| `CISLO_PP`            | `claOrderPPV`         |
| `UIC`                 | `claUICPPV`           |
| `AKTIVNI`             | `claAktivniPPV`       |
| `DRUH_PP_KOD`         | `claDruhKodPPV`       |
| `DRUH_PP`             | `claDruhPPV`          |
| `FUNKCE_KOD`          | `claFunkceKodPPV`     |
| `FUNKCE`              | `claFunkcePPV`        |
| `KZAM_KOD`            | `claKzamKodPPV`       |
| `KZAM`                | `claKzamPPV`          |
| `STRED_KMEN_KOD`      | `claStrediskoKodPPV`  |
| `NASTUP`              | `claStartDatePPV`     |
| `VYSTUP`              | `claEndDatePPV`       |
| `VYNETI_DO`           | `claVynetiDo`         |
| `VYNETI_OD`           | `claVynetiOd`         |
| `VYSE_UVAZKU`         | `claVyseUvazku`       |
| `ZARAZENI_KOD`        | `claZarazeniKodPPV`   |
| `ZARAZENI`            | `claZarazeniPPV`      |
| `KATEGORIE1_KOD`      | `claKategorie1KodPPV` |
| `KATEGORIE1`          | `claKategorie1PPV`    |
| `KATEGORIE2_KOD`      | `claKategorie2KodPPV` |
| `KATEGORIE2`          | `claKategorie2PPV`    |
| `FAKULTA`             | `claFaculty`          |


### 7.2 Poznámky

- Data dále putují do: **OUT-Origin-EmployeeRelation**, **OUT-Origin-BaseRelation**
- Policy: `TDPJDBCCFG-pub-ctp`, `Pub-Cmd-Transf-SetczuPrimarni`

---

## 8. IN-Magion-Komise (komise a funkce)

### 8.1 Schema Mapping – V_IDM_KOMISE → Group


| Magion (view sloupec) | IDM atribut                 |
| --------------------- | --------------------------- |
| `OSOBA`               | `czuVirtRelSourceID`        |
| `OS_CIS`              | `czuVirtRelOsCisZAM`        |
| `UIC`                 | `czuVirtRelMemberUIC`       |
| `RK_KOD`              | `czuVirtRelGroupCode`       |
| `RK_ID`               | `czuVirtRelGroupID`         |
| `RK_NAZEV`            | `czuVirtRelGroupName`       |
| `SOJ_CISLO`           | `czuVirtRelGroupOrgUnitID`  |
| `RK_SOUCAST_KOD`      | `czuVirtRelGroupTypeCode`   |
| `CLEN_OD`             | `czuVirtRelMemberFrom`      |
| `CLEN_DO`             | `czuVirtRelMemberTo`        |
| `FUNKCE_KOD`          | `czuVirtRelRelationCode`    |
| `FUNKCE_NAZEV`        | `czuVirtRelRelationNameMas` |
| `FUNKCE_ZNAZEV`       | `czuVirtRelRelationNameFem` |
| `FUNKCE_MNNAZEV`      | `czuVirtRelRelationNamePlu` |


### 8.2 Poznámky

- **Start: Manual** (není auto-start)
- Data dále putují do: **OUT-Origin-Komise**
- Žádná subscriber politika (pouze publisher)

---

## 9. IN-Magion-PracMista (pracovní místa)

### 9.1 Schema Mapping – IDM.V_IDM_MISTA → Group


| Magion (view sloupec) | IDM atribut                    |
| --------------------- | ------------------------------ |
| `PK_ID`               | `czuVirtRelSourceID`           |
| `PMIS_CISLO`          | `czuVirtRelGroupID`            |
| `OS_CIS`              | `czuVirtRelOsCisZAM`           |
| `UIC`                 | `czuVirtRelMemberUIC`          |
| `SOJ_CISLO`           | `czuVirtRelGroupOrgUnitID`     |
| `CISLO_PP`            | `czuVirtRelOrderID`            |
| `PLATNOST_OD`         | `czuVirtRelMemberFrom`         |
| `PLATNOST_DO`         | `czuVirtRelMemberTo`           |
| `PMIS_NAZEV`          | `czuVirtRelRelationName`       |
| `PMIS_UNAZEV`         | `czuVirtRelRelationNameMas`    |
| `PMIS_UNAZEVZ`        | `czuVirtRelRelationNameFem`    |
| `PMIS_TYP_VED`        | `czuVirtRelPriority`           |
| `PMIS_NADRIZ`         | `czuVirtRelParentGroupID`      |
| `PMIS_ALTERNATIV`     | `czuVirtRelRelationAlternativ` |
| `PMIS_ZODP`           | `czuVirtRelResponsible`        |
| `PMIS_VZNIK`          | `czuVirtRelValidFrom`          |
| `PMIS_ZANIK`          | `czuVirtRelValidTo`            |
| `ZVEREJNIT`           | `czuVirtRelVisible`            |


### 9.2 Poznámky

- **Subscriber zakázán** (jen čtení)
- **Polling interval:** 60 sekund
- Data dále putují do: **OUT-Origin-PracMista**

---

## 10. Celkový datový tok (Magion v IDM ekosystému)

```
                    ┌─────────────────────────────────────────────┐
                    │           MAGION (Oracle RAC)                │
                    │  magi1/magi2.oikt.czu.cz:1521               │
                    │  SERVICE: MAGI.kondor.czu.cz                 │
                    └──────────────────┬──────────────────────────┘
                                       │
           ┌───────────────────────────┼───────────────────────────────┐
           │                           │                               │
           ↓                           ↓                               ↓
    IN-Magion                  IN-Magion-BaseUnit            IN-Magion-PPVRelation
  V_IDM_ZAM → User            V_IDM_UNIT → Group              V_IDM_PPV → Group
  V_IDM_PPV → Group                  │                               │
           │                         ↓                               ↓
           │               OUT-Origin-BaseUnit            OUT-Origin-EmployeeRelation
           │               OUT-Origin-UnitTree            OUT-Origin-BaseRelation
           │
           ├──── IN-Magion-Komise (V_IDM_KOMISE) → OUT-Origin-Komise
           ├──── IN-Magion-PracMista (V_IDM_MISTA) → OUT-Origin-PracMista
           │
           ↓                                              ↑
     Identity Vault ──────────────────────────── OUT-Magion-Rooms
     (IDM Trezor)           (místnosti: T_MISTNOSTI ← Group z roomsOU)
           │
           ├── OUT-AD-Zam / OUT-AD-Dok / OUT-AD-Stud
           ├── OUT-eDir-Zam / OUT-eDir-Dok / OUT-eDir-Stud
           ├── OUT-MSOffice365
           ├── OUT-Aleph (knihovna)
           ├── OUT-DMS-Zam / OUT-DMS-Rooms
           └── OUT-CZU-Cards, OUT-ISKAM4-Stud, OUT-CZU-Stud, ...
```

---

## 11. Klíčové IDM atributy – přehled přes všechny Magion drivery


| IDM atribut                                  | IN-Magion | IN-Magion-PPVRelation | IN-Magion-BaseUnit | IN-Magion-Komise | IN-Magion-PracMista | OUT-Magion-Rooms |
| -------------------------------------------- | --------- | --------------------- | ------------------ | ---------------- | ------------------- | ---------------- |
| `Given Name` (JMENO)                         | ✓         | –                     | –                  | –                | –                   | –                |
| `Surname` (PRIJMENI)                         | ✓         | –                     | –                  | –                | –                   | –                |
| `workforceID` (UIC)                          | ✓         | –                     | –                  | –                | –                   | –                |
| `claOsCisZAM` (OS_CIS)                       | ✓         | ✓                     | –                  | ✓                | ✓                   | –                |
| `claOsCisPPV` (OSPOM)                        | ✓         | ✓(PK_PPV)             | –                  | –                | –                   | –                |
| `claUICPPV` (UIC)                            | ✓         | ✓                     | –                  | –                | –                   | –                |
| `claAktivniPPV` (AKTIVNI)                    | ✓         | ✓                     | –                  | –                | –                   | –                |
| `claDruhPPV` (DRUH_PP)                       | ✓         | ✓                     | –                  | –                | –                   | –                |
| `claFunkceKodPPV` (FUNKCE_KOD)               | ✓         | ✓                     | –                  | –                | –                   | –                |
| `claKzamKodPPV` (KZAM_KOD)                   | ✓         | ✓                     | –                  | –                | –                   | –                |
| `claStrediskoKodPPV` (STRED_KMEN_KOD)        | ✓         | ✓                     | –                  | –                | –                   | –                |
| `claStartDatePPV` (NASTUP)                   | ✓         | ✓                     | –                  | –                | –                   | –                |
| `claEndDatePPV` (VYSTUP)                     | ✓         | ✓                     | –                  | –                | –                   | –                |
| `claVyseUvazku` (VYSE_UVAZKU)                | ✓         | ✓                     | –                  | –                | –                   | –                |
| `claFaculty` (FAKULTA)                       | ✓         | ✓                     | –                  | –                | –                   | –                |
| `claBaseUnitCode` (PK_UTVAR)                 | –         | –                     | ✓                  | –                | –                   | –                |
| `claBaseUnitName` (UTVAR_NAZEV)              | –         | –                     | ✓                  | –                | –                   | –                |
| `claParentBaseUnitCode` (UTVAR_NADRIZ_KOD)   | –         | –                     | ✓                  | –                | –                   | –                |
| `czuVirtRelMemberUIC` (UIC)                  | –         | –                     | –                  | ✓                | ✓                   | –                |
| `czuVirtRelGroupCode` (RK_KOD)               | –         | –                     | –                  | ✓                | –                   | –                |
| `czuVirtRelGroupID` (RK_ID/PMIS_CISLO)       | –         | –                     | –                  | ✓                | ✓                   | –                |
| `czuVirtRelMemberFrom` (CLEN_OD/PLATNOST_OD) | –         | –                     | –                  | ✓                | ✓                   | –                |
| `czuVirtRelMemberTo` (CLEN_DO/PLATNOST_DO)   | –         | –                     | –                  | ✓                | ✓                   | –                |
| `czuVirtRelRelationCode` (FUNKCE_KOD)        | –         | –                     | –                  | ✓                | –                   | –                |
| `claBuildNr` (BUDOVA)                        | –         | –                     | –                  | –                | –                   | ✓                |
| `claRoomIdent` (NAZEV_MISTNOSTI)             | –         | –                     | –                  | –                | –                   | ✓                |
| `claRoomNr` (MISTNOST)                       | –         | –                     | –                  | –                | –                   | ✓                |
| `claFloorNr` (PODLAZI)                       | –         | –                     | –                  | –                | –                   | ✓                |


---

## 12. Log tabulka – MAGION_CZU.IDM_CHANGES

Používá se pro:

1. **Detekci změn** (triggerless publication – driver čte změny z logu)
2. **Zápis akcí** (Pub-CmdTransf-LogAction zapisuje každý ADD/MODIFY)

Struktura zápisů:


| Sloupec       | Hodnota                      |
| ------------- | ---------------------------- |
| `OBJECT_TYPE` | Třída objektu (User/Group)   |
| `OBJECT_DN`   | Cílové DN v IDM              |
| `UIC`         | UIC zaměstnance              |
| `ACTION_TYPE` | Typ akce (add/modify/delete) |
| `STATUS`      | `N` = nové (zpracovat)       |


---

*Dokument vygenerován automaticky z `CZUIdm_FullInventory.pdf` analýzou IDM konfigurace CZU Praha.*