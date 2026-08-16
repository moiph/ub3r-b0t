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
               "type     shard   servers |  shard   servers\n");

            int serverTotal = 0;
            var i = 0;
            foreach (HeartbeatData heartbeat in serversStatus)
            {
                serverTotal += heartbeat.ServerCount;

                var botType = heartbeat.BotType;
                if (!string.IsNullOrEmpty(botType) || i % 2 == 0)
                {
                    dataSb.Append(botType.PadRight(9));
                }
                var shard = heartbeat.Shard.ToString().PadLeft(4);
                var servers = heartbeat.ServerCount.ToString().PadLeft(8);

                dataSb.Append($"{shard}  {servers}");
                if (string.IsNullOrEmpty(botType) || botType == "Discord")
                {
                    if (i % 2 == 0)
                    {
                        dataSb.Append("  |  ");
                    }
                    else
                    {
                        dataSb.Append("\n");
                    }
                }
                else
                {
                    dataSb.Append("\n");
                }
                i++;
            }

            // add up totals
            dataSb.Append($"-------\n");
            dataSb.Append($"Total:            {serverTotal,8}\n");

            dataSb.Append("```");

            return new CommandResponse { Text = dataSb.ToString() };
        }
    }
}
