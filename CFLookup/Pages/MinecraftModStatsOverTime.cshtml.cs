using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using System.Data;
using System.Text.Json;

namespace CFLookup.Pages
{
    public class MinecraftModStatsOverTimeModel : PageModel
    {
        private readonly MSSQLDB _db;

        public Dictionary<DateTimeOffset, Dictionary<string, Dictionary<string, long>>> Stats { get; set; } = new Dictionary<DateTimeOffset, Dictionary<string, Dictionary<string, long>>>();

        public MinecraftModStatsOverTimeModel(MSSQLDB db)
        {
            _db = db;
        }

        public async Task OnGetAsync()
        {
            Stats = await SharedMethods.GetMinecraftStatsOverTime(_db);
        }
    }
}
