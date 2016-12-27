
namespace UB3RB0T
{
    using Discord;
    using Discord.WebSocket;
    using System;

    public static class Utilities
    {
        public static bool HasMentionPrefix(this IUserMessage msg, IUser user, ref int argPos)
        {
            var text = msg.Content;
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

            if (userId == user.Id)
            {
                argPos = endPos + 2;
                return true;
            }

            return false;
        }

        public static DateTime GetCreatedDate(this SocketUser user)
        {
            var timeStamp = ((user.Id >> 22) + 1420070400000) / 1000;
            var createdDate = new DateTime(1970, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc);
            return createdDate.AddSeconds(timeStamp);
        }

        public static string Random(this string[] array)
        {
            var randomNumber = new Random();
            return array[randomNumber.Next(array.Length)];
        }
    }
}
