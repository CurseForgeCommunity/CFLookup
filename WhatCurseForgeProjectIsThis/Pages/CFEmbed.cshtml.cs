using CurseForge.APIClient;
using CurseForge.APIClient.Models.Mods;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
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
                var gameInfo = await SharedMethods.GetGameInfo(_redis, _cfApiClient);
                var categoryInfo = await SharedMethods.GetCategoryInfo(_redis, _cfApiClient, gameInfo, game);

                var searchForSlug = await SharedMethods.SearchForSlug(_redis, _cfApiClient, gameInfo, categoryInfo, game, category, slug);

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
    }
}
