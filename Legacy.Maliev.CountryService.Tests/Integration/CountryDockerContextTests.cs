using System.Diagnostics;
using System.Formats.Tar;

namespace Legacy.Maliev.CountryService.Tests.Integration;

public sealed class CountryDockerContextTests
{
    [Fact]
    public async Task RootDockerContext_ExcludesNestedPrivateArtifactsButKeepsRequiredBuildInputs()
    {
        var root = Path.Combine(Path.GetTempPath(), "country-context-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var image = "country-context-acceptance:" + Guid.NewGuid().ToString("N");
        string? container = null;
        try
        {
            File.Copy(FindRepositoryFile(".dockerignore"), Path.Combine(root, ".dockerignore"));
            // Marker fixtures contain no credentials. Docker, not a custom glob evaluator,
            // decides which files the actual root-context ignore policy exposes.
            var excluded = new[]
            {
                "nested/.env", "nested/.env.production", "nested/private.env", "nested/request.log",
                "nested/.git/config", "nested/.github/workflow.yml", "nested/bin/output.dll",
                "nested/obj/generated.xml", "nested/TestResults/result.trx", "nested/coverage/data.xml",
                "nested/node_modules/package/index.js", "nested/private.pfx", "nested/private.pem", "nested/private.key",
            };
            var retained = new[]
            {
                "nuget.config", "Directory.Build.props", "Legacy.Maliev.CountryService.Api/Program.cs",
                "Legacy.Maliev.CountryService.Api/Legacy.Maliev.CountryService.Api.csproj",
                "Legacy.Maliev.CountryService.Data/Migrations/Initial.cs", "Legacy.Maliev.CountryService.Domain/Country.cs",
                "Legacy.Maliev.CountryService.Api/assets/legitimate.xml",
            };
            foreach (var path in excluded.Concat(retained))
            {
                var file = Path.Combine(root, path.Replace('/', Path.DirectorySeparatorChar));
                Directory.CreateDirectory(Path.GetDirectoryName(file)!);
                File.WriteAllText(file, "country-context-marker");
            }
            File.WriteAllText(Path.Combine(root, "Dockerfile"), "FROM scratch\nCOPY . /context/\n");
            await RunDockerAsync("build", "--quiet", "--tag", image, root);
            container = (await RunDockerAsync("create", image, "/unused")).Trim();
            var archive = Path.Combine(root, "context.tar");
            await RunDockerAsync("export", "--output", archive, container);
            using var reader = new TarReader(File.OpenRead(archive));
            var names = new HashSet<string>(StringComparer.Ordinal);
            while (reader.GetNextEntry() is { } entry) names.Add(entry.Name.TrimStart('/'));
            Assert.All(excluded, path => Assert.DoesNotContain("context/" + path, names));
            Assert.All(retained, path => Assert.Contains("context/" + path, names));
        }
        finally
        {
            if (container is not null) await RunDockerAsync("rm", "--force", container);
            await RunDockerAsync("image", "rm", "--force", image);
            Directory.Delete(root, recursive: true);
        }
    }

    private static string FindRepositoryFile(string file)
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            if (File.Exists(Path.Combine(directory.FullName, "Legacy.Maliev.CountryService.slnx")))
                return Path.Combine(directory.FullName, file);
        }
        throw new DirectoryNotFoundException("Country repository root was not found.");
    }

    private static async Task<string> RunDockerAsync(params string[] arguments)
    {
        var start = new ProcessStartInfo("docker")
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        foreach (var argument in arguments) start.ArgumentList.Add(argument);
        using var process = Process.Start(start) ?? throw new InvalidOperationException("Docker could not start.");
        var output = process.StandardOutput.ReadToEndAsync();
        var error = process.StandardError.ReadToEndAsync();
        using var deadline = new CancellationTokenSource(TimeSpan.FromMinutes(2));
        try { await process.WaitForExitAsync(deadline.Token); }
        catch
        {
            process.Kill(entireProcessTree: true);
            await process.WaitForExitAsync();
            throw;
        }
        Assert.True(process.ExitCode == 0, await error);
        return await output;
    }
}
