using CFLookup.Models;
using CurseForge.APIClient;
using CurseForge.APIClient.Models;
using CurseForge.APIClient.Models.Mods;
using Hangfire;
using Hangfire.Console;
using Hangfire.Server;
using Npgsql;
using StackExchange.Redis;
using System.Diagnostics;
using System.Text;
using System.Text.Json;

namespace CFLookup.Jobs
{
    [AutomaticRetry(Attempts = 0)]
    public class StoreCFApiProjects
    {
        const int BUCKET_SIZE = 10_000;
        private const int EMPTY_BUCKETS = 25;
        private const int RETRY_BATCH = 10;

        public async static Task RunAsync(PerformContext context, IJobCancellationToken token)
        {
            await using var scope = Program.ServiceProvider.CreateAsyncScope();
            try
            {
                token.ThrowIfCancellationRequested();
                    
                var _redis = scope.ServiceProvider.GetRequiredService<ConnectionMultiplexer>();

                await using (var jobLock = await RedisJobLock.CreateAsync(
                                 _redis.GetDatabase(0),
                                 "StoreCFApiProjects",
                                 TimeSpan.FromSeconds(15)))
                {
                    if (jobLock == null)
                    {
                        return;
                    }

                    var startTime = Stopwatch.GetTimestamp();
                    context.WriteLine($"Project indexing started: {DateTimeOffset.UtcNow}");

                    var cfClient = scope.ServiceProvider.GetRequiredService<ApiClient>();

                    var emptyBuckets = 0;

                    var buckets = SharedMethods.GetBucketRanges(1, int.MaxValue, BUCKET_SIZE);
                    
                    context.WriteLine($"Fetching {buckets.Count} with {BUCKET_SIZE} per bucket of projects");

                    foreach (var bucket in buckets)
                    {
                        token.ThrowIfCancellationRequested();

                        var _bucket = Enumerable.Range(bucket.start, bucket.items).ToList();

                        var (modList, retries) = await GetMods(scope, cfClient, _bucket);
                        
                        token.ThrowIfCancellationRequested();
                        
                        if (modList is { Count: 0 })
                        {
                            emptyBuckets++;
                            if (emptyBuckets >= EMPTY_BUCKETS)
                            {
                                break;
                            }
                        }
                        else
                        {
                            emptyBuckets = 0;
                        }
                        
                        if (retries >= 5 || modList is null)
                        {
                            continue;
                        }

                        var influxWriter = scope.ServiceProvider.GetRequiredService<InfluxDBWriter>();

                        var ds = scope.ServiceProvider.GetRequiredService<NpgsqlDataSource>();
                        await using var conn = await ds.OpenConnectionAsync();

                        await using var tx = await conn.BeginTransactionAsync();
                        await using var batch = new NpgsqlBatch(conn);
                        batch.Transaction = tx;
                        batch.Timeout = 600;

                        token.ThrowIfCancellationRequested();

                        var projectMetrics = new List<InfluxProjectMetric>();

                        var currentTs = DateTime.UtcNow;

                        foreach (var mod in modList)
                        {
                            token.ThrowIfCancellationRequested();

                            var cmd = new NpgsqlBatchCommand("""

                                                             INSERT INTO project_data (
                                                             	projectid,
                                                             	gameid,
                                                             	name,
                                                             	slug,
                                                             	links,
                                                             	summary,
                                                             	status,
                                                             	downloadcount,
                                                             	isfeatured,
                                                             	primarycategoryid,
                                                             	categories,
                                                             	classid,
                                                             	authors,
                                                             	logo,
                                                             	screenshots,
                                                             	mainfileid,
                                                             	latestfiles,
                                                             	latestfileindexes,
                                                             	datecreated,
                                                             	datemodified,
                                                             	datereleased,
                                                             	allowmoddistribution,
                                                             	gamepopularityrank,
                                                             	isavailable,
                                                             	thumbsupcount,
                                                             	rating
                                                             ) 
                                                             VALUES (
                                                             	$1,
                                                             	$2,
                                                             	$3,
                                                             	$4,
                                                             	$5,
                                                             	$6,
                                                             	$7,
                                                             	$8,
                                                             	$9,
                                                             	$10,
                                                             	$11,
                                                             	$12,
                                                             	$13,
                                                             	$14,
                                                             	$15,
                                                             	$16,
                                                             	$17,
                                                             	$18,
                                                             	$19,
                                                             	$20,
                                                             	$21,
                                                             	$22,
                                                             	$23,
                                                             	$24,
                                                             	$25,
                                                             	$26
                                                             )
                                                             ON CONFLICT (projectid, gameid) DO UPDATE
                                                             SET 
                                                             	name=EXCLUDED.name,
                                                             	slug=EXCLUDED.slug,
                                                             	links=EXCLUDED.links,
                                                             	summary=EXCLUDED.summary,
                                                             	status=EXCLUDED.status,
                                                             	downloadcount=EXCLUDED.downloadcount,
                                                             	isfeatured=EXCLUDED.isfeatured,
                                                             	primarycategoryid=EXCLUDED.primarycategoryid,
                                                             	categories=EXCLUDED.categories,
                                                             	classid=EXCLUDED.classid,
                                                             	authors=EXCLUDED.authors,
                                                             	logo=EXCLUDED.logo,
                                                             	screenshots=EXCLUDED.screenshots,
                                                             	mainfileid=EXCLUDED.mainfileid,
                                                             	latestfiles=EXCLUDED.latestfiles,
                                                             	latestfileindexes=EXCLUDED.latestfileindexes,
                                                             	datecreated=EXCLUDED.datecreated,
                                                             	datemodified=EXCLUDED.datemodified,
                                                             	datereleased=EXCLUDED.datereleased,
                                                             	allowmoddistribution=EXCLUDED.allowmoddistribution,
                                                             	gamepopularityrank=EXCLUDED.gamepopularityrank,
                                                             	isavailable=EXCLUDED.isavailable,
                                                             	thumbsupcount=EXCLUDED.thumbsupcount,
                                                             	rating=EXCLUDED.rating,
                                                             	latestupdate=timezone('UTC'::text, now());

                                                             """);

                            cmd.Parameters.AddWithValue(mod.Id);
                            cmd.Parameters.AddWithValue(mod.GameId);
                            cmd.Parameters.AddWithValue(mod.Name);
                            cmd.Parameters.AddWithValue(mod.Slug);
                            cmd.Parameters.Add(new NpgsqlParameter()
                            {
                                NpgsqlDbType = NpgsqlTypes.NpgsqlDbType.Jsonb,
                                Value = JsonSerializer.Serialize(mod.Links)
                            });
                            cmd.Parameters.AddWithValue(mod.Summary.Replace("\0", ""));
                            cmd.Parameters.AddWithValue((int)mod.Status);
                            cmd.Parameters.AddWithValue(mod.DownloadCount);
                            cmd.Parameters.AddWithValue(mod.IsFeatured);
                            cmd.Parameters.AddWithValue(mod.PrimaryCategoryId);
                            cmd.Parameters.Add(new NpgsqlParameter
                            {
                                NpgsqlDbType = NpgsqlTypes.NpgsqlDbType.Jsonb,
                                Value = JsonSerializer.Serialize(mod.Categories.OrderBy(c => c.Id))
                            });
                            cmd.Parameters.Add(new NpgsqlParameter
                            {
                                NpgsqlDbType = NpgsqlTypes.NpgsqlDbType.Integer,
                                Value = (object?)mod.ClassId ?? DBNull.Value
                            });
                            cmd.Parameters.Add(new NpgsqlParameter
                            {
                                NpgsqlDbType = NpgsqlTypes.NpgsqlDbType.Jsonb,
                                Value = JsonSerializer.Serialize(mod.Authors.OrderBy(c => c.Id))
                            });
                            cmd.Parameters.Add(new NpgsqlParameter
                            {
                                NpgsqlDbType = NpgsqlTypes.NpgsqlDbType.Jsonb,
                                Value = JsonSerializer.Serialize(mod.Logo)
                            });
                            cmd.Parameters.Add(new NpgsqlParameter
                            {
                                NpgsqlDbType = NpgsqlTypes.NpgsqlDbType.Jsonb,
                                Value = JsonSerializer.Serialize(mod.Screenshots.OrderBy(c => c.Id))
                            });
                            cmd.Parameters.AddWithValue(mod.MainFileId);
                            cmd.Parameters.Add(new NpgsqlParameter
                            {
                                NpgsqlDbType = NpgsqlTypes.NpgsqlDbType.Jsonb,
                                Value = JsonSerializer.Serialize(mod.LatestFiles.OrderByDescending(c => c.Id))
                            });
                            cmd.Parameters.Add(new NpgsqlParameter
                            {
                                NpgsqlDbType = NpgsqlTypes.NpgsqlDbType.Jsonb,
                                Value = JsonSerializer.Serialize(mod.LatestFilesIndexes.OrderByDescending(c => c.FileId).ThenBy(c => c.GameVersion))
                            });
                            cmd.Parameters.AddWithValue(mod.DateCreated);
                            cmd.Parameters.AddWithValue(mod.DateModified);
                            cmd.Parameters.AddWithValue(mod.DateReleased);
                            cmd.Parameters.Add(new NpgsqlParameter
                            {
                                NpgsqlDbType = NpgsqlTypes.NpgsqlDbType.Boolean,
                                Value = (object?)mod.AllowModDistribution ?? DBNull.Value
                            });
                            cmd.Parameters.AddWithValue(mod.GamePopularityRank);
                            cmd.Parameters.AddWithValue(mod.IsAvailable);
                            cmd.Parameters.AddWithValue(mod.ThumbsUpCount);
                            cmd.Parameters.Add(new NpgsqlParameter
                            {
                                NpgsqlDbType = NpgsqlTypes.NpgsqlDbType.Double,
                                Value = (object?)mod.Rating ?? DBNull.Value
                            });

                            batch.BatchCommands.Add(cmd);

                            projectMetrics.Add(new InfluxProjectMetric
                            {
                                ProjectId = mod.Id,
                                GameId = mod.GameId,
                                DownloadCount = mod.DownloadCount,
                                ThumbsUpCount = mod.ThumbsUpCount,
                                GamePopularityRank = mod.GamePopularityRank,
                                Timestamp = currentTs
                            });

                            if (batch.BatchCommands.Count >= 1000)
                            {
                                if (!await ExecuteBatchWithRetries(batch))
                                {
                                    await SendDiscordErrorNotification(scope, $"Problem with saving the batch data for projects {bucket.start} -  {bucket.end}");
                                }

                                await influxWriter.WriteBatchAsync("CFLookup", "cf_project_data", projectMetrics);
                                projectMetrics.Clear();
                                currentTs = DateTime.UtcNow;
                                token.ThrowIfCancellationRequested();
                            }
                        }

                        if (batch.BatchCommands.Count > 0)
                        {
                            if (!await ExecuteBatchWithRetries(batch))
                            {
                                await SendDiscordErrorNotification(scope, $"Problem with saving the batch data for projects {bucket.start} -  {bucket.end}");
                            }

                            await influxWriter.WriteBatchAsync("CFLookup", "cf_project_data", projectMetrics);
                            projectMetrics.Clear();

                            token.ThrowIfCancellationRequested();
                        }

                        await tx.CommitAsync();
                    }

                    token.ThrowIfCancellationRequested();
                }
            }
            catch (Exception ex)
            {
                await SendDiscordErrorNotification(scope, $"Exception: {ex}");
            }
        }

