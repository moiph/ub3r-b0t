﻿namespace UB3RB0T.Modules
{
    using System.Linq;
    using System.Threading.Tasks;
    using Discord;

    public class BotlessModule : BaseDiscordModule
    {
        private readonly string BotlessRole = "botless";

        public override Task<ModuleResult> ProcessDiscordModule(IDiscordBotContext context)
        {
            // if the user is blocked based on role, return
            var botlessRoleId = context.GuildChannel?.Guild.Roles?.FirstOrDefault(r => r.Name?.ToLowerInvariant() == BotlessRole)?.Id;
            if (botlessRoleId != null)
            {
                var targetUser = (context.ReactionUser ?? context.Author) as IGuildUser;
                if (targetUser?.RoleIds.Contains(botlessRoleId.Value) ?? false)
                {
                    return Task.FromResult(ModuleResult.Stop);
                }
            }

            return Task.FromResult(ModuleResult.Continue);
        }
    }
}
