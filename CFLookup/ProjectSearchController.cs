using CurseForge.APIClient;
using Microsoft.AspNetCore.Mvc;
using Npgsql;
using StackExchange.Redis;

namespace CFLookup
{
    [Route("api/[controller]")]
    [ApiController]
    public class ProjectSearchController : ControllerBase
    {
        private readonly ApiClient _cfApiClient;
        private readonly IDatabaseAsync _redis;
        private readonly NpgsqlConnection _conn;

        public ProjectSearchController(ApiClient cfApiClient, ConnectionMultiplexer connectionMultiplexer, NpgsqlConnection conn)
        {
            _cfApiClient = cfApiClient;
            _redis = connectionMultiplexer.GetDatabase(5);
            _conn = conn;
        }

        [HttpGet("Games")]
        public async Task<IActionResult> Games()
        {
            var stats = await SharedMethods.GetGamesWithProjectStats(_redis, _conn);

            return new JsonResult(stats);
        }

        [HttpGet("Games/{gameId:int}")]
        public async Task<IActionResult> GameName(int gameId)
        {
            var name = await SharedMethods.GetGameNameForDisplay(_conn, _redis, _cfApiClient, gameId);

            return new JsonResult(new
            {
                gameId,
                name,
                label = name ?? $"GameId: {gameId}"
            });
        }

        [HttpGet("Categories/{gameId:int}")]
        public async Task<IActionResult> Categories(int gameId)
        {
            var categories = await SharedMethods.GetCategoriesForSearch(_redis, _cfApiClient, gameId);

            return new JsonResult(new
            {
                classes = categories.Where(c => c.IsClass == true).Select(c => new { c.Id, c.Name }),
                categories = categories.Where(c => c.IsClass != true).Select(c => new { c.Id, c.Name })
            });
        }

        [HttpGet("FieldRanges")]
        public async Task<IActionResult> FieldRanges()
        {
            var stats = await SharedMethods.GetFieldRangeStats(_redis, _conn);

            return new JsonResult(stats);
        }
    }
}
