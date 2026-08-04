
using System.Data;
using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using CFLookup.Models;
using CurseForge.APIClient;
using CurseForge.APIClient.Models.Mods;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Npgsql;
using StackExchange.Redis;

namespace CFLookup.Pages
{
    public class ProjectSearchModel : PageModel
    {
        private const int PageSize = 100;
        
        private readonly NpgsqlDataSource _db;
        private readonly IDatabaseAsync _redis;
        private readonly ApiClient _cfApiClient;
        private readonly ILogger<ProjectSearchModel> _logger;

        [BindProperty(SupportsGet = true)]
        public ProjectSearchCriteria Criteria { get; set; } = new();

        public ProjectSearchPageViewModel ViewModel { get; set; } = new();

        public string? SelectedGameName { get; set; }

        public ProjectSearchModel(NpgsqlDataSource db, ConnectionMultiplexer connectionMultiplexer, ApiClient cfApiClient, ILogger<ProjectSearchModel> logger)
        {
            _db = db;
            _redis = connectionMultiplexer.GetDatabase(5);
            _cfApiClient = cfApiClient;
            _logger = logger;
        }

        public async Task<IActionResult> OnGetAsync()
        {
            await ExecuteSearchAsync();
            return Page();
        }

        public async Task<IActionResult> OnPostAsync()
        {
            await ExecuteSearchAsync();
            return Page();
        }

