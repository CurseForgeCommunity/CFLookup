using CFDiscordBot;
using CurseForge.APIClient;
using Discord.WebSocket;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

var configuration = new ConfigurationBuilder()
    .SetBasePath(Directory.GetCurrentDirectory())
    .AddJsonFile("appsettings.json", optional: true, reloadOnChange: true)
    .AddJsonFile("appsettings.Dev.json", optional: true, reloadOnChange: true)
    .AddEnvironmentVariables("CFLOOKUP_")
    .Build();

await Host.CreateDefaultBuilder(args)
    .ConfigureServices(services =>
    {
        services.AddLogging(services => services.AddConsole());
        services.AddSingleton(x => new ApiClient(configuration["CurseForge:ApiKey"]));
        services.AddSingleton(x => new DiscordSocketConfig
        {
            UseInteractionSnowflakeDate = false
        });
        services.AddSingleton<DiscordShardedClient>();
        services.AddHostedService(x =>
            new DiscordBot(
                x.GetRequiredService<ILogger<DiscordBot>>(),
                x.GetRequiredService<DiscordShardedClient>(),
                configuration["Discord:BotToken"]!,
                x
            )
        );
    })
    .Build()
    .RunAsync();