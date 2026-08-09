using MedGuard.Domain.Exceptions;

namespace MedGuard.Domain.ValueObjects;

/// <summary>
/// The safe temperature window a batch must be kept within (e.g. 2C-8C for most vaccines).
/// Immutable value object — two ranges with the same bounds are equal (record semantics).
/// </summary>
public sealed record TemperatureRange
{
    public decimal MinC { get; }
    public decimal MaxC { get; }

    public TemperatureRange(decimal minC, decimal maxC)
    {
        if (minC >= maxC)
            throw new InvalidTemperatureRangeException(minC, maxC);

        MinC = minC;
        MaxC = maxC;
    }

    public bool IsBreach(decimal temperatureC) => temperatureC < MinC || temperatureC > MaxC;

    /// <summary>How far outside the range a temperature is. 0 if it's within range.</summary>
    public decimal DistanceOutsideRange(decimal temperatureC)
    {
        if (temperatureC > MaxC) return temperatureC - MaxC;
        if (temperatureC < MinC) return MinC - temperatureC;
        return 0m;
    }

    public override string ToString() => $"{MinC}C to {MaxC}C";
}
