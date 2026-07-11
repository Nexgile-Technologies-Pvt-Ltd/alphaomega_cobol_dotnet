using CardDemo.Application.Abstractions;
using CardDemo.Infrastructure.Persistence;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace CardDemo.Tests;

/// <summary>
/// A deterministic <see cref="TimeProvider"/> that always returns a fixed instant.
/// Implemented locally (no external FakeTimeProvider package is available).
/// </summary>
internal sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
{
    private readonly DateTimeOffset _now = now;

    public override DateTimeOffset GetUtcNow() => _now;

    public override TimeZoneInfo LocalTimeZone => TimeZoneInfo.Utc;
}

/// <summary>
/// Helpers for spinning up a real <see cref="CardDemoDbContext"/> backed by a shared
/// in-memory SQLite database. The underlying <see cref="SqliteConnection"/> is kept
/// open for the lifetime of the owning object so the schema/data survive between
/// context instances.
/// </summary>
internal sealed class SqliteTestDatabase : IDisposable
{
    private readonly SqliteConnection _connection;

    public SqliteTestDatabase()
    {
        _connection = new SqliteConnection("DataSource=:memory:");
        _connection.Open();
    }

    public CardDemoDbContext NewContext()
    {
        var options = new DbContextOptionsBuilder<CardDemoDbContext>()
            .UseSqlite(_connection)
            .Options;
        return new CardDemoDbContext(options);
    }

    public void Dispose() => _connection.Dispose();
}

/// <summary>
/// Locates the repository's ASCII fixture directory
/// (<c>src/CardDemo.Console/fixtures/ASCII</c>) by walking up from the test output
/// directory until the folder is found.
/// </summary>
internal static class FixturePaths
{
    public static string AsciiRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            var candidate = Path.Combine(dir.FullName, "src", "CardDemo.Console", "fixtures", "ASCII");
            if (Directory.Exists(candidate))
                return candidate;
            dir = dir.Parent;
        }

        throw new DirectoryNotFoundException(
            "Could not locate src/CardDemo.Console/fixtures/ASCII from " + AppContext.BaseDirectory);
    }

    public static string DailyTran() => Path.Combine(AsciiRoot(), "dailytran.txt");
}

/// <summary>A no-op password hasher for tests that do not exercise verification.</summary>
internal sealed class StubPasswordHasher : IPasswordHasher
{
    public string Hash(string password) => "stub$" + password;

    public bool Verify(string password, string hash) => hash == "stub$" + password;
}
