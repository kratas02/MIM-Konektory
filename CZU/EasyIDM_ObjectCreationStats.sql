USE EasyIDM_Test
GO

-- Statistika vytvoreni objektu po hodinach v ramci dne
-- Pocet = pocet za danou hodinu
-- PrubeznyPocet = kumulativ od nejstarsi hodiny
SELECT
    CAST(Created AS date)                              AS Den,
    DATEPART(hour, Created)                            AS Hodina,
    DATEADD(hour, DATEDIFF(hour, 0, Created), 0)       AS DenHodina,
    COUNT(*)                                           AS Pocet,
    SUM(COUNT(*)) OVER (
        ORDER BY DATEADD(hour, DATEDIFF(hour, 0, Created), 0)
        ROWS UNBOUNDED PRECEDING
    )                                                  AS PrubeznyPocet
FROM dbo.IDMObjects
WHERE Created IS NOT NULL
GROUP BY
    CAST(Created AS date),
    DATEPART(hour, Created),
    DATEADD(hour, DATEDIFF(hour, 0, Created), 0)
ORDER BY DenHodina
GO
