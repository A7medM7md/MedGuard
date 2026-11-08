-- Throughput and end-to-end latency per 10-second window, straight from the data.
--
--   latency = CreatedAtUtc (worker built the row, a few ms before commit)
--           - RecordedAtUtc (the device sampled it)
--
-- That covers MQTT, bridge, Kafka, queueing in Kafka, and processing. Every
-- process runs on this machine, so the clocks agree.
--
-- How to read it:
--   rate/s flat at the offered load and p95 flat  -> keeping up
--   rate/s capped below the offered load          -> that cap IS the throughput
--   p95 climbing every window                     -> backlog growing (lag): latency = lag / rate

IF DB_NAME() <> 'MedGuardLoadTest'
    THROW 50000, 'Connect to MedGuardLoadTest.', 1;

-- sqlcmd defaults QUOTED_IDENTIFIER to OFF, and SQL Server rejects any write to a
-- table with a FILTERED index (SensorReadings.MessageId, OutboxMessages) unless it's
-- ON. Worse, sqlcmd still exits 0, so the script "succeeds" having done nothing.
SET QUOTED_IDENTIFIER ON;
SET NOCOUNT ON;

WITH r AS (
    SELECT DATEADD(second, DATEDIFF(second, '20260101', CreatedAtUtc) / 10 * 10, '20260101') AS WindowStart,
           DATEDIFF_BIG(millisecond, RecordedAtUtc, CreatedAtUtc) AS LatencyMs
    FROM SensorReadings
    WHERE MessageId IS NOT NULL
)
SELECT DISTINCT
    CONVERT(char(8), WindowStart, 108)                                                       AS [window],
    CAST(COUNT(*) OVER (PARTITION BY WindowStart) / 10.0 AS decimal(8,1))                    AS [rate/s],
    CAST(PERCENTILE_CONT(0.50) WITHIN GROUP (ORDER BY LatencyMs) OVER (PARTITION BY WindowStart) AS int) AS [p50 ms],
    CAST(PERCENTILE_CONT(0.95) WITHIN GROUP (ORDER BY LatencyMs) OVER (PARTITION BY WindowStart) AS int) AS [p95 ms],
    MAX(LatencyMs) OVER (PARTITION BY WindowStart)                                           AS [max ms]
FROM r
ORDER BY [window];

SELECT COUNT(*) AS readings,
       (SELECT COUNT(*) FROM Alerts) AS alerts,
       (SELECT COUNT(*) FROM OutboxMessages WHERE ProcessedAtUtc IS NULL) AS outbox_pending,
       (SELECT COUNT(*) FROM Batches WHERE Status = 3) AS quarantined
FROM SensorReadings;
