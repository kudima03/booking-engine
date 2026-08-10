using BookingEngine.Domain.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace BookingEngine.Infrastructure.Bookings.Configuration;

public sealed record OpeningHoursConfiguration : IEntityTypeConfiguration<OpeningHours>
{
    public void Configure(EntityTypeBuilder<OpeningHours> builder)
    {
        _ = builder.HasKey(x => x.Id);

        _ = builder.Property(x => x.Id).ValueGeneratedNever();

        _ = builder.Property(x => x.DayOfWeek).IsRequired();

        _ = builder.Property(x => x.StartTime).IsRequired();

        _ = builder.Property(x => x.EndTime).IsRequired();

        _ = builder
            .HasOne<Resource>()
            .WithMany()
            .HasForeignKey(x => x.ResourceId)
            .OnDelete(DeleteBehavior.Cascade);

        _ = builder.HasIndex(x => new { x.ResourceId, x.DayOfWeek });
    }
}
