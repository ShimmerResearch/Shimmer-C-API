using NUnit.Framework;
using shimmer.Sensors;
using ShimmerAPI;
using static ShimmerBLEAPI.Devices.VerisenseDevice;

namespace ShimmerBLETests
{
    /// <summary>
    /// A GSR code below the amplifier reference is an open circuit on every range, and has to decode
    /// as one (DEV-1070). No skin resistance can pull the amplifier's output under its reference, so
    /// the equation goes negative there. Range 3 has long raised such codes to its open-circuit
    /// limit. Ranges 0-2 did not, and in auto-range they see them: when the electrodes come off, the
    /// device climbs one range at a time and repeats the sample that triggered each switch for 80 ms,
    /// tagged with the range it was measured on. The auto-range nudge floored the negative resistance
    /// at 8 kOhm, so an open circuit read 125 uS and counted as Connected. DEV-793 dataset B6 (ASM_PC
    /// Test_056, an SR68-9) has such samples on range 0 at codes 0 and 1131, range 1 at 1131 and
    /// 1137, and range 2 at 1132.
    /// </summary>
    public class SensorGSRBelowReferenceTest
    {
        static readonly double[] WindowMaxKOhms = { 63.0, 220.0, 680.0, 4700.0 };
        static readonly Sensor.SensorSetting[] FixedRanges =
        {
            SensorGSR.GSRRange.Range_0, SensorGSR.GSRRange.Range_1, SensorGSR.GSRRange.Range_2, SensorGSR.GSRRange.Range_3
        };

        /// <summary>
        /// Test_056's codes, and 1135-1137: above the 0.4986 V reference this API divides by, but
        /// below the 0.5 V that sets the limit, so they decode as open too, as they already do on range 3.
        /// </summary>
        static readonly int[] SR68CodesBelowLimit = { 0, 1131, 1132, 1134, 1135, 1136, 1137 };
        static readonly int[] SR62CodesBelowLimit = { 0, 682 };

        [Test]
        public void TestSR68BelowLimitReadsOpenOnEveryRangeInAutoRange()
        {
            AssertAutoRangeReadsOpen(HardwareIdentifier.VERISENSE_PULSE_PLUS, SensorGSR.GSR_UNCAL_LIMIT_RANGE3_SR68, SR68CodesBelowLimit);
        }

        [Test]
        public void TestSR62BelowLimitReadsOpenOnEveryRangeInAutoRange()
        {
            AssertAutoRangeReadsOpen(HardwareIdentifier.VERISENSE_GSR_PLUS, SensorGSR.GSR_UNCAL_LIMIT_RANGE3_SR62, SR62CodesBelowLimit);
        }

        /// <summary>
        /// A fixed range still clamps to its own window (DEV-1068), but an open circuit now pins it to
        /// the top of the window, as fixed range 3 already did, instead of the bottom.
        /// </summary>
        [Test]
        public void TestFixedRangesReadTheTopOfTheirWindow()
        {
            for (int range = 0; range <= 3; range++)
            {
                AssertFixedRangeReads(HardwareIdentifier.VERISENSE_PULSE_PLUS, range, SR68CodesBelowLimit, WindowMaxKOhms[range]);
                AssertFixedRangeReads(HardwareIdentifier.VERISENSE_GSR_PLUS, range, SR62CodesBelowLimit, WindowMaxKOhms[range]);
            }
        }

        /// <summary>Every code from the limit up decodes on its own range, exactly as before.</summary>
        [Test]
        public void TestCodesAtOrAboveTheLimitDecodeAsBefore()
        {
            AssertCodesFromLimitDecodeAsBefore(HardwareIdentifier.VERISENSE_PULSE_PLUS, SensorGSR.GSR_UNCAL_LIMIT_RANGE3_SR68);
            AssertCodesFromLimitDecodeAsBefore(HardwareIdentifier.VERISENSE_GSR_PLUS, SensorGSR.GSR_UNCAL_LIMIT_RANGE3_SR62);
        }

