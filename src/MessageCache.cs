namespace UB3RB0T
{
    using Discord;
    using System;
    using System.Collections.Concurrent;

    // Based on https://github.com/RogueException/Discord.Net/blob/dev/src/Discord.Net.WebSocket/Entities/Messages/MessageCache.cs
    internal class MessageCache
    {
        private const int PROCESSOR_COUNT_REFRESH_INTERVAL_MS = 30000;
        private static volatile int s_processorCount;
        private static volatile int s_lastProcessorCountRefreshTicks;

        private readonly ConcurrentDictionary<ulong, IMessage> _messages;
        private readonly ConcurrentQueue<ulong> _orderedMessages;
        private readonly int _size;

        public MessageCache()
        {
            _size = 500;
            _messages = new ConcurrentDictionary<ulong, IMessage>(DefaultConcurrencyLevel, (int)(_size * 1.05));
            _orderedMessages = new ConcurrentQueue<ulong>();
        }

        public void Add(ulong id, IMessage message)
        {
            if (_messages.TryAdd(id, message))
            {
                _orderedMessages.Enqueue(id);

                while (_orderedMessages.Count > _size && _orderedMessages.TryDequeue(out ulong msgId))
                {
                    _messages.TryRemove(msgId, out var msg);
                }
            }
        }

        public IMessage Remove(ulong id)
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
