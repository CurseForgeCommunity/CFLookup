using CurseForge.APIClient;
using Microsoft.AspNetCore.Mvc;
using Newtonsoft.Json;
using StackExchange.Redis;

namespace WhatCurseForgeProjectIsThis
{
    [Route("api/[controller]")]
    [ApiController]
    public class oEmbedController : ControllerBase
    {
        private readonly ApiClient _cfApiClient;
        private readonly IDatabaseAsync _redis;

        public oEmbedController(ApiClient cfApiClient, ConnectionMultiplexer connectionMultiplexer)
        {
            _cfApiClient = cfApiClient;
            _redis = connectionMultiplexer.GetDatabase(5);
        }

        [HttpGet("{projectId}.oembed.json")]
        public async Task<IActionResult> OEmbedResultAsync(int projectId)
        {
            var mod = await SearchModAsync(projectId);

            if (mod == null)
            {
                return NotFound();
            }

            var oembed = new Dictionary<string, object>
            {
                { "type", "rich" },
                { "version", "1.0" },
                { "title", mod.Name },
                { "provider_name", "CurseForge" },
                { "provider_url", "https://www.curseforge.com" },
                { "cache_age", 1800 },
                { "width", 400 },
                { "height", 400 }
            };

            if (mod.Logo != null && !string.IsNullOrWhiteSpace(mod.Logo.ThumbnailUrl))
            {
                oembed.Add("thumbnail_url", mod.Logo.ThumbnailUrl);
                oembed.Add("thumbnail_width", 256);
                oembed.Add("thumbnail_height", 256);
            }

            var summaryText = new System.Text.StringBuilder();
            var haveExtraLinebreak = false;
            summaryText.AppendLine(mod.Summary);

            if (!string.IsNullOrWhiteSpace(mod.Links?.IssuesUrl))
            {
                summaryText.AppendLine();
                summaryText.Append($"<a href=\"{mod.Links.IssuesUrl}\" target=\"_blank\">Issues</a> ");

            }

            if (!string.IsNullOrWhiteSpace(mod.Links?.WikiUrl))
            {
                summaryText.Append($"<a href=\"{mod.Links.WikiUrl}\" target=\"_blank\">Wiki/Docs</a> ");
            }

            if (!string.IsNullOrWhiteSpace(mod.Links?.SourceUrl))
            {
                summaryText.Append($"<a href=\"{mod.Links.SourceUrl}\" target=\"_blank\">Source</a>");
            }

            if (mod.LatestFilesIndexes?.Count > 0)
            {
                var gameVersionList = new List<string>();
                var modloaderList = new List<string>();

                foreach (var file in mod.LatestFilesIndexes)
                {
                    var modFile = mod.LatestFiles.FirstOrDefault(f => f.Id == file.FileId);
                    if (modFile?.IsAvailable ?? true)
                    {
                        if (!string.IsNullOrWhiteSpace(file.GameVersion))
                        {
                            gameVersionList.Add(file.GameVersion);
                        }
                        if (!string.IsNullOrWhiteSpace(file.ModLoader?.ToString()))
                        {
                            modloaderList.Add(file.ModLoader?.ToString());
                        }
                    }
                }
                var gameVersions = string.Join(", ", gameVersionList.Distinct());
                var modLoaders = string.Join(", ", modloaderList.Distinct());

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

            oembed.Add("html", summaryText.ToString().Trim().ReplaceLineEndings("<br />\n"));

            return new JsonResult(oembed);
        }

        private async Task<CurseForge.APIClient.Models.Mods.Mod?> SearchModAsync(int projectId)
        {
            var modResultCache = await _redis.StringGetAsync($"cf-mod-{projectId}");
            if (!modResultCache.IsNull)
            {
                if (modResultCache == "empty")
                {
                    return null;
                }

                return JsonConvert.DeserializeObject<CurseForge.APIClient.Models.Mods.Mod>(modResultCache); ;
            }

            try
            {
                var modResult = await _cfApiClient.GetModAsync(projectId);

                if (modResult.Data.Name == $"project-{projectId}" && modResult.Data.LatestFiles.Count > 0)
                {
                    var projectName = GetProjectNameFromFile(modResult.Data.LatestFiles.OrderByDescending(m => m.FileDate).First().DownloadUrl);
                    // Replace the project name with the filename for the projects latest available file
                    modResult.Data.Name = projectName;
                }

                var modJson = JsonConvert.SerializeObject(modResult.Data);

                await _redis.StringSetAsync($"cf-mod-{projectId}", modJson, TimeSpan.FromMinutes(5));

                return modResult.Data;
            }
            catch
            {
                return null;
            }
        }

        private string GetProjectNameFromFile(string url)
        {
            return Path.GetFileName(url);
        }
    }
}
