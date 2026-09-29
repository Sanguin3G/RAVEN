using System.Text.Json;
using Raven.DemoSeed;

namespace Raven.DemoSeed;

internal static class DemoSeedCommand
{
    public static async Task<int> Main(string[] args)
    {
        var serializerOptions = new JsonSerializerOptions(JsonSerializerDefaults.Web) { WriteIndented = true };
        try
        {
            if (args.Length == 2 && args[0] == "--inventory")
            {
                var inventory = await DemoDatabaseInventoryReader.ReadAsync(args[1]);
                Console.WriteLine(JsonSerializer.Serialize(inventory, serializerOptions));
                return 0;
            }

            if (args.Length >= 1 && args[0] == "--source")
            {
                var parsed = ParseOptions(args);
                await DemoDatabaseCurator.ExportAsync(parsed);
                return 0;
            }

            Console.Error.WriteLine("Usage:");
            Console.Error.WriteLine("  dotnet run --project DemoSeed -- --inventory <source.db>");
            Console.Error.WriteLine("  dotnet run --project DemoSeed -- --source <source.db> --manifest <selection.json> --output <raven.demo.db> [--public-seed <raven.seed.db>]");
            return 2;
        }
        catch (Exception exception) when (exception is IOException or InvalidDataException or InvalidOperationException or ArgumentException)
        {
            Console.Error.WriteLine(exception.Message);
            return 1;
        }
    }

    private static DemoSeedExportOptions ParseOptions(string[] args)
    {
        var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        for (var index = 0; index < args.Length; index += 2)
        {
            if (index + 1 >= args.Length || !args[index].StartsWith("--", StringComparison.Ordinal))
                throw new ArgumentException("Export options must be supplied as --name value pairs.");
            if (!values.TryAdd(args[index], args[index + 1])) throw new ArgumentException($"Option {args[index]} was supplied more than once.");
        }

        string Required(string name) => values.TryGetValue(name, out var value) && !string.IsNullOrWhiteSpace(value)
            ? value
            : throw new ArgumentException($"Missing required option {name}.");

        var allowedOptions = new HashSet<string>(["--source", "--manifest", "--output", "--public-seed"], StringComparer.OrdinalIgnoreCase);
        if (values.Keys.Any(key => !allowedOptions.Contains(key))) throw new ArgumentException("One or more export options are not recognized.");
        return new DemoSeedExportOptions(Required("--source"), Required("--manifest"), Required("--output"),
            values.GetValueOrDefault("--public-seed"));
    }
}
