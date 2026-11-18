using System;
using System.Collections.Generic;
using System.IO;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using ShimmerAPI;

namespace ShimmerBluetoothTests
{
    /// <summary>
    /// The Shimmer3's alternative accelerometer (channels 0x14-0x16) and alternative magnetometer
    /// (0x17-0x19), DEV-1452. On a Shimmer3 they are the MPU-9150/9250 or ICM-20948 accelerometer,
    /// int16 big-endian, and its AK8975/AK8963/AK09916 magnetometer, int16 little-endian, two bytes
    /// each, after the magnetometer and before the pressure sensor. The decoder used to give these
    /// channels nothing on a Shimmer3, so a packet with either sensor on read six bytes short per
    /// sensor and every stream with them was mis-framed from the first packet. The API holds no
    /// Shimmer3 calibration for them, so they are output RAW only. The Shimmer3R's high-g
    /// accelerometer and alternative magnetometer use the same channel IDs and decode as before.
    /// Each Bluetooth case goes through InterpretDataPacketFormat and BuildMsg, and the SD case
    /// through ShimmerSDLog.
    /// </summary>
    [TestClass]
    public class ShimmerAltAccelMagDecodeTest
    {
        static readonly int Shimmer3 = (int)ShimmerBluetooth.ShimmerVersion.SHIMMER3;
        static readonly int Shimmer3R = (int)ShimmerBluetooth.ShimmerVersion.SHIMMER3R;

        static readonly string Raw = ShimmerConfiguration.SignalFormats.RAW;
        static readonly string Cal = ShimmerConfiguration.SignalFormats.CAL;

        static readonly string[] AltAccel =
        {
            Shimmer3Configuration.SignalNames.ALTERNATIVE_ACCELEROMETER_X,
            Shimmer3Configuration.SignalNames.ALTERNATIVE_ACCELEROMETER_Y,
            Shimmer3Configuration.SignalNames.ALTERNATIVE_ACCELEROMETER_Z
        };
        static readonly string[] AltMag =
        {
            Shimmer3Configuration.SignalNames.ALT_MAGNETOMETER_X,
            Shimmer3Configuration.SignalNames.ALT_MAGNETOMETER_Y,
            Shimmer3Configuration.SignalNames.ALT_MAGNETOMETER_Z
        };
        static readonly string[] HighGAccel =
        {
            Shimmer3Configuration.SignalNames.HIGH_G_ACCELEROMETER_X,
            Shimmer3Configuration.SignalNames.HIGH_G_ACCELEROMETER_Y,
            Shimmer3Configuration.SignalNames.HIGH_G_ACCELEROMETER_Z
        };
        static readonly string[] LnAccel =
        {
            Shimmer3Configuration.SignalNames.LOW_NOISE_ACCELEROMETER_X,
            Shimmer3Configuration.SignalNames.LOW_NOISE_ACCELEROMETER_Y,
            Shimmer3Configuration.SignalNames.LOW_NOISE_ACCELEROMETER_Z
        };
        static readonly string[] Gyro =
        {
            Shimmer3Configuration.SignalNames.GYROSCOPE_X,
            Shimmer3Configuration.SignalNames.GYROSCOPE_Y,
            Shimmer3Configuration.SignalNames.GYROSCOPE_Z
        };
        static readonly string[] WrAccel =
        {
            Shimmer3Configuration.SignalNames.WIDE_RANGE_ACCELEROMETER_X,
            Shimmer3Configuration.SignalNames.WIDE_RANGE_ACCELEROMETER_Y,
            Shimmer3Configuration.SignalNames.WIDE_RANGE_ACCELEROMETER_Z
        };
        static readonly string[] Mag =
        {
            Shimmer3Configuration.SignalNames.MAGNETOMETER_X,
            Shimmer3Configuration.SignalNames.MAGNETOMETER_Y,
            Shimmer3Configuration.SignalNames.MAGNETOMETER_Z
        };

