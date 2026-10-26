using Microsoft.AspNetCore.Authentication.BearerToken;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using TaskFlow.Application.Abstractions;
using TaskFlow.Application.Users;
using TaskFlow.Infrastructure.Email;
using TaskFlow.Infrastructure.Identity;
using TaskFlow.Infrastructure.Persistence;

namespace TaskFlow.Infrastructure;

public static class DependencyInjection
{
    public const string ConnectionStringName = "TaskFlow";

    public static IServiceCollection AddInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        var connectionString = configuration.GetConnectionString(ConnectionStringName)
            ?? throw new InvalidOperationException($"Connection string '{ConnectionStringName}' is not configured.");

        services.AddDbContext<AppDbContext>((provider, options) => options
            .UseSqlServer(connectionString, sql => sql.EnableRetryOnFailure())
            .AddInterceptors(new CreationTimeInterceptor(provider.GetRequiredService<TimeProvider>())));
        services.AddScoped<IAppDbContext>(provider => provider.GetRequiredService<AppDbContext>());

        services.AddHealthChecks()
            .AddDbContextCheck<AppDbContext>("database");

        services.AddIdentityApiEndpoints<ApplicationUser>(options =>
            {
                options.User.RequireUniqueEmail = true;
                options.Password.RequiredLength = 8;
                options.Password.RequireDigit = true;
                options.Password.RequireLowercase = true;
                options.Password.RequireUppercase = true;
                options.Password.RequireNonAlphanumeric = false;
                options.Lockout.AllowedForNewUsers = true;
            })
            .AddRoles<IdentityRole<Guid>>()
            .AddEntityFrameworkStores<AppDbContext>()
            .AddTokenProvider<InvitationTokenProvider>(InvitationTokenProvider.ProviderName);

        // These keys protect bearer, refresh and invitation tokens. Kept only in memory or in the container's file
        // system, a restart or a second instance would invalidate every session and every pending invitation, so
        // deployments point DataProtection:KeysPath at storage that persists and is shared between instances.
        var dataProtection = services.AddDataProtection().SetApplicationName("TaskFlow");
        if (configuration["DataProtection:KeysPath"] is { Length: > 0 } keysPath)
        {
            dataProtection.PersistKeysToFileSystem(new DirectoryInfo(keysPath));
        }

        services.Configure<InvitationTokenProviderOptions>(options => options.TokenLifespan =
            TimeSpan.FromDays(configuration.GetValue("Auth:InvitationDays", InvitationTokenProviderOptions.DefaultLifespanDays)));

        services.Configure<BearerTokenOptions>(IdentityConstants.BearerScheme, options =>
        {
            options.BearerTokenExpiration = TimeSpan.FromMinutes(configuration.GetValue("Auth:AccessTokenMinutes", 60));
            options.RefreshTokenExpiration = TimeSpan.FromDays(configuration.GetValue("Auth:RefreshTokenDays", 14));
        });

        services.Configure<AdminSeedOptions>(configuration.GetSection(AdminSeedOptions.SectionName));
        services.AddScoped<IdentitySeeder>();
        services.AddScoped<IIdentityService, IdentityService>();
        services.AddMemoryCache();
        services.AddScoped<SessionValidator>();

        services.Configure<EmailOptions>(configuration.GetSection(EmailOptions.SectionName));
        if (string.IsNullOrWhiteSpace(configuration[$"{EmailOptions.SectionName}:Smtp:Host"]))
        {
            services.AddSingleton<IEmailSender, LoggingEmailSender>();
        }
        else
        {
            services.AddSingleton<IEmailSender, SmtpEmailSender>();
        }

        services.AddScoped<IInvitationSender, InvitationSender>();

        // MapIdentityApi resolves it once, from the root provider, so it must not depend on scoped services.
        services.AddSingleton<IEmailSender<ApplicationUser>, IdentityEmailSender>();

        return services;
    }

    /// <summary>Applies pending migrations and seeds roles and the first admin, as configured.</summary>
    public static async Task InitializeDatabaseAsync(this IServiceProvider services, bool migrate, bool seed)
    {
        await using var scope = services.CreateAsyncScope();

        if (migrate)
        {
            await scope.ServiceProvider.GetRequiredService<AppDbContext>().Database.MigrateAsync();
        }

        if (seed)
        {
            await scope.ServiceProvider.GetRequiredService<IdentitySeeder>().SeedAsync();
        }
    }
}
