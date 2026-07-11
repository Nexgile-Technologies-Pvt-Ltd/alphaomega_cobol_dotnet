namespace CardDemo.Mq;

/// <summary>
/// An in-proc FIFO queue keyed by its trimmed mainframe name (MQ_SHIM.md §6.1). Created on first use by
/// the <see cref="MqBroker"/>. Both CardDemo modules GET "any next" (MsgId/CorrelId = NONE), so GET is a
/// plain head-removal; the only matching the shim does is on the reply side (a requester filters the
/// reply queue by its CorrelId — see <see cref="MqBroker.Request"/>).
/// </summary>
public sealed class MqQueue
{
    private readonly LinkedList<MqMessage> _messages = new();

    /// <summary>The trimmed mainframe queue name this queue is keyed on.</summary>
    public string Name { get; }

    internal MqQueue(string name) => Name = name;

    /// <summary>Number of messages currently enqueued.</summary>
    public int Count => _messages.Count;

    /// <summary>Appends a message to the tail (MQPUT / MQPUT1 enqueue).</summary>
    public void Enqueue(MqMessage message) => _messages.AddLast(message);

    /// <summary>
    /// Removes and returns the head message, or null when empty (the no-message / MQRC-NO-MSG-AVAILABLE
    /// case). Messages whose <see cref="MqMessage.ExpiresAtUtc"/> has passed (per <paramref name="nowUtc"/>)
    /// are discarded first, so an expired auth reply is never handed back (MQ_SHIM.md §6.1).
    /// </summary>
    public MqMessage? Dequeue(DateTime nowUtc)
    {
        while (_messages.First is { } node)
        {
            _messages.RemoveFirst();
            if (node.Value.ExpiresAtUtc is { } exp && exp <= nowUtc)
                continue;   // drop the expired message and try the next
            return node.Value;
        }
        return null;
    }

    /// <summary>
    /// Removes and returns the first non-expired message whose CorrelId matches <paramref name="correlId"/>
    /// (how a requester correlates its reply). Returns null if none match. Non-matching messages stay queued.
    /// </summary>
    public MqMessage? DequeueByCorrelId(byte[] correlId, DateTime nowUtc)
    {
        // Pass 1: drop any expired messages (so an expired reply is never matched/returned).
        LinkedListNode<MqMessage>? node = _messages.First;
        while (node is not null)
        {
            LinkedListNode<MqMessage> current = node;
            node = node.Next;
            if (current.Value.ExpiresAtUtc is { } exp && exp <= nowUtc)
                _messages.Remove(current);
        }

        // Pass 2: return the first message whose CorrelId matches.
        for (node = _messages.First; node is not null; node = node.Next)
        {
            if (MqMessage.IdEquals(node.Value.CorrelId, correlId))
            {
                _messages.Remove(node);
                return node.Value;
            }
        }
        return null;
    }

    /// <summary>Discards all messages (test/reset helper).</summary>
    public void Clear() => _messages.Clear();
}
