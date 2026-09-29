using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Raven.Api.Data;

namespace Raven.Api.Features.Auth;

public sealed class IdentityBootstrapper(
    RavenDbContext db,
    UserManager<ApplicationUser> userManager,
    RoleManager<IdentityRole> roleManager,
    IConfiguration configuration,
    IHostEnvironment environment,
    ILogger<IdentityBootstrapper> logger)
{
    private static readonly string[] WorkspaceRoles = ["Admin", "Researcher"];

    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        foreach (var role in WorkspaceRoles)
        {
            if (await roleManager.RoleExistsAsync(role)) continue;
            var createRole = await roleManager.CreateAsync(new IdentityRole(role));
            if (!createRole.Succeeded)
                throw new InvalidOperationException($"Could not create the required {role} workspace role: {string.Join(", ", createRole.Errors.Select(error => error.Code))}.");
        }

        if (await userManager.Users.AnyAsync(cancellationToken))
        {
            await transaction.CommitAsync(cancellationToken);
            return;
        }

        var email = configuration["RAVEN_BOOTSTRAP_ADMIN_EMAIL"]?.Trim();
        var password = configuration["RAVEN_BOOTSTRAP_ADMIN_PASSWORD"];
        if (string.IsNullOrWhiteSpace(email) || string.IsNullOrWhiteSpace(password))
        {
            if (environment.IsProduction())
                throw new InvalidOperationException("The workspace has no users. Configure RAVEN_BOOTSTRAP_ADMIN_EMAIL and RAVEN_BOOTSTRAP_ADMIN_PASSWORD before starting the API.");

            await transaction.CommitAsync(cancellationToken);
            logger.LogWarning("The workspace has no users. Configure RAVEN_BOOTSTRAP_ADMIN_EMAIL and RAVEN_BOOTSTRAP_ADMIN_PASSWORD to create the initial Admin account.");
            return;
        }

        var admin = new ApplicationUser
        {
            UserName = email,
            Email = email,
            DisplayName = email,
            EmailConfirmed = true,
            IsEnabled = true,
            CreatedAt = DateTimeOffset.UtcNow
        };
        var createAdmin = await userManager.CreateAsync(admin, password);
        if (!createAdmin.Succeeded)
            throw new InvalidOperationException($"Could not create the initial Admin account: {string.Join(", ", createAdmin.Errors.Select(error => error.Code))}.");

        var addAdminRole = await userManager.AddToRoleAsync(admin, "Admin");
        if (!addAdminRole.Succeeded)
            throw new InvalidOperationException($"Could not assign the initial Admin role: {string.Join(", ", addAdminRole.Errors.Select(error => error.Code))}.");

        await transaction.CommitAsync(cancellationToken);
        logger.LogInformation("Created the initial workspace Admin from deployment bootstrap configuration.");
    }
}
