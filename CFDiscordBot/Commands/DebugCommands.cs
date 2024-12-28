using Discord;
using Discord.Interactions;

namespace CFDiscordBot.Commands
{
    [DefaultMemberPermissions(GuildPermission.Administrator)]
    public class DebugCommands : InteractionModuleBase<ShardedInteractionContext>
    {
        [SlashCommand("ping", "Returns the latency of the bot")]
        public async Task PingAsync()
        {
            await RespondAsync($"Pong! {Context.Client.Latency}ms", ephemeral: true);
            await Task.Delay(2000);

            await Context.Interaction.DeleteOriginalResponseAsync();
        }

        //        [SlashCommand("shardinfo", "Returns information about the shard")]
        //        public async Task ShardInfoAsync()
        //        {
        //            var shardId = Context.Client.GetShardIdFor(Context.Guild);
        //            var shard = Context.Client.GetShard(shardId);

        //            var embed = new EmbedBuilder()
        //                .WithTitle("Shard Information")
        //                .WithDescription($"""
        //Shard ID: {shardId}
        //Guilds: {shard.Guilds.Count}
        //Users: {shard.Guilds.Sum(x => x.MemberCount)}
        //Channels: {shard.Guilds.Sum(x => x.Channels.Count)}
        //Latency: {shard.Latency}ms
        //""")
        //                .WithColor(Color.Blue)
        //                .Build();

        //            await RespondAsync(embeds: new[] { embed }, ephemeral: true);
        //        }
    }
}
