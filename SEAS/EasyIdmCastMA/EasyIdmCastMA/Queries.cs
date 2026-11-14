namespace EasyIdmCastMA
{
    /// <summary>
    /// SQL nad EasyIDM. Full Import je puvodni dotaz (validni SAP casty),
    /// RoleCategory je jedna radka na roli (RoleAccesses muze roli zdvojit).
    ///
    /// Validni cast:
    ///   IsArchived = 0 AND (ValidTo > GETDATE() OR ValidTo IS NULL)
    ///   AND category IS NOT NULL AND Role.TechName NOT LIKE 'RDPV_AUTBASE%'
    ///
    /// Duplicita = stejny IdentityTechName + RoleTechName. Z validnich castu
    /// se bere jen novejsi: vyssi ValidFrom, pri shode vyssi CastId.
    ///
    /// Smazany cast: IsArchived = 1 AND IsValid = 0
    /// </summary>
    internal static class Queries
    {
        /// <summary>
        /// Kategorie SAP role z JSON. Stejne cesty jako ve stávající Full Import query.
        /// </summary>
        public const string RoleCategoryCte = @"
RoleSource AS (
    SELECT
        i.IDMObjectId,
        JSON_VALUE(rs.JsonData, '$.applicationModulCode') AS applicationModulCode,
        JSON_VALUE(rsApp.JsonData, '$.applicationCodeAP') AS applicationCode
    FROM dbo.IDMObjects AS i
    LEFT OUTER JOIN dbo.IDMObjectReadStore AS rs
        ON i.IDMObjectId = rs.IdmObjectId
    LEFT OUTER JOIN dbo.RoleAccesses AS ra
        ON ra.ApplicationRoleId = i.IDMObjectId
    LEFT OUTER JOIN dbo.IDMObjectReadStore AS rsApp
        ON ra.ResourceId = rsApp.IdmObjectId
    WHERE i.IDMObjectTypeId = 6
),
RoleCategoryRaw AS (
    SELECT
        IDMObjectId,
        applicationCode,
        applicationModulCode,
        CASE
            WHEN applicationCode LIKE 'SAP% A' THEN 'SAP_BAPI_ARCH'
            WHEN applicationCode LIKE 'SAP%'
             AND applicationCode NOT LIKE '% A'
             AND (applicationModulCode NOT LIKE 'RDPV%' OR applicationModulCode IS NULL) THEN 'SAP_BAPI_PS4'
            WHEN applicationCode LIKE 'SAP%'
             AND applicationCode NOT LIKE '% A'
             AND (applicationModulCode LIKE 'RDPV%') THEN 'SAP_BTP'
            ELSE NULL
        END AS category
    FROM RoleSource
),
RoleCategory AS (
    SELECT IDMObjectId, applicationCode, applicationModulCode, category
    FROM (
        SELECT
            IDMObjectId, applicationCode, applicationModulCode, category,
            ROW_NUMBER() OVER (
                PARTITION BY IDMObjectId
                ORDER BY CASE WHEN category IS NOT NULL THEN 0 ELSE 1 END, applicationCode
            ) AS rn
        FROM RoleCategoryRaw
    ) AS ranked
    WHERE rn = 1
)";

        /// <summary>Stejne filtry jako puvodni Full Import (bez duplicitni AND category IS NOT NULL).</summary>
        public const string ValidCastWhere = @"
    c.IsArchived = 0
    AND (c.ValidTo > GETDATE() OR c.ValidTo IS NULL)
    AND y.category IS NOT NULL
    AND r.TechName NOT LIKE 'RDPV_AUTBASE%'";

        /// <summary>
        /// Vsechny validni casty s poradim duplicity IdentityTechName + RoleTechName.
        /// dupRn = 1 je novejsi zaznam (ValidFrom DESC, pak CastId DESC).
        /// </summary>
        public const string ValidCastRankedCte = @"
ValidCastRanked AS (
    SELECT
        c.CastId,
        y.applicationCode,
        y.applicationModulCode,
        i.IDMObjectId,
        i.IsValid AS isValidUser,
        r.IsValid AS IsValidRole,
        i.TechName AS IdentityTechName,
        i.Name AS IdentityName,
        r.IDMObjectId AS RoleId,
        r.TechName AS RoleTechName,
        r.Name AS RoleName,
        y.category,
        c.IsArchived,
        c.IsValid AS isValidCast,
        c.ValidFrom,
        c.ValidTo,
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
    WHERE " + ValidCastWhere + @"
)";

        public const string RankedSelectList = @"
    CastId,
    applicationCode,
    applicationModulCode,
    IDMObjectId,
    isValidUser,
    IsValidRole,
    IdentityTechName,
    IdentityName,
    RoleId,
    RoleTechName,
    RoleName,
    category,
    IsArchived,
    isValidCast,
    ValidFrom,
    ValidTo";

        public static readonly string FullImportPage = @"
WITH " + RoleCategoryCte + @",
" + ValidCastRankedCte + @"
SELECT " + RankedSelectList + @"
FROM ValidCastRanked
WHERE dupRn = 1
  AND CastId > @lastCastId
ORDER BY CastId
OFFSET 0 ROWS FETCH NEXT @pageSize ROWS ONLY;";

        public static readonly string CurrentByCastIds = @"
WITH " + RoleCategoryCte + @",
" + ValidCastRankedCte + @"
SELECT " + RankedSelectList + @"
FROM ValidCastRanked
WHERE dupRn = 1
  AND CastId IN (SELECT CAST(value AS int) FROM STRING_SPLIT(@castIds, ','));";

        /// <summary>
        /// Candidati delta: zmena castu, identity, role, RoleAccesses,
        /// vyprseni ValidTo, nebo smazany cast (IsArchived=1, IsValid=0).
        /// Ke kazde zmene se pridaji i ostatni validni casty stejne
        /// IdentityTechName + RoleTechName, aby starsi duplicita odesla jako Delete.
        /// </summary>
        public const string DeltaCandidateIds = @"
WITH Changed AS (
    SELECT DISTINCT
        c.CastId,
        i.TechName AS IdentityTechName,
        r.TechName AS RoleTechName
    FROM dbo.Casts AS c
    INNER JOIN dbo.IDMObjects AS i ON i.IDMObjectId = c.IdentityId
    INNER JOIN dbo.IDMObjects AS r ON r.IDMObjectId = c.RoleId
    LEFT OUTER JOIN dbo.RoleAccesses AS ra ON ra.ApplicationRoleId = c.RoleId
    WHERE r.IDMObjectTypeId = 6
      AND (r.TechName IS NULL OR r.TechName NOT LIKE 'RDPV_AUTBASE%')
      AND (
            c.Created >= @fromTime OR ISNULL(c.Modified, c.Created) >= @fromTime
         OR i.Created >= @fromTime OR ISNULL(i.Modified, i.Created) >= @fromTime
         OR r.Created >= @fromTime OR ISNULL(r.Modified, r.Created) >= @fromTime
         OR ra.Created >= @fromTime OR ISNULL(ra.Modified, ra.Created) >= @fromTime
         OR (c.ValidTo IS NOT NULL AND c.ValidTo > @fromTime AND c.ValidTo <= GETDATE())
         OR (c.IsArchived = 1 AND c.IsValid = 0 AND (c.Created >= @fromTime OR ISNULL(c.Modified, c.Created) >= @fromTime))
      )
),
" + RoleCategoryCte + @",
" + ValidCastRankedCte + @"
SELECT DISTINCT TOP (@maxRows) x.CastId
FROM (
    SELECT ch.CastId
    FROM Changed AS ch
    UNION
    SELECT v.CastId
    FROM ValidCastRanked AS v
    INNER JOIN Changed AS ch
        ON ch.IdentityTechName = v.IdentityTechName
       AND ch.RoleTechName = v.RoleTechName
) AS x
ORDER BY x.CastId;";

        public const string ServerNow = "SELECT SYSDATETIME();";

        public const string VerifyObjects = @"
SELECT
    OBJECT_ID('dbo.IDMObjects') AS IDMObjects,
    OBJECT_ID('dbo.Casts') AS Casts,
    OBJECT_ID('dbo.IDMObjectReadStore') AS IDMObjectReadStore,
    OBJECT_ID('dbo.RoleAccesses') AS RoleAccesses;";
    }
}
