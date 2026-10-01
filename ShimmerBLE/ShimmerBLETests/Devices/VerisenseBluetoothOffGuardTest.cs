using NUnit.Framework;
using shimmer.Models;
using ShimmerBLETests.Communications;
using System;
using System.Threading.Tasks;
using static shimmer.Models.OpConfigPayload;

namespace ShimmerBLETests
{
    /// <summary>
    /// DEV-1096: before V2.01.003, ASM_Production handled USB only while its SoftDevice was on, and
    /// started the SoftDevice only for Bluetooth, so a sensor with Bluetooth off and USB on - which
    /// the firmware's comms-channel interlock allows - was unreachable. Operational config writes
    /// keep Bluetooth on for that firmware, and for firmware whose version is unknown.
    /// </summary>
    public class VerisenseBluetoothOffGuardTest
    {
        readonly string uuid = "00000000-0000-0000-0000-c96117537402";

        // VerisenseCommandsTest's default: GEN_CFG_0 0x17 has Bluetooth on and USB off.
        readonly byte[] defaultBytes = new byte[] { 0x5A, 0x17, 0x74, 0x00, 0x00, 0x00, 0x00, 0x00, 0x7F, 0x00, 0xD8, 0x0F, 0x00, 0x00, 0x00, 0x00, 0x80, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x03, 0xF4, 0x18, 0x1C, 0x02, 0x0A, 0x0F, 0x00, 0x18, 0x1C, 0x02, 0x0A, 0x0F, 0x00, 0x18, 0x1C, 0x02, 0x0A, 0x0F, 0x00, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0x3C, 0x00, 0x0E, 0x00, 0x00, 0x63, 0x28, 0xCC, 0xCC, 0x1E, 0x00, 0x0A, 0x00, 0x00, 0x00, 0x00, 0x01 };

        const int GenCfg0 = (int)ConfigurationBytesIndexName.GEN_CFG_0;
        const byte BluetoothBit = 0b00010000;
        const byte UsbBit = 0b00001000;
        const int RequestHeaderLength = 3;

        /// <summary>The default config with Bluetooth off and USB on: the offline-logger setting</summary>
        byte[] BluetoothOffUsbOn()
        {
            var bytes = (byte[])defaultBytes.Clone();
            bytes[GenCfg0] = (byte)((bytes[GenCfg0] & ~BluetoothBit) | UsbBit);
            return bytes;
        }

        /// <summary>A production config read reply reporting firmware major.minor.internal</summary>
        static byte[] ProdConfigReporting(int major, int minor, int internalVersion)
        {
            return new byte[] { 51, 13, 0, 90, 215, 74, 1, 2, 4, 25, 68, 9, (byte)major, (byte)minor, (byte)(internalVersion & 0xFF), (byte)(internalVersion >> 8) };
        }

        static bool BluetoothOnInRequest(byte[] request)
        {
            return (request[RequestHeaderLength + GenCfg0] & BluetoothBit) != 0;
        }

        [TestCase(1, 2, 87, false)]
        [TestCase(2, 0, 6, false)]
        [TestCase(2, 0, 7, false)]
        [TestCase(2, 1, 2, false)]
        [TestCase(2, 1, 3, true)]
        [TestCase(2, 2, 0, true)]
        [TestCase(3, 0, 0, true)]
        [TestCase(255, 255, 65535, false)]
        public void SupportsBluetoothOffFromV2_01_003(int major, int minor, int internalVersion, bool expected)
        {
            var device = new TestVerisenseBLEDevice(uuid, "", defaultBytes, ProdConfigReporting(major, minor, internalVersion));
            Assert.AreEqual(expected, device.SupportsBluetoothOff());
        }

        [Test]
        public void DoesNotSupportBluetoothOffWithNoProductionConfig()
        {
            Assert.IsFalse(new TestVerisenseBLEDevice(uuid, "", defaultBytes).SupportsBluetoothOff());
        }

        [Test]
        public void GuardKeepsBluetoothOnForV2_01_002AndLeavesTheOtherBits()
        {
            var device = new TestVerisenseBLEDevice(uuid, "", defaultBytes, ProdConfigReporting(2, 1, 2));
            var opConfig = BluetoothOffUsbOn();
            var before = opConfig[GenCfg0];
            Assert.IsTrue(device.EnforceBluetoothOffFirmwareGuard(opConfig));
            Assert.AreEqual((byte)(before | BluetoothBit), opConfig[GenCfg0]);
        }

        [Test]
        public void GuardLetsBluetoothOffThroughFromV2_01_003()
        {
            var device = new TestVerisenseBLEDevice(uuid, "", defaultBytes, ProdConfigReporting(2, 1, 3));
            var opConfig = BluetoothOffUsbOn();
            Assert.IsFalse(device.EnforceBluetoothOffFirmwareGuard(opConfig));
            CollectionAssert.AreEqual(BluetoothOffUsbOn(), opConfig);
        }

        [Test]
        public void GuardLeavesConfigsWithBluetoothOnAlone()
        {
            var device = new TestVerisenseBLEDevice(uuid, "", defaultBytes);
            var opConfig = (byte[])defaultBytes.Clone();
            Assert.IsFalse(device.EnforceBluetoothOffFirmwareGuard(opConfig));
            CollectionAssert.AreEqual(defaultBytes, opConfig);
        }

        /// <summary>A device connected over the fake radio, with its production config read if given</summary>
        async Task<TestVerisenseBLEDevice> ConnectedDevice(byte[] prodConfigResponse, bool readProdConfig)
        {
            var device = new TestVerisenseBLEDevice(uuid, "");
            Assert.IsTrue(await device.Connect(false));
            if (prodConfigResponse != null)
            {
                device.Radio.ProdConfigResponse = prodConfigResponse;
            }
            if (readProdConfig)
            {
                await device.ExecuteRequest(RequestType.ReadProductionConfig);
            }
            return device;
        }

