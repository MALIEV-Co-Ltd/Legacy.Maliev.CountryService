using System.Text.RegularExpressions;

namespace Legacy.Maliev.CountryService.Tests.Workflows;

public sealed class WorkflowContractTests
{
    private const string CheckoutSha = "9c091bb21b7c1c1d1991bb908d89e4e9dddfe3e0";
    private const string SharedWorkflowSha = "4f4cccc99ac46d46c2bcc487b0f5fa4f939b0191";
    private static readonly string Workflow = File.ReadAllText(FindRepositoryFile(".github", "workflows", "_build-and-test.yml"));

    [Fact]
    public void BuildAndTest_DelegatesValidationToExactSharedAction()
    {
        Assert.Contains(
            $"uses: MALIEV-Co-Ltd/Legacy.Maliev.Workflows/actions/dotnet-validate@{SharedWorkflowSha}",
            Workflow,
            StringComparison.Ordinal);
        Assert.Matches(@"(?m)^\s+with:\s*\r?\n\s+solution:\s+Legacy\.Maliev\.CountryService\.slnx\s*$", Workflow);
    }

    [Fact]
    public void BuildAndTest_DoesNotDuplicateSharedValidationCommands()
    {
        Assert.DoesNotContain("actions/setup-dotnet@", Workflow, StringComparison.Ordinal);
        Assert.DoesNotContain("actions/cache@", Workflow, StringComparison.Ordinal);

        foreach (var duplicatedCommand in new[] { "dotnet restore", "dotnet build", "dotnet test", "dotnet format", "dotnet list" })
        {
            Assert.DoesNotContain(duplicatedCommand, Workflow, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void BuildAndTest_PinsCallerOwnedCheckoutsWithoutCredentials()
    {
        Assert.Equal(3, Regex.Matches(Workflow, $@"uses:\s+actions/checkout@{CheckoutSha}\b").Count);
        Assert.Equal(3, Regex.Matches(Workflow, @"(?m)^\s+persist-credentials:\s+false\s*$").Count);
        Assert.Contains("ref: 085f24b8b6b19c5a8e932b229d93421b03bcd032", Workflow, StringComparison.Ordinal);
        Assert.Contains("ref: c533c12a8154f5cf7c4fbc9734e82a62705ac60f", Workflow, StringComparison.Ordinal);
        Assert.DoesNotMatch(@"uses:\s+actions/checkout@(?![0-9a-f]{40}\b)", Workflow);
    }

    [Fact]
    public void BuildAndTest_PreservesReadOnlyValidateJobContract()
    {
        Assert.Matches(@"(?m)^permissions:\s*\r?\n\s+contents:\s+read\s*$", Workflow);
        Assert.Matches(@"(?m)^\s{2}validate:\s*\r?\n\s{4}name:\s+validate\s*$", Workflow);
        Assert.DoesNotMatch(@"(?im)^\s*(id-token|packages|pull-requests|actions|checks|deployments|issues|statuses):\s+write\s*$", Workflow);
        Assert.DoesNotMatch(@"(?im)^\s*(secrets|environment|target):", Workflow);
    }

    private static string FindRepositoryFile(params string[] segments)
    {
        DirectoryInfo? directory = new(AppContext.BaseDirectory);

        while (directory is not null)
        {
            var candidate = Path.Combine([directory.FullName, .. segments]);
            if (File.Exists(candidate))
            {
                return candidate;
            }

            directory = directory.Parent;
        }

        throw new FileNotFoundException($"Could not find repository file '{Path.Combine(segments)}'.");
    }
}