        /// <summary>The alt accel and alt mag enabled-sensor bits, the same on both generations.</summary>
        static readonly long AltSensorBits = (long)ShimmerBluetooth.SensorBitmapShimmer3.SENSOR_ACCEL_ALT | (long)ShimmerBluetooth.SensorBitmapShimmer3.SENSOR_MAG_ALT;

        static readonly byte[] LnAccelIds = { 0x00, 0x01, 0x02 };
        static readonly byte[] AltAccelIds = { 0x14, 0x15, 0x16 };
        static readonly byte[] AltMagIds = { 0x17, 0x18, 0x19 };
        static readonly byte[] BmpIds = { 0x1A, 0x1B };

        /// <summary>
        /// Every IMU channel a Shimmer3 has, in the order the firmware packs them: LN accel, VBATT,
        /// gyro, WR accel, mag, alt accel, alt mag. 19 channels, 38 data bytes.
        /// </summary>
        static readonly byte[] AllImuIds =
        {
            0x00, 0x01, 0x02, 0x03, 0x0A, 0x0B, 0x0C, 0x04, 0x05, 0x06, 0x07, 0x08, 0x09,
            0x14, 0x15, 0x16, 0x17, 0x18, 0x19
        };

        /// <summary>
        /// One packet for <see cref="AllImuIds"/> from an ICM-20948 board, without its packet type
        /// byte. The alt accel carries the same chip reading as the WR accel, which the firmware
        /// byte-swaps to little-endian and whose Y it negates; the alt mag carries the same reading
        /// as the mag, whose Z it negates.
        /// </summary>
        const string AllImuPacket =
            "102030" +       // timestamp
            "0008ff070108" + // LN accel LE 2048, 2047, 2049
            "8c0a" +         // VBATT LE 2700
            "0064ff9c0000" + // gyro BE 100, -100, 0
            "d204d2040040" + // WR accel LE 1234, 1234, 16384
            "230110ff00fe" + // mag LE 291, -240, -512
            "04d2fb2e4000" + // alt accel BE 1234, -1234, 16384
            "230110ff0002";  // alt mag LE 291, -240, 512

        static readonly int[] AltAccelValues = { 1234, -1234, 16384 };
        static readonly int[] AltMagValues = { 291, -240, 512 };

        /// <summary>
        /// A Shimmer3 on an SR31-9-1 IMU board, which has no LSM303 fitted: the ICM-20948 serves as
        /// the WR accel and mag, as in the fixture. The decoder reads the mag of a board this recent
        /// little-endian.
        /// </summary>
        static AltSensorDecoder Shimmer3Decoder(byte[] channels)
        {
            return new AltSensorDecoder(Shimmer3, channels, (int)ShimmerBluetooth.ExpansionBoardDetectShimmer3.SHIMMER3, 9, 1);
        }

        [TestMethod]
        public void TestShimmer3PacketWithEveryImuChannelIsFramedAt41Bytes()
        {
            var decoder = Shimmer3Decoder(AllImuIds);
            Assert.AreEqual(41, Hex(AllImuPacket).Length, "fixture");
            // 41 after the packet type byte, so 42 on the wire
            Assert.AreEqual(3 + 38, decoder.Size);
            Assert.AreEqual(42, decoder.Size + 1);
        }

