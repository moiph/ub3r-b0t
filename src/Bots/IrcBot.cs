
namespace UB3RB0T
{
    using Serilog;
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Text.RegularExpressions;
    using System.Threading.Tasks;
    using UB3RIRC;

    public class IrcBot : Bot
    {
        private readonly Dictionary<string, IrcClient> ircClients = new Dictionary<string, IrcClient>();
        private readonly Dictionary<string, ServerData> serverData = new Dictionary<string, ServerData>(StringComparer.OrdinalIgnoreCase);
        private static readonly Regex namesRegex = new Regex(".*(#[^ ]+) :(.*)", RegexOptions.Compiled | RegexOptions.IgnoreCase);

        public IrcBot(int shard) : base(shard, 1)
        {
        }

        public override BotType BotType => BotType.Irc;

        protected override async Task StartAsyncInternal()
        {
            if (this.Config.Irc.Servers == null)
            {
                throw new InvalidConfigException("Irc server list is missing.");
            }

            if (this.Config.Irc.Servers.Any(s => s.Channels.Any(c => !c.StartsWith("#"))))
            {
                throw new InvalidConfigException("Invalid channel specified; all channels should start with #.");
            }

            foreach (IrcServer server in this.Config.Irc.Servers.Where(s => s.Enabled))
            {
                this.serverData.Add(server.Host, new ServerData());
                var ircClient = new IrcClient(server.Id ?? server.Host, server.Nick ?? this.Config.Name, server.Host, server.Port, server.UseSsl, server.Password, this.Config.Irc.LogType);
                this.ircClients.Add(server.Host, ircClient);

                ircClient.OnIrcEvent += this.OnIrcEventAsync;
                ircClient.OnLogEvent += this.OnLogEventAsync;

                await ircClient.ConnectAsync();
            }
        }

        /// <inheritdoc />
        protected override Task StopAsyncInternal(bool unexpected)
        {
            foreach (var client in this.ircClients.Values)
            {
                client.Disconnect("Shutting down.");
                client.OnIrcEvent -= this.OnIrcEventAsync;
            }

            this.ircClients.Clear();

            return Task.CompletedTask;
        }

        // TODO: IRC library needs... some improvements.
        public async void OnIrcEventAsync(MessageData data, IrcClient client)
        {
            var responses = new List<string>();
            var settings = new Settings
            {
                FunResponsesEnabled = true,
                AutoTitlesEnabled = true,
            };

            if (data.Verb == ReplyCode.RPL_ENDOFMOTD || data.Verb == ReplyCode.RPL_NOMOTD) //  motd end or motd missing
            {
                var server = this.Config.Irc.Servers.FirstOrDefault(s => s.Id == client.Id || s.Host == client.Id);
                if (server == null)
                {
                    Log.Error($"Received message on {client.Id} but no matching configuration found");
                    return;
                }

                foreach (string channel in server.Channels)
                {
                    serverData[client.Host].Channels[channel.ToLowerInvariant()] = new ChannelData();
                    client.Command("JOIN", channel, string.Empty);
                }
            }

            if (data.Verb == "PRIVMSG")
            {
                string query = string.Empty;

                var props = new Dictionary<string, string> {
                    { "server", client.Host.ToLowerInvariant() },
                    { "channel", data.Target.ToLowerInvariant() },
                };

                this.TrackEvent("messageReceived", props);

                // TODO: put this in config
                // twitch parses "." prefix as internal commands; so we have to remap it :(
                if (client.Host == "irc.chat.twitch.tv")
                {
                    settings.Prefix = "^";
                }

                var messageData = BotMessageData.Create(data, client, settings);

                await this.PreProcessMessage(messageData, settings);

                responses.AddRange((await this.ProcessMessageAsync(messageData, settings)).Responses);

                foreach (string response in responses)
                {
                    client.Command("PRIVMSG", data.Target, response);
                    this.TrackEvent("messageSent", props);
                }
            }
            // TODO: This stuff should be handled by the IRC library...
            else if (data.Verb == "353")
            {
                var namesMatch = namesRegex.Match(data.Text);
                var channel = namesMatch.Groups[1].ToString().ToLowerInvariant();
                var userList = namesMatch.Groups[2].ToString();
                var users = userList.Split(new[] { ' ' });

                if (!serverData[client.Host].Channels.ContainsKey(channel))
                {
                    this.serverData[client.Host].Channels[channel] = new ChannelData();
                }

                this.serverData[client.Host].Channels[channel].Users = new HashSet<string>(users);
            }
        }

        public void OnLogEventAsync(LogData logData)
        {
            switch (logData.LogType)
            {
                case LogType.Fatal:
                    Log.Fatal(logData.Exception, logData.Message);
                    break;
                case LogType.Error:
                    Log.Error(logData.Exception, logData.Message);
                    break;
                case LogType.Warn:
                    Log.Warning(logData.Exception, logData.Message);
                    break;
                case LogType.Info:
                    Log.Information(logData.Exception, logData.Message);
                    break;
                case LogType.Debug:
                    Log.Debug(logData.Exception, logData.Message);
                    break;
                case LogType.Incoming:
                    Log.Verbose($"{{Incoming}} {logData.Message}", "<<<");
                    break;
                case LogType.Outgoing:
                    if (this.Config.LogOutgoing)
                    {
                        Log.Verbose($"{{Outgoing}} {logData.Message}", ">>>");
                    }
                    break;
            }
        }

        // TODO: Just a quick helper until the irc library side cleans up its act
        public int GetIrcUserCount()
        {
            var count = 0;
            foreach (var server in this.serverData.Values)
            {
                count += server.Channels.Sum(c => c.Value.Users.Count);
            }

            return count;
        }

        /// <inheritdoc />
        protected override Task<bool> SendNotification(NotificationData notification)
        {
            if (this.ircClients.TryGetValue(notification.Server, out var ircClient))
            {
                ircClient.Command("PRIVMSG", notification.Channel, notification.Text);
                var props = new Dictionary<string, string> {
                    { "server", ircClient.Host.ToLowerInvariant() },
                    { "channel", notification.Channel.ToLowerInvariant() },
                };

                this.TrackEvent("notificationSent", props);
                return Task.FromResult(true);
            }

            return Task.FromResult(false);
        }

        /// <inheritdoc />
        protected override HeartbeatData GetHeartbeatData()
        {
            var heartbeatData = new HeartbeatData
            {
                ServerCount = this.ircClients.Count,
                UserCount = this.GetIrcUserCount(),
                ChannelCount = this.serverData.Values.Sum(i => i.Channels.Count),
            };

            return heartbeatData;
        }

        protected override Task RespondAsync(BotMessageData messageData, string text)
        {
            this.ircClients[messageData.Server]?.Command("PRIVMSG", messageData.Channel, text);

            var props = new Dictionary<string, string> {
                    { "server", messageData.Server.ToLowerInvariant() },
                    { "channel", messageData.Channel.ToLowerInvariant() },
            };
            this.TrackEvent("messageSent", props);

            return Task.CompletedTask;
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
