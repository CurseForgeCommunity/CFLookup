using System.Data;
using CFLookup.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Npgsql;

namespace CFLookup.Pages
{
    public class ProjectHistoryModel : PageModel
    {
        private readonly NpgsqlDataSource _db;

        public long ProjectId { get; set; }

        public string? ProjectName { get; set; }

        public string? GameName { get; set; }

        public List<ProjectHistoryEntry> History { get; set; } = new();

        public ProjectHistoryModel(NpgsqlDataSource db)
        {
            _db = db;
        }

        public async Task<IActionResult> OnGetAsync(long projectId)
        {
            ProjectId = projectId;

            await using var conn = await _db.OpenConnectionAsync();

            await using (var cmd = conn.CreateCommand())
            {
                cmd.CommandType = CommandType.Text;
                cmd.CommandText = """
                    SELECT pd.name, gd.name AS gamename
                    FROM project_data pd
                    LEFT JOIN game_data gd ON pd.gameid = gd.gameid
                    WHERE pd.projectid = @projectId
                    """;
                cmd.Parameters.AddWithValue("projectId", projectId);

                await using var reader = await cmd.ExecuteReaderAsync();
                if (await reader.ReadAsync())
                {
                    ProjectName = reader.GetString(reader.GetOrdinal("name"));

                    var gameNameOrdinal = reader.GetOrdinal("gamename");
                    GameName = reader.IsDBNull(gameNameOrdinal) ? null : reader.GetString(gameNameOrdinal);
                }
            }

            History = await SharedMethods.GetProjectHistory(conn, projectId, 200);

            return Page();
        }
    }
}
