using Microsoft.Extensions.Configuration;
using Orbita.Api.Configuration;

namespace Orbita.IntegrationTests.Configuration;

/// <summary>
/// The .env loader is how the OpenRouter API key reaches the app in local development,
/// so its parsing rules are pinned here rather than discovered by a key silently not
/// arriving.
/// </summary>
public sealed class DotEnvConfigurationTests : IDisposable
{
    private readonly string _directory = Directory.CreateTempSubdirectory("orbita-dotenv").FullName;

    private IConfiguration Load(string contents, string? startDirectory = null)
    {
        File.WriteAllText(Path.Combine(_directory, ".env"), contents);

        return new ConfigurationBuilder()
            .AddDotEnvFile(startDirectory ?? _directory)
            .Build();
    }

    [Fact]
    public void Keys_are_written_the_same_way_as_in_appsettings()
    {
        var configuration = Load("Ai:Providers:openai-compatible:ApiKey=sk-or-v1-abc123");

        Assert.Equal("sk-or-v1-abc123", configuration["Ai:Providers:openai-compatible:ApiKey"]);
    }

    [Fact]
    public void The_double_underscore_form_works_too()
    {
        // So a .env copied from another project still resolves.
        var configuration = Load("Ai__Providers__openai-compatible__ApiKey=sk-or-v1-abc123");

        Assert.Equal("sk-or-v1-abc123", configuration["Ai:Providers:openai-compatible:ApiKey"]);
    }

    [Fact]
    public void Comments_blank_lines_and_export_prefixes_are_ignored()
    {
        var configuration = Load("""
            # esto es un comentario

            export Ai:Providers:openai-compatible:ApiKey=sk-or-v1-abc123
            """);

        Assert.Equal("sk-or-v1-abc123", configuration["Ai:Providers:openai-compatible:ApiKey"]);
    }

    [Fact]
    public void Surrounding_quotes_are_stripped()
    {
        var configuration = Load("""Ai:Providers:openai-compatible:ApiKey="sk-or-v1-abc123" """);

        Assert.Equal("sk-or-v1-abc123", configuration["Ai:Providers:openai-compatible:ApiKey"]);
    }

    [Fact]
    public void An_empty_value_does_not_shadow_appsettings()
    {
        // .env.example ships the key with no value; leaving it blank must not blank out
        // whatever appsettings already configured.
        File.WriteAllText(Path.Combine(_directory, ".env"), "Ai:Providers:openai-compatible:ApiKey=");

        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Ai:Providers:openai-compatible:ApiKey"] = "from-appsettings",
            })
            .AddDotEnvFile(_directory)
            .Build();

        Assert.Equal("from-appsettings", configuration["Ai:Providers:openai-compatible:ApiKey"]);
    }

    [Fact]
    public void The_file_is_found_by_walking_up_from_the_project_folder()
    {
        // `dotnet run --project Orbita.Api` starts inside Orbita.Api/, but the .env
        // lives at the repository root.
        var nested = Directory.CreateDirectory(Path.Combine(_directory, "Orbita.Api", "bin")).FullName;

        var configuration = Load("Ai:Providers:openai-compatible:ApiKey=sk-or-v1-abc123", nested);

        Assert.Equal("sk-or-v1-abc123", configuration["Ai:Providers:openai-compatible:ApiKey"]);
    }

    [Fact]
    public void A_missing_file_is_not_an_error()
    {
        // Production, CI and the integration tests all run without a .env.
        var empty = Directory.CreateTempSubdirectory("orbita-dotenv-empty").FullName;

        var configuration = new ConfigurationBuilder().AddDotEnvFile(empty).Build();

        Assert.Null(configuration["Ai:Providers:openai-compatible:ApiKey"]);

        Directory.Delete(empty, recursive: true);
    }

    public void Dispose() => Directory.Delete(_directory, recursive: true);
}
