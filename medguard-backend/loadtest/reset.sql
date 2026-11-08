-- Between runs: wipe the results, keep the batches, put them all back to Active,
-- so every experiment starts from the same state.

IF DB_NAME() <> 'MedGuardLoadTest'
    THROW 50000, 'Refusing to reset: this deletes data. Connect to MedGuardLoadTest.', 1;

-- sqlcmd defaults QUOTED_IDENTIFIER to OFF, and SQL Server rejects any write to a
-- table with a FILTERED index (SensorReadings.MessageId, OutboxMessages) unless it's
-- ON. Worse, sqlcmd still exits 0, so the script "succeeds" having done nothing.
SET QUOTED_IDENTIFIER ON;
SET NOCOUNT ON;
DELETE FROM Alerts;
DELETE FROM SensorReadings;
DELETE FROM OutboxMessages;
UPDATE Batches SET Status = 0, UpdatedAtUtc = NULL;
SELECT 'reset done' AS Result;