        [TestMethod]
        public void TestShimmer3AltAccelAndMagDecodeRawOnly()
        {
            var decoder = Shimmer3Decoder(AllImuIds);
            ObjectCluster ojc = decoder.Decode(Hex(AllImuPacket));

            AssertRaw(ojc, AltAccel, AltAccelValues);
            AssertRaw(ojc, AltMag, AltMagValues);
            foreach (string name in Concat(AltAccel, AltMag))
            {
                Assert.IsNull(ojc.GetData(name, Cal), name + " has no Shimmer3 calibration, so no CAL");
            }
            foreach (string name in HighGAccel)
            {
                Assert.IsNull(ojc.GetData(name, Raw), name + " is the Shimmer3R's sensor");
                Assert.IsNull(ojc.GetData(name, Cal), name + " is the Shimmer3R's sensor");
            }

            // The channels before them still read their own bytes
            AssertRaw(ojc, LnAccel, new[] { 2048, 2047, 2049 });
            Assert.AreEqual(2700.0, ojc.GetData(Shimmer3Configuration.SignalNames.V_SENSE_BATT, Raw).Data);
            AssertRaw(ojc, Gyro, new[] { 100, -100, 0 });
            AssertRaw(ojc, WrAccel, new[] { 1234, 1234, 16384 });
            AssertRaw(ojc, Mag, new[] { 291, -240, -512 });
        }

        /// <summary>
        /// On an ICM-20948 board the alt accel is the chip the WR accel reads, before the firmware's
        /// byte swap and Y negation, and the alt mag is the mag before its Z negation. So the right
        /// byte order gives the same numbers on both.
        /// </summary>
        [TestMethod]
        public void TestShimmer3AltSensorsAgreeWithTheChannelsReadFromTheSameChip()
        {
            var decoder = Shimmer3Decoder(AllImuIds);
            ObjectCluster ojc = decoder.Decode(Hex(AllImuPacket));

            Assert.AreEqual(RawOf(ojc, WrAccel[0]), RawOf(ojc, AltAccel[0]));
            Assert.AreEqual(RawOf(ojc, WrAccel[1]), -RawOf(ojc, AltAccel[1]));
            Assert.AreEqual(RawOf(ojc, WrAccel[2]), RawOf(ojc, AltAccel[2]));
            Assert.AreEqual(RawOf(ojc, Mag[0]), RawOf(ojc, AltMag[0]));
            Assert.AreEqual(RawOf(ojc, Mag[1]), RawOf(ojc, AltMag[1]));
            Assert.AreEqual(RawOf(ojc, Mag[2]), -RawOf(ojc, AltMag[2]));
        }

        [TestMethod]
        public void TestShimmer3AltAccelAloneAddsSixBytes()
        {
            var decoder = Shimmer3Decoder(Concat(LnAccelIds, AltAccelIds));
            Assert.AreEqual(3 + 6 + 6, decoder.Size);

            ObjectCluster ojc = decoder.Decode(Hex("102030" + "0008ff070108" + "04d2fb2e4000"));
            AssertRaw(ojc, AltAccel, AltAccelValues);
            AssertRaw(ojc, LnAccel, new[] { 2048, 2047, 2049 });
            foreach (string name in AltAccel)
            {
                Assert.IsNull(ojc.GetData(name, Cal), name);
            }
            foreach (string name in AltMag)
            {
                Assert.IsNull(ojc.GetData(name, Raw), name + " is off");
            }
        }

        [TestMethod]
        public void TestShimmer3AltMagAloneAddsSixBytes()
        {
            var decoder = Shimmer3Decoder(Concat(LnAccelIds, AltMagIds));
            Assert.AreEqual(3 + 6 + 6, decoder.Size);

            ObjectCluster ojc = decoder.Decode(Hex("102030" + "0008ff070108" + "230110ff0002"));
            AssertRaw(ojc, AltMag, AltMagValues);
            AssertRaw(ojc, LnAccel, new[] { 2048, 2047, 2049 });
            foreach (string name in AltMag)
            {
                Assert.IsNull(ojc.GetData(name, Cal), name);
            }
            foreach (string name in AltAccel)
            {
                Assert.IsNull(ojc.GetData(name, Raw), name + " is off");
            }
        }

        [TestMethod]
        public void TestShimmer3BothAltSensorsAddTwelveBytes()
        {
            var decoder = Shimmer3Decoder(Concat(LnAccelIds, AltAccelIds, AltMagIds));
            Assert.AreEqual(3 + 6 + 12, decoder.Size);

            ObjectCluster ojc = decoder.Decode(Hex("102030" + "0008ff070108" + "04d2fb2e4000" + "230110ff0002"));
            AssertRaw(ojc, AltAccel, AltAccelValues);
            AssertRaw(ojc, AltMag, AltMagValues);
        }