        private static void AssertAutoRangeReadsOpen(HardwareIdentifier hardware, int limit, int[] codes)
        {
            var sensor = CreateSensor(hardware, SensorGSR.GSRRange.Range_Auto);
            double openKOhms = sensor.CalibrateGsrDataToKOhmsUsingAmplifierEq(CalibrateADCValueToVolts(limit, hardware), 3);
            for (int range = 0; range <= 3; range++)
            {
                foreach (int code in codes)
                {
                    string context = hardware + " auto-range, range " + range + ", code " + code;
                    var ojc = ParseSample(sensor, range, code);
                    double kOhms = ojc.GetData(SensorGSR.ObjectClusterSensorName.GSR, ShimmerConfiguration.SignalFormats.CAL, ShimmerConfiguration.SignalUnits.KiloOhms).Data;
                    double uS = ojc.GetData(SensorGSR.ObjectClusterSensorName.GSR, ShimmerConfiguration.SignalFormats.CAL, ShimmerConfiguration.SignalUnits.MicroSiemens).Data;

                    Assert.That(kOhms, Is.EqualTo(openKOhms), context);
                    Assert.That(uS, Is.GreaterThan(0).And.LessThan(SensorGSR.LIMIT_FOR_MINIMUM_VALID_GSR_CONDUCTANCE_US), context);
                    Assert.That(sensor.GetGSRConnectivityLevel(), Is.EqualTo(SensorGSR.GSRConnectivityLevel.Disconnected), context);
                }
            }
        }

        private static void AssertFixedRangeReads(HardwareIdentifier hardware, int range, int[] codes, double expectedKOhms)
        {
            var sensor = CreateSensor(hardware, FixedRanges[range]);
            foreach (int code in codes)
            {
                var ojc = ParseSample(sensor, range, code);
                double kOhms = ojc.GetData(SensorGSR.ObjectClusterSensorName.GSR, ShimmerConfiguration.SignalFormats.CAL, ShimmerConfiguration.SignalUnits.KiloOhms).Data;
                Assert.That(kOhms, Is.EqualTo(expectedKOhms), hardware + " fixed range " + range + ", code " + code);
            }
        }

        private static void AssertCodesFromLimitDecodeAsBefore(HardwareIdentifier hardware, int limit)
        {
            var sensor = CreateSensor(hardware, SensorGSR.GSRRange.Range_Auto);
            for (int range = 0; range <= 3; range++)
            {
                for (int code = limit; code <= 4095; code++)
                {
                    double equation = sensor.CalibrateGsrDataToKOhmsUsingAmplifierEq(CalibrateADCValueToVolts(code, hardware), range);
                    var ojc = ParseSample(sensor, range, code);
                    double kOhms = ojc.GetData(SensorGSR.ObjectClusterSensorName.GSR, ShimmerConfiguration.SignalFormats.CAL, ShimmerConfiguration.SignalUnits.KiloOhms).Data;
                    string context = hardware + " auto-range, range " + range + ", code " + code;
                    // Range 0 at full scale is already above 8 kOhm, so the auto-range floor changes nothing
                    Assert.That(equation, Is.GreaterThan(8.0), context);
                    Assert.That(kOhms, Is.EqualTo(equation), context);
                }
            }
        }

        private static SensorGSR CreateSensor(HardwareIdentifier hardware, Sensor.SensorSetting range)
        {
            var sensor = new SensorGSR();
            sensor.SetDeviceHardwareIdentifier(hardware);
            sensor.SetGSREnabled(true);
            sensor.SetGSRRange(range);
            return sensor;
        }

        /// <summary>One GSR sample as the firmware sends it: range in bits 15-14 over the 12-bit code, LSB first.</summary>
        private static ObjectCluster ParseSample(SensorGSR sensor, int range, int code)
        {
            int raw = (range << 14) | code;
            return sensor.ParseSensorData(new byte[] { (byte)(raw & 0xFF), (byte)(raw >> 8) }, new ObjectCluster("", ""));
        }
    }
}
