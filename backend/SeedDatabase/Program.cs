using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Raven.Api.Data;
using Raven.Api.Features.Settings;

if (args.Length != 1)
{
    Console.Error.WriteLine("Usage: dotnet run --project SeedDatabase -- <output.db>");
    return 1;
}

var output = Path.GetFullPath(args[0]);
if (File.Exists(output))
{
    Console.Error.WriteLine($"Refusing to overwrite an existing database: {output}");
    return 1;
}

Directory.CreateDirectory(Path.GetDirectoryName(output)!);
var options = new DbContextOptionsBuilder<RavenDbContext>()
    .UseSqlite($"Data Source={output};Pooling=False")
    .Options;

try
{
    await using (var db = new RavenDbContext(options))
    {
        await db.Database.MigrateAsync();
        db.ResearchSettings.Add(ResearchSettingsDefaults.CreateEntity(DateTimeOffset.UnixEpoch));
        await db.SaveChangesAsync();
    }

    await using (var connection = new SqliteConnection($"Data Source={output};Pooling=False"))
    {
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = "PRAGMA wal_checkpoint(TRUNCATE)";
        await command.ExecuteNonQueryAsync();
        command.CommandText = "PRAGMA journal_mode=DELETE";
        var journalMode = (string?)await command.ExecuteScalarAsync();
        if (!string.Equals(journalMode, "delete", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("Could not finalize the seed as a standalone SQLite file.");
        }
    }
    Console.WriteLine($"Created clean SQLite seed: {output}");
    return 0;
}
catch
{
    File.Delete(output);
    throw;
}
