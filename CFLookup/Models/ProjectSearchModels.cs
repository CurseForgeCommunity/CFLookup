using CurseForge.APIClient.Models.Mods;

namespace CFLookup.Models
{
    public class ProjectSearchCriteria
    {
        public string? Query { get; set; }

        public List<ProjectSearchFilter> Filters { get; set; } = new();

        public int Page { get; set; } = 1;

        public int PageSize { get; set; } = 50;
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

        public DateTimeOffset DateCreated { get; set; }

        public DateTimeOffset DateModified { get; set; }

        public DateTimeOffset DateReleased { get; set; }

        public DateTimeOffset LatestUpdate { get; set; }
    }

    public class ProjectSearchPageViewModel
    {
        public ProjectSearchCriteria Criteria { get; set; } = new();

        public List<ProjectSearchResult> Results { get; set; } = new();

        public bool HasNextPage { get; set; }

        public bool HasPreviousPage => Criteria.Page > 1;
    }
}

