-- Pouze pro lokalni testovani (SeasMock DB na .\SQLEXPRESS) - NENI soucasti nasazeni.
-- Napodobuje strukturu mms_metaverse (overeno na serveru MIM) + SEAS specificke
-- atributy presne dle nazvu, ktere byly zadany v ukazkovych dotazech.
USE SeasMock;
GO

CREATE TABLE dbo.mms_metaverse
(
    row_key                 bigint IDENTITY(1,1) NOT NULL,
    object_id               uniqueidentifier NOT NULL PRIMARY KEY,
    object_type             nvarchar(128)    NOT NULL,
    csObjectID              nvarchar(896)    NULL,
    accountName             nvarchar(896)    NULL,
    displayName             nvarchar(896)    NULL,
    description             nvarchar(896)    NULL,
    -- SEAS specificke atributy skupiny (Group, appGroupType='apl')
    appGroupType            nvarchar(896)    NULL,
    externalRoleCode        nvarchar(896)    NULL,
    applicationCode         nvarchar(896)    NULL,
    applicationModulCode    nvarchar(896)    NULL,
    validFrom               nvarchar(896)    NULL,
    ValidTo                 nvarchar(896)    NULL,
    seasIsValid             bit              NULL,
    seasappRoleSubType      nvarchar(896)    NULL,
    -- SEAS specificke atributy zadosti (RoleRequest)
    isValid                 bit              NULL,
    reqStatus               nvarchar(896)    NULL
);
GO

CREATE INDEX IX_mms_metaverse_object_type ON dbo.mms_metaverse(object_type);
GO

CREATE TABLE dbo.mms_mv_link
(
    object_id       uniqueidentifier NOT NULL,
    attribute_name  nvarchar(256)    NOT NULL,
    lineage_id      uniqueidentifier NOT NULL,
    lineage_date    datetime         NOT NULL,
    reference_id    uniqueidentifier NOT NULL
);
GO

CREATE INDEX IX_mms_mv_link_object_attr ON dbo.mms_mv_link(object_id, attribute_name, reference_id);
CREATE INDEX IX_mms_mv_link_reference_id ON dbo.mms_mv_link(reference_id);
GO

-- ---------------------------------------------------------------------------
-- Testovaci data
-- ---------------------------------------------------------------------------

DECLARE @Group1 uniqueidentifier = 'AAAAAAAA-0000-0000-0000-000000000001'; -- appGroupType='apl' -> ma se zobrazit
DECLARE @Group2 uniqueidentifier = 'AAAAAAAA-0000-0000-0000-000000000002'; -- appGroupType<>'apl' -> nema se zobrazit
DECLARE @Member1 uniqueidentifier = 'BBBBBBBB-0000-0000-0000-000000000001'; -- ma i matchujici RoleRequest
DECLARE @Member2 uniqueidentifier = 'BBBBBBBB-0000-0000-0000-000000000002'; -- clen bez zadosti
DECLARE @Member3 uniqueidentifier = 'BBBBBBBB-0000-0000-0000-000000000003'; -- clen se 2 zadostmi (historie)
DECLARE @Req1 uniqueidentifier = 'CCCCCCCC-0000-0000-0000-000000000001';   -- Member1 + Group1 (match)
DECLARE @Req2 uniqueidentifier = 'CCCCCCCC-0000-0000-0000-000000000002';   -- Member3 + Group1 (match, starsi)
DECLARE @Req3 uniqueidentifier = 'CCCCCCCC-0000-0000-0000-000000000003';   -- Member3 + Group1 (match, novejsi)
DECLARE @Req4 uniqueidentifier = 'CCCCCCCC-0000-0000-0000-000000000004';   -- Member2 + Group2 (bez vazby na vysledny pohled)
DECLARE @Member4 uniqueidentifier = 'BBBBBBBB-0000-0000-0000-000000000004'; -- neni clenem zadne skupiny
DECLARE @Req5 uniqueidentifier = 'CCCCCCCC-0000-0000-0000-000000000005';   -- status 7, Member4 + Group1 (BEZ clenstvi -> ma se zobrazit)
DECLARE @Req6 uniqueidentifier = 'CCCCCCCC-0000-0000-0000-000000000006';   -- status 7, Member1 + Group1 (s clenstvim -> NEma se zobrazit)

