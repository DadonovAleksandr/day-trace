using DayTrace.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Testcontainers.PostgreSql;

namespace DayTrace.Tests.Integration;

/// <summary>
/// Shared PostgreSQL Testcontainer fixture for integration tests.
/// One container per test collection for performance.
/// </summary>
public class PostgresFixture : IAsyncLifetime
{
    private readonly PostgreSqlContainer _container;

    public string ConnectionString => _container.GetConnectionString();

    public PostgresFixture()
    {
        _container = new PostgreSqlBuilder("postgres:16-alpine")
            .WithDatabase("daytrace_test")
            .WithUsername("test")
            .WithPassword("test")
            .Build();
    }

    public async Task InitializeAsync()
    {
        await _container.StartAsync();

        // Apply EF Core migrations to create schema
        var options = new DbContextOptionsBuilder<DayTraceDbContext>()
            .UseNpgsql(ConnectionString)
            .Options;

        await using var db = new DayTraceDbContext(options);
        await db.Database.EnsureCreatedAsync();
    }

    public async Task DisposeAsync()
    {
        await _container.DisposeAsync();
    }
}

[CollectionDefinition("Postgres")]
public class PostgresCollection : ICollectionFixture<PostgresFixture>
{
}
