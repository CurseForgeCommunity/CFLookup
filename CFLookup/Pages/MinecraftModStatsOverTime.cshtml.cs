using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using System.Data;
using System.Text.Json;

namespace CFLookup.Pages
{
    public class MinecraftModStatsOverTimeModel : PageModel
    {
        private readonly MSSQLDB _db;

        public Dictionary<string, Dictionary<DateTimeOffset, Dictionary<string, long>>> Stats { get; set; } = new Dictionary<string, Dictionary<DateTimeOffset, Dictionary<string, long>>>();

        public MinecraftModStatsOverTimeModel(MSSQLDB db)
        {
            _db = db;
        }

        public async Task OnGetAsync()
        {
            var stats = await SharedMethods.GetMinecraftStatsOverTime(_db);

            var modloaderStats = new Dictionary<string, Dictionary<DateTimeOffset, Dictionary<string, long>>>();

            foreach (var stat in stats)
            {
                var date = stat.Key;
                foreach(var modloaderHolder in stat.Value)
                {
                    var gameVersion = modloaderHolder.Key;

                    if(gameVersion.Contains("Snapshot")) continue;

                    foreach(var gameInfo in modloaderHolder.Value)
                    {
                        var modloader = gameInfo.Key;
                        var count = gameInfo.Value;

                        if(modloader.Contains("LiteLoader")) continue;

                        if (!modloaderStats.ContainsKey(modloader))
                        {
                            modloaderStats[modloader] = new Dictionary<DateTimeOffset, Dictionary<string, long>>();
                        }

                        if (!modloaderStats[modloader].ContainsKey(date))
                        {
                            modloaderStats[modloader][date] = new Dictionary<string, long>();
                        }

                        modloaderStats[modloader][date][gameVersion] = count;
                    }
                }
            }

            Stats = modloaderStats;
        }
    }
}
