/*
    Vypis vsech skupin (Group), u kterych je vyplneny atribut Filter (kriteriove/dynamicke
    skupiny), vcetne AccountName a DisplayName.

    Filter muze byt v ruznych instalacich MIM ulozen jako String (fim.ObjectValueString)
    nebo Text (fim.ObjectValueText) - dotaz kontroluje obe tabulky, takze funguje v obou
    pripadech. Skupiny bez vyplneneho Filter (staticke skupiny) se nezobrazi, protoze
    v hodnotovych tabulkach pro ne zadny radek neexistuje.
*/

USE [FIMService];
SET TRANSACTION ISOLATION LEVEL READ UNCOMMITTED;
GO

DECLARE @groupTypeKey SMALLINT = [fim].[ObjectTypeKeyFromName](N'Group');
DECLARE @accountNameKey SMALLINT = [fim].[AttributeKeyFromName](N'AccountName');
DECLARE @displayNameKey SMALLINT = [fim].[AttributeKeyFromName](N'DisplayName');
DECLARE @filterKey SMALLINT = [fim].[AttributeKeyFromName](N'Filter');
DECLARE @localeKey SMALLINT = 127;   -- invariant

SELECT
    [o].[ObjectID]                        AS [ObjectID],
    [acc].[ValueString]                   AS [AccountName],
    [disp].[ValueString]                  AS [DisplayName],
    [filt].[FilterValue]                  AS [Filtr]
FROM [fim].[Objects] AS [o]
LEFT JOIN [fim].[ObjectValueString] AS [acc]
    ON      [acc].[ObjectKey] = [o].[ObjectKey]
        AND [acc].[AttributeKey] = @accountNameKey
        AND [acc].[LocaleKey] = @localeKey
LEFT JOIN [fim].[ObjectValueString] AS [disp]
    ON      [disp].[ObjectKey] = [o].[ObjectKey]
        AND [disp].[AttributeKey] = @displayNameKey
        AND [disp].[LocaleKey] = @localeKey
INNER JOIN
(
    -- Filter jako String
    SELECT [ObjectKey], CAST([ValueString] AS NVARCHAR(MAX)) AS [FilterValue]
    FROM [fim].[ObjectValueString]
    WHERE [AttributeKey] = @filterKey AND [LocaleKey] = @localeKey

    UNION ALL

    -- Filter jako Text (delsi XPath dotazy)
    SELECT [ObjectKey], [ValueText]
    FROM [fim].[ObjectValueText]
    WHERE [AttributeKey] = @filterKey AND [LocaleKey] = @localeKey
) AS [filt]
    ON [filt].[ObjectKey] = [o].[ObjectKey]
WHERE
        [o].[ObjectTypeKey] = @groupTypeKey
    AND LEN([filt].[FilterValue]) > 0
ORDER BY [disp].[ValueString];
GO
