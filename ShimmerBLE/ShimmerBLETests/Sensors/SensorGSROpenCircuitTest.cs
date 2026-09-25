using NUnit.Framework;
using shimmer.Sensors;
using ShimmerAPI;
using static ShimmerBLEAPI.Devices.VerisenseDevice;

namespace ShimmerBLETests
{
    /// <summary>
    /// An open circuit on GSR range 3 of a Verisense Pulse+ (SR68) has to decode as open (DEV-1067).
    /// With nothing across the electrodes the amplifier output settles on its reference, so the ADC
    /// reads a few codes either side of it: an SR68-9 open-circuit recording (DEV-793 dataset B6)
    /// peaks at codes 1126-1136. The range-3 limit was 1134, itself below the 0.4986 V reference
    /// (code 1134.3), so every clamped sample decoded to a negative resistance, which the auto-range
    /// nudge floored at 8 kOhm: an open circuit read 125 uS and counted as Connected.
    /// </summary>
    public class SensorGSROpenCircuitTest
    {
        const int Range3 = 3;
        /// <summary>Top of range 3, 4.7 MOhm - an open circuit should read at least this.</summary>
        const double Range3MaxKOhms = 4700.0;

        [Test]
        public void TestOpenCircuitOnRange3ReadsOpenInAutoRange()
        {
            var sensor = CreateSR68Sensor(SensorGSR.GSRRange.Range_Auto);
            for (int code = 1134; code <= 1138; code++)
            {
                AssertReadsOpen(sensor, code, "auto-range");
                Assert.That(sensor.GetGSRConnectivityLevel(), Is.EqualTo(SensorGSR.GSRConnectivityLevel.Disconnected), "auto-range, code " + code);
            }
        }

        /// <summary>
        /// A fixed range pins the reading to that range's limits, so here an open circuit reads
        /// 4.7 MOhm (0.213 uS, still above the 0.03 uS connectivity threshold) rather than the
        /// 680 kOhm bottom of the range that 1134 produced.
        /// </summary>
        [Test]
        public void TestOpenCircuitOnRange3ReadsOpenInFixedRange3()
        {
            var sensor = CreateSR68Sensor(SensorGSR.GSRRange.Range_3);
            for (int code = 1134; code <= 1138; code++)
            {
                AssertReadsOpen(sensor, code, "fixed range 3");
            }
        }

        /// <summary>
        /// The Java driver divides by 0.5 V where this API uses 0.4986 V, so the limit is the first
        /// code above 0.5 V: correct under both references.
        /// </summary>
        [Test]
        public void TestLimitIsTheFirstCodeAboveHalfAVolt()
        {
            int limit = SensorGSR.GSR_UNCAL_LIMIT_RANGE3_SR68;
            Assert.That(CalibrateADCValueToVolts(limit, HardwareIdentifier.VERISENSE_PULSE_PLUS), Is.GreaterThan(0.5));
            Assert.That(CalibrateADCValueToVolts(limit - 1, HardwareIdentifier.VERISENSE_PULSE_PLUS), Is.LessThan(0.5));
        }

        private static SensorGSR CreateSR68Sensor(Sensor.SensorSetting range)
        {
            var sensor = new SensorGSR();
            sensor.SetDeviceHardwareIdentifier(HardwareIdentifier.VERISENSE_PULSE_PLUS);
            sensor.SetGSREnabled(true);
            sensor.SetGSRRange(range);
            return sensor;
        }

        private static void AssertReadsOpen(SensorGSR sensor, int code, string rangeLabel)
        {
            // One GSR sample as the firmware sends it: range in bits 15-14 over the 12-bit code, LSB first.
            int raw = (Range3 << 14) | code;
            var ojc = sensor.ParseSensorData(new byte[] { (byte)(raw & 0xFF), (byte)(raw >> 8) }, new ObjectCluster("", ""));
            double kOhms = ojc.GetData(SensorGSR.ObjectClusterSensorName.GSR, ShimmerConfiguration.SignalFormats.CAL, ShimmerConfiguration.SignalUnits.KiloOhms).Data;
            double uS = ojc.GetData(SensorGSR.ObjectClusterSensorName.GSR, ShimmerConfiguration.SignalFormats.CAL, ShimmerConfiguration.SignalUnits.MicroSiemens).Data;

            string context = rangeLabel + ", code " + code;
            Assert.That(kOhms, Is.GreaterThanOrEqualTo(Range3MaxKOhms), context);
            Assert.That(uS, Is.GreaterThan(0).And.LessThanOrEqualTo(1000.0 / Range3MaxKOhms), context);
        }
    }
}
