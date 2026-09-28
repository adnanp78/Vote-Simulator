-- v_OpstineNivoi — općina × nivo (izvor za Ombre_Kombinacije)
-- Izmjena: p3_Combinations.Tip7012 = '7012' ne postoji u BMunicipalityRegion.Code,
-- pa je INNER JOIN izbacivao Predsjedništvo za FBiH. Kandidati su certificirani
-- odvojeno na regionima 701 (bošnjački član) i 702 (hrvatski član) → vraćamo oba.
-- Uslov '= 7012' (ne IS NOT NULL) — red Brčko 200 ima '*' u svim Tip kolonama.

ALTER VIEW v_OpstineNivoi AS
SELECT MunicipalityCode, MunicipalityName, Tip200 AS LevelCode FROM p3_Combinations WHERE Tip200 IS NOT NULL
UNION ALL SELECT MunicipalityCode, MunicipalityName, Tip300  FROM p3_Combinations WHERE Tip300  IS NOT NULL
UNION ALL SELECT MunicipalityCode, MunicipalityName, Tip3001 FROM p3_Combinations WHERE Tip3001 IS NOT NULL
UNION ALL SELECT MunicipalityCode, MunicipalityName, Tip510  FROM p3_Combinations WHERE Tip510  IS NOT NULL
UNION ALL SELECT MunicipalityCode, MunicipalityName, Tip520  FROM p3_Combinations WHERE Tip520  IS NOT NULL
UNION ALL SELECT MunicipalityCode, MunicipalityName, Tip600  FROM p3_Combinations WHERE Tip600  IS NOT NULL
UNION ALL SELECT MunicipalityCode, MunicipalityName, N'701'  FROM p3_Combinations WHERE Tip7012 = '7012'
UNION ALL SELECT MunicipalityCode, MunicipalityName, N'702'  FROM p3_Combinations WHERE Tip7012 = '7012'
UNION ALL SELECT MunicipalityCode, MunicipalityName, Tip703  FROM p3_Combinations WHERE Tip703  IS NOT NULL;
GO

-- Provjera: očekivano 69.254 reda; 701 → 87 općina / 348 redova, 702 → 87 / 261, 703 → 64 / 192
SELECT COUNT(*) AS ukupno FROM Ombre_Kombinacije;
SELECT LevelCode, COUNT(DISTINCT MunicipalityCode) AS opcina, COUNT(*) AS redova
FROM Ombre_Kombinacije WHERE LevelCode LIKE '70%' GROUP BY LevelCode ORDER BY LevelCode;
