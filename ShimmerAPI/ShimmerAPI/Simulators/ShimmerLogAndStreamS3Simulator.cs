using System;
using System.Collections.Concurrent;
using System.Threading.Tasks;
using ShimmerAPI.Radios;
using ShimmerAPI.Utilities;
using static com.shimmerresearch.radioprotocol.LiteProtocolInstructionSet.Types;

namespace ShimmerAPI.Simulators
{
    public class ShimmerLogAndStreamS3Simulator : ShimmerLogAndStream
    {
        public string ComPort;
        public AbstractRadio mAbstractRadio;
        TaskCompletionSource<bool> mConnectTask = new TaskCompletionSource<bool>();
        public bool isConnectionOpen = false;
        protected BlockingCollection<byte> mBuffer = new BlockingCollection<byte>(1000); // Fixed size 1000
        /// <summary>
        /// The calibration blocks GET_ALL_CALIBRATION_COMMAND is answered with: a Shimmer3 sends four.
        /// Null leaves the command unanswered.
        /// </summary>
        public byte[] AllCalibrationBlocks;


        public ShimmerLogAndStreamS3Simulator(string devID, string bComPort)
    : base(devID)
        {
            ComPort = bComPort;
        }

        public ShimmerLogAndStreamS3Simulator(string devName, string bComPort, double samplingRate, int setEnabledSensors, byte[] exg1configuration, byte[] exg2configuration)
            : base(devName, samplingRate, setEnabledSensors, exg1configuration, exg2configuration)
        {
            ComPort = bComPort;
        }

        public ShimmerLogAndStreamS3Simulator(string devName, string bComPort, double samplingRate, int accelRange, int gsrRange, int setEnabledSensors, bool enableLowPowerAccel, bool enableLowPowerGyro, bool enableLowPowerMag, int gyroRange, int magRange, byte[] exg1configuration, byte[] exg2configuration, bool internalexppower)
            : base(devName, samplingRate, accelRange, gsrRange, setEnabledSensors, enableLowPowerAccel, enableLowPowerGyro, enableLowPowerMag, gyroRange, magRange, exg1configuration, exg2configuration, internalexppower)
        {
            ComPort = bComPort;
        }
        public ShimmerLogAndStreamS3Simulator(string devName, string bComPort, double samplingRate, int accelRange, int gsrRange, int setEnabledSensors, bool enableLowPowerAccel, bool enableLowPowerGyro, bool enableLowPowerMag, int gyroRange, int magRange, byte[] exg1configuration, byte[] exg2configuration, bool internalexppower, BTCRCMode CRCmode)
            : base(devName, samplingRate, accelRange, gsrRange, setEnabledSensors, enableLowPowerAccel, enableLowPowerGyro, enableLowPowerMag, gyroRange, magRange, exg1configuration, exg2configuration, internalexppower, CRCmode)
        {
            ComPort = bComPort;
        }

        public ShimmerLogAndStreamS3Simulator(string devID, string bComPort, double samplingRate, int AccelRange, int GyroRange, int gsrRange, int setEnabledSensors)
            : base(devID, samplingRate, AccelRange, GyroRange, gsrRange, setEnabledSensors)
        {
            ComPort = bComPort;
        }

        public override string GetShimmerAddress()
        {
            return ComPort;
        }

        public override void SetShimmerAddress(string address)
        {
            ComPort = address;
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
            return isConnectionOpen;

        }

        protected override void OpenConnection()
        {
            isConnectionOpen = true;
        }

        protected override int ReadByte()
        {
            return mBuffer.Take();
        }
        protected virtual void TxShimmerVersion()
        {
            mBuffer.Add(0xff);
            mBuffer.Add(0x25);
            mBuffer.Add(0x03);
        }

