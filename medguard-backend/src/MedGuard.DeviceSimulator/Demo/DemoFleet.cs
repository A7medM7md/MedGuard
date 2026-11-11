using System.Collections.Concurrent;
using static MedGuard.DeviceSimulator.Demo.DemoCatalog;

namespace MedGuard.DeviceSimulator.Demo;

/// <summary>
/// A demo fleet that lives entirely in a deployed MedGuard: sensors post readings and
/// heartbeats over HTTP, trucks deliver and start new trips, and a simulated QA operator
/// works the alert queue. Everything goes through the public API, so every reading meets
/// the same domain rules (alerts, auto-quarantine) as a real sensor's.
///
/// Stateless across restarts: the fleet is rebuilt from the API on start (devices,
/// their batches, in-transit shipments), so stopping and starting just continues.
/// </summary>
public sealed class DemoFleet(MedGuardApi api, DemoOptions options)
{
    private readonly Random _random = new();
    private readonly ConcurrentDictionary<string, Sensor> _sensors = new();
    private decimal _warningMarginPercent = 15m;
    private long _readings, _failures, _outOfRange;

    public async Task RunAsync(bool seedOnly, CancellationToken ct)
    {
        _warningMarginPercent = (await api.GetAsync<SettingsInfo>("settings", ct)).WarningMarginPercent;
        await SeedAsync(ct);
        if (seedOnly) return;

        await LoadFleetAsync(ct);
        Log.Summary($"{_sensors.Count} sensor(s) -> {options.ApiBaseUrl}, a reading every {options.IntervalSeconds}s each. Ctrl+C to stop.");
        foreach (var s in _sensors.Values.OrderBy(s => s.Code))
            Log.Summary($"  {s.Code,-11} {s.Trip?.Batch.BatchNumber ?? "(idle)",-14} {Describe(s.Trip)}");

        if (options.KickoffExcursion)
            KickoffExcursion();

        var loops = _sensors.Values.Select(s => RunSensorAsync(s, ct)).ToList();
        loops.Add(RunLifecycleAsync(ct));
        loops.Add(ReportAsync(ct));
        await Task.WhenAll(loops);
    }

    // ---------------------------------------------------------------- seeding

    /// <summary>Registers every catalog sensor that doesn't exist yet, with its batch (and trip).</summary>
    private async Task SeedAsync(CancellationToken ct)
    {
        var existing = (await api.GetAsync<List<DeviceInfo>>("devices", ct)).Select(d => d.DeviceCode).ToHashSet();
        var batches = await api.GetAsync<List<BatchInfo>>("batches", ct);
        var created = 0;

        foreach (var entry in Fleet.Where(f => !existing.Contains(f.DeviceCode)))
        {
            var device = await api.PostAsync<DeviceInfo>("devices", new { deviceCode = entry.DeviceCode, model = entry.Model }, ct);
            var batch = await CreateBatchAsync(ProductByCode(entry.ProductCode), batches, ct);
            await api.PostAsync<DeviceInfo>($"devices/{device.Id}/assign/{batch.Id}", null, ct);
            if (entry.To is not null)
                await api.PostAsync<ShipmentInfo>("shipments", new { batchId = batch.Id, originLocation = entry.At, destinationLocation = entry.To, courierName = entry.Courier }, ct);
            Log.Summary($"seeded {entry.DeviceCode} -> {batch.BatchNumber} ({batch.DrugName}){(entry.To is null ? $" at {entry.At}" : $", {entry.At} -> {entry.To}")}");
            created++;
        }

        if (created == 0) Log.Summary("fleet already seeded");
    }

    private async Task<BatchInfo> CreateBatchAsync(Product product, List<BatchInfo> knownBatches, CancellationToken ct)
    {
        // INS-2611-001, INS-2611-002, ... numbered per product and month.
        var prefix = $"{product.Code}-{DateTime.UtcNow:yyMM}-";
        var next = knownBatches
            .Where(b => b.BatchNumber.StartsWith(prefix, StringComparison.Ordinal))
            .Select(b => int.TryParse(b.BatchNumber[prefix.Length..], out var n) ? n : 0)
            .DefaultIfEmpty(0).Max() + 1;

        var manufactured = DateTime.UtcNow.Date.AddDays(-_random.Next(20, 120));
        var batch = await api.PostAsync<BatchInfo>("batches", new
        {
            batchNumber = $"{prefix}{next:000}",
            drugName = product.DrugName,
            manufacturerName = product.Manufacturer,
            quantityUnits = product.Units / 50 * 50 + _random.Next(0, 10) * 50,
            manufacturedDateUtc = manufactured,
            expiryDateUtc = manufactured.AddMonths(product.ShelfLifeMonths),
            minSafeTemperatureC = product.MinC,
            maxSafeTemperatureC = product.MaxC,
        }, ct);
        knownBatches.Add(batch);
        return batch;
    }

