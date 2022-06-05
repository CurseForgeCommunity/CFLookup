using CurseForge.APIClient;
using CurseForge.APIClient.Models;
using CurseForge.APIClient.Models.Games;
using CurseForge.APIClient.Models.Mods;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Newtonsoft.Json;
using StackExchange.Redis;

namespace WhatCurseForgeProjectIsThis.Pages
{
    public class CFEmbedModel : PageModel
    {
        private readonly ILogger<IndexModel> _logger;
        private readonly ApiClient _cfApiClient;
        private readonly IDatabaseAsync _redis;

        [BindProperty]
        public string ProjectSearchField { get; set; } = string.Empty;
        [BindProperty]
        public string FileSearchField { get; set; } = string.Empty;
        public string ErrorMessage { get; set; } = string.Empty;

        public string? Game { get; set; }
        public string? Category { get; set; }
        public string? Slug { get; set; }

        public bool IsDiscord { get; set; } = false;

        public Mod? FoundMod { get; set; }

        public CFEmbedModel(ILogger<IndexModel> logger, ApiClient cfApiClient, ConnectionMultiplexer connectionMultiplexer)
        {
            _logger = logger;
            _cfApiClient = cfApiClient;
            _redis = connectionMultiplexer.GetDatabase(5);
        }

        internal string[] IgnoredUserAgentsForRedirect = new[]
        {
           "Twitterbot",
           "Discordbot",
           "facebookexternalhit",
           "LinkedInBot"
        };

        public async Task<IActionResult> OnGet(string game, string category, string slug)
        {
            Game = game;
            Category = category;
            Slug = slug;

            IsDiscord = false; //Request.Headers.UserAgent.Any(ua => ua.Contains("Discordbot"));

            try
            {
                var gameInfo = await GetGameInfo();
                var categoryInfo = await GetCategoryInfo(gameInfo, game);

                var searchForSlug = await SearchForSlug(gameInfo, categoryInfo, game, category, slug);

                if (searchForSlug == null)
                {
                    if (!IgnoredUserAgentsForRedirect.Any(i => Request.Headers.UserAgent.Any(ua => ua.Contains(i))))
                    {
                        return Redirect($"https://www.curseforge.com/{game}/{category}/{slug}");
                    }
                }

                if (FoundMod?.Links != null && !string.IsNullOrWhiteSpace(FoundMod.Links.WebsiteUrl))
                {
                    if (!IgnoredUserAgentsForRedirect.Any(i => Request.Headers.UserAgent.Any(ua => ua.Contains(i))))
                    {
                        return Redirect(FoundMod.Links.WebsiteUrl);
                    }
                }

                return Page();
            }
            catch
            {
                return NotFound();
            }
        }

        private async Task<Mod?> SearchForSlug(List<Game> gameInfo, List<Category> categoryInfo, string game, string category, string slug)
        {
            var cachedResponse = await _redis.StringGetAsync($"cf-mod-{game}-{category}-{slug}");
            if (!cachedResponse.IsNullOrEmpty)
            {
                if (cachedResponse == "empty")
                {
                    return null;
                }

                var cachedMod = JsonConvert.DeserializeObject<Mod>(cachedResponse);
                FoundMod = cachedMod;

                return cachedMod;
            }

            var gameId = gameInfo.FirstOrDefault(x => x.Slug.Equals(game, StringComparison.InvariantCultureIgnoreCase))?.Id;
            var categoryId = categoryInfo.FirstOrDefault(x => x.Slug.Equals(category, StringComparison.InvariantCultureIgnoreCase))?.Id;

            if (!gameId.HasValue || !categoryId.HasValue)
            {
                return null;
            }

            var mod = await _cfApiClient.SearchModsAsync(gameId.Value, classId: categoryId, slug: slug);

            if (mod.Data.Count == 0)
            {
                mod = await _cfApiClient.SearchModsAsync(gameId.Value, categoryId: categoryId, slug: slug);
            }

            if (mod.Data.Count == 1)
            {
                FoundMod = mod.Data[0];

                await _redis.StringSetAsync($"cf-mod-{game}-{category}-{slug}", JsonConvert.SerializeObject(FoundMod), TimeSpan.FromMinutes(5));

                return mod.Data[0];
            }

            return null;
        }

        private async Task<List<Category>> GetCategoryInfo(List<Game> gameInfo, string game)
        {
            var cachedCategories = await _redis.StringGetAsync($"cf-categories-{game}");

            if (!cachedCategories.IsNullOrEmpty)
            {
                return JsonConvert.DeserializeObject<List<Category>>(cachedCategories);
            }

            var gameId = gameInfo.FirstOrDefault(x => x.Slug.Equals(game, StringComparison.InvariantCultureIgnoreCase))?.Id;

            var categories = await _cfApiClient.GetCategoriesAsync(gameId);
            await _redis.StringSetAsync($"cf-categories-{game}", JsonConvert.SerializeObject(categories.Data), TimeSpan.FromMinutes(5));

            return categories.Data;
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
    }
}
