-- Predchozi verze delta importu. Aktualni je Clenstvi.sql (okno 24 hodin).
-- Full import: v_object + v_member, po uspechu EXEC dbo.ClearObjectDelta.
-- Delta import: EXEC dbo.BeginObjectDelta, pak import v_object_delta, po uspechu EXEC dbo.CommitObjectDelta.
-- Begin oznaci cekajici radky (InProgress = 1) a pohled vraci jen je.
-- Commit je po uspesnem importu smaze. Kdyz import spadne, Commit se nevola a pristi Begin je vezme znovu.
-- Radky vznikle behem importu zustanou na InProgress = 0.
-- Pri zakladani konektoru musi v_object vracet aspon jeden radek Person a jeden Group.

CREATE TABLE dbo.Object (
    GUID uniqueidentifier NOT NULL CONSTRAINT PK_Object PRIMARY KEY,
    objectType nvarchar(20) NOT NULL,
    sAMAccountName nvarchar(256) NULL,
    DisplayName nvarchar(256) NULL,
    extensionAttribute1 nvarchar(1024) NULL,
    extensionAttribute2 nvarchar(1024) NULL,
    extensionAttribute1Hash AS CONVERT(binary(32), HASHBYTES('SHA2_256', NULLIF(extensionAttribute1, N''))) PERSISTED,
    extensionAttribute2Hash AS CONVERT(binary(32), HASHBYTES('SHA2_256', NULLIF(extensionAttribute2, N''))) PERSISTED,
    CONSTRAINT CK_Object_objectType CHECK (objectType IN (N'Person', N'Group'))
);

CREATE INDEX IX_Object_Type_Ext1
    ON dbo.Object (objectType, extensionAttribute1Hash) INCLUDE (GUID);
CREATE INDEX IX_Object_Type_Ext2
    ON dbo.Object (objectType, extensionAttribute2Hash) INCLUDE (GUID);

CREATE TABLE dbo.ObjectDelta (
    DeltaId bigint IDENTITY(1,1) NOT NULL CONSTRAINT PK_ObjectDelta PRIMARY KEY,
    GUID uniqueidentifier NOT NULL,
    objectType nvarchar(20) NOT NULL,
    changeType nvarchar(10) NOT NULL,
    InProgress bit NOT NULL CONSTRAINT DF_ObjectDelta_InProgress DEFAULT (0),
    queuedAt datetime2(3) NOT NULL CONSTRAINT DF_ObjectDelta_queuedAt DEFAULT (SYSUTCDATETIME()),
    CONSTRAINT CK_ObjectDelta_changeType CHECK (changeType IN (N'Add', N'Modify', N'Delete'))
);

CREATE INDEX IX_ObjectDelta_InProgress
    ON dbo.ObjectDelta (InProgress, GUID, DeltaId) INCLUDE (objectType, changeType);
GO

CREATE VIEW dbo.v_object AS
SELECT GUID, objectType, sAMAccountName, DisplayName,
       extensionAttribute1, extensionAttribute2
FROM dbo.Object;
GO

CREATE VIEW dbo.v_member AS
SELECT g.GUID,
       CAST(N'member' AS nvarchar(64)) AS attributeName,
       p.GUID AS referenceId
FROM dbo.Object AS g
INNER JOIN dbo.Object AS p
    ON p.objectType = N'Person'
   AND p.extensionAttribute1Hash = g.extensionAttribute1Hash
   AND p.extensionAttribute1 = g.extensionAttribute1
WHERE g.objectType = N'Group'
  AND NULLIF(g.extensionAttribute1, N'') IS NOT NULL
  AND NULLIF(p.extensionAttribute1, N'') IS NOT NULL
UNION ALL
SELECT parent.GUID,
       CAST(N'member' AS nvarchar(64)),
       child.GUID
FROM dbo.Object AS parent
INNER JOIN dbo.Object AS child
    ON child.objectType = N'Group'
   AND child.extensionAttribute1Hash = parent.extensionAttribute2Hash
   AND child.extensionAttribute1 = parent.extensionAttribute2
WHERE parent.objectType = N'Group'
  AND NULLIF(parent.extensionAttribute2, N'') IS NOT NULL
  AND NULLIF(child.extensionAttribute1, N'') IS NOT NULL
  AND parent.GUID <> child.GUID;
GO

