namespace MedGuard.DeviceSimulator.Demo;

/// <summary>
/// The "Demo" section: a fleet that talks to a deployed MedGuard.API over HTTP,
/// the way a sensor gateway does where there is no MQTT/Kafka (runasp.net, ADR 0001).
/// </summary>
public class DemoOptions
{
    /// <summary>The API root, without /api, e.g. https://medguard.runasp.net.</summary>
    public string ApiBaseUrl { get; set; } = "https://medguard.runasp.net";

    /// <summary>IdentityServer that issues the client_credentials token.</summary>
    public string Authority { get; set; } = "https://medguard-identity.runasp.net";

    public string ClientId { get; set; } = "medguard-m2m";

    /// <summary>The medguard-m2m secret (M2M:ClientSecret on the IdentityServer).</summary>
    public string ClientSecret { get; set; } = "medguard-m2m-dev-secret";

    public string Scope { get; set; } = "medguard.api";

    /// <summary>Seconds between two readings of one sensor. Every reading is one HTTP
    /// request and one SensorReadings row, so this sets both API load and DB growth.</summary>
    public double IntervalSeconds { get; set; } = 20;

    /// <summary>Send a heartbeat every N readings.</summary>
    public int HeartbeatEvery { get; set; } = 3;

    /// <summary>Average temperature excursions per sensor per hour.</summary>
    public double ExcursionsPerHour { get; set; } = 0.3;

    /// <summary>Share (0-1) of excursions that go past the warning margin, i.e. Critical +
    /// auto-quarantine. Storage sensors; transit sensors use <see cref="TransitSevereShare"/>.</summary>
    public double StorageSevereShare { get; set; } = 0.5;

    /// <summary>Kept low: a quarantined batch can't be delivered, so it stays held.</summary>
    public double TransitSevereShare { get; set; } = 0.2;

    /// <summary>Average truck speed, which sets how long a route takes (real time).</summary>
    public double TransitSpeedKmh { get; set; } = 70;

    /// <summary>Start one Critical excursion on a storage sensor right away, so a fresh
    /// demo shows an alert and an auto-quarantine within a couple of minutes.</summary>
    public bool KickoffExcursion { get; set; } = true;

    /// <summary>
    /// The simulated QA operator: resolves alerts of simulator batches once they're this
    /// old, then clears the quarantine of storage batches whose temperature is back in
    /// range. 0 = off (alerts and quarantines pile up until a human handles them).
    /// </summary>
    public int AutoResolveAfterMinutes { get; set; } = 30;
}