        /// <summary>
        /// The pressure sensor follows the alt sensors in a Shimmer3 packet (temperature u16
        /// big-endian, pressure u24 big-endian), and is read from its own bytes.
        /// </summary>
        [TestMethod]
        public void TestShimmer3ChannelAfterTheAltSensorsKeepsItsPlace()
        {
            var decoder = Shimmer3Decoder(Concat(AltAccelIds, AltMagIds, BmpIds));
            Assert.AreEqual(3 + 12 + 5, decoder.Size);

            ObjectCluster ojc = decoder.Decode(Hex("102030" + "04d2fb2e4000" + "230110ff0002" + "6a5b" + "7b8c9d"));
            AssertRaw(ojc, AltAccel, AltAccelValues);
            AssertRaw(ojc, AltMag, AltMagValues);
            Assert.AreEqual((double)0x6A5B, ojc.GetData(Shimmer3Configuration.SignalNames.TEMPERATURE, Raw).Data);
            Assert.AreEqual((double)0x7B8C9D, ojc.GetData(Shimmer3Configuration.SignalNames.PRESSURE, Raw).Data);
        }

        [TestMethod]
        public void TestShimmer3AltSensorsSetTheirEnabledSensorBits()
        {
            var decoder = Shimmer3Decoder(Concat(AltAccelIds, AltMagIds));
            Assert.AreEqual(AltSensorBits, decoder.GetEnabledSensors());
            CollectionAssert.AreEqual(new[] { "i16r", "i16r", "i16r", "i16", "i16", "i16" }, decoder.DataTypes(6));
        }

        /// <summary>
        /// A Shimmer3's alt sensors are uncalibrated, so they must not stand in for the accel or mag
        /// that the orientation filter needs.
        /// </summary>
        [TestMethod]
        public void TestShimmer3AltSensorsDoNotFeedTheOrientation()
        {
            var decoder = Shimmer3Decoder(Concat(new byte[] { 0x0A, 0x0B, 0x0C }, AltAccelIds, AltMagIds));
            decoder.Set3DOrientation(true);
            ObjectCluster ojc = decoder.Decode(Hex("102030" + "0064ff9c0000" + "04d2fb2e4000" + "230110ff0002"));
            Assert.IsNull(ojc.GetData(Shimmer3Configuration.SignalNames.QUATERNION_0, Cal));
            AssertRaw(ojc, AltAccel, AltAccelValues);
        }

        /// <summary>
        /// The Shimmer3R's high-g accelerometer (ADXL371, 12-bit big-endian left-justified) and
        /// alternative magnetometer (LIS3MDL, int16 little-endian) on the same channel IDs decode as
        /// they did, calibrated.
        /// </summary>
        [TestMethod]
        public void TestShimmer3RHighGAccelAndAltMagDecodeAsBefore()
        {
            var decoder = new AltSensorDecoder(Shimmer3R, Concat(AltAccelIds, AltMagIds));
            Assert.AreEqual(3 + 12, decoder.Size);
            CollectionAssert.AreEqual(new[] { "i12*>", "i12*>", "i12*>", "i16", "i16", "i16" }, decoder.DataTypes(6));
            Assert.AreEqual(AltSensorBits, decoder.GetEnabledSensors());

            // 0x123 = 291, 0xFED = -19, 0x7FF = 2047, as 12 bits in the top of a big-endian word
            ObjectCluster ojc = decoder.Decode(Hex("102030" + "1230" + "fed0" + "7ff0" + "230110ff0002"));
            AssertRaw(ojc, HighGAccel, new[] { 291, -19, 2047 });
            AssertRaw(ojc, AltMag, AltMagValues);
            foreach (string name in Concat(HighGAccel, AltMag))
            {
                Assert.IsNotNull(ojc.GetData(name, Cal), name + " is calibrated on a Shimmer3R");
            }
            foreach (string name in AltAccel)
            {
                Assert.IsNull(ojc.GetData(name, Raw), name + " is the Shimmer3's sensor");
            }
        }

