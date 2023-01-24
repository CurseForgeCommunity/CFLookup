using CurseForge.APIClient;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using StackExchange.Redis;
using System.Collections.Concurrent;

namespace CFLookup.Pages
{
    public class MinecraftModpackStatsModel : PageModel
    {
        private readonly ApiClient _cfApiClient;
        private readonly IDatabaseAsync _redis;

        public ConcurrentDictionary<string, long> MinecraftStats = new ConcurrentDictionary<string, long>();
        public TimeSpan? CacheExpiration { get; set; }
        public MinecraftModpackStatsModel(ApiClient cfApiClient, ConnectionMultiplexer connectionMultiplexer)
        {
            _cfApiClient = cfApiClient;
            _redis = connectionMultiplexer.GetDatabase(5);
        }

        public async Task<IActionResult> OnGetAsync()
        {
            MinecraftStats = await SharedMethods.GetMinecraftModpackStatistics(_redis, _cfApiClient);
            CacheExpiration = await _redis.KeyTimeToLiveAsync("cf-mcmodpack-stats");

            return Page();
        }
    }
}
