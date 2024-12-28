using CurseForge.APIClient;
using CurseForge.APIClient.Models.Mods;
using Microsoft.AspNetCore.Mvc;
using StackExchange.Redis;
using System.Collections.Concurrent;
using System.Text.RegularExpressions;

namespace CFLookup
{
    [Route("api/[controller]")]
    [ApiController]
    public class StatsController : ControllerBase
    {
        private readonly ApiClient _cfApiClient;
        private readonly IDatabaseAsync _redis;
        private readonly MSSQLDB _db;

        public ConcurrentDictionary<string, ConcurrentDictionary<ModLoaderType, long>> MinecraftStats = new();
        public TimeSpan? CacheExpiration { get; set; }

        public StatsController(ApiClient cfApiClient, ConnectionMultiplexer connectionMultiplexer, MSSQLDB db)
        {
            _cfApiClient = cfApiClient;
            _redis = connectionMultiplexer.GetDatabase(5);
            _db = db;
        }

        [HttpGet("Minecraft/ModStats.json")]
        public async Task<IActionResult> MinecraftModStats()
        {
            var minecraftStats = (await SharedMethods.GetMinecraftModStatistics(_redis, _cfApiClient))
                .OrderBy(gvt => Regex.Replace(gvt.Key, "\\d+", m => m.Value.PadLeft(10, '0')));
            var cacheExpiration = await _redis.KeyTimeToLiveAsync("cf-mcmod-stats");

            return new JsonResult(new
            {
                Stats = minecraftStats,
                CacheExpiration = GetTruncatedTime(cacheExpiration.Value)
            });
        }

        [HttpGet("Minecraft/ModpackStats.json")]
        public async Task<IActionResult> MinecraftModpackStats()
        {
            var minecraftStats = (await SharedMethods.GetMinecraftModpackStatistics(_redis, _cfApiClient))
                .OrderBy(gvt => Regex.Replace(gvt.Key, "\\d+", m => m.Value.PadLeft(10, '0')));
            var cacheExpiration = await _redis.KeyTimeToLiveAsync("cf-mcmodpack-stats");

            return new JsonResult(new
            {
                Stats = minecraftStats,
                CacheExpiration = GetTruncatedTime(cacheExpiration.Value)
            });
        }

        [HttpGet("Minecraft/ModStatsOverTime.json")]
        public async Task<IActionResult> MinecraftModStatsOverTime()
        {
            var stats = await SharedMethods.GetMinecraftStatsOverTime(_db);

            return new JsonResult(stats);
        }

        [HttpGet("Minecraft/ModStatsOverTime.v2.json")]
        public async Task<IActionResult> MinecraftModStatsOverTimeV2()
        {
            var stats = await SharedMethods.GetMinecraftStatsOverTime(_db);

            var modloaderStats = new Dictionary<string, List<GameVersionTimestampInfo>>();

            foreach (var stat in stats)
            {
                var date = stat.Key;
                foreach (var modloaderHolder in stat.Value)
                {
                    var gameVersion = modloaderHolder.Key;

                    if (gameVersion.Contains("Snapshot", StringComparison.InvariantCultureIgnoreCase)) continue;

                    foreach (var gameInfo in modloaderHolder.Value)
                    {
                        var modloader = gameInfo.Key;
                        var count = gameInfo.Value;

                        if (modloader.Contains("LiteLoader", StringComparison.InvariantCultureIgnoreCase)) continue;

                        if (!modloaderStats.ContainsKey(modloader))
                        {
                            modloaderStats[modloader] = new List<GameVersionTimestampInfo>();
                        }

                        modloaderStats[modloader].Add(new GameVersionTimestampInfo
                        {
                            Timestamp = date,
                            GameVersion = gameVersion,
                            Count = count
                        });
                    }
                }
            }

            return new JsonResult(modloaderStats);
        }

        public class GameVersionTimestampInfo
        {
            public DateTimeOffset Timestamp { get; set; }
            public string GameVersion { get; set; }
            public long Count { get; set; }
        }

        private static DateTimeOffset GetTruncatedTime(TimeSpan timeSpan)
        {
            var now = DateTimeOffset.UtcNow;
            now = now.Add(timeSpan);

            now = new DateTimeOffset(now.Year, now.Month, now.Day, now.Hour, now.Minute, now.Second, now.Offset);

            return now;
        }
    }
}