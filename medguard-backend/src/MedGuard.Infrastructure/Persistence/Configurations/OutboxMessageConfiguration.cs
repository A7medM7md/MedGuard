using MedGuard.Infrastructure.Outbox;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace MedGuard.Infrastructure.Persistence.Configurations;

public class OutboxMessageConfiguration : IEntityTypeConfiguration<OutboxMessage>
{
    public void Configure(EntityTypeBuilder<OutboxMessage> builder)
    {
        builder.ToTable("OutboxMessages");
        builder.HasKey(m => m.Id);
        builder.Property(m => m.Topic).HasMaxLength(200).IsRequired();
        builder.Property(m => m.MessageKey).HasMaxLength(200).IsRequired();
        builder.Property(m => m.Type).HasMaxLength(200).IsRequired();
        builder.Property(m => m.Payload).IsRequired();
        builder.Property(m => m.LastError).HasMaxLength(1000);

        // The dispatcher's only query is "oldest unpublished first". Filtering the
        // index to pending rows keeps it tiny however many published rows pile up.
        builder.HasIndex(m => m.OccurredAtUtc)
            .HasDatabaseName("IX_OutboxMessages_Pending")
            .HasFilter("[ProcessedAtUtc] IS NULL");
    }
}
