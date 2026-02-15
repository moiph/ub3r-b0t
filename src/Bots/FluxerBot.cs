using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Fluxer.Net;
using Fluxer.Net.Data.Models;
using Fluxer.Net.Gateway.Data;
using Serilog;
using Serilog.Core;

namespace UB3RB0T
{
    public class FluxerBot : Bot
    {
        private readonly GatewayClient client;
        private readonly ApiClient apiClient;
        private readonly MessageCache<ulong> botResponsesCache = new();

        private readonly Timer heartbeatTimer;
        private DateTime lastHeartbeatAck = DateTime.MinValue;

        public FluxerBot(int shard, int totalShards) : base(shard, totalShards)
        {
            var fluxerConfig = new FluxerConfig
            {
                Serilog = Log.Logger as Logger,
            };

            this.apiClient = new ApiClient(this.Config.Fluxer.Token, fluxerConfig);
            this.client = new GatewayClient(this.Config.Fluxer.Token, fluxerConfig);

            this.client.MessageCreate += async data => await this.HandleMessageCreated(data);
            this.client.MessageUpdate += async data => await this.HandleMessageUpdated(data);
            this.client.MessageDelete += async data => await this.HandleMessageDeleted(data);
            this.client.HeartbeatAck += this.HeartbeatAck;

            this.heartbeatTimer = new Timer(HeartBeatTimerAsync, null, 60000, 60000 * 5);
        }

        public override BotType BotType => BotType.Fluxer;
        protected override string UserId => this.Config.Fluxer.BotId;

        protected override async Task<HeartbeatData> GetHeartbeatData()
        {
            List<GuildProperties> guilds = null;
            try
            {
                guilds = await this.apiClient.GetCurrentUserGuilds();
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "Failed to fetch guilds for heartbeat data");
            }

            var heartbeatData = new HeartbeatData
            {
                ServerCount = guilds?.Count ?? 0,
                UserCount = guilds?.Sum(g => g.MemberCount) ?? 0,
                ChannelCount = 0,
            };

            return heartbeatData;
        }

        protected override Task RespondAsync(BotMessageData messageData, string text)
        {
            return this.RespondAsync(messageData.FluxerMessageData, text);
        }

        private async Task HandleMessageCreated(MessageGatewayData data)
        {
            try
            {
                if (data.Author.Id == ulong.Parse(this.Config.Fluxer.BotId))
                {
                    return;
                }

                if (this.Throttler.IsThrottled(data.Author.Id.ToString(), ThrottleType.User))
                {
                    Log.Debug($"messaging throttle from user: {data.Author} on chan {data.ChannelId} server {data.GuildId}");
                    return;
                }

                if (this.Throttler.IsThrottled(data.GuildId.ToString(), ThrottleType.Guild))
                {
                    Log.Debug($"messaging throttle from guild: {data.Author} on chan {data.ChannelId} server {data.GuildId}");
                    return;
                }

                Log.Debug($"Received message from {data.Author} in channel {data.ChannelId} on server {data.GuildId}");

                // Temporary settings until admin panel stuff is wired up
                var settings = new Settings
                {
                    FunResponsesEnabled = true,
                    AutoTitlesEnabled = true,
                    PreferEmbeds = true,
                };

                var messageData = BotMessageData.Create(data, settings);

                await this.PreProcessMessage(messageData, settings);

                BotResponseData responseData = await this.ProcessMessageAsync(messageData, settings);

                if (responseData.Embed != null)
                {
                    await this.RespondAsync(data, string.Empty, bypassEdit: true, responseData.Embed.CreateFluxerEmbed());
                }
                else
                {
                    foreach (string response in responseData.Responses)
                    {
                        await this.RespondAsync(data, response, bypassEdit: responseData.Responses.Count > 1);
                    }
                }
            }
            catch (Exception ex)
            {
                Log.Warning(ex, $"Error in HandleMessageCreated");
            }
        }

        private async Task HandleMessageUpdated(MessageGatewayData data)
        {
            try
            {
                if (DateTimeOffset.UtcNow.Subtract(data.Timestamp) < TimeSpan.FromHours(1) && !string.IsNullOrEmpty(data.Content))
                {
                    await this.HandleMessageCreated(data);
                }
            }
            catch (Exception ex)
            {
                Log.Warning(ex, $"Error in HandleMessageUpdated");
            }
        }

