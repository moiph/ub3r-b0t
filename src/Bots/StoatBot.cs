using Serilog;
using StoatSharp;
using System;
using System.Threading.Tasks;

namespace UB3RB0T
{
    public class StoatBot : Bot
    {
        private readonly StoatClient client;
        private readonly MessageCache<string> botResponsesCache = new();

        public StoatBot(int shard, int totalShards) : base(shard, totalShards)
        {
            this.client = new StoatClient(ClientMode.WebSocket, new ClientConfig
            {
                Owners = [this.Config.Stoat.OwnerId],
                OwnerBypassPermissions = true,
                LogMode = StoatLogSeverity.Debug,
            });

            this.client.OnMessageRecieved += async message => await this.OnMessageReceived(message);
            this.client.OnMessageUpdated += async (message_cache, message) => await this.OnMessageUpdated(message_cache, message);
            this.client.OnMessageDeleted += async (channel, message_id) => await this.OnMesssageDeleted(channel, message_id);
            this.client.OnLog += this.OnLog;
            this.client.OnWebSocketError += (ex) =>
            {
                // TODO: reconnection handling
                Log.Error($"Websocket error: {ex}");
            };
        }

        public override BotType BotType => BotType.Stoat;

        protected override string UserId => this.Config.Stoat.BotId;

        protected override Task<HeartbeatData> GetHeartbeatData()
        {
            var heartbeatData = new HeartbeatData
            {
                ServerCount = this.client.Servers.Count,
                UserCount = this.client.Users.Count,
                ChannelCount = this.client.Channels.Count,
            };

            return Task.FromResult(heartbeatData);
        }

        protected override Task RespondAsync(BotMessageData messageData, string text)
        {
            return this.RespondAsync(messageData.StoatMessageData, text);
        }

        private async Task OnMessageReceived(Message message)
        {
            try
            {
                if (message.AuthorId == this.client.CurrentUser.Id)
                {
                    return;
                }

                if (this.Throttler.IsThrottled(message.AuthorId, ThrottleType.User))
                {
                    Log.Debug($"messaging throttle from user: {message.Author} on chan {message.ChannelId} server {message.ServerId}");
                    return;
                }

                if (this.Throttler.IsThrottled(message.ServerId, ThrottleType.Guild))
                {
                    Log.Debug($"messaging throttle from guild: {message.Author} on chan {message.ChannelId} server {message.ServerId}");
                    return;
                }

                Log.Debug($"Received message from {message.Author} in channel {message.ChannelId} on server {message.ServerId}");

                // Temporary settings until admin panel stuff is wired up
                var settings = new Settings
                {
                    FunResponsesEnabled = true,
                    AutoTitlesEnabled = true,
                    PreferEmbeds = true,
                };

                var messageData = BotMessageData.Create(message, settings);

                await this.PreProcessMessage(messageData, settings);

                BotResponseData responseData = await this.ProcessMessageAsync(messageData, settings);

                if (responseData.Embed != null)
                {
                    await this.RespondAsync(message, string.Empty, bypassEdit: true, responseData.Embed.CreateStoatEmbed());
                }
                else
                {
                    foreach (string response in responseData.Responses)
                    {
                        await this.RespondAsync(message, response, bypassEdit: responseData.Responses.Count > 1);
                    }
                }
            }
            catch (Exception ex)
            {
                Log.Error(ex, $"Error in OnMessageReceived");
            }
        }

        private async Task OnMessageUpdated(Downloadable<string, Message> message_cache, MessageUpdatedProperties message)
        {
            try
            {
                if (DateTimeOffset.UtcNow.Subtract(message.CreatedAt) < TimeSpan.FromHours(1) && !string.IsNullOrEmpty(message.Content.Value))
                {
                    var msg = await message_cache.GetOrDownloadAsync();
                    await this.OnMessageReceived(msg);
                }
            }
            catch (Exception ex)
            {
                Log.Warning(ex, $"Error in HandleMessageUpdated");
            }
        }

        private async Task OnMesssageDeleted(Channel channel, string message_id)
        {
            try
            {
                var botMessageId = this.botResponsesCache.Remove(message_id);
                if (!string.IsNullOrEmpty(botMessageId))
                {
                    await channel.DeleteMessageAsync(botMessageId);
                }
            }
            catch (Exception ex)
            {
                Log.Warning(ex, $"Error in HandleMessageDeleted");
            }
        }

        private async Task RespondAsync(Message message, string text, bool bypassEdit = false, Embed embed = null)
        {
            this.TrackEvent("messageSent");

            if (!bypassEdit && this.botResponsesCache.Get(message.Id) is string oldMessgeId)
            {
                // Stoat is missing an update/edit capability, so for now just do a delete and send a new message
                await this.client.GetChannel(message.ChannelId).DeleteMessageAsync(oldMessgeId);
            }
            
            Message sentMessage;
            Embed[] embeds = null;
            if (embed != null)
            {
                embeds = [embed];
            }

            var reply = new MessageReply(message.Id, false);
            Log.Debug($"Sending message in response to {message.Id} on channel {message.ChannelId} on server {message.ServerId}");
            sentMessage = await message.Channel.SendMessageAsync(text, embeds: embeds, replies: [reply]);
            
            this.botResponsesCache.Add(message.Id, sentMessage.Id);
        }

        protected override async Task<bool> SendNotification(NotificationData notification)
        {
            if (notification.Text.Contains("%%username%%"))
            {
                var member = this.client.GetUser(notification.UserId);
                if (member != null)
                {
                    notification.Text = notification.Text.Replace("%%username%%", member.Mention);
                }
            }

            Message originalMessage = null;
            if (!string.IsNullOrEmpty(notification.MessageId))
            {
                try
                {
                    originalMessage = await this.client.GetChannel(notification.Channel).GetMessageAsync(notification.MessageId);
                }
                catch (Exception ex)
                {
                    Log.Error(ex, "Failed to fetch notification base message");
                }
            }

            if (originalMessage != null)
            {
                var reply = new MessageReply(originalMessage.Id, false);
                await originalMessage.Channel.SendMessageAsync(notification.Text, replies: [reply]);
            }
            else
            {
                await this.client.GetChannel(notification.Channel).SendMessageAsync(notification.Text);
            }

            return true;
        }

        protected override async Task StartAsyncInternal()
        {
            await this.client.LoginAsync(this.Config.Stoat.Token, AccountType.Bot);
            await this.client.StartAsync();
        }

        protected override async Task StopAsyncInternal(bool unexpected)
        {
            await this.client.StopAsync();
        }

        private void OnLog(string message, StoatLogSeverity severity)
        {
            switch (severity)
            {
                case StoatLogSeverity.Error:
                    Log.Error(message);
                    break;
                case StoatLogSeverity.Warn:
                    Log.Warning(message);
                    break;
                case StoatLogSeverity.Info:
                    Log.Information(message);
                    break;
                case StoatLogSeverity.Debug:
                    Log.Debug(message);
                    break;
            }
        }
    }
}
