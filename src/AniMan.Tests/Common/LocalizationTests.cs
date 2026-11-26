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

    [Fact]
    public void EveryErrorKind_HasALocalizedMessage()
    {
        // ErrorText.For(kind) resolves "Error_{kind}"; a missing key would show the raw key to the user.
        var projectRoot = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "AniMan"));
        foreach (var file in new[] { "Strings.resx", "Strings.tr.resx" })
        {
            var keys = XDocument.Load(Path.Combine(projectRoot, "Resources", file)).Root!
                .Elements("data").Select(e => e.Attribute("name")!.Value).ToHashSet();

            foreach (var kind in Enum.GetValues<AniMan.Core.Common.ErrorKind>())
                keys.Should().Contain($"Error_{kind}", $"{file} needs a message for ErrorKind.{kind}");
        }
    }
}
