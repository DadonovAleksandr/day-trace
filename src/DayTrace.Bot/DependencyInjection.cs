using DayTrace.Bot.Configuration;
using DayTrace.Bot.Handlers;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Telegram.Bot;

namespace DayTrace.Bot;

public static class DependencyInjection
{
    public static IServiceCollection AddTelegramBot(this IServiceCollection services, IConfiguration configuration)
    {
        // Bind options
        services.Configure<TelegramBotOptions>(configuration.GetSection(TelegramBotOptions.SectionName));

        var botToken = configuration.GetSection(TelegramBotOptions.SectionName)
            .GetValue<string>("BotToken") ?? string.Empty;

        // Register TelegramBotClient as singleton
        if (!string.IsNullOrEmpty(botToken))
        {
            services.AddSingleton<ITelegramBotClient>(new TelegramBotClient(botToken));
        }

        // Register handlers
        services.AddScoped<BotUpdateHandler>();

        return services;
    }
}
