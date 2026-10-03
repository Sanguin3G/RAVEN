using System.ComponentModel.DataAnnotations;

namespace Raven.Api.Features.Auth;

/// <summary>Credentials for a private RAVEN workspace login.</summary>
public sealed record LoginRequest
{
    [Required, EmailAddress, MaxLength(256)]
    public required string Email { get; init; }

    [Required, MaxLength(128)]
    public required string Password { get; init; }
}

/// <summary>Safe identity details for the signed-in workspace user.</summary>
public sealed record AuthUserResponse(
    string Id,
    string DisplayName,
    string Email,
    IReadOnlyList<string> Roles);

/// <summary>Antiforgery request token used by browser API mutations.</summary>
public sealed record AntiforgeryTokenResponse(string RequestToken);

/// <summary>Request to change the current user's password.</summary>
public sealed record ChangePasswordRequest
{
    [Required, MaxLength(128)]
    public required string CurrentPassword { get; init; }

    [Required, MinLength(12), MaxLength(128)]
    public required string NewPassword { get; init; }
}

/// <summary>Request to add a workspace member.</summary>
public sealed record CreateWorkspaceUserRequest
{
    [Required, MaxLength(160)]
    public required string DisplayName { get; init; }

    [Required, EmailAddress, MaxLength(256)]
    public required string Email { get; init; }

    [Required, MinLength(12), MaxLength(128)]
    public required string TemporaryPassword { get; init; }

    [Required, RegularExpression("^(Admin|Researcher)$")]
    public string Role { get; init; } = "Researcher";
}

/// <summary>Request to assign one of the supported workspace roles.</summary>
public sealed record UpdateWorkspaceUserRoleRequest
{
    [Required, RegularExpression("^(Admin|Researcher)$")]
    public required string Role { get; init; }
}

/// <summary>Request to enable or disable a workspace account.</summary>
public sealed record SetWorkspaceUserEnabledRequest(bool IsEnabled);

/// <summary>Safe workspace user details for administrators.</summary>
public sealed record WorkspaceUserResponse(
    string Id,
    string DisplayName,
    string Email,
    string Role,
    bool IsEnabled,
    DateTimeOffset CreatedAt);

public enum WorkspaceUserChangeStatus
{
    Success,
    NotFound,
    Conflict,
    Invalid
}

public sealed record WorkspaceUserChangeResult(
    WorkspaceUserChangeStatus Status,
    WorkspaceUserResponse? User = null,
    IReadOnlyDictionary<string, string[]>? Errors = null);
