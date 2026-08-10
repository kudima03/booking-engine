using BookingEngine.Infrastructure.Auth;
using Microsoft.AspNetCore.Identity;

namespace BookingEngine.Api;

/// <summary>
/// Creates the known roles and the bootstrap administrator once the schema is in place.
/// </summary>
/// <remarks>
/// Runs after the migration managers, in <c>StartedAsync</c>. The administrator's credentials
/// come from <c>Identity:Admin:Email</c> and <c>Identity:Admin:Password</c>; when either is
/// missing no administrator is created and only the roles are seeded.
/// </remarks>
public sealed record IdentitySeeder : IHostedLifecycleService
{
    private readonly IServiceProvider _serviceProvider;
    private readonly IConfiguration _configuration;

    public IdentitySeeder(IServiceProvider serviceProvider, IConfiguration configuration)
    {
        _serviceProvider = serviceProvider;
        _configuration = configuration;
    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        return Task.CompletedTask;
    }

    public async Task StartedAsync(CancellationToken cancellationToken)
    {
        using IServiceScope scope = _serviceProvider.CreateScope();

        RoleManager<ApplicationRole> roles = scope.ServiceProvider.GetRequiredService<
            RoleManager<ApplicationRole>
        >();

        foreach (string role in KnownRoles.All)
        {
            if (!await roles.RoleExistsAsync(role))
            {
                _ = await roles.CreateAsync(new ApplicationRole { Name = role });
            }
        }

        await SeedAdministratorAsync(scope);
    }

    public Task StartingAsync(CancellationToken cancellationToken)
    {
        return Task.CompletedTask;
    }

    public Task StoppedAsync(CancellationToken cancellationToken)
    {
        return Task.CompletedTask;
    }

    public Task StoppingAsync(CancellationToken cancellationToken)
    {
        return Task.CompletedTask;
    }

    private async Task SeedAdministratorAsync(IServiceScope scope)
    {
        string? email = _configuration["Identity:Admin:Email"];
        string? password = _configuration["Identity:Admin:Password"];

        if (string.IsNullOrWhiteSpace(email) || string.IsNullOrWhiteSpace(password))
        {
            return;
        }

        UserManager<ApplicationUser> users = scope.ServiceProvider.GetRequiredService<
            UserManager<ApplicationUser>
        >();

        if (await users.FindByEmailAsync(email) is not null)
        {
            return;
        }

        ApplicationUser administrator = new()
        {
            Id = Guid.CreateVersion7(),
            UserName = email,
            Email = email,
            EmailConfirmed = true,
        };

        if (!(await users.CreateAsync(administrator, password)).Succeeded)
        {
            return;
        }

        // Creating a user already grants the User role, and AddToRolesAsync fails as a whole
        // if any role is already held, so only the missing ones are added here.
        IList<string> held = await users.GetRolesAsync(administrator);

        _ = await users.AddToRolesAsync(administrator, KnownRoles.All.Except(held));
    }
}
