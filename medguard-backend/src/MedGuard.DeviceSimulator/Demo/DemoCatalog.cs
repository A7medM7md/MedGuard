namespace MedGuard.DeviceSimulator.Demo;

/// <summary>Places, products and the starting fleet of the demo, set in Egypt like the README's story.</summary>
public static class DemoCatalog
{
    public sealed record Place(string Name, double Lat, double Lng);

    /// <summary>A product the demo can make batches of. Code is the batch-number prefix.</summary>
    public sealed record Product(string Code, string DrugName, string Manufacturer, decimal MinC, decimal MaxC, int Units, int ShelfLifeMonths);

    /// <summary>One seeded sensor: in storage at <see cref="At"/>, or on the road to <see cref="To"/>.</summary>
    public sealed record FleetEntry(string DeviceCode, string Model, string ProductCode, string At, string? To = null, string? Courier = null);

    public static readonly Place[] Places =
    [
        new("Cairo Central Cold Store", 30.0626, 31.2497),
        new("Giza Vaccine Depot", 30.0131, 31.2089),
        new("6th of October Pharma Depot", 29.9285, 30.9188),
        new("Alexandria Port Cold Hub", 31.1975, 29.8925),
        new("Tanta University Hospital Pharmacy", 30.7865, 31.0004),
        new("Mansoura Regional Pharmacy", 31.0409, 31.3785),
        new("Port Said Distribution Center", 31.2653, 32.3019),
        new("Ismailia Cold Room", 30.5965, 32.2715),
        new("Assiut Medical Stores", 27.1809, 31.1837),
        new("Aswan Regional Pharmacy", 24.0889, 32.8998),
    ];

    /// <summary>Warehouses trucks leave from when a new trip starts.</summary>
    public static readonly string[] Hubs = ["Cairo Central Cold Store", "Giza Vaccine Depot", "Alexandria Port Cold Hub", "6th of October Pharma Depot"];

    public static readonly string[] Couriers = ["ColdRun Logistics", "NileFreeze Transport", "PharmaLink Express", "Delta Reefer Co."];

    public static readonly Product[] Products =
    [
        new("INS", "Insulin Glargine 100 U/mL", "Sanofi", 2, 8, 1200, 24),
        new("ASP", "Insulin Aspart 100 U/mL", "Novo Nordisk", 2, 8, 900, 24),
        new("HBV", "Hepatitis B Vaccine", "GSK", 2, 8, 2500, 30),
        new("MRV", "Measles-Rubella Vaccine", "Serum Institute of India", 2, 8, 4000, 24),
        new("ROT", "Rotavirus Vaccine (oral)", "GSK", 2, 8, 1800, 24),
        new("ADA", "Adalimumab 40 mg Pen", "AbbVie", 2, 8, 300, 24),
        new("TRZ", "Trastuzumab 440 mg", "Roche", 2, 8, 150, 36),
        new("EPO", "Epoetin Alfa 4000 IU", "Janssen", 2, 8, 600, 24),
        new("OXY", "Oxytocin 10 IU/mL", "VACSERA", 2, 8, 3000, 24),
        new("VAR", "Varicella Vaccine (frozen)", "Merck", -50, -15, 800, 18),
        new("MRN", "COVID-19 mRNA Vaccine", "Pfizer-BioNTech", -90, -60, 1950, 9),
    ];

    /// <summary>Products trucks pick up for a new trip (2-8 °C, the bulk of real cold-chain traffic).</summary>
    public static readonly string[] TripProducts = ["INS", "ASP", "HBV", "MRV", "ROT", "ADA", "TRZ", "EPO", "OXY"];

    public static readonly FleetEntry[] Fleet =
    [
        new("MG-CT-1001", "CryoTag S2", "INS", "Cairo Central Cold Store"),
        new("MG-CT-1002", "CryoTag S2", "HBV", "Giza Vaccine Depot"),
        new("MG-CT-1003", "CryoTag S2", "ADA", "Alexandria Port Cold Hub"),
        new("MG-FG-2001", "FreezeGuard F1", "VAR", "6th of October Pharma Depot"),
        new("MG-UC-3001", "UltraCold Probe X", "MRN", "Cairo Central Cold Store"),
        new("MG-TL-4001", "ThermoLink Pro", "ASP", "Cairo Central Cold Store", "Alexandria Port Cold Hub", "ColdRun Logistics"),
        new("MG-TL-4002", "ThermoLink Pro", "MRV", "Cairo Central Cold Store", "Aswan Regional Pharmacy", "NileFreeze Transport"),
        new("MG-TL-4003", "ThermoLink Pro", "ROT", "Alexandria Port Cold Hub", "Tanta University Hospital Pharmacy", "PharmaLink Express"),
        new("MG-TL-4004", "ThermoLink Pro", "TRZ", "Giza Vaccine Depot", "Mansoura Regional Pharmacy", "Delta Reefer Co."),
    ];

    /// <summary>Device codes the demo owns.</summary>
    public const string DevicePrefix = "MG-";

    /// <summary>
    /// Batches the demo created ("INS-2611-004"). The QA operator acts on these only,
    /// never on batches a person created through the UI.
    /// </summary>
    public static bool IsDemoBatch(string batchNumber)
    {
        var parts = batchNumber.Split('-');
        return parts.Length == 3 && Products.Any(p => p.Code == parts[0])
               && parts[1].Length == 4 && parts[1].All(char.IsAsciiDigit)
               && parts[2].Length == 3 && parts[2].All(char.IsAsciiDigit);
    }

    /// <summary>Transit sensors start a new trip after each delivery.</summary>
    public static bool IsTransitSensor(string deviceCode) => deviceCode.StartsWith("MG-TL-", StringComparison.Ordinal);

    public static Product ProductByCode(string code) => Products.First(p => p.Code == code);

    public static Place PlaceByName(string name) =>
        Places.FirstOrDefault(p => string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase)) ?? Places[0];

    /// <summary>Road distance estimate: great-circle distance x 1.25.</summary>
    public static double RoadKm(Place a, Place b)
    {
        const double R = 6371;
        double Rad(double d) => d * Math.PI / 180;
        var dLat = Rad(b.Lat - a.Lat);
        var dLng = Rad(b.Lng - a.Lng);
        var h = Math.Pow(Math.Sin(dLat / 2), 2) + Math.Cos(Rad(a.Lat)) * Math.Cos(Rad(b.Lat)) * Math.Pow(Math.Sin(dLng / 2), 2);
        return 2 * R * Math.Asin(Math.Sqrt(h)) * 1.25;
    }
}
