namespace UB3RB0T.Commands
{
    using System.Text;
    using System.Threading.Tasks;

    public class StatusCommand : IDiscordCommand
    {
        public async Task<CommandResponse> Process(IDiscordBotContext context)
        {
            var serversStatus = await Utilities.GetApiResponseAsync<HeartbeatData[]>(BotConfig.Instance.HeartbeatEndpoint);

            var dataSb = new StringBuilder();
            dataSb.Append("```cs\n" +
               "type       shard   servers      users\n");

            int serverTotal = 0;
            int userTotal = 0;
            int voiceTotal = 0;
            foreach (HeartbeatData heartbeat in serversStatus)
            {
                serverTotal += heartbeat.ServerCount;
                userTotal += heartbeat.UserCount;
                voiceTotal += heartbeat.VoiceChannelCount;

                var botType = heartbeat.BotType.PadRight(11);
                var shard = heartbeat.Shard.ToString().PadLeft(4);
                var servers = heartbeat.ServerCount.ToString().PadLeft(8);
                var users = heartbeat.UserCount.ToString().PadLeft(10);

                dataSb.Append($"{botType} {shard}  {servers} {users}\n");
            }

            // add up totals
            dataSb.Append($"-------\n");
            dataSb.Append($"Total:            {serverTotal,8} {userTotal,10}\n");

            dataSb.Append("```");

            return new CommandResponse { Text = dataSb.ToString() };
        }
    }
}
