using CurseForge.APIClient;
using Discord.Interactions;

namespace CFDiscordBot.Commands
{
    [Group("lookup", "Does lookups against CurseForge")]
    public partial class Lookup(ApiClient apiClient) : InteractionModuleBase<ShardedInteractionContext> { }
}
