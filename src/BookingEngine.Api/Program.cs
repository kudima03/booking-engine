using BookingEngine.Api;
using BookingEngine.Api.Middlewares;
using BookingEngine.Api.MigrationsManagers;
using BookingEngine.Infrastructure.Auth;
using BookingEngine.Infrastructure.Bookings;
using Microsoft.AspNetCore.Authentication.BearerToken;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Scalar.AspNetCore;

WebApplicationBuilder builder = WebApplication.CreateBuilder(args);

// Add service defaults & Aspire client integrations.
builder.AddServiceDefaults();

// Add services to the container.
builder.Services.AddProblemDetails();

builder.Services.AddControllers();

builder.Services.AddSingleton<ExceptionHandlingMiddleware>();

builder.Services.AddSingleton(TimeProvider.System);

builder.Services.AddDbContext<BookingDbContext>(options =>
    options.UseNpgsql(
        builder.Configuration.GetConnectionString("BookingDb"),
        contextOptions =>
            contextOptions
                .MigrationsAssembly("BookingEngine.Infrastructure")
                .EnableRetryOnFailure()
    )
);

builder.Services.AddDbContext<AuthDbContext>(options =>
    options.UseNpgsql(
        builder.Configuration.GetConnectionString("AuthDb"),
        contextOptions =>
            contextOptions
                .MigrationsAssembly("BookingEngine.Infrastructure")
                .EnableRetryOnFailure()
    )
);

// AddIdentityApiEndpoints installs the bearer-token scheme as the default; nothing else
// may call AddAuthentication with a different default or every [Authorize] stops working.
builder
    .Services.AddIdentityApiEndpoints<ApplicationUser>()
    .AddRoles<ApplicationRole>()
    .AddUserManager<RoleAssigningUserManager>()
    .AddEntityFrameworkStores<AuthDbContext>();

// Blocking a user cannot revoke an access token that has already been issued, so keep the
// window short: a blocked user loses access at their next refresh.
builder.Services.Configure<BearerTokenOptions>(
    IdentityConstants.BearerScheme,
    options => options.BearerTokenExpiration = TimeSpan.FromMinutes(15)
);

builder.Services.AddAuthorization();

builder.Services.AddHostedService<BookingContextMigrationsManager>();

builder.Services.AddHostedService<AuthContextMigrationsManager>();

builder.Services.AddHostedService<IdentitySeeder>();

builder.Services.AddOutputCache();

if (builder.Environment.IsDevelopment())
{
    _ = builder.Services.AddOpenApi(options =>
        options.AddDocumentTransformer(new BearerSecuritySchemeTransformer())
    );
}

WebApplication app = builder.Build();

app.UseMiddleware<ExceptionHandlingMiddleware>();

app.UseOutputCache();

app.UseAuthentication();

app.UseAuthorization();

if (app.Environment.IsDevelopment())
{
    _ = app.MapOpenApi().CacheOutput(x => x.Expire(TimeSpan.FromDays(365)));
    _ = app.MapScalarApiReference();
}

app.MapDefaultEndpoints();

app.MapGroup("auth").MapIdentityApi<ApplicationUser>().WithTags("Auth");

app.MapControllers();

app.Run();
