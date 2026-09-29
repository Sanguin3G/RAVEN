namespace Raven.Api.Features.Auth;

public interface IWorkspaceAccessService
{
    Task<IReadOnlyList<WorkspaceUserResponse>> ListUsersAsync(CancellationToken cancellationToken);
    Task<WorkspaceUserResponse?> GetUserAsync(string id, CancellationToken cancellationToken);
    Task<WorkspaceUserChangeResult> CreateUserAsync(CreateWorkspaceUserRequest request, CancellationToken cancellationToken);
    Task<WorkspaceUserChangeResult> UpdateRoleAsync(string id, string role, CancellationToken cancellationToken);
    Task<WorkspaceUserChangeResult> SetEnabledAsync(string id, bool isEnabled, CancellationToken cancellationToken);
}
