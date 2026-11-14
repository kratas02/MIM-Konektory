/*
    Diagnostika pro FimServiceDbMA - read-only konektor nad MIM Service databazi.

    Vsechny dotazy jen ctou. Spoustet v kontextu databaze FIMService, nejlepe na read-only
    replice nebo mimo peak, protoze [fim].[Objects] muze byt velka.

    Poradi odpovida tomu, co dela konektor:
      1) overeni pristupu a struktury
      2) co vsechno je k dispozici (object types + pocty objektu)
      3) jak bude vypadat schema MA
      4) kompletni vypis jednoho resource - presne to, co konektor posila do CS
      5) dostupne lokalizace
      6) delta import - co by konektor vzal za posledni hodinu
      7) delta import - jsou mazani dohledatelna v [fim].[RequestLog]?
*/

USE [FIMService];
SET TRANSACTION ISOLATION LEVEL READ UNCOMMITTED;
GO

/* ---------------------------------------------------------------------------
   1) Overeni, ze mame pristup k tabulkam, ktere konektor potrebuje
   --------------------------------------------------------------------------- */
SELECT [t].[name] AS [Tabulka],
       CASE WHEN HAS_PERMS_BY_NAME(N'fim.' + [t].[name], N'OBJECT', N'SELECT') = 1
            THEN N'OK' ELSE N'CHYBI PRAVO SELECT' END AS [Pravo]
FROM [sys].[tables] AS [t]
INNER JOIN [sys].[schemas] AS [s] ON [s].[schema_id] = [t].[schema_id]
WHERE [s].[name] = N'fim'
  AND [t].[name] IN (N'Objects', N'ObjectTypeInternal', N'AttributeInternal', N'BindingInternal',
                     N'ObjectValueString', N'ObjectValueText', N'ObjectValueXml', N'ObjectValueInteger',
                     N'ObjectValueBoolean', N'ObjectValueDateTime', N'ObjectValueIdentifier',
                     N'ObjectValueReference', N'ObjectValueBinary', N'LocaleInternal',
                     N'Request', N'RequestLog')
ORDER BY [t].[name];

/* ---------------------------------------------------------------------------
   2) Object types a pocty objektu = odhad velikosti full importu.
      Typy jako Request, RequestLog nebo WorkflowInstance mivaji miliony radku,
      do MA je vybirat jen kdyz je to opravdu potreba.
   --------------------------------------------------------------------------- */
SELECT [oti].[Name] AS [ObjectType],
       [oti].[Key]  AS [ObjectTypeKey],
       COUNT([o].[ObjectKey]) AS [PocetObjektu]
FROM [fim].[ObjectTypeInternal] AS [oti]
LEFT JOIN [fim].[Objects] AS [o] ON [o].[ObjectTypeKey] = [oti].[Key]
GROUP BY [oti].[Name], [oti].[Key]
ORDER BY COUNT([o].[ObjectKey]) DESC;

/* ---------------------------------------------------------------------------
   3) Schema, ktere konektor nabidne v MA (stejny dotaz jako FimDbMetadata.Load).
      DataType -> typ atributu v MIM:
        String, Text, Xml, Identifier, DateTime -> String
        Integer -> Integer, Boolean -> Boolean, Reference -> Reference, Binary -> Binary
      ObjectID se do schematu nedava, je to anchor.
   --------------------------------------------------------------------------- */
SELECT [oti].[Name] AS [ObjectType],
       [ai].[Name]  AS [Attribute],
       [ai].[DataType],
       [ai].[Multivalued]
FROM [fim].[BindingInternal] AS [bi]
INNER JOIN [fim].[ObjectTypeInternal] AS [oti] ON [oti].[Key] = [bi].[ObjectTypeKey]
INNER JOIN [fim].[AttributeInternal] AS [ai] ON [ai].[Key] = [bi].[AttributeKey]
-- WHERE [oti].[Name] = N'Person'
ORDER BY [oti].[Name], [ai].[Name];

