using BookingEngine.Domain.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace BookingEngine.Infrastructure.Bookings.Configuration;

public sealed record ResourceTypeConfiguration : IEntityTypeConfiguration<ResourceType>
{
    public void Configure(EntityTypeBuilder<ResourceType> builder)
    {
        _ = builder.HasKey(x => x.Id);

        _ = builder.Property(x => x.Id).ValueGeneratedNever();

        _ = builder.Property(x => x.Name).IsRequired().HasMaxLength(64);

        _ = builder.Property(x => x.Description).IsRequired().HasMaxLength(512);

        _ = builder.HasIndex(x => x.Name).IsUnique();
    }
}
