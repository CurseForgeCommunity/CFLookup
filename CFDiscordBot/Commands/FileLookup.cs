using Discord;
using Discord.Interactions;
using System.Text;
using System.Text.RegularExpressions;

namespace CFDiscordBot.Commands
{
    public partial class Lookup
    {
        [SlashCommand("fileid", "Looks up a project by its file ID")]
        public async Task FileIdAsync(
            [Summary("id", "The ID of the file to look up")]
            int fileId
        )
        {
            CurseForge.APIClient.Models.Mods.Mod? mod = null;
            CurseForge.APIClient.Models.Files.File? file = null;
            var projectId = 0;
            try
            {
                var projectFiles =
                    await apiClient.GetFilesAsync(new CurseForge.APIClient.Models.Files.GetModFilesRequestBody
                        { FileIds = [fileId] });
                if (projectFiles.Data.Count > 0)
                {
                    file = projectFiles.Data.First();
                    var project = await apiClient.GetModAsync(file.ModId);
                    mod = project.Data;
                    projectId = project.Data.Id;
                }
                else
                {
                    await RespondAsync($"File with id {fileId} was not found.", ephemeral: true);
                    await Task.Delay(2000);
                    await Context.Interaction.DeleteOriginalResponseAsync();
                    return;
                }
            }
            catch
            {
                // Empty because, ugh
            }

            if (mod is null)
            {
                await RespondAsync($"File with id {fileId} was not found.", ephemeral: true);

                await Task.Delay(2000);
                await Context.Interaction.DeleteOriginalResponseAsync();
                return;
            }

            var summaryText = new StringBuilder();

            summaryText.AppendLine(mod.Summary);

            var projectEmbed = new EmbedBuilder
            {
                Title = "Project information",
                Color = Color.DarkOrange,
                Fields = []
            };

            if (Uri.TryCreate(mod.Logo?.ThumbnailUrl, UriKind.Absolute, out var logoUri))
            {
                projectEmbed.ThumbnailUrl = logoUri.AbsoluteUri;
            }
            else
            {
                projectEmbed.ThumbnailUrl = "https://www.curseforge.com/images/flame.svg";
            }

            var categories = string.Join(", ", mod.Categories.Select(c => $"[{c.Name}]({c.Url})"));

            var fields = new List<EmbedFieldBuilder>
            {
                new()
                {
                    Name = "Author", Value = string.Join(", ", mod.Authors.Select(c => $"[{c.Name}]({c.Url})")),
                    IsInline = true
                },
                new() { Name = "Status", Value = mod.Status.ToString(), IsInline = true },
                new() { Name = "Created", Value = $"<t:{mod.DateCreated.ToUnixTimeSeconds()}:F>", IsInline = true },
                new() { Name = "Modified", Value = $"<t:{mod.DateModified.ToUnixTimeSeconds()}:F>", IsInline = true },
                new() { Name = "Released", Value = $"<t:{mod.DateReleased.ToUnixTimeSeconds()}:F>", IsInline = true },
                new() { Name = "Downloads", Value = mod.DownloadCount.ToString("n0"), IsInline = true },
                new()
                {
                    Name = "Mod Distribution", Value = mod.AllowModDistribution ?? true ? "Allowed" : "Not allowed",
                    IsInline = true
                },
                new() { Name = "Is available", Value = mod.IsAvailable ? "Yes" : "No", IsInline = true }
            };

            if (!string.IsNullOrWhiteSpace(categories))
            {
                fields.Add(new EmbedFieldBuilder { Name = "Categories", Value = categories, IsInline = false });
            }

            if (file is { GameId: 1 })
            {
                fields.Add(new EmbedFieldBuilder
                {
                    Name = "Installable",
                    Value = file.Modules.Any(m => m.Name.EndsWith(".toc"))
                        ? "No, potentially invalid file structure"
                        : "Yes",
                    IsInline = false
                });
            }

            projectEmbed.Fields.AddRange(fields);

            projectEmbed.Footer = new EmbedFooterBuilder
            {
                IconUrl =
                    "https://cdn.discordapp.com/avatars/1199770925025984513/3f52d33635a688cfd24f0d78272aaf00.png?size=256",
                Text = "CurseForge"
            };

            if (mod.Screenshots.Count > 0)
            {
                if (Uri.TryCreate(mod.Screenshots.First().Url, UriKind.Absolute, out var screenshotUri))
                {
                    projectEmbed.ImageUrl = screenshotUri.AbsoluteUri;
                }
            }

            var buttons = new ComponentBuilder();

            if (!string.IsNullOrWhiteSpace(mod.Links?.WikiUrl))
            {
                buttons.WithButton(
                    style: ButtonStyle.Link,
                    label: "Wiki",
                    url: mod.Links.WikiUrl
                );
            }

            if (!string.IsNullOrWhiteSpace(mod.Links?.IssuesUrl))
            {
                buttons.WithButton(
                    style: ButtonStyle.Link,
                    label: "Issues",
                    url: mod.Links.IssuesUrl
                );
            }

            if (!string.IsNullOrWhiteSpace(mod.Links?.SourceUrl))
            {
                buttons.WithButton(
                    style: ButtonStyle.Link,
                    label: "Source",
                    url: mod.Links.SourceUrl
                );
            }

            buttons.WithButton(
                style: ButtonStyle.Link,
                label: "CFLookup",
                url: $"https://cflookup.com/{projectId}?fileId={fileId}"
            );

            if (!string.IsNullOrWhiteSpace(mod.Links?.WebsiteUrl))
            {
                buttons.WithButton(
                    style: ButtonStyle.Link,
                    label: "CurseForge",
                    url: mod.Links.WebsiteUrl
                );
            }

            // If the game is Minecraft Bedrock, we do an additional check if MCPEDL has the mod available through a slug lookup.
            if (mod.GameId == 78022 && mod.AllowModDistribution.HasValue && mod.AllowModDistribution.Value)
            {
                var client = httpClientFactory.CreateClient();
                client.DefaultRequestHeaders.TryAddWithoutValidation("User-Agent",
                    "CFLookup Discord Bot/1.0; (+cflookup@itssimple.se)");
                client.BaseAddress = new Uri("https://mcpedl.com/");

                var res = await client.GetAsync(mod.Slug);

                if (res != null && res.IsSuccessStatusCode &&
                    !res.RequestMessage!.RequestUri!.ToString().Contains("notfound"))
                {
                    buttons.WithButton(
                        style: ButtonStyle.Link,
                        label: "MCPEDL",
                        url: $"https://mcpedl.com/{mod.Slug}"
                    );
                }
            }

            await RespondAsync($"Project `{projectId}` is: **[{mod.Name}](https://cflookup.com/{projectId})**\n" +
                               $"{summaryText}",
                embeds: new[] { projectEmbed.Build() },
                components: buttons.Build()
            );
        }
    }
}