/* ---------------------------------------------------------------------------
   4) Kompletni vypis jednoho resource pres vsechny hodnotove tabulky.
      Nastavit @objectId na ObjectID (GUID) z portalu.
   --------------------------------------------------------------------------- */
DECLARE @objectId UNIQUEIDENTIFIER = '00000000-0000-0000-0000-000000000000';
DECLARE @localeKey SMALLINT = 127;   -- 127 = invariant, tj. hodnoty pro synchronizaci

DECLARE @objectKey BIGINT;
SELECT @objectKey = [ObjectKey] FROM [fim].[Objects] WHERE [ObjectID] = @objectId;

IF @objectKey IS NULL
    PRINT 'Resource se zadanym ObjectID neexistuje - uprav @objectId.';
ELSE
    SELECT [ai].[Name] AS [Attribute], [ai].[DataType], [ai].[Multivalued], [x].[Seq], [x].[Value]
    FROM
    (
        SELECT [v].[AttributeKey], CAST([v].[ValueString] AS NVARCHAR(MAX)) AS [Value], CAST([v].[SequenceID] AS INT) AS [Seq]
        FROM [fim].[ObjectValueString] AS [v] WHERE [v].[ObjectKey] = @objectKey AND [v].[LocaleKey] = @localeKey
        UNION ALL
        SELECT [v].[AttributeKey], [v].[ValueText], CAST([v].[SequenceID] AS INT)
        FROM [fim].[ObjectValueText] AS [v] WHERE [v].[ObjectKey] = @objectKey AND [v].[LocaleKey] = @localeKey
        UNION ALL
        SELECT [v].[AttributeKey], CAST([v].[ValueXml] AS NVARCHAR(MAX)), CAST([v].[SequenceID] AS INT)
        FROM [fim].[ObjectValueXml] AS [v] WHERE [v].[ObjectKey] = @objectKey AND [v].[LocaleKey] = @localeKey
        UNION ALL
        SELECT [v].[AttributeKey], CAST([v].[ValueInteger] AS NVARCHAR(MAX)), CAST([v].[SequenceID] AS INT)
        FROM [fim].[ObjectValueInteger] AS [v] WHERE [v].[ObjectKey] = @objectKey
        UNION ALL
        SELECT [v].[AttributeKey], CASE WHEN [v].[ValueBoolean] = 1 THEN N'true' ELSE N'false' END, 0
        FROM [fim].[ObjectValueBoolean] AS [v] WHERE [v].[ObjectKey] = @objectKey
        UNION ALL
        SELECT [v].[AttributeKey], CONVERT(NVARCHAR(MAX), [v].[ValueDateTime], 126), CAST([v].[SequenceID] AS INT)
        FROM [fim].[ObjectValueDateTime] AS [v] WHERE [v].[ObjectKey] = @objectKey
        UNION ALL
        SELECT [v].[AttributeKey], CONVERT(NVARCHAR(MAX), [v].[ValueIdentifier]), 0
        FROM [fim].[ObjectValueIdentifier] AS [v] WHERE [v].[ObjectKey] = @objectKey
        UNION ALL
        SELECT [v].[AttributeKey], CONVERT(NVARCHAR(MAX), [tgt].[ObjectID]), 0
        FROM [fim].[ObjectValueReference] AS [v]
        INNER JOIN [fim].[Objects] AS [tgt] ON [tgt].[ObjectKey] = [v].[ValueReference]
        WHERE [v].[ObjectKey] = @objectKey
        UNION ALL
        SELECT [v].[AttributeKey], N'(binary, ' + CAST(DATALENGTH([v].[ValueBinary]) AS NVARCHAR(32)) + N' B)',
               CAST([v].[SequenceID] AS INT)
        FROM [fim].[ObjectValueBinary] AS [v] WHERE [v].[ObjectKey] = @objectKey
    ) AS [x]
    INNER JOIN [fim].[AttributeInternal] AS [ai] ON [ai].[Key] = [x].[AttributeKey]
    ORDER BY [ai].[Name], [x].[Seq];

