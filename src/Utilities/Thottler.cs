using System;
using System.Collections.Concurrent;

namespace UB3RB0T
{
    // Modified from https://github.com/Joe4evr/Discord.Addons/blob/master/src/Discord.Addons.Preconditions/Ratelimit/RatelimitAttribute.cs
    public class Throttler
    {
        private readonly ConcurrentDictionary<string, MessageTimeout> invokeTracker = new ConcurrentDictionary<string, MessageTimeout>();

        public void Increment(string key, ThrottleType throttleType)
        {
            if (BotConfig.Instance.Throttles.TryGetValue(throttleType, out var throttle))
            {
                this.AddOrUpdate($"{throttleType}_{key}", throttle, true);
            }
        }

        public bool IsThrottled(string key, ThrottleType throttleType)
        {
            if (BotConfig.Instance.Throttles.TryGetValue(throttleType, out var throttle))
            {
                return this.AddOrUpdate($"{throttleType}_{key}", throttle, false).TimesInvoked > throttle.Limit;
            }

            return false;
        }

        private MessageTimeout AddOrUpdate(string key, Throttle throttle, bool increment = true)
        {
            var now = DateTime.UtcNow;

            return this.invokeTracker.AddOrUpdate(key, new MessageTimeout(now), (k, val) =>
            {
                TimeSpan timespan = throttle.PeriodInMinutes > 0 ? TimeSpan.FromMinutes(throttle.PeriodInMinutes) : TimeSpan.FromSeconds(throttle.PeriodInSeconds);
                if ((now - val.FirstInvoke) > timespan)
                {
                    val = new MessageTimeout(now);
                }

                if (increment)
                {
                    val.TimesInvoked++;
                }
                return val;
            });
        }
    }

    internal class MessageTimeout
    {
        public uint TimesInvoked { get; set; }
        public DateTime FirstInvoke { get; }

        public MessageTimeout(DateTime timeStarted)
        {
            this.FirstInvoke = timeStarted;
        }
    }
}
