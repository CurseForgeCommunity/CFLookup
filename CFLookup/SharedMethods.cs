using CurseForge.APIClient;
using CurseForge.APIClient.Models;
using CurseForge.APIClient.Models.Files;
using CurseForge.APIClient.Models.Games;
using CurseForge.APIClient.Models.Mods;
using StackExchange.Redis;
using System.Collections.Concurrent;
using System.Collections.Frozen;
using System.Data;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace CFLookup
{
    public static class SharedMethods
    {
        public static async Task<List<Game>> GetGameInfo(IDatabaseAsync _redis, ApiClient _cfApiClient)
        {
            var cachedGames = await _redis.StringGetAsync("cf-games");

            if (!cachedGames.IsNullOrEmpty)
            {
                return JsonSerializer.Deserialize<List<Game>>(cachedGames);
            }

            var games = await _cfApiClient.GetGamesAsync();
            await _redis.StringSetAsync("cf-games", JsonSerializer.Serialize(games.Data), TimeSpan.FromMinutes(5));
            return games.Data;
        }

        public static async Task<Game> GetGameInfo(IDatabaseAsync _redis, ApiClient _cfApiClient, int gameId)
        {
            var cachedGame = await _redis.StringGetAsync($"cf-games-{gameId}");

            if (!cachedGame.IsNullOrEmpty)
            {
                return JsonSerializer.Deserialize<Game>(cachedGame);
            }

            var games = await _cfApiClient.GetGameAsync(gameId);
            await _redis.StringSetAsync($"cf-games-{gameId}", JsonSerializer.Serialize(games.Data), TimeSpan.FromMinutes(5));
            return games.Data;
        }

        public static async Task<List<Category>> GetCategoryInfo(IDatabaseAsync _redis, ApiClient _cfApiClient, List<Game> gameInfo, string game)
        {
            var cachedCategories = await _redis.StringGetAsync($"cf-categories-{game}");

            if (!cachedCategories.IsNullOrEmpty)
            {
                return JsonSerializer.Deserialize<List<Category>>(cachedCategories);
            }

            var gameId = gameInfo.FirstOrDefault(x => x.Slug.Equals(game, StringComparison.InvariantCultureIgnoreCase))?.Id;

            var categories = await _cfApiClient.GetCategoriesAsync(gameId);
            await _redis.StringSetAsync($"cf-categories-{game}", JsonSerializer.Serialize(categories.Data), TimeSpan.FromMinutes(5));

            return categories.Data;
        }

        public static async Task<(Mod? mod, CurseForge.APIClient.Models.Files.File? file, string? changelog)> GetFileInfoAsync(IDatabaseAsync _redis, ApiClient _cfApiClient, int fileId)
        {
            var cachedFile = await _redis.StringGetAsync($"cf-fileinfo-{fileId}");

            if (!cachedFile.IsNullOrEmpty)
            {
                return JsonSerializer.Deserialize<(Mod mod, CurseForge.APIClient.Models.Files.File file, string changelog)>(cachedFile);
            }

            var file = await _cfApiClient.GetFilesAsync(new GetModFilesRequestBody
            {
                FileIds = new List<int> { fileId }
            });

            if (file.Data.Count == 0)
                return (null, null, null);

            var mod = await _cfApiClient.GetModAsync(file.Data[0].ModId);

            var changelog = await _cfApiClient.GetModFileChangelogAsync(file.Data[0].ModId, fileId);

            await _redis.StringSetAsync($"cf-fileinfo-{fileId}", JsonSerializer.Serialize((mod.Data, file.Data[0], changelog.Data)), TimeSpan.FromMinutes(5));

            return (mod.Data, file.Data[0], changelog.Data);
        }

        public static async Task<List<Category>> GetCategoryInfo(IDatabaseAsync _redis, ApiClient _cfApiClient, int gameId)
        {
            var cachedCategories = await _redis.StringGetAsync($"cf-categories-id-{gameId}");

            if (!cachedCategories.IsNullOrEmpty)
            {
                return JsonSerializer.Deserialize<List<Category>>(cachedCategories);
            }

            var categories = await _cfApiClient.GetCategoriesAsync(gameId);
            await _redis.StringSetAsync($"cf-categories-id-{gameId}", JsonSerializer.Serialize(categories.Data), TimeSpan.FromMinutes(5));
            await Task.Delay(25);
            return categories.Data;
        }

        public static async Task<List<Mod>> SearchModsAsync(IDatabaseAsync _redis, ApiClient _cfApiClient, List<int> modIds)
        {
            var cachedMods = await _redis.StringGetAsync($"cf-mods-{string.Join('-', modIds)}");
            if (!cachedMods.IsNullOrEmpty)
            {
                return JsonSerializer.Deserialize<List<Mod>>(cachedMods);
            }

            var mods = await _cfApiClient.GetModsByIdListAsync(new GetModsByIdsListRequestBody { ModIds = modIds });

            await _redis.StringSetAsync($"cf-mods-{string.Join('-', modIds)}", JsonSerializer.Serialize(mods.Data), TimeSpan.FromMinutes(5));

            return mods.Data;
        }

        public static async Task<Mod?> SearchModAsync(IDatabaseAsync _redis, ApiClient _cfApiClient, int projectId)
        {
            var modResultCache = await _redis.StringGetAsync($"cf-mod-{projectId}");
            if (!modResultCache.IsNull)
            {
                if (modResultCache == "empty")
                {
                    return null;
                }

                return JsonSerializer.Deserialize<Mod>(modResultCache); ;
            }

            try
            {
                var modResult = await _cfApiClient.GetModAsync(projectId);

                /*if (modResult.Data.Name == $"project-{projectId}" && modResult.Data.LatestFiles.Count > 0)
                {
                    var file = modResult.Data.LatestFiles.OrderByDescending(m => m.FileDate).First();
                    var projectName = GetProjectNameFromFile(file.DownloadUrl);
                    if (string.IsNullOrWhiteSpace(projectName))
                    {
                        projectName = file.DisplayName;
                    }
                    // Replace the project name with the filename for the projects latest available file
                    modResult.Data.Name = projectName;
                }*/

                var modJson = JsonSerializer.Serialize(modResult.Data);

                await _redis.StringSetAsync($"cf-mod-{projectId}", modJson, TimeSpan.FromMinutes(5));

                return modResult.Data;
            }
            catch
            {
                return null;
            }
        }

        public static async Task<int?> SearchModFileAsync(IDatabaseAsync _redis, ApiClient _cfApiClient, int fileId)
        {
            var modResultCache = await _redis.StringGetAsync($"cf-file-{fileId}");
            if (!modResultCache.IsNull)
            {
                if (modResultCache == "empty")
                {
                    return null;
                }

                var obj = JsonSerializer.Deserialize<GenericListResponse<CurseForge.APIClient.Models.Files.File>>(modResultCache);

                if (obj?.Data.Count > 0)
                {
                    return obj.Data[0].ModId;
                }
            }

            try
            {
                var modResult = await _cfApiClient.GetFilesAsync(new CurseForge.APIClient.Models.Files.GetModFilesRequestBody
                {
                    FileIds = new List<int> { fileId }
                });

                if (modResult.Data.Count > 0)
                {
                    var modJson = JsonSerializer.Serialize(modResult);
                    await _redis.StringSetAsync($"cf-file-{fileId}", modJson, TimeSpan.FromMinutes(5));

                    return modResult.Data[0].ModId;
                }

                return null;
            }
            catch
            {
                return null;
            }
        }

        public static async Task<Mod?> SearchForSlug(IDatabaseAsync _redis, ApiClient _cfApiClient, List<Game> gameInfo, List<Category> categoryInfo, string game, string category, string slug)
        {
            var cachedResponse = await _redis.StringGetAsync($"cf-mod-{game}-{category}-{slug}");
            if (!cachedResponse.IsNullOrEmpty)
            {
                if (cachedResponse == "empty")
                {
                    return null;
                }

                var cachedMod = JsonSerializer.Deserialize<Mod>(cachedResponse);

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
                await _redis.StringSetAsync($"cf-mod-{game}-{category}-{slug}", JsonSerializer.Serialize(mod.Data[0]), TimeSpan.FromMinutes(5));
                return mod.Data[0];
            }

            return null;
        }

        public static async Task<ConcurrentDictionary<string, ConcurrentDictionary<ModLoaderType, long>>> GetMinecraftModStatistics(IDatabaseAsync _redis, ApiClient _cfApiClient)
        {
            var cachedResponse = await _redis.StringGetAsync("cf-mcmod-stats");
            if (!cachedResponse.IsNullOrEmpty)
            {
                if (cachedResponse == "empty")
                {
                    return null;
                }

                var cachedMod = JsonSerializer.Deserialize<ConcurrentDictionary<string, ConcurrentDictionary<ModLoaderType, long>>>(cachedResponse);

                return cachedMod;
            }

            var mcVersionModCount = new ConcurrentDictionary<string, ConcurrentDictionary<ModLoaderType, long>>();

            var gameVersionTypes = await _cfApiClient.GetGameVersionTypesAsync(432);

            var minecraftVersions = gameVersionTypes.Data
                .Where(gvt => gvt.Slug.StartsWith("minecraft-") && !gvt.Slug.EndsWith("beta"))
                .OrderBy(gvt => Regex.Replace(gvt.Slug, "\\d+", m => m.Value.PadLeft(10, '0'))).ToList();

            var gameVersions = await _cfApiClient.GetGameVersionsAsync(432);

            var filteredVersions = gameVersions.Data.Where(gv => minecraftVersions.Any(mv => mv.Id == gv.Type)).ToDictionary(gv => gv.Type, gv => gv.Versions);

            var modLoaders = new[] {
                ModLoaderType.Forge,
                ModLoaderType.Fabric,
                ModLoaderType.Quilt,
                (ModLoaderType)6 // NeoForge
            };

            var versionTasks = minecraftVersions.Select(async mcVersion =>
            {
                var subTasks = filteredVersions[mcVersion.Id].Select(async subVersion =>
                {
                    var loaderTasks = modLoaders.Select(async modloader =>
                    {
                        var cfSearch = await _cfApiClient.SearchModsAsync(432, 6, gameVersion: subVersion, modLoaderType: modloader, pageSize: 1);

                        if (!mcVersionModCount.ContainsKey(mcVersion.Name))
                        {
                            mcVersionModCount[mcVersion.Name] = new ConcurrentDictionary<ModLoaderType, long>();
                        }

                        if (!mcVersionModCount[mcVersion.Name].ContainsKey(modloader))
                        {
                            mcVersionModCount[mcVersion.Name][modloader] = 0;
                        }

                        mcVersionModCount[mcVersion.Name][modloader] += cfSearch.Pagination.TotalCount;
                    });

                    await Task.WhenAll(loaderTasks);
                });

                await Task.WhenAll(subTasks);
            });

            await Task.WhenAll(versionTasks);

            await _redis.StringSetAsync("cf-mcmod-stats", JsonSerializer.Serialize(mcVersionModCount), TimeSpan.FromHours(1));

            return mcVersionModCount;
        }

        public static async Task<ConcurrentDictionary<string, long>> GetMinecraftModpackStatistics(IDatabaseAsync _redis, ApiClient _cfApiClient)
        {
            var cachedResponse = await _redis.StringGetAsync("cf-mcmodpack-stats");
            if (!cachedResponse.IsNullOrEmpty)
            {
                if (cachedResponse == "empty")
                {
                    return null;
                }

                var cachedMod = JsonSerializer.Deserialize<ConcurrentDictionary<string, long>>(cachedResponse);

                return cachedMod;
            }

            var mcVersionModCount = new ConcurrentDictionary<string, long>();

            var gameVersionTypes = await _cfApiClient.GetGameVersionTypesAsync(432);

            var minecraftVersions = gameVersionTypes.Data
                .Where(gvt => gvt.Slug.StartsWith("minecraft-") && !gvt.Slug.EndsWith("beta"))
                .OrderBy(gvt => Regex.Replace(gvt.Slug, "\\d+", m => m.Value.PadLeft(10, '0'))).ToList();

            var gameVersions = await _cfApiClient.GetGameVersionsAsync(432);

            var filteredVersions = gameVersions.Data.Where(gv => minecraftVersions.Any(mv => mv.Id == gv.Type)).ToDictionary(gv => gv.Type, gv => gv.Versions);

            var versionTasks = minecraftVersions.Select(async mcVersion =>
            {
                var subTasks = filteredVersions[mcVersion.Id].Select(async subVersion =>
                {
                    var cfSearch = await _cfApiClient.SearchModsAsync(432, 4471, gameVersion: subVersion, pageSize: 1);

                    if (!mcVersionModCount.ContainsKey(mcVersion.Name))
                    {
                        mcVersionModCount[mcVersion.Name] = 0;
                    }

                    mcVersionModCount[mcVersion.Name] += cfSearch.Pagination.TotalCount;
                });

                await Task.WhenAll(subTasks);
            });

            await Task.WhenAll(versionTasks);

            await _redis.StringSetAsync("cf-mcmodpack-stats", JsonSerializer.Serialize(mcVersionModCount), TimeSpan.FromHours(1));

            return mcVersionModCount;
        }

        public static async Task<Dictionary<DateTimeOffset, Dictionary<string, Dictionary<string, long>>>> GetMinecraftStatsOverTime(MSSQLDB _db, CancellationToken cancellationToken, int? datapoints = 1000)
        {
            var stats = await _db.ExecuteReader(
$@"
SELECT timestamp_utc, stats, RowNumber
FROM (
    SELECT ROW_NUMBER() OVER (ORDER BY statId DESC) AS RowNumber, *
    FROM MinecraftModStatsOverTime
) AS MCStats
{(datapoints.HasValue && datapoints > 0 ? $"WHERE RowNumber <= {datapoints}" : "")}
ORDER BY RowNumber DESC
");
            var Stats = new Dictionary<DateTimeOffset, Dictionary<string, Dictionary<string, long>>>();
            while (stats.Read())
            {
                if(cancellationToken.IsCancellationRequested)
                {
                    break;
                }
                var timestamp = stats.GetDateTimeOffset(0);
                var gameStats = JsonSerializer.Deserialize<Dictionary<string, Dictionary<string, long>>>(stats.GetString(1))!;
                Stats.Add(timestamp, gameStats);
            }

            return Stats;
        }

        public static string GetProjectNameFromFile(string url)
        {
            return Path.GetFileName(url);
        }

        public static string ToHumanReadableFormat(this TimeSpan timeSpan, bool shortText = false, double skipMinutesAfterHours = 24.0f, double skipSecondsAfterHours = 1.0f)
        {
            var secondText = shortText ? "s" : " second" + (timeSpan.Seconds != 1 ? "s" : string.Empty);
            var minuteText = shortText ? "m" : " minute" + (timeSpan.Minutes != 1 ? "s" : string.Empty);
            var hourText = shortText ? "h" : " hour" + (timeSpan.Hours != 1 ? "s" : string.Empty);
            var dayText = shortText ? "d" : " day" + (timeSpan.Days != 1 ? "s" : string.Empty);

            return timeSpan.TotalSeconds <= 0 ? "0 seconds" : string.Format("{0}{1}{2}{3}",
                timeSpan.Days > 0 ? $"{timeSpan.Days:n0}{dayText}, " : string.Empty,
                timeSpan.Hours > 0 ? $"{timeSpan.Hours:n0}{hourText}, " : string.Empty,
                timeSpan.TotalHours <= skipMinutesAfterHours ?
                    timeSpan.Minutes > 0 ? $"{timeSpan.Minutes:n0}{minuteText}, " : string.Empty :
                    string.Empty,
                timeSpan.TotalHours <= skipSecondsAfterHours ?
                    timeSpan.Seconds > 0 ? $"{timeSpan.Seconds:n0}{secondText}" : string.Empty :
                    string.Empty
            ).Trim(new[] { ' ', ',' });
        }
    }
}