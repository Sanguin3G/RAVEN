using Microsoft.AspNetCore.Identity;

namespace Raven.Api.Features.Auth;

public sealed class ApplicationUser : IdentityUser
{
    public required string DisplayName { get; set; }
    public bool IsEnabled { get; set; } = true;
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}
