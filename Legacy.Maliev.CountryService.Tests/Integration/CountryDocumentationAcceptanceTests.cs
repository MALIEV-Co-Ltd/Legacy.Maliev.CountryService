using System.Xml.Linq;

namespace Legacy.Maliev.CountryService.Tests.Integration;

public sealed class CountryDocumentationAcceptanceTests
{
    [Theory]
    [InlineData("Legacy.Maliev.CountryService.Api", "T:Legacy.Maliev.CountryService.Api.Controllers.CountriesController")]
    [InlineData("Legacy.Maliev.CountryService.Data", "T:Legacy.Maliev.CountryService.Data.CountryDbContext")]
    [InlineData("Legacy.Maliev.CountryService.Application", "T:Legacy.Maliev.CountryService.Application.Services.CountryApplicationService")]
    [InlineData("Legacy.Maliev.CountryService.Domain", "T:Legacy.Maliev.CountryService.Domain.Country")]
    public void BuiltDocumentation_ContainsItsOwnAssemblyAndPublicMembers(string assembly, string member)
    {
        var document = XDocument.Load(Path.Combine(AppContext.BaseDirectory, assembly + ".xml"));
        Assert.Equal(assembly, document.Root!.Element("assembly")!.Element("name")!.Value);
        Assert.Contains(document.Root.Element("members")!.Elements("member"), value => (string?)value.Attribute("name") == member);
    }
}
