using DayTrace.Api.BackgroundServices;
using DayTrace.Api.Middleware;
using DayTrace.Bot;
using DayTrace.Infrastructure;
using DayTrace.Infrastructure.Data;
using NLog;
using NLog.Web;

var logger = LogManager.Setup().LoadConfigurationFromAppSettings().GetCurrentClassLogger();

try
{
    logger.Info("Starting DayTrace API");

    var builder = WebApplication.CreateBuilder(args);

    // NLog
    builder.Logging.ClearProviders();
    builder.Host.UseNLog();

    // Infrastructure (EF Core + PostgreSQL + Domain services)
    builder.Services.AddInfrastructure(builder.Configuration);

    // Telegram Bot
    builder.Services.AddTelegramBot(builder.Configuration);

    // Health checks
    builder.Services.AddHealthChecks()
        .AddDbContextCheck<DayTraceDbContext>("postgresql");

    // CORS
    var allowedOrigins = builder.Configuration.GetValue<string>("ALLOWED_ORIGINS") ?? "*";
    builder.Services.AddCors(options =>
    {
        options.AddDefaultPolicy(policy =>
        {
            if (allowedOrigins == "*")
            {
                policy.AllowAnyOrigin().AllowAnyMethod().AllowAnyHeader();
            }
            else
            {
                policy.WithOrigins(allowedOrigins.Split(',', StringSplitOptions.RemoveEmptyEntries))
                      .AllowAnyMethod()
                      .AllowAnyHeader()
                      .AllowCredentials();
            }
        });
    });

    // Background services
    builder.Services.AddHostedService<OperationIdCleanupService>();
    builder.Services.AddHostedService<PeriodJobWorkerService>();
    builder.Services.AddHostedService<StuckJobReaperService>();

    // Controllers + Swagger
    builder.Services.AddControllers();
    builder.Services.AddEndpointsApiExplorer();
    builder.Services.AddSwaggerGen(c =>
    {
        c.SwaggerDoc("v1", new() { Title = "DayTrace API", Version = "v1" });
    });

    var app = builder.Build();

    // Middleware pipeline
    app.UseMiddleware<CorrelationIdMiddleware>();
    app.UseMiddleware<GlobalExceptionHandlerMiddleware>();

    if (app.Environment.IsDevelopment())
    {
        app.UseSwagger();
        app.UseSwaggerUI(c => c.SwaggerEndpoint("/swagger/v1/swagger.json", "DayTrace API v1"));
    }

    app.UseCors();
    app.UseMiddleware<SessionAuthMiddleware>();
    app.UseMiddleware<ClientOperationIdMiddleware>();
    app.MapControllers();
    app.MapHealthChecks("/health/db");

    app.Run();
}
catch (Exception ex)
{
    logger.Error(ex, "Application stopped due to exception");
    throw;
}
finally
{
    LogManager.Shutdown();
}
