using MedGuard.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace MedGuard.Infrastructure.Persistence.Configurations;

public class ShipmentConfiguration : IEntityTypeConfiguration<Shipment>
{
    public void Configure(EntityTypeBuilder<Shipment> builder)
    {
        builder.HasKey(s => s.Id);
        builder.Property(s => s.OriginLocation).HasMaxLength(200).IsRequired();
        builder.Property(s => s.DestinationLocation).HasMaxLength(200).IsRequired();
        builder.Property(s => s.CourierName).HasMaxLength(200);
    }
}
