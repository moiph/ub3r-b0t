using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Fluxer.Net;
using Fluxer.Net.Gateway;
using Fluxer.Net.Rest;
using Serilog;
using Serilog.Core;

namespace UB3RB0T
{
    public class FluxerBot : Bot
    {
        private readonly FluxerClient client;
        private readonly MessageCache<ulong> botResponsesCache = new();

        private readonly Timer heartbeatTimer;
        private DateTime lastHeartbeatAck = DateTime.MinValue;

        public FluxerBot(int shard, int totalShards) : base(shard, totalShards)
        {
            var fluxerConfig = new FluxerConfig
            {
                RestSerilog = Log.Logger as Logger,
                GatewaySerilog = Log.Logger as Logger,
                IgnoredGatewayEvents = ["PRESENCE_UPDATE", "TYPING_START"],
            };

            this.client = new FluxerClient(this.Config.Fluxer.Token, fluxerConfig);

            this.client.Gateway.MessageCreate += async data => await this.HandleMessageCreated(data);
            this.client.Gateway.MessageUpdate += async data => await this.HandleMessageUpdated(data);
            this.client.Gateway.MessageDelete += async data => await this.HandleMessageDeleted(data);
            this.client.Gateway.HeartbeatAck += this.HeartbeatAck;

            this.heartbeatTimer = new Timer(HeartBeatTimerAsync, null, 60000, 60000 * 5);
        }

        public override BotType BotType => BotType.Fluxer;
        protected override string UserId => this.Config.Fluxer.BotId;

        protected override async Task<HeartbeatData> GetHeartbeatData()
        {
            List<Guild> guilds = null;
            try
            {
                guilds = (await this.client.Rest.GetCurrentUserGuildsAsync()).ToList();
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "Failed to fetch guilds for heartbeat data");
            }

            var heartbeatData = new HeartbeatData
            {
                ServerCount = guilds?.Count ?? 0,
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
                    await this.RespondAsync(data, string.Empty, embed: responseData.Embed.CreateFluxerEmbed());
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
                if (DateTimeOffset.UtcNow.Subtract(data.CreatedAt) < TimeSpan.FromHours(1) && !string.IsNullOrEmpty(data.Content))
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
                    await this.client.Rest.DeleteMessageAsync(data.ChannelId.Value, botMessageId);
                }
            }
            catch (Exception ex)
            {
                Log.Warning(ex, $"Error in HandleMessageDeleted");
            }
        }

        private async Task RespondAsync(MessageGatewayData messageData, string text, bool bypassEdit = false, EmbedRequest embed = null)
        {
            this.TrackEvent("messageSent");
            List<EmbedRequest> embeds = null;
            if (embed != null)
            {
                embeds = [embed];
            }

            var message = new MessageRequest
            {
                MessageReference = new MessageReferenceRequest
                {
                    MessageId = messageData.Id,
                    ChannelId = messageData.ChannelId,
                },
                Content = text,
            };

            if (!bypassEdit && this.botResponsesCache.Get(messageData.Id) is ulong oldMessageId && oldMessageId != 0)
            {
                try
                {
                    var oldMessage = await this.client.Rest.GetMessageAsync(messageData.ChannelId, oldMessageId);

                    var messageUpdateRequest = new UpdateMessageRequest
                    {
                        Content = message.Content,
                    };

                    if (embed != null)
                    {
                        var embedRequest = new EmbedRequest
                        {
                            Title = embed?.Title,
                            Description = embed?.Description,
                            Url = embed?.Url,
                            Color = embed?.Color,
                            Author = embed?.Author != null ? new EmbedAuthorRequest
                            {
                                Name = embed.Author.Name,
                                Url = embed.Author.Url,
                                IconUrl = embed.Author.IconUrl,
                            } : null,
                            Image = embed?.Image != null ? new EmbedMediaRequest
                            {
                                Url = embed.Image.Url,
                            } : null,
                            Thumbnail = embed.Thumbnail != null ? new EmbedMediaRequest
                            {
                                Url = embed.Thumbnail.Url,
                            } : null,
                            Footer = embed?.Footer != null ? new EmbedFooterRequest
                            {
                                Text = embed.Footer.Text,
                                IconUrl = embed.Footer.IconUrl,
                            } : null,
                            Fields = embed?.Fields?.Select(f => new EmbedFieldRequest
                            {
                                Name = f.Name,
                                Value = f.Value,
                                IsInline = f.IsInline,
                            }).ToArray(),
                        };

                        messageUpdateRequest.Embeds = [embedRequest];
                    }

                    await this.client.Rest.EditMessageAsync(oldMessage.ChannelId, oldMessageId, messageUpdateRequest);
                    return;
                }
                catch (Exception ex)
                {
                    Log.Warning(ex, $"Failed to fetch old message for edit, it may have already been deleted");
                }
            }

            var sentMessage = await this.client.Rest.SendMessageAsync(messageData.ChannelId, message.Content, embeds, message.MessageReference);

            this.botResponsesCache.Add(messageData.Id, sentMessage.Id);
        }

        protected override async Task<bool> SendNotification(NotificationData notification)
        {
            Message originalMessage = null;
            if (!string.IsNullOrEmpty(notification.MessageId) && ulong.TryParse(notification.Channel, out var channelId) && ulong.TryParse(notification.MessageId, out var messageId))
            {
                try
                {
                    originalMessage = await this.client.Rest.GetMessageAsync(channelId, messageId);
                }
                catch (Exception ex)
                {
                    Log.Error(ex, "Failed to fetch notification original message");
                }
            }

            var message = new MessageRequest
            {
                Content = notification.Text,
                MessageReference = originalMessage != null ? new MessageReferenceRequest
                {
                    MessageId = originalMessage.Id,
                    ChannelId = originalMessage.ChannelId,
                } : null,
            };
            
            await this.client.Rest.SendMessageAsync(ulong.Parse(notification.Channel), message.Content, reference: message.MessageReference);

            return true;
        }

        protected override async Task StartAsyncInternal()
        {
            await this.client.Gateway.ConnectAsync();
        }

        protected override Task StopAsyncInternal(bool unexpected)
        {
            this.client.Gateway.Dispose();
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
                    await this.client.Gateway.ConnectAsync();
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
