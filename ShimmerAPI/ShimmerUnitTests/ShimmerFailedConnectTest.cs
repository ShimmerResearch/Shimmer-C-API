using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using ShimmerAPI;

namespace ShimmerBluetoothTests
{
    /// <summary>
    /// A 32Feet BLE link that cannot open, such as one to a device without the expected service, reports
    /// that by staying closed: OpenConnection returns without throwing. Connect() had already started the
    /// read thread, never stopped it, and never said the connect failed. The thread then read the closed
    /// link, whose IOException ReadData swallows, for the life of the process (DEV-1314).
    /// </summary>
    [TestClass]
    public class ShimmerFailedConnectTest
    {
        [TestMethod]
        public void TestAConnectWhoseLinkNeverOpensStopsReadingAndSaysSo()
        {
            var device = new LinkThatNeverOpens();
            var messages = new List<string>();
            device.UICallback += (sender, args) =>
            {
                CustomEventArgs eventArgs = (CustomEventArgs)args;
                if (eventArgs.getIndicator() == (int)ShimmerBluetooth.ShimmerIdentifier.MSG_IDENTIFIER_NOTIFICATION_MESSAGE && eventArgs.getObject() is string)
                {
                    lock (messages)
                    {
                        messages.Add((string)eventArgs.getObject());
                    }
                }
            };
            try
            {
                device.Connect();
                Assert.AreEqual(ShimmerBluetooth.SHIMMER_STATE_NONE, device.GetState());
                lock (messages)
                {
                    CollectionAssert.Contains(messages, "Unable to connect to specified port");
                }
                Thread.Sleep(200);
                int reads = device.Reads;
                Thread.Sleep(300);
                Assert.AreEqual(reads, device.Reads, "The read thread is still reading the closed link");
            }
            finally
            {
                // Stops the thread where Connect did not
                device.Disconnect();
            }
        }

        /// <summary>
        /// A link that never opens, as ShimmerLogAndStream32FeetBLE's does when the device lacks the
        /// service: OpenConnection fails without throwing, and reading the closed link throws IOException,
        /// as LinkByteBuffer does.
        /// </summary>
        sealed class LinkThatNeverOpens : ShimmerLogAndStream
        {
            public int Reads;

            public LinkThatNeverOpens()
                : base("LinkThatNeverOpens")
            {
            }

            public override string GetShimmerAddress()
            {
                return "LinkThatNeverOpens";
            }

            public override void SetShimmerAddress(string address)
            {
            }

            protected override void OpenConnection()
            {
                SetState(SHIMMER_STATE_CONNECTING);
                SetState(SHIMMER_STATE_NONE);
            }

            protected override bool IsConnectionOpen()
            {
                return false;
            }

            protected override void CloseConnection()
            {
            }

            protected override void FlushConnection()
            {
            }

            protected override void FlushInputConnection()
            {
            }

            protected override int ReadByte()
            {
                Interlocked.Increment(ref Reads);
                throw new IOException("The link is closed");
            }

            protected override void WriteBytes(byte[] b, int index, int length)
            {
            }
        }
    }
}