    // ---------------------------------------------------------------- fleet state

    private sealed class Sensor(DeviceInfo device)
    {
        public DeviceInfo Device { get; } = device;
        public string Code => Device.DeviceCode;
        public bool IsTransit => IsTransitSensor(Code);
        /// <summary>Swapped whole by the lifecycle loop; the sensor loop just reads it.</summary>
        public volatile Assignment? Trip;
        public int Battery = 100;
        public long Ticks;
    }

    /// <summary>What a sensor is attached to right now. Shipment null = in storage at <see cref="From"/>.</summary>
    private sealed record Assignment(BatchInfo Batch, ThermalModel Model, Place From, Place? To, ShipmentInfo? Shipment, DateTime DepartedAtUtc, TimeSpan Duration)
    {
        public BatchState Status { get; set; } = Batch.Status;

        public double Progress => To is null || Duration <= TimeSpan.Zero
            ? 0
            : Math.Clamp((DateTime.UtcNow - DepartedAtUtc) / Duration, 0, 1);
    }

    private async Task LoadFleetAsync(CancellationToken ct)
    {
        var devices = await api.GetAsync<List<DeviceInfo>>("devices", ct);
        var batches = (await api.GetAsync<List<BatchInfo>>("batches", ct)).ToDictionary(b => b.Id);
        var shipments = await api.GetAsync<List<ShipmentInfo>>("shipments", ct);

        foreach (var device in devices.Where(d => d.DeviceCode.StartsWith(DevicePrefix, StringComparison.Ordinal)))
        {
            var sensor = new Sensor(device);
            if (device.AssignedBatchId is { } batchId && batches.TryGetValue(batchId, out var batch))
            {
                var shipment = shipments
                    .Where(s => s.BatchId == batchId && s.Status == ShipmentState.InTransit)
                    .OrderByDescending(s => s.DepartedAtUtc)
                    .FirstOrDefault();
                var place = FleetEntryFor(device.DeviceCode)?.At ?? Hubs[0];
                sensor.Trip = shipment is null
                    ? NewAssignment(batch, PlaceByName(place), null, null)
                    : NewAssignment(batch, PlaceByName(shipment.OriginLocation), PlaceByName(shipment.DestinationLocation), shipment);
            }
            _sensors[sensor.Code] = sensor;
        }
    }

    private static FleetEntry? FleetEntryFor(string code) => Fleet.FirstOrDefault(f => f.DeviceCode == code);

    private Assignment NewAssignment(BatchInfo batch, Place from, Place? to, ShipmentInfo? shipment)
    {
        var departed = shipment?.DepartedAtUtc is { } d ? DateTime.SpecifyKind(d, DateTimeKind.Utc) : DateTime.UtcNow;
        var duration = to is null ? TimeSpan.Zero : TimeSpan.FromHours(RoadKm(from, to) / options.TransitSpeedKmh);
        return new Assignment(batch, new ThermalModel(batch.MinSafeTemperatureC, batch.MaxSafeTemperatureC, _warningMarginPercent),
            from, to, shipment, departed, duration);
    }

    private static string Describe(Assignment? a) => a switch
    {
        null => "",
        { To: null } => $"in storage at {a.From.Name}",
        _ => $"{a.From.Name} -> {a.To.Name}, {a.Progress:P0} of {a.Duration.TotalHours:0.0}h",
    };

    /// <summary>A fridge door left open at a warehouse, a minute after start.</summary>
    private void KickoffExcursion()
    {
        var target = _sensors.Values
            .Where(s => !s.IsTransit && s.Trip is { Status: BatchState.Active } t && t.Batch.MinSafeTemperatureC >= 0)
            .OrderBy(_ => _random.Next())
            .FirstOrDefault();
        if (target?.Trip is null) return;
        target.Trip.Model.StartExcursion(severe: true);
        Log.Summary($"kickoff: {target.Code} ({target.Trip.Batch.BatchNumber}) starts a critical excursion - watch Live Monitoring");
    }

    // ---------------------------------------------------------------- sensors

