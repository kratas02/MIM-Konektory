USE [EasyIDM]
GO

-- Identity objekty (jmeno, TechName, IDMObjectId), ktere maji aktivne prirazenou
-- danou roli (Cast.RoleId) - platny, nearchivovany cast, jehoz platnost jeste nevyprsela.
SELECT
    io.[name]        AS [Name],
    io.[TechName],
    io.[IDMObjectId]
FROM [EasyIDM].[dbo].[Casts] AS c
INNER JOIN [EasyIDM].[dbo].[IDMObjects] AS io
    ON io.[IDMObjectId] = c.[IdentityId]
WHERE
        c.[RoleId] = 'c36393c5-b5d1-4dae-a8c4-a1cee0da5e1e'
    AND c.[IsValid] = 1
    AND c.[IsArchived] = 0
    AND (c.[ValidTo] > GETDATE() OR c.[ValidTo] IS NULL)
ORDER BY io.[name]
GO
