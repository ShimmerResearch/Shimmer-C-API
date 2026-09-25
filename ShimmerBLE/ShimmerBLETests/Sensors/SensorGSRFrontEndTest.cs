using NUnit.Framework;
using shimmer.Sensors;
using ShimmerAPI;
using ShimmerBLETests.Communications;
using System;
using System.Linq;
using static shimmer.Models.OpConfigPayload;
using static ShimmerBLEAPI.Devices.VerisenseDevice;

namespace ShimmerBLETests
{
    /// <summary>
    /// Every GSR-capable Verisense except the GSR+ (SR62) carries the second-generation DC GSR front
    /// end: 21/150/562/1740 kOhm feedback resistors, a 0.4986 V amplifier reference and the
    /// GSR_UNCAL_LIMIT_RANGE3_SR68 range-3 clamp (DEV-1069). An SR61-5/6 arrives as VERISENSE_IMU,
    /// and keying those choices on VERISENSE_PULSE_PLUS alone decoded it with the Shimmer3 values
    /// instead: about 1.9 times high, and with no range-3 clamp.
    /// </summary>
    public class SensorGSRFrontEndTest
    {
        static readonly double[] Gen2FeedbackKOhms = { 21.0, 150.0, 562.0, 1740.0 };
        static readonly double[] Shimmer3FeedbackKOhms = { 40.2, 287.0, 1000.0, 3300.0 };

        /// <summary>
        /// The DEV-793 resistor sweep on an SR61-5: one load per range, and the conductance it was
        /// accepted at. The same values back the web SDK's DEV-874 test.
        /// </summary>
        static readonly (double LoadKOhms, int Range, double MicroSiemens)[] Sweep =
        {
            (33.0, 0, 30.3),
            (100.0, 1, 10.0),
            (470.0, 2, 2.13),
            (2700.0, 3, 0.37),
        };

        static readonly Sensor.SensorSetting[] FixedRanges =
        {
            SensorGSR.GSRRange.Range_0,
            SensorGSR.GSRRange.Range_1,
            SensorGSR.GSRRange.Range_2,
            SensorGSR.GSRRange.Range_3,
        };

        [Test]
        public void TestSR61DecodesTheResistorSweepInAutoRange()
        {
            var sensor = CreateSensor(HardwareIdentifier.VERISENSE_IMU, SensorGSR.GSRRange.Range_Auto);
            foreach (var (loadKOhms, range, microSiemens) in Sweep)
            {
                AssertDecodesLoad(sensor, range, Gen2Code(loadKOhms, range), loadKOhms, microSiemens, "auto-range");
            }
        }

        [Test]
        public void TestSR61DecodesTheResistorSweepOnFixedRanges()
        {
            foreach (var (loadKOhms, range, microSiemens) in Sweep)
            {
                var sensor = CreateSensor(HardwareIdentifier.VERISENSE_IMU, FixedRanges[range]);
                AssertDecodesLoad(sensor, range, Gen2Code(loadKOhms, range), loadKOhms, microSiemens, "fixed range " + range);
            }
        }

        /// <summary>
        /// A production config reporting an SR61-5 gives the device's GSR sensor the gen-2 front end.
        /// The identifier comes from the hardware major, which is how an SR61-5/6 reached the decode
        /// as VERISENSE_IMU in the first place.
        /// </summary>
        [Test]
        public void TestSR61ProductionConfigSelectsTheSecondGenerationFrontEnd()
        {
            var opConfig = (byte[])DefaultVerisenseConfiguration.GSR_Batt_Default_Setting.GetOperationalConfigurationBytes().Clone();
            // GSR alone, so that each sample is two bytes; the setting is already auto-range.
            opConfig[(int)ConfigurationBytesIndexName.GEN_CFG_2] &= 0b11111101;
            var device = new TestVerisenseBLEDevice("00000000-0000-0000-0000-daa619f04ad7", "device", opConfig, ProductionConfig(61, 5));
            var sensor = (SensorGSR)device.GetSensor(SensorGSR.SensorName);

            Assert.That(device.GetProductionConfig().HardwareIdentifier, Is.EqualTo(HardwareIdentifier.VERISENSE_IMU));
            Assert.That(sensor.GetGSRRange(), Is.EqualTo(SensorGSR.GSRRange.Range_Auto));
            foreach (var (loadKOhms, range, microSiemens) in Sweep)
            {
                AssertDecodesLoad(sensor, range, Gen2Code(loadKOhms, range), loadKOhms, microSiemens, "SR61-5 production config");
            }
        }

