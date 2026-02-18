using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace DayTrace.Infrastructure.Data;

/// <summary>
/// Design-time factory for EF Core migrations (dotnet ef).
/// </summary>
public class DesignTimeDbContextFactory : IDesignTimeDbContextFactory<DayTraceDbContext>
{
    public DayTraceDbContext CreateDbContext(string[] args)
    {
        var connectionString = Environment.GetEnvironmentVariable("ConnectionStrings__DefaultConnection")
            ?? "Host=localhost;Port=5432;Database=daytrace;Username=daytrace;Password=daytrace_dev";

        var optionsBuilder = new DbContextOptionsBuilder<DayTraceDbContext>();
        optionsBuilder.UseNpgsql(connectionString);
        optionsBuilder.ConfigureWarnings(w =>
            w.Ignore(RelationalEventId.PendingModelChangesWarning));

        return new DayTraceDbContext(optionsBuilder.Options);
    }
}
