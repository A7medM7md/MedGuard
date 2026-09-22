using MedGuard.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace MedGuard.Infrastructure.Persistence.Configurations;

public class OrgSettingsConfiguration : IEntityTypeConfiguration<OrgSettings>
{
    public void Configure(EntityTypeBuilder<OrgSettings> builder)
    {
        builder.HasKey(s => s.Id);

        builder.Property(s => s.OrganizationName).HasMaxLength(200).IsRequired();
        builder.Property(s => s.TimeZone).HasMaxLength(100).IsRequired();
        builder.Property(s => s.WarningMarginPercent).HasPrecision(5, 2);
    }
}
