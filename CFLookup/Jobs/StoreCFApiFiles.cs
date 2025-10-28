using CurseForge.APIClient;
using CurseForge.APIClient.Models.Files;
using Hangfire;
using Hangfire.Server;
using Npgsql;
using System.Text;
using System.Text.Json;

namespace CFLookup.Jobs
{
    [AutomaticRetry(Attempts = 0)]
    public class StoreCFApiFiles
    {
        const int BUCKET_SIZE = 10_000;
        private const int EMPTY_BUCKETS = 300;
        private const int RETRY_BATCH = 3;

        public async static Task RunAsync(PerformContext context)
        {
            using (var scope = Program.ServiceProvider.CreateScope())
            {
                try
                {
                    var cfClient = scope.ServiceProvider.GetRequiredService<ApiClient>();
                    cfClient.RequestDelay = TimeSpan.FromSeconds(0.05);
                    cfClient.RequestTimeout = TimeSpan.FromMinutes(5);

                    var conn = scope.ServiceProvider.GetRequiredService<NpgsqlConnection>();
                    await conn.OpenAsync();

                    var emptyBuckets = 0;

                    var buckets = SharedMethods.GetBucketRanges(1, int.MaxValue, BUCKET_SIZE);

                    foreach (var bucket in buckets)
                    {
                        var _bucket = Enumerable.Range(bucket.start, bucket.items);

                        var modList = await cfClient.GetFilesAsync(new GetModFilesRequestBody
                        {
                            FileIds = _bucket.ToList()
                        });

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

                        foreach (var mod in modList.Data)
                        {
                            var cmd = new NpgsqlBatchCommand("""

                                                             INSERT INTO file_data (
                                                                fileid,
                                                             	gameid,
                                                             	projectid,
                                                             	isavailable,
                                                             	displayname,
                                                             	filename,
                                                             	releasetype,
                                                             	filestatus,
                                                             	hashes,
                                                             	filedate,
                                                             	filelength,
                                                             	filesizeondisk,
                                                             	downloadcount,
                                                             	downloadurl,
                                                             	gameversions,
                                                             	sortablegameversions,
                                                             	dependencies,
                                                             	exposeasalternative,
                                                             	parentprojectfileid,
                                                             	alternatefileid,
                                                             	isserverpack,
                                                             	serverpackfileid,
                                                             	isearlyaccesscontent,
                                                             	earlyaccessenddate,
                                                             	filefingerprint,
                                                             	modules
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
                                                             ON CONFLICT (fileid, gameid, projectid) DO UPDATE
                                                             SET
                                                                isavailable=EXCLUDED.isavailable,
                                                             	displayname=EXCLUDED.displayname,
                                                             	filename=EXCLUDED.filename,
                                                             	releasetype=EXCLUDED.releasetype,
                                                             	filestatus=EXCLUDED.filestatus,
                                                             	hashes=EXCLUDED.hashes,
                                                             	filedate=EXCLUDED.filedate,
                                                             	filelength=EXCLUDED.filelength,
                                                             	filesizeondisk=EXCLUDED.filesizeondisk,
                                                             	downloadcount=EXCLUDED.downloadcount,
                                                             	downloadurl=EXCLUDED.downloadurl,
                                                             	gameversions=EXCLUDED.gameversions,
                                                             	sortablegameversions=EXCLUDED.sortablegameversions,
                                                             	dependencies=EXCLUDED.dependencies,
                                                             	exposeasalternative=EXCLUDED.exposeasalternative,
                                                             	parentprojectfileid=EXCLUDED.parentprojectfileid,
                                                             	alternatefileid=EXCLUDED.alternatefileid,
                                                             	isserverpack=EXCLUDED.isserverpack,
                                                             	serverpackfileid=EXCLUDED.serverpackfileid,
                                                             	isearlyaccesscontent=EXCLUDED.isearlyaccesscontent,
                                                             	earlyaccessenddate=EXCLUDED.earlyaccessenddate,
                                                             	filefingerprint=EXCLUDED.filefingerprint,
                                                             	modules=EXCLUDED.modules,
                                                             	latestupdate=timezone('UTC'::text, now());
                                                             """);

                            cmd.Parameters.AddWithValue(mod.Id);
                            cmd.Parameters.AddWithValue(mod.GameId);
                            cmd.Parameters.AddWithValue(mod.ModId);
                            cmd.Parameters.AddWithValue(mod.IsAvailable);
                            cmd.Parameters.AddWithValue(mod.DisplayName);
                            cmd.Parameters.AddWithValue(mod.FileName);
                            cmd.Parameters.AddWithValue((int)mod.ReleaseType);
                            cmd.Parameters.AddWithValue((int)mod.FileStatus);
                            cmd.Parameters.Add(new NpgsqlParameter()
                            {
                                NpgsqlDbType = NpgsqlTypes.NpgsqlDbType.Jsonb,
                                Value = JsonSerializer.Serialize(mod.Hashes)
                            });
                            cmd.Parameters.AddWithValue(mod.FileDate);
                            cmd.Parameters.AddWithValue(mod.FileLength);
                            cmd.Parameters.Add(new NpgsqlParameter
                            {
                                NpgsqlDbType = NpgsqlTypes.NpgsqlDbType.Bigint,
                                Value = (object?)mod.FileSizeOnDisk ?? DBNull.Value
                            });
                            cmd.Parameters.AddWithValue(mod.DownloadCount);
                            cmd.Parameters.AddWithValue(mod.DownloadUrl ?? string.Empty);
                            cmd.Parameters.Add(new NpgsqlParameter()
                            {
                                NpgsqlDbType = NpgsqlTypes.NpgsqlDbType.Jsonb,
                                Value = JsonSerializer.Serialize(mod.GameVersions)
                            });
                            cmd.Parameters.Add(new NpgsqlParameter()
                            {
                                NpgsqlDbType = NpgsqlTypes.NpgsqlDbType.Jsonb,
                                Value = JsonSerializer.Serialize(mod.SortableGameVersions)
                            });
                            cmd.Parameters.Add(new NpgsqlParameter()
                            {
                                NpgsqlDbType = NpgsqlTypes.NpgsqlDbType.Jsonb,
                                Value = JsonSerializer.Serialize(mod.Dependencies)
                            });
                            
                            cmd.Parameters.Add(new NpgsqlParameter
                            {
                                NpgsqlDbType = NpgsqlTypes.NpgsqlDbType.Boolean,
                                Value = (object?)mod.ExposeAsAlternative ?? DBNull.Value
                            });
                            cmd.Parameters.Add(new NpgsqlParameter
                            {
                                NpgsqlDbType = NpgsqlTypes.NpgsqlDbType.Integer,
                                Value = (object?)mod.ParentProjectFileId ?? DBNull.Value
                            });
                            cmd.Parameters.Add(new NpgsqlParameter
                            {
                                NpgsqlDbType = NpgsqlTypes.NpgsqlDbType.Integer,
                                Value = (object?)mod.AlternateFileId ?? DBNull.Value
                            });
                            cmd.Parameters.Add(new NpgsqlParameter
                            {
                                NpgsqlDbType = NpgsqlTypes.NpgsqlDbType.Boolean,
                                Value = (object?)mod.IsServerPack ?? DBNull.Value
                            });
                            cmd.Parameters.Add(new NpgsqlParameter
                            {
                                NpgsqlDbType = NpgsqlTypes.NpgsqlDbType.Integer,
                                Value = (object?)mod.ServerPackFileId ?? DBNull.Value
                            });
                            cmd.Parameters.Add(new NpgsqlParameter
                            {
                                NpgsqlDbType = NpgsqlTypes.NpgsqlDbType.Boolean,
                                Value = (object?)mod.IsEarlyAccessContent ?? DBNull.Value
                            });
                            cmd.Parameters.Add(new NpgsqlParameter
                            {
                                NpgsqlDbType = NpgsqlTypes.NpgsqlDbType.TimestampTz,
                                Value = (object?)mod.EarlyAccessEndDate ?? DBNull.Value
                            });
                            cmd.Parameters.AddWithValue(mod.FileFingerprint);
                            cmd.Parameters.Add(new NpgsqlParameter()
                            {
                                NpgsqlDbType = NpgsqlTypes.NpgsqlDbType.Jsonb,
                                Value = JsonSerializer.Serialize(mod.Modules)
                            });

                            batch.BatchCommands.Add(cmd);

                            if (batch.BatchCommands.Count >= 1000)
                            {
                                if (!await ExecuteBatchWithRetries(batch))
                                {
                                    // No-op for now, maybe Discord logs later
                                }
                            }
                        }

                        if (batch.BatchCommands.Count > 0)
                        {
                            if (!await ExecuteBatchWithRetries(batch))
                            {
                                // No-op for now, maybe Discord logs later
                            }
                        }

                        await tx.CommitAsync();
                    }

                }
                catch (Exception ex)
                {
                    await SendDiscordErrorNotification(scope, $"Exception: {ex}");
                }
                finally
                {
                    BackgroundJob.Schedule(() => StoreCFApiFiles.RunAsync(null), TimeSpan.FromMinutes(30));
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
                        @$"An error occurred while trying to store all project files from CurseForge, the command will run again in 30 minutes.
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
