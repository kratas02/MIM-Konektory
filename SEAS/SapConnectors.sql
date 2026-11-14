WITH RoleSource AS (
    SELECT
        role.IDMObjectId,
        role.TechName,
        JSON_VALUE(rsRole.JsonData, '$.application_applicationCodeAP') AS ApplicationCode,
        JSON_VALUE(rsRole.JsonData, '$.application_applicationModulCode') AS ApplicationModulCode
    FROM dbo.IDMObjects AS role
    LEFT JOIN dbo.IDMObjectReadStore AS rsRole
        ON rsRole.IdmObjectId = role.IDMObjectId
    WHERE role.IDMObjectTypeId = 6
      AND role.TechName NOT LIKE 'RDPV_AUTBASE%'
),
RoleCategory AS (
    SELECT
        IDMObjectId,
        CASE
            WHEN ApplicationCode LIKE 'SAP% A' THEN 'SAP_BAPI_ARCH'
            WHEN ApplicationCode LIKE 'SAP%'
             AND ApplicationCode NOT LIKE '% A'
             AND ISNULL(ApplicationModulCode, '') NOT LIKE 'RDPV%' THEN 'SAP_BAPI_PS4'
            WHEN ApplicationCode LIKE 'SAP%'
             AND ApplicationCode NOT LIKE '% A'
             AND ISNULL(ApplicationModulCode, '') LIKE 'RDPV%' THEN 'SAP_BTP'
            ELSE NULL
        END AS Category
    FROM RoleSource
),
Users AS (
    SELECT
        i.IDMObjectId,
        i.TechName,
        i.Name,
        JSON_VALUE(rs.JsonData, '$.sAPConnectors') AS SAPConnectorsCurrent,
        rs.JsonData AS UserJson
    FROM dbo.IDMObjects AS i
    LEFT JOIN dbo.IDMObjectReadStore AS rs
        ON rs.IdmObjectId = i.IDMObjectId
    WHERE i.IDMObjectTypeId = 3
      AND i.IsArchived = 0
),
CurrentSorted AS (
    SELECT
        d.IDMObjectId,
        STRING_AGG(d.Connector, ';') WITHIN GROUP (ORDER BY d.Connector) AS SAPConnectorsCurrentSorted
    FROM (
        SELECT DISTINCT
            u.IDMObjectId,
            LTRIM(RTRIM(s.value)) AS Connector
        FROM Users AS u
        CROSS APPLY STRING_SPLIT(ISNULL(u.SAPConnectorsCurrent, N''), ';') AS s
        WHERE LTRIM(RTRIM(s.value)) <> N''
    ) AS d
    GROUP BY d.IDMObjectId
),
NewConnectors AS (
    SELECT
        d.IdentityId,
        STRING_AGG(d.Category, ';') WITHIN GROUP (ORDER BY d.Category) AS SAPConnectorsNEW
    FROM (
        SELECT DISTINCT
            vc.IdentityId,
            rc.Category
        FROM dbo.view_ValidCasts AS vc
        INNER JOIN RoleCategory AS rc
            ON rc.IDMObjectId = vc.RoleId
        WHERE vc.IsArchived = 0
          AND rc.Category IS NOT NULL
    ) AS d
    GROUP BY d.IdentityId
)
SELECT
    u.IDMObjectId,
    u.TechName,
    u.Name,
    u.SAPConnectorsCurrent,
    n.SAPConnectorsNEW,
    u.UserJson
FROM Users AS u
LEFT JOIN CurrentSorted AS c
    ON c.IDMObjectId = u.IDMObjectId
LEFT JOIN NewConnectors AS n
    ON n.IdentityId = u.IDMObjectId
WHERE ISNULL(c.SAPConnectorsCurrentSorted, N'') <> ISNULL(n.SAPConnectorsNEW, N'')
ORDER BY u.TechName