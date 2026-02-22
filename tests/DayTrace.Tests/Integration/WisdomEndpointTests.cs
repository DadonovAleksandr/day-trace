using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using DayTrace.Domain.Entities;
using DayTrace.Infrastructure.Data;
using Microsoft.Extensions.DependencyInjection;

namespace DayTrace.Tests.Integration;

[Collection("Postgres")]
public class WisdomEndpointTests : IAsyncLifetime
{
    private readonly PostgresFixture _pg;
    private DayTraceWebFactory _factory = null!;

    public WisdomEndpointTests(PostgresFixture pg) => _pg = pg;

    public async Task InitializeAsync()
    {
        _factory = new DayTraceWebFactory(_pg.ConnectionString);
        await _factory.CleanDatabaseAsync();
    }

    public Task DisposeAsync()
    {
        _factory?.Dispose();
        return Task.CompletedTask;
    }

    [Fact]
    public async Task GetRandomWisdom_NoData_ReturnsNotFound()
    {
        var client = _factory.CreateClient();

        var response = await client.GetAsync("/wisdoms/random");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("not_found", body.GetProperty("error").GetString());
    }

    [Fact]
    public async Task GetRandomWisdom_WithSeed_ReturnsOk()
    {
        // Arrange: seed a wisdom
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<DayTraceDbContext>();
        db.Wisdoms.Add(new Wisdom
        {
            Text = "Тестовая мудрость",
            Category = "philosophy",
            Author = "Тест",
            CreatedAt = DateTime.UtcNow
        });
        await db.SaveChangesAsync();

        var client = _factory.CreateClient();

        // Act
        var response = await client.GetAsync("/wisdoms/random");

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("Тестовая мудрость", body.GetProperty("text").GetString());
        Assert.Equal("philosophy", body.GetProperty("category").GetString());
        Assert.Equal("Тест", body.GetProperty("author").GetString());
        Assert.True(body.GetProperty("id").GetInt32() > 0);
    }

    [Fact]
    public async Task GetRandomWisdom_NoAuth_ReturnsOk()
    {
        // Arrange: seed a wisdom
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<DayTraceDbContext>();
        db.Wisdoms.Add(new Wisdom
        {
            Text = "Анонимная мудрость",
            Category = "motivation",
            CreatedAt = DateTime.UtcNow
        });
        await db.SaveChangesAsync();

        // Act: no auth header
        var client = _factory.CreateClient();
        var response = await client.GetAsync("/wisdoms/random");

        // Assert: accessible without authentication
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("Анонимная мудрость", body.GetProperty("text").GetString());
        Assert.Null(body.GetProperty("author").GetString());
    }

    [Fact]
    public async Task GetRandomWisdom_MultipleWisdoms_ReturnsOne()
    {
        // Arrange: seed multiple wisdoms
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<DayTraceDbContext>();
        for (var i = 1; i <= 5; i++)
        {
            db.Wisdoms.Add(new Wisdom
            {
                Text = $"Мудрость {i}",
                Category = "science",
                CreatedAt = DateTime.UtcNow
            });
        }
        await db.SaveChangesAsync();

        var client = _factory.CreateClient();

        // Act
        var response = await client.GetAsync("/wisdoms/random");

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        var text = body.GetProperty("text").GetString()!;
        Assert.StartsWith("Мудрость", text);
        Assert.Equal("science", body.GetProperty("category").GetString());
    }
}
