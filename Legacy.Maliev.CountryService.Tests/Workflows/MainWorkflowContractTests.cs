namespace Legacy.Maliev.CountryService.Tests.Workflows;

public sealed class MainWorkflowContractTests
{
    private static readonly string Workflow = File.ReadAllText(
        FindRepositoryFile(".github", "workflows", "ci-main.yml"));

    [Fact]
    public void MainWorkflow_ValidatesButCannotPublishWithoutExplicitOwnerGate()
    {
        Assert.Contains("name: CI - Main", Workflow, StringComparison.Ordinal);
        Assert.Contains("validate:", Workflow, StringComparison.Ordinal);
        Assert.Contains("needs: validate", Workflow, StringComparison.Ordinal);
        Assert.Contains("if: vars.LEGACY_DEPLOY_ENABLED == 'true'", Workflow, StringComparison.Ordinal);
        Assert.Contains("environment: legacy-production", Workflow, StringComparison.Ordinal);
    }

    [Fact]
    public void MainWorkflow_PublishJobUsesLeastPrivilegeAndPublicLegacyDependencies()
    {
        Assert.Contains("permissions:\n      contents: read\n      id-token: write", Workflow, StringComparison.Ordinal);
        Assert.Contains("MALIEV-Co-Ltd/Legacy.Maliev.ServiceDefaults", Workflow, StringComparison.Ordinal);
        Assert.Contains("bcab875a7f703d1d9c2d535479e93653720eb62d", Workflow, StringComparison.Ordinal);
        Assert.Contains("MALIEV-Co-Ltd/Legacy.Maliev.CompatibilityContracts", Workflow, StringComparison.Ordinal);
        Assert.Contains("95c62eb6209411f5aada443b315447a2f76ca0cd", Workflow, StringComparison.Ordinal);
        Assert.Contains("actions/checkout@9c091bb21b7c1c1d1991bb908d89e4e9dddfe3e0", Workflow, StringComparison.Ordinal);
        Assert.DoesNotContain("MALIEV-Co-Ltd/Maliev.Aspire", Workflow, StringComparison.Ordinal);
        Assert.DoesNotContain("MALIEV-Co-Ltd/Maliev.MessagingContracts", Workflow, StringComparison.Ordinal);
        Assert.DoesNotContain("actions/checkout@v", Workflow, StringComparison.Ordinal);
    }

    private static string FindRepositoryFile(params string[] segments)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            var path = segments.Aggregate(directory.FullName, Path.Combine);
            if (File.Exists(path))
            {
                return path;
            }

            directory = directory.Parent;
        }

        throw new FileNotFoundException($"Repository file was not found: {Path.Combine(segments)}");
    }
}
