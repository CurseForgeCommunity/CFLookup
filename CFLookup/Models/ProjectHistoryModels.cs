using System.Text.Json;
using System.Text.Json.Serialization;

namespace CFLookup.Models
{
    public class ProjectHistoryEntry
    {
        public DateTimeOffset ChangedAt { get; set; }

        public Dictionary<string, ProjectHistoryFieldChange> Changes { get; set; } = new();
    }

    public class ProjectHistoryFieldChange
    {
        // The Postgres trigger writes lowercase "old"/"new" keys (jsonb_build_object('old', ..., 'new', ...)) -
        // System.Text.Json's property matching is case-sensitive by default, so without these attributes
        // both properties silently stay null instead of erroring, which is what was rendering as "(none)".
        [JsonPropertyName("old")]
        public JsonElement? Old { get; set; }

        [JsonPropertyName("new")]
        public JsonElement? New { get; set; }
    }
}
