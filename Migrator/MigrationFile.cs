using System.Globalization;
using System.Text.RegularExpressions;

namespace Migrator;

// One migration: <yyyyMMddHHmmss>_<name>.up.sql and its optional .down.sql (M.1b, M.9).
public sealed record MigrationFile(string Id, string Name, string UpPath, string? DownPath)
{
    private static readonly Regex UpFileName = new Regex(@"^(?<id>\d{14})_(?<name>[A-Za-z0-9_\-]+)\.up\.sql$");

    // Ordered by id. Throws on a .sql file that doesn't follow the naming rule, or on a duplicate id.
    public static IReadOnlyList<MigrationFile> Load(string directory)
    {
        List<MigrationFile> migrations = new List<MigrationFile>();
        if (!Directory.Exists(directory))
        {
            return migrations;
        }

        foreach (string path in Directory.GetFiles(directory, "*.sql"))
        {
            string fileName = Path.GetFileName(path);
            if (fileName.EndsWith(".down.sql", StringComparison.Ordinal))
            {
                string upName = fileName[..^".down.sql".Length] + ".up.sql";
                if (!File.Exists(Path.Combine(directory, upName)))
                {
                    throw new InvalidOperationException($"{fileName} has no matching {upName}");
                }
                continue;
            }

            Match match = UpFileName.Match(fileName);
            if (!match.Success || !DateTime.TryParseExact(match.Groups["id"].Value, "yyyyMMddHHmmss",
                    CultureInfo.InvariantCulture, DateTimeStyles.None, out _))
            {
                throw new InvalidOperationException($"{fileName} is not named <yyyyMMddHHmmss>_<name>.up.sql / .down.sql");
            }

            string id = match.Groups["id"].Value;
            string downPath = Path.Combine(directory, $"{id}_{match.Groups["name"].Value}.down.sql");
            migrations.Add(new MigrationFile(id, match.Groups["name"].Value, path, File.Exists(downPath) ? downPath : null));
        }

        List<IGrouping<string, MigrationFile>> duplicates = migrations.GroupBy(m => m.Id).Where(g => g.Count() > 1).ToList();
        if (duplicates.Count > 0)
        {
            throw new InvalidOperationException($"duplicate migration id {duplicates[0].Key}");
        }

        return migrations.OrderBy(m => m.Id, StringComparer.Ordinal).ToList();
    }
}
