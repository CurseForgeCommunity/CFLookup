using Hangfire.Server;
using Highsoft.Web.Mvc.Charts.Rendering;
using Highsoft.Web.Mvc.Charts;
using StackExchange.Redis;

namespace CFLookup.Jobs
{
    public class CacheMCOverTime
    {
        public static async Task RunAsync(PerformContext context)
        {
            using (var scope = Program.ServiceProvider.CreateScope())
            {
                var _db = scope.ServiceProvider.GetRequiredService<MSSQLDB>();
                var _redis = scope.ServiceProvider.GetRequiredService<ConnectionMultiplexer>();

                var _rdb = _redis.GetDatabase(5);

                var stats = await SharedMethods.GetMinecraftStatsOverTime(_db, CancellationToken.None, null);

                var renderers = new List<string>();
                var ModLoaderStats = new Dictionary<string, List<Series>>();
                var modloaderStats = new Dictionary<string, Dictionary<DateTimeOffset, Dictionary<string, long>>>();

                foreach (var stat in stats)
                {
                    var date = stat.Key;
                    foreach (var modloaderHolder in stat.Value)
                    {
                        var gameVersion = modloaderHolder.Key;

                        if (gameVersion.Contains("snapshot", StringComparison.InvariantCultureIgnoreCase)) continue;

                        foreach (var gameInfo in modloaderHolder.Value)
                        {
                            var modloader = gameInfo.Key;
                            var count = gameInfo.Value;

                            if (modloader.Contains("LiteLoader", StringComparison.InvariantCultureIgnoreCase)) continue;

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

                foreach (var kv in modloaderStats)
                {
                    var loader = kv.Key;

                    var testGraph = new Dictionary<string, List<LineSeriesData>>();

                    foreach (var d in kv.Value)
                    {
                        var date = d.Key;
                        var gameVersions = d.Value;

                        foreach (var gameVersion in gameVersions)
                        {
                            if (!testGraph.ContainsKey(gameVersion.Key))
                            {
                                testGraph[gameVersion.Key] = new List<LineSeriesData>();
                            }

                            testGraph[gameVersion.Key].Add(new LineSeriesData { X = date.ToUnixTimeMilliseconds(), Y = gameVersion.Value });
                        }
                    }

                    var viewData = new List<Series>();

                    foreach (var series in testGraph)
                    {
                        viewData.Add(new LineSeries
                        {
                            Name = series.Key,
                            Data = series.Value,
                            TurboThreshold = 100,
                            Selected = false
                        });
                    }

                    ModLoaderStats[$"{loader}Data"] = viewData;
                }

                foreach (var loaderData in ModLoaderStats)
                {
                    var loader = loaderData.Key;
                    var chartOptions =
                        new Highcharts
                        {
                            ID = $"{loader.Replace("Data", "")}Chart",
                            Chart = new Chart
                            {
                                HeightNumber = 800,
                                ZoomType = ChartZoomType.X
                            },
                            XAxis = new List<XAxis>
                            {
                                new XAxis
                                {
                                    Type = "datetime",
                                    MinRange = 3600000
                                }
                            },
                            Legend = new Legend
                            {
                                Enabled = true
                            },
                            YAxis = new List<YAxis>
                            {
                                new YAxis
                                {
                                    Labels = new YAxisLabels
                                    {
                                    },
                                    PlotLines = new List<YAxisPlotLines>
                                    {
                                        new YAxisPlotLines
                                        {
                                            Value = 0,
                                            Width = 2,
                                            Color = "silver"
                                        }
                                    }
                                }
                            },
                            Tooltip = new Tooltip
                            {
                                PointFormat = @"<span style='color:{series.color}'>{series.name}</span>: <b>{point.y}</b><br/>",
                                ValueDecimals = 0
                            },
                            PlotOptions = new PlotOptions
                            {
                                Series = new PlotOptionsSeries
                                {
                                    TurboThreshold = 10000
                                },
                                Area = new PlotOptionsArea
                                {
                                    Marker = new PlotOptionsAreaMarker
                                    {
                                        Radius = 2
                                    },
                                    LineWidth = 1,
                                    States = new PlotOptionsAreaStates
                                    {
                                        Hover = new PlotOptionsAreaStatesHover
                                        {
                                            LineWidth = 1
                                        }
                                    },
                                    Threshold = null
                                }
                            },
                            Series = loaderData.Value,
                            Title = new Title { Text = $"Amount of mods for {loader.Replace("Data", "")} over time" }
                        };

                    var renderer = new HighchartsRenderer(chartOptions);
                    renderers.Add(renderer.RenderHtml());
                }

                await _rdb.StringSetAsync("cf-mcmodloader-stats", string.Join("<hr />", renderers), TimeSpan.FromHours(1));
            }
        }
    }
}
