#!/usr/bin/env bash
# Drain test (ADR 0008): measures the monitoring workers' MAX throughput.
#
#   1. put COUNT readings on sensor-readings (KafkaLab fill-readings, no MQTT)
#   2. start WORKERS workers with MaxBatchSize=BATCH
#   3. throughput = readings committed / (last commit - first commit), from SQL
#
# Needs only SQL Server + Kafka running (no HiveMQ / bridge / simulator), so it
# fits on a laptop. Build Release first:  dotnet build MedGuard.sln -c Release
#
# usage (Git Bash, from medguard-backend/):
#   SA_PASSWORD=... loadtest/drain-test.sh <workers> <batch-size> [count=20000] [batches=1000]
set -euo pipefail
export MSYS_NO_PATHCONV=1
WORKERS=$1; BATCH=$2; COUNT=${3:-20000}; BATCHES=${4:-1000}
: "${SA_PASSWORD:?set SA_PASSWORD}"
ROOT=$(cd "$(dirname "$0")/.." && pwd)
LOGS=${LOGS:-$ROOT/loadtest/logs}; mkdir -p "$LOGS"
CS="Server=localhost,1433;Database=MedGuardLoadTest;User Id=sa;Password=$SA_PASSWORD;TrustServerCertificate=True;"
SQL() { docker exec -i dev-sqlserver /opt/mssql-tools18/bin/sqlcmd -S localhost -U sa -P "$SA_PASSWORD" -C -b -d MedGuardLoadTest -W -h -1 "$@"; }  # -b: SQL errors fail the script
KG()  { docker exec medguard-kafka /opt/kafka/bin/kafka-consumer-groups.sh --bootstrap-server localhost:9092 "$@"; }
stop_workers() {
  powershell.exe -NoProfile -Command "Get-CimInstance Win32_Process -Filter \"Name='dotnet.exe'\" | Where-Object { \$_.CommandLine -match 'MedGuard\.Monitoring\.Worker\.dll' } | ForEach-Object { Stop-Process -Id \$_.ProcessId -Force }" >/dev/null 2>&1 || true
}
trap stop_workers EXIT

# A paging laptop measures its disk, not the pipeline.
AVAIL=$(powershell.exe -NoProfile -Command "[int](Get-Counter '\Memory\Available MBytes').CounterSamples[0].CookedValue" | tr -d '\r')
MIN_AVAIL_MB=${MIN_AVAIL_MB:-700}
if [ "$AVAIL" -lt "$MIN_AVAIL_MB" ]; then echo "only ${AVAIL}MB RAM available: close something first (results would be distorted)"; exit 2; fi

SQL -i /dev/stdin < "$ROOT/loadtest/reset.sql" >/dev/null
# Move the group past everything already in the topic. Right after Kafka starts
# this fails with "Coordinator load in progress", and a silent failure means
# draining some OLD backlog instead of ours. So retry, and fail loudly.
for attempt in $(seq 1 20); do
  if KG --group monitoring --reset-offsets --to-latest --topic sensor-readings --execute 2>&1 | grep -q "NEW-OFFSET"; then break; fi
  [ "$attempt" = 20 ] && { echo "could not reset offsets for group 'monitoring' (still active members?)"; exit 3; }
  sleep 3
done

# Workstation GC: Server GC reserves a heap per core (12 here), which costs
# hundreds of MB per process. That's fine on a server, but not with 8 workers on 8 GB.
export DOTNET_gcServer=0
for i in $(seq 1 "$WORKERS"); do
  (cd "$ROOT/src/MedGuard.Monitoring.Worker/bin/Release/net8.0" && exec dotnet MedGuard.Monitoring.Worker.dll \
     --Monitoring:InstanceId="$i" --Monitoring:SessionTimeoutMs=10000 --Monitoring:MaxBatchSize="$BATCH" \
     --Outbox:Enabled=false "--ConnectionStrings:MedGuardDb=$CS") > "$LOGS/worker$i.log" 2>&1 &
done

# Workers FIRST, then the data. Every member that joins triggers a rebalance;
# if they join mid-drain, those pauses land inside the measurement (the first
# version of this script did that, and 3 workers came out slower than 2).
# Wait until all members are in and the assignment has been stable for 2 checks.
prev=""; stable=0
for _ in $(seq 1 30); do
  sleep 2
  cur=$(KG --describe --group monitoring --members 2>/dev/null | awk '$1=="monitoring"{print $NF}' | sort | tr '\n' ',')
  members=$(KG --describe --group monitoring --members 2>/dev/null | awk '$1=="monitoring"' | wc -l)
  if [ "$members" -ge "$WORKERS" ] && [ "$cur" = "$prev" ]; then stable=$((stable+1)); [ $stable -ge 2 ] && break; else stable=0; fi
  prev=$cur
done

(cd "$ROOT/src/MedGuard.KafkaLab/bin/Release/net8.0" && dotnet MedGuard.KafkaLab.dll fill-readings "$COUNT" "batches=$BATCHES")

# Wait until everything is committed (or progress stops for 30s).
last=-1; still=0
while :; do
  sleep 3
  n=$(SQL -Q "SET NOCOUNT ON; SELECT COUNT(*) FROM SensorReadings" | tr -d '[:space:]')
  # One snapshot mid-drain: which partitions still have work, and who owns them.
  if [ -z "${SNAP:-}" ] && [ "$n" -gt 0 ]; then SNAP=1; KG --describe --group monitoring > "$LOGS/describe.txt" 2>&1 || true; fi
  [ "$n" -ge "$COUNT" ] && break
  if [ "$n" = "$last" ]; then still=$((still+3)); [ $still -ge 30 ] && { echo "stalled at $n"; break; }; else still=0; fi
  last=$n
done

stop_workers; trap - EXIT
RESULT=$(SQL -Q "SET NOCOUNT ON; SELECT COUNT(*), DATEDIFF_BIG(millisecond, MIN(CreatedAtUtc), MAX(CreatedAtUtc)) FROM SensorReadings")
read -r N MS <<<"$RESULT"
# busy = partitions that had a backlog mid-drain; active = members owning at least one of them.
BUSY=$(awk '$1=="monitoring" && $6+0>0 {b++; o[$7]=1} END {n=0; for (k in o) n++; printf "busy-partitions=%d active-workers=%d", b, n}' "$LOGS/describe.txt" 2>/dev/null)
END_AVAIL=$(powershell.exe -NoProfile -Command "[int](Get-Counter '\Memory\Available MBytes').CounterSamples[0].CookedValue" | tr -d '\r')
awk -v ram="RAM ${AVAIL}->${END_AVAIL}MB" -v w="$WORKERS" -v b="$BATCH" -v k="$BATCHES" -v n="$N" -v ms="$MS" -v busy="$BUSY" \
  'BEGIN { printf "workers=%d batch=%d keys=%d recorded=%d in %.1fs => %.0f readings/s   %s   %s\n", w, b, k, n, ms/1000, n/(ms/1000), busy, ram }'

# Let the killed members' sessions expire before the next run joins the group.
sleep 11
