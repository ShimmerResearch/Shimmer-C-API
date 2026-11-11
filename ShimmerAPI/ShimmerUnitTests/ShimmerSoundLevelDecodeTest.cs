using System;
using System.Collections.Generic;
using System.IO;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using ShimmerAPI;

namespace ShimmerBluetoothTests
{
    /// <summary>
    /// The Shimmer3R's sound level channel, 0x38: an int16 in 0.01 dB(A) steps, with -32768 while the
    /// firmware has no level to report. RAW is the int16, CAL is RAW / 100 in dB(A), or NaN for
    /// -32768. The channel has no enabled-sensors bit, so the decoder finds it in the channel list.
    /// Each case goes through InterpretDataPacketFormat and BuildMsg, or ShimmerSDLog for a file.
    /// </summary>
    [TestClass]
    public class ShimmerSoundLevelDecodeTest
    {
        const byte SoundLevel = 0x38;
        const byte VBatt = 0x03;

        static readonly string Name = Shimmer3RConfiguration.SignalNames.SOUND_LEVEL;
        static readonly string Raw = ShimmerConfiguration.SignalFormats.RAW;
        static readonly string Cal = ShimmerConfiguration.SignalFormats.CAL;

        [TestMethod]
        public void TestLevelDecodesInHundredthsOfADecibel()
        {
            var decoder = new SoundLevelDecoder((int)ShimmerBluetooth.ShimmerVersion.SHIMMER3R, SoundLevel, VBatt);
            foreach (int raw in new[] { 4941, 0, 1, -1, 12345, 32767, -32767 })
            {
                ObjectCluster ojc = decoder.Decode(Packet(0x123456, raw, 2872));
                Assert.AreEqual((double)raw, ojc.GetData(Name, Raw).Data, "raw " + raw);
                SensorData cal = ojc.GetData(Name, Cal);
                Assert.AreEqual(ShimmerConfiguration.SignalUnits.DecibelsA, cal.Unit, "raw " + raw);
                Assert.AreEqual(raw / 100.0, cal.Data, 1e-12, "raw " + raw);
            }
            Assert.AreEqual(49.41, ShimmerBluetooth.CalibrateSoundLevel(4941), 1e-12);
        }

        [TestMethod]
        public void TestNoLevelDecodesAsNaN()
        {
            var decoder = new SoundLevelDecoder((int)ShimmerBluetooth.ShimmerVersion.SHIMMER3R, SoundLevel, VBatt);
            ObjectCluster ojc = decoder.Decode(Packet(0x000010, ShimmerBluetooth.SOUND_LEVEL_NO_LEVEL, 2867));

            Assert.AreEqual(-32768.0, ojc.GetData(Name, Raw).Data);
            // The CAL entry is still there, so every packet carries the same signals
            Assert.IsTrue(double.IsNaN(ojc.GetData(Name, Cal).Data));
            Assert.IsTrue(double.IsNaN(ShimmerBluetooth.CalibrateSoundLevel(ShimmerBluetooth.SOUND_LEVEL_NO_LEVEL)));
        }

        /// <summary>
        /// The battery after the level is read from its own bytes: the level takes two bytes, no more
        /// and no fewer.
        /// </summary>
        [TestMethod]
        public void TestTheChannelAfterTheLevelKeepsItsPlace()
        {
            var decoder = new SoundLevelDecoder((int)ShimmerBluetooth.ShimmerVersion.SHIMMER3R, SoundLevel, VBatt);
            Assert.AreEqual(3 + 2 + 2, decoder.Size);

            ObjectCluster ojc = decoder.Decode(Packet(0x000010, 9044, 2861));
            Assert.AreEqual(2861.0, ojc.GetData(Shimmer3Configuration.SignalNames.V_SENSE_BATT, Raw).Data);
            Assert.AreEqual(90.44, ojc.GetData(Name, Cal).Data, 1e-12);
        }