        private async static Task<(List<Mod>? modList, int retries)> GetMods(AsyncServiceScope scope, ApiClient cfClient, List<int> projectIdBucket)
        {
            var retries = 0;

            while (true)
            {
                try
                {
                    var modList = await cfClient.GetModsByIdListAsync(new GetModsByIdsListRequestBody
                    {
                        FilterPcOnly = true,
                        ModIds = projectIdBucket
                    });

                    if (modList.Error != null && modList.Error.ErrorCode != 404 &&
                        !string.IsNullOrWhiteSpace(modList.Error.ErrorMessage))
                    {
                        switch (modList.Error.ErrorCode)
                        {
                            case 502:
                            case 504:
                                await Task.Delay(TimeSpan.FromSeconds(30));
                                retries++;
                                continue;
                            default:
                                await Task.Delay(TimeSpan.FromSeconds(30));
                                retries++;
                                await SendDiscordErrorNotification(scope,
                                    $"The CF API threw an error at me: **{modList.Error.ErrorCode}**: {modList.Error.ErrorMessage}");
                                continue;
                        }
                    }

                    return (modList.Data, retries);
                }
                catch
                {
                    await Task.Delay(TimeSpan.FromSeconds(30));
                    retries++;
                    if (retries >= 5)
                    {
                        break;
                    }
                }
            }

            return  (null, retries);
        }

