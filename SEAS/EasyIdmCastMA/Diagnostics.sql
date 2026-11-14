-- =============================================================================
-- EasyIdmCastMA - diagnostika Full / Delta Import
-- Server / DB: MIM / EasyIDM
-- =============================================================================
USE EasyIDM;
GO

-- 1) Pocet radku Full Importu (validni SAP casty)
WITH RoleSource AS (
    SELECT
        i.IDMObjectId,
        JSON_VALUE(rs.JsonData, '$.applicationModulCode') AS applicationModulCode,
        JSON_VALUE(rsApp.JsonData, '$.applicationCodeAP') AS applicationCode
    FROM dbo.IDMObjects AS i
    LEFT OUTER JOIN dbo.IDMObjectReadStore AS rs ON i.IDMObjectId = rs.IdmObjectId
    LEFT OUTER JOIN dbo.RoleAccesses AS ra ON ra.ApplicationRoleId = i.IDMObjectId
    LEFT OUTER JOIN dbo.IDMObjectReadStore AS rsApp ON ra.ResourceId = rsApp.IdmObjectId
    WHERE i.IDMObjectTypeId = 6
),
RoleCategoryRaw AS (
    SELECT
        IDMObjectId,
        applicationCode,
        applicationModulCode,
        CASE
            WHEN applicationCode LIKE 'SAP% A' THEN 'SAP_BAPI_ARCH'
            WHEN applicationCode LIKE 'SAP%' AND applicationCode NOT LIKE '% A'
                 AND (applicationModulCode NOT LIKE 'RDPV%' OR applicationModulCode IS NULL) THEN 'SAP_BAPI_PS4'
            WHEN applicationCode LIKE 'SAP%' AND applicationCode NOT LIKE '% A'
                 AND (applicationModulCode LIKE 'RDPV%') THEN 'SAP_BTP'
            ELSE NULL
        END AS category
    FROM RoleSource
),
RoleCategory AS (
    SELECT IDMObjectId, applicationCode, applicationModulCode, category
    FROM (
        SELECT IDMObjectId, applicationCode, applicationModulCode, category,
               ROW_NUMBER() OVER (
                   PARTITION BY IDMObjectId
                   ORDER BY CASE WHEN category IS NOT NULL THEN 0 ELSE 1 END, applicationCode
               ) AS rn
        FROM RoleCategoryRaw
    ) AS ranked
    WHERE rn = 1
)
SELECT
    COUNT(*) AS FullImportRows,
    SUM(CASE WHEN category = 'SAP_BAPI_ARCH' THEN 1 ELSE 0 END) AS SAP_BAPI_ARCH,
    SUM(CASE WHEN category = 'SAP_BAPI_PS4' THEN 1 ELSE 0 END) AS SAP_BAPI_PS4,
    SUM(CASE WHEN category = 'SAP_BTP' THEN 1 ELSE 0 END) AS SAP_BTP
FROM (
    SELECT
        y.category,
        ROW_NUMBER() OVER (
            PARTITION BY i.TechName, r.TechName
            ORDER BY
                CASE WHEN c.ValidFrom IS NULL THEN 1 ELSE 0 END,
                c.ValidFrom DESC,
                c.CastId DESC
        ) AS dupRn
    FROM dbo.Casts AS c
    INNER JOIN dbo.IDMObjects AS i ON i.IDMObjectId = c.IdentityId
    INNER JOIN dbo.IDMObjects AS r ON r.IDMObjectId = c.RoleId
    INNER JOIN RoleCategory AS y ON y.IDMObjectId = c.RoleId
    WHERE c.IsArchived = 0
      AND (c.ValidTo > GETDATE() OR c.ValidTo IS NULL)
      AND y.category IS NOT NULL
      AND r.TechName NOT LIKE 'RDPV_AUTBASE%'
) AS ranked
WHERE dupRn = 1;
GO

-- 2) Smazane casty
SELECT COUNT(*) AS DeletedCasts
FROM dbo.Casts
WHERE IsArchived = 1 AND IsValid = 0;
GO

-- 3) Delta kandidati za poslednich 24 hodin (upravit @from)
DECLARE @from datetime2 = DATEADD(hour, -24, SYSDATETIME());
SELECT COUNT(DISTINCT c.CastId) AS DeltaCandidates24h
FROM dbo.Casts AS c
INNER JOIN dbo.IDMObjects AS i ON i.IDMObjectId = c.IdentityId
INNER JOIN dbo.IDMObjects AS r ON r.IDMObjectId = c.RoleId
LEFT OUTER JOIN dbo.RoleAccesses AS ra ON ra.ApplicationRoleId = c.RoleId
WHERE r.IDMObjectTypeId = 6
  AND (r.TechName IS NULL OR r.TechName NOT LIKE 'RDPV_AUTBASE%')
  AND (
        c.Created >= @from OR ISNULL(c.Modified, c.Created) >= @from
     OR i.Created >= @from OR ISNULL(i.Modified, i.Created) >= @from
     OR r.Created >= @from OR ISNULL(r.Modified, r.Created) >= @from
     OR ra.Created >= @from OR ISNULL(ra.Modified, ra.Created) >= @from
     OR (c.ValidTo IS NOT NULL AND c.ValidTo > @from AND c.ValidTo <= GETDATE())
     OR (c.IsArchived = 1 AND c.IsValid = 0 AND (c.Created >= @from OR ISNULL(c.Modified, c.Created) >= @from))
  );
GO
