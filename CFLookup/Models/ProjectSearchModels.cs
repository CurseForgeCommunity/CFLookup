using CurseForge.APIClient.Models.Mods;
using System.Text;
using System.Text.Json;

namespace CFLookup.Models
{
    public class ProjectSearchCriteria
    {
        public string? Query { get; set; }

        public List<ProjectSearchFilter> Filters { get; set; } = new();

        public int? GameId { get; set; }

        public string? Cursor { get; set; }
    }

    public class SearchCursor
    {
        public string Name { get; set; } = string.Empty;

        public long ProjectId { get; set; }

        public string Direction { get; set; } = "next";

        public string Encode()
        {
            var json = JsonSerializer.Serialize(this);
            return Convert.ToBase64String(Encoding.UTF8.GetBytes(json));
        }

        public static bool TryDecode(string? token, out SearchCursor? cursor)
        {
            cursor = null;

            if (string.IsNullOrWhiteSpace(token))
            {
                return false;
            }

            try
            {
                var json = Encoding.UTF8.GetString(Convert.FromBase64String(token));
                cursor = JsonSerializer.Deserialize<SearchCursor>(json);
                return cursor != null && !string.IsNullOrEmpty(cursor.Name);
            }
            catch
            {
                return false;
            }
        }
    }

    public class ProjectSearchFilter
    {
        public string? Field { get; set; }

        public string? Operator { get; set; }

        public string? Value { get; set; }
    }

    public class ProjectSearchResult
    {
        public long ProjectId { get; set; }

        public int GameId { get; set; }

        public string Name { get; set; } = string.Empty;

        public string Slug { get; set; } = string.Empty;

        public string Summary { get; set; } = string.Empty;

        public ModStatus Status { get; set; }

        public long DownloadCount { get; set; }

        public bool IsFeatured { get; set; }

        public int PrimaryCategoryId { get; set; }

        public int ClassId { get; set; }

        public bool AllowModDistribution { get; set; }

        public long GamePopularityRank { get; set; }

        public bool IsAvailable { get; set; }

        public long ThumbsUpCount { get; set; }

        public double? Rating { get; set; }

        public DateTimeOffset DateCreated { get; set; }

        public DateTimeOffset DateModified { get; set; }

        public DateTimeOffset DateReleased { get; set; }

        public DateTimeOffset LatestUpdate { get; set; }
        public string GameName { get; set; }
    }

    public class ProjectSearchPageViewModel
    {
        public ProjectSearchCriteria Criteria { get; set; } = new();

        public List<ProjectSearchResult> Results { get; set; } = new();

        public string? NextCursor { get; set; }

        public string? PreviousCursor { get; set; }

        public bool HasNext { get; set; }

        public bool HasPrevious { get; set; }
    }

    public class GameProjectStat
    {
        public int GameId { get; set; }

        public string Name { get; set; } = string.Empty;

        public long ProjectCount { get; set; }
    }

    public class FieldRangeStats
    {
        public long? MinDownloadCount { get; set; }
        public long? MaxDownloadCount { get; set; }

        public long? MinThumbsUpCount { get; set; }
        public long? MaxThumbsUpCount { get; set; }

        public double? MinRating { get; set; }
        public double? MaxRating { get; set; }

        public long? MinGamePopularityRank { get; set; }
        public long? MaxGamePopularityRank { get; set; }

        public DateTimeOffset? MinDateCreated { get; set; }
        public DateTimeOffset? MaxDateCreated { get; set; }

        public DateTimeOffset? MinDateModified { get; set; }
        public DateTimeOffset? MaxDateModified { get; set; }

        public DateTimeOffset? MinDateReleased { get; set; }
        public DateTimeOffset? MaxDateReleased { get; set; }

        public DateTimeOffset? MinLatestUpdate { get; set; }
        public DateTimeOffset? MaxLatestUpdate { get; set; }
    }
}

