using CreatorRizz.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Xunit;

namespace CreatorRizz.Persistence.Tests;

/// <summary>
/// Creates a throwaway PostgreSQL database and migrates it, so every test run proves the schema is
/// built from the EF Core migrations on a real server. Set CREATORRIZZ_TEST_POSTGRES to point at a
/// different server; the default matches the local Docker Compose service.
/// </summary>
public sealed class PostgresFixture : IAsyncLifetime
{
    private const string DefaultAdminConnectionString =
        "Host=localhost;Port=5432;Database=shorts;Username=shorts;Password=shorts";

    private string _databaseName = "";

    public string ConnectionString { get; private set; } = "";

    public async Task InitializeAsync()
    {
        var adminConnectionString = Environment.GetEnvironmentVariable("CREATORRIZZ_TEST_POSTGRES") ?? DefaultAdminConnectionString;
        _databaseName = $"creatorrizz_test_{Guid.NewGuid():N}";

        await using (var connection = new NpgsqlConnection(adminConnectionString))
        {
            await connection.OpenAsync();
            await using var command = new NpgsqlCommand($"CREATE DATABASE \"{_databaseName}\"", connection);
            await command.ExecuteNonQueryAsync();
        }

        ConnectionString = new NpgsqlConnectionStringBuilder(adminConnectionString)
        {
            Database = _databaseName,
            Pooling = false
        }.ConnectionString;

        await using var database = CreateContext();
        await database.Database.MigrateAsync();
    }

    public async Task DisposeAsync()
    {
        if (string.IsNullOrEmpty(_databaseName)) return;
        var adminConnectionString = Environment.GetEnvironmentVariable("CREATORRIZZ_TEST_POSTGRES") ?? DefaultAdminConnectionString;
        await using var connection = new NpgsqlConnection(adminConnectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(
            $"DROP DATABASE IF EXISTS \"{_databaseName}\" WITH (FORCE)", connection);
        await command.ExecuteNonQueryAsync();
    }

    public CreatorRizzDbContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<CreatorRizzDbContext>()
            .UseNpgsql(ConnectionString)
            .Options;
        return new CreatorRizzDbContext(options);
    }
}

[CollectionDefinition(Name)]
public sealed class PostgresCollection : ICollectionFixture<PostgresFixture>
{
    public const string Name = "postgres";
}
