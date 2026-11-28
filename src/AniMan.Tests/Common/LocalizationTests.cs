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
    public void Translations_UseTheSamePlaceholdersAsEnglish()
    {
        // AM-122: a Turkish text missing "{1}" silently drops a value; one with an extra "{2}"
        // throws FormatException in string.Format at runtime.
        var projectRoot = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "AniMan"));
        var en = LoadValues(Path.Combine(projectRoot, "Resources", "Strings.resx"));
        var tr = LoadValues(Path.Combine(projectRoot, "Resources", "Strings.tr.resx"));

        var mismatches = en.Keys.Intersect(tr.Keys)
            .Where(key => !Placeholders(en[key]).SetEquals(Placeholders(tr[key])))
            .Select(key => $"{key}: en {{{string.Join(",", Placeholders(en[key]).Order())}}} / tr {{{string.Join(",", Placeholders(tr[key]).Order())}}}")
            .ToList();

        mismatches.Should().BeEmpty();
    }

    private static Dictionary<string, string> LoadValues(string path) =>
        XDocument.Load(path).Root!.Elements("data")
            .ToDictionary(e => e.Attribute("name")!.Value, e => e.Element("value")?.Value ?? "");

    /// <summary>Format item indexes ("{0}", "{1:N0}"), ignoring escaped braces ("{{").</summary>
    private static HashSet<int> Placeholders(string text) =>
        System.Text.RegularExpressions.Regex.Matches(text.Replace("{{", "").Replace("}}", ""), @"\{(\d+)(?:[,:][^}]*)?\}")
            .Select(m => int.Parse(m.Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture))
            .ToHashSet();

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
