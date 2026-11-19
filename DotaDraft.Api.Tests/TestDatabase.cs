using DotaDraft.Api.Data;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Testcontainers.PostgreSql;

namespace DotaDraft.Api.Tests;

// One Postgres container for the whole test run, which Testcontainers removes when the run ends.
// The migrations are applied once to a template database, and each test gets its own copy of it,
// so tests are isolated from each other and can still run in parallel
public static class TestDatabase
{
    // The same Postgres version as docker-compose.yml
    private const string Image = "postgres:17-alpine";
    private const string TemplateName = "dotadraft_template";

    private static readonly Lazy<Task<PostgreSqlContainer>> Container = new(StartAsync);

    // Postgres refuses to copy a template while another session is using it, so copies are made one at a time
    private static readonly SemaphoreSlim CopyLock = new(1, 1);

    // A new database with the current schema and no data
    public static async Task<string> CreateAsync()
    {
        var container = await Container.Value;
        var name = $"test_{Guid.NewGuid():N}";

        await CopyLock.WaitAsync();
        try
        {
            await ExecuteAsync(container.GetConnectionString(), $"CREATE DATABASE {name} TEMPLATE {TemplateName}");
        }
        finally
        {
            CopyLock.Release();
        }

        return ConnectionString(container, name);
    }

    // A context on a new database, for tests that use services without the rest of the app
    public static async Task<AppDbContext> CreateContextAsync() => new(Options(await CreateAsync()));

    public static DbContextOptions<AppDbContext> Options(string connectionString) =>
        new DbContextOptionsBuilder<AppDbContext>().UseNpgsql(connectionString).Options;

    private static async Task<PostgreSqlContainer> StartAsync()
    {
        var container = new PostgreSqlBuilder(Image).Build();
        await container.StartAsync();

        await ExecuteAsync(container.GetConnectionString(), $"CREATE DATABASE {TemplateName}");
        await using (var db = new AppDbContext(Options(ConnectionString(container, TemplateName))))
            await db.Database.MigrateAsync();

        return container;
    }

    // Without pooling, connections close when they're disposed. Otherwise the migration would leave
    // connections open on the template, which can't be copied while anyone is connected, and each
    // test's database would hold idle connections that add up to Postgres's connection limit
    private static string ConnectionString(PostgreSqlContainer container, string database) =>
        new NpgsqlConnectionStringBuilder(container.GetConnectionString()) { Database = database, Pooling = false }
            .ConnectionString;

    private static async Task ExecuteAsync(string connectionString, string sql)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(sql, connection);
        await command.ExecuteNonQueryAsync();
    }
}
