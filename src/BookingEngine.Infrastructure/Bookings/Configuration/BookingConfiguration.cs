using BookingEngine.Domain.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace BookingEngine.Infrastructure.Bookings.Configuration;

public sealed record BookingConfiguration : IEntityTypeConfiguration<Booking>
{
    public void Configure(EntityTypeBuilder<Booking> builder)
    {
        _ = builder.HasKey(x => x.Id);

        _ = builder.Property(x => x.Id).ValueGeneratedNever();

        // UserId refers to the separate authentication database, so there is
        // deliberately no foreign key here.
        _ = builder.Property(x => x.UserId).IsRequired();

        _ = builder.Property(x => x.Status).IsRequired().HasConversion<string>().HasMaxLength(16);

        _ = builder
            .Property(x => x.StartsAt)
            .IsRequired()
            .HasConversion(v => v.ToUniversalTime(), v => v);

        _ = builder
            .Property(x => x.EndsAt)
            .IsRequired()
            .HasConversion(v => v.ToUniversalTime(), v => v);

        // Restrict, not Cascade: a booking is a record of what happened, and it must
        // survive the resource it was made against being deleted later.
        _ = builder
            .HasOne<Resource>()
            .WithMany()
            .HasForeignKey(x => x.ResourceId)
            .OnDelete(DeleteBehavior.Restrict);

        // Serves the availability read inside the booking transaction.
        _ = builder.HasIndex(x => new { x.ResourceId, x.StartsAt });

        _ = builder.HasIndex(x => x.UserId);
    }
}
