using Discord;
using Discord.Interactions;
using System.Text;
using System.Text.RegularExpressions;

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
            CurseForge.APIClient.Models.Mods.Mod? mod = null;
            try
            {
                var project = await apiClient.GetModAsync(projectId);
                mod = project.Data;
            }
            catch
            {
                // Empty because, ugh
            }

            if (mod is null)
            {
                await RespondAsync($"Project with id {projectId} was not found.", ephemeral: true);

                await Task.Delay(2000);
                await Context.Interaction.DeleteOriginalResponseAsync();
                return;
            }

            //var modFiles = new List<CurseForge.APIClient.Models.Files.File>();

            //var files = await apiClient.GetModFilesAsync(projectId, pageSize: 50);
            //modFiles.AddRange(files.Data);

            //var index = files.Pagination.Index;
            //while (modFiles.Count < files.Pagination.TotalCount)
            //{
            //    files = await apiClient.GetModFilesAsync(projectId, index: index++, pageSize: 50);
            //    modFiles.AddRange(files.Data);
            //}

            var summaryText = new StringBuilder();
            var haveExtraLinebreak = false;

            summaryText.AppendLine(mod.Summary);

            if (mod.LatestFilesIndexes?.Count > 0)
            {
                var gameVersionList = new List<string>();
                var modloaderList = new List<string>();

                /*foreach (var modFile in modFiles)
                {
                    if (modFile?.IsAvailable ?? true)
                    {
                        if (!string.IsNullOrWhiteSpace(file.GameVersion))
                        {
                            gameVersionList.Add(file.GameVersion);
                        }
                        if (!string.IsNullOrWhiteSpace(file.ModLoader?.ToString()))
                        {
                            modloaderList.Add(file.ModLoader.Value.ToString());
                        }
                    }
                }*/

                var gameVersions = string.Join(", ", gameVersionList.Distinct().OrderBy(gvt => Regex.Replace(gvt, "\\d+", m => m.Value.PadLeft(10, '0'))));
                var modLoaders = string.Join(", ", modloaderList.Distinct().OrderBy(gvt => Regex.Replace(gvt, "\\d+", m => m.Value.PadLeft(10, '0'))));

                if ((!string.IsNullOrWhiteSpace(gameVersions) || !string.IsNullOrWhiteSpace(modLoaders)) && !haveExtraLinebreak)
                {
                    summaryText.AppendLine();
                    haveExtraLinebreak = true;
                }

                if (!string.IsNullOrWhiteSpace(gameVersions))
                {
                    summaryText.AppendLine($"Game version(s): {gameVersions}");
                }

                if (!string.IsNullOrWhiteSpace(modLoaders))
                {
                    summaryText.AppendLine($"Modloader(s): {modLoaders}");
                }
            }

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

            var fields = new List<EmbedFieldBuilder> {
                new() { Name = "Author", Value = string.Join(", ", mod.Authors.Select(c => $"[{c.Name}]({c.Url})")), IsInline = true },
                new() { Name = "Status", Value = mod.Status.ToString(), IsInline = true },
                new() { Name = "Created", Value = $"<t:{mod.DateCreated.ToUnixTimeSeconds()}:F>", IsInline = true },
                new() { Name = "Modified", Value = $"<t:{mod.DateModified.ToUnixTimeSeconds()}:F>", IsInline = true },
                new() { Name = "Released", Value = $"<t:{mod.DateReleased.ToUnixTimeSeconds()}:F>", IsInline = true },
                new() { Name = "Downloads", Value = mod.DownloadCount.ToString("n0"), IsInline = true },
                new() { Name = "Mod Distribution", Value = mod.AllowModDistribution ?? true ? "Allowed" : "Not allowed", IsInline = true },
            };

            if (!string.IsNullOrWhiteSpace(categories))
            {
                fields.Add(new EmbedFieldBuilder { Name = "Categories", Value = categories, IsInline = false });
            }

            projectEmbed.Fields.AddRange(fields);

            projectEmbed.Footer = new EmbedFooterBuilder
            {
                IconUrl = "https://cdn.discordapp.com/avatars/1199770925025984513/3f52d33635a688cfd24f0d78272aaf00.png?size=256",
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
                url: $"https://cflookup.com/{projectId}"
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
                client.DefaultRequestHeaders.TryAddWithoutValidation("User-Agent", "CFLookup Discord Bot/1.0; (+cflookup@itssimple.se)");
                client.BaseAddress = new Uri("https://mcpedl.com/");

                var res = await client.GetAsync(mod.Slug);

                if (res != null && res.IsSuccessStatusCode && !res.RequestMessage!.RequestUri!.ToString().Contains("notfound"))
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

        [SlashCommand("cflookup", "Looks up a project by its ID", ignoreGroupNames: true)]
        public async Task CFLookup([Summary("id", "The ID")] int projectId) => await ProjectIdAsync(projectId);
    }
}
