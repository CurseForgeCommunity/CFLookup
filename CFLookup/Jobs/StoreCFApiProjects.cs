using CurseForge.APIClient;
using CurseForge.APIClient.Models.Mods;
using Hangfire;
using Hangfire.Server;
using Npgsql;
using System.Text;
using System.Text.Json;

namespace CFLookup.Jobs
{
    [AutomaticRetry(Attempts = 0)]
    public class StoreCFApiProjects
    {
        const int BUCKET_SIZE = 10_000;
        private const int EMPTY_BUCKETS = 25;
        private const int RETRY_BATCH = 3;

        public async static Task RunAsync(PerformContext context, IJobCancellationToken token)
        {
            if (SharedMethods.CheckIfTaskIsScheduledOrInProgress("StoreCFApiFiles", "RunAsync"))
            {
                return;
            }

            using (var scope = Program.ServiceProvider.CreateScope())
            {
                try
                {
                    token.ThrowIfCancellationRequested();
                    
                    var cfClient = scope.ServiceProvider.GetRequiredService<ApiClient>();
                    cfClient.RequestDelay = TimeSpan.FromSeconds(0.05);
                    cfClient.RequestTimeout = TimeSpan.FromMinutes(5);

                    var conn = scope.ServiceProvider.GetRequiredService<NpgsqlConnection>();
                    await conn.OpenAsync();

                    var emptyBuckets = 0;

                    var buckets = SharedMethods.GetBucketRanges(1, int.MaxValue, BUCKET_SIZE);

                    foreach (var bucket in buckets)
                    {
                        token.ThrowIfCancellationRequested();
                        
                        var _bucket = Enumerable.Range(bucket.start, bucket.items);

                        var modList = await cfClient.GetModsByIdListAsync(new GetModsByIdsListRequestBody
                        {
                            FilterPcOnly = true,
                            ModIds = _bucket.ToList()
                        });
                        
                        token.ThrowIfCancellationRequested();

                        if (modList.Error != null && modList.Error.ErrorCode != 404)
                        {
                            // No-op for now, maybe Discord logs later
                            await SendDiscordErrorNotification(scope, $"The CF API threw an error at me: **{modList.Error.ErrorCode}**: {modList.Error.ErrorMessage}");
                        }

                        if (modList.Data.Count == 0)
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

                        await using var tx = await conn.BeginTransactionAsync();
                        await using var batch = new NpgsqlBatch(conn);
                        batch.Transaction = tx;
                        batch.Timeout = 600;
                        
                        token.ThrowIfCancellationRequested();

                        foreach (var mod in modList.Data)
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
                                Value = JsonSerializer.Serialize(mod.Categories)
                            });
                            cmd.Parameters.Add(new NpgsqlParameter
                            {
                                NpgsqlDbType = NpgsqlTypes.NpgsqlDbType.Integer,
                                Value = (object?)mod.ClassId ?? DBNull.Value
                            });
                            cmd.Parameters.Add(new NpgsqlParameter
                            {
                                NpgsqlDbType = NpgsqlTypes.NpgsqlDbType.Jsonb,
                                Value = JsonSerializer.Serialize(mod.Authors)
                            });
                            cmd.Parameters.Add(new NpgsqlParameter
                            {
                                NpgsqlDbType = NpgsqlTypes.NpgsqlDbType.Jsonb,
                                Value = JsonSerializer.Serialize(mod.Logo)
                            });
                            cmd.Parameters.Add(new NpgsqlParameter
                            {
                                NpgsqlDbType = NpgsqlTypes.NpgsqlDbType.Jsonb,
                                Value = JsonSerializer.Serialize(mod.Screenshots)
                            });
                            cmd.Parameters.AddWithValue(mod.MainFileId);
                            cmd.Parameters.Add(new NpgsqlParameter
                            {
                                NpgsqlDbType = NpgsqlTypes.NpgsqlDbType.Jsonb,
                                Value = JsonSerializer.Serialize(mod.LatestFiles)
                            });
                            cmd.Parameters.Add(new NpgsqlParameter
                            {
                                NpgsqlDbType = NpgsqlTypes.NpgsqlDbType.Jsonb,
                                Value = JsonSerializer.Serialize(mod.LatestFilesIndexes)
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

                            if (batch.BatchCommands.Count >= 1000)
                            {
                                if (!await ExecuteBatchWithRetries(batch))
                                {
                                    // No-op for now, maybe Discord logs later
                                }
                                token.ThrowIfCancellationRequested();
                            }
                        }

                        if (batch.BatchCommands.Count > 0)
                        {
                            if (!await ExecuteBatchWithRetries(batch))
                            {
                                // No-op for now, maybe Discord logs later
                            }
                            token.ThrowIfCancellationRequested();
                        }

                        await tx.CommitAsync();
                    }
                    
                    token.ThrowIfCancellationRequested();
                }
                catch (Exception ex)
                {
                    await SendDiscordErrorNotification(scope, $"Exception: {ex}");
                }
                finally
                {
                    BackgroundJob.Schedule(() => StoreCFApiFiles.RunAsync(null, JobCancellationToken.Null), TimeSpan.FromSeconds(10));
                }
            }
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