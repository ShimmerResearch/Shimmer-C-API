using System;
using System.Collections.Generic;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using ShimmerAPI;

namespace ShimmerBluetoothTests
{
    /// <summary>
    /// Shimmer3R LogAndStream 1.0.24 to 1.0.49 hardfault when a status push the firmware sends by itself
    /// carries a 2-byte Bluetooth CRC: the push overruns its six-byte buffer by one byte (DEV-621, fixed
    /// in 1.0.50). The API refuses a 2-byte CRC on those releases, and a constructor's TWO_BYTE is turned
    /// on as ONE_BYTE with a warning (DEV-1141). A 1-byte CRC fits, and so does a 2-byte one wherever the
    /// status is one byte: Shimmer3R 1.0.23 and earlier, and every Shimmer3. Each case runs on a
    /// CrcFirmwareDevice, which records what it would have sent.
    /// </summary>
    [TestClass]
    public class ShimmerCrcStatusPushTest
    {
        const int Shimmer3 = (int)ShimmerBluetooth.ShimmerVersion.SHIMMER3;
        const int Shimmer3R = (int)ShimmerBluetooth.ShimmerVersion.SHIMMER3R;
        static readonly ShimmerBluetooth.BTCRCMode Off = ShimmerBluetooth.BTCRCMode.OFF;
        static readonly ShimmerBluetooth.BTCRCMode OneByte = ShimmerBluetooth.BTCRCMode.ONE_BYTE;
        static readonly ShimmerBluetooth.BTCRCMode TwoByte = ShimmerBluetooth.BTCRCMode.TWO_BYTE;

        /// <summary>The first and last Shimmer3R LogAndStream releases whose status push overruns: 1.0.x.</summary>
        static readonly int[] OverrunReleases = { 24, 49 };

        [TestMethod]
        public void TestTwoByteIsRefusedOnTheOverrunReleases()
        {
            foreach (int fwInternal in OverrunReleases)
            {
                var device = new CrcFirmwareDevice(Shimmer3R, 1, 0, fwInternal);
                Assert.IsTrue(device.TwoByteCRCOverrunsStatusPush());
                Assert.IsTrue(device.IsCRCSupported(), "A 1-byte CRC is still supported on 1.0." + fwInternal);
                Exception e = ShimmerCrcFirmwareTest.Throws(() => device.WriteCRCMode(TwoByte));
                StringAssert.Contains(e.Message, "Shimmer3R LogAndStream 1.0." + fwInternal);
                StringAssert.Contains(e.Message, "LogAndStream 1.0.50 or later");
                Assert.AreEqual(0, device.Written.Count, "Nothing should be sent");
                Assert.AreEqual(Off, device.GetCRCMode());
            }
        }

        [TestMethod]
        public void TestOneByteAndOffAreAllowedOnTheOverrunReleases()
        {
            foreach (int fwInternal in OverrunReleases)
            {
                foreach (var mode in new[] { OneByte, Off })
                {
                    var device = new CrcFirmwareDevice(Shimmer3R, 1, 0, fwInternal);
                    device.WriteCRCMode(mode);
                    ShimmerCrcFirmwareTest.AssertSentOnly(device, mode);
                    Assert.AreEqual(mode, device.GetCRCMode());
                }
            }
        }

        /// <summary>A link already on a 1-byte CRC keeps it when a 2-byte one is refused.</summary>
        [TestMethod]
        public void TestARefusedTwoByteLeavesTheLinkAsItWas()
        {
            var device = new CrcFirmwareDevice(Shimmer3R, 1, 0, 40);
            device.WriteCRCMode(OneByte);
            ShimmerCrcFirmwareTest.Throws(() => device.WriteCRCMode(TwoByte));
            ShimmerCrcFirmwareTest.AssertSentOnly(device, OneByte);
            Assert.AreEqual(OneByte, device.GetCRCMode());
        }

