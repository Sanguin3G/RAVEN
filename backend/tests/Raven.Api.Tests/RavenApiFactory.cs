using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Raven.Api.Tests;

public sealed class RavenApiFactory : WebApplicationFactory<Program>
{
    private readonly string databasePath = Path.Combine(Path.GetTempPath(), $"raven-tests-{Guid.NewGuid():N}.db");

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        builder.UseSetting("ConnectionStrings:Raven", $"Data Source={databasePath};Pooling=False");
        builder.UseSetting("Security:ValidateApiAntiforgery", "false");
        builder.ConfigureLogging(logging => logging.ClearProviders().AddDebug());
        builder.ConfigureTestServices(services => services
            .AddAuthentication(options =>
            {
                options.DefaultAuthenticateScheme = RavenTestAuthenticationHandler.SchemeName;
                options.DefaultChallengeScheme = RavenTestAuthenticationHandler.SchemeName;
                options.DefaultForbidScheme = RavenTestAuthenticationHandler.SchemeName;
            })
            .AddScheme<AuthenticationSchemeOptions, RavenTestAuthenticationHandler>(
                RavenTestAuthenticationHandler.SchemeName, _ => { }));
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);

        if (disposing && File.Exists(databasePath))
        {
            File.Delete(databasePath);
        }

    }
}
