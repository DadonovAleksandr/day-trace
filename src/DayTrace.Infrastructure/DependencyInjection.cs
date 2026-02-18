using DayTrace.Domain.Interfaces;
using DayTrace.Domain.Services;
using DayTrace.Infrastructure.Data;
using DayTrace.Infrastructure.Logging;
using DayTrace.Infrastructure.Repositories;
// Repo aliases
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
        services.AddScoped<IUserRepository, UserRepository>();
        services.AddScoped<IUserSettingsRepository, UserSettingsRepository>();
        services.AddScoped<IWeekScheduleHistoryRepository, WeekScheduleHistoryRepository>();
        services.AddScoped<ITimezoneHistoryRepository, TimezoneHistoryRepository>();
        services.AddScoped<ISessionRepository, SessionRepository>();
        services.AddScoped<IAuthReplayCacheRepository, AuthReplayCacheRepository>();
        services.AddScoped<IEventRepository, EventRepository>();
        services.AddScoped<IOperationIdCacheRepository, OperationIdCacheRepository>();
        services.AddScoped<IPeriodRunCounterRepository, PeriodRunCounterRepository>();
        services.AddScoped<IPeriodJobRepository, PeriodJobRepository>();
        services.AddScoped<ISummaryRepository, SummaryRepository>();
        services.AddScoped<IPromptDeliveryRepository, PromptDeliveryRepository>();

        // Domain services
        services.AddScoped<DateCalculationService>();
        services.AddScoped<UserRegistrationService>();
        services.AddScoped<TelegramAuthService>();
        services.AddScoped<PeriodRunCounterService>();
        services.AddScoped<SummaryGenerationService>();
        services.AddScoped<PeriodJobCreationService>();
        services.AddScoped<AutoTriggerService>();
        services.AddScoped<PeriodSelectionService>();

        return services;
    }
}