        private async static Task SendDiscordErrorNotification(IServiceScope scope, string webhookMessage)
        {
            try
            {
                var httpClient = scope.ServiceProvider.GetRequiredService<IHttpClientFactory>().CreateClient();
                var discordWebhook =
                    Environment.GetEnvironmentVariable("DISCORD_WEBHOOK_PROJECT", EnvironmentVariableTarget.Machine) ??
                    Environment.GetEnvironmentVariable("DISCORD_WEBHOOK_PROJECT", EnvironmentVariableTarget.User) ??
                    Environment.GetEnvironmentVariable("DISCORD_WEBHOOK_PROJECT", EnvironmentVariableTarget.Process) ??
                    string.Empty;

                if (!string.IsNullOrWhiteSpace(discordWebhook))
                {
                    var message =
                        @$"An error occurred while trying to store all projects from CurseForge, the command will run again in 30 minutes.
{webhookMessage}";
                    var payload = new
                    {
                        content = message,
                        flags = 4
                    };

                    var json = JsonSerializer.Serialize(payload);
                    var content = new StringContent(json, Encoding.UTF8, "application/json");
                    await httpClient.PostAsync(discordWebhook, content);
                }
            }
            catch
            {
                // No-op
            }
        }

        private async static Task<bool> ExecuteBatchWithRetries(NpgsqlBatch batch, int retries = RETRY_BATCH)
        {
            for (var attempt = 1; attempt <= retries; attempt++)
            {
                try
                {
                    await batch.ExecuteNonQueryAsync();
                    batch.BatchCommands.Clear();
                    return true;
                }
                catch (Exception)
                {
                    if (attempt == retries)
                    {
                        return false;
                    }

                    await Task.Delay(1000 * attempt);
                }
            }

            return false;
        }
    }
}
