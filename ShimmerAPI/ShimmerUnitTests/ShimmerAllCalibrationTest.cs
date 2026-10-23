using System;
using System.Linq;
using System.Threading;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using ShimmerAPI;
using ShimmerAPI.Simulators;
using ShimmerAPI.Utilities;

namespace ShimmerBluetoothTests
{
    /// <summary>
    /// GET_ALL_CALIBRATION_COMMAND is answered with ALL_CALIBRATION_RESPONSE (0x2D) and 21-byte blocks:
    /// LN accel, gyro, mag and WR accel, then alt accel and alt mag from a Shimmer3R only
    /// (log-and-stream-common Comms/shimmer_bt_uart.c, under SHIMMER3R). The blocks here are a
    /// Shimmer3's own, as the S3 simulator's infomem holds them.
    /// </summary>
    [TestClass]
    public class ShimmerAllCalibrationTest
    {
        const int Shimmer3 = (int)ShimmerBluetooth.ShimmerVersion.SHIMMER3;
        const int Shimmer3R = (int)ShimmerBluetooth.ShimmerVersion.SHIMMER3R;
        static readonly byte[] Shimmer3Blocks = UtilShimmer.HexStringToByteArray(
            "083C081F0825005300530054019C009C0100FEFF9C" + // LN accel
            "0000000000000CD00CD00CD0009C009C000000009C" + // gyro
            "002AFBEDFC21010700E500FD009C0064000000009C" + // mag
            "00000000000001A201A201A2009C0064000000009C"); // WR accel
        static readonly double[,] LnAccelSensitivity = { { 83, 0, 0 }, { 0, 83, 0 }, { 0, 0, 84 } };
        static readonly double[,] LnAccelOffset = { { 2108 }, { 2079 }, { 2085 } };
        static readonly double[,] WrAccelSensitivity = { { 418, 0, 0 }, { 0, 418, 0 }, { 0, 0, 418 } };

        /// <summary>
        /// The parse read six blocks from every device that is not a Shimmer2, so on a Shimmer3 it took
        /// the next reply's 42 bytes as alt accel and alt mag calibration, and that reply was lost (DEV-1137).
        /// </summary>
        [TestMethod]
        public void TestAShimmer3ReplyLeavesTheNextReplyToBeRead()
        {
            var device = new CrcLinkDevice(Shimmer3, 1, 1, 7, ShimmerBluetooth.BTCRCMode.OFF);
            device.AllCalibrationBlocks = Shimmer3Blocks;
            device.StartReading();
            try
            {
                device.ReadCalibrationParameters("All");
                ShimmerCrcSwitchTest.AssertReadsSamplingRate(device, 320, "after a Shimmer3's calibration reply");
                AssertMatrix(LnAccelSensitivity, device.SensitivityMatrixAccel, "LN accel sensitivity");
                AssertMatrix(LnAccelOffset, device.OffsetVectorAccel, "LN accel offset");
                AssertMatrix(WrAccelSensitivity, device.SensitivityMatrixAccel2, "WR accel sensitivity");
            }
            finally
            {
                device.StopReadingAndWait();
            }
        }

        /// <summary>A Shimmer3R's six blocks are all read, and nothing after them.</summary>
        [TestMethod]
        public void TestAShimmer3RReplyIsReadWhole()
        {
            var device = new CrcLinkDevice(Shimmer3R, 1, 1, 22, ShimmerBluetooth.BTCRCMode.OFF);
            device.AllCalibrationBlocks = Shimmer3Blocks.Concat(Shimmer3Blocks.Skip(42).Take(42)).ToArray();
            device.StartReading();
            try
            {
                device.ReadCalibrationParameters("All");
                ShimmerCrcSwitchTest.AssertReadsSamplingRate(device, 320, "after a Shimmer3R's calibration reply");
                AssertMatrix(LnAccelSensitivity, device.SensitivityMatrixAccel, "LN accel sensitivity");
                AssertMatrix(WrAccelSensitivity, device.SensitivityMatrixAccel2, "WR accel sensitivity");
            }
            finally
            {
                device.StopReadingAndWait();
            }
        }

        /// <summary>
        /// A Shimmer3 connected through the 32Feet BLE class read the calibration dump instead, which sets
        /// none of the calibration the stream is decoded with, so its accel decoded with the Shimmer2
        /// starting values: sensitivity 38, offset 2048 (DEV-1271). The connect sequence chose that by the
        /// class's name, so a simulator with the same name takes the path it took.
        /// </summary>
        [TestMethod]
        public void TestAShimmer3OverTheBleClassReadsTheCalibrationAsOverClassic()
        {
            var device = new ShimmerLogAndStream32FeetBLE();
            device.AllCalibrationBlocks = Shimmer3Blocks;
            device.StartConnectThread();
            DateTime deadline = DateTime.UtcNow.AddSeconds(60);
            while (!device.IsConnected() && DateTime.UtcNow < deadline)
            {
                Thread.Sleep(100);
            }
            Assert.IsTrue(device.IsConnected(), "The simulator did not connect");
            AssertMatrix(LnAccelSensitivity, device.SensitivityMatrixAccel, "LN accel sensitivity");
            AssertMatrix(LnAccelOffset, device.OffsetVectorAccel, "LN accel offset");
            AssertMatrix(WrAccelSensitivity, device.SensitivityMatrixAccel2, "WR accel sensitivity");
        }

        static void AssertMatrix(double[,] expected, double[,] actual, string what)
        {
            Assert.AreEqual(expected.GetLength(0), actual.GetLength(0), what);
            Assert.AreEqual(expected.GetLength(1), actual.GetLength(1), what);
            for (int i = 0; i < expected.GetLength(0); i++)
            {
                for (int j = 0; j < expected.GetLength(1); j++)
                {
                    Assert.AreEqual(expected[i, j], actual[i, j], what + " [" + i + "," + j + "]");
                }
            }
        }

        /// <summary>Named as the 32Feet BLE class is, which is what the connect sequence looked at.</summary>
        sealed class ShimmerLogAndStream32FeetBLE : ShimmerLogAndStreamS3Simulator
        {
            public ShimmerLogAndStream32FeetBLE()
                : base("", "COM99")
            {
            }
        }
    }
}
