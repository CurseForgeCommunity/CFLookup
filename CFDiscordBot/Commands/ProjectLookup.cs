using Discord;
using Discord.Interactions;

namespace CFDiscordBot.Commands
{
    public partial class Lookup
    {
        [SlashCommand("projectid", "Looks up a project by its ID")]
        public async Task ProjectIdAsync(
            [Summary("id", "The ID of the project to look up")]
            int projectId
        )
        {
            var project = await apiClient.GetModAsync(projectId);

            if (project is null)
            {
                await RespondAsync($"Project with id {projectId} was not found.", ephemeral: true);

                await Task.Delay(2000);
                await Context.Interaction.DeleteOriginalResponseAsync();
                return;
            }

            var embed = new EmbedBuilder()
                .WithTitle(project.Data.Name)
                .WithUrl(project.Data.Links.WebsiteUrl)
                .WithDescription(project.Data.Summary)
                .WithColor(Color.Blue)
                .Build();

            await RespondAsync(embeds: new[] { embed }, ephemeral: true);
        }
    }
}
