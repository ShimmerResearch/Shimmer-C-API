using Microsoft.VisualStudio.TestTools.UnitTesting;
using ShimmerAPI.Simulators;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace ShimmerBluetoothTests
{
    [TestClass]
    public class ShimmerLogAndStreamS3SimulatorTest
    {
        ShimmerLogAndStreamS3Simulator mDevice;
        String ComPort = "COM99";
        ConcurrentQueue<byte> cq = new ConcurrentQueue<byte>();

        [TestInitialize]
        public void SetUp()
        {
            mDevice = new ShimmerLogAndStreamS3Simulator("", ComPort);
        }

        [TestMethod]
        public void Test001_testConnectandDisconnect()
        {
            if (mDevice != null)
            {
                try
                {
                    mDevice.StartConnectThread();
                    Thread.Sleep(30000);
                    if (!mDevice.IsConnected())
                    {
                        Assert.Fail();
                    }

                    if (!mDevice.GetFirmwareVersionFullName().Equals("LogAndStream 0.16.9"))
                    {
                        Assert.Fail();
                    }

                    if (!mDevice.GetShimmerVersion().Equals(3)) //Shimmer3
                    {
                        Assert.Fail();
                    }
                }
                catch (Exception ex)
                {
                    Assert.Fail($"Test aborted due to exception: {ex.Message}");
                }
            }
            else
            {
                Assert.Fail("mDevice is null");
            }

        }

        /// <summary>
        /// Connecting reads the derived-channel bytes, and derived-sensor bit 29 (byte 3, bit 5)
        /// turns the gyro on-the-fly offset calibration on, as it does in the Java driver.
        /// </summary>
        [TestMethod]
        public void Test002_derivedBit29EnablesGyroOnTheFlyCalibration()
        {
            mDevice.DerivedChannelBytes = new byte[] { 0, 0, 0, 0x20, 0, 0, 0, 0 };
            mDevice.StartConnectThread();
            for (int i = 0; i < 600 && !mDevice.IsConnected(); i++)
            {
                Thread.Sleep(100);
            }
            Assert.IsTrue(mDevice.IsConnected());
            Assert.AreEqual(1L << 29, mDevice.GetDerivedSensors());
            Assert.IsTrue(mDevice.IsGyroOnTheFlyCalEnabled());
        }

    }
}
