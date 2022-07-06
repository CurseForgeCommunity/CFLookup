using CurseForge.APIClient;
using CurseForge.APIClient.Models.Mods;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using StackExchange.Redis;
using System.Collections.Concurrent;

namespace WhatCurseForgeProjectIsThis.Pages
{
    public class MinecraftModStatsModel : PageModel
    {
        private readonly ApiClient _cfApiClient;
        private readonly IDatabaseAsync _redis;

        public ConcurrentDictionary<string, ConcurrentDictionary<ModLoaderType, uint>> MinecraftStats = new ConcurrentDictionary<string, ConcurrentDictionary<ModLoaderType, uint>>();
        public MinecraftModStatsModel(ApiClient cfApiClient, ConnectionMultiplexer connectionMultiplexer)
        {
            _cfApiClient = cfApiClient;
            _redis = connectionMultiplexer.GetDatabase(5);
        }

        public async Task<IActionResult> OnGetAsync()
        {
            MinecraftStats = await SharedMethods.GetMinecraftModStatistics(_redis, _cfApiClient);

            return Page();
        }
    }
}
