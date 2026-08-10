using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace BookingEngine.Infrastructure.Auth;

/// <summary>
/// Persistence context for users, roles and their claims.
/// </summary>
/// <remarks>
/// Lives in its own database, separate from the booking data. Bookings reference a user by
/// identifier only, with no foreign key across the boundary.
/// </remarks>
public sealed class AuthDbContext(DbContextOptions<AuthDbContext> options)
    : IdentityDbContext<ApplicationUser, ApplicationRole, Guid>(options)
{
    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);

        _ = builder.Entity<ApplicationUser>(user =>
        {
            _ = user.Property(x => x.Name).HasMaxLength(64);
            _ = user.Property(x => x.Surname).HasMaxLength(64);
        });
    }
}
