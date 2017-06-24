
namespace UB3RB0T
{
    using System;
    using UB3RIRC;

    public class WebhookLog : ILog
    {
        private const int MessageLengthLimit = 2000;

        public BotType BotType { get; }
        public int Shard { get; }
        public Uri WebhookUri { get; }

        public WebhookLog(BotType botType, int shard, Uri webhookUri)
        {
            this.BotType = botType;
            this.Shard = shard;
            this.WebhookUri = webhookUri;
        }

        public void Debug(string text, long ticks)
        {
            // ignore
        }

        public void Error(string text, long ticks)
        {
            this.ReportError(LogType.Error, text);
        }

        public void Fatal(string text, long ticks)
        {
            this.ReportError(LogType.Fatal, text);
        }

        public void Incoming(string text, long ticks)
        {
            // ignore
        }

        public void Info(string text, long ticks)
        {
            // ignore
        }

        public void Outgoing(string text, long ticks)
        {
            // ignore
        }

        public void Warn(string text, long ticks)
        {
            this.ReportError(LogType.Warn, text);
        }

        private void ReportError(LogType severity, string message)
        {
            if (!string.IsNullOrEmpty(message))
            {
                message = message.ToLowerInvariant();
                // TODO:
                // need a better way to classify these errors.
                // Don't want to dismiss Warning severity as a whole (yet? Maybe?), but a lot of it is inactionable from the client perpsective.
                if (!message.ToLowerInvariant().Contains("rate limit") &&
                    !message.Contains("referenced an unknown") && 
                    !message.Contains("unknown user") &&
                    !message.Contains("unknown channel") &&
                    !message.Contains("unknown guild"))
                {
                    string iconType = "\U00002139";
                    switch (severity)
                    {
                        case LogType.Warn:
                            iconType = "\U000026A0";
                            break;
                        case LogType.Error:
                        case LogType.Fatal:
                            iconType = "\U0000274C";
                            break;
                    }

                    try
                    {
                        string messageContent = $"{iconType} {severity} on {this.BotType} shard {this.Shard}: {message}";
                        this.WebhookUri.PostJsonAsync(new { content = messageContent.Substring(0, Math.Min(messageContent.Length, MessageLengthLimit)) });
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine("Error sending log data: " + ex);
                    }
                }
            }
        }
    }
}
