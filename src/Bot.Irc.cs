namespace UB3RB0T
{
    using System;
    using System.Collections.Concurrent;
    using System.Collections.Generic;
    using System.Linq;
    using System.Text.RegularExpressions;
    using System.Threading.Tasks;
    using UB3RIRC;

    public partial class Bot
    {
        private Dictionary<string, ServerData> serverData = new Dictionary<string, ServerData>(StringComparer.OrdinalIgnoreCase);
        private Regex namesRegex = new Regex(".*(#[^ ]+) :(.*)", RegexOptions.Compiled | RegexOptions.IgnoreCase);

        public async Task CreateIrcBotsAsync()
        {
            ircClients = new Dictionary<string, IrcClient>();

            foreach (IrcServer server in this.Config.Irc.Servers.Where(s => s.Enabled))
            {
                serverData.Add(server.Host, new ServerData());
                var ircClient = new IrcClient(server.Host, server.Nick ?? this.Config.Name, server.Host, server.Port, /* useSsl -- pending support */false, server.Password);
                ircClients.Add(server.Host, ircClient);
                ircClient.OnIrcEvent += this.OnIrcEventAsync;

                await ircClient.ConnectAsync();
            }
        }

        // TODO: IRC library needs... some improvements.
        public async void OnIrcEventAsync(MessageData data, IrcClient client)
        {
            var responses = new List<string>();
            var settings = new Settings();

            if (data.Verb == ReplyCode.RPL_ENDOFMOTD || data.Verb == ReplyCode.RPL_NOMOTD) //  motd end or motd missing
            {
                foreach (string channel in this.Config.Irc.Servers.Where(s => s.Host == client.Id).First().Channels)
                {
                    serverData[client.Host].Channels[channel.ToLowerInvariant()] = new ChannelData();
                    client.Command("JOIN", channel, string.Empty);
                }
            }

            if (data.Verb == "PRIVMSG")
            {
                string query = string.Empty;

                // TODO: put this in config
                // twitch parses "." prefix as internal commands; so we have to remap it :(
                if (client.Host == "irc.chat.twitch.tv")
                {
                    settings.Prefix = "^";
                }

                // Update the seen data
                if (!string.IsNullOrEmpty(data.Text) && this.Config.SeenEndpoint != null)
                {
                    var messageText = data.Text;
                    if (messageText.Length > 256)
                    {
                        messageText = data.Text.Substring(0, 253) + "...";
                    }

                    seenUsers["" + data.Target + data.Nick] = new SeenUserData
                    {
                        Name = data.Nick,
                        Channel = data.Target,
                        Server = client.Host,
                        Text = messageText,
                        Timestamp = Utilities.Utime,
                    };
                }

                var httpMatch = httpRegex.Match(data.Text);
                if (httpMatch.Success)
                {
                    this.urls[data.Target] = httpMatch.Value;
                }

                if (data.Text.StartsWith(settings.Prefix))
                {
                    query = data.Text.Substring(settings.Prefix.Length);
                }
                
                responses.AddRange((await this.ProcessMessageAsync(BotMessageData.Create(data, query, client), settings)).Responses);

                foreach (string response in responses)
                {
                    client.Command("PRIVMSG", data.Target, response);
                }
            }
            // TODO: This stuff should be handled by the IRC library...
            else if (data.Verb == "353")
            {
                var namesMatch = namesRegex.Match(data.Text);
                var channel = namesMatch.Groups[1].ToString().ToLowerInvariant();
                var userList = namesMatch.Groups[2].ToString();
                var users = userList.Split(new[] { ' ' });

                serverData[client.Host].Channels[channel].Users = new HashSet<string>(users);
            }
        }

        // TODO: Just a quick helper until the irc library side cleans up its act
        public int GetIrcUserCount()
        {
            var count = 0;
            foreach (var server in serverData.Values)
            {
                count += server.Channels.Sum(c => c.Value.Users.Count);
            }

            return count;
        }

        private class ServerData
        {
            public Dictionary<string, ChannelData> Channels { get; set; } = new Dictionary<string, ChannelData>();
        }

        private class ChannelData
        {
            public HashSet<string> Users { get; set; } = new HashSet<string>();
        }
    }
}
