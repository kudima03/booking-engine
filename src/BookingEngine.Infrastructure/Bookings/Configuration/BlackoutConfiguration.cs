using BookingEngine.Domain.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace BookingEngine.Infrastructure.Bookings.Configuration;

public sealed record BlackoutConfiguration : IEntityTypeConfiguration<Blackout>
{
    public void Configure(EntityTypeBuilder<Blackout> builder)
    {
        _ = builder.HasKey(x => x.Id);

        _ = builder.Property(x => x.Id).ValueGeneratedNever();

        _ = builder.Property(x => x.Reason).IsRequired().HasMaxLength(512);

        // Npgsql rejects a timestamptz parameter whose offset is not zero, and the
        // converter applies to query parameters as well as writes.
        _ = builder
            .Property(x => x.StartsAt)
            .IsRequired()
            .HasConversion(v => v.ToUniversalTime(), v => v);

        _ = builder
            .Property(x => x.EndsAt)
            .IsRequired()
            .HasConversion(v => v.ToUniversalTime(), v => v);

        _ = builder
            .HasOne<Resource>()
            .WithMany()
            .HasForeignKey(x => x.ResourceId)
            .OnDelete(DeleteBehavior.Cascade);

        _ = builder.HasIndex(x => new { x.ResourceId, x.StartsAt });
    }
}
