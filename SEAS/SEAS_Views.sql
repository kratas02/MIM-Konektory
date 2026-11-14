-- =============================================================================
-- SEAS - sada pohledu nad FIMSynchronizationService.dbo.mms_metaverse / mms_mv_link
-- =============================================================================
-- Databaze:   FIMSynchronizationService
-- Ucel:       Poskytnout report kombinujici APL skupiny, jejich cleny a (pokud
--             existuje) prislusnou zadost (RoleRequest), ktera dany clenstvi
--             ve skupine zalozila.
--
-- Zdrojove tabulky pouzite v pohledech (overeno primo na serveru MIM):
--   dbo.mms_metaverse  - hlavni tabulka objektu metaverse (Group, Person,
--                         RoleRequest, ...), rozlisena sloupcem object_type.
--   dbo.mms_mv_link    - reference/multivalue vazby mezi objekty
--                         (object_id, attribute_name, reference_id, lineage_id,
--                         lineage_date).
--
-- POZNAMKA K OVERENI: Server MIM obsahuje FIMSynchronizationService jen se
-- zakladni/obecnou strukturou tabulek (109 obecnych sloupcu mms_metaverse),
-- SEAS specificke atributy (externalRoleCode, appGroupType, applicationCode,
-- applicationModulCode, validFrom/ValidTo, seasIsValid, seasappRoleSubType,
-- isValid, reqStatus) tam nejsou nasazeny a v datech nejsou zadne radky
-- (0 objektu, 0 vazeb) - proto nebylo mozne CREATE VIEW s temito sloupci
-- primo na serveru MIM odzkouset. Nazvy/typy sloupcu vychazi presne z
-- dotazu, ktere byly zadany jako vzor. Join/logika pohledu byla funkcne
-- overena na lokalnim mock modelu se stejnou strukturou (viz
-- _mock_schema_and_data.sql v tomto adresari) a syntax byla zvalidovana
-- T-SQL parserem. Pred nasazenim na produkci doporucuji spustit tento
-- skript nejprve na testovacim prostredi, kde jsou SEAS atributy skutecne
-- nasazeny, a zkontrolovat presne nazvy/typy sloupcu (napr. zda isValid a
-- seasIsValid jsou bit, nebo nvarchar 'True'/'False' apod.)
--
-- Databaze FIMSynchronizationService ma collation Latin1_General_CI_AS,
-- tedy porovnani attribute_name (napr. 'member' vs 'Member') je case
-- INSENSITIVE - na velikosti pismen v nazvech atributu tedy nezalezi.
-- =============================================================================

USE FIMSynchronizationService;
GO

-- -----------------------------------------------------------------------------
-- 1) dbo.vw_SEAS_Groups
--    Zakladni seznam APL skupin (object_type = 'Group', appGroupType = 'apl').
--    Odpovida 1. zadanemu dotazu (duplicitni externalRoleCode ve vzoru byl
--    v tomto pohledu sloucen do jednoho vyskytu).
-- -----------------------------------------------------------------------------
CREATE OR ALTER VIEW dbo.vw_SEAS_Groups
AS
    SELECT
        mv.object_id,
        mv.csObjectID,
        mv.accountName,
        mv.externalRoleCode,
        mv.displayName,
        mv.description,
        mv.applicationCode,
        mv.applicationModulCode,
        mv.validFrom,
        mv.ValidTo,
        mv.seasIsValid,
        mv.seasappRoleSubType
    FROM dbo.mms_metaverse AS mv
    WHERE mv.object_type = 'Group'
      AND mv.appGroupType = 'apl';
GO

