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
                    449, // Skyrim
                    540, // 7 Days To Die
                    4482, // Subnautica
                    4593, // Final Fantasy XV
                    4619, // The Last of Us
                    4819, // American Truck Simulator
                    6351, // Mario Party 3
                    18237, // Staxel
                    66004, // Starfield
                    73492, // Kerbal Space Program 2
                    78022, // Minecraft Bedrock
                    78023, // Timber and Stone
                    78072, // Hometopia
                    78101, // Tiny Life
                    78103, // art of rally
                    78163, // Astro Colony
                    78225, // The Anacrusis
                    78251, // Kingshunt
                    78496, // Hero's Tour
                    79630, // GTA-SA
                    79805, // CurseForge Demo
                    80345, // Dwerve
                    81975, // LEAP
                    82010, // KSP QA Test Game
                    82047, // Oaken
                    82164, // Unity SDK Tester
                    83357, // River City Girls 2
                    83374, // ARK
                    83375, // Far Cry 6
                    83444, // WorldBox - God Simulator
                    83453, // Minecraft Legends
                    83981, // Unreal Test Game
                    84529, // NighspadeTest001
                    84530, // OWITestGame
                    84610, // Test01
                    84658, // AI M3
                    84749, // Minecraft
                    84801, // stopdeletingmystuffiamtesting
                    84810, // Oaken_Testing
                    85196, // Palworld
                };

                foreach (var privateGame in privateGames)
                {
                    var game = await cfClient.GetGameAsync(privateGame);
                    if (game != null && game.Data != null)
                    {
                        allGames.Add(game.Data);
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
