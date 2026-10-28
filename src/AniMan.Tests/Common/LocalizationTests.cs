using System.Xml.Linq;
using FluentAssertions;

namespace AniMan.Tests.Common;

public class LocalizationTests
{
    [Fact]
    public void ResxFiles_HaveCompleteKeyParity()
    {
        // Locate Strings.resx and Strings.tr.resx relative to test execution folder
        var baseDir = AppContext.BaseDirectory;
        var projectRoot = Path.GetFullPath(Path.Combine(baseDir, "..", "..", "..", "..", "AniMan"));
        var enPath = Path.Combine(projectRoot, "Resources", "Strings.resx");
        var trPath = Path.Combine(projectRoot, "Resources", "Strings.tr.resx");

        File.Exists(enPath).Should().BeTrue("Strings.resx must exist at {0}", enPath);
        File.Exists(trPath).Should().BeTrue("Strings.tr.resx must exist at {0}", trPath);

        var enDoc = XDocument.Load(enPath);
        var trDoc = XDocument.Load(trPath);

        var enKeys = enDoc.Root!.Elements("data").Select(e => e.Attribute("name")!.Value).ToHashSet();
        var trKeys = trDoc.Root!.Elements("data").Select(e => e.Attribute("name")!.Value).ToHashSet();

        var missingInTr = enKeys.Except(trKeys).ToList();
        var missingInEn = trKeys.Except(enKeys).ToList();

        missingInTr.Should().BeEmpty("all English resource keys must have a Turkish translation");
        missingInEn.Should().BeEmpty("all Turkish resource keys must exist in default English resx");
    }
}
