-- Odebrani clenu: jsou v memberAD (import z AD) a uz nejsou v member (zdroj).
-- Stav po importu a synchronizaci, pred exportem.
-- GroupId je object_id skupiny v metaverzi.

USE FIMSynchronizationService;

SELECT
    g.object_id        AS GroupId,
    g.accountName      AS GroupAccountName,
    g.externalRoleCode AS GroupExternalRoleCode,
    ad.reference_id    AS MemberId,
    mv.accountName     AS AccountName,
    mv.displayName     AS DisplayName
FROM dbo.mms_mv_link AS ad WITH (NOLOCK)
INNER JOIN dbo.mms_metaverse AS g WITH (NOLOCK)
    ON g.object_id = ad.object_id
LEFT JOIN dbo.mms_metaverse AS mv WITH (NOLOCK)
    ON mv.object_id = ad.reference_id
WHERE ad.attribute_name = 'memberAD'
  AND NOT EXISTS (
      SELECT 1
      FROM dbo.mms_mv_link AS src WITH (NOLOCK)
      WHERE src.object_id = ad.object_id
        AND src.reference_id = ad.reference_id
        AND src.attribute_name = 'member'
  )
ORDER BY g.accountName, mv.accountName;
