using AniMan.Infrastructure;

namespace AniMan.Tests.TestDoubles;

/// <summary>
/// A private app-data folder for one test (AM-060): <c>%TEMP%\AniMan.Tests\{guid}</c> with a
/// <c>covers</c> folder inside, deleted on dispose. Tests must never hand the bare temp root
/// to the app — cover sweeps delete every <c>*.jpg</c> in the covers folder.
/// </summary>
internal sealed class TestStorage : IDisposable
{
    public TestStorage()
    {
        Root = Path.Combine(Path.GetTempPath(), "AniMan.Tests", Guid.NewGuid().ToString("N"));
        Paths = new StoragePaths(Root, Path.Combine(Root, "covers"));
        Directory.CreateDirectory(Paths.CoversPath);
    }

    public string Root { get; }
    public StoragePaths Paths { get; }

    /// <summary>A path inside this test's folder (the file is not created).</summary>
    public string File(string name) => Path.Combine(Root, name);

    public void Dispose()
    {
        try { Directory.Delete(Root, recursive: true); }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }
}
