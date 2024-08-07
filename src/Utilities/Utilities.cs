
namespace UB3RB0T
{
    using Discord;
    using Discord.Net;
    using Discord.WebSocket;
    using Flurl.Http;
    using Microsoft.AspNetCore.WebUtilities;
    using Newtonsoft.Json;
    using Serilog;
    using System;
    using System.Collections.Generic;
    using System.Globalization;
    using System.IO;
    using System.Linq;
    using System.Net;
    using System.Net.Http;
    using System.Text;
    using System.Text.RegularExpressions;
    using System.Threading.Tasks;
    using GuildedEmbed = Guilded.Base.Embeds.Embed;

    public static class Utilities
    {
        const int ONEHOUR = 60 * 60;
        const int ONEDAY = ONEHOUR * 24;
        const int ONEWEEK = ONEDAY * 7;
        const int ONEFORTNIGHT = ONEWEEK * 2;
        const int ONEMONTH = ONEDAY * 31;
        const int ONEYEAR = ONEDAY * 365;
        const long TOOLONG = 315360000;
                             

        private static readonly HashSet<ulong> blockedDMUsers = new HashSet<ulong>();
        private static readonly Random random = new Random();

        public static void Forget(this Task task) { }

        public static Uri AppendQueryParam(this Uri uri, string key, string value)
        {
            var newQueryParams = new Dictionary<string, string> { { key, value } };
            return new Uri(QueryHelpers.AddQueryString(uri.ToString(), newQueryParams));
        }

        public static Task<IFlurlResponse> PostJsonAsync(this Uri uri, object data)
        {
            return uri.ToString().WithTimeout(15).PostJsonAsync(data);
        }

        public static bool IsSuccessStatusCode(this IFlurlResponse response)
        {
            return response.StatusCode >= 200 && response.StatusCode < 300;
        }

        public static Task<Stream> GetStreamAsync(this Uri uri)
        {
            return uri.ToString().WithTimeout(10).GetStreamAsync();
        }

        public static long ToUnixMilliseconds(DateTimeOffset dto)
        {
            return (dto.UtcDateTime.Ticks / TimeSpan.TicksPerMillisecond) - 62135596800;
        }

        public static string SubstringUpTo(this string value, int maxLength)
        {
            return value.Substring(0, Math.Min(value.Length, maxLength));
        }

        public static bool IContains(this string haystack, string needle)
        {
            return haystack.Contains(needle, StringComparison.OrdinalIgnoreCase);
        }

        public static bool IEquals(this string first, string second)
        {
            return first.Equals(second, StringComparison.OrdinalIgnoreCase);
        }

        public static string ReplaceMulti(this string s, string[] oldValues, string newValue)
        {
            var sb = new StringBuilder(s);
            foreach (var oldValue in oldValues)
            {
                sb.Replace(oldValue, newValue);
            }

            return sb.ToString();
        }

        public static EmbedBuilder CreateEmbedBuilder(this EmbedData embedData)
        {
            var embedBuilder = new EmbedBuilder
            {
                Title = embedData.Title?.SubstringUpTo(256),
                ThumbnailUrl = embedData.ThumbnailUrl,
                Description = embedData.Description,
                Url = Uri.IsWellFormedUriString(embedData.Url, UriKind.Absolute) ? embedData.Url : null,
                ImageUrl = embedData.ImageUrl,
            };

            if (!string.IsNullOrEmpty(embedData.Author))
            {
                embedBuilder.Author = new EmbedAuthorBuilder
                {
                    Name = embedData.Author,
                    Url = embedData.AuthorUrl,
                    IconUrl = embedData.AuthorIconUrl,
                };
            }

            if (!string.IsNullOrEmpty(embedData.Color))
            {
                var red = Convert.ToInt32(embedData.Color.Substring(0, 2), 16);
                var green = Convert.ToInt32(embedData.Color.Substring(2, 2), 16);
                var blue = Convert.ToInt32(embedData.Color.Substring(4, 2), 16);

                embedBuilder.Color = new Color(red / 255.0f, green / 255.0f, blue / 255.0f);
            }

            if (!string.IsNullOrEmpty(embedData.Footer))
            {
                embedBuilder.Footer = new EmbedFooterBuilder
                {
                    Text = embedData.Footer,
                    IconUrl = embedData.FooterIconUrl,
                };
            }

            if (embedData.EmbedFields != null)
            {
                foreach (var embedField in embedData.EmbedFields)
                {
                    if (!string.IsNullOrEmpty(embedField.Name) && !string.IsNullOrEmpty(embedField.Value))
                    {
                        embedBuilder.AddField((field) =>
                        {
                            field.IsInline = embedField.IsInline;
                            field.Name = embedField.Name;
                            field.Value = embedField.Value;
                        });
                    }
                }
            }

            return embedBuilder;
        }