INSERT INTO dbo.mms_metaverse (object_id, object_type, csObjectID, accountName, displayName, description, appGroupType, externalRoleCode, applicationCode, applicationModulCode, validFrom, ValidTo, seasIsValid, seasappRoleSubType)
VALUES
    (@Group1, 'Group', 'CS-G-001', 'grp.apl.finance', 'Financni aplikace - uzivatel', 'Testovaci APL skupina', 'apl', 'ROLE-FIN-USER', 'FIN', 'FIN-CORE', '2020-01-01', NULL, 1, 'standard'),
    (@Group2, 'Group', 'CS-G-002', 'grp.ad.other',    'Bezna AD skupina',            'Nema byt v pohledu (neni apl)', 'security', NULL, NULL, NULL, NULL, NULL, NULL, NULL);

INSERT INTO dbo.mms_metaverse (object_id, object_type, csObjectID, accountName, displayName, description)
VALUES
    (@Member1, 'Person', 'CS-P-001', 'jnovak',  'Jan Novak',   'Clen se zadosti'),
    (@Member2, 'Person', 'CS-P-002', 'pvomacka','Petr Vomacka','Clen bez zadosti'),
    (@Member3, 'Person', 'CS-P-003', 'akovarova','Alena Kovarova','Clen se 2 zadostmi (historie)'),
    (@Member4, 'Person', 'CS-P-004', 'mhajek',  'Martin Hajek',  'Zadost status 7 bez clenstvi ve skupine');

INSERT INTO dbo.mms_metaverse (object_id, object_type, isValid, reqStatus, validFrom, ValidTo, externalRoleCode)
VALUES
    (@Req1, 'RoleRequest', 1, 'Provisioned', '2024-01-10', NULL,        'ROLE-FIN-USER'),
    (@Req2, 'RoleRequest', 0, 'Removed',     '2022-05-01', '2023-01-01','ROLE-FIN-USER'),
    (@Req3, 'RoleRequest', 1, 'Provisioned', '2023-01-02', NULL,        'ROLE-FIN-USER'),
    (@Req4, 'RoleRequest', 1, 'Provisioned', '2024-02-01', NULL,        'ROLE-OTHER'),
    (@Req5, 'RoleRequest', 1, '7',           '2024-03-01', NULL,        'ROLE-FIN-USER'),
    (@Req6, 'RoleRequest', 1, '7',           '2024-03-15', NULL,        'ROLE-FIN-USER');

-- Group -> Member vazby (attribute_name = 'member')
INSERT INTO dbo.mms_mv_link (object_id, attribute_name, lineage_id, lineage_date, reference_id)
VALUES
    (@Group1, 'member', NEWID(), '2024-01-01', @Member1),
    (@Group1, 'member', NEWID(), '2024-01-01', @Member2),
    (@Group1, 'member', NEWID(), '2024-01-01', @Member3),
    (@Group2, 'member', NEWID(), '2024-01-01', @Member2);

-- RoleRequest -> Member / AppRoles vazby
INSERT INTO dbo.mms_mv_link (object_id, attribute_name, lineage_id, lineage_date, reference_id)
VALUES
    (@Req1, 'Member',   NEWID(), '2024-01-10', @Member1),
    (@Req1, 'AppRoles', NEWID(), '2024-01-10', @Group1),
    (@Req2, 'Member',   NEWID(), '2022-05-01', @Member3),
    (@Req2, 'AppRoles', NEWID(), '2022-05-01', @Group1),
    (@Req3, 'Member',   NEWID(), '2023-01-02', @Member3),
    (@Req3, 'AppRoles', NEWID(), '2023-01-02', @Group1),
    (@Req4, 'Member',   NEWID(), '2024-02-01', @Member2),
    (@Req4, 'AppRoles', NEWID(), '2024-02-01', @Group2),
    (@Req5, 'Member',   NEWID(), '2024-03-01', @Member4),
    (@Req5, 'AppRoles', NEWID(), '2024-03-01', @Group1),
    (@Req6, 'Member',   NEWID(), '2024-03-15', @Member1),
    (@Req6, 'AppRoles', NEWID(), '2024-03-15', @Group1);
GO
