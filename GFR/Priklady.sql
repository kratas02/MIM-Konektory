-- 5 skupin, 20 uzivatelu, 2 podrizene skupiny.
-- Clenstvi: osoba.extensionAttribute1 = skupina.extensionAttribute1.
-- Podrizenost: podrizena.extensionAttribute1 = nadrizena.extensionAttribute2.
-- A  (uzivatele 01-04) obsahuje podřízenou A1 (uzivatele 05-08)
-- B  (uzivatele 09-12) obsahuje podřízenou B1 (uzivatele 13-16)
-- C  (uzivatele 17-20) nema podřízenou skupinu

DELETE FROM dbo.Object
WHERE sAMAccountName LIKE N'GFR-%';
GO

INSERT INTO dbo.Object (GUID, objectType, sAMAccountName, DisplayName, extensionAttribute1, extensionAttribute2)
VALUES
    (NEWID(), N'Group', N'GFR-G-A',  N'Skupina A',  N'A',  N'A1'),
    (NEWID(), N'Group', N'GFR-G-A1', N'Skupina A1', N'A1', NULL),
    (NEWID(), N'Group', N'GFR-G-B',  N'Skupina B',  N'B',  N'B1'),
    (NEWID(), N'Group', N'GFR-G-B1', N'Skupina B1', N'B1', NULL),
    (NEWID(), N'Group', N'GFR-G-C',  N'Skupina C',  N'C',  NULL);

INSERT INTO dbo.Object (GUID, objectType, sAMAccountName, DisplayName, extensionAttribute1)
VALUES
    (NEWID(), N'Person', N'GFR-U01', N'Uzivatel 01', N'A'),
    (NEWID(), N'Person', N'GFR-U02', N'Uzivatel 02', N'A'),
    (NEWID(), N'Person', N'GFR-U03', N'Uzivatel 03', N'A'),
    (NEWID(), N'Person', N'GFR-U04', N'Uzivatel 04', N'A'),
    (NEWID(), N'Person', N'GFR-U05', N'Uzivatel 05', N'A1'),
    (NEWID(), N'Person', N'GFR-U06', N'Uzivatel 06', N'A1'),
    (NEWID(), N'Person', N'GFR-U07', N'Uzivatel 07', N'A1'),
    (NEWID(), N'Person', N'GFR-U08', N'Uzivatel 08', N'A1'),
    (NEWID(), N'Person', N'GFR-U09', N'Uzivatel 09', N'B'),
    (NEWID(), N'Person', N'GFR-U10', N'Uzivatel 10', N'B'),
    (NEWID(), N'Person', N'GFR-U11', N'Uzivatel 11', N'B'),
    (NEWID(), N'Person', N'GFR-U12', N'Uzivatel 12', N'B'),
    (NEWID(), N'Person', N'GFR-U13', N'Uzivatel 13', N'B1'),
    (NEWID(), N'Person', N'GFR-U14', N'Uzivatel 14', N'B1'),
    (NEWID(), N'Person', N'GFR-U15', N'Uzivatel 15', N'B1'),
    (NEWID(), N'Person', N'GFR-U16', N'Uzivatel 16', N'B1'),
    (NEWID(), N'Person', N'GFR-U17', N'Uzivatel 17', N'C'),
    (NEWID(), N'Person', N'GFR-U18', N'Uzivatel 18', N'C'),
    (NEWID(), N'Person', N'GFR-U19', N'Uzivatel 19', N'C'),
    (NEWID(), N'Person', N'GFR-U20', N'Uzivatel 20', N'C');
GO

SELECT g.sAMAccountName AS Skupina,
       o.objectType,
       o.sAMAccountName AS Clen
FROM dbo.v_member AS m
INNER JOIN dbo.Object AS g ON g.GUID = m.GUID
INNER JOIN dbo.Object AS o ON o.GUID = m.referenceId
WHERE g.sAMAccountName LIKE N'GFR-%'
ORDER BY g.sAMAccountName, o.objectType, o.sAMAccountName;
