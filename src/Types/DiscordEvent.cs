using System;

namespace UB3RB0T
{
    public class DiscordEvent
    {
        public DiscordEvent()
        {
            this.Created = DateTime.Now.Ticks;
        }

        public DiscordEventType EventType { get; set; }

        public object[] Args { get; set; }

        public long Created { get; }

        public TimeSpan Elapsed => new TimeSpan(DateTime.Now.Ticks - this.Created);
    }
}
