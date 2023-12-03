using CurseForge.APIClient;
using CurseForge.APIClient.Models.Mods;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using StackExchange.Redis;

namespace CFLookup.Pages
{
    public class CFFileEmbedModel : PageModel
    {
        private readonly ILogger<IndexModel> _logger;
        private readonly ApiClient _cfApiClient;
        private readonly IDatabaseAsync _redis;

        [BindProperty]
        public string ProjectSearchField { get; set; } = string.Empty;
        [BindProperty]
        public string FileSearchField { get; set; } = string.Empty;
        public string ErrorMessage { get; set; } = string.Empty;

        public int? FileId { get; set; }

        public Mod? FoundMod { get; set; }
        public CurseForge.APIClient.Models.Files.File? FoundFile { get; set; }
        public string? Changelog { get; set; }


        public CFFileEmbedModel(ILogger<IndexModel> logger, ApiClient cfApiClient, ConnectionMultiplexer connectionMultiplexer)
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

        public async Task<IActionResult> OnGet(int fileId)
        {
            FileId = fileId;

            try
            {
                var searchForFile = await SharedMethods.GetFileInfoAsync(_redis, _cfApiClient, fileId);

                if (searchForFile.file == null)
                {
                    return NotFound();
                }
                else
                {
                    FoundMod = searchForFile.mod;
                    FoundFile = searchForFile.file;
                    Changelog = searchForFile.changelog;
                }

                if (FoundMod?.Links != null && !string.IsNullOrWhiteSpace(FoundMod.Links.WebsiteUrl))
                {
                    if (!IgnoredUserAgentsForRedirect.Any(i => Request.Headers.UserAgent.Any(ua => ua.Contains(i))))
                    {
                        return Redirect($"{FoundMod.Links.WebsiteUrl}/files/{fileId}");
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
