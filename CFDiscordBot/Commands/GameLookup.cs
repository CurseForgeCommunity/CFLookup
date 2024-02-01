using Discord;
using Discord.Interactions;

namespace CFDiscordBot.Commands
{
    public partial class Lookup
    {
        [SlashCommand("game", "Looks up a game by its ID")]
        public async Task GameIdAsync(
            [Summary("id", "The ID of the game to look up")]
            int gameId
        )
        {
            var game = await apiClient.GetGameAsync(gameId);

            if (game is null)
            {
                await RespondAsync($"Game with id {gameId} was not found.", ephemeral: true);

                await Task.Delay(2000);
                await Context.Interaction.DeleteOriginalResponseAsync();
                return;
            }

            var embed = new EmbedBuilder()
                .WithTitle(game.Data.Name)
                .WithUrl($"https://curseforge.com/{game.Data.Slug}")
                .WithColor(Color.Blue);

            if (!string.IsNullOrWhiteSpace(game.Data.Assets.IconUrl))
            {
                embed.WithThumbnailUrl(game.Data.Assets.IconUrl);
            }

            if (!string.IsNullOrWhiteSpace(game.Data.Assets.CoverUrl))
            {
                embed.WithImageUrl(game.Data.Assets.CoverUrl);
            }

            await RespondAsync(embeds: new[] { embed.Build() }, ephemeral: true);
        }
    }
}
