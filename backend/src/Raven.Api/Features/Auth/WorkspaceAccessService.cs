using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Raven.Api.Data;

namespace Raven.Api.Features.Auth;

public sealed class WorkspaceAccessService(
    RavenDbContext db,
    UserManager<ApplicationUser> userManager,
    RoleManager<IdentityRole> roleManager) : IWorkspaceAccessService
{
    private static readonly string[] AllowedRoles = ["Admin", "Researcher"];

    public async Task<IReadOnlyList<WorkspaceUserResponse>> ListUsersAsync(CancellationToken cancellationToken)
    {
        var users = await userManager.Users.OrderBy(user => user.DisplayName).ToListAsync(cancellationToken);
        var result = new List<WorkspaceUserResponse>(users.Count);
        foreach (var user in users)
            result.Add(await ToResponseAsync(user));
        return result;
    }

    public async Task<WorkspaceUserResponse?> GetUserAsync(string id, CancellationToken cancellationToken)
    {
        var user = await userManager.FindByIdAsync(id);
        return user is null ? null : await ToResponseAsync(user);
    }

    public async Task<WorkspaceUserChangeResult> CreateUserAsync(CreateWorkspaceUserRequest request, CancellationToken cancellationToken)
    {
        var role = request.Role.Trim();
        if (!AllowedRoles.Contains(role, StringComparer.Ordinal))
            return Invalid(nameof(request.Role), "Choose Admin or Researcher.");
        if (!await roleManager.RoleExistsAsync(role))
            return Invalid(nameof(request.Role), "The selected workspace role is not available.");

        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        var user = new ApplicationUser
        {
            UserName = request.Email.Trim(),
            Email = request.Email.Trim(),
            DisplayName = request.DisplayName.Trim(),
            EmailConfirmed = true,
            IsEnabled = true,
            CreatedAt = DateTimeOffset.UtcNow
        };
        var createResult = await userManager.CreateAsync(user, request.TemporaryPassword);
        if (!createResult.Succeeded)
        {
            await transaction.RollbackAsync(cancellationToken);
            return FromIdentityErrors(createResult.Errors);
        }

        var roleResult = await userManager.AddToRoleAsync(user, role);
        if (!roleResult.Succeeded)
        {
            await transaction.RollbackAsync(cancellationToken);
            return FromIdentityErrors(roleResult.Errors);
        }

        await transaction.CommitAsync(cancellationToken);
        return new WorkspaceUserChangeResult(WorkspaceUserChangeStatus.Success, await ToResponseAsync(user));
    }

    public async Task<WorkspaceUserChangeResult> UpdateRoleAsync(string id, string role, CancellationToken cancellationToken)
    {
        if (!AllowedRoles.Contains(role, StringComparer.Ordinal))
            return Invalid(nameof(UpdateWorkspaceUserRoleRequest.Role), "Choose Admin or Researcher.");
        if (!await roleManager.RoleExistsAsync(role))
            return Invalid(nameof(UpdateWorkspaceUserRoleRequest.Role), "The selected workspace role is not available.");

        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        var user = await userManager.FindByIdAsync(id);
        if (user is null)
        {
            await transaction.RollbackAsync(cancellationToken);
            return new WorkspaceUserChangeResult(WorkspaceUserChangeStatus.NotFound);
        }

        var currentRoles = await userManager.GetRolesAsync(user);
        if (currentRoles.Count == 1 && string.Equals(currentRoles[0], role, StringComparison.Ordinal))
        {
            await transaction.CommitAsync(cancellationToken);
            return new WorkspaceUserChangeResult(WorkspaceUserChangeStatus.Success, await ToResponseAsync(user));
        }

        if (user.IsEnabled && currentRoles.Contains("Admin", StringComparer.Ordinal) && role != "Admin"
            && await EnabledAdminCountAsync(cancellationToken) <= 1)
        {
            await transaction.RollbackAsync(cancellationToken);
            return new WorkspaceUserChangeResult(WorkspaceUserChangeStatus.Conflict);
        }

        var removeResult = await userManager.RemoveFromRolesAsync(user, currentRoles);
        if (!removeResult.Succeeded)
        {
            await transaction.RollbackAsync(cancellationToken);
            return FromIdentityErrors(removeResult.Errors);
        }

        var addResult = await userManager.AddToRoleAsync(user, role);
        if (!addResult.Succeeded)
        {
            await transaction.RollbackAsync(cancellationToken);
            return FromIdentityErrors(addResult.Errors);
        }

        var stampResult = await userManager.UpdateSecurityStampAsync(user);
        if (!stampResult.Succeeded)
        {
            await transaction.RollbackAsync(cancellationToken);
            return FromIdentityErrors(stampResult.Errors);
        }

        await transaction.CommitAsync(cancellationToken);
        return new WorkspaceUserChangeResult(WorkspaceUserChangeStatus.Success, await ToResponseAsync(user));
    }

    public async Task<WorkspaceUserChangeResult> SetEnabledAsync(string id, bool isEnabled, CancellationToken cancellationToken)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        var user = await userManager.FindByIdAsync(id);
        if (user is null)
        {
            await transaction.RollbackAsync(cancellationToken);
            return new WorkspaceUserChangeResult(WorkspaceUserChangeStatus.NotFound);
        }

        if (user.IsEnabled == isEnabled)
        {
            await transaction.CommitAsync(cancellationToken);
            return new WorkspaceUserChangeResult(WorkspaceUserChangeStatus.Success, await ToResponseAsync(user));
        }

        if (!isEnabled && user.IsEnabled && await userManager.IsInRoleAsync(user, "Admin")
            && await EnabledAdminCountAsync(cancellationToken) <= 1)
        {
            await transaction.RollbackAsync(cancellationToken);
            return new WorkspaceUserChangeResult(WorkspaceUserChangeStatus.Conflict);
        }

        user.IsEnabled = isEnabled;
        var updateResult = await userManager.UpdateAsync(user);
        if (!updateResult.Succeeded)
        {
            await transaction.RollbackAsync(cancellationToken);
            return FromIdentityErrors(updateResult.Errors);
        }

        var stampResult = await userManager.UpdateSecurityStampAsync(user);
        if (!stampResult.Succeeded)
        {
            await transaction.RollbackAsync(cancellationToken);
            return FromIdentityErrors(stampResult.Errors);
        }

        await transaction.CommitAsync(cancellationToken);
        return new WorkspaceUserChangeResult(WorkspaceUserChangeStatus.Success, await ToResponseAsync(user));
    }

    private async Task<int> EnabledAdminCountAsync(CancellationToken cancellationToken)
    {
        var admins = await userManager.GetUsersInRoleAsync("Admin");
        cancellationToken.ThrowIfCancellationRequested();
        return admins.Count(user => user.IsEnabled);
    }

    private async Task<WorkspaceUserResponse> ToResponseAsync(ApplicationUser user)
    {
        var roles = await userManager.GetRolesAsync(user);
        return new WorkspaceUserResponse(user.Id, user.DisplayName, user.Email ?? string.Empty,
            roles.SingleOrDefault() ?? "Researcher", user.IsEnabled, user.CreatedAt);
    }

    private static WorkspaceUserChangeResult Invalid(string key, string detail) =>
        new(WorkspaceUserChangeStatus.Invalid, Errors: new Dictionary<string, string[]> { [key] = [detail] });

    private static WorkspaceUserChangeResult FromIdentityErrors(IEnumerable<IdentityError> errors)
    {
        var items = errors.ToArray();
        var status = items.Any(error => error.Code is "DuplicateEmail" or "DuplicateUserName")
            ? WorkspaceUserChangeStatus.Conflict
            : WorkspaceUserChangeStatus.Invalid;
        return new WorkspaceUserChangeResult(status, Errors: items
            .GroupBy(error => error.Code, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.Select(error => error.Description).ToArray(), StringComparer.Ordinal));
    }
}