        /// <summary>
        /// Three packets a Shimmer3R streamed over classic Bluetooth at 1024 Hz, with every sensor and
        /// the sound level on (23 channels, 51 bytes after the packet type). The firmware's own test
        /// rig decoded no level, then 49.41 dB(A) twice, from the same bytes.
        /// </summary>
        [TestMethod]
        public void TestPacketsFromAShimmer3R()
        {
            byte[] channels = { 0x07, 0x08, 0x09, 0x0A, 0x0B, 0x0C, 0x00, 0x01, 0x02, 0x1B, 0x1A, 0x14, 0x15, 0x16, 0x04, 0x05, 0x06, 0x17, 0x18, 0x19, 0x38, 0x1C, 0x03 };
            string[] packets =
            {
                "3c49a82e004c0010fdc9ff2f00010097f3e13cb4ef009d5e00848001200040ffe0ccc408f1fcf200000000000000807c0a330b",
                "5c49a82b004d0012fdabff2000150077f3f83c85ef009d5e0084800140fff00020b8c400f128f30000000000004d137f0a380b",
                "7c49a82b004d0012fda9ff28000b007bf3de3c89ef009d5e0084800120fff0fff0ccc4d8f004f30000000000004d13820a4c0b",
            };
            int[] rawLevels = { -32768, 4941, 4941 };
            int[] rawBattery = { 2867, 2872, 2892 };

            var decoder = new SoundLevelDecoder((int)ShimmerBluetooth.ShimmerVersion.SHIMMER3R, channels);
            Assert.AreEqual(51, decoder.Size);
            for (int i = 0; i < packets.Length; i++)
            {
                ObjectCluster ojc = decoder.Decode(Hex(packets[i]));
                Assert.AreEqual((double)rawLevels[i], ojc.GetData(Name, Raw).Data, "packet " + i);
                Assert.AreEqual(ShimmerBluetooth.CalibrateSoundLevel(rawLevels[i]), ojc.GetData(Name, Cal).Data, "packet " + i);
                Assert.AreEqual((double)rawBattery[i], ojc.GetData(Shimmer3Configuration.SignalNames.V_SENSE_BATT, Raw).Data, "packet " + i);
            }
        }

        /// <summary>
        /// A Shimmer3 never sends 0x38. If one did, it would stay an unnamed two-byte channel, as any
        /// unknown ID is.
        /// </summary>
        [TestMethod]
        public void TestShimmer3LeavesTheChannelUnnamed()
        {
            var decoder = new SoundLevelDecoder((int)ShimmerBluetooth.ShimmerVersion.SHIMMER3, SoundLevel, VBatt);
            Assert.AreEqual(3 + 2 + 2, decoder.Size);
            ObjectCluster ojc = decoder.Decode(Packet(0x000010, 4941, 2861));
            Assert.IsNull(ojc.GetData(Name, Raw));
            Assert.IsNull(ojc.GetData(Name, Cal));
        }

        /// <summary>
        /// A Shimmer3R SD log carries its channel list in the header (count at 314, IDs from 315), so
        /// the sound level is read from a file the same way.
        /// </summary>
        [TestMethod]
        public void TestShimmer3RSdLog()
        {
            var records = new List<byte>();
            records.AddRange(Packet(0x000100, ShimmerBluetooth.SOUND_LEVEL_NO_LEVEL, 2847));
            records.AddRange(Packet(0x000140, 9044, 2861));
            records.AddRange(Packet(0x000180, 9044, 2841));
            string filePath = WriteShimmer3RSdLog(new[] { SoundLevel, VBatt }, records.ToArray());
            try
            {
                AssertSdLog(filePath);
            }
            finally
            {
                GC.Collect();
                GC.WaitForPendingFinalizers();
                try
                {
                    File.Delete(filePath);
                }
                catch (IOException)
                {
                }
            }
        }

