namespace MedGuard.Domain.Enums
{
    public enum DeviceStatus
    {
        Online = 0,   // heartbeat within the last 5 minutes
        Stale = 1,    // heartbeat 5-30 minutes ago — probably a connectivity issue
        Offline = 2   // no heartbeat in 30+ minutes — treat as a monitoring gap
    }
}