    private async Task RunSensorAsync(Sensor sensor, CancellationToken ct)
    {
        var interval = TimeSpan.FromSeconds(options.IntervalSeconds);
        var chancePerTick = options.ExcursionsPerHour * options.IntervalSeconds / 3600;
        try
        {
            // Spread the fleet over the interval instead of every sensor firing at once.
            await Task.Delay(TimeSpan.FromSeconds(_random.NextDouble() * options.IntervalSeconds), ct);
            using var timer = new PeriodicTimer(interval);
            do
            {
                try
                {
                    if (sensor.Trip is { } trip)
                        await PostReadingAsync(sensor, trip, chancePerTick, ct);
                    if (sensor.Ticks++ % Math.Max(1, options.HeartbeatEvery) == 0)
                        await PostHeartbeatAsync(sensor, ct);
                }
                catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
                {
                    Interlocked.Increment(ref _failures);
                    Log.Warn(sensor.Code, ex.Message);
                }
            } while (await timer.WaitForNextTickAsync(ct));
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { }
    }

    private async Task PostReadingAsync(Sensor sensor, Assignment trip, double chancePerTick, CancellationToken ct)
    {
        var severeShare = sensor.IsTransit ? options.TransitSevereShare : options.StorageSevereShare;
        var temperature = trip.Model.Next(chancePerTick, severeShare);
        var (lat, lng) = Position(trip);

        await api.PostAsync<object>("sensorreadings", new
        {
            batchId = trip.Batch.Id,
            deviceId = sensor.Code,
            temperatureC = temperature,
            humidityPercent = Math.Round((decimal)(trip.To is null ? 42 + _random.NextDouble() * 10 : 50 + _random.NextDouble() * 15), 1),
            latitude = lat,
            longitude = lng,
        }, ct);
        Interlocked.Increment(ref _readings);

        if (temperature < trip.Batch.MinSafeTemperatureC || temperature > trip.Batch.MaxSafeTemperatureC)
        {
            Interlocked.Increment(ref _outOfRange);
            Log.Warn(sensor.Code, $"{trip.Batch.BatchNumber} {temperature:0.00} °C outside [{trip.Batch.MinSafeTemperatureC}, {trip.Batch.MaxSafeTemperatureC}] -> alert");
        }
        else
        {
            Log.Info(sensor.Code, $"{trip.Batch.BatchNumber} {temperature,7:0.00} °C{(trip.Model.InExcursion ? "  <- excursion" : "")}");
        }
    }

    private (double Lat, double Lng) Position(Assignment trip)
    {
        if (trip.To is null) // parked: GPS jitter only
            return (Math.Round(trip.From.Lat + (_random.NextDouble() - 0.5) * 0.0004, 6),
                    Math.Round(trip.From.Lng + (_random.NextDouble() - 0.5) * 0.0004, 6));

        var p = trip.Progress;
        return (Math.Round(trip.From.Lat + (trip.To.Lat - trip.From.Lat) * p + (_random.NextDouble() - 0.5) * 0.002, 6),
                Math.Round(trip.From.Lng + (trip.To.Lng - trip.From.Lng) * p + (_random.NextDouble() - 0.5) * 0.002, 6));
    }

    private Task PostHeartbeatAsync(Sensor sensor, CancellationToken ct)
    {
        // Trucks run on battery and lose a little each heartbeat; warehouse sensors are on mains.
        sensor.Battery = sensor.IsTransit
            ? Math.Max(5, sensor.Battery - (_random.Next(0, 8) == 0 ? 1 : 0))
            : Math.Clamp(sensor.Battery + _random.Next(-1, 2), 92, 100);
        var signal = sensor.IsTransit ? _random.Next(55, 96) : _random.Next(85, 101);
        return api.PostAnonymousAsync<object>($"devices/{sensor.Device.Id}/heartbeat", new { batteryPercent = sensor.Battery, signalPercent = signal }, ct);
    }

    // ---------------------------------------------------------------- lifecycle

    /// <summary>Once a minute: refresh batch statuses, finish trips that arrived, start new
    /// ones, and let the QA operator work the alert queue.</summary>
    private async Task RunLifecycleAsync(CancellationToken ct)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromMinutes(1));
        try
        {
            do
            {
                try
                {
                    var batches = await api.GetAsync<List<BatchInfo>>("batches", ct);
                    var byId = batches.ToDictionary(b => b.Id);
                    foreach (var trip in _sensors.Values.Select(s => s.Trip).OfType<Assignment>())
                        if (byId.TryGetValue(trip.Batch.Id, out var b)) trip.Status = b.Status;

                    foreach (var sensor in _sensors.Values.Where(s => s.IsTransit))
                    {
                        if (sensor.Trip is { To: not null, Progress: >= 1 } arrived)
                            await ArriveAsync(sensor, arrived, ct);
                        if (sensor.Trip is null)
                            await StartTripAsync(sensor, batches, ct);
                    }

                    if (options.AutoResolveAfterMinutes > 0)
                        await OperateAsync(batches, ct);
                }
                catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
                {
                    Interlocked.Increment(ref _failures);
                    Log.Warn("lifecycle", ex.Message);
                }
            } while (await timer.WaitForNextTickAsync(ct));
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { }
    }

