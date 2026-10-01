using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using static ShimmerAPI.ShimmerBluetooth;

namespace ShimmerAPI
{
    public abstract class ShimmerDevice
    {
        protected double LastReceivedTimeStamp = 0;
        protected double CurrentTimeStampCycle = 0;
        /// <summary>
        /// False only before the first sample of a stream. The pair above cannot say
        /// it on their own: (0, 0) is the reset state and also a state the unwrap can
        /// reach, when a reordered packet lands exactly on the counter's origin.
        /// </summary>
        protected bool HasPreviousTimeStamp = false;
        /// <summary>
        /// True when the sample most recently passed to CalibrateTimeStamp carried an
        /// invalid zero timestamp and was rejected rather than unwrapped. Its sensor
        /// data is fine; only its timestamp is missing. A caller reading a file should
        /// drop the record - see ShimmerSDLog.ReadPacketMsg.
        /// </summary>
        public bool LastTimestampRejected { get; protected set; }
        protected double LastReceivedCalibratedTimeStamp = -1;
        protected double CalTimeStart;
        protected double SamplingRate;
        protected int TimeStampPacketByteSize = 2;
        protected int TimeStampPacketRawMaxValue = 65536;// 16777216 or 65536 
        protected int ADCRawSamplingRateValue;
        protected int HardwareVersion = 0;
        public Boolean FirstTimeCalTime = true;
        public long PacketLossCount = 0;
        public double PacketReceptionRate = 100;
        public EventHandler UICallback; //this is to be used by other classes to communicate with the c# API
        public enum ShimmerVersion
        {
            SHIMMER1 = 0,
            SHIMMER2 = 1,
            SHIMMER2R = 2,
            SHIMMER3 = 3,
            SHIMMER3R = 10,
            SHIMMER4SDK = 58
        }
        /// <summary>
        /// How far behind its predecessor a sample may sit and still be read as a
        /// reordered packet rather than a counter roll-over. See
        /// <see cref="TimestampUnwrap.ReorderWindowTicks"/> for why it is sized in
        /// sample periods, and why an unknown rate must give zero rather than an
        /// infinite window.
        /// <para>
        /// Derived on every sample rather than cached, so a rate written mid-session is
        /// picked up by the next one and there is no stale window to reset.
        /// <see cref="SamplingRate"/> defaults to zero, which disables the branch until
        /// an inquiry or an SD header has set it.
        /// </para>
        /// <para>
        /// Zero - reorder detection off - on Shimmer2 and Shimmer2R. Their tick domain
        /// is not settled: this class divides their 16-bit counter by 1024 while the
        /// Java driver divides by 32768, so a window derived from the rate would be
        /// wrong in one of the two. Those devices keep the behaviour they have always
        /// had; the invalid-zero rule never applied to a 2-byte counter anyway.
        /// </para>
        /// </summary>
        protected virtual double GetReorderWindowTicks()
        {
            if (HardwareVersion == (int)ShimmerVersion.SHIMMER2 || HardwareVersion == (int)ShimmerVersion.SHIMMER2R)
            {
                return 0.0;
            }
            return TimestampUnwrap.ReorderWindowTicks(SamplingRate, TimeStampPacketRawMaxValue);
        }

        protected double CalibrateTimeStamp(double timeStamp)
        {
            //first convert to continuous time stamp
            double calibratedTimeStamp = 0;
            TimestampUnwrap.Result unwrapped = TimestampUnwrap.Unwrap(
                timeStamp, LastReceivedTimeStamp, CurrentTimeStampCycle, TimeStampPacketRawMaxValue,
                GetReorderWindowTicks(), HasPreviousTimeStamp);
            HasPreviousTimeStamp = true;

            LastTimestampRejected = unwrapped.Rejected;
            CurrentTimeStampCycle = unwrapped.Cycle;
            //On a rejected sample this puts back the value it already held, which is
            //what keeps the rejection from cascading: the next sample reads above it
            //and is accepted normally.
            LastReceivedTimeStamp = unwrapped.Unwrapped;

            double clockConstant = 1024;
            if (HardwareVersion == (int)ShimmerVersion.SHIMMER2R || HardwareVersion == (int)ShimmerVersion.SHIMMER2)
            {
                clockConstant = 1024;
            }
            else if (HardwareVersion == (int)ShimmerVersion.SHIMMER3 || HardwareVersion == (int)ShimmerVersion.SHIMMER3R)
            {
                clockConstant = 32768;
            }

            calibratedTimeStamp = LastReceivedTimeStamp / clockConstant * 1000;   // to convert into mS
            if (FirstTimeCalTime)
            {
                FirstTimeCalTime = false;
                CalTimeStart = calibratedTimeStamp;
            }
            if (LastReceivedCalibratedTimeStamp != -1 && !LastTimestampRejected)
            {
                //A rejected sample carries the previous timestamp, so the difference
                //here would be zero - a gap that never happened.
                double timeDifference = calibratedTimeStamp - LastReceivedCalibratedTimeStamp;
                double expectedTimeDifference = (1 / SamplingRate) * 1000; //in ms
                double adjustedETD = expectedTimeDifference + (expectedTimeDifference * 0.1);

                //if (timeDifference > (1 / ((clockConstant / ADCRawSamplingRateValue) - 1)) * 1000)
                if (timeDifference > adjustedETD)
                {
                    //calculate the estimated packet loss within that time period
                    int numberOfLostPackets = ((int)Math.Ceiling(timeDifference / expectedTimeDifference)) - 1;
                    PacketLossCount = PacketLossCount + numberOfLostPackets;
                    //PacketLossCount = PacketLossCount + 1;
                    long mTotalNumberofPackets = (long)((calibratedTimeStamp - CalTimeStart) / (1 / (clockConstant / ADCRawSamplingRateValue) * 1000));
                    mTotalNumberofPackets = (long)((calibratedTimeStamp - CalTimeStart) / expectedTimeDifference);

                    PacketReceptionRate = (double)((mTotalNumberofPackets - PacketLossCount) / (double)mTotalNumberofPackets) * 100;

                    if (PacketReceptionRate < 99)
                    {
                        //System.Console.WriteLine("PRR: " + PacketReceptionRate);
                    }
                }
            }

            EventHandler handler = UICallback;
            if (handler != null)
            {
                CustomEventArgs newEventArgs = new CustomEventArgs((int)ShimmerIdentifier.MSG_IDENTIFIER_PACKET_RECEPTION_RATE, (object)GetPacketReceptionRate());
                handler(this, newEventArgs);
            }

            LastReceivedCalibratedTimeStamp = calibratedTimeStamp;
            return calibratedTimeStamp - CalTimeStart; // make it start at zero
        }
        public double GetPacketReceptionRate()
        {
            return PacketReceptionRate;
        }

    }
}