        protected virtual void TxFirmwareVersion()
        {
            mBuffer.Add(0xff);
            mBuffer.Add(0x2f);
            mBuffer.Add(0x03);
            mBuffer.Add(0x00);
            mBuffer.Add(0x00);
            mBuffer.Add(0x00);
            mBuffer.Add(0x10);
            mBuffer.Add(0x09);
        }
        protected override void WriteBytes(byte[] buffer, int index, int length)
        {

            if (buffer[0] == (byte)PacketTypeShimmer3.GET_SHIMMER_VERSION_COMMAND)
            {
                Console.WriteLine(UtilShimmer.BytesToHexString(buffer));
                TxShimmerVersion();
            }
            else if (buffer[0] == (byte)PacketTypeShimmer3.GET_VBATT_COMMAND)
            {
                mBuffer.Add((byte)0xff);
                mBuffer.Add((byte)0x8A);
                mBuffer.Add((byte)0x94);
                byte[] bytes = UtilShimmer.HexStringToByteArray("240B80");
                foreach (byte byteValue in bytes)
                {
                    mBuffer.Add(byteValue);
                }
                mBuffer.Add((byte)0x61);
            }
            else if (buffer[0] == (byte)PacketTypeShimmer2.GET_SAMPLING_RATE_COMMAND)
            {
                mBuffer.Add((byte)0xff);
                mBuffer.Add((byte)0x04);
                mBuffer.Add((byte)0x80);
                mBuffer.Add((byte)0x02);
            }
            else if (buffer[0] == (byte)PacketTypeShimmer2.GET_FW_VERSION_COMMAND)
            {
                TxFirmwareVersion();
            }
            else if (buffer[0] == (byte)PacketTypeShimmer3.GET_EXPANSION_BOARD_COMMAND)
            {
                mBuffer.Add((byte)0xff);
                mBuffer.Add((byte)0x65);
                mBuffer.Add((byte)0x03);
                mBuffer.Add((byte)0x30);
                mBuffer.Add((byte)0x04);
                mBuffer.Add((byte)0x02);
            }
            else if (buffer[0] == (byte)PacketTypeShimmer3SDBT.GET_INFOMEM_COMMAND)
            {
                if (buffer[1] == (byte)0x80 && buffer[2] == (byte)0x00 && buffer[3] == (byte)0x00) //0x8E 0x80 0x00 0x00
                {
                    mBuffer.Add((byte)0xff);
                    mBuffer.Add((byte)0x8D);
                    mBuffer.Add((byte)0x80);
                    byte[] bytes = UtilShimmer.HexStringToByteArray("800201E010004D9B0E08008010000000000002010080100000000000020109000000083C081F0825005300530054019C009C0100FEFF9C0000000000000CD00CD00CD0009C009C000000009C002AFBEDFC21010700E500FD009C0064000000009C00000000000001A201A201A2009C0064000000009C0000000000FFFFFFFFFF");
                    foreach (byte byteValue in bytes)
                    {
                        mBuffer.Add(byteValue);
                    }
                    mBuffer.Add((byte)0x8c);
                }
                else if (buffer[1] == (byte)0x80 && buffer[2] == (byte)0x80 && buffer[3] == (byte)0x00) //0x8E 0x80 0x80 0x00
                {
                    mBuffer.Add((byte)0xff);
                    mBuffer.Add((byte)0x8D);
                    mBuffer.Add((byte)0x80);
                    byte[] bytes = UtilShimmer.HexStringToByteArray("00000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000005368696D6D65725F33453336574354455354FFFFFFFFFFFF670773A70000B9003600000000E8EB1B713E360000000000000000000000000000000000000000000000000000");
                    foreach (byte byteValue in bytes)
                    {
                        mBuffer.Add(byteValue);
                    }

                    mBuffer.Add((byte)0x32);
                }
                else if (buffer[1] == (byte)0x80 && buffer[2] == (byte)0x00 && buffer[3] == (byte)0x01) //[0x8E 0x80 0x00 0x01]
                {
                    mBuffer.Add((byte)0xff);
                    mBuffer.Add((byte)0x8D);
                    mBuffer.Add((byte)0x80);
                    byte[] bytes = UtilShimmer.HexStringToByteArray("0000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000");
                    foreach (byte byteValue in bytes)
                    {
                        mBuffer.Add(byteValue);
                    }

                    mBuffer.Add((byte)0x9B);
                }
            }
            else if (buffer[0] == (byte)PacketTypeShimmer3.GET_BMP280_CALIBRATION_COEFFICIENTS_COMMAND)
            {
                mBuffer.Add((byte)0xff);
                mBuffer.Add((byte)0x9f);
                byte[] bytes = UtilShimmer.HexStringToByteArray("7A6A0D6632007F9016D7D00BBC1B2AFFF9FF8C3CF8C67017");
                foreach (byte byteValue in bytes)
                {
                    mBuffer.Add(byteValue);
                }
                mBuffer.Add((byte)0xDF);
            }
            else if (buffer[0] == (byte)PacketTypeShimmer2.GET_BLINK_LED)
            {
                mBuffer.Add((byte)0xff);
                mBuffer.Add((byte)0x31);
                mBuffer.Add((byte)0x01);
                mBuffer.Add((byte)0xE0);
            }
            else if (buffer[0] == (byte)PacketTypeShimmer3SDBT.GET_STATUS_COMMAND)
            {
                mBuffer.Add((byte)0xff);
                mBuffer.Add((byte)0x8A);
                mBuffer.Add((byte)0x71);
                mBuffer.Add((byte)0x21);
                mBuffer.Add((byte)0xF4);
            }
            else if (buffer[0] == (byte)PacketTypeShimmer2.INQUIRY_COMMAND)
            {
                mBuffer.Add((byte)0xff);
                mBuffer.Add((byte)0x02);
                byte[] bytes = UtilShimmer.HexStringToByteArray("800202FF01080001");
                foreach (byte byteValue in bytes)
                {
                    mBuffer.Add(byteValue);
                }
                mBuffer.Add((byte)0x86);
            }
            else if (buffer[0] == (byte)PacketTypeShimmer3SDBT.GET_BT_FW_VERSION_STR_COMMAND)
            {
                mBuffer.Add((byte)0xff);
                mBuffer.Add((byte)0xa2);
                mBuffer.Add((byte)0x35);
                byte[] bytes = UtilShimmer.HexStringToByteArray("524E343637382056312E30302E352031312F31352F32303136202863294D6963726F6368697020546563686E6F6C6F677920496E63");
                foreach (byte byteValue in bytes)
                {
                    mBuffer.Add(byteValue);
                }
                mBuffer.Add((byte)0x79);
            }
            else if (buffer[0] == (byte)PacketTypeShimmer3SDBT.GET_CALIB_DUMP_COMMAND)
            {
                if (buffer[1] == (byte)0x80 && buffer[2] == (byte)0x00 && buffer[3] == (byte)0x00) //[0x9A 0x80 0x00 0x00]
                {
                    mBuffer.Add((byte)0xff);
                    mBuffer.Add((byte)0x99);
                    byte[] bytes = UtilShimmer.HexStringToByteArray("8000005201030002000000160702000015BE0FB95C7E330000083C081F0825005300530054019C009C0100FEFF9C1E0000150000000000000000000000000000332C332C332C009C009C000000009C1E0001150000000000000000FDC00474FFC219D31957133C009CFF9C0100FE009C1E00021500000000000000000000000000000C");
                    foreach (byte byteValue in bytes)
                    {
                        mBuffer.Add(byteValue);
                    }
                    mBuffer.Add((byte)0xB5);
                }
                else if (buffer[1] == (byte)0x80 && buffer[2] == (byte)0x80 && buffer[3] == (byte)0x00) //[0x9A 0x80 0x80 0x00]
                {
                    mBuffer.Add((byte)0xff);
                    mBuffer.Add((byte)0x99);
                    byte[] bytes = UtilShimmer.HexStringToByteArray("808000D00CD00CD0009C009C000000009C1E0003150000000000000000000000000000066806680668009C009C000000009C1F00001539D4942779330000FFF5018CFD240683067F0692FF9C00640000F8FE9C1F000115000000000000000000000000000000D100D100D1009C0064000000009C1F0002150000000000000000FFEB01");
                    foreach (byte byteValue in bytes)
                    {
                        mBuffer.Add(byteValue);
                    }
                    mBuffer.Add((byte)0xFA);
                }
                else if (buffer[1] == (byte)0x54 && buffer[2] == (byte)0x00 && buffer[3] == (byte)0x01) //[0x9A 0x54 0x00 0x01]
                {
                    mBuffer.Add((byte)0xff);
                    mBuffer.Add((byte)0x99);
                    byte[] bytes = UtilShimmer.HexStringToByteArray("5400015AFDF8034203410348009C0064FF00FC009C1F000315000000000000000000000000000001A201A201A2009C0064000000009C200000150000000000000000002AFBEDFC21010700E500FD009C0064000000009C");
                    foreach (byte byteValue in bytes)
                    {
                        mBuffer.Add(byteValue);
                    }
                    mBuffer.Add((byte)0x6A);
                }
            }
            else if (buffer[0] == (byte)PacketTypeShimmer2.GET_ALL_CALIBRATION_COMMAND && AllCalibrationBlocks != null)
            {
                mBuffer.Add((byte)0xff);
                mBuffer.Add((byte)PacketTypeShimmer2.ALL_CALIBRATION_RESPONSE);
                foreach (byte byteValue in AllCalibrationBlocks)
                {
                    mBuffer.Add(byteValue);
                }
            }
            else
            {

                Console.WriteLine("Unresolved: " + UtilShimmer.BytesToHexString(buffer));
            }

        }

    }
}
