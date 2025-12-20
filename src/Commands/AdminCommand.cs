namespace UB3RB0T.Commands
{
    using System;
    using System.Net;
    using System.Net.Http;
    using System.Threading.Tasks;
    using Discord;
    using Serilog;

    [UserPermissions(GuildPermission.ManageGuild, "RequireUserManageGuild")]
    public class AdminCommand : IDiscordCommand
    {
        private static HttpClient httpClient = new HttpClient();

        public async Task<CommandResponse> Process(IDiscordBotContext context)
        {
            if (SettingsConfig.Instance.CreateEndpoint != null && context.GuildChannel != null)
            {
                var guildName = WebUtility.UrlEncode(context.GuildChannel.Guild.Name);
                try
                {
                    var resp = await httpClient.GetAsync($"{SettingsConfig.Instance.CreateEndpoint}?id={context.GuildChannel.Guild.Id}&name={guildName}");
                    if (resp.StatusCode == HttpStatusCode.OK)
                    {
                        return new CommandResponse { Text = $"Manage from {SettingsConfig.Instance.ManagementEndpoint}" };
                    }
                    else
                    {
                        var respContent = await resp.Content.ReadAsStringAsync();
                        Log.Error("Unexpected response in admin command: " + respContent);
                    }
                }
                catch (Exception ex)
                {
                    Log.Error(ex, "Failure in admin command");
                }

                return new CommandResponse { Text = "Settings creation failed, report this to the support server" };
            }

            return null;
        }
    }
}
