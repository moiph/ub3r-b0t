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

        private readonly ConcurrentDictionary<ulong, ulong> _messages;
        private readonly ConcurrentQueue<ulong> _orderedMessages;
        private readonly int _size;

        public MessageCache()
        {
            _size = 1000;
            _messages = new ConcurrentDictionary<ulong, ulong>(DefaultConcurrencyLevel, (int)(_size * 1.05));
            _orderedMessages = new ConcurrentQueue<ulong>();
        }

        public void Add(ulong id, IMessage message)
        {
            if (message != null)
            {
                this.Add(id, message.Id);
            }
        }

        public void Add(ulong id, ulong message)
        {
            if (message != 0)
            {
                if (_messages.TryAdd(id, message))
                {
                    _orderedMessages.Enqueue(id);

                    while (_orderedMessages.Count > _size && _orderedMessages.TryDequeue(out ulong msgId))
                    {
                        _messages.TryRemove(msgId, out _);
                    }
                }
            }
        }

        public ulong Get(ulong id)
        {
            return _messages.ContainsKey(id) ? _messages[id] : 0;
        }

        public ulong Remove(ulong id)
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
