using Discord;
using Discord.Interactions;
using Discord.WebSocket;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using System.Reflection;

namespace CFDiscordBot
{
    public partial class DiscordBot(
        ILogger logger,
        DiscordShardedClient discordClient,
        string botToken,
        IServiceProvider serviceProvider
    ) : BackgroundService
    {
        private InteractionService? interactionService;

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            discordClient.ShardReady += DiscordClient_ShardReady;

            discordClient.Log += Log;

            await discordClient.LoginAsync(TokenType.Bot, botToken);
            await discordClient.StartAsync();

            await Task.Delay(-1, stoppingToken);

            await discordClient.StopAsync();
            await discordClient.LogoutAsync();
        }

        private async Task DiscordClient_ShardReady(DiscordSocketClient _shardClient)
        {
            discordClient.ShardReady -= DiscordClient_ShardReady;

            interactionService = new InteractionService(_shardClient);

            interactionService.Log += Log;

            logger.LogInformation("Registering slash commands");
            await interactionService.AddModulesAsync(Assembly.GetEntryAssembly(), serviceProvider);
            await interactionService.RegisterCommandsGloballyAsync(true);

            discordClient.InteractionCreated += DiscordClient_InteractionCreated; ;

            logger.LogInformation("Slash commands registered");
        }

        private async Task Log(LogMessage msg)
        {
            switch (msg.Severity)
            {
                case LogSeverity.Critical:
                case LogSeverity.Error:
                    logger.LogError(msg.Exception, msg.Message);
                    break;
                case LogSeverity.Warning:
                    logger.LogWarning(msg.Exception, msg.Message);
                    break;
                case LogSeverity.Info:
                    logger.LogInformation(msg.Exception, msg.Message);
                    break;
                case LogSeverity.Verbose:
                case LogSeverity.Debug:
                    logger.LogDebug(msg.Exception, msg.Message);
                    break;
            }
            await Task.CompletedTask;
        }

        private async Task DiscordClient_InteractionCreated(SocketInteraction interaction)
        {
            logger.LogDebug("Interaction received: {Interaction}", interaction);
            var ctx = new ShardedInteractionContext(discordClient, interaction);
            await interactionService!.ExecuteCommandAsync(ctx, serviceProvider);
            logger.LogDebug("Interaction handled");
        }
    }
}
