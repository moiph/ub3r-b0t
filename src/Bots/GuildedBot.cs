using System;
using System.Reactive.Linq;
using System.Threading.Tasks;
using Guilded;
using Guilded.Base;
using Guilded.Base.Content;
using Guilded.Base.Embeds;
using Guilded.Base.Events;
using Serilog;

namespace UB3RB0T
{
    class GuildedBot : Bot
    {
        public GuildedBotClient client { get; }
        private readonly MessageCache<Guid> botResponsesCache = new MessageCache<Guid>();

        public override BotType BotType => BotType.Guilded;

        public GuildedBot(int shard, int totalShards): base (shard, totalShards)
        {
            this.client = new GuildedBotClient(this.Config.Guilded.Token);

            client.MessageCreated.Subscribe(async m => await this.HandleMessage(m));
            client.MessageUpdated.Subscribe(async m => await this.HandleMessageUpdated(m));
            client.MessageDeleted.Subscribe(async m => await this.HandleMessageDeleted(m));
        }

        public async Task HandleMessage(MessageEvent messageEvent)
        {
            try
            {
                if (messageEvent.IsSystemMessage || messageEvent.CreatedByWebhook != null || messageEvent.CreatedBy == this.client.Me.Id || messageEvent.IsReply)
                {
                    return;
                }

                if (this.Throttler.IsThrottled(messageEvent.CreatedBy.ToString(), ThrottleType.User))
                {
                    Log.Debug($"messaging throttle from user: {messageEvent.CreatedBy} on chan {messageEvent.ChannelId} server {messageEvent.ServerId}");
                    return;
                }

                if (this.Throttler.IsThrottled(messageEvent.ServerId.ToString(), ThrottleType.Guild))
                {
                    Log.Debug($"messaging throttle from guild: {messageEvent.CreatedBy} on chan {messageEvent.ChannelId} server {messageEvent.ServerId}");
                    return;
                }

                var settings = new Settings
                {
                    FunResponsesEnabled = true,
                    AutoTitlesEnabled = true,
                    PreferEmbeds = true,
                };

                var messageData = BotMessageData.Create(messageEvent.Message, settings);

                await this.PreProcessMessage(messageData, settings);

                BotResponseData responseData = await this.ProcessMessageAsync(messageData, settings);

                if (responseData.Embed != null)
                {
                    await this.RespondAsync(messageEvent.Message, string.Empty, bypassEdit: true, responseData.Embed.CreateGuildedEmbed());
                }
                else
                {
                    foreach (string response in responseData.Responses)
                    {
                        await this.RespondAsync(messageEvent.Message, response, bypassEdit: responseData.Responses.Count > 1);
                    }
                }
            }
            catch (Exception ex)
            {
                Log.Warning(ex, $"Error in HandleMessage");
                this.AppInsights?.TrackException(ex);
            }
        }

        public async Task HandleMessageUpdated(MessageEvent messageEvent)
        {
            try
            {
                if (messageEvent.CreatedBy != this.client.Me.Id &&
                    DateTimeOffset.UtcNow.Subtract(messageEvent.CreatedAt) < TimeSpan.FromHours(1))
                {
                    await this.HandleMessage(messageEvent);
                }
            }
            catch (Exception ex)
            {
                Log.Warning(ex, $"Error in HandleMessageUpdated");
                this.AppInsights?.TrackException(ex);
            }
        }

        public async Task HandleMessageDeleted(MessageDeletedEvent messageEvent)
        {
            try
            {
                var botMessageId = this.botResponsesCache.Remove(messageEvent.Id);
                if (botMessageId != Guid.Empty)
                {
                    await this.client.DeleteMessageAsync(messageEvent.ChannelId, botMessageId);
                }
            }
            catch (Exception ex)
            {
                Log.Warning(ex, $"Error in HandleMessageDeleted");
                this.AppInsights?.TrackException(ex);
            }
        }

        protected override async Task StartAsyncInternal()
        {
            await this.client.ConnectAsync();
        }

        protected override async Task StopAsyncInternal(bool unexpected)
        {
            await this.client.DisconnectAsync();
        }

        protected override HeartbeatData GetHeartbeatData()
        {
            // TODO: valid heartbeat data
            var heartbeatData = new HeartbeatData
            {
                ServerCount = 1,
                UserCount = 1,
                ChannelCount = 1,
            };

            return heartbeatData;
        }

        protected override async Task<bool> SendNotification(NotificationData notification)
        {
            if (notification.Text.Contains("%%username%%"))
            {
                var member = await this.client.GetMemberAsync(new HashId(notification.Server), new HashId(notification.UserId));
                if (member != null)
                {
                    notification.Text = notification.Text.Replace("%%username%%", member.Nickname ?? member.Name);
                }
            }
            
            Message originalMessage = null;
            if (!string.IsNullOrEmpty(notification.MessageId))
            {
                try
                {
                    originalMessage = await this.client.GetMessageAsync(Guid.Parse(notification.Channel), Guid.Parse(notification.MessageId));
                }
                catch (Exception ex)
                {
                    Log.Error(ex, "Failed to fetch notification base message");
                }
            }

            if (originalMessage != null)
            {
                await originalMessage.ReplyAsync(notification.Text);
            }
            else
            {
                await this.client.CreateMessageAsync(Guid.Parse(notification.Channel), notification.Text);
            }

            return true;
        }

        protected override async Task RespondAsync(BotMessageData messageData, string text)
        {
            await this.RespondAsync(messageData.GuildedMessageData, text);
        }

        private async Task RespondAsync(Message message, string text, bool bypassEdit = false, Embed embed = null)
        { 
            this.TrackEvent("messageSent");
            if (text.Contains("%%username%%") && message.ServerId.HasValue)
            {
                var member = await this.client.GetMemberAsync(message.ServerId.Value, message.CreatedBy);
                if (member != null)
                {
                    text = text.Replace("%%username%%", member.Nickname ?? member.Name);
                }
            }

            if (!bypassEdit && this.botResponsesCache.Get(message.Id) is Guid oldMessgeId && oldMessgeId != Guid.Empty)
            {
                var oldMsg = await this.client.GetMessageAsync(message.ChannelId, oldMessgeId);
                await oldMsg.UpdateAsync(text);
            }
            else
            {
                Message sentMessage;
                if (embed != null)
                {
                    sentMessage = await message.ReplyAsync(isPrivate: false, isSilent: false, embed);
                }
                else
                {
                    sentMessage = await message.ReplyAsync(text);
                }

                this.botResponsesCache.Add(message.Id, sentMessage.Id);
            }
        }
    }
}
