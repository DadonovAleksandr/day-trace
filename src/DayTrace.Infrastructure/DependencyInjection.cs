using DayTrace.Domain.Interfaces;
using DayTrace.Domain.Services;
using DayTrace.Infrastructure.Data;
using DayTrace.Infrastructure.Logging;
using DayTrace.Infrastructure.Repositories;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace DayTrace.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        // PostgreSQL + EF Core
        services.AddDbContext<DayTraceDbContext>(options =>
        {
            options.UseNpgsql(configuration.GetConnectionString("DefaultConnection"), npgsql =>
            {
                npgsql.EnableRetryOnFailure(3);
            });
        });

        // Domain logger
        services.AddSingleton<IDomainLogger, NLogDomainLogger>();

        // Repositories
        services.AddScoped<IWeekScheduleHistoryRepository, WeekScheduleHistoryRepository>();

        // Domain services
        services.AddScoped<DateCalculationService>();

        return services;
    }
}
