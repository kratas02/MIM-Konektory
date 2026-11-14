-- Clenove skupin v metaverzi synchronizacni sluzby (FIMSynchronizationService).
-- member je zdrojove clenstvi (stejne jako GroupMemberAdds.sql / GroupMemberRemovals.sql).
-- Pro clenstvi prinesene importem z AD zamen 'member' za 'memberAD'.

USE FIMSynchronizationService;

-- 1) Jsou mezi cleny i jine objekty nez uzivatel?
--    Uzivatel v metaverzi je person / Person (nekde user / User).
--    Radek "(odkaz bez objektu v metaverzi)" je reference, ktera uz nema objekt.

SELECT
    ISNULL(m.object_type, '(odkaz bez objektu v metaverzi)') AS MemberObjectType,
    COUNT(*)                         AS MemberLinkCount,
    COUNT(DISTINCT src.reference_id) AS DistinctMembers,
    COUNT(DISTINCT src.object_id)    AS DistinctGroups
FROM dbo.mms_mv_link AS src WITH (NOLOCK)
INNER JOIN dbo.mms_metaverse AS g WITH (NOLOCK)
    ON g.object_id = src.object_id
   AND g.object_type = 'Group'
LEFT JOIN dbo.mms_metaverse AS m WITH (NOLOCK)
    ON m.object_id = src.reference_id
WHERE src.attribute_name = 'member'
GROUP BY m.object_type
ORDER BY MemberLinkCount DESC;

-- Konkretni clenove, kteri nejsou uzivatele
-- (vnorene skupiny, kontakty, pocitace i odkazy bez objektu).

SELECT
    g.object_id      AS GroupId,
    g.accountName    AS GroupAccountName,
    g.displayName    AS GroupDisplayName,
    src.reference_id AS MemberId,
    ISNULL(m.object_type, '(odkaz bez objektu v metaverzi)') AS MemberObjectType,
    m.accountName    AS MemberAccountName,
    m.displayName    AS MemberDisplayName
FROM dbo.mms_mv_link AS src WITH (NOLOCK)
INNER JOIN dbo.mms_metaverse AS g WITH (NOLOCK)
    ON g.object_id = src.object_id
   AND g.object_type = 'Group'
LEFT JOIN dbo.mms_metaverse AS m WITH (NOLOCK)
    ON m.object_id = src.reference_id
WHERE src.attribute_name = 'member'
  AND (
        m.object_id IS NULL
        OR m.object_type NOT IN (N'person', N'Person', N'user', N'User')
      )
ORDER BY MemberObjectType, g.accountName, m.accountName;

-- 2) Vicenasobne vnoreni: skupina je clenem skupiny a ta vnorena skupina
--    ma jako clena zase skupinu.
--    Prazdny vysledek = vnoreni je nejvyse jedne urovne.
--    Depth je pocet hran (2 = skupina ve skupine ve skupine).
--    Cycle = 1 je kruh. Truncated = 1 znamena, ze retez pokracuje za limit 25 urovni.

WITH GroupEdges AS (
    SELECT DISTINCT
        parent.object_id AS ParentId,
        ISNULL(parent.accountName, CONVERT(nvarchar(36), parent.object_id)) AS ParentAccountName,
        child.object_id AS ChildId,
        ISNULL(child.accountName, CONVERT(nvarchar(36), child.object_id)) AS ChildAccountName
    FROM dbo.mms_mv_link AS lnk WITH (NOLOCK)
    INNER JOIN dbo.mms_metaverse AS parent WITH (NOLOCK)
        ON parent.object_id = lnk.object_id
       AND parent.object_type = 'Group'
    INNER JOIN dbo.mms_metaverse AS child WITH (NOLOCK)
        ON child.object_id = lnk.reference_id
       AND child.object_type = 'Group'
    WHERE lnk.attribute_name = 'member'
),
Walk AS (
    SELECT
        e.ParentId AS RootId,
        e.ParentAccountName AS RootAccountName,
        e.ChildId,
        e.ChildAccountName,
        1 AS Depth,
        CAST(CONVERT(nvarchar(36), e.ParentId) + N'>' + CONVERT(nvarchar(36), e.ChildId) AS nvarchar(max)) AS IdPath,
        CAST(e.ParentAccountName + N' > ' + e.ChildAccountName AS nvarchar(max)) AS NamePath,
        CASE
            WHEN e.ParentId = e.ChildId THEN CAST(1 AS bit)
            ELSE CAST(0 AS bit)
        END AS Cycle
    FROM GroupEdges AS e

    UNION ALL

    SELECT
        w.RootId,
        w.RootAccountName,
        e.ChildId,
        e.ChildAccountName,
        w.Depth + 1,
        w.IdPath + N'>' + CONVERT(nvarchar(36), e.ChildId),
        w.NamePath + N' > ' + e.ChildAccountName,
        CASE
            WHEN CHARINDEX(CONVERT(nvarchar(36), e.ChildId), w.IdPath) > 0 THEN CAST(1 AS bit)
            ELSE CAST(0 AS bit)
        END
    FROM Walk AS w
    INNER JOIN GroupEdges AS e
        ON e.ParentId = w.ChildId
    WHERE w.Cycle = 0
      AND w.Depth < 25
)
SELECT
    w.Depth,
    w.Cycle,
    w.RootAccountName,
    w.NamePath,
    CASE
        WHEN w.Depth = 25 AND w.Cycle = 0 AND EXISTS (
            SELECT 1 FROM GroupEdges AS nxt WHERE nxt.ParentId = w.ChildId
        ) THEN 1
        ELSE 0
    END AS Truncated
FROM Walk AS w
WHERE (w.Depth >= 2 OR w.Cycle = 1)
  AND (
        w.Cycle = 1
        OR NOT EXISTS (
            SELECT 1 FROM GroupEdges AS nxt WHERE nxt.ParentId = w.ChildId
        )
      )
ORDER BY w.Cycle DESC, w.Depth DESC, w.NamePath
OPTION (MAXRECURSION 25);