        /// <summary>
        /// Every board but the SR62 decodes every code on every range exactly as the SR68 does: the
        /// resistors, the reference and the range-3 clamp alike. That follows the firmware, which
        /// chooses on the hardware major alone, so it covers boards without GSR and a device whose
        /// production config has not been read as well. Comparing with the SR68 rather than with
        /// fixed numbers keeps this independent of the clamp's value, which DEV-1067 changes.
        /// </summary>
        [TestCase(HardwareIdentifier.VERISENSE_IMU)]
        [TestCase(HardwareIdentifier.VERISENSE_IMU_01)]
        [TestCase(HardwareIdentifier.VERISENSE_PPG)]
        [TestCase(HardwareIdentifier.VERISENSE_DEV_BOARD)]
        [TestCase(HardwareIdentifier.UNKNOWN)]
        public void TestDecodesEveryCodeAsTheSR68Does(HardwareIdentifier id)
        {
            var rangeSettings = new[] { SensorGSR.GSRRange.Range_Auto }.Concat(FixedRanges);
            foreach (var rangeSetting in rangeSettings)
            {
                var sensor = CreateSensor(id, rangeSetting);
                var sr68 = CreateSensor(HardwareIdentifier.VERISENSE_PULSE_PLUS, rangeSetting);
                // On a fixed range the firmware reports that range's resistor in the range bits.
                bool isAutoRange = rangeSetting.Equals(SensorGSR.GSRRange.Range_Auto);
                int firstRange = isAutoRange ? 0 : rangeSetting.GetConfigurationValue();
                int lastRange = isAutoRange ? 3 : rangeSetting.GetConfigurationValue();
                for (int range = firstRange; range <= lastRange; range++)
                {
                    for (int code = 0; code <= 4095; code++)
                    {
                        var actual = Decode(sensor, range, code);
                        var expected = Decode(sr68, range, code);
                        if (!actual.Equals(expected))
                        {
                            Assert.Fail(id + ", " + rangeSetting.GetDisplayName() + ", range " + range + ", code " + code
                                + ": " + actual + " but the SR68 reads " + expected);
                        }
                    }
                }
            }
        }

        /// <summary>
        /// The GSR+ (SR62) keeps the Shimmer3 values: 40.2/287/1000/3300 kOhm, a 0.5 V reference,
        /// the 3.0 V ADC reference and the range-3 clamp at 683.
        /// </summary>
        [Test]
        public void TestSR62KeepsTheShimmer3FrontEnd()
        {
            var sensor = CreateSensor(HardwareIdentifier.VERISENSE_GSR_PLUS, SensorGSR.GSRRange.Range_Auto);
            foreach (var (loadKOhms, range, microSiemens) in Sweep)
            {
                int code = (int)Math.Round(0.5 * (1 + Shimmer3FeedbackKOhms[range] / loadKOhms) * 4095 / 3.0);
                AssertDecodesLoad(sensor, range, code, loadKOhms, microSiemens, "SR62");
            }
            Assert.That(Decode(sensor, 3, 600), Is.EqualTo(Decode(sensor, 3, SensorGSR.GSR_UNCAL_LIMIT_RANGE3_SR62)), "SR62 range-3 clamp");
        }

        /// <summary>
        /// The 12-bit code a gen-2 front end produces for a resistive load: the amplifier output is
        /// 0.4986 V x (1 + Rf/R), read against the 1.8 V full scale (the web SDK's gen2AdcForLoad).
        /// </summary>
        private static int Gen2Code(double loadKOhms, int range)
        {
            return (int)Math.Round(0.4986 * (1 + Gen2FeedbackKOhms[range] / loadKOhms) * 4095 / 1.8);
        }

        private static SensorGSR CreateSensor(HardwareIdentifier id, Sensor.SensorSetting range)
        {
            var sensor = new SensorGSR();
            sensor.SetDeviceHardwareIdentifier(id);
            sensor.SetGSREnabled(true);
            sensor.SetGSRRange(range);
            return sensor;
        }

        private static (double KOhms, double MicroSiemens) Decode(SensorGSR sensor, int range, int code)
        {
            // One GSR sample as the firmware sends it: range in bits 15-14 over the 12-bit code, LSB first.
            int raw = (range << 14) | code;
            var ojc = sensor.ParseSensorData(new byte[] { (byte)(raw & 0xFF), (byte)(raw >> 8) }, new ObjectCluster("", ""));
            return (ojc.GetData(SensorGSR.ObjectClusterSensorName.GSR, ShimmerConfiguration.SignalFormats.CAL, ShimmerConfiguration.SignalUnits.KiloOhms).Data,
                ojc.GetData(SensorGSR.ObjectClusterSensorName.GSR, ShimmerConfiguration.SignalFormats.CAL, ShimmerConfiguration.SignalUnits.MicroSiemens).Data);
        }

        private static void AssertDecodesLoad(SensorGSR sensor, int range, int code, double loadKOhms, double microSiemens, string context)
        {
            var (kOhms, uS) = Decode(sensor, range, code);
            string label = context + ": " + loadKOhms + " kOhm on range " + range + " (code " + code + ")";
            // Within 1% of the load, which is the ADC quantisation and nothing else.
            Assert.That(kOhms, Is.EqualTo(loadKOhms).Within(1).Percent, label);
            Assert.That(uS, Is.EqualTo(microSiemens).Within(0.05), label);
        }

        /// <summary>
        /// A production config response laid out as in VerisenseProdConfigTest: header, length 55,
        /// config header, the ASM ID, then the hardware and firmware revisions, with the passkey and
        /// advertising-name fields unset.
        /// </summary>
        private static byte[] ProductionConfig(byte hwMajor, byte hwMinor)
        {
            var bytes = new byte[] { 0x33, 0x37, 0x00, 0x5A, 0xBB, 0xA2, 0x01, 0x25, 0x09, 0x19, hwMajor, hwMinor, 0x01, 0x02, 0x63, 0x00 };
            return bytes.Concat(Enumerable.Repeat((byte)0xFF, 42)).ToArray();
        }
    }
}
