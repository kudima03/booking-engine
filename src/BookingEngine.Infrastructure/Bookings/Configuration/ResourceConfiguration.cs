using BookingEngine.Domain.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace BookingEngine.Infrastructure.Bookings.Configuration;

public sealed record ResourceConfiguration : IEntityTypeConfiguration<Resource>
{
    public void Configure(EntityTypeBuilder<Resource> builder)
    {
        _ = builder.HasKey(x => x.Id);

        _ = builder.Property(x => x.Id).ValueGeneratedNever();

        _ = builder.Property(x => x.Name).IsRequired().HasMaxLength(64);

        _ = builder.Property(x => x.Description).IsRequired().HasMaxLength(512);

        _ = builder.Property(x => x.MinNotice).IsRequired();

        _ = builder.Property(x => x.MaxHorizon).IsRequired();

        _ = builder.Property(x => x.SlotDuration).IsRequired();

        // Configured without a navigation property: the models are related by
        // identifier only, so the type graph stays free of edges.
        _ = builder
            .HasOne<ResourceType>()
            .WithMany()
            .HasForeignKey(x => x.TypeId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
