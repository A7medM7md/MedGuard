namespace MedGuard.Domain.Enums
{
    /// <summary>
    /// Where an alert notification goes. Flags so a severity can route to more than
    /// one channel at once (e.g. Critical = Email | Sms | InApp). Dispatch itself
    /// isn't wired up yet (no email/SMS provider — see Roadmap's
    /// Alerting-Notification service); this only records the org's intent so that
    /// service has something real to read once it exists.
    /// </summary>
    [Flags]
    public enum AlertChannel
    {
        None = 0,
        InApp = 1,
        Email = 2,
        Sms = 4,
    }
}