    private async Task ArriveAsync(Sensor sensor, Assignment trip, CancellationToken ct)
    {
        // A batch quarantined on the road can't be delivered (Batch.MarkDelivered): it
        // stays held, shipment and all, for a person to deal with. The sensor moves on.
        if (trip.Status == BatchState.InTransit && trip.Shipment is not null)
        {
            await api.PostAsync<ShipmentInfo>($"shipments/{trip.Shipment.Id}/deliver", null, ct);
            Log.Summary($"{sensor.Code}: {trip.Batch.BatchNumber} delivered to {trip.To!.Name}");
        }
        else
        {
            Log.Summary($"{sensor.Code}: {trip.Batch.BatchNumber} arrived at {trip.To!.Name} but is {trip.Status} - left held");
        }
        await api.PostAsync<DeviceInfo>($"devices/{sensor.Device.Id}/unassign", null, ct);
        sensor.Trip = null;
    }

    private async Task StartTripAsync(Sensor sensor, List<BatchInfo> batches, CancellationToken ct)
    {
        var from = PlaceByName(Hubs[_random.Next(Hubs.Length)]);
        var candidates = Places.Where(p => p.Name != from.Name).ToArray();
        var to = candidates[_random.Next(candidates.Length)];
        var product = ProductByCode(TripProducts[_random.Next(TripProducts.Length)]);

        var batch = await CreateBatchAsync(product, batches, ct);
        await api.PostAsync<DeviceInfo>($"devices/{sensor.Device.Id}/assign/{batch.Id}", null, ct);
        var shipment = await api.PostAsync<ShipmentInfo>("shipments", new
        {
            batchId = batch.Id,
            originLocation = from.Name,
            destinationLocation = to.Name,
            courierName = Couriers[_random.Next(Couriers.Length)],
        }, ct);

        sensor.Battery = 100; // charged at the hub
        sensor.Trip = NewAssignment(batch with { Status = BatchState.InTransit }, from, to, shipment);
        Log.Summary($"{sensor.Code}: new trip {batch.BatchNumber} ({product.DrugName}) {Describe(sensor.Trip)}");
    }

    /// <summary>
    /// The simulated QA operator, for demo batches only. Resolves alerts once they're
    /// old enough (a fresh one stays up long enough to be shown live), then releases
    /// a storage batch from quarantine when its fridge is back in range. Batches held
    /// in transit stay quarantined: the domain can't deliver them anyway.
    /// </summary>
    private async Task OperateAsync(List<BatchInfo> batches, CancellationToken ct)
    {
        var cutoff = DateTime.UtcNow.AddMinutes(-options.AutoResolveAfterMinutes);
        var unresolved = (await api.GetAsync<List<AlertInfo>>("alerts/unresolved", ct))
            .Where(a => IsDemoBatch(a.BatchNumber))
            .ToList();

        var resolved = 0;
        foreach (var alert in unresolved.Where(a => DateTime.SpecifyKind(a.TriggeredAtUtc, DateTimeKind.Utc) < cutoff))
        {
            await api.PostAsync<object>($"alerts/{alert.Id}/resolve", null, ct);
            resolved++;
        }
        if (resolved > 0) Log.Summary($"QA operator: resolved {resolved} alert(s) older than {options.AutoResolveAfterMinutes} min");

        var stillOpen = unresolved.Where(a => DateTime.SpecifyKind(a.TriggeredAtUtc, DateTimeKind.Utc) >= cutoff).Select(a => a.BatchId).ToHashSet();
        foreach (var sensor in _sensors.Values.Where(s => !s.IsTransit))
        {
            if (sensor.Trip is not { Status: BatchState.Quarantined } trip) continue;
            if (stillOpen.Contains(trip.Batch.Id) || trip.Model.InExcursion || !trip.Model.InRange) continue;

            await api.PostAsync<BatchInfo>($"batches/{trip.Batch.Id}/clear-quarantine", null, ct);
            trip.Status = BatchState.Active;
            Log.Summary($"QA operator: {trip.Batch.BatchNumber} reviewed, back in range - quarantine cleared");
        }
    }

    private async Task ReportAsync(CancellationToken ct)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromMinutes(1));
        try
        {
            while (await timer.WaitForNextTickAsync(ct))
                Log.Summary($"readings={Interlocked.Read(ref _readings)} out-of-range={Interlocked.Read(ref _outOfRange)} failed={Interlocked.Read(ref _failures)}");
        }
        catch (OperationCanceledException) { }
    }
}
