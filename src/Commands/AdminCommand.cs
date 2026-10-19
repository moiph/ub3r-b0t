namespace UB3RB0T.Commands
{
    using Serilog;
    using System;
    using System.Net;
    using System.Net.Http;
    using System.Threading.Tasks;

    public interface ICommand
    {
        Task<CommandResponse?> Process(IBotContext context);
    }

    public abstract class AdminCommand : ICommand
    {
        private static readonly HttpClient httpClient = new();

        public abstract BotType botType { get; }

        public async Task<CommandResponse?> Process(IBotContext context)
        {
            var messageData = context.MessageData;
            if (SettingsConfig.Instance.CreateEndpoint != null && !string.IsNullOrEmpty(messageData.Server))
            {
                try
                {
                    var resp = await httpClient.GetAsync($"{SettingsConfig.Instance.CreateEndpoint}?id={messageData.Server}&platform={botType}");
                    if (resp.StatusCode == HttpStatusCode.OK)
                    {
                        return new CommandResponse { Text = $"Manage from <{SettingsConfig.Instance.ManagementEndpoint}>" };
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
