using YamlDotNet.RepresentationModel;

namespace Legacy.Maliev.CountryService.Tests.Workflows;

public sealed class PublicationDependencyTests
{
    [Fact]
    public void Publisher_ProvidesPinnedDockerDependencies_WithoutOpeningDeploymentGate()
    {
        var root = new DirectoryInfo(AppContext.BaseDirectory);
        while (root is not null && !File.Exists(Path.Combine(root.FullName, "Legacy.Maliev.CountryService.slnx")))
        {
            root = root.Parent;
        }

        Assert.NotNull(root);
        var yaml = new YamlStream();
        yaml.Load(new StringReader(File.ReadAllText(Path.Combine(root.FullName, ".github", "workflows", "publish-image.yml"))));
        var document = (YamlMappingNode)yaml.Documents[0].RootNode;
        var workflowPermissions = (YamlMappingNode)document.Children[new YamlScalarNode("permissions")];
        Assert.Single(workflowPermissions.Children);
        Assert.Equal("read", Value(workflowPermissions, "contents"));
        var jobs = (YamlMappingNode)document.Children[new YamlScalarNode("jobs")];
        var deploymentGate = (YamlMappingNode)jobs.Children[new YamlScalarNode("deployment-gate")];
        Assert.Equal("vars.LEGACY_DEPLOY_ENABLED != 'true'", Value(deploymentGate, "if"));
        var publish = (YamlMappingNode)jobs.Children[new YamlScalarNode("publish")];
        Assert.Equal("vars.LEGACY_DEPLOY_ENABLED == 'true'", Value(publish, "if"));
        Assert.Equal("MALIEV-Co-Ltd/Legacy.Maliev.Workflows/.github/workflows/publish-image.yml@d583f55473f47f72d33b51060fa5d14e0974daf5", Value(publish, "uses"));
        var permissions = (YamlMappingNode)publish.Children[new YamlScalarNode("permissions")];
        Assert.Equal(3, permissions.Children.Count);
        Assert.Equal("read", Value(permissions, "contents"));
        Assert.Equal("read", Value(permissions, "actions"));
        Assert.Equal("write", Value(permissions, "id-token"));
        var inputs = (YamlMappingNode)publish.Children[new YamlScalarNode("with")];
        Assert.Equal(8, inputs.Children.Count);
        Assert.Equal("${{ vars.LEGACY_ARTIFACT_REGISTRY }}/legacy-maliev-country-service", Value(inputs, "image"));
        Assert.Equal("Legacy.Maliev.CountryService.Api/Dockerfile", Value(inputs, "dockerfile"));
        Assert.Equal("173d63218d9142b34075bca87f5c6d933bcbdfb6", Value(inputs, "legacy-service-defaults-ref"));
        Assert.Equal("fe17b8cea8ace3eef5434f8b29c9038f5c975613", Value(inputs, "compatibility-contracts-ref"));
        Assert.Equal(".", Value(inputs, "context"));
        Assert.Equal("legacy-production", Value(inputs, "environment"));
        Assert.Equal("${{ vars.LEGACY_WORKLOAD_IDENTITY_PROVIDER }}", Value(inputs, "workload-identity-provider"));
        Assert.Equal("${{ vars.LEGACY_COUNTRY_PUBLISHER_SERVICE_ACCOUNT }}", Value(inputs, "service-account"));
    }

    private static string? Value(YamlMappingNode mapping, string key) =>
        mapping.Children.TryGetValue(new YamlScalarNode(key), out var value) ? ((YamlScalarNode)value).Value : null;
}