        public static GuildedEmbed CreateGuildedEmbed(this EmbedData embedData)
        {
            var embed = new GuildedEmbed
            {
                Title = embedData.Title,
                Description = embedData.Description,
            };

            if (!string.IsNullOrEmpty(embedData.Author))
            {
                embed.SetAuthor(embedData.Author, new Uri(embedData.AuthorUrl), new Uri(embedData.AuthorIconUrl));
            }

            if (!string.IsNullOrEmpty(embedData.ThumbnailUrl))
            {
                embed.SetThumbnail(embedData.ThumbnailUrl);
            }

            if (!string.IsNullOrEmpty(embedData.ImageUrl))
            {
                embed.SetImage(embedData.ImageUrl);
            }

            if (!string.IsNullOrEmpty(embedData.Url))
            {
                embed.SetUrl(embedData.Url);
            }

            if (!string.IsNullOrEmpty(embedData.Color))
            {
                var red = Convert.ToInt32(embedData.Color.Substring(0, 2), 16);
                var green = Convert.ToInt32(embedData.Color.Substring(2, 2), 16);
                var blue = Convert.ToInt32(embedData.Color.Substring(4, 2), 16);

                embed.SetColor(red, green, blue);
            }

            if (!string.IsNullOrEmpty(embedData.Footer))
            {
                embed.SetFooter(embedData.Footer, embedData.FooterIconUrl);
            }

            if (embedData.EmbedFields != null)
            {
                foreach (var embedField in embedData.EmbedFields)
                {
                    if (!string.IsNullOrEmpty(embedField.Name) && !string.IsNullOrEmpty(embedField.Value))
                    {
                        embed.AddField(embedField.Name, embedField.Value, embedField.IsInline);
                    }
                }
            }

            return embed;
        }

        public static bool HasMentionPrefix(this string text, ulong botUserId, ref int argPos)
        {
            if (text.Length <= 3 || text[0] != '<' || text[1] != '@')
            {
                return false;
            }

            int endPos = text.IndexOf('>');
            if (endPos == -1)
            {
                return false;
            }

            // Must end in "> "
            if (text.Length < endPos + 2 || text[endPos + 1] != ' ')
            {
                return false; 
            }

            if (!MentionUtils.TryParseUser(text.Substring(0, endPos + 1), out ulong userId))
            {
                return false;
            }

            if (userId == botUserId)
            {
                argPos = endPos + 2;
                return true;
            }

            return false;
        }

        public static DateTime GetCreatedDate(this IUser user)
        {
            var timeStamp = ((user.Id >> 22) + 1420070400000) / 1000;
            var createdDate = new DateTime(1970, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc);
            return createdDate.AddSeconds(timeStamp);
        }

        public static string Random(this List<string> list)
        {
            return list[random.Next(0, list.Count)];
        }

        public static string Random(this string[] array)
        {
            return array[random.Next(array.Length)];
        }

        public static async Task<T> GetApiResponseAsync<T>(Uri uri)
        {
            try
            {
                var content = await uri.ToString().WithTimeout(TimeSpan.FromSeconds(15)).GetStringAsync();
                return JsonConvert.DeserializeObject<T>(content);
            }
            catch (Exception ex)
            {
                Log.Error(ex, $"Failed to parse {{Endpoint}}", uri);
                return default;
            }
        }

