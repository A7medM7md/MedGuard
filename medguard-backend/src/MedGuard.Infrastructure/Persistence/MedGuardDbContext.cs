using MedGuard.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace MedGuard.Infrastructure.Persistence;

public class MedGuardDbContext : DbContext
{
    public MedGuardDbContext(DbContextOptions<MedGuardDbContext> options) : base(options) { }

    public DbSet<Batch> Batches => Set<Batch>();
    public DbSet<SensorReading> SensorReadings => Set<SensorReading>();
    public DbSet<Alert> Alerts => Set<Alert>();
    public DbSet<Shipment> Shipments => Set<Shipment>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(MedGuardDbContext).Assembly);
        base.OnModelCreating(modelBuilder);
    }
}