CREATE VIEW dbo.v_object_delta AS
WITH pending AS (
    SELECT DeltaId, GUID, objectType, changeType
    FROM dbo.ObjectDelta
    WHERE InProgress = 1
),
decision AS (
    SELECT p.GUID,
           COALESCE(o.objectType, p.objectType) AS objectType,
           CASE
               WHEN o.GUID IS NULL THEN N'Delete'
               WHEN EXISTS (
                   SELECT 1
                   FROM pending AS a
                   WHERE a.GUID = p.GUID
                     AND a.changeType = N'Add'
                     AND NOT EXISTS (
                         SELECT 1
                         FROM pending AS d
                         WHERE d.GUID = a.GUID
                           AND d.changeType = N'Delete'
                           AND d.DeltaId > a.DeltaId
                     )
               ) THEN N'Add'
               ELSE N'Modify'
           END AS changeType
    FROM pending AS p
    LEFT JOIN dbo.Object AS o ON o.GUID = p.GUID
    GROUP BY p.GUID, COALESCE(o.objectType, p.objectType), o.GUID
)
SELECT d.GUID, d.objectType, o.sAMAccountName, o.DisplayName,
       o.extensionAttribute1, o.extensionAttribute2, d.changeType
FROM decision AS d
LEFT JOIN dbo.Object AS o ON o.GUID = d.GUID;
GO

CREATE PROCEDURE dbo.BeginObjectDelta AS
BEGIN
    SET NOCOUNT ON;
    UPDATE dbo.ObjectDelta SET InProgress = 1 WHERE InProgress = 0;
END;
GO

CREATE PROCEDURE dbo.CommitObjectDelta AS
BEGIN
    SET NOCOUNT ON;
    DELETE FROM dbo.ObjectDelta WHERE InProgress = 1;
END;
GO

CREATE PROCEDURE dbo.ClearObjectDelta AS
BEGIN
    SET NOCOUNT ON;
    DELETE FROM dbo.ObjectDelta;
END;
GO

