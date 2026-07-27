namespace Legacy.Maliev.CountryService.Tests.Workflows;

public sealed class MainWorkflowContractTests
{
    private static readonly string Workflow = File.ReadAllText(
        FindRepositoryFile(".github", "workflows", "ci-main.yml"));

    [Fact]
    public void MainWorkflow_OnlyRunsValidation()
    {
        Assert.Contains("name: CI - Main", Workflow, StringComparison.Ordinal);
        Assert.Contains("validate:", Workflow, StringComparison.Ordinal);
        Assert.Contains("uses: ./.github/workflows/_build-and-test.yml", Workflow, StringComparison.Ordinal);
        Assert.DoesNotContain("needs: validate", Workflow, StringComparison.Ordinal);
        Assert.DoesNotContain("LEGACY_DEPLOY_ENABLED", Workflow, StringComparison.Ordinal);
        Assert.DoesNotContain("environment: legacy-production", Workflow, StringComparison.Ordinal);
        Assert.DoesNotContain("id-token: write", Workflow, StringComparison.Ordinal);
        Assert.DoesNotContain("docker push", Workflow, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("gcloud auth", Workflow, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("kustomize edit", Workflow, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("git push", Workflow, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("GITOPS_PAT", Workflow, StringComparison.Ordinal);
    }

    [Fact]
    public void MainWorkflow_UsesReadOnlyWorkflowPermissions()
    {
        Assert.Contains("permissions:\n  contents: read", Workflow, StringComparison.Ordinal);
        Assert.DoesNotContain("id-token:", Workflow, StringComparison.Ordinal);
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