        /// <summary>On a Shimmer3R the high-g accel and alt mag still count toward the orientation.</summary>
        [TestMethod]
        public void TestShimmer3RAltSensorsStillFeedTheOrientation()
        {
            var decoder = new AltSensorDecoder(Shimmer3R, Concat(new byte[] { 0x0A, 0x0B, 0x0C }, AltAccelIds, AltMagIds));
            decoder.Set3DOrientation(true);
            ObjectCluster ojc = decoder.Decode(Hex("102030" + "640000000000" + "1230" + "fed0" + "7ff0" + "230110ff0002"));
            Assert.IsNotNull(ojc.GetData(Shimmer3Configuration.SignalNames.QUATERNION_0, Cal));
        }

        /// <summary>
        /// A Shimmer3 SD file has no channel list: the reader builds one from the header's
        /// enabled-sensors bits, where the alt accel is bit 22 and the alt mag bit 21. The firmware
        /// writes each record as the packet it streams, so they sit after the mag and before the
        /// pressure sensor.
        /// </summary>
        [TestMethod]
        public void TestShimmer3SdLog()
        {
            string row = "0008ff070108" + "04d2fb2e4000" + "230110ff0002" + "6a5b" + "7b8c9d";
            var records = new List<byte>();
            for (int i = 0; i < 3; i++)
            {
                int ticks = 0x001000 + (i * 0x40);
                records.AddRange(new[] { (byte)ticks, (byte)(ticks >> 8), (byte)(ticks >> 16) });
                records.AddRange(Hex(row));
            }
            int enabledSensors = ShimmerSDLog.SDLogHeader.ACCEL_LN | ShimmerSDLog.SDLogHeader.ACCEL_MPU
                | ShimmerSDLog.SDLogHeader.MAG_MPU | ShimmerSDLog.SDLogHeader.BMPX80;
            string path = WriteShimmer3SdLog(enabledSensors, records.ToArray());
            try
            {
                AssertShimmer3SdLog(path);
            }
            finally
            {
                GC.Collect();
                GC.WaitForPendingFinalizers();
                try
                {
                    Directory.Delete(Path.GetDirectoryName(path), true);
                }
                catch (IOException)
                {
                    // A temp file left behind must not fail a test
                }
            }
        }

        // The ShimmerSDLog is created in a separate method so it is unreachable, and its file can be released, once the method returns
        private static void AssertShimmer3SdLog(string path)
        {
            var sdLog = new ShimmerSDLog(path);
            for (int i = 0; i < 3; i++)
            {
                ObjectCluster ojc = sdLog.ReadPacketMsg();
                Assert.IsNotNull(ojc, "record " + i);
                AssertRaw(ojc, LnAccel, new[] { 2048, 2047, 2049 });
                AssertRaw(ojc, AltAccel, AltAccelValues);
                AssertRaw(ojc, AltMag, AltMagValues);
                foreach (string name in Concat(AltAccel, AltMag))
                {
                    Assert.IsNull(ojc.GetData(name, Cal), name + " record " + i);
                }
                foreach (string name in HighGAccel)
                {
                    Assert.IsNull(ojc.GetData(name, Raw), name + " record " + i);
                }
                Assert.AreEqual((double)0x6A5B, ojc.GetData(Shimmer3Configuration.SignalNames.TEMPERATURE, Raw).Data, "record " + i);
                Assert.AreEqual((double)0x7B8C9D, ojc.GetData(Shimmer3Configuration.SignalNames.PRESSURE, Raw).Data, "record " + i);
            }
        }

