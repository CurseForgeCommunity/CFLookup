using CurseForge.APIClient;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Newtonsoft.Json;
using StackExchange.Redis;
using System.IO.Compression;
using System.Text.RegularExpressions;

namespace WhatCurseForgeProjectIsThis.Pages
{
    public class IndexModel : PageModel
    {
        private readonly ILogger<IndexModel> _logger;
        private readonly ApiClient _cfApiClient;
        private readonly IDatabaseAsync _redis;

        [BindProperty]
        public string ProjectSearchField { get; set; } = string.Empty;
        public string ErrorMessage { get; set; } = string.Empty;

        public CurseForge.APIClient.Models.Mods.Mod? FoundMod { get; set; }

        readonly Regex modsTomlRegex = new(@"displayName=""(.*?)""", RegexOptions.Compiled | RegexOptions.IgnoreCase);

        public IndexModel(ILogger<IndexModel> logger, ApiClient cfApiClient, ConnectionMultiplexer connectionMultiplexer)
        {
            _logger = logger;
            _cfApiClient = cfApiClient;
            _redis = connectionMultiplexer.GetDatabase(5);
        }

        public async Task OnGet(int? projectId = null)
        {
            if (projectId.HasValue)
            {
                ProjectSearchField = projectId.Value.ToString();
                await SearchModAsync(projectId.Value);
            }
        }

        public async Task<IActionResult> OnPostAsync()
        {
            var couldParseProjectId = int.TryParse(ProjectSearchField, out int projectId);
            if (string.IsNullOrEmpty(ProjectSearchField) && !couldParseProjectId)
            {
                ErrorMessage = "You need to enter a valid project id to lookup the project.";
                return Page();
            }

            await SearchModAsync(projectId);

            return Page();
        }

        private async Task SearchModAsync(int projectId)
        {
            ErrorMessage = string.Empty;
            var modResultCache = await _redis.StringGetAsync($"cf-mod-{projectId}");
            if (!modResultCache.IsNull)
            {
                if (modResultCache == "empty")
                {
                    ErrorMessage = "Project not found, or problems with the query. Try again later.";
                    FoundMod = null;
                    return;
                }

                FoundMod = JsonConvert.DeserializeObject<CurseForge.APIClient.Models.Mods.Mod>(modResultCache);
                return;
            }

            try
            {
                var modResult = await _cfApiClient.GetModAsync(projectId);

                if (modResult.Data.Name == $"project-{projectId}" && modResult.Data.LatestFiles.Count > 0)
                {
                    var projectName = await GetProjectNameFromFile(modResult.Data.LatestFiles.OrderByDescending(m => m.FileDate).First().DownloadUrl);
                    // Replace the project name with the filename for the projects latest available file
                    modResult.Data.Name = projectName;
                }

                var modJson = JsonConvert.SerializeObject(modResult.Data);

                await _redis.StringSetAsync($"cf-mod-{projectId}", modJson, TimeSpan.FromMinutes(5));

                FoundMod = modResult.Data;
            }
            catch
            {
                await _redis.StringSetAsync($"cf-mod-{projectId}", "empty", TimeSpan.FromMinutes(5));
                ErrorMessage = "Project not found, or problems with the query. Try again later.";
            }
        }

        private async Task<string> GetProjectNameFromFile(string url)
        {
            return Path.GetFileName(url);

            using (var hc = new HttpClient())
            {
                var fileBytes = await hc.GetByteArrayAsync(url);

                using (var ms = new MemoryStream(fileBytes))
                using (var zf = new ZipArchive(ms, ZipArchiveMode.Read, false))
                {
                    var modsToml = zf.GetEntry("META-INF/mods.toml");
                    if (modsToml != null)
                    {
                        using (StreamReader sr = new StreamReader(modsToml.Open()))
                        {
                            var fileContent = await sr.ReadToEndAsync();
                            var tomlMatch = modsTomlRegex.Match(fileContent);
                            if (tomlMatch != null)
                            {
                                return tomlMatch.Groups[1].Value.Trim();
                            }
                        }
                    }
                }
            }

            return Path.GetFileName(url);
        }
    }
}