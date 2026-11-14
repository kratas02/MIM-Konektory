-- Pridani clenu: jsou v member (zdroj) a jeste nejsou v memberAD (import z AD).
-- Stav po importu a synchronizaci, pred exportem.
-- GroupId je object_id skupiny v metaverzi.

USE FIMSynchronizationService;

SELECT
    g.object_id        AS GroupId,
    g.accountName      AS GroupAccountName,
    g.externalRoleCode AS GroupExternalRoleCode,
    src.reference_id   AS MemberId,
    mv.accountName     AS AccountName,
    mv.displayName     AS DisplayName
FROM dbo.mms_mv_link AS src WITH (NOLOCK)
INNER JOIN dbo.mms_metaverse AS g WITH (NOLOCK)
    ON g.object_id = src.object_id
LEFT JOIN dbo.mms_metaverse AS mv WITH (NOLOCK)
    ON mv.object_id = src.reference_id
WHERE src.attribute_name = 'member'
  AND NOT EXISTS (
      SELECT 1
      FROM dbo.mms_mv_link AS ad WITH (NOLOCK)
      WHERE ad.object_id = src.object_id
        AND ad.reference_id = src.reference_id
        AND ad.attribute_name = 'memberAD'
  )
ORDER BY g.accountName, mv.accountName;
