using MedGuard.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace MedGuard.Infrastructure.Persistence.Configurations;

public class SensorReadingConfiguration : IEntityTypeConfiguration<SensorReading>
{
    public void Configure(EntityTypeBuilder<SensorReading> builder)
    {
        builder.HasKey(r => r.Id);
        builder.Property(r => r.DeviceId).HasMaxLength(100).IsRequired();
        builder.Property(r => r.TemperatureC).HasColumnType("decimal(5,2)");
        builder.Property(r => r.HumidityPercent).HasColumnType("decimal(5,2)");

        // Time-series style access pattern: always querying "latest readings for batch X".
        builder.HasIndex(r => new { r.BatchId, r.RecordedAtUtc });
    }
}

public class AlertConfiguration : IEntityTypeConfiguration<Alert>
{
    public void Configure(EntityTypeBuilder<Alert> builder)
    {
        builder.HasKey(a => a.Id);
        builder.Property(a => a.Message).HasMaxLength(500).IsRequired();

        builder.HasOne(a => a.SensorReading)
            .WithMany()
            .HasForeignKey(a => a.SensorReadingId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(a => a.IsResolved);
    }
}
