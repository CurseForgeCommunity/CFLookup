namespace CFLookup.Models
{
    public class InfluxProjectMetric
    {
        public required long ProjectId { get; set; }
        public required int GameId { get; set; }
        public required double DownloadCount { get; set; }
        public required int ThumbsUpCount { get; set; }
        public required int GamePopularityRank { get; set; }
        public DateTime Timestamp { get; set; }
    }
}