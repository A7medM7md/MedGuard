using MedGuard.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace MedGuard.Infrastructure.Persistence.Configurations;

public class BatchConfiguration : IEntityTypeConfiguration<Batch>
{
    public void Configure(EntityTypeBuilder<Batch> builder)
    {
        builder.HasKey(b => b.Id);

        builder.Property(b => b.BatchNumber).HasMaxLength(50).IsRequired();
        builder.HasIndex(b => b.BatchNumber).IsUnique();

        builder.Property(b => b.DrugName).HasMaxLength(200).IsRequired();
        builder.Property(b => b.ManufacturerName).HasMaxLength(200).IsRequired();

        // TemperatureRange is a value object (no Id of its own) — OwnsOne maps it to two
        // columns on the Batches table instead of requiring a separate table/entity.
        builder.OwnsOne(b => b.SafeRange, range =>
        {
            range.Property(r => r.MinC)
                .HasColumnName("MinSafeTemperatureC")
                .HasColumnType("decimal(5,2)")
                .IsRequired();

            range.Property(r => r.MaxC)
                .HasColumnName("MaxSafeTemperatureC")
                .HasColumnType("decimal(5,2)")
                .IsRequired();
        });
        builder.Navigation(b => b.SafeRange).IsRequired();

        // SensorReadings/Alerts/Shipments are exposed as IReadOnlyCollection backed by private
        // Lists — EF can't use property setters that don't exist, so it must read/write the
        // backing fields directly.
        builder.HasMany(b => b.SensorReadings)
            .WithOne(r => r.Batch)
            .HasForeignKey(r => r.BatchId)
            .OnDelete(DeleteBehavior.Cascade);
        builder.Metadata.FindNavigation(nameof(Batch.SensorReadings))!
            .SetPropertyAccessMode(PropertyAccessMode.Field);

        builder.HasMany(b => b.Alerts)
            .WithOne(a => a.Batch)
            .HasForeignKey(a => a.BatchId)
            .OnDelete(DeleteBehavior.Cascade);
        builder.Metadata.FindNavigation(nameof(Batch.Alerts))!
            .SetPropertyAccessMode(PropertyAccessMode.Field);

        builder.HasMany(b => b.Shipments)
            .WithOne(s => s.Batch)
            .HasForeignKey(s => s.BatchId)
            .OnDelete(DeleteBehavior.Cascade);
        builder.Metadata.FindNavigation(nameof(Batch.Shipments))!
            .SetPropertyAccessMode(PropertyAccessMode.Field);
    }
}