        public static bool TryParseAbsoluteReminder(Match timerMatch, BotMessageData messageData, out string query)
        {
            query = string.Empty;

            string toMatch = timerMatch.Groups["target"].ToString().Trim();
            string to = toMatch.IEquals("me") ? messageData.UserName : toMatch;
            string req = to == messageData.UserName ? string.Empty : messageData.UserName;
            string durationStr = string.Empty;
            long duration = 0;

            GroupCollection matchGroups = timerMatch.Groups;
            string reason = matchGroups["reason"].ToString();

            var dateTimeString = matchGroups["time"].ToString();
            if (dateTimeString.Contains(".")) // fix up e.g. +5.5 to +5:30
            {
                var parts = dateTimeString.Split(new[] { '.' });
                dateTimeString = parts[0];
                for (var i = 1; i < parts.Length; i++)
                {
                    var time = (float)int.Parse(parts[i]) / 10 * 60;
                    dateTimeString += $":{time}";
                }
            }

            if (matchGroups["date"].Success)
            {
                dateTimeString = matchGroups["date"] + " " + dateTimeString;
            }

            if (DateTime.TryParse(dateTimeString, DateTimeFormatInfo.InvariantInfo, DateTimeStyles.AdjustToUniversal, out DateTime dt))
            {
                duration = (long)Math.Ceiling(dt.Subtract(DateTime.UtcNow).TotalSeconds);

                // if duration was negative then set the date for tomorrow.
                if (duration < 0)
                {
                    duration = (long)dt.AddDays(1).Subtract(DateTime.UtcNow).TotalSeconds;
                }

                durationStr = $"{duration}s";
            }

            if (duration < 10 || duration > TOOLONG)
            {
                return false;
            }

            query = $"timer for:\"{to}\" {durationStr} {reason}";

            // if we see a pattern of "on yy/mm/dd" it indicates the user was trying to do an absolute time
            // reminder but parsing broke, so bail out. TODO: error messaging to the user
            if (Consts.TimerDateCheckRegex.IsMatch(reason))
            {
                return false;
            }

            return true;
        }

