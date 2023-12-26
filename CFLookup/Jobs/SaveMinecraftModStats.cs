using CurseForge.APIClient;
using CurseForge.APIClient.Models.Mods;
using Hangfire;
using Hangfire.Server;
using Microsoft.Data.SqlClient;
using StackExchange.Redis;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace CFLookup.Jobs
{
    [AutomaticRetry(Attempts = 0)]
    public class SaveMinecraftModStats
    {
        public static async Task RunAsync(PerformContext context)
        {
            using (var scope = Program.ServiceProvider.CreateScope())
            {
                var cfClient = scope.ServiceProvider.GetRequiredService<ApiClient>();
                var db = scope.ServiceProvider.GetRequiredService<MSSQLDB>();
                var _redis = scope.ServiceProvider.GetRequiredService<ConnectionMultiplexer>();

                var _db = _redis.GetDatabase(5);

                var gameVersionTypes = await cfClient.GetGameVersionTypesAsync(432);

                var minecraftVersions = gameVersionTypes.Data
                .Where(gvt => gvt.Slug.StartsWith("minecraft-") && !gvt.Slug.EndsWith("beta"))
                .OrderBy(gvt => Regex.Replace(gvt.Slug, "\\d+", m => m.Value.PadLeft(10, '0'))).ToList();

                var gameVersions = await cfClient.GetGameVersionsAsync(432);

                var filteredVersions = gameVersions.Data.Where(gv => minecraftVersions.Any(mv => mv.Id == gv.Type))
                    .ToDictionary(gv => gv.Type, gv => gv.Versions.OrderBy(gvt => Regex.Replace(gvt, "\\d+", m => m.Value.PadLeft(10, '0'))));

                var mvList = new List<MinecraftVersionHolder>();

                foreach (var minecraftVersion in minecraftVersions)
                {
                    var holder = new MinecraftVersionHolder
                    {
                        VersionId = minecraftVersion.Id,
                        GameVersion = minecraftVersion.Name,
                        GameSubVersions = filteredVersions[minecraftVersion.Id].ToList()
                    };

                    mvList.Add(holder);
                }

                var modLoaders = new Dictionary<string, ModLoaderType>
                {
                    { "Forge", ModLoaderType.Forge },
                    { "Fabric", ModLoaderType.Fabric },
                    { "LiteLoader", ModLoaderType.LiteLoader },
                    { "Quilt", ModLoaderType.Quilt },
                    { "NeoForge", (ModLoaderType)6 }
                };

                var modsPerVersion = new Dictionary<string, Dictionary<string, long>>();

                foreach (var gameVersion in mvList)
                {
                    foreach (var subversion in gameVersion.GameSubVersions)
                    {
                        modsPerVersion.Add(subversion, new Dictionary<string, long>());
                        foreach(var modloader in modLoaders)
                        { 
                            var mods = await cfClient.SearchModsAsync(432, 6, gameVersion: subversion, modLoaderType: modloader.Value, pageSize: 1);
                            modsPerVersion[subversion].Add(modloader.Key, mods.Pagination.TotalCount);
                        }
                    }
                }

                var json = JsonSerializer.Serialize(modsPerVersion);

                await db.ExecuteNonQueryAsync("INSERT INTO [dbo].[MinecraftModStatsOverTime] ([stats]) VALUES (@stats)", new SqlParameter("@stats", json));
            }
        }

        public class MinecraftVersionHolder
        {
            public int VersionId { get; set; }
            public string GameVersion { get; set; }
            public List<string> GameSubVersions { get; set; } = new List<string>();
        }
    }
}
