using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace Raven.Api.Tests;

public sealed class RavenIdentityApiFactory : WebApplicationFactory<Program>
{
    public const string AdminEmail = "workspace-admin@example.invalid";
    public const string AdminPassword = "Raven-Integration-Pass9!";

    private readonly string databasePath = Path.Combine(Path.GetTempPath(), $"raven-identity-tests-{Guid.NewGuid():N}.db");

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        builder.UseSetting("ConnectionStrings:Raven", $"Data Source={databasePath};Pooling=False");
        builder.ConfigureAppConfiguration((_, configuration) => configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["RAVEN_BOOTSTRAP_ADMIN_EMAIL"] = AdminEmail,
            ["RAVEN_BOOTSTRAP_ADMIN_PASSWORD"] = AdminPassword,
            ["Security:ValidateApiAntiforgery"] = "true"
        }));
        builder.ConfigureLogging(logging => logging.ClearProviders().AddDebug());
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        if (!disposing) return;
        foreach (var path in new[] { databasePath, $"{databasePath}-shm", $"{databasePath}-wal" })
            if (File.Exists(path)) File.Delete(path);
    }
}