        public static bool TryParseReminder(Match timerMatch, BotMessageData messageData, out string query)
        {
            query = string.Empty;

            string toMatch = timerMatch.Groups["target"].ToString().Trim();
            string to = toMatch.IEquals("me") ? messageData.UserName : toMatch;
            string req = to == messageData.UserName ? string.Empty : messageData.UserName;
            string durationStr = string.Empty;
            long duration = 0;

            GroupCollection matchGroups = timerMatch.Groups;
            string reason = matchGroups["reason"].ToString();

            if (matchGroups["rand"].Success)
            {
                var randValue = random.Next(20, 360);
                duration += randValue * 60;
                durationStr = $"{randValue}m";
            }

            if (matchGroups["years"].Success)
            {
                string yearString = timerMatch.Groups["years"].ToString();
                if (yearString.IEquals("a year"))
                {
                    yearString = "1 year";
                }

                if (int.TryParse(yearString.Remove(yearString.Length - 5, 5), out int yearValue))
                {
                    duration += yearValue * ONEYEAR;
                    durationStr = $"{yearValue}y";
                }
            }

            if (matchGroups["months"].Success)
            {
                string monthString = timerMatch.Groups["months"].ToString();
                if (monthString.IEquals("a month"))
                {
                    monthString = "1 month";
                }

                if (int.TryParse(monthString.Remove(monthString.Length - 6, 6), out int monthValue))
                {
                    duration += monthValue * ONEMONTH; // approximation; final calculation will occur on command processing
                    durationStr = $"{monthValue}mo";
                }
            }

            if (matchGroups["fortnights"].Success)
            {
                string fortnightString = timerMatch.Groups["fortnights"].ToString();
                if (fortnightString.IEquals("a fortnight"))
                {
                    fortnightString = "1 fortnight";
                }

                if (int.TryParse(fortnightString.Remove(fortnightString.Length - 10, 10), out int fortnightValue))
                {
                    duration += fortnightValue * ONEFORTNIGHT;
                    durationStr = $"{fortnightValue}fn";
                }
            }

            if (matchGroups["weeks"].Success)
            {
                string weekString = matchGroups["weeks"].ToString();
                if (weekString.IEquals("a week"))
                {
                    weekString = "1 week";
                }

                if (int.TryParse(weekString.Remove(weekString.Length - 5, 5), out int weekValue))
                {
                    duration += weekValue * ONEWEEK;
                    durationStr += $"{weekValue}w";
                }
            }

            if (matchGroups["days"].Success)
            {
                string dayString = matchGroups["days"].ToString();
                if (dayString.IEquals("a day"))
                {
                    dayString = "1 day";
                }

                if (int.TryParse(dayString.Remove(dayString.Length - 4, 4), out int dayValue))
                {
                    duration += dayValue * ONEDAY;
                    durationStr += $"{dayValue}d";
                }
            }

            if (matchGroups["hours"].Success)
            {
                string hourString = matchGroups["hours"].ToString();
                if (hourString.IEquals("an hour"))
                {
                    hourString = "1 hour";
                }
                if (int.TryParse(hourString.Remove(hourString.Length - 5, 5), out int hourValue))
                {
                    duration += hourValue * ONEHOUR;
                    durationStr += $"{hourValue}h";
                }
            }

            if (matchGroups["minutes"].Success)
            {
                string minuteString = matchGroups["minutes"].ToString();
                if (int.TryParse(minuteString.Remove(minuteString.Length - 7, 7), out int minuteValue))
                {
                    duration += minuteValue * 60;
                    durationStr += $"{minuteValue}m";
                }
            }

            if (matchGroups["seconds"].Success)
            {
                string secongString = matchGroups["seconds"].ToString();
                if (int.TryParse(secongString.Remove(secongString.Length - 8, 8), out int secondValue))
                {
                    duration += secondValue;
                    durationStr += $"{secondValue}s";
                }
            }

            if (duration < 10 || duration > TOOLONG)
            {
                return false;
            }

            // if we see a pattern of "on yy/mm/dd" it indicates the user was trying to do an absolute time
            // reminder but parsing broke, so bail out. TODO: error messaging to the user
            if (Consts.TimerDateCheckRegex.IsMatch(reason))
            {
                return false;
            }

            query = $"timer for:\"{WebUtility.UrlEncode(to)}\" {durationStr} {reason}";

            return true;
        }

        public static string UserOrNickname(this IUser user)
        {
            if (user is SocketGuildUser guildUser && !string.IsNullOrEmpty(guildUser.Nickname))
            {
                return guildUser.Nickname;
            }

            return user.Username;
        }

        public static ChannelPermissions GetCurrentUserPermissions(this ITextChannel channel)
        {
            return (channel as SocketGuildChannel)?.Guild?.CurrentUser?.GetPermissions(channel) ?? new ChannelPermissions();
        }

        public static GuildPermissions GetCurrentUserGuildPermissions(this ITextChannel channel)
        {
            return (channel as SocketGuildChannel)?.Guild?.CurrentUser?.GuildPermissions ?? new GuildPermissions();
        }

        public static async Task<IUserMessage> SendOwnerDMAsync(this IGuild guild, string message)
        {
            if (blockedDMUsers.Contains(guild.OwnerId))
            {
                return null;
            }

            try
            {
                return await (await (await guild.GetOwnerAsync()).CreateDMChannelAsync()).SendMessageAsync(message);
            }
            catch (HttpException ex) when (ex.HttpCode == HttpStatusCode.Forbidden)
            {
                blockedDMUsers.Add(guild.OwnerId);
                Log.Debug(ex, "Failed to send guild owner message (forbidden");
            }
            catch (Exception ex)
            { 
                blockedDMUsers.Add(guild.OwnerId);
                Log.Warning(ex, "Failed to send guild owner message");
            }

            return null;
        }

