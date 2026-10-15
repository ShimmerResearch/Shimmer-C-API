using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using ShimmerAPI;

namespace ShimmerBluetoothTests
{
    /// <summary>
    /// Shimmer3R LogAndStream 0.0.2 to 1.0.10 turn the Bluetooth CRC off by themselves whenever sensing
    /// stops, without telling the host, so every reply after a stop arrives without the CRC this API
    /// still expects (DEV-976). 1.0.11 is the first release that keeps it. The API no longer turns a CRC
    /// on for those releases, whether asked through WriteCRCMode or through a constructor's CRC mode.
    /// No Shimmer3 firmware clears the CRC at a stop, and the two version lines overlap, so a Shimmer3
    /// on LogAndStream 1.0.8 still gets one. Each case runs on a ShimmerLogAndStream with no connection
    /// behind it, which records what it would have sent.
    /// </summary>
    [TestClass]
    public class ShimmerCrcFirmwareTest
    {
        const int Shimmer3 = (int)ShimmerBluetooth.ShimmerVersion.SHIMMER3;
        const int Shimmer3R = (int)ShimmerBluetooth.ShimmerVersion.SHIMMER3R;
        const byte SetCrcCommand = 0x8B;

        [TestMethod]
        public void TestShimmer3RLogAndStream1_0_10IsRefused()
        {
            var device = new CrcFirmwareDevice(Shimmer3R, 1, 0, 10);
            Assert.IsFalse(device.KeepsCRCWhenSensingStops());
            Assert.IsFalse(device.IsCRCSupported());
            foreach (var mode in new[] { ShimmerBluetooth.BTCRCMode.ONE_BYTE, ShimmerBluetooth.BTCRCMode.TWO_BYTE })
            {
                Exception e = Throws(() => device.WriteCRCMode(mode));
                StringAssert.Contains(e.Message, "Shimmer3R LogAndStream 1.0.10");
                StringAssert.Contains(e.Message, "LogAndStream 1.0.11 or later");
            }
            Assert.AreEqual(0, device.Written.Count, "Nothing should be sent");
            Assert.AreEqual(ShimmerBluetooth.BTCRCMode.OFF, device.GetCRCMode());
        }

        [TestMethod]
        public void TestShimmer3RLogAndStream1_0_11IsAllowed()
        {
            var device = new CrcFirmwareDevice(Shimmer3R, 1, 0, 11);
            Assert.IsTrue(device.KeepsCRCWhenSensingStops());
            Assert.IsTrue(device.IsCRCSupported());
            device.WriteCRCMode(ShimmerBluetooth.BTCRCMode.TWO_BYTE);
            AssertSentOnly(device, ShimmerBluetooth.BTCRCMode.TWO_BYTE);
            Assert.AreEqual(ShimmerBluetooth.BTCRCMode.TWO_BYTE, device.GetCRCMode());
        }

        /// <summary>
        /// LogAndStream_Shimmer3R_v1.00.008, a side build for older Consensys, reports itself as exactly
        /// this, so it gets a CRC too. That is deliberate: refusing it would refuse every Shimmer3 on 1.0.8.
        /// </summary>
        [TestMethod]
        public void TestShimmer3LogAndStream1_0_8IsAllowed()
        {
            var device = new CrcFirmwareDevice(Shimmer3, 1, 0, 8);
            Assert.IsTrue(device.KeepsCRCWhenSensingStops());
            Assert.IsTrue(device.IsCRCSupported());
            device.WriteCRCMode(ShimmerBluetooth.BTCRCMode.ONE_BYTE);
            AssertSentOnly(device, ShimmerBluetooth.BTCRCMode.ONE_BYTE);
            Assert.AreEqual(ShimmerBluetooth.BTCRCMode.ONE_BYTE, device.GetCRCMode());
        }

        /// <summary>Turning the CRC off is never refused, so a caller that turns it off to be safe still can.</summary>
        [TestMethod]
        public void TestTurningTheCRCOffIsNotRefused()
        {
            var device = new CrcFirmwareDevice(Shimmer3R, 1, 0, 10);
            device.WriteCRCMode(ShimmerBluetooth.BTCRCMode.OFF);
            AssertSentOnly(device, ShimmerBluetooth.BTCRCMode.OFF);
        }

        /// <summary>
        /// A constructor's CRC mode is applied while connecting, where an exception would fail the
        /// connection. So the device connects without a CRC, and a warning says why.
        /// </summary>
        [TestMethod]
        public void TestConstructorCRCModeIsRefusedWithAWarning()
        {
            var device = new CrcFirmwareDevice(Shimmer3R, 1, 0, 10, ShimmerBluetooth.BTCRCMode.TWO_BYTE);
            List<CustomEventArgs> notifications = CollectNotifications(device);
            device.ApplyConstructorCRCMode();
            Assert.AreEqual(0, device.Written.Count, "Nothing should be sent");
            Assert.AreEqual(ShimmerBluetooth.BTCRCMode.OFF, device.GetCRCMode());
            Assert.AreEqual(1, notifications.Count);
            Assert.AreEqual((int)ShimmerLogAndStream.ShimmerSDBTMinorIdentifier.MSG_WARNING, notifications[0].getMinorIndication());
            StringAssert.Contains((string)notifications[0].getObject(), "LogAndStream 1.0.11 or later");

            // No CRC asked for, nothing to warn about
            device = new CrcFirmwareDevice(Shimmer3R, 1, 0, 10);
            notifications = CollectNotifications(device);
            device.ApplyConstructorCRCMode();
            Assert.AreEqual(0, notifications.Count);
            Assert.AreEqual(ShimmerBluetooth.BTCRCMode.OFF, device.GetCRCMode());
        }

