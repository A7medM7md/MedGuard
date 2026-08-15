using MedGuard.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace MedGuard.Infrastructure.Persistence.Configurations;

public class DeviceConfiguration : IEntityTypeConfiguration<Device>
{
    public void Configure(EntityTypeBuilder<Device> builder)
    {
        builder.HasKey(d => d.Id);

        builder.Property(d => d.DeviceCode).HasMaxLength(50).IsRequired();
        builder.HasIndex(d => d.DeviceCode).IsUnique();

        builder.Property(d => d.Model).HasMaxLength(100).IsRequired();

        // No FK constraint to Batch on purpose — a device can be assigned/unassigned
        // freely and outlives any single batch; SetNull keeps it that way if a batch
        // is ever hard-deleted.
        builder.HasIndex(d => d.AssignedBatchId);
    }
}