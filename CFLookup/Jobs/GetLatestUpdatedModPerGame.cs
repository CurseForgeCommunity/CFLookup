using CFLookup.Models;
using CurseForge.APIClient;
using CurseForge.APIClient.Models.Games;
using CurseForge.APIClient.Models.Mods;
using Hangfire;
using Hangfire.Server;
using Microsoft.Data.SqlClient;
using StackExchange.Redis;
using System.Data;
using System.Text;
using System.Text.Json;

namespace CFLookup.Jobs
{
    [AutomaticRetry(Attempts = 0)]
    public class GetLatestUpdatedModPerGame
    {
        public async static Task RunAsync(PerformContext context)
        {
            using (var scope = Program.ServiceProvider.CreateScope())
            {
                var cfClient = scope.ServiceProvider.GetRequiredService<ApiClient>();
                var db = scope.ServiceProvider.GetRequiredService<MSSQLDB>();
                var _redis = scope.ServiceProvider.GetRequiredService<ConnectionMultiplexer>();

                var _db = _redis.GetDatabase(5);

                await using (var jobLock = await RedisJobLock.CreateAsync(
                           _redis.GetDatabase(0), 
                           "GetLatestUpdatedModPerGame",
                           scope.ServiceProvider.GetRequiredService<Logger<RedisJobLock>>(),
                           TimeSpan.FromSeconds(15)))
                {
                    if (jobLock == null)
                    {
                        return;
                    }
                    
                    Console.WriteLine("Fetching all games");

                    var allGames = new List<Game>();

                    DateTimeOffset lastUpdatedMod = DateTimeOffset.MinValue;
                    Mod? latestUpdatedModData = null;
                    CurseForge.APIClient.Models.Files.File? latestUpdatedFileData = null;

                    var privateGames = await db.ExecuteListAsync<ProcessingGames>(
                        "SELECT * FROM ProcessingGames WHERE Disabled = 0 AND ModCount > 0"
                    );

                    foreach (var privateGame in privateGames)
                    {
                        if (!allGames.Any(g => g.Id == privateGame.GameId))
                        {
                            var game = await cfClient.GetGameAsync(privateGame.GameId);
                            if (game != null && game.Data != null)
                            {
                                allGames.Add(game.Data);
                            }

                            if (game != null && game.Error != null && game.Error.ErrorCode != 404)
                            {
                                Console.WriteLine(
                                    $"Error fetching game info for {privateGame.Id}: {game.Error.ErrorMessage}");
                                continue;
                            }

                            await Task.Delay(100);
                        }
                    }

                    foreach (var game in allGames)
                    {
                        Console.WriteLine(
                            $"Starting to check for latest updated mod for {game.Name} (GameId: {game.Id})");
                        await _db.StringSetAsync($"cf-game-{game.Id}", JsonSerializer.Serialize(game),
                            TimeSpan.FromDays(1));

                        await Task.Delay(100);

                        var latestUpdatedMod = await cfClient.SearchModsAsync(game.Id,
                            sortField: ModsSearchSortField.LastUpdated, sortOrder: ModsSearchSortOrder.Descending,
                            pageSize: 1);

                        if (latestUpdatedMod != null && latestUpdatedMod.Error != null)
                        {
                            Console.WriteLine(
                                $"Error fetching latest updated mod for {game.Name} (GameId: {game.Id}): {latestUpdatedMod.Error.ErrorMessage}");
                            continue;
                        }

                        if (latestUpdatedMod != null && latestUpdatedMod.Pagination != null &&
                            latestUpdatedMod.Pagination.ResultCount > 0)
                        {
                            await db.ExecuteNonQueryAsync(
                                "UPDATE ProcessingGames SET LastUpdate = GETUTCDATE(), ModCount = @modCount WHERE GameId = @gameId",
                                new SqlParameter("@modCount", latestUpdatedMod.Pagination.TotalCount),
                                new SqlParameter("@gameId", game.Id)
                            );

                            var mod = latestUpdatedMod.Data.First();
                            var latestUpdatedFile = mod.LatestFiles.OrderByDescending(f => f.FileDate).FirstOrDefault();
                            if (latestUpdatedFile != null)
                            {
                                Console.WriteLine(
                                    $"Latest updated mod for {game.Name} (GameId: {game.Id}) is {mod.Name} (ModId: {mod.Id}) with {mod.DownloadCount} downloads and the latest file was updated {latestUpdatedFile.FileDate}");
                                if (lastUpdatedMod < latestUpdatedFile.FileDate)
                                {
                                    lastUpdatedMod = latestUpdatedFile.FileDate;
                                    latestUpdatedModData = mod;
                                    latestUpdatedFileData = latestUpdatedFile;
                                }

                                await _db.StringSetAsync($"cf-mod-{mod.Id}", JsonSerializer.Serialize(mod),
                                    TimeSpan.FromDays(1));
                                await _db.StringSetAsync($"cf-file-{latestUpdatedFile.Id}",
                                    JsonSerializer.Serialize(latestUpdatedFile), TimeSpan.FromDays(1));

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

                    if (lastUpdatedMod < DateTimeOffset.UtcNow.AddHours(-3) && latestUpdatedModData != null &&
                        latestUpdatedFileData != null)
                    {
                        Console.WriteLine("No mods were updated in the last 3 hours, file processing might be down.");

                        var warned = await _db.StringGetAsync("cf-file-processing-warning");

                        if (warned.HasValue && warned == "true")
                        {
                            Console.WriteLine("Already warned about this, skipping.");
                            return;
                        }

                        var httpClient = scope.ServiceProvider.GetRequiredService<IHttpClientFactory>().CreateClient();
                        var discordWebhook =
                            Environment.GetEnvironmentVariable("DISCORD_WEBHOOK", EnvironmentVariableTarget.Machine) ??
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
                    }
                }
            }
        }
    }

    public class ProcessingGames
    {
        public long Id { get; set; }
        public int GameId { get; set; }
        public string GameName { get; set; }
        public long? ModCount { get; set; }
        public bool Disabled { get; set; }
        public DateTime LastUpdate { get; set; }

        public ProcessingGames(DataRow row)
        {
            Id = (long)row["Id"];
            GameId = (int)row["GameId"];
            GameName = (string)row["GameName"];
            ModCount = (long)row["ModCount"];
            Disabled = (bool)row["Disabled"];
            LastUpdate = (DateTime)row["LastUpdate"];
        }
    }
}
