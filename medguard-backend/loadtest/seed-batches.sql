-- Creates one batch per simulator seed SIM-0001..SIM-$(Count), with the SAME
-- ids the simulator derives (MD5 of the seed read as a GUID; see
-- DeviceSimulator/Program.cs StableGuid). Idempotent: existing batches are skipped.
--
--   docker exec dev-sqlserver /opt/mssql-tools18/bin/sqlcmd -S localhost -U sa -P <pwd> -C \
--     -d MedGuardLoadTest -v Count=1000 -i /dev/stdin < loadtest/seed-batches.sql
--
-- Run it against the load-test database only (see the guard below).

IF DB_NAME() <> 'MedGuardLoadTest'
    THROW 50000, 'Refusing to seed: connect to MedGuardLoadTest, not your dev database.', 1;

-- sqlcmd defaults QUOTED_IDENTIFIER to OFF, and SQL Server rejects any write to a
-- table with a FILTERED index (SensorReadings.MessageId, OutboxMessages) unless it's
-- ON. Worse, sqlcmd still exits 0, so the script "succeeds" having done nothing.
SET QUOTED_IDENTIFIER ON;
SET NOCOUNT ON;
DECLARE @Count int = $(Count);

WITH n AS (
    SELECT TOP (@Count) ROW_NUMBER() OVER (ORDER BY (SELECT NULL)) AS i
    FROM sys.all_objects a CROSS JOIN sys.all_objects b
),
seeds AS (
    -- varchar, not nvarchar: the simulator hashes UTF-8 bytes, and for ASCII
    -- that's the same as varchar. nvarchar would hash UTF-16 and never match.
    SELECT CAST('SIM-' + RIGHT('0000' + CAST(i AS varchar(4)), 4) AS varchar(20)) AS seed FROM n
)
INSERT INTO Batches (Id, BatchNumber, DrugName, ManufacturerName, QuantityUnits,
                     ManufacturedDateUtc, ExpiryDateUtc, MinSafeTemperatureC, MaxSafeTemperatureC,
                     Status, CreatedAtUtc)
SELECT CAST(CAST(HASHBYTES('MD5', s.seed) AS binary(16)) AS uniqueidentifier),
       'LT-' + s.seed, 'Load-test vaccine', 'MedGuard Load Lab', 1000,
       DATEADD(month, -1, SYSUTCDATETIME()), DATEADD(year, 1, SYSUTCDATETIME()), 2.0, 8.0,
       0, SYSUTCDATETIME()
FROM seeds s
WHERE NOT EXISTS (SELECT 1 FROM Batches b
                  WHERE b.Id = CAST(CAST(HASHBYTES('MD5', s.seed) AS binary(16)) AS uniqueidentifier));

SELECT COUNT(*) AS LoadTestBatches FROM Batches WHERE BatchNumber LIKE 'LT-SIM-%';