-- -----------------------------------------------------------------------------
-- 2) dbo.vw_SEAS_GroupMembers
--    Zaklad vysledneho pohledu: kombinace skupiny (vw_SEAS_Groups) a jejich
--    clenu. Vazba clen -> skupina je v mms_mv_link, kde:
--      object_id      = reference na skupinu
--      attribute_name = 'member'
--      reference_id   = reference na uzivatele (clena)
--    K referenci clena jsou pripojeny i jeho zakladni udaje z mms_metaverse
--    (LEFT JOIN, aby se pohled nerozbil, pokud by reference_id nekdy ukazoval
--    na jiz neexistujici objekt).
-- -----------------------------------------------------------------------------
CREATE OR ALTER VIEW dbo.vw_SEAS_GroupMembers
AS
    SELECT
        g.object_id             AS GroupObjectId,
        g.csObjectID            AS GroupCsObjectID,
        g.accountName           AS GroupAccountName,
        g.externalRoleCode      AS GroupExternalRoleCode,
        g.displayName           AS GroupDisplayName,
        g.description           AS GroupDescription,
        g.applicationCode       AS GroupApplicationCode,
        g.applicationModulCode  AS GroupApplicationModulCode,
        g.validFrom             AS GroupValidFrom,
        g.ValidTo               AS GroupValidTo,
        g.seasIsValid           AS GroupSeasIsValid,
        g.seasappRoleSubType    AS GroupSeasAppRoleSubType,
        lnk.reference_id        AS MemberObjectId,
        lnk.lineage_id          AS MemberLineageId,
        lnk.lineage_date        AS MemberLineageDate,
        m.csObjectID            AS MemberCsObjectID,
        m.accountName           AS MemberAccountName,
        m.displayName           AS MemberDisplayName
    FROM dbo.vw_SEAS_Groups AS g
    INNER JOIN dbo.mms_mv_link AS lnk
        ON lnk.object_id = g.object_id
       AND lnk.attribute_name = N'member'
    LEFT JOIN dbo.mms_metaverse AS m
        ON m.object_id = lnk.reference_id;
GO

-- -----------------------------------------------------------------------------
-- 3) dbo.vw_SEAS_RoleRequests
--    Zadosti (object_type = 'RoleRequest') dooplnene o reference na clena a
--    na app roli (skupinu), ktere jsou dohledatelne v mms_mv_link pod
--    atributy 'Member' a 'AppRoles'.
--
--    Predpoklad: atributy Member a AppRoles jsou na zadosti single-valued
--    (1 zadost -> max. 1 clen a max. 1 role). Pokud by v datech vyjimecne
--    byly multi-valued, OUTER APPLY s TOP (1) zajisti, ze pohled vrati
--    presne jeden radek na zadost (vybere jednu z hodnot) - nedojde k
--    nechtenemu krizovemu soucinu, ktery by nastal pri dvou LEFT JOINech.
-- -----------------------------------------------------------------------------
CREATE OR ALTER VIEW dbo.vw_SEAS_RoleRequests
AS
    SELECT
        rr.object_id                AS RequestObjectId,
        rr.csObjectID               AS RequestCsObjectID,
        rr.isValid                  AS RequestIsValid,
        rr.reqStatus                AS RequestStatus,
        rr.validFrom                AS RequestValidFrom,
        rr.ValidTo                  AS RequestValidTo,
        rr.externalRoleCode         AS RequestExternalRoleCode,
        memberLink.reference_id     AS RequestMemberObjectId,
        memberLink.lineage_date     AS RequestMemberLineageDate,
        roleLink.reference_id       AS RequestAppRoleObjectId,
        roleLink.lineage_date       AS RequestAppRoleLineageDate
    FROM dbo.mms_metaverse AS rr
    OUTER APPLY (
        SELECT TOP (1) l.reference_id, l.lineage_date
        FROM dbo.mms_mv_link AS l
        WHERE l.object_id = rr.object_id
          AND l.attribute_name = N'Member'
        ORDER BY l.lineage_date DESC
    ) AS memberLink
    OUTER APPLY (
        SELECT TOP (1) l.reference_id, l.lineage_date
        FROM dbo.mms_mv_link AS l
        WHERE l.object_id = rr.object_id
          AND l.attribute_name = N'AppRoles'
        ORDER BY l.lineage_date DESC
    ) AS roleLink
    WHERE rr.object_type = 'RoleRequest';
GO

