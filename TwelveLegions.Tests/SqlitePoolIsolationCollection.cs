using Xunit;

namespace TwelveLegions.Tests;

[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class SqlitePoolIsolationCollection
{
    public const string Name = "SQLite pool isolation";
}