        [TestMethod]
        public void TestConstructorCRCModeIsAppliedWhereTheCRCIsKept()
        {
            foreach (int hardwareVersion in new[] { Shimmer3R, Shimmer3 })
            {
                int fwInternal = hardwareVersion == Shimmer3R ? 11 : 8;
                var device = new CrcFirmwareDevice(hardwareVersion, 1, 0, fwInternal, ShimmerBluetooth.BTCRCMode.TWO_BYTE);
                List<CustomEventArgs> notifications = CollectNotifications(device);
                device.ApplyConstructorCRCMode();
                AssertSentOnly(device, ShimmerBluetooth.BTCRCMode.TWO_BYTE);
                Assert.AreEqual(ShimmerBluetooth.BTCRCMode.TWO_BYTE, device.GetCRCMode());
                Assert.AreEqual(0, notifications.Count, "HW " + hardwareVersion);
            }
        }

        /// <summary>
        /// Every Shimmer3R LogAndStream release before 1.0.11 is refused and every one from it is
        /// allowed. A Shimmer3 is still decided by whether its firmware has the command at all
        /// (LogAndStream 0.13.7), whatever its version.
        /// </summary>
        [TestMethod]
        public void TestOnlyShimmer3RBefore1_0_11IsRefused()
        {
            AssertCRCSupported(false, Shimmer3R, 0, 0, 2);
            AssertCRCSupported(false, Shimmer3R, 1, 0, 5);
            AssertCRCSupported(false, Shimmer3R, 1, 0, 8);
            AssertCRCSupported(false, Shimmer3R, 1, 0, 10);
            AssertCRCSupported(true, Shimmer3R, 1, 0, 11);
            AssertCRCSupported(true, Shimmer3R, 1, 0, 45);
            AssertCRCSupported(true, Shimmer3R, 1, 1, 0);
            AssertCRCSupported(true, Shimmer3R, 2, 0, 0);

            AssertCRCSupported(false, Shimmer3, 0, 13, 6);
            AssertCRCSupported(true, Shimmer3, 0, 13, 7);
            AssertCRCSupported(true, Shimmer3, 0, 16, 9);
            AssertCRCSupported(true, Shimmer3, 1, 0, 8);
            AssertCRCSupported(true, Shimmer3, 1, 0, 10);
        }

        static void AssertCRCSupported(bool expected, int hardwareVersion, int major, int minor, int fwInternal)
        {
            var device = new CrcFirmwareDevice(hardwareVersion, major, minor, fwInternal);
            Assert.AreEqual(expected, device.IsCRCSupported(), "HW " + hardwareVersion + " LogAndStream " + major + "." + minor + "." + fwInternal);
        }

        static void AssertSentOnly(CrcFirmwareDevice device, ShimmerBluetooth.BTCRCMode mode)
        {
            Assert.AreEqual(1, device.Written.Count);
            CollectionAssert.AreEqual(new byte[] { SetCrcCommand, (byte)mode }, device.Written[0]);
        }

        static List<CustomEventArgs> CollectNotifications(CrcFirmwareDevice device)
        {
            var notifications = new List<CustomEventArgs>();
            device.UICallback += (sender, args) =>
            {
                CustomEventArgs eventArgs = (CustomEventArgs)args;
                if (eventArgs.getIndicator() == (int)ShimmerBluetooth.ShimmerIdentifier.MSG_IDENTIFIER_NOTIFICATION_MESSAGE)
                {
                    notifications.Add(eventArgs);
                }
            };
            return notifications;
        }

        static Exception Throws(Action action)
        {
            try
            {
                action();
            }
            catch (Exception e)
            {
                return e;
            }
            Assert.Fail("Expected an exception");
            return null;
        }
    }

    /// <summary>
    /// A connected ShimmerLogAndStream on the given hardware and LogAndStream version, with no connection
    /// behind it. It records every command it would have sent.
    /// </summary>
    class CrcFirmwareDevice : ShimmerLogAndStream
    {
        public readonly List<byte[]> Written = new List<byte[]>();

        public CrcFirmwareDevice(int hardwareVersion, int major, int minor, int fwInternal, BTCRCMode crcModeToSetup = BTCRCMode.OFF)
            : base("CrcFirmwareDevice", 51.2, 0, 0, 0, false, false, false, 0, 0, SHIMMER3_DEFAULT_TEST_REG1, SHIMMER3_DEFAULT_TEST_REG2, false, crcModeToSetup)
        {
            HardwareVersion = hardwareVersion;
            FirmwareIdentifier = FW_IDENTIFIER_LOGANDSTREAM;
            FirmwareMajor = major;
            FirmwareMinor = minor;
            FirmwareInternal = fwInternal;
            FirmwareVersionFullName = "LogAndStream " + major + "." + minor + "." + fwInternal;
            SetCompatibilityCode();
            SetState(SHIMMER_STATE_CONNECTED);
        }

        /// <summary>What InitializeShimmer3SDBT does with the constructor's CRC mode while connecting.</summary>
        public void ApplyConstructorCRCMode()
        {
            WriteCRCModeToSetup();
        }

        public override string GetShimmerAddress()
        {
            return "CrcFirmwareDevice";
        }

        public override void SetShimmerAddress(string address)
        {
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

        protected override bool IsConnectionOpen()
        {
            return true;
        }

        protected override void OpenConnection()
        {
        }

        protected override int ReadByte()
        {
            throw new NotSupportedException();
        }

        protected override void WriteBytes(byte[] b, int index, int length)
        {
            Written.Add(b.Skip(index).Take(length).ToArray());
        }
    }
}
