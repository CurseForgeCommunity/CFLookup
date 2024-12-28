using Highsoft.Web.Mvc.Stocks;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace CFLookup.Pages
{
    public class MinecraftModStatsOverTimeModel : PageModel
    {
        private readonly MSSQLDB _db;

        public Dictionary<string, Dictionary<DateTimeOffset, Dictionary<string, long>>> Stats { get; set; } = new Dictionary<string, Dictionary<DateTimeOffset, Dictionary<string, long>>>();

        public Dictionary<string, List<Series>> ModLoaderStats = new Dictionary<string, List<Series>>();

        public MinecraftModStatsOverTimeModel(MSSQLDB db)
        {
            _db = db;
        }

        public async Task OnGetAsync()
        {
            var stats = await SharedMethods.GetMinecraftStatsOverTime(_db, 24 * 30);

            var modloaderStats = new Dictionary<string, Dictionary<DateTimeOffset, Dictionary<string, long>>>();

            foreach (var stat in stats)
            {
                var date = stat.Key;
                foreach(var modloaderHolder in stat.Value)
                {
                    var gameVersion = modloaderHolder.Key;

                    if(gameVersion.Contains("snapshot", StringComparison.InvariantCultureIgnoreCase)) continue;

                    foreach(var gameInfo in modloaderHolder.Value)
                    {
                        var modloader = gameInfo.Key;
                        var count = gameInfo.Value;

                        if(modloader.Contains("LiteLoader", StringComparison.InvariantCultureIgnoreCase)) continue;

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

            // Generate different series per modloader and game version for Highstock as separate graphs, where the game versions are the line series
            var testGraph = new Dictionary<string, List<LineSeriesData>>();
            var forgeData = modloaderStats["Forge"];

            foreach(var d in forgeData)
            {
                var date = d.Key;
                var gameVersions = d.Value;

                foreach(var gameVersion in gameVersions)
                {
                    if (!testGraph.ContainsKey(gameVersion.Key))
                    {
                        testGraph[gameVersion.Key] = new List<LineSeriesData>();
                    }

                    testGraph[gameVersion.Key].Add(new LineSeriesData { X = date.ToUnixTimeMilliseconds(), Y = gameVersion.Value });
                }
            }

            var viewData = new List<Series>();

            foreach(var series in testGraph)
            {
                viewData.Add(new LineSeries
                {
                    Name = series.Key,
                    Data = series.Value,
                    TurboThreshold = 100,
                    
                });
            }

            ModLoaderStats["ForgeData"] = viewData;
        }
    }
}
