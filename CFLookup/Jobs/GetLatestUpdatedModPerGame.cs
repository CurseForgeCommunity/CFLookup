using CFLookup.Models;
using CurseForge.APIClient;
using CurseForge.APIClient.Models.Games;
using CurseForge.APIClient.Models.Mods;
using Hangfire.Server;
using Microsoft.Data.SqlClient;
using StackExchange.Redis;
using System.Text;
using System.Text.Json;

namespace CFLookup.Jobs
{
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
                    83374
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
                    Console.WriteLine($"Starting to check for latest updated mod for {game.Name}");
                    await _db.StringSetAsync($"cf-game-{game.Id}", JsonSerializer.Serialize(game), TimeSpan.FromDays(1));

                    var latestUpdatedMod = await cfClient.SearchModsAsync(game.Id, sortField: ModsSearchSortField.LastUpdated, sortOrder: ModsSearchSortOrder.Descending, pageSize: 1);
                    if (latestUpdatedMod != null && latestUpdatedMod.Pagination.ResultCount > 0)
                    {
                        var mod = latestUpdatedMod.Data.First();
                        var latestUpdatedFile = mod.LatestFiles.OrderByDescending(f => f.FileDate).First();
                        Console.WriteLine($"Latest updated mod for {game.Name} is {mod.Name} with {mod.DownloadCount} downloads and the latest file was updated {latestUpdatedFile.FileDate}");
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
