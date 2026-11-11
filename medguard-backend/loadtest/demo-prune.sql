-- Retention for a long-running demo fleet (DeviceSimulator `demo` mode) on a small
-- hosted database: run it every hour or so, e.g.
--   sqlcmd -S <server> -d <db> -U <user> -P <password> -N -C -i demo-prune.sql
--
-- Keeps 48 hours of readings, and every reading an alert points at, however old
-- (Alerts.SensorReadingId has no cascade, and the alert log needs its reading).
-- Deletes in chunks so the transaction log of a small database never balloons.
SET NOCOUNT ON;
DECLARE @cutoff datetime2 = DATEADD(HOUR, -48, SYSUTCDATETIME());
DECLARE @deleted int = 1, @total int = 0;

WHILE @deleted > 0
BEGIN
    DELETE TOP (5000) r
    FROM SensorReadings r
    WHERE r.RecordedAtUtc < @cutoff
      AND NOT EXISTS (SELECT 1 FROM Alerts a WHERE a.SensorReadingId = r.Id);
    SET @deleted = @@ROWCOUNT;
    SET @total += @deleted;
END

-- Without a Kafka dispatcher (runasp.net, ADR 0001) nothing ever sends these.
DELETE FROM OutboxMessages WHERE OccurredAtUtc < DATEADD(DAY, -2, SYSUTCDATETIME());

SELECT @total AS ReadingsDeleted, @@ROWCOUNT AS OutboxDeleted,
       (SELECT COUNT(*) FROM SensorReadings) AS ReadingsLeft;
