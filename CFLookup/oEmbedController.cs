using CurseForge.APIClient;
using Microsoft.AspNetCore.Mvc;
using StackExchange.Redis;

namespace CFLookup
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
        public async Task<IActionResult> OEmbedResultAsync(uint projectId)
        {
            var mod = await SharedMethods.SearchModAsync(_redis, _cfApiClient, projectId);

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

            if (!string.IsNullOrWhiteSpace(mod.Links?.WebsiteUrl))
            {
                oembed.Add("url", mod.Links?.WebsiteUrl);
            }

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
    }
}
