
namespace UB3RB0T
{
    using System;
    using System.Collections.Generic;
    using System.Net;
    using System.Threading.Tasks;

    /// <summary>
    /// Internal, private API for UB3R-B0T.
    /// TODO: Try to generalize this, notably allow for a dynamic customizeable response so others can tailor for their needs.
    /// </summary>
    public class BotApi
    {
        private Uri apiEndpoint;
        private string apiKey;
        private BotType botType;

        public BotApi(Uri endpoint, string key, BotType botType)
        {
            this.apiEndpoint = endpoint;
            this.apiKey = key;
            this.botType = botType;
        }

        public async Task<BotResponseData> IssueRequestAsync(BotMessageData messageData, string query)
        {
            var responses = new List<string>();
            var responseData = new BotResponseData();

            string requestUrl = string.Format("{0}?apikey={1}&nick={2}&host={3}&server={4}&channel={5}&bottype={6}&userId={7}&format={8}&query={9}",
                this.apiEndpoint,
                this.apiKey,
                WebUtility.UrlEncode(messageData.UserName),
                WebUtility.UrlEncode(messageData.UserHost ?? messageData.UserId),
                messageData.Server,
                WebUtility.UrlEncode(messageData.Channel),
                this.botType.ToString().ToLowerInvariant(),
                messageData.UserId,
                messageData.Format,
                WebUtility.UrlEncode(query));

            var botResponse = await Utilities.GetApiResponseAsync<BotApiResponse>(new Uri(requestUrl));

            if (botResponse != null)
            {
                if (botResponse.Embed != null)
                {
                    responseData.Embed = botResponse.Embed;
                }
                else
                {
                    // TODO: fix API stupidity and just return the array
                    var botResponses = botResponse.Msgs.Length > 0 ? botResponse.Msgs : new string[] { botResponse.Msg };

                    if (botResponses[0] != null)
                    {
                        char a = (char)1;
                        char b = (char)2;
                        char c = (char)3;

                        foreach (var response in botResponses)
                        {
                            if (response != null)
                            {
                                responses.Add(response.Replace("%a", a.ToString()).Replace("%b", b.ToString()).Replace("%c", c.ToString()));
                            }
                        }

                        if (this.botType == BotType.Discord)
                        {
                            string response = string.Join("\n", responses);

                            // Extra processing for figlet/cowsay on Discord
                            if (query.StartsWith("cowsay", StringComparison.OrdinalIgnoreCase) || query.StartsWith("figlet", StringComparison.OrdinalIgnoreCase))
                            {
                                // use a non printable character to force preceeding whitespace to display correctly
                                response = "```" + (char)1 + response + "```";
                            }

                            responses = new List<string> { response };
                        }
                    }
                }

                responseData.Responses = responses;
            }

            return responseData;
        }
    }
}
