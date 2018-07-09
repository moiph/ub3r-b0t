namespace UB3RB0T.Commands
{
    using System;
    using System.Net;
    using System.Threading.Tasks;
    using Discord;
    using Serilog;

    [UserPermissions(GuildPermission.ManageGuild, "You must have manage server permissions to use that command. nice try, dungheap")]
    public class AdminCommand : IDiscordCommand
    {
        public async Task<CommandResponse> Process(IDiscordBotContext context)
        {
            if (SettingsConfig.Instance.CreateEndpoint != null && context.GuildChannel != null)
            {
                var guildName = WebUtility.UrlEncode(context.GuildChannel.Guild.Name);
                var req = WebRequest.Create($"{SettingsConfig.Instance.CreateEndpoint}?id={context.GuildChannel.Guild.Id}&name={guildName}");
                try
                {
                    await req.GetResponseAsync();
                    return new CommandResponse { Text = $"Manage from {SettingsConfig.Instance.ManagementEndpoint}" };
                }
                catch (Exception ex)
                {
                    Log.Error(ex, "Failure in admin command");
                }
            }

            return null;
        }
    }
}
