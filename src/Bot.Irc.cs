namespace UB3RB0T
{
    using System.Collections.Generic;
    using System.Linq;
    using System.Threading.Tasks;
    using UB3RIRC;

    public partial class Bot
    {
        public async Task CreateIrcBotsAsync()
        {
            ircClients = new Dictionary<string, IrcClient>();

            foreach (IrcServer server in this.Config.Irc.Servers.Where(s => s.Enabled))
            {
                var ircClient = new IrcClient(server.Host, this.Config.Name, server.Host, server.Port, /* useSsl -- pending support */false);
                ircClients.Add(server.Host, ircClient);
                ircClient.OnIrcEvent += this.OnIrcEventAsync;

                await ircClient.ConnectAsync();
            }
        }

        // TODO: IRC library needs... some improvements.
        public async void OnIrcEventAsync(MessageData data, IrcClient client)
        {
            var responses = new List<string>();

            if (data.Verb == ReplyCode.RPL_ENDOFMOTD || data.Verb == ReplyCode.RPL_NOMOTD) //  motd end or motd missing
            {
                foreach (string channel in this.Config.Irc.Servers.Where(s => s.Host == client.Id).First().Channels)
                {
                    client.Command("JOIN", channel, string.Empty);
                }
            }

            if (data.Verb == "PRIVMSG")
            {
                string query = string.Empty;
                string prefix = ".";

                if (data.Text.StartsWith("."))
                {
                    query = data.Text.Substring(prefix.Length);
                }

                responses.AddRange(await this.ProcessMessageAsync(BotMessageData.Create(data, query, client)));

                foreach (string response in responses)
                {
                    client.Command("PRIVMSG", data.Target, response);
                }
            }
        }
    }
}