        /// <summary>A minimal Shimmer3 LogAndStream SD log: a 256 byte header, firmware v1.01.003, no sync.</summary>
        static string WriteShimmer3SdLog(int enabledSensors, byte[] records)
        {
            byte[] h = new byte[256];
            h[0] = 0x80; // sampling rate 32768/128 = 256 Hz
            h[2] = 1; // buffer size
            // 3-7: enabled sensors
            h[3] = (byte)enabledSensors;
            h[4] = (byte)(enabledSensors >> 8);
            h[5] = (byte)(enabledSensors >> 16);
            h[31] = (byte)ShimmerBluetooth.ShimmerVersion.SHIMMER3;
            h[35] = (byte)ShimmerBluetooth.FW_IDENTIFIER_LOGANDSTREAM;
            h[37] = 1;
            h[38] = 1;
            h[39] = 3;
            // 214-216: expansion board, SR31-9-1
            h[214] = (byte)ShimmerBluetooth.ExpansionBoardDetectShimmer3.SHIMMER3;
            h[215] = 9;
            h[216] = 1;

            // The reader takes the last three characters of the path as the SD file number
            string dir = Path.Combine(Path.GetTempPath(), "shimmer_sdlog_alt_sensors_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(dir);
            string path = Path.Combine(dir, "000");
            using (var fs = new FileStream(path, FileMode.CreateNew))
            {
                fs.Write(h, 0, h.Length);
                fs.Write(records, 0, records.Length);
            }
            return path;
        }

        static void AssertRaw(ObjectCluster ojc, string[] names, int[] values)
        {
            for (int i = 0; i < names.Length; i++)
            {
                SensorData data = ojc.GetData(names[i], Raw);
                Assert.IsNotNull(data, names[i] + " RAW missing");
                Assert.AreEqual((double)values[i], data.Data, names[i]);
            }
        }

        static double RawOf(ObjectCluster ojc, string name)
        {
            SensorData data = ojc.GetData(name, Raw);
            Assert.IsNotNull(data, name + " RAW missing");
            return data.Data;
        }

        static T[] Concat<T>(params T[][] parts)
        {
            var all = new List<T>();
            foreach (T[] part in parts)
            {
                all.AddRange(part);
            }
            return all.ToArray();
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
    }

    /// <summary>
    /// A ShimmerBluetooth with no connection behind it, set up as a device on firmware with 3-byte
    /// timestamps, so that a packet can be put through BuildMsg directly.
    /// </summary>
    class AltSensorDecoder : ShimmerBluetooth
    {
        public AltSensorDecoder(int hardwareVersion, byte[] channels) : this(hardwareVersion, channels, 0, 0, 0)
        {
        }

        public AltSensorDecoder(int hardwareVersion, byte[] channels, int expansionBoardId, int expansionBoardRev, int expansionBoardRevSpecial) : base("AltSensorDecoder")
        {
            HardwareVersion = hardwareVersion;
            ExpansionBoardId = expansionBoardId;
            ExpansionBoardRev = expansionBoardRev;
            ExpansionBoardRevSpecial = expansionBoardRevSpecial;
            CompatibilityCode = 6;
            UpdateBasedOnCompatibilityCode();
            InterpretDataPacketFormat(channels.Length, channels);
        }

        /// <summary>Bytes in one packet after the packet type byte.</summary>
        public int Size => PacketSize;

        /// <summary>The data types of the last <paramref name="count"/> channels.</summary>
        public string[] DataTypes(int count)
        {
            int channels = 0;
            while (SignalDataTypeArray[channels + 1] != null)
            {
                channels++;
            }
            var types = new string[count];
            Array.Copy(SignalDataTypeArray, channels + 1 - count, types, 0, count);
            return types;
        }

        /// <summary>Decode one packet, given without its packet type byte.</summary>
        public ObjectCluster Decode(byte[] packet)
        {
            return BuildMsg(new List<byte>(packet));
        }

        public override string GetShimmerAddress()
        {
            return "AltSensorDecoder";
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