        private async Task ExecuteSearchAsync()
        {
            await using var conn = await _db.OpenConnectionAsync();
            var sqlBuilder = new StringBuilder();
            sqlBuilder.Append(
                """
                    SELECT pd.projectid, pd.gameid, pd.name, pd.slug, pd.summary, pd.status, pd.downloadcount, pd.isfeatured,
                         pd.primarycategoryid, pd.classid, pd.allowmoddistribution, pd.gamepopularityrank,
                         pd.isavailable, pd.thumbsupcount, pd.rating, pd.datecreated, pd.datemodified, pd.datereleased, pd.latestupdate,
                         gd.name as gamename
                    FROM project_data pd
                    LEFT JOIN game_data gd ON pd.gameid = gd.gameid
                """);

            await using var cmd = conn.CreateCommand();
            cmd.CommandType = CommandType.Text;

            var conditions = BuildFilterConditions(cmd);

            if (conn.State != ConnectionState.Open)
            {
                await conn.OpenAsync();
            }

            await using var countCmd = conn.CreateCommand();
            countCmd.CommandType = CommandType.Text;
            var countConditions = BuildFilterConditions(countCmd);

            var countSql = new StringBuilder("SELECT COUNT(*) FROM project_data pd LEFT JOIN game_data gd ON pd.gameid = gd.gameid");
            if (countConditions.Count > 0)
            {
                countSql.Append(" WHERE ").Append(string.Join(" AND ", countConditions));
            }

            countCmd.CommandText = countSql.ToString();
            var totalCount = (long)(await countCmd.ExecuteScalarAsync() ?? 0L);

            SearchCursor.TryDecode(Criteria.Cursor, out var cursor);
            var isPrevious = cursor is { Direction: "prev" };

            if (cursor != null)
            {
                conditions.Add(isPrevious
                    ? "(pd.name, pd.projectid) > (@cursorName, @cursorId)"
                    : "(pd.name, pd.projectid) < (@cursorName, @cursorId)");

                cmd.Parameters.AddWithValue("cursorName", cursor.Name);
                cmd.Parameters.AddWithValue("cursorId", cursor.ProjectId);
            }

            if (conditions.Count > 0)
            {
                sqlBuilder.Append(" WHERE ");
                sqlBuilder.Append(string.Join(" AND ", conditions));
            }

            // Keyset pagination needs a fully-ordered key - name alone isn't unique, so projectid
            // (the primary key) breaks ties. "Previous" walks the same key ascending, then the
            // fetched page is reversed below to restore normal descending display order.
            var sortDirection = isPrevious ? "ASC" : "DESC";
            sqlBuilder.Append($" ORDER BY pd.name {sortDirection}, pd.projectid {sortDirection}");
            sqlBuilder.Append($" LIMIT {PageSize + 1}");

            cmd.CommandText = sqlBuilder.ToString();

            var results = new List<ProjectSearchResult>();

            ViewModel = new ProjectSearchPageViewModel
            {
                Criteria = Criteria,
                TotalCount = totalCount
            };

            try
            {
                await using var reader = await cmd.ExecuteReaderAsync();

                while (await reader.ReadAsync())
                {
                    var gameNameOrdinal = reader.GetOrdinal("gamename");
                    var classIdOrdinal = reader.GetOrdinal("classid");

                    var result = new ProjectSearchResult
                    {
                        ProjectId = reader.GetInt64(reader.GetOrdinal("projectid")),
                        GameId = reader.GetInt32(reader.GetOrdinal("gameid")),
                        GameName = reader.IsDBNull(gameNameOrdinal) ? string.Empty : reader.GetString(gameNameOrdinal),
                        Name = reader.GetString(reader.GetOrdinal("name")),
                        Slug = reader.GetString(reader.GetOrdinal("slug")),
                        Summary = reader.GetString(reader.GetOrdinal("summary")),
                        Status = (ModStatus)reader.GetInt32(reader.GetOrdinal("status")),
                        DownloadCount = reader.GetInt64(reader.GetOrdinal("downloadcount")),
                        IsFeatured = reader.GetBoolean(reader.GetOrdinal("isfeatured")),
                        PrimaryCategoryId = reader.GetInt32(reader.GetOrdinal("primarycategoryid")),
                        ClassId = reader.IsDBNull(classIdOrdinal) ? 0 : reader.GetInt32(classIdOrdinal),
                        AllowModDistribution = !reader.IsDBNull("allowmoddistribution") && reader.GetBoolean(reader.GetOrdinal("allowmoddistribution")),
                        GamePopularityRank = reader.GetInt64(reader.GetOrdinal("gamepopularityrank")),
                        IsAvailable = reader.GetBoolean(reader.GetOrdinal("isavailable")),
                        ThumbsUpCount = reader.GetInt64(reader.GetOrdinal("thumbsupcount")),
                        Rating = reader.IsDBNull("rating") ? null : reader.GetDouble(reader.GetOrdinal("rating")),
                        DateCreated = reader.GetDateTime(reader.GetOrdinal("datecreated")),
                        DateModified = reader.GetDateTime(reader.GetOrdinal("datemodified")),
                        DateReleased = reader.GetDateTime(reader.GetOrdinal("datereleased")),
                        LatestUpdate = reader.GetDateTime(reader.GetOrdinal("latestupdate"))
                    };

                    results.Add(result);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error executing project search query.");
            }

            var hasMore = results.Count > PageSize;
            if (hasMore)
            {
                results.RemoveAt(results.Count - 1);
            }

            if (isPrevious)
            {
                results.Reverse();
            }

            ViewModel.Results = results;

            if (results.Count > 0)
            {
                var first = results[0];
                var last = results[^1];

                ViewModel.PreviousCursor = new SearchCursor { Name = first.Name, ProjectId = first.ProjectId, Direction = "prev" }.Encode();
                ViewModel.NextCursor = new SearchCursor { Name = last.Name, ProjectId = last.ProjectId, Direction = "next" }.Encode();

                // Once you've stepped in a direction, a page in the opposite direction is guaranteed
                // to exist (it's the one you came from) - only the direction you're heading needs
                // the "is there actually another page" check via the 101st-row probe above.
                ViewModel.HasPrevious = cursor == null ? false : (isPrevious ? hasMore : true);
                ViewModel.HasNext = cursor == null ? hasMore : (isPrevious ? true : hasMore);
            }
            else if (cursor != null)
            {
                // Ran off the end of the result set in this direction - still offer a way back
                // to where we came from, using the incoming cursor's own key.
                ViewModel.HasPrevious = !isPrevious;
                ViewModel.HasNext = isPrevious;
                ViewModel.PreviousCursor = isPrevious ? null : new SearchCursor { Name = cursor.Name, ProjectId = cursor.ProjectId, Direction = "prev" }.Encode();
                ViewModel.NextCursor = isPrevious ? new SearchCursor { Name = cursor.Name, ProjectId = cursor.ProjectId, Direction = "next" }.Encode() : null;
            }

            if (Criteria.GameId.HasValue)
            {
                SelectedGameName = await SharedMethods.GetGameNameForDisplay(conn, _redis, _cfApiClient, Criteria.GameId.Value);
            }
        }

        // Shared between the results query and the COUNT(*) query so the two can never drift apart -
        // "how many total" must always reflect exactly the same WHERE clause as "which ones are shown".
        // Cursor pagination is deliberately excluded here; the total reflects the search, not the page.
        private List<string> BuildFilterConditions(NpgsqlCommand targetCmd)
        {
            var conditions = new List<string>();

            if (!string.IsNullOrWhiteSpace(Criteria.Query))
            {
                var paramName = "q";
                conditions.Add("(pd.name ILIKE @" + paramName + " OR pd.slug ILIKE @" + paramName + " OR pd.summary ILIKE @" + paramName + ")");
                targetCmd.Parameters.AddWithValue(paramName, $"%{Criteria.Query.Trim()}%");
            }

            if (Criteria.GameId.HasValue)
            {
                conditions.Add("pd.gameid = @gameId");
                targetCmd.Parameters.AddWithValue("gameId", Criteria.GameId.Value);
            }

            if (Criteria.Filters is { Count: > 0 })
            {
                var paramIndex = 0;
                foreach (var filter in Criteria.Filters)
                {
                    if (filter == null ||
                        string.IsNullOrWhiteSpace(filter.Field) ||
                        string.IsNullOrWhiteSpace(filter.Operator) ||
                        string.IsNullOrWhiteSpace(filter.Value))
                    {
                        continue;
                    }

                    if (filter.Field == "Author")
                    {
                        if (TryBuildAuthorCondition(filter, targetCmd, ref paramIndex, out var authorCondition))
                        {
                            conditions.Add(authorCondition);
                        }

                        continue;
                    }

                    if (!TryGetColumnInfo(filter.Field, out var columnName, out var valueType))
                    {
                        continue;
                    }

                    if (!TryBuildFilterCondition(filter, columnName, valueType, targetCmd, ref paramIndex, out var condition))
                    {
                        continue;
                    }

                    conditions.Add(condition);
                }
            }

            return conditions;
        }

        private static bool TryGetColumnInfo(string field, out string columnName, out FilterValueType valueType)
        {
            columnName = string.Empty;
            valueType = FilterValueType.Numeric;

            switch (field)
            {
                case "GameId":
                    columnName = "pd.gameid";
                    valueType = FilterValueType.Numeric;
                    return true;
                case "Status":
                    columnName = "pd.status";
                    valueType = FilterValueType.Numeric;
                    return true;
                case "DownloadCount":
                    columnName = "pd.downloadcount";
                    valueType = FilterValueType.Numeric;
                    return true;
                case "IsFeatured":
                    columnName = "pd.isfeatured";
                    valueType = FilterValueType.Boolean;
                    return true;
                case "PrimaryCategoryId":
                    columnName = "pd.primarycategoryid";
                    valueType = FilterValueType.Numeric;
                    return true;
                case "ClassId":
                    columnName = "pd.classid";
                    valueType = FilterValueType.Numeric;
                    return true;
                case "AllowModDistribution":
                    columnName = "pd.allowmoddistribution";
                    valueType = FilterValueType.Boolean;
                    return true;
                case "GamePopularityRank":
                    columnName = "pd.gamepopularityrank";
                    valueType = FilterValueType.Numeric;
                    return true;
                case "IsAvailable":
                    columnName = "pd.isavailable";
                    valueType = FilterValueType.Boolean;
                    return true;
                case "ThumbsUpCount":
                    columnName = "pd.thumbsupcount";
                    valueType = FilterValueType.Numeric;
                    return true;
                case "Rating":
                    columnName = "pd.rating";
                    valueType = FilterValueType.Numeric;
                    return true;
                case "DateCreated":
                    columnName = "pd.datecreated";
                    valueType = FilterValueType.DateTime;
                    return true;
                case "DateModified":
                    columnName = "pd.datemodified";
                    valueType = FilterValueType.DateTime;
                    return true;
                case "DateReleased":
                    columnName = "pd.datereleased";
                    valueType = FilterValueType.DateTime;
                    return true;
                case "LatestUpdate":
                    columnName = "pd.latestupdate";
                    valueType = FilterValueType.DateTime;
                    return true;
                case "Name":
                    columnName = "pd.name";
                    valueType = FilterValueType.String;
                    return true;
                case "Slug":
                    columnName = "pd.slug";
                    valueType = FilterValueType.String;
                    return true;
                case "Summary":
                    columnName = "pd.summary";
                    valueType = FilterValueType.String;
                    return true;
                case "GameName":
                    columnName = "gd.name";
                    valueType = FilterValueType.String;
                    return true;
                default:
                    return false;
            }
        }

        private static bool TryBuildFilterCondition(
            ProjectSearchFilter filter,
            string columnName,
            FilterValueType valueType,
            NpgsqlCommand cmd,
            ref int paramIndex,
            out string condition)
        {
            condition = string.Empty;

            var opToken = filter.Operator!.ToLowerInvariant();
            var isString = valueType == FilterValueType.String;
            var isBoolean = valueType == FilterValueType.Boolean;

            string sqlOperator;
            var useLike = false;

            switch (opToken)
            {
                case "eq":
                    sqlOperator = "=";
                    break;
                case "neq":
                    sqlOperator = "<>";
                    break;
                case "gte" when !isString && !isBoolean:
                    sqlOperator = ">=";
                    break;
                case "lte" when !isString && !isBoolean:
                    sqlOperator = "<=";
                    break;
                case "gt" when !isString && !isBoolean:
                    sqlOperator = ">";
                    break;
                case "lt" when !isString && !isBoolean:
                    sqlOperator = "<";
                    break;
                case "contains" when isString:
                    sqlOperator = "ILIKE";
                    useLike = true;
                    break;
                default:
                    return false;
            }

            var paramName = $"p{paramIndex++}";

            object typedValue;
            try
            {
                typedValue = valueType switch
                {
                    FilterValueType.Numeric => ParseNumeric(filter.Value!, columnName),
                    FilterValueType.Boolean => ParseBoolean(filter.Value!),
                    FilterValueType.DateTime => ParseDateTime(filter.Value!),
                    FilterValueType.String => ParseString(filter.Value!, useLike),
                    _ => throw new ArgumentOutOfRangeException(nameof(valueType), valueType, null)
                };
            }
            catch
            {
                return false;
            }

            cmd.Parameters.AddWithValue(paramName, typedValue);
            condition = $"{columnName} {sqlOperator} @{paramName}";
            return true;
        }

        private static bool TryBuildAuthorCondition(
            ProjectSearchFilter filter,
            NpgsqlCommand cmd,
            ref int paramIndex,
            out string condition)
        {
            condition = string.Empty;

            var opToken = filter.Operator!.ToLowerInvariant();
            var escaped = Regex.Escape(filter.Value!.Trim());

            string pattern;
            switch (opToken)
            {
                case "eq":
                    pattern = $"^{escaped}$";
                    break;
                case "contains":
                    pattern = escaped;
                    break;
                default:
                    return false;
            }

            // like_regex's pattern has to be a literal inside the jsonpath expression itself -
            // Postgres doesn't support passing it as a jsonpath $variable - so the untrusted value
            // is regex-escaped above (closes the ReDoS/pattern-injection surface) and then
            // JSON-string-escaped here (jsonpath strings follow JSON string syntax) before being
            // spliced into the expression text. The whole expression is still one bound parameter,
            // cast to jsonpath server-side - nothing is concatenated into the outer SQL text.
            var literal = JsonSerializer.Serialize(pattern);
            var paramName = $"p{paramIndex++}";

            cmd.Parameters.AddWithValue(paramName, $"$[*].name like_regex {literal} flag \"i\"");

            // jsonb_path_exists checks whether the path yields ANY item - and a like_regex
            // predicate always yields exactly one boolean item, true or false, so it "exists"
            // either way and matches every row. @@ (jsonb_path_match) is the right operator for
            // a predicate expression like this one: it checks whether that boolean result is true.
            condition = $"(pd.authors @@ @{paramName}::jsonpath)";

            return true;
        }

        private static object ParseNumeric(string value, string columnName)
        {
            if (columnName is "pd.status" or "pd.primarycategoryid" or "pd.classid")
            {
                return int.Parse(value, CultureInfo.InvariantCulture);
            }

            if (columnName is "pd.rating")
            {
                return double.Parse(value, CultureInfo.InvariantCulture);
            }

            return long.Parse(value, CultureInfo.InvariantCulture);
        }

        private static bool ParseBoolean(string value)
        {
            if (!bool.TryParse(value, out var result))
            {
                throw new FormatException("Invalid boolean value.");
            }

            return result;
        }

        private static DateTime ParseDateTime(string value)
        {
            if (!DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var dto))
            {
                throw new FormatException("Invalid date/time value.");
            }

            return dto.UtcDateTime;
        }

        private static string ParseString(string value, bool useLike)
        {
            var trimmed = value.Trim();
            return useLike ? $"%{trimmed}%" : trimmed;
        }

        private enum FilterValueType
        {
            Numeric,
            Boolean,
            DateTime,
            String
        }
    }
}