-- -----------------------------------------------------------------------------
-- 4) dbo.vw_SEAS_GroupMembersWithRequests
--    Vysledny pohled: zaklad (vw_SEAS_GroupMembers) doplneny o atributy
--    zadosti (vw_SEAS_RoleRequests) tam, kde je mozne zadost napojit podle
--    dvojice (Member, AppRoles) na existujici radek zaklad. Jde o LEFT JOIN,
--    protoze zadost nemusi pro dane clenstvi existovat ("pokud bude
--    existovat rolerequest... pridaji se dalsi atributy").
--
--    POZOR na kardinalitu: pokud existuje pro stejnou dvojici (clen, skupina)
--    vice zadosti (napr. historie - starsi zamitnuta/odebrana + novejsi
--    aktivni), radek zakladu se pro kazdou takovou zadost zopakuje. Pokud je
--    potreba jen jedna (napr. nejnovejsi/aktivni) zadost na clenstvi, pouzijte
--    dbo.vw_SEAS_GroupMembersWithLatestRequest nize.
-- -----------------------------------------------------------------------------
CREATE OR ALTER VIEW dbo.vw_SEAS_GroupMembersWithRequests
AS
    SELECT
        gm.GroupObjectId,
        gm.GroupCsObjectID,
        gm.GroupAccountName,
        gm.GroupExternalRoleCode,
        gm.GroupDisplayName,
        gm.GroupDescription,
        gm.GroupApplicationCode,
        gm.GroupApplicationModulCode,
        gm.GroupValidFrom,
        gm.GroupValidTo,
        gm.GroupSeasIsValid,
        gm.GroupSeasAppRoleSubType,
        gm.MemberObjectId,
        gm.MemberLineageId,
        gm.MemberLineageDate,
        gm.MemberCsObjectID,
        gm.MemberAccountName,
        gm.MemberDisplayName,
        rr.RequestObjectId,
        rr.RequestIsValid,
        rr.RequestStatus,
        rr.RequestValidFrom,
        rr.RequestValidTo,
        rr.RequestExternalRoleCode
    FROM dbo.vw_SEAS_GroupMembers AS gm
    LEFT JOIN dbo.vw_SEAS_RoleRequests AS rr
        ON rr.RequestMemberObjectId = gm.MemberObjectId
       AND rr.RequestAppRoleObjectId = gm.GroupObjectId;
GO

-- -----------------------------------------------------------------------------
-- 5) dbo.vw_SEAS_GroupMembersWithLatestRequest (volitelne)
--    Stejne jako predchozi pohled, ale pri vice zadostech na stejne clenstvi
--    vraci jen tu s nejnovejsim RequestValidFrom (1 radek na clenstvi). Pouzijte
--    tento pohled, pokud vysledek nema obsahovat historicke duplicity.
-- -----------------------------------------------------------------------------
CREATE OR ALTER VIEW dbo.vw_SEAS_GroupMembersWithLatestRequest
AS
    SELECT
        gm.GroupObjectId,
        gm.GroupCsObjectID,
        gm.GroupAccountName,
        gm.GroupExternalRoleCode,
        gm.GroupDisplayName,
        gm.GroupDescription,
        gm.GroupApplicationCode,
        gm.GroupApplicationModulCode,
        gm.GroupValidFrom,
        gm.GroupValidTo,
        gm.GroupSeasIsValid,
        gm.GroupSeasAppRoleSubType,
        gm.MemberObjectId,
        gm.MemberLineageId,
        gm.MemberLineageDate,
        gm.MemberCsObjectID,
        gm.MemberAccountName,
        gm.MemberDisplayName,
        rr.RequestObjectId,
        rr.RequestIsValid,
        rr.RequestStatus,
        rr.RequestValidFrom,
        rr.RequestValidTo,
        rr.RequestExternalRoleCode
    FROM dbo.vw_SEAS_GroupMembers AS gm
    OUTER APPLY (
        SELECT TOP (1) *
        FROM dbo.vw_SEAS_RoleRequests AS r
        WHERE r.RequestMemberObjectId = gm.MemberObjectId
          AND r.RequestAppRoleObjectId = gm.GroupObjectId
        ORDER BY r.RequestValidFrom DESC
    ) AS rr;
