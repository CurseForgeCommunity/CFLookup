
using System.Data;
using System.Globalization;
using System.Text;
using CFLookup.Models;
using CurseForge.APIClient.Models.Mods;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Npgsql;

namespace CFLookup.Pages
{
    public class ProjectSearchModel : PageModel
    {
        private readonly NpgsqlConnection _conn;
        private readonly ILogger<ProjectSearchModel> _logger;

        [BindProperty(SupportsGet = true)]
        public ProjectSearchCriteria Criteria { get; set; } = new();

        public ProjectSearchPageViewModel ViewModel { get; set; } = new();

        public ProjectSearchModel(NpgsqlConnection conn, ILogger<ProjectSearchModel> logger)
        {
            _conn = conn;
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
            var sqlBuilder = new StringBuilder();
            sqlBuilder.Append(
                """
                    SELECT pd.projectid, pd.gameid, pd.name, pd.slug, pd.summary, pd.status, pd.downloadcount, pd.isfeatured,
                         pd.primarycategoryid, pd.classid, pd.allowmoddistribution, pd.gamepopularityrank,
                         pd.isavailable, pd.thumbsupcount, pd.datecreated, pd.datemodified, pd.datereleased, pd.latestupdate,
                         gd.name as gamename
                    FROM project_data pd
                    LEFT JOIN game_data gd ON pd.gameid = gd.gameid
                """);

            var conditions = new List<string>();

            await using var cmd = _conn.CreateCommand();
            cmd.CommandType = CommandType.Text;

            if (!string.IsNullOrWhiteSpace(Criteria.Query))
            {
                var paramName = "q";
                conditions.Add("(pd.name ILIKE @" + paramName + " OR pd.slug ILIKE @" + paramName + " OR pd.summary ILIKE @" + paramName + ")");
                cmd.Parameters.AddWithValue(paramName, $"%{Criteria.Query.Trim()}%");
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

                    if (!TryGetColumnInfo(filter.Field, out var columnName, out var valueType))
                    {
                        continue;
                    }

                    if (!TryBuildFilterCondition(filter, columnName, valueType, cmd, ref paramIndex, out var condition))
                    {
                        continue;
                    }

                    conditions.Add(condition);
                }
            }

            if (conditions.Count > 0)
            {
                sqlBuilder.Append(" WHERE ");
                sqlBuilder.Append(string.Join(" AND ", conditions));
            }

            sqlBuilder.Append(" ORDER BY pd.name DESC");
            sqlBuilder.Append(" LIMIT 100");

            cmd.CommandText = sqlBuilder.ToString();

            if (_conn.State != ConnectionState.Open)
            {
                await _conn.OpenAsync();
            }

            var results = new List<ProjectSearchResult>();

            ViewModel = new ProjectSearchPageViewModel
            {
                Criteria = Criteria
            };

            try
            {
                await using var reader = await cmd.ExecuteReaderAsync();

                while (await reader.ReadAsync())
                {
                    var result = new ProjectSearchResult
                    {
                        ProjectId = reader.GetInt64(reader.GetOrdinal("projectid")),
                        GameId = reader.GetInt32(reader.GetOrdinal("gameid")),
                        GameName = reader.GetString(reader.GetOrdinal("gamename")),
                        Name = reader.GetString(reader.GetOrdinal("name")),
                        Slug = reader.GetString(reader.GetOrdinal("slug")),
                        Summary = reader.GetString(reader.GetOrdinal("summary")),
                        Status = (ModStatus)reader.GetInt32(reader.GetOrdinal("status")),
                        DownloadCount = reader.GetInt64(reader.GetOrdinal("downloadcount")),
                        IsFeatured = reader.GetBoolean(reader.GetOrdinal("isfeatured")),
                        PrimaryCategoryId = reader.GetInt32(reader.GetOrdinal("primarycategoryid")),
                        ClassId = reader.GetInt32(reader.GetOrdinal("classid")),
                        AllowModDistribution = reader.GetBoolean(reader.GetOrdinal("allowmoddistribution")),
                        GamePopularityRank = reader.GetInt64(reader.GetOrdinal("gamepopularityrank")),
                        IsAvailable = reader.GetBoolean(reader.GetOrdinal("isavailable")),
                        ThumbsUpCount = reader.GetInt64(reader.GetOrdinal("thumbsupcount")),
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

            ViewModel.Results = results;
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

        private static object ParseNumeric(string value, string columnName)
        {
            if (columnName is "pd.status" or "pd.primarycategoryid" or "pd.classid")
            {
                return int.Parse(value, CultureInfo.InvariantCulture);
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
