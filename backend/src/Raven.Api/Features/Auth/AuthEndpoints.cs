using System.Security.Claims;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;

namespace Raven.Api.Features.Auth;

public static class AuthEndpoints
{
    public static IEndpointRouteBuilder MapAuthEndpoints(this IEndpointRouteBuilder app)
    {
        var auth = app.MapGroup("/api/auth").WithTags("Authentication");

        auth.MapGet("/csrf", GetAntiforgeryTokenAsync)
            .AllowAnonymous()
            .WithName("GetAntiforgeryToken")
            .WithSummary("Get a browser request token")
            .WithDescription("Returns a request token for protecting browser mutations, including login, from cross-site request forgery.")
            .Produces<AntiforgeryTokenResponse>(StatusCodes.Status200OK);

        auth.MapPost("/login", LoginAsync)
            .AllowAnonymous()
            .WithName("Login")
            .WithSummary("Sign in to the private workspace")
            .Produces<AuthUserResponse>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status401Unauthorized)
            .ProducesValidationProblem(StatusCodes.Status400BadRequest);

        auth.MapGet("/me", GetCurrentUserAsync)
            .RequireAuthorization()
            .WithName("GetCurrentWorkspaceUser")
            .WithSummary("Get the signed-in workspace user")
            .Produces<AuthUserResponse>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status401Unauthorized);

        auth.MapPost("/logout", LogoutAsync)
            .RequireAuthorization()
            .WithName("Logout")
            .WithSummary("Sign out of the workspace")
            .Produces(StatusCodes.Status204NoContent)
            .Produces(StatusCodes.Status401Unauthorized);

        auth.MapPost("/change-password", ChangePasswordAsync)
            .RequireAuthorization()
            .WithName("ChangeWorkspacePassword")
            .WithSummary("Change the signed-in user's password")
            .Produces(StatusCodes.Status204NoContent)
            .ProducesValidationProblem(StatusCodes.Status400BadRequest)
            .Produces(StatusCodes.Status401Unauthorized);

        return app;
    }

    private static Task<Ok<AntiforgeryTokenResponse>> GetAntiforgeryTokenAsync(
        HttpContext context,
        IAntiforgery antiforgery)
    {
        var tokens = antiforgery.GetAndStoreTokens(context);
        return Task.FromResult(TypedResults.Ok(new AntiforgeryTokenResponse(tokens.RequestToken!)));
    }

    private static async Task<Results<Ok<AuthUserResponse>, UnauthorizedHttpResult>> LoginAsync(
        LoginRequest request,
        UserManager<ApplicationUser> userManager,
        SignInManager<ApplicationUser> signInManager)
    {
        var user = await userManager.FindByEmailAsync(request.Email.Trim());
        if (user is null || !user.IsEnabled) return TypedResults.Unauthorized();

        var result = await signInManager.PasswordSignInAsync(user, request.Password,
            isPersistent: true, lockoutOnFailure: true);
        if (!result.Succeeded) return TypedResults.Unauthorized();

        return TypedResults.Ok(await ToAuthUserResponseAsync(user, userManager));
    }

    private static async Task<Results<Ok<AuthUserResponse>, UnauthorizedHttpResult>> GetCurrentUserAsync(
        ClaimsPrincipal principal,
        UserManager<ApplicationUser> userManager)
    {
        var user = await userManager.GetUserAsync(principal);
        return user is null || !user.IsEnabled
            ? TypedResults.Unauthorized()
            : TypedResults.Ok(await ToAuthUserResponseAsync(user, userManager));
    }

    private static async Task<NoContent> LogoutAsync(SignInManager<ApplicationUser> signInManager)
    {
        await signInManager.SignOutAsync();
        return TypedResults.NoContent();
    }

    private static async Task<IResult> ChangePasswordAsync(
        ClaimsPrincipal principal,
        ChangePasswordRequest request,
        UserManager<ApplicationUser> userManager,
        SignInManager<ApplicationUser> signInManager)
    {
        var user = await userManager.GetUserAsync(principal);
        if (user is null || !user.IsEnabled) return TypedResults.Unauthorized();

        var result = await userManager.ChangePasswordAsync(user, request.CurrentPassword, request.NewPassword);
        if (!result.Succeeded)
        {
            var errors = result.Errors
                .GroupBy(error => error.Code, StringComparer.Ordinal)
                .ToDictionary(group => group.Key, group => group.Select(error => error.Description).ToArray(), StringComparer.Ordinal);
            return TypedResults.ValidationProblem(errors);
        }

        await signInManager.RefreshSignInAsync(user);
        return TypedResults.NoContent();
    }

    private static async Task<AuthUserResponse> ToAuthUserResponseAsync(
        ApplicationUser user,
        UserManager<ApplicationUser> userManager) =>
        new(user.Id, user.DisplayName, user.Email ?? string.Empty, (await userManager.GetRolesAsync(user)).ToArray());
}

