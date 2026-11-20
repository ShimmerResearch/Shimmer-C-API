using System;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using ShimmerAPI;
using ShimmerAPI.Utilities;

namespace ShimmerBluetoothTests
{
    /// <summary>
    /// DEV-1521: the KXTC9-2050's default calibration on a Shimmer3. Kionix specifies the part at
    /// Vdd = 3.3 V (zero-g 1.65 V, 660 mV/g) with an offset and a sensitivity "ratiometric error":
    /// its output scales with its supply. Every Shimmer3 board that carries it powers it from the same
    /// 3.0 V LDO as the MSP430's AVCC, the ADC reference, so in ADC counts it reads like the KXRB5-2042
    /// it replaced, and its defaults must be that part's: 2047 and 83. The old 2253 and 92 assumed
    /// 3.3 V and made an uncalibrated new-IMU Shimmer3 read gravity about 17 % high.
    /// </summary>
    [TestClass]
    public class ShimmerKxtc9DefaultCalibrationTest
    {
        [TestMethod]
        public void DefaultsAreTheDatasheetFiguresScaledToTheShimmersThreeVoltSupply()
        {
            const double Supply = 3.0, AdcVref = 3.0, AdcFullScale = 4095, G = 9.81;
            double offset = 1.65 * Supply / 3.3 / AdcVref * AdcFullScale; // 2047.5
            double sensitivity = 0.660 * Supply / 3.3 / AdcVref * AdcFullScale / G; // 83.5
            for (int axis = 0; axis < 3; axis++)
            {
                Assert.AreEqual(offset, ShimmerBluetooth.OFFSET_VECTOR_ACCEL_LOW_NOISE_SHIMMER3_KXTC9_2050[axis, 0], 1.0, "offset, axis " + axis);
                Assert.AreEqual(sensitivity, ShimmerBluetooth.SENSITIVITY_MATRIX_LOW_NOISE_ACCEL_SHIMMER3_KXTC9_2050[axis, axis], 1.0, "sensitivity, axis " + axis);
            }
        }

        [TestMethod]
        public void DefaultsMatchTheKxrb5s()
        {
            CollectionAssert.AreEqual(ShimmerBluetooth.OFFSET_VECTOR_ACCEL_LOW_NOISE_SHIMMER3_KCRB5_2042, ShimmerBluetooth.OFFSET_VECTOR_ACCEL_LOW_NOISE_SHIMMER3_KXTC9_2050);
            CollectionAssert.AreEqual(ShimmerBluetooth.SENSITIVITY_MATRIX_LOW_NOISE_ACCEL_SHIMMER3_KCRB5_2042, ShimmerBluetooth.SENSITIVITY_MATRIX_LOW_NOISE_ACCEL_SHIMMER3_KXTC9_2050);
            CollectionAssert.AreEqual(ShimmerBluetooth.ALIGNMENT_MATRIX_LOW_NOISE_ACCEL_SHIMMER3_KCRB5_2042, ShimmerBluetooth.ALIGNMENT_MATRIX_LOW_NOISE_ACCEL_SHIMMER3_KXTC9_2050);
        }

        /// <summary>
        /// Shimmer3 A615 (SR49-3-1) lying still, 20 Nov 2026: its raw LN accel at rest. On the defaults
        /// it must read 1 g; on the old 2253 and 92 it read 11.5 m/s2.
        /// </summary>
        [TestMethod]
        public void AShimmer3AtRestReadsOneG()
        {
            double[] cal = UtilCalibration.CalibrateInertialSensorData(new double[] { 1232, 2011, 2125 },
                ShimmerBluetooth.ALIGNMENT_MATRIX_LOW_NOISE_ACCEL_SHIMMER3_KXTC9_2050,
                ShimmerBluetooth.SENSITIVITY_MATRIX_LOW_NOISE_ACCEL_SHIMMER3_KXTC9_2050,
                ShimmerBluetooth.OFFSET_VECTOR_ACCEL_LOW_NOISE_SHIMMER3_KXTC9_2050);
            double magnitude = Math.Sqrt(cal[0] * cal[0] + cal[1] * cal[1] + cal[2] * cal[2]);
            Assert.AreEqual(9.81, magnitude, 0.15);
        }
    }
}
