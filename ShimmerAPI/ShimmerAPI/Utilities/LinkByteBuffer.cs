using System;
using System.Collections.Concurrent;
using System.IO;

namespace ShimmerAPI.Utilities
{
    /// <summary>
    /// The bytes a radio delivers in notifications, for ReadData to take one at a time as it takes
    /// them from a serial port. A read waits up to a timeout and then throws TimeoutException, as a
    /// serial port's read does. That keeps ReadData's timeout handling (the idle status poll,
    /// "Connection lost") working on the link, and a read never spins.
    /// <para>
    /// <see cref="Close"/> releases a waiting read at once, so the read thread can see the link has
    /// gone and stop. A read of a closed, empty buffer throws IOException, as a serial port's read does
    /// when the port is closed under it, not TimeoutException: ReadData answers a timeout by polling the
    /// link for the unit's status, and a link being torn down must not be written to. A closed buffer
    /// stays closed: a link makes a new one each time it opens.
    /// </para>
    /// </summary>
    public sealed class LinkByteBuffer
    {
        readonly BlockingCollection<byte> bytes = new BlockingCollection<byte>();

        /// <summary>Queues the bytes of one notification. Ignored once the buffer is closed.</summary>
        public void Add(byte[] received)
        {
            try
            {
                foreach (byte b in received)
                {
                    if (!bytes.TryAdd(b))
                    {
                        return;
                    }
                }
            }
            catch (InvalidOperationException)
            {
                // Closed while adding: the link is gone, and so is what it was sending
            }
        }

        /// <summary>
        /// The next byte, waiting up to <paramref name="timeoutMs"/> for one.
        /// </summary>
        /// <exception cref="TimeoutException">If none arrived in time</exception>
        /// <exception cref="IOException">If the buffer is closed and empty: the link has gone</exception>
        public int Read(int timeoutMs)
        {
            byte b;
            if (bytes.TryTake(out b, timeoutMs))
            {
                return b;
            }
            if (bytes.IsCompleted)
            {
                throw new IOException("The link is closed");
            }
            throw new TimeoutException();
        }

        /// <summary>Stops taking bytes, and wakes a read that is waiting.</summary>
        public void Close()
        {
            bytes.CompleteAdding();
        }

        public int Count
        {
            get { return bytes.Count; }
        }
    }
}
