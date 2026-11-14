-- EasyIDM: uzivatele, u kterych se lisi userWorkstations a hodnota
-- spocitana z clenstvi v objektech typu 32.
--
-- userWorkstations je obycejny text (klic v IDMObjectReadStore).
-- Nova hodnota jsou TechName objektu typu 32, ve kterych je uzivatel clenem
-- (view_ValidCasts), serazene a spojene carkou. Zadne dalsi podminky na typ 32.
--
-- Typy objektu:
--   3  = identity
--   32 = objekt, jehoz TechName patri do userWorkstations

USE EasyIDM;
GO

WITH Objects AS (
    SELECT
        o.IDMObjectId,
        o.TechName
    FROM dbo.IDMObjects AS o
    WHERE o.IDMObjectTypeId = 32
),
Users AS (
    SELECT
        i.IDMObjectId,
        i.TechName,
        i.Name,
        JSON_VALUE(rs.JsonData, '$.userWorkstations') AS UserWorkstations,
        rs.JsonData AS UserJson
    FROM dbo.IDMObjects AS i
    LEFT JOIN dbo.IDMObjectReadStore AS rs
        ON rs.IdmObjectId = i.IDMObjectId
    WHERE i.IDMObjectTypeId = 3
      AND i.IsArchived = 0
),
NewWorkstations AS (
    SELECT
        d.IdentityId,
        STRING_AGG(d.TechName, ';') WITHIN GROUP (ORDER BY d.TechName) AS UserWorkstationsNew
    FROM (
        SELECT DISTINCT
            vc.IdentityId,
            o.TechName
        FROM dbo.view_ValidCasts AS vc
        INNER JOIN Objects AS o
            ON o.IDMObjectId = vc.RoleId
        WHERE vc.IsArchived = 0
    ) AS d
    GROUP BY d.IdentityId
)
SELECT
    u.TechName,
    u.Name,
    u.IDMObjectId,
    u.UserWorkstations,
    n.UserWorkstationsNew,
    u.UserJson
FROM Users AS u
LEFT JOIN NewWorkstations AS n
    ON n.IdentityId = u.IDMObjectId
WHERE ISNULL(u.UserWorkstations, N'') <> ISNULL(n.UserWorkstationsNew, N'')
ORDER BY u.TechName;