/* ---------------------------------------------------------------------------
   5) Dostupne lokalizace. Konektor cte 127 (invariant), pokud neni parametr
      "Locale" nastaven na jinou hodnotu.
   --------------------------------------------------------------------------- */
SELECT [Key], [Name] FROM [fim].[LocaleInternal] ORDER BY [Key];
GO

/* ---------------------------------------------------------------------------
   6) Delta import: co by konektor vzal za zvolene okno.
      Stejne zdroje jako delta - commitnute requesty a log zmen hodnot.
      Necommitnute requesty (CommittedTime NULL) se ignoruji, prijdou az po commitu.
   --------------------------------------------------------------------------- */
DECLARE @fromUtc DATETIME = DATEADD(HOUR, -1, GETUTCDATE());
DECLARE @toUtc DATETIME = GETUTCDATE();

SELECT [r].[Operation],
       [r].[TargetObjectTypeName] AS [ObjectType],
       COUNT(DISTINCT [r].[TargetKey]) AS [PocetObjektu],
       COUNT(*) AS [PocetRequestu]
FROM [fim].[Request] AS [r]
WHERE [r].[CommittedTime] > @fromUtc
  AND [r].[CommittedTime] <= @toUtc
  AND [r].[TargetKey] IS NOT NULL
GROUP BY [r].[Operation], [r].[TargetObjectTypeName]
ORDER BY COUNT(*) DESC;

-- Objekty, ktere by delta znovu precetla (z [fim].[Request] i [fim].[RequestLog])
SELECT [oti].[Name] AS [ObjectType], COUNT(*) AS [PocetObjektu]
FROM
(
    SELECT DISTINCT [r].[TargetKey] AS [ObjectKey]
    FROM [fim].[Request] AS [r]
    WHERE [r].[CommittedTime] > @fromUtc AND [r].[CommittedTime] <= @toUtc AND [r].[TargetKey] IS NOT NULL
    UNION
    SELECT DISTINCT [l].[ObjectKey]
    FROM [fim].[RequestLog] AS [l]
    WHERE [l].[EventTime] > @fromUtc AND [l].[EventTime] <= @toUtc
) AS [changed]
INNER JOIN [fim].[Objects] AS [o] ON [o].[ObjectKey] = [changed].[ObjectKey]
INNER JOIN [fim].[ObjectTypeInternal] AS [oti] ON [oti].[Key] = [o].[ObjectTypeKey]
GROUP BY [oti].[Name]
ORDER BY COUNT(*) DESC;

/* ---------------------------------------------------------------------------
   7) Delta import a mazani: GUID smazaneho resource prezije jen v [fim].[RequestLog],
      kterou MIM Service plni pouze pro object types uvedene v resource
      Synchronization Filter. Kdyz sloupec [ZachyceniDeltou] hlasi mensi cislo nez
      [DeleteRequestu], zbytek dobere az Full Import.
   --------------------------------------------------------------------------- */
DECLARE @objectIdAttributeKey SMALLINT =
    (SELECT TOP (1) [Key] FROM [fim].[AttributeInternal] WHERE [Name] = N'ObjectID');

SELECT
    (
        SELECT COUNT(*)
        FROM [fim].[Request] AS [r]
        WHERE [r].[CommittedTime] > @fromUtc AND [r].[CommittedTime] <= @toUtc
          AND [r].[Operation] = N'Delete'
    ) AS [DeleteRequestu],
    (
        SELECT COUNT(DISTINCT [l].[ObjectID])
        FROM [fim].[RequestLog] AS [l]
        WHERE [l].[EventTime] > @fromUtc AND [l].[EventTime] <= @toUtc
          AND [l].[Added] = 0 AND [l].[AttributeKey] = @objectIdAttributeKey
    ) AS [ZachyceniDeltou];

