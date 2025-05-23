﻿using CurseForge.APIClient;
using CurseForge.APIClient.Models;
using CurseForge.APIClient.Models.Games;
using CurseForge.APIClient.Models.Mods;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Newtonsoft.Json;
using StackExchange.Redis;
using System.Collections.Concurrent;
using System.IO.Compression;
using System.Text.RegularExpressions;

namespace CFLookup.Pages
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

        public Game? FoundGame { get; set; }
        public Mod? FoundMod { get; set; }
        public List<Category>? FoundCategories { get; set; }

        public ConcurrentDictionary<string, (Game game, Category? category, List<Mod> mods)> FoundMods { get; set; }

        readonly Regex modsTomlRegex = new(@"displayName=""(.*?)""", RegexOptions.Compiled | RegexOptions.IgnoreCase);

        public IndexModel(ILogger<IndexModel> logger, ApiClient cfApiClient, ConnectionMultiplexer connectionMultiplexer)
        {
            _logger = logger;
            _cfApiClient = cfApiClient;
            _redis = connectionMultiplexer.GetDatabase(5);
        }

        public async Task OnGet(int? projectId = null, int? fileId = null, bool? rcf = false, string search = null)
        {
            if (fileId.HasValue)
            {
                FileSearchField = fileId.Value.ToString();
                projectId = await SharedMethods.SearchModFileAsync(_redis, _cfApiClient, fileId.Value);
            }

            if (projectId.HasValue)
            {
                ProjectSearchField = projectId.Value.ToString();
                FoundMod = await SharedMethods.SearchModAsync(_redis, _cfApiClient, projectId.Value);
            }

            if (!string.IsNullOrWhiteSpace(search))
            {
                ProjectSearchField = search;
                await OnPostAsync();
            }

            IsDiscord = false; //Request.Headers.UserAgent.Any(ua => ua.Contains("Discordbot"));

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

            if (FoundMod != null)
            {
                FoundGame = (await _cfApiClient.GetGameAsync(FoundMod.GameId)).Data;
                FoundCategories = (await _cfApiClient.GetCategoriesAsync(FoundMod.GameId)).Data;
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
            var couldParseFileId = int.TryParse(FileSearchField, out var fileId);
            var couldParseProjectId = int.TryParse(ProjectSearchField, out var projectId);

            IsDiscord = Request.Headers.UserAgent.Any(ua => ua.Contains("Discordbot"));

            if (couldParseFileId)
            {
                var tmpProjectId = await SharedMethods.SearchModFileAsync(_redis, _cfApiClient, fileId);

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

            // Handle if the input in the project search field is a comma separated list of IDs and then serve a list of those mods found
            if (ProjectSearchField.Contains(','))
            {
                var projectIds = ProjectSearchField.Split(',').Select(p => int.TryParse(p, out var id) ? id : -1).Where(p => p != -1).ToList();

                var foundMods = await SharedMethods.SearchModsAsync(_redis, _cfApiClient, projectIds);

                if (foundMods.Count > 0)
                {
                    FoundMods = new ConcurrentDictionary<string, (Game game, Category? category, List<Mod> mods)>();
                    foreach (var mod in foundMods)
                    {
                        if (!FoundMods.ContainsKey($"{mod.GameId}-{string.Join("|", mod.Categories.Select(i => i.Id))}"))
                        {
                            var game = await SharedMethods.GetGameInfo(_redis, _cfApiClient, mod.GameId);
                            var gameInfo = new List<Game> { game };
                            var categoryInfo = await SharedMethods.GetCategoryInfo(_redis, _cfApiClient, gameInfo, game.Slug);
                            if (categoryInfo != null)
                            {
                                var category = categoryInfo.FirstOrDefault(c => mod.Categories.Any(mc => mc.Id == c.Id));
                                FoundMods.TryAdd($"{mod.GameId}-{string.Join("|", mod.Categories.Select(i => i.Id))}", (game, category, new List<Mod>()));
                            }
                            else
                            {
                                FoundMods.TryAdd($"{mod.GameId}-{string.Join("|", mod.Categories.Select(i => i.Id))}", (game, null, new List<Mod>()));
                            }
                            FoundMods.TryAdd($"{mod.GameId}-{string.Join("|", mod.Categories.Select(i => i.Id))}", (game, null, new List<Mod>()));
                        }

                        FoundMods[$"{mod.GameId}-{string.Join("|", mod.Categories.Select(i => i.Id))}"].mods.Add(mod);
                    }

                    return Page();
                }
                else
                {
                    ErrorMessage = "Could not find any of the projects";
                    return Page();
                }
            }

            if (string.IsNullOrEmpty(ProjectSearchField) || !couldParseProjectId)
            {
                if (!string.IsNullOrWhiteSpace(ProjectSearchField))
                {
                    if (Uri.TryCreate(ProjectSearchField, UriKind.Absolute, out var url))
                    {
                        if (url.Host.EndsWith("cflookup.com") || url.Host.EndsWith("curseforge.com"))
                        {
                            if (url.Segments.Length > 0 && url.Segments.Length >= 4)
                            {
                                var game = url.Segments[1].TrimEnd('/');
                                var classId = url.Segments[2].TrimEnd('/');
                                var slug = url.Segments[3].TrimEnd('/');

                                var gameInfo = await SharedMethods.GetGameInfo(_redis, _cfApiClient);
                                var categoryInfo = await SharedMethods.GetCategoryInfo(_redis, _cfApiClient, gameInfo, game);

                                var foundMod = await SharedMethods.SearchForSlug(_redis, _cfApiClient, gameInfo, categoryInfo, game, classId, slug);

                                if (foundMod == null)
                                {
                                    ErrorMessage = "Could not find a project for that URL";
                                    return Page();
                                }

                                FoundGame = gameInfo?.FirstOrDefault(g => g.Id == foundMod.GameId);
                                FoundMod = foundMod;
                                FoundCategories = categoryInfo;

                                return Page();
                            }
                        }
                        else
                        {
                            ErrorMessage = "Not a valid URL, only CurseForge domains are valid";
                            return Page();
                        }
                    }

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

            FoundMod = await SharedMethods.SearchModAsync(_redis, _cfApiClient, projectId);


            if (FoundMod == null)
            {
                ErrorMessage = "Could not find the project";
            }
            else
            {
                FoundGame = (await _cfApiClient.GetGameAsync(FoundMod.GameId)).Data;
                FoundCategories = (await _cfApiClient.GetCategoriesAsync(FoundMod.GameId)).Data;
            }

            return Page();
        }

        private async Task<ConcurrentDictionary<string, (Game game, Category category, List<Mod> mods)>> TryToFindSlug(string slug)
        {
            var returnValue = new ConcurrentDictionary<string, (Game game, Category category, List<Mod> mods)>();
            var gameClasses = new ConcurrentDictionary<Game, List<Category>>();
            var games = await SharedMethods.GetGameInfo(_redis, _cfApiClient);

            var gameClassTasks = games.Select(async game =>
            {
                var classes = (await SharedMethods.GetCategoryInfo(_redis, _cfApiClient, game.Id)).Where(c => c.IsClass ?? false).ToList() ?? new List<Category>();
                gameClasses.TryAdd(game, classes);
            });

            await Task.WhenAll(gameClassTasks);

            var sortedList = gameClasses.OrderByDescending(c => c.Key.Id == 432 || c.Key.Id == 1);

            var cachedSlugSearch = await _redis.StringGetAsync($"cf-slug-search-{slug}");

            if (!cachedSlugSearch.IsNullOrEmpty)
            {
                return JsonConvert.DeserializeObject<ConcurrentDictionary<string, (Game game, Category category, List<Mod> mods)>>(cachedSlugSearch);
            }

            var keyTasks = sortedList.Select(async kv =>
            {
                var gameCategoryTasks = kv.Value.Select(async cat =>
                {
                    try
                    {
                        var modSearch = await _cfApiClient.SearchModsAsync(kv.Key.Id, cat.Id, slug: slug);
                        if (modSearch.Data.Count > 0)
                        {
                            if (!returnValue.ContainsKey($"{kv.Key.Id}-{cat.Id}"))
                            {
                                returnValue.TryAdd($"{kv.Key.Id}-{cat.Id}", (kv.Key, cat, new List<Mod>()));
                            }

                            returnValue[$"{kv.Key.Id}-{cat.Id}"].mods.AddRange(modSearch.Data);
                        }
                    }
                    catch
                    {
                        // Empty, because.. yeah
                    }
                });

                await Task.WhenAll(gameCategoryTasks);
            });

            await Task.WhenAll(keyTasks);

            await _redis.StringSetAsync($"cf-slug-search-{slug}", JsonConvert.SerializeObject(returnValue), TimeSpan.FromMinutes(5));

            return returnValue;
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