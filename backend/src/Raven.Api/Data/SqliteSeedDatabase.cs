using Microsoft.Data.Sqlite;

namespace Raven.Api.Data;

public static class SqliteSeedDatabase
{
    public static void CopyIfMissing(string connectionString, string seedPath)
    {
        var settings = new SqliteConnectionStringBuilder(connectionString);
        if (settings.Mode == SqliteOpenMode.Memory || settings.DataSource == ":memory:")
        {
            return;
        }

        var databasePath = Path.GetFullPath(settings.DataSource);
        if (File.Exists(databasePath))
        {
            return;
        }

        if (!File.Exists(seedPath))
        {
            throw new FileNotFoundException("The SQLite seed database is missing.", seedPath);
        }

        Directory.CreateDirectory(Path.GetDirectoryName(databasePath)!);
        var temporaryPath = $"{databasePath}.{Guid.NewGuid():N}.tmp";
        try
        {
            File.Copy(seedPath, temporaryPath, overwrite: false);
            File.Move(temporaryPath, databasePath, overwrite: false);
        }
        catch (IOException) when (File.Exists(databasePath))
        {
            // Preserve a database created by another startup attempt.
        }
        finally
        {
            if (File.Exists(temporaryPath))
            {
                File.Delete(temporaryPath);
            }
        }
    }
}