-- Object types, pro ktere se [fim].[RequestLog] vubec plni (obsah Synchronization Filteru)
SELECT [target].[Name] AS [SynchronizeObjectType]
FROM [fim].[Objects] AS [filter]
INNER JOIN [fim].[ObjectTypeInternal] AS [filterType] ON [filterType].[Key] = [filter].[ObjectTypeKey]
INNER JOIN [fim].[ObjectValueReference] AS [ref] ON [ref].[ObjectKey] = [filter].[ObjectKey]
INNER JOIN [fim].[AttributeInternal] AS [attr] ON [attr].[Key] = [ref].[AttributeKey]
INNER JOIN [fim].[ObjectTypeInternal] AS [target] ON [target].[ObjectKey] = [ref].[ValueReference]
WHERE [filterType].[Name] = N'SynchronizationFilter'
  AND [attr].[Name] = N'SynchronizeObjectType'
ORDER BY [target].[Name];
GO

/* ---------------------------------------------------------------------------
   8) Objekty bez hodnot atributu. V [fim].[Objects] maji radek, ale zadnou
      hodnotu (krome pripadneho ObjectID). Portal je nezna a konektor je neimportuje.
      Na velke databazi muze dotaz chvili trvat.
   --------------------------------------------------------------------------- */
DECLARE @objectIdKey SMALLINT =
    (SELECT TOP (1) [Key] FROM [fim].[AttributeInternal] WHERE [Name] = N'ObjectID');

SELECT [oti].[Name] AS [ObjectType], COUNT(*) AS [BezAtributu]
FROM [fim].[Objects] AS [o]
INNER JOIN [fim].[ObjectTypeInternal] AS [oti] ON [oti].[Key] = [o].[ObjectTypeKey]
WHERE NOT EXISTS (SELECT 1 FROM [fim].[ObjectValueString] AS [v]
                  WHERE [v].[ObjectKey] = [o].[ObjectKey] AND [v].[AttributeKey] <> @objectIdKey AND [v].[ValueString] <> N'')
  AND NOT EXISTS (SELECT 1 FROM [fim].[ObjectValueText] AS [v]
                  WHERE [v].[ObjectKey] = [o].[ObjectKey] AND [v].[ValueText] <> N'')
  AND NOT EXISTS (SELECT 1 FROM [fim].[ObjectValueXml] AS [v]
                  WHERE [v].[ObjectKey] = [o].[ObjectKey] AND DATALENGTH([v].[ValueXml]) > 0)
  AND NOT EXISTS (SELECT 1 FROM [fim].[ObjectValueInteger] AS [v] WHERE [v].[ObjectKey] = [o].[ObjectKey])
  AND NOT EXISTS (SELECT 1 FROM [fim].[ObjectValueBoolean] AS [v] WHERE [v].[ObjectKey] = [o].[ObjectKey])
  AND NOT EXISTS (SELECT 1 FROM [fim].[ObjectValueDateTime] AS [v] WHERE [v].[ObjectKey] = [o].[ObjectKey])
  AND NOT EXISTS (SELECT 1 FROM [fim].[ObjectValueIdentifier] AS [v]
                  WHERE [v].[ObjectKey] = [o].[ObjectKey] AND [v].[AttributeKey] <> @objectIdKey)
  AND NOT EXISTS (SELECT 1 FROM [fim].[ObjectValueReference] AS [v]
                  INNER JOIN [fim].[Objects] AS [tgt] ON [tgt].[ObjectKey] = [v].[ValueReference]
                  WHERE [v].[ObjectKey] = [o].[ObjectKey])
  AND NOT EXISTS (SELECT 1 FROM [fim].[ObjectValueBinary] AS [v] WHERE [v].[ObjectKey] = [o].[ObjectKey])
GROUP BY [oti].[Name]
ORDER BY COUNT(*) DESC;
GO