        // The ShimmerSDLog is created in a separate method so it is unreachable, and its file can be released, once the method returns
        private static void AssertSdLog(string filePath)
        {
            var sdLog = new ShimmerSDLog(filePath);
            double[] want = { double.NaN, 90.44, 90.44 };
            int[] battery = { 2847, 2861, 2841 };
            for (int i = 0; i < want.Length; i++)
            {
                ObjectCluster ojc = sdLog.ReadPacketMsg();
                Assert.IsNotNull(ojc, "record " + i);
                double cal = ojc.GetData(Name, Cal).Data;
                if (double.IsNaN(want[i]))
                {
                    Assert.IsTrue(double.IsNaN(cal), "record " + i + " read " + cal);
                }
                else
                {
                    Assert.AreEqual(want[i], cal, 1e-12, "record " + i);
                }
                Assert.AreEqual((double)battery[i], ojc.GetData(Shimmer3Configuration.SignalNames.V_SENSE_BATT, Raw).Data, "record " + i);
            }
        }

        /// <summary>A 3-byte timestamp, the level, then the battery, all little-endian.</summary>
        static byte[] Packet(int timestamp, int level, int battery)
        {
            return new byte[]
            {
                (byte)timestamp, (byte)(timestamp >> 8), (byte)(timestamp >> 16),
                (byte)level, (byte)(level >> 8),
                (byte)battery, (byte)(battery >> 8)
            };
        }

        static byte[] Hex(string hex)
        {
            var bytes = new byte[hex.Length / 2];
            for (int i = 0; i < bytes.Length; i++)
            {
                bytes[i] = Convert.ToByte(hex.Substring(2 * i, 2), 16);
            }
            return bytes;
        }

        /// <summary>A minimal Shimmer3R LogAndStream SD log: a 384 byte header, firmware v1.01.025, 256 Hz.</summary>
        static string WriteShimmer3RSdLog(byte[] channels, byte[] records)
        {
            byte[] header = new byte[384];
            header[0] = 0x80; // sampling rate 32768/128 = 256Hz
            header[31] = (byte)ShimmerBluetooth.ShimmerVersion.SHIMMER3R;
            header[35] = (byte)ShimmerBluetooth.FW_IDENTIFIER_LOGANDSTREAM;
            header[36] = 0;
            header[37] = 1;
            header[38] = 1;
            header[39] = 25;
            header[224] = 0xFF; // no pressure sensor ID recorded
            header[226] = 0x13; // audio mode 3, sound level on (bit 4); the decoder goes by the channel list
            header[314] = (byte)channels.Length;
            Array.Copy(channels, 0, header, 315, channels.Length);

            string filePath = Path.Combine(Path.GetTempPath(), "SoundLevelSdLogTest_" + Guid.NewGuid().ToString("N") + ".000");
            using (var fs = new FileStream(filePath, FileMode.CreateNew))
            {
                fs.Write(header, 0, header.Length);
                fs.Write(records, 0, records.Length);
            }
            return filePath;
        }
    }

    /// <summary>
    /// A ShimmerBluetooth with no connection behind it, set up as a device on firmware with 3-byte
    /// timestamps, so that a packet can be put through BuildMsg directly.
    /// </summary>
    class SoundLevelDecoder : ShimmerBluetooth
    {
        public SoundLevelDecoder(int hardwareVersion, params byte[] channels) : base("SoundLevelDecoder")
        {
            HardwareVersion = hardwareVersion;
            CompatibilityCode = 6;
            UpdateBasedOnCompatibilityCode();
            InterpretDataPacketFormat(channels.Length, channels);
        }

        /// <summary>Bytes in one packet after the packet type byte.</summary>
        public int Size => PacketSize;

        /// <summary>Decode one packet, given without its packet type byte.</summary>
        public ObjectCluster Decode(byte[] packet)
        {
            return BuildMsg(new List<byte>(packet));
        }

        public override string GetShimmerAddress()
        {
            return "SoundLevelDecoder";
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
            return false;
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
        }
    }
}
