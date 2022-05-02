namespace UB3RB0T
{
    using Discord;
    using System;
    using System.Collections.Concurrent;

    // Based on https://github.com/RogueException/Discord.Net/blob/dev/src/Discord.Net.WebSocket/Entities/Messages/MessageCache.cs
    internal class MessageCache<T>
    {
        private const int PROCESSOR_COUNT_REFRESH_INTERVAL_MS = 30000;
        private static volatile int s_processorCount;
        private static volatile int s_lastProcessorCountRefreshTicks;

        private readonly ConcurrentDictionary<T, T> _messages;
        private readonly ConcurrentQueue<T> _orderedMessages;
        private readonly int _size;

        public MessageCache()
        {
            _size = 1000;
            _messages = new ConcurrentDictionary<T, T>(DefaultConcurrencyLevel, (int)(_size * 1.05));
            _orderedMessages = new ConcurrentQueue<T>();
        }

        public void Add(T id, T message)
        {
            if (message != null)
            {
                if (_messages.TryAdd(id, message))
                {
                    _orderedMessages.Enqueue(id);

                    while (_orderedMessages.Count > _size && _orderedMessages.TryDequeue(out T msgId))
                    {
                        _messages.TryRemove(msgId, out _);
                    }
                }
            }
        }

        public T Get(T id)
        {
            return _messages.ContainsKey(id) ? _messages[id] : default(T);
        }

        public T Remove(T id)
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