        [Test]
        public async Task WriteKeepsBluetoothOnForASensorReportingV2_01_002()
        {
            var device = await ConnectedDevice(ProdConfigReporting(2, 1, 2), true);
            var opConfig = BluetoothOffUsbOn();
            await device.ExecuteRequest(RequestType.WriteOperationalConfig, opConfig);
            Assert.IsTrue(BluetoothOnInRequest(device.Radio.LastOpConfigWriteRequest));
            CollectionAssert.AreEqual(BluetoothOffUsbOn(), opConfig, "the caller's array is left as it was");
        }

        [Test]
        public async Task WriteLetsBluetoothOffThroughForASensorReportingV2_01_003()
        {
            var device = await ConnectedDevice(ProdConfigReporting(2, 1, 3), true);
            await device.ExecuteRequest(RequestType.WriteOperationalConfig, BluetoothOffUsbOn());
            Assert.IsFalse(BluetoothOnInRequest(device.Radio.LastOpConfigWriteRequest));
        }

        // Connect(true, configuration, ...) writes the configuration before it reads the
        // production config, so the write has to find the version out for itself.
        [Test]
        public async Task WriteReadsTheVersionFirstWhenNoneIsKnown()
        {
            var device = await ConnectedDevice(ProdConfigReporting(2, 1, 3), false);
            await device.ExecuteRequest(RequestType.WriteOperationalConfig, BluetoothOffUsbOn());
            Assert.AreEqual(1, device.Radio.ProdConfigReads);
            Assert.IsFalse(BluetoothOnInRequest(device.Radio.LastOpConfigWriteRequest));
        }

        [Test]
        public async Task WriteKeepsBluetoothOnWhenTheVersionReadShowsOldFirmware()
        {
            var device = await ConnectedDevice(null, false); // the radio's default reports 1.2.87
            await device.ExecuteRequest(RequestType.WriteOperationalConfig, BluetoothOffUsbOn());
            Assert.AreEqual(1, device.Radio.ProdConfigReads);
            Assert.IsTrue(BluetoothOnInRequest(device.Radio.LastOpConfigWriteRequest));
        }

        [Test]
        public async Task WriteKeepsBluetoothOnForAnErasedProductionConfig()
        {
            var erased = new byte[] { 51, 13, 0, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF };
            var device = await ConnectedDevice(erased, true);
            await device.ExecuteRequest(RequestType.WriteOperationalConfig, BluetoothOffUsbOn());
            Assert.IsTrue(BluetoothOnInRequest(device.Radio.LastOpConfigWriteRequest));
        }

        [Test]
        public async Task WriteWithBluetoothOnDoesNotReadTheVersion()
        {
            var device = await ConnectedDevice(null, false);
            await device.ExecuteRequest(RequestType.WriteOperationalConfig, (byte[])defaultBytes.Clone());
            Assert.AreEqual(0, device.Radio.ProdConfigReads);
            Assert.IsTrue(BluetoothOnInRequest(device.Radio.LastOpConfigWriteRequest));
        }

        /// <summary>
        /// A subclass that builds its own write requests, as ASM_BaseStation's sync service does
        /// from the cloud's config
        /// </summary>
        class RequestBuildingDevice : TestVerisenseBLEDevice
        {
            public byte[] Config;

            public RequestBuildingDevice(string id) : base(id, "")
            {
            }

            static byte[] Request(byte[] config)
            {
                var request = new byte[config.Length + RequestHeaderLength];
                request[0] = 0x24;
                request[1] = (byte)(config.Length & 0xFF);
                request[2] = (byte)(config.Length >> 8);
                Array.Copy(config, 0, request, RequestHeaderLength, config.Length);
                return request;
            }

            protected override Task<byte[]> CreateWriteOpConfigRequest()
            {
                return Task.FromResult(Request(Config));
            }

            protected override Task<byte[]> CreateWriteOpConfigRequestOnUnpairing()
            {
                return Task.FromResult(Request(Config));
            }
        }

        async Task<RequestBuildingDevice> ConnectedRequestBuildingDevice(byte[] prodConfigResponse)
        {
            var device = new RequestBuildingDevice(uuid) { Config = BluetoothOffUsbOn() };
            Assert.IsTrue(await device.Connect(false));
            device.Radio.ProdConfigResponse = prodConfigResponse;
            return device;
        }

        // Its bytes cannot be looked at before it is built, so a built request with no version known
        // reads the production config first, whatever it holds.
        [TestCase(RequestType.WriteOperationalConfig)]
        [TestCase(RequestType.OperationalConfigWriteOnUnpairing)]
        public async Task ABuiltRequestKeepsBluetoothOnForV2_01_002(RequestType requestType)
        {
            var device = await ConnectedRequestBuildingDevice(ProdConfigReporting(2, 1, 2));
            await device.ExecuteRequest(requestType);
            Assert.AreEqual(1, device.Radio.ProdConfigReads);
            Assert.IsTrue(BluetoothOnInRequest(device.Radio.LastOpConfigWriteRequest));
        }

        [TestCase(RequestType.WriteOperationalConfig)]
        [TestCase(RequestType.OperationalConfigWriteOnUnpairing)]
        public async Task ABuiltRequestLetsBluetoothOffThroughForV2_01_003(RequestType requestType)
        {
            var device = await ConnectedRequestBuildingDevice(ProdConfigReporting(2, 1, 3));
            await device.ExecuteRequest(requestType);
            Assert.AreEqual(1, device.Radio.ProdConfigReads);
            Assert.IsFalse(BluetoothOnInRequest(device.Radio.LastOpConfigWriteRequest));
        }
    }
}
