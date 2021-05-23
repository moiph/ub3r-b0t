namespace UB3RB0T.Modules
{
    using Serilog;
    using System;
    using System.Linq;
    using System.Threading.Tasks;

    public class AprilFoolsModule : BaseDiscordModule
    {
        private static Random random = new Random();

        public override Task<ModuleResult> ProcessDiscordModule(IDiscordBotContext context)
        {
            if (context.Settings.AprilFoolsEnabled && BotConfig.Instance.AprilFools.Chance > 0 && random.Next(1, 100) <= BotConfig.Instance.AprilFools.Chance && context.GuildChannel != null)
            {
                Task.Run(async () =>
                {
                    await Task.Delay(BotConfig.Instance.AprilFools.Delay);
                    try
                    {
                        await context.GuildChannel.Guild.DownloadUsersAsync();
                        var bots = context.GuildChannel.Guild.Users.Where(u => u.IsBot && !u.IsWebhook && u.Id != BotConfig.Instance.Discord.BotId && !BotConfig.Instance.AprilFools.IgnoreIds.Contains(u.Id));
                        if (bots.Count() > 0)
                        {
                            var botMention = bots.Select(b => b.Mention).ToArray().Random();
                            var botInsult = BotConfig.Instance.AprilFools.Responses.Random().Replace("{bot}", botMention).Replace("{author}", context.Message.Author.UserOrNickname());
                            Log.Verbose($"[AF] [{context.GuildChannel.Guild.Id}]: {botInsult}");
                            await context.Message.Channel.SendMessageAsync(botInsult);
                        }
                    }
                    catch (Exception ex)
                    {
                        Log.Warning(ex, "Failure in AF module send");
                    }
                }).Forget();
            }

            return Task.FromResult(ModuleResult.Continue);
        }
    }
}
