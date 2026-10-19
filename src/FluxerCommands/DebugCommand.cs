namespace UB3RB0T.Commands
{
    using System.Reflection;
    using System.Threading.Tasks;

    internal class FluxerDebugCommand : IFluxerCommand
    {
        public Task<CommandResponse?> Process(IFluxerBotContext context)
        {
            var serverId = context.MessageData.Server ?? "n/a";
            var botVersion = Assembly.GetEntryAssembly()?.GetName()?.Version?.ToString() ?? "Unknown";
            var fluxerVersion = Assembly.GetAssembly(typeof(Fluxer.Net.FluxerClient))?.GetName().Version?.ToString() ?? "Unknown";
            var response = new CommandResponse
            {
                Text = $"```Server ID: {serverId} | Channel ID: {context.MessageData.Channel} | Your ID: {context.MessageData.UserId} | Version: {botVersion} | Fluxer.NET Version: {fluxerVersion}```"
            };

            return Task.FromResult<CommandResponse?>(response);
        }
    }
}
