using Microsoft.AspNetCore.Mvc.RazorPages;
using StackExchange.Redis;

namespace CFLookup.Pages
{
    public class MinecraftModStatsOverTimeModel : PageModel
    {
        private readonly ConnectionMultiplexer _db;

        public string ChartHtml { get; set; }

        public MinecraftModStatsOverTimeModel(ConnectionMultiplexer db)
        {
            _db = db;
        }

        public async Task OnGetAsync(CancellationToken cancellationToken)
        {
            var rdb = _db.GetDatabase(5);

            var statHtml = await rdb.StringGetAsync("cf-mcmodloader-stats");

            if(statHtml == RedisValue.Null)
            {
                ChartHtml = "No data loaded yet";
                return;
            }
        }
    }
}
