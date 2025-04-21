using CurseForge.APIClient;
using Microsoft.AspNetCore.Mvc;
using StackExchange.Redis;

namespace CFLookup
{
    [Route("api/[controller]")]
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

        [HttpGet("/{projectId}.json")]
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

        [HttpGet("/file-{fileId}.json")]
        public async Task<IActionResult> GetFile(int fileId)
        {
            try
            {
                (var project, var file, var changelog) = await SharedMethods.GetFileInfoAsync(_redis, _cfApiClient, fileId);

                if (project == null)
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
}
