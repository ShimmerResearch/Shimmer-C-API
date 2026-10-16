using System;
using System.Collections.Generic;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using ShimmerAPI;

namespace ShimmerBluetoothTests
{
    /// <summary>
    /// A GSR code below the amplifier reference is an open circuit on every range, and has to decode
    /// as one (DEV-1070). No skin resistance can pull the amplifier's output under its 0.5 V
    /// reference, so the equation goes negative there. Range 3 has long raised such codes to
    /// GSR_UNCAL_LIMIT_RANGE3. Ranges 0-2 did not, and in auto-range they see them: when the
    /// electrodes come off, the device climbs one range at a time and repeats the sample that
    /// triggered each switch for 80 ms, tagged with the range it was measured on. Those samples
    /// decoded to a negative resistance and conductance. Each case here goes through BuildMsg, for
    /// the Shimmer3, the Shimmer3R and the Shimmer2R.
    /// </summary>
    [TestClass]
    public class ShimmerGsrDecodeTest
    {
        const int AutoRange = 4;
        static readonly double[] WindowMaxKOhms = { 63.0, 220.0, 680.0, 4700.0 };
        static readonly int[] CodesBelowLimit = { 0, 500, 682 };
        static readonly int[] HardwareVersions =
        {
            (int)ShimmerBluetooth.ShimmerVersion.SHIMMER3,
            (int)ShimmerBluetooth.ShimmerVersion.SHIMMER3R,
            (int)ShimmerBluetooth.ShimmerVersion.SHIMMER2R
        };

        [TestMethod]
        public void TestBelowLimitReadsOpenOnEveryRangeInAutoRange()
        {
            foreach (int hardwareVersion in HardwareVersions)
            {
                var decoder = new GsrDecoder(hardwareVersion, AutoRange);
                double openKOhms = decoder.AmplifierEq(ShimmerBluetooth.GSR_UNCAL_LIMIT_RANGE3, 3);
                for (int range = 0; range <= 3; range++)
                {
                    foreach (int code in CodesBelowLimit)
                    {
                        string context = "HW " + hardwareVersion + " auto-range, range " + range + ", code " + code;
                        // The equation alone gives these a negative resistance
                        Assert.IsTrue(decoder.AmplifierEq(code, range) < 0, context);

                        decoder.Decode(range, code, out double kOhms, out double uS);
                        Assert.AreEqual(openKOhms, kOhms, context);
                        Assert.IsTrue(uS > 0 && uS < 0.03, context + " read " + uS + " uS");
                    }
                }
            }
        }

        /// <summary>
        /// A fixed range still clamps to its own window, but an open circuit now pins it to the top of
        /// the window, as fixed range 3 already did, instead of the bottom.
        /// </summary>
        [TestMethod]
        public void TestFixedRangesReadTheTopOfTheirWindow()
        {
            foreach (int hardwareVersion in HardwareVersions)
            {
                for (int range = 0; range <= 3; range++)
                {
                    var decoder = new GsrDecoder(hardwareVersion, range);
                    foreach (int code in CodesBelowLimit)
                    {
                        decoder.Decode(range, code, out double kOhms, out double uS);
                        Assert.AreEqual(WindowMaxKOhms[range], kOhms, "HW " + hardwareVersion + " fixed range " + range + ", code " + code);
                    }
                }
            }
        }

        /// <summary>Every code from the limit up decodes on its own range, exactly as before.</summary>
        [TestMethod]
        public void TestCodesAtOrAboveTheLimitDecodeAsBefore()
        {
            foreach (int hardwareVersion in HardwareVersions)
            {
                var decoder = new GsrDecoder(hardwareVersion, AutoRange);
                for (int range = 0; range <= 3; range++)
                {
                    for (int code = ShimmerBluetooth.GSR_UNCAL_LIMIT_RANGE3; code <= 4095; code++)
                    {
                        decoder.Decode(range, code, out double kOhms, out double uS);
                        Assert.AreEqual(decoder.AmplifierEq(code, range), kOhms, "HW " + hardwareVersion + " range " + range + ", code " + code);
                    }
                }
            }
        }
    }

    /// <summary>
    /// A ShimmerBluetooth with no connection behind it, so that one GSR packet can be put through
    /// BuildMsg directly.
    /// </summary>
    class GsrDecoder : ShimmerBluetooth
    {
        public GsrDecoder(int hardwareVersion, int gsrRange) : base("GsrDecoder")
        {
            HardwareVersion = hardwareVersion;
            GSRRange = gsrRange;
            // The Shimmer3 and 3R carry GSR as channel 0x1C, the Shimmer2R as 0x0B
            byte gsrChannel = hardwareVersion == (int)ShimmerVersion.SHIMMER2R ? (byte)0x0B : (byte)0x1C;
            InterpretDataPacketFormat(1, new byte[] { gsrChannel });
        }

        /// <summary>
        /// Decode one packet: a 16-bit timestamp, then the GSR word, range in bits 15-14 over the
        /// 12-bit code, LSB first.
        /// </summary>
        public void Decode(int range, int code, out double kOhms, out double uS)
        {
            int raw = (range << 14) | code;
            ObjectCluster ojc = BuildMsg(new List<byte> { 0, 0, (byte)(raw & 0xFF), (byte)(raw >> 8) });
            bool shimmer2 = HardwareVersion == (int)ShimmerVersion.SHIMMER2R;
            string gsr = shimmer2 ? Shimmer2Configuration.SignalNames.GSR : Shimmer3Configuration.SignalNames.GSR;
            string conductance = shimmer2 ? Shimmer2Configuration.SignalNames.GSR_CONDUCTANCE : Shimmer3Configuration.SignalNames.GSR_CONDUCTANCE;
            kOhms = ojc.GetData(gsr, ShimmerConfiguration.SignalFormats.CAL, ShimmerConfiguration.SignalUnits.KiloOhms).Data;
            uS = ojc.GetData(conductance, ShimmerConfiguration.SignalFormats.CAL, ShimmerConfiguration.SignalUnits.MicroSiemens).Data;
        }

        public double AmplifierEq(double code, int range)
        {
            return CalibrateGsrDataToResistanceFromAmplifierEq(code, range);
        }

        public override string GetShimmerAddress()
        {
            return "GsrDecoder";
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