public static class WorkspaceAccessEndpoints
{
    public static IEndpointRouteBuilder MapWorkspaceAccessEndpoints(this IEndpointRouteBuilder app)
    {
        var users = app.MapGroup("/api/admin/users")
            .WithTags("Workspace access")
            .RequireAuthorization("AdminOnly");

        users.MapGet("/", ListAsync)
            .WithName("ListWorkspaceUsers")
            .WithSummary("List workspace users")
            .Produces<IReadOnlyList<WorkspaceUserResponse>>(StatusCodes.Status200OK);

        users.MapGet("/{id}", GetAsync)
            .WithName("GetWorkspaceUser")
            .WithSummary("Get a workspace user")
            .Produces<WorkspaceUserResponse>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status404NotFound);

        users.MapPost("/", CreateAsync)
            .WithName("CreateWorkspaceUser")
            .WithSummary("Create an Admin or Researcher account")
            .Produces<WorkspaceUserResponse>(StatusCodes.Status201Created)
            .Produces<ProblemDetails>(StatusCodes.Status409Conflict)
            .ProducesValidationProblem(StatusCodes.Status400BadRequest);

        users.MapPut("/{id}/role", UpdateRoleAsync)
            .WithName("UpdateWorkspaceUserRole")
            .WithSummary("Change a workspace user's role")
            .Produces<WorkspaceUserResponse>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status404NotFound)
            .Produces<ProblemDetails>(StatusCodes.Status409Conflict)
            .ProducesValidationProblem(StatusCodes.Status400BadRequest);

        users.MapPut("/{id}/enabled", SetEnabledAsync)
            .WithName("SetWorkspaceUserEnabled")
            .WithSummary("Enable or disable a workspace user")
            .Produces<WorkspaceUserResponse>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status404NotFound)
            .Produces<ProblemDetails>(StatusCodes.Status409Conflict);

        return app;
    }

    private static async Task<Ok<IReadOnlyList<WorkspaceUserResponse>>> ListAsync(
        IWorkspaceAccessService service,
        CancellationToken cancellationToken) =>
        TypedResults.Ok(await service.ListUsersAsync(cancellationToken));

    private static async Task<Results<Ok<WorkspaceUserResponse>, NotFound>> GetAsync(
        string id,
        IWorkspaceAccessService service,
        CancellationToken cancellationToken)
    {
        var user = await service.GetUserAsync(id, cancellationToken);
        return user is null ? TypedResults.NotFound() : TypedResults.Ok(user);
    }

    private static async Task<IResult> CreateAsync(
        CreateWorkspaceUserRequest request,
        IWorkspaceAccessService service,
        CancellationToken cancellationToken)
    {
        var result = await service.CreateUserAsync(request, cancellationToken);
        return result.Status switch
        {
            WorkspaceUserChangeStatus.Success => TypedResults.Created($"/api/admin/users/{result.User!.Id}", result.User),
            WorkspaceUserChangeStatus.Conflict => TypedResults.Conflict(new ProblemDetails
            {
                Status = StatusCodes.Status409Conflict,
                Title = "Workspace user already exists",
                Detail = "An account with this email address already exists."
            }),
            _ => TypedResults.ValidationProblem(result.Errors ?? new Dictionary<string, string[]>()
            {
                ["user"] = ["The workspace user could not be created."]
            })
        };
    }

    private static async Task<IResult> UpdateRoleAsync(
        string id,
        UpdateWorkspaceUserRoleRequest request,
        IWorkspaceAccessService service,
        CancellationToken cancellationToken)
    {
        var result = await service.UpdateRoleAsync(id, request.Role, cancellationToken);
        return ChangeResult(result, "The last enabled Admin cannot be demoted.");
    }

    private static async Task<IResult> SetEnabledAsync(
        string id,
        SetWorkspaceUserEnabledRequest request,
        IWorkspaceAccessService service,
        CancellationToken cancellationToken)
    {
        var result = await service.SetEnabledAsync(id, request.IsEnabled, cancellationToken);
        return ChangeResult(result, "The last enabled Admin cannot be disabled.");
    }

    private static IResult ChangeResult(WorkspaceUserChangeResult result, string lastAdminDetail) => result.Status switch
    {
        WorkspaceUserChangeStatus.Success => TypedResults.Ok(result.User),
        WorkspaceUserChangeStatus.NotFound => TypedResults.NotFound(),
        WorkspaceUserChangeStatus.Conflict => TypedResults.Conflict(new ProblemDetails
        {
            Status = StatusCodes.Status409Conflict,
            Title = "An enabled Admin must remain",
            Detail = lastAdminDetail
        }),
        _ => TypedResults.ValidationProblem(result.Errors ?? new Dictionary<string, string[]>()
        {
            ["user"] = ["The workspace user could not be updated."]
        })
    };
}
