using CFLookup.Models;
using InfluxDB.Client;
using InfluxDB.Client.Api.Domain;
using InfluxDB.Client.Writes;

namespace CFLookup
{
    public class InfluxDBWriter(InfluxDBClient influxClient)
    {
        public async Task WriteBatchAsync(string org, string bucket, IEnumerable<InfluxProjectMetric> metrics)
        {
            var writeApi = influxClient.GetWriteApiAsync();

            await writeApi.WritePointsAsync(
                metrics.Select(m => 
                    PointData.Measurement("cf_project_metrics")
                        .Tag("project_id", m.ProjectId.ToString())
                        .Tag("game_id", m.GameId.ToString())
                        .Field("download_count", m.DownloadCount)
                        .Field("thumbs_up_count", m.ThumbsUpCount)
                        .Field("game_popularity_rank", m.GamePopularityRank)
                        .Timestamp(m.Timestamp, WritePrecision.Ns)
                ).ToList(),
                bucket,
                org,
                CancellationToken.None
            );
        }
    }
}