        private async Task HandleMessageDeleted(EntityRemovedGatewayData data)
        {
            try
            {
                if (data.Id == null || data.ChannelId == null)
                {
                    return;
                }

                var botMessageId = this.botResponsesCache.Remove(data.Id.Value);
                if (botMessageId != 0)
                {
                    await this.apiClient.DeleteMessage(data.ChannelId.Value, botMessageId);
                }
            }
            catch (Exception ex)
            {
                Log.Warning(ex, $"Error in HandleMessageDeleted");
            }
        }

        private async Task RespondAsync(MessageGatewayData messageData, string text, bool bypassEdit = false, Embed embed = null)
        {
            this.TrackEvent("messageSent");
            Message sentMessage;
            List<Embed> embeds = null;
            if (embed != null)
            {
                embeds = [embed];
            }

            var message = new Message
            {
                Embeds = embeds,
                Reference = new MessageRef
                {
                    MessageId = messageData.Id,
                    ChannelId = messageData.ChannelId,
                    Type = messageData.Type,
                },
                Content = text,
            };

            if (!bypassEdit && this.botResponsesCache.Get(messageData.Id) is ulong oldMessageId && oldMessageId != 0)
            {
                try
                {
                    var oldMessage = await this.apiClient.GetMessage(messageData.ChannelId, oldMessageId);
                    await this.apiClient.EditMessage(oldMessage.ChannelId, oldMessageId, message);
                    return;
                }
                catch (Exception ex)
                {
                    Log.Warning(ex, $"Failed to fetch old message for edit, it may have already been deleted");
                }
            }

            sentMessage = await this.apiClient.SendMessage(messageData.ChannelId, message);

            this.botResponsesCache.Add(messageData.Id, sentMessage.Id);
        }

        protected override async Task<bool> SendNotification(NotificationData notification)
        {
            Message originalMessage = null;
            if (!string.IsNullOrEmpty(notification.MessageId) && ulong.TryParse(notification.Channel, out var channelId) && ulong.TryParse(notification.MessageId, out var messageId))
            {
                try
                {
                    originalMessage = await this.apiClient.GetMessage(channelId, messageId);
                }
                catch (Exception ex)
                {
                    Log.Error(ex, "Failed to fetch notification original message");
                }
            }

            var message = new Message
            {
                Content = notification.Text,
            };

            if (originalMessage != null)
            {
                message.Reference = new MessageRef
                {
                    MessageId = originalMessage.Id,
                    ChannelId = originalMessage.ChannelId,
                    Type = originalMessage.Type,
                };
            }
            
            await this.apiClient.SendMessage(ulong.Parse(notification.Channel), message);

            return true;
        }

        protected override async Task StartAsyncInternal()
        {
            await this.client.ConnectAsync();
        }

        protected override Task StopAsyncInternal(bool unexpected)
        {
            this.client.Dispose();
            return Task.CompletedTask;
        }

        private void HeartbeatAck()
        {
            Log.Debug("Received heartbeat ACK from Fluxer");
            lastHeartbeatAck = DateTime.UtcNow;
        }

        private async void HeartBeatTimerAsync(object state)
        {
            if (DateTime.UtcNow.Subtract(lastHeartbeatAck) > TimeSpan.FromMinutes(10))
            {
                Log.Warning("No heartbeat ACK received from Fluxer in the last 10 minutes, reconnecting...");

                try
                {
                    if (this.Config.AlertEndpoint != null)
                    {
                        string messageContent = $"\U0001F501 {this.BotType} triggered automatic restart due to inactivity";
                        try
                        {
                            await this.Config.AlertEndpoint.PostJsonAsync(new { content = messageContent });
                        }
                        catch (Exception ex)
                        {
                            Log.Error(ex, "Failed to send alert message to endpoint");
                        }
                    }

                    lastHeartbeatAck = DateTime.UtcNow;
                    await this.client.ConnectAsync();
                }
                catch (Exception ex)
                {
                    Log.Error(ex, "Error while reconnecting to Fluxer");
                }
            }
            else
            {
                Log.Debug("Heartbeat ACK received recently, no need to reconnect");
            }
        }
    }
}
