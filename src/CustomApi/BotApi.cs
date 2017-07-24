
namespace UB3RB0T
{
    using Newtonsoft.Json;
    using System;
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
            messageData.Query = query;

            try
            {
                var response = await new Uri($"{this.apiEndpoint}/{messageData.Command}").PostJsonAsync(messageData);
                return JsonConvert.DeserializeObject<BotResponseData>(await response.Content.ReadAsStringAsync());
            }
            catch (Exception ex)
            {
                // TODO: proper logger
                Console.WriteLine($"Failed to parse {this.apiEndpoint}: ");
                Console.WriteLine(ex);
                return null;
            }
        }
    }
}