CREATE TRIGGER dbo.tr_Object_Delta
ON dbo.Object
AFTER INSERT, UPDATE, DELETE
AS
BEGIN
    SET NOCOUNT ON;

    IF EXISTS (
        SELECT 1
        FROM inserted AS i
        INNER JOIN deleted AS d ON d.GUID = i.GUID
        WHERE i.objectType <> d.objectType
    )
    BEGIN
        RAISERROR(N'objectType u existujiciho GUID nelze zmenit.', 16, 1);
        ROLLBACK TRANSACTION;
        RETURN;
    END;

    DECLARE @impact TABLE (GUID uniqueidentifier NOT NULL PRIMARY KEY);

    INSERT INTO dbo.ObjectDelta (GUID, objectType, changeType)
    SELECT i.GUID, i.objectType, N'Add'
    FROM inserted AS i
    WHERE NOT EXISTS (SELECT 1 FROM deleted AS d WHERE d.GUID = i.GUID);

    INSERT INTO dbo.ObjectDelta (GUID, objectType, changeType)
    SELECT d.GUID, d.objectType, N'Delete'
    FROM deleted AS d
    WHERE NOT EXISTS (SELECT 1 FROM inserted AS i WHERE i.GUID = d.GUID);

    INSERT INTO dbo.ObjectDelta (GUID, objectType, changeType)
    SELECT i.GUID, i.objectType, N'Modify'
    FROM inserted AS i
    INNER JOIN deleted AS d ON d.GUID = i.GUID
    WHERE EXISTS (SELECT NULLIF(i.sAMAccountName, N'') EXCEPT SELECT NULLIF(d.sAMAccountName, N''))
       OR EXISTS (SELECT NULLIF(i.DisplayName, N'') EXCEPT SELECT NULLIF(d.DisplayName, N''))
       OR EXISTS (SELECT NULLIF(i.extensionAttribute1, N'') EXCEPT SELECT NULLIF(d.extensionAttribute1, N''))
       OR EXISTS (SELECT NULLIF(i.extensionAttribute2, N'') EXCEPT SELECT NULLIF(d.extensionAttribute2, N''));

    INSERT INTO @impact (GUID)
    SELECT DISTINCT g.GUID
    FROM (
        SELECT NULLIF(i.extensionAttribute1, N'') AS ext1
        FROM inserted AS i
        WHERE i.objectType = N'Person'
          AND NOT EXISTS (SELECT 1 FROM deleted AS d WHERE d.GUID = i.GUID)
        UNION
        SELECT NULLIF(d.extensionAttribute1, N'')
        FROM deleted AS d
        WHERE d.objectType = N'Person'
          AND NOT EXISTS (SELECT 1 FROM inserted AS i WHERE i.GUID = d.GUID)
        UNION
        SELECT NULLIF(i.extensionAttribute1, N'')
        FROM inserted AS i
        INNER JOIN deleted AS d ON d.GUID = i.GUID
        WHERE i.objectType = N'Person'
          AND EXISTS (SELECT NULLIF(i.extensionAttribute1, N'') EXCEPT SELECT NULLIF(d.extensionAttribute1, N''))
        UNION
        SELECT NULLIF(d.extensionAttribute1, N'')
        FROM inserted AS i
        INNER JOIN deleted AS d ON d.GUID = i.GUID
        WHERE i.objectType = N'Person'
          AND EXISTS (SELECT NULLIF(i.extensionAttribute1, N'') EXCEPT SELECT NULLIF(d.extensionAttribute1, N''))
    ) AS v
    INNER JOIN dbo.Object AS g
        ON g.objectType = N'Group'
       AND g.extensionAttribute1Hash = CONVERT(binary(32), HASHBYTES('SHA2_256', v.ext1))
       AND g.extensionAttribute1 = v.ext1
    WHERE v.ext1 IS NOT NULL;

    INSERT INTO @impact (GUID)
    SELECT DISTINCT p.GUID
    FROM (
        SELECT i.GUID AS src, NULLIF(i.extensionAttribute1, N'') AS ext1
        FROM inserted AS i
        WHERE i.objectType = N'Group'
          AND NOT EXISTS (SELECT 1 FROM deleted AS d WHERE d.GUID = i.GUID)
        UNION
        SELECT d.GUID, NULLIF(d.extensionAttribute1, N'')
        FROM deleted AS d
        WHERE d.objectType = N'Group'
          AND NOT EXISTS (SELECT 1 FROM inserted AS i WHERE i.GUID = d.GUID)
        UNION
        SELECT i.GUID, NULLIF(i.extensionAttribute1, N'')
        FROM inserted AS i
        INNER JOIN deleted AS d ON d.GUID = i.GUID
        WHERE i.objectType = N'Group'
          AND EXISTS (SELECT NULLIF(i.extensionAttribute1, N'') EXCEPT SELECT NULLIF(d.extensionAttribute1, N''))
        UNION
        SELECT i.GUID, NULLIF(d.extensionAttribute1, N'')
        FROM inserted AS i
        INNER JOIN deleted AS d ON d.GUID = i.GUID
        WHERE i.objectType = N'Group'
          AND EXISTS (SELECT NULLIF(i.extensionAttribute1, N'') EXCEPT SELECT NULLIF(d.extensionAttribute1, N''))
    ) AS v
    INNER JOIN dbo.Object AS p
        ON p.objectType = N'Group'
       AND p.extensionAttribute2Hash = CONVERT(binary(32), HASHBYTES('SHA2_256', v.ext1))
       AND p.extensionAttribute2 = v.ext1
       AND p.GUID <> v.src
    WHERE v.ext1 IS NOT NULL
      AND NOT EXISTS (SELECT 1 FROM @impact AS i WHERE i.GUID = p.GUID);

    INSERT INTO dbo.ObjectDelta (GUID, objectType, changeType)
    SELECT o.GUID, o.objectType, N'Modify'
    FROM @impact AS i
    INNER JOIN dbo.Object AS o ON o.GUID = i.GUID;
END;
GO

-- Pouziti. Kazdy blok spustte samostatne, v tomto poradi.
-- Cely soubor pouzivejte jen pro zalozeni objektu. Bloky nize jsou zakomentovane,
-- aby se fronta nespustila sama pri zalozeni.

-- 1) Tesne pred Delta Import v Synchronization Service.
--    Oznaci cekajici radky: InProgress 0 -> 1.
--    v_object_delta vraci jen tyto radky. Bez tohoto kroku delta import nic nevidi.
-- EXEC dbo.BeginObjectDelta;

-- 2) V Synchronization Service spustte profil Delta Import.
--    Radky vznikle behem importu maji InProgress = 0 a tento beh je necte.

-- 3) Az kdyz Delta Import skonci uspesne. Smaze radky s InProgress = 1.
--    Kdyz import spadne, tento prikaz nespoustejte. Pristi Begin je vezme znovu.
-- EXEC dbo.CommitObjectDelta;

-- Po uspesnem Full Importu. Full import cte v_object, ne frontu.
-- Frontu zahodte, jinak ji pristi delta posle znovu.
-- EXEC dbo.ClearObjectDelta;