        public static async Task<IUser> GetOrDownloadUserAsync(this SocketReaction reaction)
        {
            IUser reactionUser;
            if (reaction.User.IsSpecified)
            {
                reactionUser = reaction.User.Value;
            }
            else
            {
                var channel = reaction.Channel as SocketTextChannel;
                reactionUser = channel.GetUser(reaction.UserId);

                if (reactionUser == null)
                {
                    await channel.Guild.DownloadUsersAsync();
                    reactionUser = channel.GetUser(reaction.UserId);
                }   
            }

            return reactionUser;
        }

        public static async Task<IUserMessage> GetOrDownloadMessage(this SocketReaction reaction)
        {
            IUserMessage reactionMessage = null;
            if (reaction.Message.IsSpecified)
            {
                reactionMessage = reaction.Message.Value;
            }
            else
            {
                reactionMessage = await reaction.Channel.GetMessageAsync(reaction.MessageId) as IUserMessage;
            }

            return reactionMessage;
        }

        /// <summary>
        /// Grabs the attachment URL from the message attachment, if present, else tries to parse an image URL out of the message contents.
        /// </summary>
        /// <param name="message">The message to parse</param>
        /// <returns>Image URL</returns>
        public static string ParseImageUrl(this SocketUserMessage message)
        {
            string attachmentUrl = null;

            var attachment = message.Attachments?.FirstOrDefault();
            if (attachment != null)
            {
                // attachment needs to be larger than 50x50 (API restrictions)
                if (attachment.Height > 50 && attachment.Width > 50)
                {
                    attachmentUrl = attachment.Url;
                }
            }
            else
            {
                if (Uri.TryCreate(message.Content, UriKind.Absolute, out Uri attachmentUri))
                {
                    attachmentUrl = attachmentUri.ToString();
                    if (!attachmentUrl.EndsWith(".jpg") && !attachmentUrl.EndsWith(".png"))
                    {
                        attachmentUrl = null;
                    }
                }
            }

            return attachmentUrl;
        }

        // port from discord.net .9x
        public static IEnumerable<IUser> Find(this IEnumerable<IUser> users, string name, ushort? discriminator = null, bool exactMatch = false)
        {
            //Search by name
            var query = users.Where(x => string.Equals(x.Username, name, exactMatch ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase));

            if (!exactMatch)
            {
                if (name.Length >= 3 && name[0] == '<' && name[1] == '@' && name[2] == '!' && name[name.Length - 1] == '>') // Search by nickname'd mention
                {
                    if (name.Substring(3, name.Length - 4).TryToId(out ulong id))
                    {
                        var user = users.Where(x => x.Id == id).FirstOrDefault();
                        if (user != null)
                        {
                            query = query.Concat(new IUser[] { user });
                        }
                    }
                }
                if (name.Length >= 2 && name[0] == '<' && name[1] == '@' && name[name.Length - 1] == '>') // Search by raw mention
                {
                    if (name.Substring(2, name.Length - 3).TryToId(out ulong id))
                    {
                        var user = users.Where(x => x.Id == id).FirstOrDefault();
                        if (user != null)
                        {
                            query = query.Concat(new IUser[] { user });
                        }
                    }
                }
                if (name.Length >= 1 && name[0] == '@') // Search by clean mention
                {
                    string name2 = name.Substring(1);
                    query = query.Concat(users.Where(x => string.Equals(x.Username, name2, StringComparison.OrdinalIgnoreCase)));
                }
                if (name.TryToId(out var userId))
                {
                    query = query.Concat(users.Where(x => x.Id == userId));
                }
            }

            if (discriminator != null)
            {
                query = query.Where(x => x.DiscriminatorValue == discriminator.Value);
            }

            return query;
        }

        public static bool TryToId(this string value, out ulong result) => ulong.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out result);

        /// <summary>
        /// Helper to get a unix timestamp.
        /// </summary>
        public static long Utime => (long)(DateTime.UtcNow - new DateTime(1970, 1, 1, 0, 0, 0)).TotalSeconds;
    }
}
