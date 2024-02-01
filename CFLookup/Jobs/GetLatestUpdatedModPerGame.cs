using CFLookup.Models;
using CurseForge.APIClient;
using CurseForge.APIClient.Models.Games;
using CurseForge.APIClient.Models.Mods;
using Hangfire;
using Hangfire.Server;
using Microsoft.Data.SqlClient;
using StackExchange.Redis;
using System.Text;
using System.Text.Json;

namespace CFLookup.Jobs
{
    [AutomaticRetry(Attempts = 0)]
    public class GetLatestUpdatedModPerGame
    {
        public static async Task RunAsync(PerformContext context)
        {
            using (var scope = Program.ServiceProvider.CreateScope())
            {
                var cfClient = scope.ServiceProvider.GetRequiredService<ApiClient>();
                var db = scope.ServiceProvider.GetRequiredService<MSSQLDB>();
                var _redis = scope.ServiceProvider.GetRequiredService<ConnectionMultiplexer>();

                var _db = _redis.GetDatabase(5);

                Console.WriteLine("Fetching all games");

                var allGames = new List<Game>();
                var games = await cfClient.GetGamesAsync();
                if (games != null && games.Pagination.ResultCount > 0)
                {
                    allGames.AddRange(games.Data);
                    var index = 0;
                    while (games.Pagination.ResultCount > 0)
                    {
                        index += games.Pagination.PageSize;
                        games = await cfClient.GetGamesAsync(index);
                        allGames.AddRange(games.Data);
                    }
                }

                Console.WriteLine($"Fetched {allGames.Count} games");

                DateTimeOffset lastUpdatedMod = DateTimeOffset.MinValue;
                Mod? latestUpdatedModData = null;
                CurseForge.APIClient.Models.Files.File? latestUpdatedFileData = null;

                var privateGames = new List<int>
                {
                    1, // World of Warcraft
                    64, // The Secret World
                    65, // StarCraft II
                    68, // Fallout 3
                    202, // Warcraft III: Reign of Chaos
                    335, // Runes of Magic
                    423, // World of Tanks
                    424, // Rift
                    431, // Terraria
                    432, // Minecraft
                    447, // Fallout: New Vegas
                    449, // The Elder Scrolls V: Skyrim
                    454, // WildStar
                    455, // The Elder Scrolls Online
                    465, // Counter-Strike: Global Offensive
                    496, // Grand Theft Auto V
                    504, // Euro Truck Simulator 2
                    540, // 7 Days to Die
                    608, // Darkest Dungeon
                    632, // Rocket League
                    646, // Fallout 4
                    661, // Factorio
                    669, // Stardew Valley
                    727, // Sid Meier's Civilization VI
                    732, // Planet Coaster
                    4401, // Kerbal Space Program
                    4455, // Secret World Legends
                    4482, // Subnautica
                    4588, // RimWorld
                    4593, // Final Fantasy XV
                    4611, // XCOM 2
                    4619, // The Last of Us
                    4741, // Final Fantasy IV
                    4773, // Final Fantasy VI
                    4819, // American Truck Simulator
                    4892, // Space Engineers
                    5001, // Final Fantasy II
                    5002, // Mario Party 2
                    5021, // Final Fantasy V
                    5026, // Final Fantasy III
                    5230, // Final Fantasy I
                    6222, // BattleTech
                    6351, // Mario Party 3
                    6647, // My Time at Portia
                    6820, // Kenshi
                    6999, // Big Pharma
                    7005, // System Shock
                    8612, // Euro Truck Simulator
                    8686, // Jurassic Park: Operation Genesis
                    8892, // Microsoft Flight Simulator
                    11805, // Zoo Tycoon 2
                    12471, // Rend
                    14331, // WarGroove
                    18237, // Staxel
                    22184, // New World
                    22191, // Frostpunk
                    48907, // Beat It!
                    51667, // Dead Island 2
                    57483, // Days Gone
                    57972, // Phantom Brigade
                    58024, // Vintage Story
                    58053, // Jurassic World Evolution
                    58234, // Warhammer: Vermintide 2
                    61489, // Surviving Mars
                    64244, // Farming Simulator 19
                    65814, // Fallout 76
                    66004, // Starfield
                    66022, // Satisfactory
                    67850, // Bloons TD 6
                    68013, // Valheim
                    69073, // Megaquarium
                    69271, // Minecraft Dungeons
                    69761, // Among Us
                    70667, // Chronicles of Arcadia
                    70752, // Subnautica: Below Zero
                    71010, // Darkest Dungeon 2
                    71638, // The Riftbreaker
                    71878, // Planet Zoo
                    72430, // Watch Dogs Legion
                    72458, // Baldur's Gate 3
                    73492, // Kerbal Space Program 2
                    75009, // Sons of the Forest
                    76592, // XCOM: Chimera Squad
                    77546, // Resident Evil Village
                    77548, // Returnal
                    78017, // Dyson Sphere Program
                    78018, // osu!
                    78019, // Loop Hero
                    78022, // Minecraft Bedrock
                    78023, // Timber and Stone
                    78062, // The Sims 4
                    78072, // Hometopia
                    78101, // Tiny Life
                    78103, // art of rally
                    78135, // Demeo
                    78163, // Astro Colony
                    78225, // The Anacrusis
                    78251, // Kingshunt
                    78496, // Hero's Hour
                    79630, // GTA-SA
                    79805, // CurseForge Demo
                    80016, // Mario Party
                    80214, // Spider-Man Remastered
                    80345, // Dwerve
                    81975, // LEAP
                    82010, // KSP QA Test Game
                    82047, // Oaken
                    82164, // Unity SDK Tester
                    82203, // Conquer Online
                    83357, // River City Girls 2
                    83372, // The Settlers: New Allies
                    83374, // ARK Survival Ascended
                    83375, // Far Cry 6
                    83387, // Wild Hearts
                    83388, // Company of Heroes 3
                    83431, // Wo Long Fallen Dynasty
                    83432, // Resident Evil 4 Remake
                    83444, // WorldBox - God Simulator
                    83445, // V Rising
                    83452, // Crime Boss: Rockay City
                    83453, // Minecraft Legends
                    83454, // Star Wars Jedi Survivor
                    83457, // Redfall
                    83461, // Darkest Dungeon II
                    83462, // Endless Dungeon
                    83463, // Suicide Squad: Kill the Justice League
                    83634, // Age of Wonders 4
                    83644, // Starship Troopers: Extermination
                    83645, // Terra Nil
                    83647, // Brinefall
                    83648, // Spiritfall
                    83649, // Meet Your Maker
                    83871, // Street Fighter 6
                    83981, // Unreal Test Game
                    84062, // Rushdown Revolt
                    84137, // Tennis Elbow 4
                    84438, // Trine 5
                    84439, // Mortal Kombat 1
                    84529, // NighspadeTest001
                    84530, // OWITestGame
                    84610, // Test01
                    84658, // AI M3
                    84749, // Minecraft
                    84801, // stopdeletingmystuffiamtesting
                    84810, // Oaken_Testing
                    84932, // MINIcraft
                    85196, // Palworld
                };

                foreach (var privateGame in privateGames)
                {
                    if (!allGames.Any(g => g.Id == privateGame))
                    {
                        var game = await cfClient.GetGameAsync(privateGame);
                        if (game != null && game.Data != null)
                        {
                            allGames.Add(game.Data);
                        }
                    }
                }

                foreach (var game in allGames)
                {
                    Console.WriteLine($"Starting to check for latest updated mod for {game.Name} (GameId: {game.Id})");
                    await _db.StringSetAsync($"cf-game-{game.Id}", JsonSerializer.Serialize(game), TimeSpan.FromDays(1));

                    var latestUpdatedMod = await cfClient.SearchModsAsync(game.Id, sortField: ModsSearchSortField.LastUpdated, sortOrder: ModsSearchSortOrder.Descending, pageSize: 1);
                    if (latestUpdatedMod != null && latestUpdatedMod.Pagination.ResultCount > 0)
                    {
                        var mod = latestUpdatedMod.Data.First();
                        var latestUpdatedFile = mod.LatestFiles.OrderByDescending(f => f.FileDate).FirstOrDefault();
                        if (latestUpdatedFile != null)
                        {
                            Console.WriteLine($"Latest updated mod for {game.Name} (GameId: {game.Id}) is {mod.Name} (ModId: {mod.Id}) with {mod.DownloadCount} downloads and the latest file was updated {latestUpdatedFile.FileDate}");
                            if (lastUpdatedMod < latestUpdatedFile.FileDate)
                            {
                                lastUpdatedMod = latestUpdatedFile.FileDate;
                                latestUpdatedModData = mod;
                                latestUpdatedFileData = latestUpdatedFile;
                            }

                            await _db.StringSetAsync($"cf-mod-{mod.Id}", JsonSerializer.Serialize(mod), TimeSpan.FromDays(1));
                            await _db.StringSetAsync($"cf-file-{latestUpdatedFile.Id}", JsonSerializer.Serialize(latestUpdatedFile), TimeSpan.FromDays(1));

                            var existingGame = await db.ExecuteSingleRowAsync<FileProcessingStatus>(
                                "SELECT * FROM fileProcessingStatus WHERE gameId = @gameId",
                                new SqlParameter("@gameId", game.Id)
                            );

                            if (existingGame == null)
                            {
                                // New game, insert it
                                await db.ExecuteNonQueryAsync(
                                    "INSERT INTO fileProcessingStatus (last_updated_utc, gameId, modId, fileId) VALUES (@last_updated_utc, @gameId, @modId, @fileId)",
                                    new SqlParameter("@last_updated_utc", latestUpdatedFile.FileDate),
                                    new SqlParameter("@gameId", game.Id),
                                    new SqlParameter("@modId", mod.Id),
                                    new SqlParameter("@fileId", latestUpdatedFile.Id)
                                );
                            }
                            else
                            {
                                // Existing game, update it
                                await db.ExecuteNonQueryAsync(
                                    "UPDATE fileProcessingStatus SET last_updated_utc = @last_updated_utc, modId = @modId, fileId = @fileId WHERE gameId = @gameId",
                                    new SqlParameter("@last_updated_utc", latestUpdatedFile.FileDate),
                                    new SqlParameter("@modId", mod.Id),
                                    new SqlParameter("@fileId", latestUpdatedFile.Id),
                                    new SqlParameter("@gameId", game.Id)
                                );
                            }
                        }
                        else
                        {
                            Console.WriteLine($"No updated files found for {game.Name} and mod {mod.Name}");
                        }
                    }
                    else
                    {
                        Console.WriteLine($"No mods found for {game.Name}");
                    }
                }

                Console.WriteLine($"Last updated mod was updated {lastUpdatedMod}");

                if (lastUpdatedMod < DateTimeOffset.UtcNow.AddHours(-3) && latestUpdatedModData != null && latestUpdatedFileData != null)
                {
                    Console.WriteLine("No mods were updated in the last 3 hours, file processing might be down.");

                    var warned = await _db.StringGetAsync("cf-file-processing-warning");

                    if (warned.HasValue && warned == "true")
                    {
                        Console.WriteLine("Already warned about this, skipping.");
                        return;
                    }

                    var httpClient = scope.ServiceProvider.GetRequiredService<IHttpClientFactory>().CreateClient();
                    var discordWebhook = Environment.GetEnvironmentVariable("DISCORD_WEBHOOK", EnvironmentVariableTarget.Machine) ??
                        Environment.GetEnvironmentVariable("DISCORD_WEBHOOK", EnvironmentVariableTarget.User) ??
                        Environment.GetEnvironmentVariable("DISCORD_WEBHOOK", EnvironmentVariableTarget.Process) ??
                        string.Empty;

                    if (!string.IsNullOrWhiteSpace(discordWebhook))
                    {
                        var message = @$"No mods were updated in the last 3 hours, file processing might be down.
Last updated mod was updated {lastUpdatedMod}, and it was {latestUpdatedModData.Name}
(ProjectID: {latestUpdatedModData.Id}, FileId: {latestUpdatedFileData.Id})
https://cflookup.com/{latestUpdatedModData.Id}";
                        var payload = new
                        {
                            content = message,
                            flags = 4
                        };

                        var json = JsonSerializer.Serialize(payload);
                        var content = new StringContent(json, Encoding.UTF8, "application/json");
                        await httpClient.PostAsync(discordWebhook, content);
                    }

                    await _db.StringSetAsync("cf-file-processing-warning", "true", TimeSpan.FromHours(1));
                    return;
                }
            }
        }
    }
}