        /// <summary>
        /// A constructor's CRC mode is applied while connecting, where an exception would fail the
        /// connection. ShimmerConsoleAppExample asks for TWO_BYTE this way. On these releases it gets a
        /// 1-byte CRC, which the push has room for, and a warning says why.
        /// </summary>
        [TestMethod]
        public void TestConstructorTwoByteIsTurnedOnAsOneByteWithAWarning()
        {
            foreach (int fwInternal in OverrunReleases)
            {
                var device = new CrcFirmwareDevice(Shimmer3R, 1, 0, fwInternal, TwoByte);
                List<CustomEventArgs> notifications = ShimmerCrcFirmwareTest.CollectNotifications(device);
                device.ApplyConstructorCRCMode();
                ShimmerCrcFirmwareTest.AssertSentOnly(device, OneByte);
                Assert.AreEqual(OneByte, device.GetCRCMode());
                Assert.AreEqual(1, notifications.Count);
                Assert.AreEqual((int)ShimmerLogAndStream.ShimmerSDBTMinorIdentifier.MSG_WARNING, notifications[0].getMinorIndication());
                string message = (string)notifications[0].getObject();
                StringAssert.Contains(message, "A 1-byte Bluetooth CRC was turned on");
                StringAssert.Contains(message, "LogAndStream 1.0.50 or later");
            }
        }

        [TestMethod]
        public void TestConstructorOneByteIsAppliedWithoutAWarning()
        {
            var device = new CrcFirmwareDevice(Shimmer3R, 1, 0, 24, OneByte);
            List<CustomEventArgs> notifications = ShimmerCrcFirmwareTest.CollectNotifications(device);
            device.ApplyConstructorCRCMode();
            ShimmerCrcFirmwareTest.AssertSentOnly(device, OneByte);
            Assert.AreEqual(0, notifications.Count);
        }

        /// <summary>
        /// Where the push has room for a 2-byte CRC, a constructor's TWO_BYTE is sent as it is: one status
        /// byte on Shimmer3R 1.0.23, and on a Shimmer3 with the same version numbers as the overrun
        /// releases; the larger buffer on Shimmer3R 1.0.50.
        /// </summary>
        [TestMethod]
        public void TestConstructorTwoByteIsAppliedWhereThePushHasRoom()
        {
            foreach (int[] release in new[] { new[] { Shimmer3R, 23 }, new[] { Shimmer3R, 50 }, new[] { Shimmer3, 24 }, new[] { Shimmer3, 49 } })
            {
                var device = new CrcFirmwareDevice(release[0], 1, 0, release[1], TwoByte);
                List<CustomEventArgs> notifications = ShimmerCrcFirmwareTest.CollectNotifications(device);
                device.ApplyConstructorCRCMode();
                ShimmerCrcFirmwareTest.AssertSentOnly(device, TwoByte);
                Assert.AreEqual(TwoByte, device.GetCRCMode());
                Assert.AreEqual(0, notifications.Count, "HW " + release[0] + " LogAndStream 1.0." + release[1]);
            }
        }

        /// <summary>
        /// Only Shimmer3R LogAndStream 1.0.24 to 1.0.49 overrun. 1.0.24 is the first release with two
        /// status bytes, and every release from 1.0.50 has the larger buffer, the 1.1.x line included.
        /// </summary>
        [TestMethod]
        public void TestOnlyShimmer3R1_0_24To1_0_49Overrun()
        {
            AssertOverruns(false, Shimmer3R, 1, 0, 11);
            AssertOverruns(false, Shimmer3R, 1, 0, 23);
            AssertOverruns(true, Shimmer3R, 1, 0, 24);
            AssertOverruns(true, Shimmer3R, 1, 0, 40);
            AssertOverruns(true, Shimmer3R, 1, 0, 49);
            AssertOverruns(false, Shimmer3R, 1, 0, 50);
            AssertOverruns(false, Shimmer3R, 1, 0, 60);
            AssertOverruns(false, Shimmer3R, 1, 1, 0);
            AssertOverruns(false, Shimmer3R, 1, 1, 18);
            AssertOverruns(false, Shimmer3R, 2, 0, 0);

            AssertOverruns(false, Shimmer3, 1, 0, 24);
            AssertOverruns(false, Shimmer3, 1, 0, 49);
        }

        static void AssertOverruns(bool expected, int hardwareVersion, int major, int minor, int fwInternal)
        {
            var device = new CrcFirmwareDevice(hardwareVersion, major, minor, fwInternal);
            Assert.AreEqual(expected, device.TwoByteCRCOverrunsStatusPush(), "HW " + hardwareVersion + " LogAndStream " + major + "." + minor + "." + fwInternal);
        }
    }
}
