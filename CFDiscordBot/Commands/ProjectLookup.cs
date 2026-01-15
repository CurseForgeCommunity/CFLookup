using CurseForge.APIClient.Models.Mods;
using Discord;
using Discord.Interactions;
using System.Text;
using System.Text.Json;

namespace CFDiscordBot.Commands
{
    public partial class Lookup
    {
        [SlashCommand("projectid", "Looks up a project by its ID")]
        public async Task ProjectIdAsync(
            [Summary("id", "The ID of the project to look up")]
            int projectId,
            [Summary("GameVersion", "The version of the game you want to find info about for this project")]
            string? gameVersion = null,
            [Summary("Modloader", "The modloader you want to find info about for this project")]
            ModLoaderType? modLoader = null
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

            List<CurseForge.APIClient.Models.Files.File> matchingFiles = new();

            bool isFileSearch = false;

            if (gameVersion is not null || modLoader is not null)
            {
                isFileSearch = true;
                var fileReq = await apiClient.GetModFilesAsync(projectId, gameVersion, modLoader);
                if (fileReq?.Data.Count > 0)
                {
                    matchingFiles.AddRange(fileReq.Data);
                }
            }

            var summaryText = new StringBuilder();

            summaryText.AppendLine(mod.Summary);

            var projectEmbed = new EmbedBuilder
            {
                Title = "Project information",
                Color = Color.DarkOrange,
                Fields = []
            };

            projectEmbed.ThumbnailUrl = 
                Uri.TryCreate(mod.Logo?.ThumbnailUrl, UriKind.Absolute, out var logoUri) ? 
                    logoUri.AbsoluteUri : 
                    "https://www.curseforge.com/images/flame.svg";

            var categories = string.Join(", ", mod.Categories.Select(c => $"[{c.Name}]({c.Url})"));

            var fields = new List<EmbedFieldBuilder> {
                new() { Name = "Author", Value = string.Join(", ", mod.Authors.Select(c => $"[{c.Name}]({c.Url})")), IsInline = true },
                new() { Name = "Status", Value = mod.Status.ToString(), IsInline = true },
                new() { Name = "Created", Value = $"<t:{mod.DateCreated.ToUnixTimeSeconds()}:F>", IsInline = true },
                new() { Name = "Modified", Value = $"<t:{mod.DateModified.ToUnixTimeSeconds()}:F>", IsInline = true },
                new() { Name = "Released", Value = $"<t:{mod.DateReleased.ToUnixTimeSeconds()}:F>", IsInline = true },
                new() { Name = "Downloads", Value = mod.DownloadCount.ToString("n0"), IsInline = true },
                new() { Name = "Mod Distribution", Value = mod.AllowModDistribution ?? true ? "Allowed" : "Not allowed", IsInline = true },
                new() { Name = "Is available", Value = mod.IsAvailable ? "Yes" : "No", IsInline = true }
            };
            
            if (matchingFiles.Count > 0)
            {
                List<string> fieldsToRemove = ["Created", "Modified", "Released", "Downloads", "Is available", "Status", "Mod Distribution"];
                fields.RemoveAll(m => fieldsToRemove.Any(i => i == m.Name));
                
                var latestFile = matchingFiles.OrderByDescending(f => f.FileDate).First();
                
                fields.AddRange([
                    new EmbedFieldBuilder { Name = "File Status", Value = latestFile.FileStatus.ToString(), IsInline = true },
                    new EmbedFieldBuilder { Name = "File Uploaded", Value = $"<t:{latestFile.FileDate.ToUnixTimeSeconds()}:F>", IsInline = true },
                    new EmbedFieldBuilder { Name = "File Downloads", Value = latestFile.DownloadCount.ToString("n0"), IsInline = true },
                    new EmbedFieldBuilder { Name = "Mod Distribution", Value = mod.AllowModDistribution ?? true ? "Allowed" : "Not allowed", IsInline = true },
                    new EmbedFieldBuilder { Name = "File is available", Value = latestFile.IsAvailable ? "Yes" : "No", IsInline = true }
                ]);

                if (mod.GameId == 432)
                {
                    var sides = latestFile.SortableGameVersions
                        .Where(i => i.GameVersionTypeId is 75208)
                        .Select(i => i.GameVersionName)
                        .ToList();

                    if (sides.Count > 0)
                    {
                        fields.Add(new EmbedFieldBuilder {  Name = "Environments", Value = string.Join(", ", sides), IsInline = false });
                    }
                    
                    var modLoaders = latestFile.SortableGameVersions
                        .Where(i => i.GameVersionTypeId is 68441)
                        .Select(i => i.GameVersionName)
                        .ToList();
                    
                    if (modLoaders.Count > 0)
                    {
                        fields.Add(new EmbedFieldBuilder {  Name = "Modloaders", Value = string.Join(", ", modLoaders), IsInline = false });
                    }
                }
                
                var gameVersions = latestFile.SortableGameVersions
                    .Where(v => !string.IsNullOrWhiteSpace(v.GameVersion))
                    .OrderByDescending(v => v.GameVersionPadded)
                    .Select(i => i.GameVersionName)
                    .ToList();
                if (gameVersions.Count > 0)
                {
                    fields.Add(new EmbedFieldBuilder {  Name = "Game versions", Value = string.Join(", ", gameVersions), IsInline = false });
                }
            }

            if (!string.IsNullOrWhiteSpace(categories))
            {
                fields.Add(new EmbedFieldBuilder { Name = "Categories", Value = categories, IsInline = false });
            }

            

            if (isFileSearch && matchingFiles.Count == 0)
            {
                var sb = new StringBuilder();
                if (gameVersion is not null)
                {
                    sb.AppendLine($"**Game version** - {gameVersion}");
                }

                if (modLoader is not null)
                {
                    sb.AppendLine($"**Modloader** - {modLoader}");
                }
                
                fields.Add(new EmbedFieldBuilder { Name = "File matching search criteria not found", Value = sb.ToString(), IsInline = false });
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
            if (mod is { GameId: 78022, AllowModDistribution: not null } && mod.AllowModDistribution.Value)
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
