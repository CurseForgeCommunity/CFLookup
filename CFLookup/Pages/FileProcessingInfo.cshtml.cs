using CFLookup.Models;
using CurseForge.APIClient;
using CurseForge.APIClient.Models.Files;
using CurseForge.APIClient.Models.Games;
using CurseForge.APIClient.Models.Mods;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using StackExchange.Redis;
using System.Text.Json;

namespace CFLookup.Pages
{
    public class FileProcessingInfoModel : PageModel
    {
        private readonly ApiClient _cfApiClient;
        private readonly IDatabase _redis;
        private readonly MSSQLDB _db;

        public List<GameModFileProcessingInfo> ModFiles { get; set; } = new List<GameModFileProcessingInfo>();

        public FileProcessingInfoModel(ApiClient cfApiClient, ConnectionMultiplexer connectionMultiplexer, MSSQLDB db)
        {
            _cfApiClient = cfApiClient;
            _redis = connectionMultiplexer.GetDatabase(5);
            _db = db;
        }

        public async Task<IActionResult> OnGetAsync()
        {
            var gameProcessingInfo = await _db.ExecuteListAsync<FileProcessingStatus>("SELECT * FROM fileProcessingStatus ORDER BY last_updated_utc DESC");

            foreach(var info in gameProcessingInfo)
            {
                Game game;
                var gameCache = await _redis.StringGetAsync($"cf-game-{info.GameId}");
                if(!gameCache.IsNullOrEmpty)
                {
                    game = JsonSerializer.Deserialize<Game>(gameCache)!;
                }
                else
                {
                    game = (await _cfApiClient.GetGameAsync(info.GameId)).Data;
                    await _redis.StringSetAsync($"cf-game-{info.GameId}", JsonSerializer.Serialize(game), TimeSpan.FromDays(1));
                }

                Mod mod;
                var modCache = await _redis.StringGetAsync($"cf-mod-{info.ModId}");
                if(!modCache.IsNullOrEmpty)
                {
                    mod = JsonSerializer.Deserialize<Mod>(modCache)!;
                }
                else
                {
                    mod = (await _cfApiClient.GetModAsync(info.ModId)).Data;
                    await _redis.StringSetAsync($"cf-mod-{info.ModId}", JsonSerializer.Serialize(mod), TimeSpan.FromDays(1));
                }

                CurseForge.APIClient.Models.Files.File file;
                var fileCache = await _redis.StringGetAsync($"cf-file-{info.FileId}");
                if(!fileCache.IsNullOrEmpty)
                {
                    file = JsonSerializer.Deserialize<CurseForge.APIClient.Models.Files.File>(fileCache)!;
                }
                else
                {
                    file = (await _cfApiClient.GetModFileAsync(info.ModId, info.FileId)).Data;
                    await _redis.StringSetAsync($"cf-file-{info.FileId}", JsonSerializer.Serialize(file), TimeSpan.FromDays(1));
                }

                var gameModFileProcessingInfo = new GameModFileProcessingInfo
                {
                    Game = game,
                    Mod = mod,
                    File = file,
                    LatestUpdatedUtc = info.Last_Updated_UTC
                };

                ModFiles.Add(gameModFileProcessingInfo);
            }

            return Page();
        }
    }

    public class GameModFileProcessingInfo
    {
        public Game Game { get; set; }
        public Mod Mod { get; set; }
        public CurseForge.APIClient.Models.Files.File File { get; set; }
        public DateTimeOffset LatestUpdatedUtc { get; set; }
    }
}
