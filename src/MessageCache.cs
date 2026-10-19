namespace UB3RB0T
{
    using Discord;
    using System;
    using System.Collections.Concurrent;

    // Based on https://github.com/RogueException/Discord.Net/blob/dev/src/Discord.Net.WebSocket/Entities/Messages/MessageCache.cs
    internal class MessageCache<TKey, TValue>
        where TKey : notnull
    {
        private const int PROCESSOR_COUNT_REFRESH_INTERVAL_MS = 30000;
        private static volatile int s_processorCount;
        private static volatile int s_lastProcessorCountRefreshTicks;

        private readonly ConcurrentDictionary<TKey, TValue> _messages;
        private readonly ConcurrentQueue<TKey> _orderedMessages;
        private readonly int _size;

        public MessageCache()
        {
            _size = 1000;
            _messages = new ConcurrentDictionary<TKey, TValue>(DefaultConcurrencyLevel, (int)(_size * 1.05));
            _orderedMessages = new ConcurrentQueue<TKey>();
        }

        public void Add(TKey id, TValue message)
        {
            if (message != null)
            {
                if (_messages.TryAdd(id, message))
                {
                    _orderedMessages.Enqueue(id);

                    while (_orderedMessages.Count > _size && _orderedMessages.TryDequeue(out TKey? msgId))
                    {
                        if (msgId != null)
                        {
                            _messages.TryRemove(msgId, out _);
                        }
                    }
                }
            }
        }

        public TValue? Get(TKey id)
        {
            return _messages.ContainsKey(id) ? _messages[id] : default;
        }

        public TValue? Remove(TKey id)
        {
            _messages.TryRemove(id, out var msg);
            return msg;
        }

        public static int DefaultConcurrencyLevel
        {
            get
            {
                int now = Environment.TickCount;
                if (s_processorCount == 0 || (now - s_lastProcessorCountRefreshTicks) >= PROCESSOR_COUNT_REFRESH_INTERVAL_MS)
                {
                    s_processorCount = Environment.ProcessorCount;
                    s_lastProcessorCountRefreshTicks = now;
                }

                return s_processorCount;
            }
        }
    }
}
