using CFLookup.Models;
using CurseForge.APIClient;
using CurseForge.APIClient.Models;
using CurseForge.APIClient.Models.Games;
using CurseForge.APIClient.Models.Mods;
using Highsoft.Web.Mvc.Charts;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Npgsql;
using StackExchange.Redis;
using System.Data;
using System.Text.Json;

namespace CFLookup.Pages
{
    public class FileProcessingInfoModel : PageModel
    {
        private readonly ApiClient _cfApiClient;
        private readonly IDatabase _redis;
        private readonly NpgsqlConnection _conn;

        public List<GameModFileProcessingInfo> ModFiles { get; set; } = [];

        public FileProcessingInfoModel(ApiClient cfApiClient, ConnectionMultiplexer connectionMultiplexer, MSSQLDB db, NpgsqlConnection conn)
        {
            _cfApiClient = cfApiClient;
            _redis = connectionMultiplexer.GetDatabase(5);
            _conn = conn;
        }

        public async Task<IActionResult> OnGetAsync()
        {
            var cmd = _conn.CreateCommand();

            if (_conn.State != ConnectionState.Open)
            {
                await _conn.OpenAsync();
            }

            cmd.CommandText = @"SELECT gd.name AS gamename, pd.*
FROM game_data gd
JOIN LATERAL (
    SELECT pd.* 
    FROM project_data pd
    WHERE pd.gameid = gd.gameid
      AND pd.isavailable = true
    ORDER BY pd.datemodified DESC
    LIMIT 1
) pd ON true;";

            cmd.CommandType = CommandType.Text;

            await using var reader = await cmd.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                var gpi = new GameModFileProcessingInfo
                {
                    ProjectId = reader.GetInt64("projectid"),
                    GameId = reader.GetInt32("gameid"),
                    GameName = reader.GetString("gamename"),
                    Slug = reader.GetString("slug"),
                    Name = reader.GetString("name"),
                    Summary = reader.GetString("summary"),
                    Links = JsonSerializer.Deserialize<ModLinks>(reader.GetString("links")),
                    Status = (ModStatus)reader.GetInt32("status"),
                    DownloadCount = reader.GetInt64("downloadcount"),
                    IsFeatured = reader.GetBoolean("isfeatured"),
                    PrimaryCategoryId = reader.GetInt32("primarycategoryid"),
                    Categories = JsonSerializer.Deserialize<List<Category>>(reader.GetString("categories")),
                    ClassId = reader.GetInt32("classid"),
                    Authors = JsonSerializer.Deserialize<List<ModAuthor>>(reader.GetString("authors")),
                    Logo = JsonSerializer.Deserialize<ModAsset>(reader.GetString("logo")),
                    Screenshots = JsonSerializer.Deserialize<List<ModAsset>>(reader.GetString("screenshots")),
                    MainFileId = reader.GetInt64("mainfileid"),
                    LatestFiles = JsonSerializer.Deserialize<List<CurseForge.APIClient.Models.Files.File>>(reader.GetString("latestfiles")),
                    DateCreated = reader.GetDateTime("datecreated"),
                    DateModified = reader.GetDateTime("datemodified"),
                    DateReleased = reader.GetDateTime("datereleased"),
                    AllowModDistribution = reader.GetBoolean("allowmoddistribution"),
                    GamePopularityRank = reader.GetInt64("gamepopularityrank"),
                    IsAvailable = reader.GetBoolean("isavailable"),
                    ThumbsUpCount = reader.GetInt64("thumbsupcount")
                };

                ModFiles.Add(gpi);
            }
            
            ModFiles = ModFiles.OrderByDescending(f => f.LatestUpdateUtc).ToList();
            