GO

-- -----------------------------------------------------------------------------
-- 6) dbo.vw_SEAS_RoleRequestsWithoutGroupAssignment
--    Zadosti (RoleRequest) ve stavu RequestStatus = 7, u kterych neexistuje
--    prirazeni clena do cilove skupiny (AppRoles). Jde o inverzni pohled
--    k vw_SEAS_GroupMembersWithRequests: stav 7 bez odpovidajiciho clenstvi
--    v mms_mv_link (attribute_name = 'member' na skupine).
--
--    Zahrnuje i zadosti bez vazby Member / AppRoles (pak "prirazeni ve
--    skupine" logicky neexistuje). reqStatus je v metaverse nvarchar,
--    proto se porovnava s N'7'.
-- -----------------------------------------------------------------------------
CREATE OR ALTER VIEW dbo.vw_SEAS_RoleRequestsWithoutGroupAssignment
AS
    SELECT
        rr.RequestObjectId,
        rr.RequestCsObjectID,
        rr.RequestIsValid,
        rr.RequestStatus,
        rr.RequestValidFrom,
        rr.RequestValidTo,
        rr.RequestExternalRoleCode,
        rr.RequestMemberObjectId,
        rr.RequestMemberLineageDate,
        m.csObjectID            AS MemberCsObjectID,
        m.accountName           AS MemberAccountName,
        m.displayName           AS MemberDisplayName,
        rr.RequestAppRoleObjectId,
        rr.RequestAppRoleLineageDate,
        g.csObjectID            AS GroupCsObjectID,
        g.accountName           AS GroupAccountName,
        g.externalRoleCode      AS GroupExternalRoleCode,
        g.displayName           AS GroupDisplayName,
        g.applicationCode       AS GroupApplicationCode,
        g.applicationModulCode  AS GroupApplicationModulCode
    FROM dbo.vw_SEAS_RoleRequests AS rr
    LEFT JOIN dbo.mms_metaverse AS m
        ON m.object_id = rr.RequestMemberObjectId
    LEFT JOIN dbo.mms_metaverse AS g
        ON g.object_id = rr.RequestAppRoleObjectId
    WHERE rr.RequestStatus = N'7'
      AND NOT EXISTS (
            SELECT 1
            FROM dbo.mms_mv_link AS lnk
            WHERE lnk.object_id = rr.RequestAppRoleObjectId
              AND lnk.attribute_name = N'member'
              AND lnk.reference_id = rr.RequestMemberObjectId
      );
GO

-- -----------------------------------------------------------------------------
-- 7) dbo.vw_SEAS_GroupMembersAndUnassignedRequests
--    Finalni report: sloucene radky z
--      - vw_SEAS_GroupMembersWithRequests (platna skupina, RequestStatus 7
--        nebo zadost neexistuje)
--      - vw_SEAS_RoleRequestsWithoutGroupAssignment (stav 7 bez clenstvi,
--        ale se znamou skupinou i clenem)
--    Sloupce odpovidaji pozadovanemu vystupu.
-- -----------------------------------------------------------------------------
CREATE OR ALTER VIEW dbo.vw_SEAS_GroupMembersAndUnassignedRequests
AS
    SELECT
        gm.GroupAccountName,
        gm.GroupExternalRoleCode,
        gm.MemberAccountName,
        gm.RequestValidFrom,
        gm.RequestValidTo
    FROM dbo.vw_SEAS_GroupMembersWithRequests AS gm
    WHERE (gm.GroupValidTo IS NULL OR gm.GroupValidTo > GETDATE())
      AND (gm.RequestStatus = 7 OR gm.RequestStatus IS NULL)

    UNION ALL

    SELECT
        ur.GroupAccountName,
        ur.GroupExternalRoleCode,
        ur.MemberAccountName,
        ur.RequestValidFrom,
        ur.RequestValidTo
    FROM dbo.vw_SEAS_RoleRequestsWithoutGroupAssignment AS ur
    WHERE ur.GroupCsObjectID IS NOT NULL
      AND ur.MemberCsObjectID IS NOT NULL;
GO
