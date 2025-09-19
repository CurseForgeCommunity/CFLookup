using CurseForge.APIClient;
using CurseForge.APIClient.Models;
using CurseForge.APIClient.Models.Games;
using CurseForge.APIClient.Models.Mods;
using Microsoft.AspNetCore.Mvc;
using StackExchange.Redis;

namespace CFLookup
{
    [ApiController]
    public class ApiController : ControllerBase
    {
        private readonly ILogger<ApiController> _logger;
        private readonly ApiClient _cfApiClient;
        private readonly IDatabase _redis;

        public ApiController(ILogger<ApiController> logger, ApiClient cfApiClient, ConnectionMultiplexer connectionMultiplexer)
        {
            _logger = logger;
            _cfApiClient = cfApiClient;
            _redis = connectionMultiplexer.GetDatabase(5);
        }

        [HttpGet("/{game}/{category}/{slug}.json")]
        public async Task<IActionResult> GetSlugProject(string game, string category, string slug)
        {
            try
            {
                var gameInfo = await SharedMethods.GetGameInfo(_redis, _cfApiClient);
                var categoryInfo = await SharedMethods.GetCategoryInfo(_redis, _cfApiClient, gameInfo, game);

                var searchForSlug = await SharedMethods.SearchForSlug(_redis, _cfApiClient, gameInfo, categoryInfo, game, category, slug);

                if (searchForSlug == null)
                {
                    return NotFound();
                }

                return new JsonResult(searchForSlug);
            }
            catch
            {
                return NotFound();
            }
        }

        [HttpGet("/api/search/slug/{slug}")]
        public async Task<IActionResult> SearchSlug(string slug)
        {
            try
            {
                var searchForSlug = await SharedMethods.TryToFindSlug(_redis, _cfApiClient, slug);

                var resultData = new List<ApiSlugResultRecord>();

                foreach (var item in searchForSlug)
                {
                    (var game, var category, var modList) = item.Value;

                    resultData.Add(new ApiSlugResultRecord(
                        game,
                        category,
                        modList
                    ));
                }

                var searchResult = new
                {
                    data = resultData.Take(100),
                    totalResults = resultData.Count,
                    __help = "This endpoint will only list up to 100 results."
                };

                return new JsonResult(searchResult);
            }
            catch
            {
                return Problem("An error occurred while searching for the slug");
            }
        }

        [HttpGet("/{projectId:int}.json")]
        public async Task<IActionResult> GetProject(int projectId)
        {
            try
            {
                var project = await SharedMethods.SearchModAsync(_redis, _cfApiClient, projectId);

                if (project == null)
                {
                    return NotFound();
                }

                return new JsonResult(project);
            }
            catch
            {
                return NotFound();
            }
        }

        [HttpGet("/file-{fileId:int}.json")]
        public async Task<IActionResult> GetFile(int fileId)
        {
            try
            {
                (var project, var file, var changelog) = await SharedMethods.GetFileInfoAsync(_redis, _cfApiClient, fileId);

                if (file == null)
                {
                    return NotFound();
                }

                return new JsonResult(new
                {
                    Project = project,
                    File = file,
                    Changelog = changelog
                });
            }
            catch
            {
                return NotFound();
            }
        }
    }

    internal record ApiSlugResultRecord(Game Game, Category Category, List<Mod> Mods);
}
