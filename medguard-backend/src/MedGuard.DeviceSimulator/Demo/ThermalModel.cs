namespace MedGuard.DeviceSimulator.Demo;

/// <summary>
/// The temperature inside one fridge, freezer or reefer truck: a mean-reverting random
/// walk around the unit's setpoint, scaled to the batch's safe range (2-8 °C, -50..-15 °C
/// and -90..-60 °C all behave sensibly), plus occasional warm excursions: a door left
/// open, a power cut, a failing compressor.
/// </summary>
public sealed class ThermalModel
{
    private readonly Random _random = new();
    private readonly double _min, _max, _center, _noise, _tolerance, _width;
    private double _temperature;
    private double? _target;
    private int _holdTicks;
    /// <summary>Readings left in which the unit pulls back hard (compressor/backup power kicks in).</summary>
    private int _recoveryTicks;

    public ThermalModel(decimal minC, decimal maxC, decimal warningMarginPercent)
    {
        _min = (double)minC;
        _max = (double)maxC;
        _width = _max - _min;
        // Fridges sit mid-range; freezers run colder than their limit with lots of headroom.
        _center = _width <= 10 ? _min + _width / 2 : _max - _width * 0.35;
        _noise = _width * 0.02;
        // The same rule Batch.RecordReading uses: within this much of the edge = Warning.
        _tolerance = _width * (double)warningMarginPercent / 100;
        _temperature = _center + (_random.NextDouble() - 0.5) * _width * 0.1;
    }

    public bool InExcursion => _target is not null;

    public bool InRange => _temperature >= _min && _temperature <= _max;

    /// <summary>Mild = peaks inside the warning margin (Warning alerts only).
    /// Severe = goes past it (Critical alerts, auto-quarantine).</summary>
    public void StartExcursion(bool severe)
    {
        _target = severe
            ? _max + _tolerance + _width * (0.15 + _random.NextDouble() * 0.35)
            : _max + _tolerance * (0.25 + _random.NextDouble() * 0.55);
        // Every out-of-range reading raises its own alert (Batch.RecordReading), so the
        // peak is kept short: an excursion makes a handful of alerts, not dozens.
        _holdTicks = _random.Next(1, 3);
    }

    /// <summary>Advances one reading interval and returns the new temperature.</summary>
    public decimal Next(double excursionChance, double severeShare)
    {
        if (_target is null && _random.NextDouble() < excursionChance)
            StartExcursion(_random.NextDouble() < severeShare);

        if (_target is { } target)
        {
            _temperature += (target - _temperature) * 0.55 + Gaussian() * _noise * 0.5;
            // Near the peak: hold there for a reading or two, then the unit recovers.
            if (Math.Abs(target - _temperature) < Math.Max(0.15, (target - _center) * 0.15) && --_holdTicks <= 0)
            {
                _target = null;
                _recoveryTicks = 4;
            }
        }
        else
        {
            var pull = _recoveryTicks > 0 ? 0.5 : 0.2;
            if (_recoveryTicks > 0) _recoveryTicks--;
            _temperature += (_center - _temperature) * pull + Gaussian() * _noise;
        }

        return Math.Round((decimal)_temperature, 2);
    }

    private double Gaussian() =>
        Math.Sqrt(-2 * Math.Log(1 - _random.NextDouble())) * Math.Cos(2 * Math.PI * _random.NextDouble());
}
