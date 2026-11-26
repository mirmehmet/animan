namespace AniMan.Tests.TestDoubles;

/// <summary>
/// <c>CatalogService</c> keeps process-wide static state (in-flight refreshes, the negative
/// streaming cache, the write gate). Test classes that create one or clear its caches share
/// this collection so they run one after another: otherwise one class's <c>ClearCaches()</c>
/// wipes another's negative cache mid-test.
/// </summary>
[CollectionDefinition(Name)]
public sealed class CatalogStateCollection
{
    public const string Name = "CatalogService static state";
}
