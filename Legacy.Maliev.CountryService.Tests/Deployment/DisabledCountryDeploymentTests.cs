using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;

using YamlDotNet.RepresentationModel;

namespace Legacy.Maliev.CountryService.Tests.Deployment;

/// <summary>Guards the actual dormant Country manifest and its offline release binding.</summary>
public sealed class DisabledCountryDeploymentTests
{
    private const string ImageRepository = "asia-southeast1-docker.pkg.dev/maliev-website/maliev-website-artifact-prod/legacy-maliev-country-service";
    private const string PublishRunPrefix = "https://github.com/MALIEV-Co-Ltd/Legacy.Maliev.CountryService/actions/runs/";
    private static readonly string Root = FindRepositoryRoot();
    private static readonly string Template = File.ReadAllText(Path.Combine(Root, "deploy", "disabled", "deployment.yaml.template"));

    /// <summary>The authored rollout retains the exact source Country headroom contract.</summary>
    [Fact]
    public void DisabledTemplate_CountryRollout_UsesOneReplicaOneSurgeAndZeroUnavailable()
    {
        var document = Parse(Template);
        Assert.Equal("apps/v1", Scalar(document, "apiVersion"));
        Assert.Equal("Deployment", Scalar(document, "kind"));
        var metadata = Mapping(document, "metadata");
        Assert.Equal("legacy-maliev-country-service", Scalar(metadata, "name"));
        Assert.Equal("maliev-legacy", Scalar(metadata, "namespace"));
        Assert.Equal("UNRENDERED_DISABLED_TEMPLATE", Scalar(Mapping(metadata, "annotations"), "maliev.com/deployment-state"));
        var spec = Mapping(document, "spec");
        Assert.Equal("1", Scalar(spec, "replicas"));
        var strategy = Mapping(spec, "strategy");
        Assert.Equal("RollingUpdate", Scalar(strategy, "type"));
        Assert.Equal("1", Scalar(Mapping(strategy, "rollingUpdate"), "maxSurge"));
        Assert.Equal("0", Scalar(Mapping(strategy, "rollingUpdate"), "maxUnavailable"));
        Assert.Equal("UNRENDERED_COUNTRY_IMAGE_DIGEST_REQUIRED", Scalar(Container(document), "image"));
    }

    /// <summary>Existing desired-state secret, health and resource policies survive the service-owned draft.</summary>
    [Fact]
    public void DisabledTemplate_ExistingCountryPolicy_PreservesManagedSecretHealthAndResources()
    {
        var document = Parse(Template);
        var pod = Mapping(Mapping(Mapping(document, "spec"), "template"), "spec");
        Assert.Equal("false", Scalar(pod, "automountServiceAccountToken"));
        Assert.Equal("true", Scalar(Mapping(pod, "securityContext"), "runAsNonRoot"));
        var container = Container(document);
        var envFrom = Assert.IsType<YamlSequenceNode>(container.Children[new YamlScalarNode("envFrom")]);
        var secret = Assert.IsType<YamlMappingNode>(Assert.Single(envFrom.Children));
        Assert.Equal("legacy-maliev-country-service", Scalar(Mapping(secret, "secretRef"), "name"));
        var resources = Mapping(container, "resources");
        Assert.Equal("25m", Scalar(Mapping(resources, "requests"), "cpu"));
        Assert.Equal("96Mi", Scalar(Mapping(resources, "requests"), "memory"));
        Assert.Equal("200m", Scalar(Mapping(resources, "limits"), "cpu"));
        Assert.Equal("192Mi", Scalar(Mapping(resources, "limits"), "memory"));
        Assert.Equal("/countries/liveness", Scalar(Mapping(Mapping(container, "livenessProbe"), "httpGet"), "path"));
        Assert.Equal("/countries/readiness", Scalar(Mapping(Mapping(container, "readinessProbe"), "httpGet"), "path"));
        Assert.Equal("12", Scalar(Mapping(container, "startupProbe"), "failureThreshold"));
        Assert.Equal("true", Scalar(Mapping(container, "securityContext"), "readOnlyRootFilesystem"));
        Assert.Equal("false", Scalar(Mapping(container, "securityContext"), "allowPrivilegeEscalation"));
    }

