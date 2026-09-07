namespace Orbita.Api.Configuration;

/// <summary>
/// Loads a <c>.env</c> file into configuration.
///
/// .NET has no built-in <c>.env</c> support — the framework expects secrets to arrive as
/// environment variables or user secrets — so this bridges the gap for local
/// development, where a single file that is easy to open and paste into beats a CLI
/// command nobody remembers.
///
/// The file is <b>never</b> committed (it is in <c>.gitignore</c>); <c>.env.example</c>
/// is the committed template that documents which keys exist.
///
/// Keys are written exactly as they appear in <c>appsettings.json</c>, using <c>:</c> to
/// nest, so anything in <c>.env</c> overrides the matching appsettings value:
///
/// <code>
/// Ai:Providers:openai-compatible:ApiKey=sk-or-v1-...
/// </code>
///
/// The double-underscore form the .NET environment-variable provider uses
/// (<c>Ai__Providers__...</c>) is accepted too, so a <c>.env</c> copied from another
/// project still works.
///
/// This is a deliberately small parser for a developer-convenience file, not a general
/// dotenv implementation: it handles <c>KEY=value</c>, <c>#</c> comments, blank lines,
/// an optional <c>export</c> prefix, and surrounding quotes. It does not do multi-line
/// values or variable interpolation.
/// </summary>
public static class DotEnvConfigurationExtensions
{
    private const string FileName = ".env";

    /// <summary>
    /// Looks for a <c>.env</c> starting at <paramref name="startDirectory"/> and walking
    /// up to <paramref name="maxDepth"/> parent directories, so the file can sit at the
    /// repository root rather than inside the API project.
    ///
    /// Missing files are ignored: production has no <c>.env</c>, and neither do the
    /// integration tests.
    /// </summary>
    public static IConfigurationBuilder AddDotEnvFile(
        this IConfigurationBuilder builder,
        string startDirectory,
        int maxDepth = 3)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentException.ThrowIfNullOrWhiteSpace(startDirectory);

        var path = FindDotEnv(startDirectory, maxDepth);

        if (path is null)
        {
            return builder;
        }

        var values = Parse(File.ReadAllLines(path));

        return values.Count == 0 ? builder : builder.AddInMemoryCollection(values);
    }

    private static string? FindDotEnv(string startDirectory, int maxDepth)
    {
        var directory = new DirectoryInfo(startDirectory);

        for (var depth = 0; depth <= maxDepth && directory is not null; depth++)
        {
            var candidate = Path.Combine(directory.FullName, FileName);

            if (File.Exists(candidate))
            {
                return candidate;
            }

            directory = directory.Parent;
        }

        return null;
    }

    private static Dictionary<string, string?> Parse(IEnumerable<string> lines)
    {
        var values = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);

        foreach (var rawLine in lines)
        {
            var line = rawLine.Trim();

            if (line.Length == 0 || line.StartsWith('#'))
            {
                continue;
            }

            if (line.StartsWith("export ", StringComparison.Ordinal))
            {
                line = line["export ".Length..].TrimStart();
            }

            var separator = line.IndexOf('=', StringComparison.Ordinal);

            if (separator <= 0)
            {
                continue;
            }

            var key = line[..separator].Trim().Replace("__", ":", StringComparison.Ordinal);
            var value = Unquote(line[(separator + 1)..].Trim());

            // An empty value means "not set" rather than "set to empty string", so a
            // placeholder line left blank does not shadow appsettings.
            if (key.Length > 0 && value.Length > 0)
            {
                values[key] = value;
            }
        }

        return values;
    }

    private static string Unquote(string value)
        => value.Length >= 2 && (value[0] == '"' || value[0] == '\'') && value[^1] == value[0]
            ? value[1..^1]
            : value;
}
