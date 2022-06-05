using CurseForge.APIClient;
using CurseForge.APIClient.Models;
using CurseForge.APIClient.Models.Games;
using CurseForge.APIClient.Models.Mods;
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
        [BindProperty]
        public string FileSearchField { get; set; } = string.Empty;
        public string ErrorMessage { get; set; } = string.Empty;

        public Mod? FoundMod { get; set; }

        public Dictionary<string, (Game game, Category category, List<Mod> mods)> FoundMods { get; set; }

        readonly Regex modsTomlRegex = new(@"displayName=""(.*?)""", RegexOptions.Compiled | RegexOptions.IgnoreCase);

        public IndexModel(ILogger<IndexModel> logger, ApiClient cfApiClient, ConnectionMultiplexer connectionMultiplexer)
        {
            _logger = logger;
            _cfApiClient = cfApiClient;
            _redis = connectionMultiplexer.GetDatabase(5);
        }

        public async Task OnGet(int? projectId = null, long? fileId = null, bool? rcf = false)
        {
            if (fileId.HasValue)
            {
                FileSearchField = fileId.Value.ToString();
                projectId = await SearchModFileAsync(fileId.Value);
            }

            if (projectId.HasValue)
            {
                ProjectSearchField = projectId.Value.ToString();
                await SearchModAsync(projectId.Value);
            }

            IsDiscord = Request.Headers.UserAgent.Any(ua => ua.Contains("Discordbot"));

            if (rcf.HasValue && rcf.Value)
            {
                if (FoundMod != null)
                {
                    if (FoundMod.Links != null && !string.IsNullOrWhiteSpace(FoundMod.Links.WebsiteUrl))
                    {
                        if (IgnoredUserAgentsForRedirect.Any(i => Request.Headers.UserAgent.Any(ua => ua.Contains(i))))
                        {
                            return;
                        }

                        Response.Redirect(FoundMod.Links.WebsiteUrl);
                    }
                }
            }
        }

        public bool IsDiscord { get; set; } = false;

        internal string[] IgnoredUserAgentsForRedirect = new[]
        {
           "Twitterbot",
           "Discordbot",
           "facebookexternalhit",
           "LinkedInBot"
        };

        public async Task<IActionResult> OnPostAsync()
        {
            var couldParseFileId = long.TryParse(FileSearchField, out var fileId);

            var couldParseProjectId = int.TryParse(ProjectSearchField, out int projectId);

            IsDiscord = Request.Headers.UserAgent.Any(ua => ua.Contains("Discordbot"));

            if (couldParseFileId)
            {
                var tmpProjectId = await SearchModFileAsync(fileId);

                if (tmpProjectId.HasValue)
                {
                    projectId = tmpProjectId.Value;
                    couldParseProjectId = true;
                    ProjectSearchField = projectId.ToString();
                }
                else
                {
                    ErrorMessage = "Could not find mod with file id " + fileId;
                    return Page();
                }
            }

            if (string.IsNullOrEmpty(ProjectSearchField) || !couldParseProjectId)
            {
                if (!string.IsNullOrWhiteSpace(ProjectSearchField))
                {
                    var slugProjects = await TryToFindSlug(ProjectSearchField);
                    if (slugProjects.Count == 0)
                    {
                        ErrorMessage = "You need to enter a valid project id or slug to lookup the project. (We found nothing)";
                        return Page();
                    }
                    else if (slugProjects.Count == 1)
                    {
                        foreach (var gameMods in slugProjects)
                        {
                            if (gameMods.Value.mods.Count == 1)
                            {
                                projectId = gameMods.Value.mods[0].Id;
                            }
                            else
                            {
                                FoundMods = slugProjects;
                                break;
                            }
                        }
                    }
                    else
                    {
                        FoundMods = slugProjects;
                    }

                    if (FoundMods != null)
                    {
                        return Page();
                    }
                }
                else
                {
                    ErrorMessage = "You need to enter a valid project id to lookup the project.";
                    return Page();
                }
            }

            await SearchModAsync(projectId);

            return Page();
        }

        private async Task<Dictionary<string, (Game game, Category category, List<Mod> mods)>> TryToFindSlug(string slug)
        {
            var returnValue = new Dictionary<string, (Game game, Category category, List<Mod> mods)>();
            var gameClasses = new Dictionary<Game, List<Category>>();
            var games = await GetGameInfo();

            foreach (var game in games)
            {
                var classes = (await GetCategoryInfo(game.Id)).Where(c => c.IsClass ?? false).ToList() ?? new List<Category>();
                gameClasses.Add(game, classes);
            }

            var sortedList = gameClasses.OrderByDescending(c => c.Key.Id == 432 || c.Key.Id == 1);

            var cachedSlugSearch = await _redis.StringGetAsync($"cf-slug-search-{slug}");

            if (!cachedSlugSearch.IsNullOrEmpty)
            {
                return JsonConvert.DeserializeObject<Dictionary<string, (Game game, Category category, List<Mod> mods)>>(cachedSlugSearch);
            }

            foreach (var kv in sortedList)
            {
                foreach (var cat in kv.Value)
                {
                    var modSearch = await _cfApiClient.SearchModsAsync(kv.Key.Id, cat.Id, slug: slug);
                    if (modSearch.Data.Count > 0)
                    {
                        if (!returnValue.ContainsKey($"{kv.Key.Id}-{cat.Id}"))
                        {
                            returnValue.Add($"{kv.Key.Id}-{cat.Id}", (kv.Key, cat, new List<Mod>()));
                        }

                        returnValue[$"{kv.Key.Id}-{cat.Id}"].mods.AddRange(modSearch.Data);
                    }
                    await Task.Delay(25);
                }
            }

            await _redis.StringSetAsync($"cf-slug-search-{slug}", JsonConvert.SerializeObject(returnValue), TimeSpan.FromMinutes(5));

            return returnValue;
        }
        private async Task<List<Game>> GetGameInfo()
        {
            var cachedGames = await _redis.StringGetAsync("cf-games");

            if (!cachedGames.IsNullOrEmpty)
            {
                return JsonConvert.DeserializeObject<List<Game>>(cachedGames);
            }

            var games = await _cfApiClient.GetGamesAsync();
            await _redis.StringSetAsync("cf-games", JsonConvert.SerializeObject(games.Data), TimeSpan.FromMinutes(5));
            return games.Data;
        }

        private async Task<List<Category>> GetCategoryInfo(int gameId)
        {
            var cachedCategories = await _redis.StringGetAsync($"cf-categories-id-{gameId}");

            if (!cachedCategories.IsNullOrEmpty)
            {
                return JsonConvert.DeserializeObject<List<Category>>(cachedCategories);
            }

            var categories = await _cfApiClient.GetCategoriesAsync(gameId);
            await _redis.StringSetAsync($"cf-categories-id-{gameId}", JsonConvert.SerializeObject(categories.Data), TimeSpan.FromMinutes(5));
            await Task.Delay(25);
            return categories.Data;
        }

        private async Task<int?> SearchModFileAsync(long fileId)
        {
            ErrorMessage = string.Empty;
            var modResultCache = await _redis.StringGetAsync($"cf-file-{fileId}");
            if (!modResultCache.IsNull)
            {
                if (modResultCache == "empty")
                {
                    ErrorMessage = "File not found, or problems with the query. Try again later.";
                    FoundMod = null;
                    return null;
                }

                var obj = JsonConvert.DeserializeObject<GenericListResponse<CurseForge.APIClient.Models.Files.File>>(modResultCache);

                if (obj?.Data.Count > 0)
                {
                    return obj.Data[0].ModId;
                }
            }

            try
            {
                var modResult = await _cfApiClient.GetFilesAsync(new CurseForge.APIClient.Models.Files.GetModFilesRequestBody
                {
                    FileIds = new List<long> { fileId }
                });

                if (modResult.Data.Count > 0)
                {
                    var modJson = JsonConvert.SerializeObject(modResult);
                    await _redis.StringSetAsync($"cf-file-{fileId}", modJson, TimeSpan.FromMinutes(5));

                    return modResult.Data[0].ModId;
                }

                ErrorMessage = "File not found, or problems with the query. Try again later.";
                return null;
            }
            catch
            {
                ErrorMessage = "File not found, or problems with the query. Try again later.";
            }

            return null;
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