            // foreach (var info in gameProcessingInfo)
            // {
            //     Game game;
            //     var gameCache = await _redis.StringGetAsync($"cf-game-{info.GameId}");
            //     if (!gameCache.IsNullOrEmpty)
            //     {
            //         game = JsonSerializer.Deserialize<Game>(gameCache)!;
            //     }
            //     else
            //     {
            //         game = (await _cfApiClient.GetGameAsync(info.GameId)).Data;
            //         await _redis.StringSetAsync($"cf-game-{info.GameId}", JsonSerializer.Serialize(game), TimeSpan.FromDays(1));
            //     }
            //
            //     Mod? mod = null;
            //     var modCache = await _redis.StringGetAsync($"cf-mod-{info.ModId}");
            //     if (!modCache.IsNullOrEmpty)
            //     {
            //         mod = JsonSerializer.Deserialize<Mod?>(modCache)!;
            //     }
            //     else
            //     {
            //         try
            //         {
            //             mod = (await _cfApiClient.GetModAsync(info.ModId))?.Data;
            //             if (mod != null)
            //             {
            //                 await _redis.StringSetAsync($"cf-mod-{info.ModId}", JsonSerializer.Serialize(mod), TimeSpan.FromDays(1));
            //             }
            //         }
            //         catch (Exception ex)
            //         {
            //             Console.WriteLine($"Problem fetching mod with id: {info.ModId}");
            //             Console.WriteLine(ex.ToString());
            //         }
            //     }
            //
            //     CurseForge.APIClient.Models.Files.File file;
            //     var fileCache = await _redis.StringGetAsync($"cf-file-{info.FileId}");
            //     if (!fileCache.IsNullOrEmpty)
            //     {
            //         file = JsonSerializer.Deserialize<CurseForge.APIClient.Models.Files.File>(fileCache)!;
            //     }
            //     else
            //     {
            //         file = (await _cfApiClient.GetModFileAsync(info.ModId, info.FileId)).Data;
            //         await _redis.StringSetAsync($"cf-file-{info.FileId}", JsonSerializer.Serialize(file), TimeSpan.FromDays(1));
            //     }
            //
            //     var gameModFileProcessingInfo = new GameModFileProcessingInfo
            //     {
            //         Game = game,
            //         Mod = mod,
            //         File = file,
            //         LatestUpdatedUtc = info.Last_Updated_UTC,
            //         FileProcessingInfo = info
            //     };
            //
            //     ModFiles.Add(gameModFileProcessingInfo);
            // }

            return Page();
        }
    }

    public class GameModFileProcessingInfo
    {
        public int GameId { get; set; }
        public string GameName { get; set; }
        public string? GameUrl
        {
            get
            {
                if (Links.WebsiteUrl == null)
                {
                    return string.Empty;
                }

                var uri = new Uri(Links.WebsiteUrl);
                return $"https://www.curseforge.com{string.Concat(uri.Segments.Take(2))}";
            }
        }

        public long? ProjectId { get; set; }
        public string Name { get; set; }
        public string Slug { get; set; }
        public ModLinks Links { get; set; }
        public string Summary { get; set; }
        public ModStatus Status { get; set; }
        public long DownloadCount { get; set; }
        public bool IsFeatured { get; set; }
        public int PrimaryCategoryId { get; set; }
        public List<Category> Categories { get; set; }
        public int ClassId { get; set; }
        public List<ModAuthor> Authors { get; set; }
        public ModAsset? Logo { get; set; }
        public List<ModAsset> Screenshots { get; set; }
        public long? MainFileId { get; set; }
        public List<CurseForge.APIClient.Models.Files.File> LatestFiles { get; set; }
        public DateTimeOffset DateCreated { get; set; }
        public DateTimeOffset DateModified { get; set; }
        public DateTimeOffset DateReleased { get; set; }
        public bool AllowModDistribution { get; set; }
        public long GamePopularityRank { get; set; }
        public bool IsAvailable { get; set; }
        public long ThumbsUpCount { get; set; }

        public DateTimeOffset LatestUpdateUtc
        {
            get
            {
                if (LatestFiles.Count <= 0)
                {
                    return DateReleased;
                }

                var latestFile = LatestFiles.OrderByDescending(f => f.FileDate).FirstOrDefault();
                if (latestFile != null)
                {
                    return latestFile.FileDate;
                }
                
                return DateReleased;
            }
        }
        public TimeSpan SinceLatestUpdate => DateTimeOffset.UtcNow - LatestUpdateUtc;
    }
}
