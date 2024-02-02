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
            CurseForge.APIClient.Models.Games.Game gameInfo = null;
            try
            {
                var game = await apiClient.GetGameAsync(gameId);
                gameInfo = game.Data;
            }
            catch
            {
                // Empty because, ugh
            }

            if (gameInfo is null)
            {
                await RespondAsync($"Game with id {gameId} was not found.", ephemeral: true);

                await Task.Delay(2000);
                await Context.Interaction.DeleteOriginalResponseAsync();
                return;
            }

            var mods = await apiClient.SearchModsAsync(gameId, pageSize: 1);

            var embed = new EmbedBuilder()
                .WithTitle(gameInfo.Name)
                .WithDescription($"This game has {mods.Pagination.TotalCount:n0} mods available on CurseForge.")
                .WithUrl($"https://www.curseforge.com/{gameInfo.Slug}")
                .WithColor(Color.DarkOrange);

            if (!string.IsNullOrWhiteSpace(gameInfo.Assets.IconUrl))
            {
                embed.WithThumbnailUrl(gameInfo.Assets.IconUrl);
            }

            if (!string.IsNullOrWhiteSpace(gameInfo.Assets.CoverUrl))
            {
                embed.WithImageUrl(gameInfo.Assets.CoverUrl);
            }

            await RespondAsync(embeds: new[] { embed.Build() }, ephemeral: true);
        }
    }
}