    /// <summary>The renderer changes only the image and explicit release annotations, without activating anything.</summary>
    [Fact]
    public async Task Render_StructurallyValidCallerRelease_ChangesOnlyReleaseBindingAndRemainsDisabled()
    {
        // Synthetic structural controls are never a published image or release-acceptance receipt.
        var digest = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes("synthetic-country-render-control")));
        var image = ImageRepository + "@sha256:" + digest;
        var commit = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes("synthetic-country-commit-control")))[..40];
        var result = await Render(image, commit, PublishRunPrefix + "1");
        Assert.Equal(0, result.ExitCode);
        var rendered = Parse(result.Output);
        Assert.Equal(image, Scalar(Container(rendered), "image"));
        var annotations = Mapping(Mapping(rendered, "metadata"), "annotations");
        Assert.Equal(commit, Scalar(annotations, "maliev.com/accepted-release-commit"));
        Assert.Equal(PublishRunPrefix + "1", Scalar(annotations, "maliev.com/accepted-publish-run-url"));
        Assert.Equal("RENDERED_DISABLED_DRAFT", Scalar(annotations, "maliev.com/deployment-state"));
        var allowed = new HashSet<string>(StringComparer.Ordinal)
        {
            "/spec/template/spec/containers/0/image",
            "/metadata/annotations/maliev.com/accepted-release-commit",
            "/metadata/annotations/maliev.com/accepted-publish-run-url",
            "/metadata/annotations/maliev.com/deployment-state"
        };
        Assert.Equal(Leaves(Parse(Template)).Where(pair => !allowed.Contains(pair.Key)).OrderBy(pair => pair.Key),
            Leaves(rendered).Where(pair => !allowed.Contains(pair.Key)).OrderBy(pair => pair.Key));
        Assert.DoesNotContain("__ACCEPTED_", result.Output, StringComparison.Ordinal);
        Assert.DoesNotContain("UNRENDERED_", result.Output, StringComparison.Ordinal);
    }

    /// <summary>Floating, foreign, malformed and zero image references cannot produce a draft.</summary>
    [Theory]
    [InlineData("latest")]
    [InlineData(ImageRepository + ":latest")]
    [InlineData(ImageRepository + "@sha256:invalid")]
    [InlineData(ImageRepository + "@sha256:0000000000000000000000000000000000000000000000000000000000000000")]
    [InlineData("example.invalid/other@sha256:abcdef0123456789abcdef0123456789abcdef0123456789abcdef0123456789")]
    [InlineData(ImageRepository + "@sha256:abcdef0123456789abcdef0123456789abcdef0123456789abcdef0123456789\n")]
    [InlineData(ImageRepository + "@sha256:abcdef0123456789abcdef0123456789abcdef0123456789abcdef0123456789\r\n")]
    public async Task Render_InvalidImage_RefusesWithoutManifestOutput(string image)
    {
        var result = await Render(image, "01113637c751c7bea1354517be7f8b56a1637bb9", PublishRunPrefix + "1");
        Assert.NotEqual(0, result.ExitCode);
        Assert.True(string.IsNullOrWhiteSpace(result.Output));
    }

    /// <summary>Missing release inputs and invalid commit or evidence identities fail before output.</summary>
    [Theory]
    [InlineData("", "1")]
    [InlineData("main", "1")]
    [InlineData("0000000000000000000000000000000000000000", "1")]
    [InlineData("01113637c751c7bea1354517be7f8b56a1637bb9", "0")]
    [InlineData("01113637c751c7bea1354517be7f8b56a1637bb9", "1\n")]
    [InlineData("01113637c751c7bea1354517be7f8b56a1637bb9", "1\r\n")]
    [InlineData("01113637c751c7bea1354517be7f8b56a1637bb9\n", "1")]
    [InlineData("01113637c751c7bea1354517be7f8b56a1637bb9\r\n", "1")]
    public async Task Render_InvalidReleaseBinding_RefusesWithoutManifestOutput(string commit, string run)
    {
        var digest = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes("synthetic-country-render-control")));
        var result = await Render(ImageRepository + "@sha256:" + digest, commit, PublishRunPrefix + run);
        Assert.NotEqual(0, result.ExitCode);
        Assert.True(string.IsNullOrWhiteSpace(result.Output));
    }

    /// <summary>Render is an offline transformation; no deployment hook or provider command is introduced.</summary>
    [Fact]
    public async Task Render_NoReleaseArguments_RefusesAndDoesNotInvokeProviderTools()
    {
        var result = await Render();
        Assert.NotEqual(0, result.ExitCode);
        Assert.True(string.IsNullOrWhiteSpace(result.Output));
        var script = File.ReadAllText(Path.Combine(Root, "deploy", "Render-CountryDeployment.ps1"));
        foreach (var command in new[] { "kubectl", "gcloud", "docker", "Invoke-Expression", "Invoke-WebRequest", "Invoke-RestMethod" })
        {
            Assert.DoesNotContain(command, script, StringComparison.OrdinalIgnoreCase);
        }
        Assert.False(Directory.Exists(Path.Combine(Root, "deploy", "disabled", "overlays")));
        Assert.False(File.Exists(Path.Combine(Root, "deploy", "disabled", "kustomization.yaml")));
    }

    private static async Task<(int ExitCode, string Output)> Render(params string[] values)
    {
        var start = new ProcessStartInfo("pwsh")
        {
            WorkingDirectory = Root,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };
        foreach (var argument in new[] { "-NoProfile", "-NonInteractive", "-File", Path.Combine(Root, "deploy", "Render-CountryDeployment.ps1") })
        {
            start.ArgumentList.Add(argument);
        }
        var names = new[] { "-AcceptedImage", "-AcceptedReleaseCommit", "-AcceptedPublishRunUrl" };
        for (var index = 0; index < values.Length; index++)
        {
            start.ArgumentList.Add(names[index]);
            start.ArgumentList.Add(values[index]);
        }
        using var process = Process.Start(start) ?? throw new InvalidOperationException("Cannot start the offline manifest renderer.");
        var output = process.StandardOutput.ReadToEndAsync();
        var error = process.StandardError.ReadToEndAsync();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        try
        {
            await process.WaitForExitAsync(timeout.Token);
        }
        catch (OperationCanceledException)
        {
            process.Kill(entireProcessTree: true);
            throw new TimeoutException("Offline Country renderer exceeded 30 seconds.");
        }
        await Task.WhenAll(output, error);
        return (process.ExitCode, await output);
    }

    private static YamlMappingNode Parse(string source)
    {
        var stream = new YamlStream();
        stream.Load(new StringReader(source));
        return Assert.IsType<YamlMappingNode>(Assert.Single(stream.Documents).RootNode);
    }

    private static YamlMappingNode Mapping(YamlMappingNode node, string key) =>
        Assert.IsType<YamlMappingNode>(node.Children[new YamlScalarNode(key)]);

    private static string? Scalar(YamlMappingNode node, string key) =>
        Assert.IsType<YamlScalarNode>(node.Children[new YamlScalarNode(key)]).Value;

    private static YamlMappingNode Container(YamlMappingNode document)
    {
        var pod = Mapping(Mapping(Mapping(document, "spec"), "template"), "spec");
        var containers = Assert.IsType<YamlSequenceNode>(pod.Children[new YamlScalarNode("containers")]);
        return Assert.IsType<YamlMappingNode>(Assert.Single(containers.Children));
    }

    private static Dictionary<string, string?> Leaves(YamlNode node, string path = "")
    {
        var result = new Dictionary<string, string?>(StringComparer.Ordinal);
        if (node is YamlScalarNode scalar)
        {
            result.Add(path, scalar.Value);
        }
        else if (node is YamlMappingNode mapping)
        {
            foreach (var child in mapping.Children)
            {
                foreach (var leaf in Leaves(child.Value, path + "/" + Assert.IsType<YamlScalarNode>(child.Key).Value))
                {
                    result.Add(leaf.Key, leaf.Value);
                }
            }
        }
        else if (node is YamlSequenceNode sequence)
        {
            for (var index = 0; index < sequence.Children.Count; index++)
            {
                foreach (var leaf in Leaves(sequence.Children[index], path + "/" + index))
                {
                    result.Add(leaf.Key, leaf.Value);
                }
            }
        }
        return result;
    }

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "Legacy.Maliev.CountryService.slnx")))
        {
            directory = directory.Parent;
        }
        return directory?.FullName ?? throw new DirectoryNotFoundException("Country repository not found.");
    }
}
