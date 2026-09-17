/*Rev 0.5
 * 
 * Copyright (c) 2014, Shimmer Research, Ltd.
 * All rights reserved
 *
 * Redistribution and use in source and binary forms, with or without
 * modification, are permitted provided that the following conditions are
 * met:

 *     * Redistributions of source code must retain the above copyright
 *       notice, this list of conditions and the following disclaimer.
 *     * Redistributions in binary form must reproduce the above
 *       copyright notice, this list of conditions and the following
 *       disclaimer in the documentation and/or other materials provided
 *       with the distribution.
 *     * Neither the name of Shimmer Research, Ltd. nor the names of its
 *       contributors may be used to endorse or promote products derived
 *       from this software without specific prior written permission.

 * THIS SOFTWARE IS PROVIDED BY THE COPYRIGHT HOLDERS AND CONTRIBUTORS
 * "AS IS" AND ANY EXPRESS OR IMPLIED WARRANTIES, INCLUDING, BUT NOT
 * LIMITED TO, THE IMPLIED WARRANTIES OF MERCHANTABILITY AND FITNESS FOR
 * A PARTICULAR PURPOSE ARE DISCLAIMED. IN NO EVENT SHALL THE COPYRIGHT
 * OWNER OR CONTRIBUTORS BE LIABLE FOR ANY DIRECT, INDIRECT, INCIDENTAL,
 * SPECIAL, EXEMPLARY, OR CONSEQUENTIAL DAMAGES (INCLUDING, BUT NOT
 * LIMITED TO, PROCUREMENT OF SUBSTITUTE GOODS OR SERVICES; LOSS OF USE,
 * DATA, OR PROFITS; OR BUSINESS INTERRUPTION) HOWEVER CAUSED AND ON ANY
 * THEORY OF LIABILITY, WHETHER IN CONTRACT, STRICT LIABILITY, OR TORT
 * (INCLUDING NEGLIGENCE OR OTHERWISE) ARISING IN ANY WAY OUT OF THE USE
 * OF THIS SOFTWARE, EVEN IF ADVISED OF THE POSSIBILITY OF SUCH DAMAGE.
 *
 * @author Ruaidhri Molloy, Cathy Swanton, Mike Healy, Jong Chern Lim
 * @date   Sept, 2014
 * 
 * Changes since rev0.5
 * - minor updates to improve readability for instreamcmd
 * 
 * Changes since rev0.4
 * - Added methods ReadShimmerName(), ReadExpID(), ReadConfigTime()
 * - Corrected magnetometer and WR accel alignment matrix to [-1, 0, 0; 0, 1, 0; 0, 0, -1] for Shimmer3
 * - Added Methods for get/set and read/write baud rate
 * - Added Expansion board read method
 * 
 * Changes since rev0.3
 * - Added writesdconfig to inquiry response
 * - fixed exg not reading 1st chip during initialization
 * - fixed 3D Orientation
 * - Added comments to important public methods
 * - Add setstate(connecting)
 * 
 * Changes since rev0.2
 * - Fix Pressure Sensor
 * - Added Low Battery Indicator
 * - Fixed KeepObjectCluster, wasnt going to null after stop streaming command because of the ACK case added for low batt indicator
 * - minor update to get ShimmerSDBT (BT functionality) to work with Shimmer.cs
 * - Added AccelHRBit, AccelLPBit, Mpu9150AccelRange for sdbt
 * - Switched startstreaming and stopstreaming to virtual methods
 * 
 * Changes since rev0.1
 * - added minorIndicator to Events
 */


using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.IO.Ports;
using System.IO;
using System.Threading;
using System.Diagnostics;
using System.Globalization;
using System.Timers;
using System.ComponentModel;
using static com.shimmerresearch.radioprotocol.LiteProtocolInstructionSet.Types;
using ShimmerAPI.Utilities;
using static ShimmerAPI.ShimmerBluetooth;
using ShimmerAPI.Sensors;

namespace ShimmerAPI
{
    public abstract class ShimmerBluetooth : ShimmerDevice
    {
        public const int SHIMMER_STATE_STREAMING = 3;
        public const int SHIMMER_STATE_CONNECTED = 2;
        public const int SHIMMER_STATE_CONNECTING = 1;
        public const int SHIMMER_STATE_NONE = 0;
        public bool mEnableTimeStampAlignmentCheck = false;
        public const int FW_IDENTIFIER_BTSTREAM = 1;
        public const int FW_IDENTIFIER_LOGANDSTREAM = 3;
        public const int FW_IDENTIFIER_SHIMMERECGMD = 16;

        public const int GSR_RANGE_8K_63K = 0;
        public const int GSR_RANGE_63K_220K = 1;
        public const int GSR_RANGE_220K_680K = 2;
        public const int GSR_RANGE_680K_4700K = 3;
        public const int GSR_RANGE_AUTO = 4;
        protected int ShimmerState = SHIMMER_STATE_NONE;

        protected bool mWaitingForStartStreamingACK = false;
        private int StreamTimeOutCount = 0;
        protected bool StreamingACKReceived = false;
        protected String DeviceName;
        protected List<ObjectCluster> ObjectClusterBuffer = new List<ObjectCluster>();
        protected volatile bool StopReading = false;
        protected Thread ReadThread;
        protected Thread ConnectThread;
        public static int MAX_NUMBER_OF_SIGNALS = 35;
        public static int MAX_INQUIRY_PACKET_SIZE = 42;
        public bool LowPowerMagEnabled = false;
        public bool LowPowerAccelEnabled = false;
        public bool LowPowerGyroEnabled = false;
        public int InternalExpPower = -1;
        protected int magSamplingRate;
        protected int NumberofChannels;
        protected int BufferSize;
        protected int FirmwareMajor;
        protected int FirmwareMinor;
        protected double FirmwareIdentifier;
        protected int FirmwareInternal;
        protected String FirmwareVersionFullName;
        protected List<byte> ListofSensorChannels = new List<byte>();
        protected Boolean Orientation3DEnabled = false;
        protected Boolean EnableGyroOnTheFlyCalibration = false;
        protected int ListSizeGyroOnTheFly = 100;
        protected double ThresholdGyroOnTheFly = 1.2;
        protected int AccelRange;
        protected int GyroRange;
        protected int PressureResolution;
        protected int MagGain;
        protected int LNAccelRange;
        protected int AccelSamplingRate;
        protected int GyroSamplingRate;
        protected int AltAccelSamplingRate;
        protected int AltMagSamplingRate;
        protected int AltMagRange;
        protected long ConfigSetupByte0; // for Shimmer2
        protected int GSRRange;
        protected int mTempIntValue;
        protected int AccelHRBit = 0;
        protected int AccelLPBit = 0;
        protected int Mpu9150AccelRange = 3;
        protected int BaudRate;
        protected byte[] ExpansionDetectArray;//Expansion board detect for Shimmer3
        protected string ExpansionBoard;
        protected int ExpansionBoardId;
        protected int ExpansionBoardRev;
        protected int ExpansionBoardRevSpecial;
        protected double BatteryVoltage;
        protected int ChargingStatus;
        protected virtual bool ShouldAddSystemTimestamp => true;

        List<double> HRMovingAVGWindow = new List<double>(4);
        protected String[] SignalNameArray = new String[MAX_NUMBER_OF_SIGNALS];
        protected String[] SignalDataTypeArray = new String[MAX_NUMBER_OF_SIGNALS];
        protected int PacketSize = 2; // Time stamp
        protected long EnabledSensors;
        protected long DerivedSensors;
        protected ObjectCluster KeepObjectCluster = null; // this is to keep the packet for one byte, just incase there is a dropped packet
        public double[,] AlignmentMatrixAccel = new double[3, 3] { { -1, 0, 0 }, { 0, -1, 0 }, { 0, 0, 1 } };
        public double[,] SensitivityMatrixAccel = new double[3, 3] { { 38, 0, 0 }, { 0, 38, 0 }, { 0, 0, 38 } };
        public double[,] OffsetVectorAccel = new double[3, 1] { { 2048 }, { 2048 }, { 2048 } };
        public double[,] AlignmentMatrixGyro = new double[3, 3] { { 0, -1, 0 }, { -1, 0, 0 }, { 0, 0, -1 } };
        public double[,] SensitivityMatrixGyro = new double[3, 3] { { 2.73, 0, 0 }, { 0, 2.73, 0 }, { 0, 0, 2.73 } };
        public double[,] OffsetVectorGyro = new double[3, 1] { { 1843 }, { 1843 }, { 1843 } };
        public double[,] AlignmentMatrixMag = new double[3, 3] { { 1, 0, 0 }, { 0, 1, 0 }, { 0, 0, -1 } };
        public double[,] SensitivityMatrixMag = new double[3, 3] { { 580, 0, 0 }, { 0, 580, 0 }, { 0, 0, 580 } };
        public double[,] OffsetVectorMag = new double[3, 1] { { 0 }, { 0 }, { 0 } };
        public double[,] AlignmentMatrixMag2 = new double[3, 3] { { 1, 0, 0 }, { 0, 1, 0 }, { 0, 0, -1 } };
        public double[,] SensitivityMatrixMag2 = new double[3, 3] { { 580, 0, 0 }, { 0, 580, 0 }, { 0, 0, 580 } };
        public double[,] OffsetVectorMag2 = new double[3, 1] { { 0 }, { 0 }, { 0 } };
        public double[,] AlignmentMatrixAccel2 = new double[3, 3] { { -1, 0, 0 }, { 0, -1, 0 }, { 0, 0, 1 } };
        public double[,] SensitivityMatrixAccel2 = new double[3, 3] { { 38, 0, 0 }, { 0, 38, 0 }, { 0, 0, 38 } };
        public double[,] OffsetVectorAccel2 = new double[3, 1] { { 2048 }, { 2048 }, { 2048 } };
        public double[,] AlignmentMatrixAltAccel = new double[3, 3] { { 0, 1, 0 }, { 1, 0, 0 }, { 0, 0, -1 } };
        public double[,] SensitivityMatrixAltAccel = new double[3, 3] { { 16, 0, 0 }, { 0, 16, 0 }, { 0, 0, 16 } };
        public double[,] OffsetVectorAltAccel = new double[3, 1] { { 0 }, { 0 }, { 0 } };

        //Default Values for Magnetometer Calibration

        public double AC1 = 408;
        public double AC2 = -72;
        public double AC3 = -14383;
        public double AC4 = 332741;
        public double AC5 = 32757;
        public double AC6 = 23153;
        public double B1 = 6190;
        public double B2 = 4;
        public double MB = -32767;
        public double MC = -8711;
        public double MD = 2868;

        public double dig_T1 = 27504;
        public double dig_T2 = 26435;
        public double dig_T3 = -1000;
        public double dig_P1 = 36477;
        public double dig_P2 = -10685;
        public double dig_P3 = 3024;
        public double dig_P4 = 2855;
        public double dig_P5 = 140;
        public double dig_P6 = -7;
        public double dig_P7 = 15500;
        public double dig_P8 = -14600;
        public double dig_P9 = 6000;

        //constant
        public const double BMP3_MIN_PRES_DOUBLE = 30000.0f;
        public const double BMP3_MAX_PRES_DOUBLE = 125000.0f;
        public const double BMP3_MIN_TEMP_DOUBLE = -40.0f;
        public const double BMP3_MAX_TEMP_DOUBLE = 85.0f;
        public const sbyte BMP3_OK = 0;
        public const sbyte BMP3_W_MIN_TEMP = 3;
        public const sbyte BMP3_W_MAX_TEMP = 4;
        public const sbyte BMP3_W_MIN_PRES = 5;
        public const sbyte BMP3_W_MAX_PRES = 6;

        public double OffsetECGRALL = 2060;
        public double GainECGRALL = 175;
        public double OffsetECGLALL = 2060;
        public double GainECGLALL = 175;
        public double OffsetEMG = 2060;
        public double GainEMG = 750;
        public double OffsetSGHigh = 60;
        public double VRef = 3;
        public double GainSGHigh = 551;
        public double OffsetSGLow = 1950;
        public double GainSGLow = 183.7;

        public int LastKnownHeartRate = 0;
        public bool FirstSystemTimestamp = true;
        public double FirstSystemTimeStampValue;
        public bool DefaultAccelParams = true;
        public bool DefaultWRAccelParams = true;
        public bool DefaultHighGAccelParams = true;
        public bool DefaultGyroParams = true;
        public bool DefaultMagParams = true;
        public bool DefaultWRMagParams = true;
        public bool DefaultECGParams = true;
        public bool DefaultEMGParams = true;
        protected int CurrentLEDStatus = 0;

        public bool IsFilled = false;
        public System.Timers.Timer TimerConnect = null; // From System.Timers

        protected bool SetupDevice = false;
        protected int SetEnabledSensors = (int)SensorBitmapShimmer2.SENSOR_ACCEL;
        protected int ChipID;
        protected int CompatibilityCode = 0;
        protected List<double> GyroXCalList = new List<double>();
        protected List<double> GyroYCalList = new List<double>();
        protected List<double> GyroZCalList = new List<double>();
        protected List<double> GyroXRawList = new List<double>();
        protected List<double> GyroYRawList = new List<double>();
        protected List<double> GyroZRawList = new List<double>();

        protected GradDes3DOrientation OrientationAlgo;

        protected int BufferSyncSizeInSeconds = 15;
        public static readonly DateTime UnixEpoch = new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc);

        protected long ShimmerRealWorldClock = 0;
        protected List<byte> UnalignedBytesReceived = new List<Byte>();

        protected LNAccel SensorLNAccel;
        protected WRAccel SensorWRAccel;
        protected HighGAccel SensorHighGAccel;
        protected GyroSensor SensorGyro;
        protected MagSensor SensorMag;
        protected WRMag SensorWRMag;

        public enum BTCRCMode
        {
            OFF = 0,
            ONE_BYTE = 1,
            TWO_BYTE = 2
        }

        protected BTCRCMode BluetoothCRCMode = BTCRCMode.OFF;
        protected BTCRCMode CRCModeToSetup = BTCRCMode.OFF;

        public enum ShimmerIdentifier
        {
            MSG_IDENTIFIER_STATE_CHANGE = 0,
            MSG_IDENTIFIER_NOTIFICATION_MESSAGE = 1,
            MSG_IDENTIFIER_DATA_PACKET = 2,
            MSG_IDENTIFIER_PACKET_RECEPTION_RATE = 3
        }

        public enum SensorBitmapShimmer2
        {
            SENSOR_ACCEL = 0x80,
            SENSOR_GYRO = 0x40,
            SENSOR_MAG = 0x20,
            SENSOR_ECG = 0x10,
            SENSOR_EMG = 0x08,
            SENSOR_GSR = 0x04,
            SENSOR_EXP_BOARD_A7 = 0x02,
            SENSOR_EXP_BOARD_A0 = 0x01,
            SENSOR_STRAIN_GAUGE = 0x8000,
            SENSOR_HEART = 0x4000, // - this is for the Polar strap, but will not be supported in c# api, for use, use the old Shimmer Connect 0.12 or below
            SENSOR_VBATT = 0x2000 // this is a dummy value

        }

        public enum SensorBitmapShimmer3
        {
            SENSOR_A_ACCEL = 0x80,
            SENSOR_GYRO = 0x040,
            SENSOR_MAG = 0x20,
            SENSOR_GSR = 0x04,
            SENSOR_EXT_A7 = 0x02,
            SENSOR_EXT_A6 = 0x01,
            SENSOR_VBATT = 0x2000,
            SENSOR_D_ACCEL = 0x1000,
            SENSOR_EXT_A15 = 0x0800,
            SENSOR_INT_A1 = 0x0400,
            SENSOR_INT_A12 = 0x0200,
            SENSOR_INT_A13 = 0x0100,
            SENSOR_INT_A14 = 0x800000,
            SENSOR_BMP180_PRESSURE = 0x40000,
            SENSOR_EXG1_24BIT = 0x10,
            SENSOR_EXG2_24BIT = 0x08,
            SENSOR_EXG1_16BIT = 0x100000,
            SENSOR_EXG2_16BIT = 0x080000,
            SENSOR_BRIDGE_AMP = 0x8000,
            SENSOR_ACCEL_ALT = 0x400000,
            SENSOR_MAG_ALT = 0X200000,

        }

        public enum SensorBitmapShimmer3R
        {
            SENSOR_EXT_A0 = 0x02,
            SENSOR_EXT_A1 = 0x01,
            SENSOR_EXT_A2 = 0x0800,
            SENSOR_INT_A3 = 0x0400,
            SENSOR_INT_A0 = 0x0200,
            SENSOR_INT_A1 = 0x0100,
            SENSOR_INT_A2 = 0x800000,
            SENSOR_BMP380_PRESSURE = 0x40000
        }

        public enum ChannelContentsShimmer3
        {
            XLNAccel = 0x00,
            YLNAccel = 0x01,
            ZLNAccel = 0x02,
            VBatt = 0x03,
            XWRAccel = 0x04,
            YWRAccel = 0x05,
            ZWRAccel = 0x06,
            XMag = 0x07,
            YMag = 0x08,
            ZMag = 0x09,
            XGyro = 0x0A,
            YGyro = 0x0B,
            ZGyro = 0x0C,
            ExternalAdc7 = 0x0D,
            ExternalAdc6 = 0x0E,
            ExternalAdc15 = 0x0F,
            InternalAdc1 = 0x10,
            InternalAdc12 = 0x11,
            InternalAdc13 = 0x12,
            InternalAdc14 = 0x13,
            AlternativeXAccel = 0x14, //Unsupported
            AlternativeYAccel = 0x15, //Unsupported
            AlternativeZAccel = 0x16, //Unsupported
            AlternativeXMag = 0x17, //Unsupported
            AlternativeYMag = 0x18, //Unsupported
            AlternativeZMag = 0x19, //Unsupported
            Temperature = 0x1A,
            Pressure = 0x1B,
            Exg1_Status = 0x1D,
            Exg1_CH1 = 0x1E,
            Exg1_CH2 = 0x1F,
            Exg2_Status = 0x20,
            Exg2_CH1 = 0x21,
            Exg2_CH2 = 0x22,
            Exg1_CH1_16Bit = 0x23,
            Exg1_CH2_16Bit = 0x24,
            Exg2_CH1_16Bit = 0x25,
            Exg2_CH2_16Bit = 0x26,
            STRAIN_HIGH = 0x27,
            STRAIN_LOW = 0x28,
            GsrRaw = 0x1C
        }

        public enum ChannelContentsShimmer2
        {
            XAccel = 0x00,
            YAccel = 0x01,
            ZAccel = 0x02,
            XGyro = 0x03,
            YGyro = 0x04,
            ZGyro = 0x05,
            XMag = 0x06,
            YMag = 0x07,
            ZMag = 0x08,
            EcgRaLl = 0x09,
            EcgLaLl = 0x0A,
            GsrRaw = 0x0B,
            GsrRes = 0x0C,
            Emg = 0x0D,
            AnExA0 = 0x0E,
            AnExA7 = 0x0F,
            StrainHigh = 0x10,
            StrainLow = 0x11,
            HeartRate = 0x12

        }

        public enum PacketTypeShimmer2 : byte //Note that most packet
        {
            DATA_PACKET = 0x00,
            INQUIRY_COMMAND = 0x01,
            INQUIRY_RESPONSE = 0x02,
            GET_SAMPLING_RATE_COMMAND = 0x03,
            SAMPLING_RATE_RESPONSE = 0x04,
            SET_SAMPLING_RATE_COMMAND = 0x05,
            TOGGLE_LED_COMMAND = 0x06,
            START_STREAMING_COMMAND = 0x07,
            SET_SENSORS_COMMAND = 0x08,
            SET_ACCEL_RANGE_COMMAND = 0x09,
            ACCEL_RANGE_RESPONSE = 0x0A,
            GET_ACCEL_RANGE_COMMAND = 0x0B,
            SET_5V_REGULATOR_COMMAND = 0x0C,
            SET_POWER_MUX_COMMAND = 0x0D,
            SET_CONFIG_SETUP_BYTE0_Command = 0x0E,
            CONFIG_SETUP_BYTE0_RESPONSE = 0x0F,
            GET_CONFIG_SETUP_BYTE0_COMMAND = 0x10,
            SET_ACCEL_CALIBRATION_COMMAND = 0x11,
            ACCEL_CALIBRATION_RESPONSE = 0x12,
            GET_ACCEL_CALIBRATION_COMMAND = 0x13,
            SET_GYRO_CALIBRATION_COMMAND = 0x14,
            GYRO_CALIBRATION_RESPONSE = 0x15,
            GET_GYRO_CALIBRATION_COMMAND = 0x16,
            SET_MAG_CALIBRATION_COMMAND = 0x17,
            MAG_CALIBRATION_RESPONSE = 0x18,
            GET_MAG_CALIBRATION_COMMAND = 0x19,
            STOP_STREAMING_COMMAND = 0x20,
            SET_GSR_RANGE_COMMAND = 0x21,
            GSR_RANGE_RESPONSE = 0x22,
            GET_GSR_RANGE_COMMAND = 0x23,
            GET_SHIMMER_VERSION_COMMAND = 0x24,
            GET_SHIMMER_VERSION_RESPONSE = 0x25,
            SET_EMG_CALIBRATION_COMMAND = 0x26,
            EMG_CALIBRATION_RESPONSE = 0x27,
            GET_EMG_CALIBRATION_COMMAND = 0x28,
            SET_ECG_CALIBRATION_COMMAND = 0x29,
            ECG_CALIBRATION_RESPONSE = 0x2A,
            GET_ECG_CALIBRATION_COMMAND = 0x2B,
            GET_ALL_CALIBRATION_COMMAND = 0x2C,
            ALL_CALIBRATION_RESPONSE = 0x2D,
            GET_FW_VERSION_COMMAND = 0x2E,
            FW_VERSION_RESPONSE = 0x2F,
            SET_BLINK_LED = 0x30,
            BLINK_LED_RESPONSE = 0x31,
            GET_BLINK_LED = 0x32,
            SET_GYRO_TEMP_VREF_COMMAND = 0x33,
            SET_BUFFER_SIZE_COMMAND = 0x34,
            BUFFER_SIZE_RESPONSE = 0x35,
            GET_BUFFER_SIZE_COMMAND = 0x36,
            SET_MAG_GAIN_COMMAND = 0x37,
            MAG_GAIN_RESPONSE = 0x38,
            GET_MAG_GAIN_COMMAND = 0x39,
            SET_MAG_SAMPLING_RATE_COMMAND = 0x3A,
            MAG_SAMPLING_RATE_RESPONSE = 0x3B,
            GET_MAG_SAMPLING_RATE_COMMAND = 0x3C,
            ACK_COMMAND = 0xFF
        };

        public enum PacketTypeShimmer3 : byte
        {

            SET_LNACCEL_CALIBRATION_COMMAND = 0x11,
            LNACCEL_CALIBRATION_RESPONSE = 0x12,
            GET_LNACCEL_CALIBRATION_COMMAND = 0x13,
            SET_ALT_ACCEL_RANGE_COMMAND = 0x4F,
            ALT_ACCEL_RANGE_RESPONSE = 0x50,
            GET_ALT_ACCEL_RANGE_COMMAND = 0x51,
            SET_GYRO_CALIBRATION_COMMAND = 0x14,
            GYRO_CALIBRATION_RESPONSE = 0x15,
            GET_GYRO_CALIBRATION_COMMAND = 0x16,
            SET_MAG_CALIBRATION_COMMAND = 0x17,
            MAG_CALIBRATION_RESPONSE = 0x18,
            GET_MAG_CALIBRATION_COMMAND = 0x19,
            WR_ACCEL_CALIBRATION_RESPONSE = 0x1B,
            STOP_STREAMING_COMMAND = 0x20,
            GET_SHIMMER_VERSION_COMMAND = 0x3F,
            SHIMMER_VERSION_RESPONSE = 0x25,
            GET_FW_VERSION_COMMAND = 0x2E,
            FW_VERSION_RESPONSE = 0x2F,
            SET_LSM303DLHC_MAG_GAIN_COMMAND = 0x37,
            SET_ACCEL_SAMPLING_RATE_COMMAND = 0x40,
            ACCEL_SAMPLING_RATE_RESPONSE = 0x41,
            GET_ACCEL_SAMPLING_RATE_COMMAND = 0x42,
            MPU9150_GYRO_RANGE_RESPONSE = 0x4A,
            GET_MPU9150_GYRO_RANGE_COMMAND = 0x4B,
            SET_MPU9150_SAMPLING_RATE_COMMAND = 0x4C,
            SET_MPU9150_GYRO_RANGE_COMMAND = 0x49,
            SET_ALT_ACCEL_CALIBRATION_COMMAND = 0xA9,
            ALT_ACCEL_CALIBRATION_RESPONSE = 0xAA,
            GET_ALT_ACCEL_CALIBRATION_COMMAND = 0xAB,
            SET_ALT_ACCEL_SAMPLING_RATE_COMMAND = 0xAC,
            ALT_ACCEL_SAMPLING_RATE_RESPONSE = 0xAD,
            GET_ALT_ACCEL_SAMPLING_RATE_COMMAND = 0xAE,
            ALT_MAG_CALIBRATION_RESPONSE = 0xB0,
            GET_ALT_MAG_CALIBRATION_COMMAND = 0xB1,
            ALT_MAG_SAMPLING_RATE_RESPONSE = 0xB3,
            GET_ALT_MAG_SAMPLING_RATE_COMMAND = 0xB4,
            SET_BMP180_PRES_RESOLUTION_COMMAND = 0x52,
            BMP180_PRES_RESOLUTION_RESPONSE = 0x53,
            GET_BMP180_PRES_RESOLUTION_COMMAND = 0x54,
            BMP180_CALIBRATION_COEFFICIENTS_RESPONSE = 0x58,
            GET_BMP180_CALIBRATION_COEFFICIENTS_COMMAND = 0x59,
            BMP280_CALIBRATION_COEFFICIENTS_RESPONSE = 0x9F,
            GET_BMP280_CALIBRATION_COEFFICIENTS_COMMAND = 0xA0,
            SET_INTERNAL_EXP_POWER_ENABLE_COMMAND = 0x5E,
            INTERNAL_EXP_POWER_ENABLE_RESPONSE = 0x5F,
            GET_INTERNAL_EXP_POWER_ENABLE_COMMAND = 0x60,
            SET_EXG_REGS_COMMAND = 0x61,
            EXG_REGS_RESPONSE = 0x62,
            GET_EXG_REGS_COMMAND = 0x63,
            SET_BAUD_RATE_COMMAND = 0x6A,
            BAUD_RATE_COMMAND_RESPONSE = 0X6B,
            GET_BAUD_RATE_COMMAND = 0X6C,
            DETECT_EXPANSION_BOARD_RESPONSE = 0X65,
            GET_EXPANSION_BOARD_COMMAND = 0x66,
            VBATT_RESPONSE = 0x94,
            GET_VBATT_COMMAND = 0x95,
            SET_VBATT_FREQ_COMMAND = 0x98,
            VBATT_FREQ_RESPONSE = 0x99,
            GET_VBATT_FREQ_COMMAND = 0x9A,
            ACK_PROCESSED = 0xFF
        };

        public enum PacketTypeShimmer3RSDBT : byte
        {
            SET_ALT_ACCEL_RANGE_COMMAND = 0x4F,
            ALT_ACCEL_RANGE_RESPONSE = 0x50,
            GET_ALT_ACCEL_RANGE_COMMAND = 0x51,
            PRESSURE_CALIBRATION_COEFFICIENTS_RESPONSE = 0XA6,
            GET_PRESSURE_CALIBRATION_COEFFICIENTS_COMMAND = 0XA7,
            SET_ALT_MAG_SAMPLING_RATE_COMMAND = 0XB2
        };

        public enum PacketTypeShimmer3SDBT : byte
        {
            START_SDBT_COMMAND = 0x70,
            STATUS_RESPONSE = 0x71,
            GET_STATUS_COMMAND = 0x72,
            SET_TRIAL_CONFIG_COMMAND = 0x73,
            TRIAL_CONFIG_RESPONSE = 0x74,
            GET_TRIAL_CONFIG_COMMAND = 0x75,
            SET_CENTER_COMMAND = 0x76,
            CENTER_RESPONSE = 0x77,
            GET_CENTER_COMMAND = 0x78,
            SET_SHIMMERNAME_COMMAND = 0x79,
            SHIMMERNAME_RESPONSE = 0x7a,
            GET_SHIMMERNAME_COMMAND = 0x7b,
            SET_EXPID_COMMAND = 0x7c,
            EXPID_RESPONSE = 0x7d,
            GET_EXPID_COMMAND = 0x7e,
            SET_MYID_COMMAND = 0x7F,
            MYID_RESPONSE = 0x80,
            GET_MYID_COMMAND = 0x81,
            SET_NSHIMMER_COMMAND = 0x82,
            NSHIMMER_RESPONSE = 0x83,
            GET_NSHIMMER_COMMAND = 0x84,
            SET_CONFIGTIME_COMMAND = 0x85,
            CONFIGTIME_RESPONSE = 0x86,
            GET_CONFIGTIME_COMMAND = 0x87,
            DIR_RESPONSE = 0x88,
            GET_DIR_COMMAND = 0x89,
            INSTREAM_CMD_RESPONSE = 0x8A,
            INFOMEM_RESPONSE = 0x8D,
            GET_INFOMEM_COMMAND = 0x8E,
            SET_RWC_COMMAND = 0x8F,
            RWC_RESPONSE = 0x90,
            GET_RWC_COMMAND = 0x91,
            CALIB_DUMP_RESPONSE = 0x99,
            GET_CALIB_DUMP_COMMAND = 0x9A,
            STOP_SDBT_COMMAND = 0x97,
            BT_FW_VERSION_STR_RESPONSE = 0xA2,
            GET_BT_FW_VERSION_STR_COMMAND = 0xA1
        };

        public enum ConfigSetupByte0Bitmap : byte
        {
            Config5VReg = 0x80,
            ConfigPMux = 0x40,
        }

        public enum ExpansionBoardDetectShimmer3
        {
            EXPANSION_BRIDGE_AMPLIFIER_PLUS = 8,
            EXPANSION_GSR_PLUS = 14,
            EXPANSION_PROTO3_MINI = 36,
            EXPANSION_EXG = 37,
            EXPANSION_PROTO3_DELUXE = 38,
            SHIMMER_3_EXG_EXTENDED = 59,
            SHIMMER3 = 31,
            EXP_BRD_HIGH_G_ACCEL = 44,
            EXP_BRD_GPS = 46,
            EXP_BRD_EXG_UNIFIED = 47,
            EXP_BRD_GSR_UNIFIED = 48,
            EXP_BRD_BR_AMP_UNIFIED = 49,
        }

        public static readonly String[] LIST_OF_BAUD_RATE = { "115200", "1200", "2400", "4800", "9600", "19200", "38400", "57600", "230400", "460800", "921600", "1000000" };
        public static readonly String[] LIST_OF_EXG_ECG_REFERENCE_ELECTRODES = { "Fixed Potential", "Inverse Wilson CT", };
        public static readonly String[] LIST_OF_EXG_EMG_REFERENCE_ELECTRODES = { "Fixed Potential", "Inverse of Ch1" };
        public static readonly String[] LIST_OF_EXG_LEAD_OFF_DETECTION_OPTIONS = { "Off", "DC Current" };
        public static readonly String[] LIST_OF_EXG_LEAD_OFF_CURRENTS = { "6nA", "22nA", "6uA", "22uA" };
        public static readonly String[] LIST_OF_EXG_LEAD_OFF_COMPARATOR_THRESHOLDS = { "Pos:95% - Neg:5%", "Pos:92.5% - Neg:7.5%", "Pos:90% - Neg:10%", "Pos:87.5% - Neg:12.5%", "Pos:85% - Neg:15%", "Pos:80% - Neg:20%", "Pos:75% - Neg:25%", "Pos:70% - Neg:30%" };
        public static readonly String[] LIST_OF_ACCEL_RANGE_SHIMMER2 = { "± 1.5g", "± 2g", "± 4g", "± 6g" };
        public static readonly String[] LIST_OF_MAG_RANGE_SHIMMER2 = { "± 0.7Ga", "± 1.0Ga", "± 1.5Ga", "± 2.0Ga", "± 3.2Ga", "± 3.8Ga", "± 4.5Ga" };
        public static readonly String[] LIST_OF_GSR_RANGE_SHIMMER2 = { "8kOhm to 63kOhm", "63kOhm to 220kOhm", "220kOhm to 680kOhm", "680kOhm to 4.7MOhm", "Auto Range" };
        public static readonly String[] LIST_OF_ACCEL_RANGE_SHIMMER3 = { "+/- 2g", "+/- 4g", "+/- 8g", "+/- 16g" };
        /// <summary>0.625Hz, 1000Hz - Low-power mode only | 560Hz - Medium-performance mode only | 300Hz - High-performance mode only | 155Hz - Ultrahigh-performance mode only | Others - all 4 modes</summary>
        public static readonly String[] LIST_OF_ALT_MAG_RATE_SHIMMER3R = { "0.625Hz","10Hz", "20Hz", "80Hz", "155Hz", "300Hz", "560Hz", "1000Hz" };
        public static readonly String[] LIST_OF_GYRO_RANGE_SHIMMER3 = { "250dps", "500dps", "1000dps", "2000dps" };
        public static readonly String[] LIST_OF_GYRO_RANGE_SHIMMER3R = { "125dps", "250dps", "500dps", "1000dps", "2000dps", "4000dps" };
        public static readonly String[] LIST_OF_MAG_RANGE_SHIMMER3 = { "+/- 1.3Ga", "+/- 1.9Ga", "+/- 2.5Ga", "+/- 4.0Ga", "+/- 4.7Ga", "+/- 5.6Ga", "+/- 8.1Ga" };
        public static readonly String[] LIST_OF_MAG_RANGE_SHIMMER3R = { "+/- 4Ga", "+/- 8Ga", "+/- 12Ga", "+/- 16Ga" };
        public static readonly String[] LIST_OF_PRESSURE_RESOLUTION_SHIMMER3 = { "Low", "Standard", "High", "Very High" };
        public static readonly String[] LIST_OF_PRESSURE_RESOLUTION_SHIMMER3R = { "Ultra Low", "Low", "Standard", "High", "Ultra High", "Highest" };
        public static readonly String[] LIST_OF_GSR_RANGE = { "8kOhm to 63kOhm", "63kOhm to 220kOhm", "220kOhm to 680kOhm", "680kOhm to 4.7MOhm", "Auto Range" };
        public static readonly String[] LIST_OF_EXG_GAINS_SHIMMER3 = new string[] { "1", "2", "3", "4", "6", "8", "12" };

        public static readonly double[,] SENSITIVITY_MATRIX_ACCEL_1_5G_Shimmer2 = new double[3, 3] { { 101, 0, 0 }, { 0, 101, 0 }, { 0, 0, 101 } };
        public static readonly double[,] SENSITIVITY_MATRIX_ACCEL_2G_SHIMMER2 = new double[3, 3] { { 76, 0, 0 }, { 0, 76, 0 }, { 0, 0, 76 } };
        public static readonly double[,] SENSITIVITY_MATRIX_ACCEL_4G_SHIMMER2 = new double[3, 3] { { 38, 0, 0 }, { 0, 38, 0 }, { 0, 0, 38 } };
        public static readonly double[,] SENSITIVITY_MATRIX_ACCEL_6G_SHIMMER2 = new double[3, 3] { { 25, 0, 0 }, { 0, 25, 0 }, { 0, 0, 25 } };
        public static readonly double[,] OFFSET_VECTOR_ACCEL_SHIMMER2 = new double[3, 1] { { 2048 }, { 2048 }, { 2048 } };				//Default Values for Accelerometer Calibration
        public static readonly double[,] ALIGNMENT_MATRIX_ACCEL_SHIMMER2 = new double[3, 3] { { -1, 0, 0 }, { 0, -1, 0 }, { 0, 0, 1 } };

        public static readonly double[,] ALIGNMENT_MATRIX_GYRO_SHIMMER2 = new double[3, 3] { { 0, -1, 0 }, { -1, 0, 0 }, { 0, 0, -1 } }; 				//Default Values for Gyroscope Calibration
        public static readonly double[,] SENSITIVITY_MATRIX_GYRO_SHIMMER2 = new double[3, 3] { { 2.73, 0, 0 }, { 0, 2.73, 0 }, { 0, 0, 2.73 } }; 		//Default Values for Gyroscope Calibration
        public static readonly double[,] OFFSET_VECTOR_GYRO_SHIMMER2 = new double[3, 1] { { 1843 }, { 1843 }, { 1843 } };						//Default Values for Gyroscope Calibration

        public static readonly double[,] ALIGNMENT_MATRIX_MAG_SHIMMER2 = new double[3, 3] { { 1, 0, 0 }, { 0, 1, 0 }, { 0, 0, -1 } }; 				//Default Values for Magnetometer Calibration
        public static readonly double[,] OFFSET_VECTOR_MAG_SHIMMER2 = new double[3, 1] { { 0 }, { 0 }, { 0 } };									//Default Values for Magnetometer Calibration
        public static readonly double[,] SENSITIVITY_MATRIX_MAG_0_8GA_SHIMMER2 = new double[3, 3] { { 1370, 0, 0 }, { 0, 1370, 0 }, { 0, 0, 1370 } };
        public static readonly double[,] SENSITIVITY_MATRIX_MAG_1_3GA_SHIMMER2 = new double[3, 3] { { 1090, 0, 0 }, { 0, 1090, 0 }, { 0, 0, 1090 } };
        public static readonly double[,] SENSITIVITY_MATRIX_MAG_1_9GA_SHIMMER2 = new double[3, 3] { { 820, 0, 0 }, { 0, 820, 0 }, { 0, 0, 820 } };
        public static readonly double[,] SENSITIVITY_MATRIX_MAG_2_5GA_SHIMMER2 = new double[3, 3] { { 660, 0, 0 }, { 0, 660, 0 }, { 0, 0, 660 } };
        public static readonly double[,] SENSITIVITY_MATRIX_MAG_4_0GA_SHIMMER2 = new double[3, 3] { { 440, 0, 0 }, { 0, 440, 0 }, { 0, 0, 440 } };
        public static readonly double[,] SENSITIVITY_MATRIX_MAG_4_7GA_SHIMMER2 = new double[3, 3] { { 390, 0, 0 }, { 0, 390, 0 }, { 0, 0, 390 } };
        public static readonly double[,] SENSITIVITY_MATRIX_MAG_5_6GA_SHIMMER2 = new double[3, 3] { { 330, 0, 0 }, { 0, 330, 0 }, { 0, 0, 330 } };
        public static readonly double[,] SENSITIVITY_MATRIX_MAG_8_1GA_SHIMMER2 = new double[3, 3] { { 230, 0, 0 }, { 0, 230, 0 }, { 0, 0, 230 } };

        [System.Obsolete] // As the shimmer 3 sensors are updated, this is made obsolete, and new variables used which indicate the sensor names e.g. KCRB5-2042
        public static readonly double[,] ALIGNMENT_MATRIX_LOW_NOISE_ACCEL_SHIMMER3 = new double[3, 3] { { 0, -1, 0 }, { -1, 0, 0 }, { 0, 0, -1 } };     //Default Values for Accelerometer Calibration
        [System.Obsolete]
        public static readonly double[,] OFFSET_VECTOR_ACCEL_LOW_NOISE_SHIMMER3 = new double[3, 1] { { 2047 }, { 2047 }, { 2047 } };                //Default Values for Accelerometer Calibration
        [System.Obsolete]
        public static readonly double[,] SENSITIVITY_MATRIX_LOW_NOISE_ACCEL_SHIMMER3 = new double[3, 3] { { 83, 0, 0 }, { 0, 83, 0 }, { 0, 0, 83 } };

        public static readonly double[,] ALIGNMENT_MATRIX_LOW_NOISE_ACCEL_SHIMMER3_KCRB5_2042 = new double[3, 3] { { 0, -1, 0 }, { -1, 0, 0 }, { 0, 0, -1 } };     //Default Values for Accelerometer Calibration
        public static readonly double[,] OFFSET_VECTOR_ACCEL_LOW_NOISE_SHIMMER3_KCRB5_2042 = new double[3, 1] { { 2047 }, { 2047 }, { 2047 } };                //Default Values for Accelerometer Calibration
        public static readonly double[,] SENSITIVITY_MATRIX_LOW_NOISE_ACCEL_SHIMMER3_KCRB5_2042 = new double[3, 3] { { 83, 0, 0 }, { 0, 83, 0 }, { 0, 0, 83 } };

        public static readonly double[,] ALIGNMENT_MATRIX_LOW_NOISE_ACCEL_SHIMMER3_KXTC9_2050 = new double[3, 3] { { 0, -1, 0 }, { -1, 0, 0 }, { 0, 0, -1 } };     //Default Values for Accelerometer Calibration
        public static readonly double[,] OFFSET_VECTOR_ACCEL_LOW_NOISE_SHIMMER3_KXTC9_2050 = new double[3, 1] { { 2253 }, { 2253 }, { 2253 } };                //Default Values for Accelerometer Calibration
        public static readonly double[,] SENSITIVITY_MATRIX_LOW_NOISE_ACCEL_SHIMMER3_KXTC9_2050 = new double[3, 3] { { 92, 0, 0 }, { 0, 92, 0 }, { 0, 0, 92 } };

        [System.Obsolete] // As the shimmer 3 sensors are updated, this is made obsolete, and new variables used which indicate the sensor names e.g. LSM303DLHC
        public static readonly double[,] SENSITIVITY_MATRIX_WIDE_RANGE_ACCEL_2G_SHIMMER3 = new double[3, 3] { { 1631, 0, 0 }, { 0, 1631, 0 }, { 0, 0, 1631 } };
        [System.Obsolete]
        public static readonly double[,] SENSITIVITY_MATRIX_WIDE_RANGE_ACCEL_4G_SHIMMER3 = new double[3, 3] { { 815, 0, 0 }, { 0, 815, 0 }, { 0, 0, 815 } };
        [System.Obsolete]
        public static readonly double[,] SENSITIVITY_MATRIX_WIDE_RANGE_ACCEL_8G_SHIMMER3 = new double[3, 3] { { 408, 0, 0 }, { 0, 408, 0 }, { 0, 0, 408 } };
        [System.Obsolete]
        public static readonly double[,] SENSITIVITY_MATRIX_WIDE_RANGE_ACCEL_16G_SHIMMER3 = new double[3, 3] { { 135, 0, 0 }, { 0, 135, 0 }, { 0, 0, 135 } };
        [System.Obsolete]
        public static readonly double[,] ALIGNMENT_MATRIX_WIDE_RANGE_ACCEL_SHIMMER3 = new double[3, 3] { { -1, 0, 0 }, { 0, 1, 0 }, { 0, 0, -1 } };     //Default Values for Accelerometer Calibration
        [System.Obsolete]
        public static readonly double[,] OFFSET_VECTOR_ACCEL_WIDE_RANGE_SHIMMER3 = new double[3, 1] { { 0 }, { 0 }, { 0 } };				//Default Values for Accelerometer Calibration

        public static readonly double[,] SENSITIVITY_MATRIX_WIDE_RANGE_ACCEL_2G_SHIMMER3_LSM303DLHC = new double[3, 3] { { 1631, 0, 0 }, { 0, 1631, 0 }, { 0, 0, 1631 } };
        public static readonly double[,] SENSITIVITY_MATRIX_WIDE_RANGE_ACCEL_4G_SHIMMER3_LSM303DLHC = new double[3, 3] { { 815, 0, 0 }, { 0, 815, 0 }, { 0, 0, 815 } };
        public static readonly double[,] SENSITIVITY_MATRIX_WIDE_RANGE_ACCEL_8G_SHIMMER3_LSM303DLHC = new double[3, 3] { { 408, 0, 0 }, { 0, 408, 0 }, { 0, 0, 408 } };
        public static readonly double[,] SENSITIVITY_MATRIX_WIDE_RANGE_ACCEL_16G_SHIMMER3_LSM303DLHC = new double[3, 3] { { 135, 0, 0 }, { 0, 135, 0 }, { 0, 0, 135 } };
        public static readonly double[,] ALIGNMENT_MATRIX_WIDE_RANGE_ACCEL_SHIMMER3_LSM303DLHC = new double[3, 3] { { -1, 0, 0 }, { 0, 1, 0 }, { 0, 0, -1 } };     //Default Values for Accelerometer Calibration
        public static readonly double[,] OFFSET_VECTOR_ACCEL_WIDE_RANGE_SHIMMER3_LSM303DLHC = new double[3, 1] { { 0 }, { 0 }, { 0 } };                //Default Values for Accelerometer Calibration

        public static readonly double[,] SENSITIVITY_MATRIX_WIDE_RANGE_ACCEL_2G_SHIMMER3_LSM303AH = new double[3, 3] { { 1671, 0, 0 }, { 0, 1671, 0 }, { 0, 0, 1671 } };
        public static readonly double[,] SENSITIVITY_MATRIX_WIDE_RANGE_ACCEL_4G_SHIMMER3_LSM303AH = new double[3, 3] { { 836, 0, 0 }, { 0, 836, 0 }, { 0, 0, 836 } };
        public static readonly double[,] SENSITIVITY_MATRIX_WIDE_RANGE_ACCEL_8G_SHIMMER3_LSM303AH = new double[3, 3] { { 418, 0, 0 }, { 0, 418, 0 }, { 0, 0, 418 } };
        public static readonly double[,] SENSITIVITY_MATRIX_WIDE_RANGE_ACCEL_16G_SHIMMER3_LSM303AH = new double[3, 3] { { 209, 0, 0 }, { 0, 209, 0 }, { 0, 0, 209 } };
        public static readonly double[,] ALIGNMENT_MATRIX_WIDE_RANGE_ACCEL_SHIMMER3_LSM303AH = new double[3, 3] { { 0, -1, 0 }, { 1, 0, 0 }, { 0, 0, -1 } };     //Default Values for Accelerometer Calibration
        public static readonly double[,] OFFSET_VECTOR_ACCEL_WIDE_RANGE_SHIMMER3_LSM303AH = new double[3, 1] { { 0 }, { 0 }, { 0 } };                //Default Values for Accelerometer Calibration

        public static readonly double[,] SENSITIVITY_MATRIX_HIGH_G_ACCEL_200G_SHIMMER3R_ADXL371 = new double[3, 3] { { 1, 0, 0 }, { 0, 1, 0 }, { 0, 0, 1 } };
        public static readonly double[,] ALIGNMENT_MATRIX_HIGH_G_ACCEL_SHIMMER3R_ADXL371 = new double[3, 3] { { 0, 1, 0 }, { 1, 0, 0 }, { 0, 0, -1 } };     //Default Values for Accelerometer Calibration
        public static readonly double[,] OFFSET_VECTOR_ACCEL_HIGH_G_SHIMMER3R_ADXL371 = new double[3, 1] { { 10 }, { 10 }, { 10 } };                //Default Values for Accelerometer Calibration
        public static readonly double[,] SENSITIVITY_MATRIX_WIDE_RANGE_ACCEL_2G_SHIMMER3R_LIS2DW12 = new double[3, 3] { { 1671, 0, 0 }, { 0, 1671, 0 }, { 0, 0, 1671 } };
        public static readonly double[,] SENSITIVITY_MATRIX_WIDE_RANGE_ACCEL_4G_SHIMMER3R_LIS2DW12 = new double[3, 3] { { 836, 0, 0 }, { 0, 836, 0 }, { 0, 0, 836 } };
        public static readonly double[,] SENSITIVITY_MATRIX_WIDE_RANGE_ACCEL_8G_SHIMMER3R_LIS2DW12 = new double[3, 3] { { 418, 0, 0 }, { 0, 418, 0 }, { 0, 0, 418 } };
        public static readonly double[,] SENSITIVITY_MATRIX_WIDE_RANGE_ACCEL_16G_SHIMMER3R_LIS2DW12 = new double[3, 3] { { 209, 0, 0 }, { 0, 209, 0 }, { 0, 0, 209 } };
        public static readonly double[,] ALIGNMENT_MATRIX_WIDE_RANGE_ACCEL_SHIMMER3R_LIS2DW12 = new double[3, 3] { { 0, -1, 0 }, { -1, 0, 0 }, { 0, 0, -1 } };     //Default Values for Accelerometer Calibration
        public static readonly double[,] OFFSET_VECTOR_ACCEL_WIDE_RANGE_SHIMMER3R_LIS2DW12 = new double[3, 1] { { 0 }, { 0 }, { 0 } };                //Default Values for Accelerometer Calibration

        public static readonly double[,] ALIGNMENT_MATRIX_GYRO_SHIMMER3 = new double[3, 3] { { 0, -1, 0 }, { -1, 0, 0 }, { 0, 0, -1 } }; 				//Default Values for Gyroscope Calibration
        public static readonly double[,] SENSITIVITIY_MATRIX_GYRO_250DPS_SHIMMER3 = new double[3, 3] { { 131, 0, 0 }, { 0, 131, 0 }, { 0, 0, 131 } }; 		//Default Values for Gyroscope Calibration
        public static readonly double[,] SENSITIVITIY_MATRIX_GYRO_500DPS_SHIMMER3 = new double[3, 3] { { 65.5, 0, 0 }, { 0, 65.5, 0 }, { 0, 0, 65.5 } }; 		//Default Values for Gyroscope Calibration
        public static readonly double[,] SENSITIVITIY_MATRIX_GYRO_1000DPS_SHIMMER3 = new double[3, 3] { { 32.8, 0, 0 }, { 0, 32.8, 0 }, { 0, 0, 32.8 } }; 		//Default Values for Gyroscope Calibration
        public static readonly double[,] SENSITIVITIY_MATRIX_GYRO_2000DPS_SHIMMER3 = new double[3, 3] { { 16.4, 0, 0 }, { 0, 16.4, 0 }, { 0, 0, 16.4 } }; 		//Default Values for Gyroscope Calibration
        public static readonly double[,] OFFSET_VECTOR_GYRO_SHIMMER3 = new double[3, 1] { { 0 }, { 0 }, { 0 } };                        //Default Values for Gyroscope Calibration

        public static readonly double[,] ALIGNMENT_MATRIX_GYRO_SHIMMER3R_LSM6DSV = new double[3, 3] { { -1, 0, 0 }, { 0, 1, 0 }, { 0, 0, -1 } };               //Default Values for Gyroscope Calibration
        public static readonly double[,] SENSITIVITIY_MATRIX_GYRO_125DPS_SHIMMER3R_LSM6DSV = new double[3, 3] { { 229, 0, 0 }, { 0, 229, 0 }, { 0, 0, 229 } };      //Default Values for Gyroscope Calibration
        public static readonly double[,] SENSITIVITIY_MATRIX_GYRO_250DPS_SHIMMER3R_LSM6DSV = new double[3, 3] { { 114, 0, 0 }, { 0, 114, 0 }, { 0, 0, 114 } }; 		//Default Values for Gyroscope Calibration
        public static readonly double[,] SENSITIVITIY_MATRIX_GYRO_500DPS_SHIMMER3R_LSM6DSV = new double[3, 3] { { 57, 0, 0 }, { 0, 57, 0 }, { 0, 0, 57 } }; 		//Default Values for Gyroscope Calibration
        public static readonly double[,] SENSITIVITIY_MATRIX_GYRO_1000DPS_SHIMMER3R_LSM6DSV = new double[3, 3] { { 29, 0, 0 }, { 0, 29, 0 }, { 0, 0, 29 } }; 		//Default Values for Gyroscope Calibration
        public static readonly double[,] SENSITIVITIY_MATRIX_GYRO_2000DPS_SHIMMER3R_LSM6DSV = new double[3, 3] { { 14, 0, 0 }, { 0, 14, 0 }, { 0, 0, 14 } };      //Default Values for Gyroscope Calibration
        public static readonly double[,] SENSITIVITIY_MATRIX_GYRO_4000DPS_SHIMMER3R_LSM6DSV = new double[3, 3] { { 7, 0, 0 }, { 0, 7, 0 }, { 0, 0, 7 } };      //Default Values for Gyroscope Calibration
        public static readonly double[,] OFFSET_VECTOR_GYRO_SHIMMER3R_LSM6DSV = new double[3, 1] { { 0 }, { 0 }, { 0 } };						//Default Values for Gyroscope Calibration

        // Shimmer3r LN Accel
        public static readonly double[,] SENSITIVITY_MATRIX_LOW_NOISE_ACCEL_2G_SHIMMER3R_LSM6DSV = new double[3, 3] { { 1672, 0, 0 }, { 0, 1672, 0 }, { 0, 0, 1672 } };
        public static readonly double[,] SENSITIVITY_MATRIX_LOW_NOISE_ACCEL_4G_SHIMMER3R_LSM6DSV = new double[3, 3] { { 836, 0, 0 }, { 0, 836, 0 }, { 0, 0, 836 } };
        public static readonly double[,] SENSITIVITY_MATRIX_LOW_NOISE_ACCEL_8G_SHIMMER3R_LSM6DSV = new double[3, 3] { { 418, 0, 0 }, { 0, 418, 0 }, { 0, 0, 418 } };
        public static readonly double[,] SENSITIVITY_MATRIX_LOW_NOISE_ACCEL_16G_SHIMMER3R_LSM6DSV = new double[3, 3] { { 209, 0, 0 }, { 0, 209, 0 }, { 0, 0, 209 } };
        public static readonly double[,] ALIGNMENT_MATRIX_LOW_NOISE_ACCEL_SHIMMER3R_LSM6DSV = new double[3, 3] { { -1, 0, 0 }, { 0, 1, 0 }, { 0, 0, -1 } };     //Default Values for Accelerometer Calibration
        public static readonly double[,] OFFSET_VECTOR_ACCEL_LOW_NOISE_SHIMMER3R_LSM6DSV = new double[3, 1] { { 0 }, { 0 }, { 0 } };                //Default Values for Accelerometer Calibration


        public static readonly double[] SHIMMER3_GSR_REF_RESISTORS_KOHMS = new double[] {
            40.200, 	//Range 0
			287.000, 	//Range 1
			1000.000, 	//Range 2
			3300.000};  //Range 3

        // Equation breaks down below 683 for range 3
        public static readonly int GSR_UNCAL_LIMIT_RANGE3 = 683;

        public static readonly double[,] SHIMMER3_GSR_RESISTANCE_MIN_MAX_KOHMS = new double[,] {
            {8.0, 63.0}, 		//Range 0
			{63.0, 220.0}, 		//Range 1
			{220.0, 680.0}, 	//Range 2
			{680.0, 4700.0}}; 	//Range 3


        [System.Obsolete]
        public static readonly double[,] ALIGNMENT_MATRIX_MAG_SHIMMER3 = new double[3, 3] { { -1, 0, 0 }, { 0, 1, 0 }, { 0, 0, -1 } };              //Default Values for Magnetometer Calibration
        [System.Obsolete]
        public static readonly double[,] OFFSET_VECTOR_MAG_SHIMMER3 = new double[3, 1] { { 0 }, { 0 }, { 0 } };
        [System.Obsolete]
        public static readonly double[,] SENSITIVITY_MATRIX_MAG_1_3GA_SHIMMER3 = new double[3, 3] { { 1100, 0, 0 }, { 0, 1100, 0 }, { 0, 0, 980 } };
        [System.Obsolete]
        public static readonly double[,] SENSITIVITY_MATRIX_MAG_1_9GA_SHIMMER3 = new double[3, 3] { { 855, 0, 0 }, { 0, 855, 0 }, { 0, 0, 760 } };
        [System.Obsolete]
        public static readonly double[,] SENSITIVITY_MATRIX_MAG_2_5GA_SHIMMER3 = new double[3, 3] { { 670, 0, 0 }, { 0, 670, 0 }, { 0, 0, 600 } };
        [System.Obsolete]
        public static readonly double[,] SENSITIVITY_MATRIX_MAG_4_0GA_SHIMMER3 = new double[3, 3] { { 450, 0, 0 }, { 0, 450, 0 }, { 0, 0, 400 } };
        [System.Obsolete]
        public static readonly double[,] SENSITIVITY_MATRIX_MAG_4_7GA_SHIMMER3 = new double[3, 3] { { 400, 0, 0 }, { 0, 400, 0 }, { 0, 0, 355 } };
        [System.Obsolete]
        public static readonly double[,] SENSITIVITY_MATRIX_MAG_5_6GA_SHIMMER3 = new double[3, 3] { { 330, 0, 0 }, { 0, 330, 0 }, { 0, 0, 295 } };
        [System.Obsolete]
        public static readonly double[,] SENSITIVITY_MATRIX_MAG_8_1GA_SHIMMER3 = new double[3, 3] { { 230, 0, 0 }, { 0, 230, 0 }, { 0, 0, 205 } };

        public static readonly double[,] ALIGNMENT_MATRIX_MAG_SHIMMER3_LSM303DLHC = new double[3, 3] { { -1, 0, 0 }, { 0, 1, 0 }, { 0, 0, -1 } }; 				//Default Values for Magnetometer Calibration
        public static readonly double[,] OFFSET_VECTOR_MAG_SHIMMER3_LSM303DLHC = new double[3, 1] { { 0 }, { 0 }, { 0 } };
        public static readonly double[,] SENSITIVITY_MATRIX_MAG_1_3GA_SHIMMER3_LSM303DLHC = new double[3, 3] { { 1100, 0, 0 }, { 0, 1100, 0 }, { 0, 0, 980 } };
        public static readonly double[,] SENSITIVITY_MATRIX_MAG_1_9GA_SHIMMER3_LSM303DLHC = new double[3, 3] { { 855, 0, 0 }, { 0, 855, 0 }, { 0, 0, 760 } };
        public static readonly double[,] SENSITIVITY_MATRIX_MAG_2_5GA_SHIMMER3_LSM303DLHC = new double[3, 3] { { 670, 0, 0 }, { 0, 670, 0 }, { 0, 0, 600 } };
        public static readonly double[,] SENSITIVITY_MATRIX_MAG_4_0GA_SHIMMER3_LSM303DLHC = new double[3, 3] { { 450, 0, 0 }, { 0, 450, 0 }, { 0, 0, 400 } };
        public static readonly double[,] SENSITIVITY_MATRIX_MAG_4_7GA_SHIMMER3_LSM303DLHC = new double[3, 3] { { 400, 0, 0 }, { 0, 400, 0 }, { 0, 0, 355 } };
        public static readonly double[,] SENSITIVITY_MATRIX_MAG_5_6GA_SHIMMER3_LSM303DLHC = new double[3, 3] { { 330, 0, 0 }, { 0, 330, 0 }, { 0, 0, 295 } };
        public static readonly double[,] SENSITIVITY_MATRIX_MAG_8_1GA_SHIMMER3_LSM303DLHC = new double[3, 3] { { 230, 0, 0 }, { 0, 230, 0 }, { 0, 0, 205 } };

        public static readonly double[,] ALIGNMENT_MATRIX_MAG_SHIMMER3_LSM303AH = new double[3, 3] { { 0, -1, 0 }, { 1, 0, 0 }, { 0, 0, -1 } }; 				//Default Values for Magnetometer Calibration
        public static readonly double[,] OFFSET_VECTOR_MAG_SHIMMER3_LSM303AH = new double[3, 1] { { 0 }, { 0 }, { 0 } };
        public static readonly double[,] SENSITIVITY_MATRIX_MAG_50GA_SHIMMER3_LSM303AH = new double[3, 3] { { 667, 0, 0 }, { 0, 667, 0 }, { 0, 0, 667 } };

        public static readonly double[,] ALIGNMENT_MATRIX_MAG_SHIMMER3R_LIS3MDL = new double[3, 3] { { 1, 0, 0 }, { 0, -1, 0 }, { 0, 0, -1 } };
        public static readonly double[,] OFFSET_VECTOR_MAG_SHIMMER3R_LIS3MDL = new double[3, 1] { { 0 }, { 0 }, { 0 } };
        public static readonly double[,] SENSITIVITY_MATRIX_MAG_4GA_SHIMMER3R_LIS3MDL = new double[3, 3] { { 6842, 0, 0 }, { 0, 6842, 0 }, { 0, 0, 6842 } };
        public static readonly double[,] SENSITIVITY_MATRIX_MAG_8GA_SHIMMER3R_LIS3MDL = new double[3, 3] { { 3421, 0, 0 }, { 0, 3421, 0 }, { 0, 0, 3421 } };
        public static readonly double[,] SENSITIVITY_MATRIX_MAG_12GA_SHIMMER3R_LIS3MDL = new double[3, 3] { { 2281, 0, 0 }, { 0, 2281, 0 }, { 0, 0, 2281 } };
        public static readonly double[,] SENSITIVITY_MATRIX_MAG_16GA_SHIMMER3R_LIS3MDL = new double[3, 3] { { 1711, 0, 0 }, { 0, 1711, 0 }, { 0, 0, 1711 } };

        public static readonly double[,] ALIGNMENT_MATRIX_MAG_SHIMMER3R_LIS2MDL = new double[3, 3] { { -1, 0, 0 }, { 0, -1, 0 }, { 0, 0, -1 } };                //Default Values for Magnetometer Calibration
        public static readonly double[,] OFFSET_VECTOR_MAG_SHIMMER3R_LIS2MDL = new double[3, 1] { { 0 }, { 0 }, { 0 } };
        public static readonly double[,] SENSITIVITY_MATRIX_MAG_50GA_SHIMMER3R_LIS2MDL = new double[3, 3] { { 667, 0, 0 }, { 0, 667, 0 }, { 0, 0, 667 } };

        public static readonly byte[] SHIMMER3_DEFAULT_ECG_REG1 = new byte[10] { 0x00, 0xA0, 0x10, 0x40, 0x40, 0x2D, 0x00, 0x00, 0x02, 0x03 };
        public static readonly byte[] SHIMMER3_DEFAULT_ECG_REG2 = new byte[10] { 0x00, 0xA0, 0x10, 0x40, 0x47, 0x00, 0x00, 0x00, 0x02, 0x01 };
        public static readonly byte[] SHIMMER3_DEFAULT_EMG_REG1 = new byte[10] { 0x00, 0xA0, 0x10, 0x69, 0x60, 0x20, 0x00, 0x00, 0x02, 0x03 };
        public static readonly byte[] SHIMMER3_DEFAULT_EMG_REG2 = new byte[10] { 0x00, 0xA0, 0x10, 0xE1, 0xE1, 0x00, 0x00, 0x00, 0x02, 0x01 };
        public static readonly byte[] SHIMMER3_DEFAULT_TEST_REG1 = new byte[10] { 0x00, 0xA3, 0x10, 0x45, 0x45, 0x00, 0x00, 0x00, 0x02, 0x01 };
        public static readonly byte[] SHIMMER3_DEFAULT_TEST_REG2 = new byte[10] { 0x00, 0xA3, 0x10, 0x45, 0x45, 0x00, 0x00, 0x00, 0x02, 0x01 };
        public static readonly byte[] SHIMMER3_DEFAULT_RESPIRATION_REG1 = new byte[10] { 0x02, 0xA8, 0x10, 0x40, 0x40, 0x20, 0x00, 0x00, 0x02, 0x03 };
        public static readonly byte[] SHIMMER3_DEFAULT_RESPIRATION_REG2 = new byte[10] { 0x02, 0xA0, 0x10, 0x40, 0x40, 0x00, 0x00, 0x00, 0xEA, 0x01 };



        public int ReadTimeout = 1000; //ms
        public int WriteTimeout = 1000; //ms

        //EXG
        public byte[] Exg1RegArray = new byte[10];
        public byte[] Exg2RegArray = new byte[10];

        // Quantized Trim Variables
        public double Bmp3QuantizedCalibData_ParT1;
        public double Bmp3QuantizedCalibData_ParT2;
        public double Bmp3QuantizedCalibData_ParT3;
        public double Bmp3QuantizedCalibData_ParP1;
        public double Bmp3QuantizedCalibData_ParP2;
        public double Bmp3QuantizedCalibData_ParP3;
        public double Bmp3QuantizedCalibData_ParP4;
        public double Bmp3QuantizedCalibData_ParP5;
        public double Bmp3QuantizedCalibData_ParP6;
        public double Bmp3QuantizedCalibData_ParP7;
        public double Bmp3QuantizedCalibData_ParP8;
        public double Bmp3QuantizedCalibData_ParP9;
        public double Bmp3QuantizedCalibData_ParP10;
        public double Bmp3QuantizedCalibData_ParP11;
        public double Bmp3QuantizedCalibData_TLin;



        // Trim Variables
        public ushort Bmp3RegCalibData_ParT1;
        public ushort Bmp3RegCalibData_ParT2;
        public sbyte Bmp3RegCalibData_ParT3;
        public short Bmp3RegCalibData_ParP1;
        public short Bmp3RegCalibData_ParP2;
        public sbyte Bmp3RegCalibData_ParP3;
        public sbyte Bmp3RegCalibData_ParP4;
        public ushort Bmp3RegCalibData_ParP5;
        public ushort Bmp3RegCalibData_ParP6;
        public sbyte Bmp3RegCalibData_ParP7;
        public sbyte Bmp3RegCalibData_ParP8;
        public short Bmp3RegCalibData_ParP9;
        public sbyte Bmp3RegCalibData_ParP10;
        public sbyte Bmp3RegCalibData_ParP11;
        public long Bmp3RegCalibData_TLin;



        public ShimmerBluetooth()
        {
        }

        //This Constructor is for both Shimmer2, Shimmer3 and ShimmerECGMD where upon connection the Settings on the Shimmer device is read and saved on the API; see bool variable SetupDevice
        public ShimmerBluetooth(String devName)
        {
            DeviceName = devName;
            SetupDevice = false;
        }

        /// <summary>
        /// ShimmerECGMD constructor, to set the Shimmer device according to specified settings upon connection
        /// </summary>
        /// <param name="devName">User Defined Device Name</param>
        /// <param name="samplingRate">Sampling rate in Hz</param>
        /// <param name="setEnabledSensors">see Shimmer.SensorBitmapShimmer3</param>
        /// <param name="exg1configuration">10 byte value, see SHIMMER3_DEFAULT_ECG_REG1/SHIMMER3_DEFAULT_EMG_REG1/SHIMMER3_DEFAULT_TEST_REG1/SHIMMER3_DEFAULT_RESPIRATION_REG1</param>
        /// <param name="exg2configuration">10 byte value, see SHIMMER3_DEFAULT_ECG_REG2/SHIMMER3_DEFAULT_EMG_REG2/SHIMMER3_DEFAULT_TEST_REG2/SHIMMER3_DEFAULT_RESPIRATION_REG2</param>
        public ShimmerBluetooth(String devName, double samplingRate, int setEnabledSensors, byte[] exg1configuration, byte[] exg2configuration)
        {
            DeviceName = devName;
            SamplingRate = samplingRate;
            SetEnabledSensors = setEnabledSensors;
            Array.Copy(exg1configuration, Exg1RegArray, 10);
            Array.Copy(exg2configuration, Exg2RegArray, 10);
            SetupDevice = true;
        }

        //Shimmer3 constructor, to set the Shimmer device according to specified settings upon connection, please avoid using this constructor if you are not using a Shimmer3 device
        /// <summary>
        /// Shimmer3 constructor, to set the Shimmer device according to specified settings upon connection
        /// </summary>
        /// <param name="devName">User Defined Device Name</param>
        /// <param name="samplingRate">Sampling rate in Hz</param>
        /// <param name="accelRange">Shimmer3 options - 0,1,2,3,4 = 2g,4g,8g,16g.</param>
        /// <param name="gsrRange">Range is between 0 and 4. 0 = 8-63kOhm, 1 = 63-220kOhm, 2 = 220-680kOhm, 3 = 680kOhm-4.7MOhm, 4 = Auto range</param>
        /// <param name="setEnabledSensors">see Shimmer.SensorBitmapShimmer3, for multiple sensors use an or operation</param>
        /// <param name="enableLowPowerAccel"></param>
        /// <param name="enableLowPowerGyro"></param>
        /// <param name="enableLowPowerMag"></param>
        /// <param name="gyroRange">Options are 0,1,2,3. Where 0 = 250 Degree/s, 1 = 500 Degree/s, 2 = 1000 Degree/s, 3 = 2000 Degree/s</param>
        /// <param name="magRange">Shimmer3: 1,2,3,4,5,6,7 = 1.3, 1.9, 2.5, 4.0, 4.7, 5.6, 8.1</param>
        /// <param name="exg1configuration">10 byte value, see SHIMMER3_DEFAULT_ECG_REG1/SHIMMER3_DEFAULT_EMG_REG1/SHIMMER3_DEFAULT_TEST_REG1/SHIMMER3_DEFAULT_RESPIRATION_REG1</param> , note that the EXG data rate is automatically updated based on the Shimmer sampling rate
        /// <param name="exg2configuration">10 byte value, see SHIMMER3_DEFAULT_ECG_REG2/SHIMMER3_DEFAULT_EMG_REG2/SHIMMER3_DEFAULT_TEST_REG2/SHIMMER3_DEFAULT_RESPIRATION_REG2</param> , note that the EXG data rate is automatically updated based on the Shimmer sampling rate
        /// <param name="internalExpPower"></param>
        public ShimmerBluetooth(String devName, double samplingRate, int accelRange, int gsrRange, int setEnabledSensors, bool enableLowPowerAccel, bool enableLowPowerGyro, bool enableLowPowerMag, int gyroRange, int magRange, byte[] exg1configuration, byte[] exg2configuration, bool internalExpPower)
        {
            DeviceName = devName;
            SamplingRate = samplingRate;
            AccelRange = accelRange;
            MagGain = magRange;
            GSRRange = gsrRange;
            GyroRange = gyroRange;
            SetEnabledSensors = setEnabledSensors;
            Array.Copy(exg1configuration, Exg1RegArray, 10);
            Array.Copy(exg2configuration, Exg2RegArray, 10);
            LowPowerAccelEnabled = enableLowPowerAccel;
            LowPowerGyroEnabled = enableLowPowerGyro;
            LowPowerMagEnabled = enableLowPowerMag;
            SetupDevice = true;
            if (internalExpPower)
            {
                InternalExpPower = 1;
            }
            else
            {
                InternalExpPower = 0;
            }
        }

        //Shimmer2 constructor, to set the Shimmer device according to specified settings upon connection
        /// <summary>
        /// Shimmer2 constructor, to set the Shimmer device according to specified settings upon connection
        /// </summary>
        /// <param name="devName">User Defined Device Name</param>
        /// <param name="samplingRate">Sampling rate in Hz</param>
        /// <param name="accelRange">Shimmer2r options - 0,1,2,3 = 1.5g,2g,4g,6g</param>
        /// <param name="gsrRange">Range is between 0 and 4. 0 = 8-63kOhm, 1 = 63-220kOhm, 2 = 220-680kOhm, 3 = 680kOhm-4.7MOhm, 4 = Auto range</param>
        /// <param name="magGain">Shimmer2R: 0,1,2,3,4,5,6 = 0.7,1.0,1.5,2.0,3.2,3.8,4.5 </param>
        /// <param name="setEnabledSensors">see Shimmer.SensorBitmapShimmer2, for multiple sensors use an or operation</param>
        public ShimmerBluetooth(String devName, double samplingRate, int accelRange, int gsrRange, int magGain, int setEnabledSensors)
        {
            DeviceName = devName;
            SamplingRate = samplingRate;
            AccelRange = accelRange;
            MagGain = magGain;
            GSRRange = gsrRange;
            SetEnabledSensors = setEnabledSensors;
            SetupDevice = true;
        }

        protected void InitializeSensors()
        {
            SensorLNAccel = new LNAccel(HardwareVersion);
            SensorGyro = new GyroSensor(HardwareVersion);
            SensorHighGAccel = new HighGAccel(HardwareVersion);
            SensorWRAccel = new WRAccel(HardwareVersion);
            SensorMag = new MagSensor(HardwareVersion);
            SensorWRMag = new WRMag(HardwareVersion);
        }

        public void StartConnectThread()
        {

            if (GetState() == SHIMMER_STATE_NONE)
            {
                SetState(SHIMMER_STATE_CONNECTING);
                ConnectThread = new Thread(new ThreadStart(Connect));
                ConnectThread.Name = "Connect Thread for Device: " + DeviceName;
                ConnectThread.Start();
                if (TimerConnect != null)
                {

                }
                else
                {
                    TimerConnect = new System.Timers.Timer(20000); // Set up the timer for connecting test
                    TimerConnect.Elapsed += new ElapsedEventHandler(ConnectTimerElapsed);
                }
                TimerConnect.Start(); // Enable it
            }
        }
        protected abstract bool IsConnectionOpen();
        protected abstract void OpenConnection();
        protected abstract void CloseConnection();
        protected abstract void FlushConnection();
        protected abstract void FlushInputConnection();
        protected abstract void WriteBytes(byte[] b, int index, int length);
        protected abstract int ReadByte();

        protected int ConnectWaitDurationinmS = 500;

        public void Connect()
        {
            if (!IsConnectionOpen())
            {
                try
                {
                    StreamTimeOutCount = 0;
                    BluetoothCRCMode = BTCRCMode.OFF; //can only be enabled upon connection
                    OpenConnection();

                    StopReading = false;
                    ReadThread = new Thread(new ThreadStart(ReadData));
                    ReadThread.Name = "Read Thread for Device: " + DeviceName;
                    ReadThread.Start();
                    // give the shimmer time to make the changes before continuing
                    System.Threading.Thread.Sleep(ConnectWaitDurationinmS);
                    // Read Shimmer Profile
                    if (IsConnectionOpen())
                    {
                        // Set default firmware version values, if there is not response it means that this values remain, and the old firmware version has been detected
                        // The following are the three main identifiers used to identify the firmware version
                        FirmwareIdentifier = 1;

                        FirmwareVersionFullName = "BoilerPlate 0.1.0";
                        FirmwareInternal = 0;

                        WriteBytes(new byte[1] { (byte)PacketTypeShimmer2.GET_FW_VERSION_COMMAND }, 0, 1);
                        System.Threading.Thread.Sleep(200);

                        WriteBytes(new byte[1] { (byte)PacketTypeShimmer2.GET_FW_VERSION_COMMAND }, 0, 1);
                        System.Threading.Thread.Sleep(200);

                        if (FirmwareMajor == 1 && FirmwareMinor == 2)//FirmwareVersion != 1.2) //Shimmer2r and Shimmer3 commands differ, using FWVersion to determine if its a Shimmer2r for the time being, future revisions of BTStream (Shimmer2r, should update the command to 3F)
                        {
                            WriteBytes(new byte[1] { (byte)PacketTypeShimmer2.GET_SHIMMER_VERSION_COMMAND }, 0, 1);
                        }
                        else
                        {
                            WriteBytes(new byte[1] { (byte)PacketTypeShimmer3.GET_SHIMMER_VERSION_COMMAND }, 0, 1);
                        }
                        System.Threading.Thread.Sleep(400);

                        ReadBlinkLED();
                        InitializeSensors();
                        if (HardwareVersion == (int)ShimmerBluetooth.ShimmerVersion.SHIMMER2R || HardwareVersion == (int)ShimmerBluetooth.ShimmerVersion.SHIMMER2)
                        {
                            InitializeShimmer2();
                        }
                        else if (HardwareVersion == (int)ShimmerBluetooth.ShimmerVersion.SHIMMER3)
                        {
                            if (GetFirmwareIdentifier() == FW_IDENTIFIER_LOGANDSTREAM)
                            {
                                //WriteBatteryFrequency(0);
                                ReadExpansionBoard();
                                InitializeShimmer3SDBT();
                            }
                            else if (GetFirmwareIdentifier() == FW_IDENTIFIER_BTSTREAM)
                            {
                                //WriteBatteryFrequency(0);
                                InitializeShimmer3();
                            }
                            else if (GetFirmwareIdentifier() == 13)
                            {
                                //WriteBatteryFrequency(0);
                                InitializeShimmer3();
                            }
                            else if (GetFirmwareIdentifier() == FW_IDENTIFIER_SHIMMERECGMD)
                            {
                                //WriteBatteryFrequency(0);
                                InitializeShimmerECGMD();
                            }
                        }
                        else if (HardwareVersion == (int)ShimmerVersion.SHIMMER3R)
                        {
                            if (GetFirmwareIdentifier() == FW_IDENTIFIER_LOGANDSTREAM)
                            {
                                //WriteBatteryFrequency(0);
                                ReadExpansionBoard();
                                InitializeShimmer3SDBT();
                            }
                        }
                    }
                }
                catch
                {
                    if (TimerConnect != null)
                    {
                        TimerConnect.Stop(); // Enable it
                    }
                    StopReading = true;
                    ReadThread = null;
                    SetState(SHIMMER_STATE_NONE);
                    EventHandler handler = UICallback;
                    if (handler != null)
                    {
                        String message = "Unable to connect to specified port";
                        CustomEventArgs newEventArgs = new CustomEventArgs((int)ShimmerIdentifier.MSG_IDENTIFIER_NOTIFICATION_MESSAGE, (object)message);
                        handler(this, newEventArgs);
                    }
                }
            }
            else
            {
                if (TimerConnect != null)
                {
                    TimerConnect.Stop(); // Enable it
                }
                StopReading = true;
                ReadThread = null;
                SetState(SHIMMER_STATE_NONE);
                EventHandler handler = UICallback;
                if (handler != null)
                {
                    String message = "Unable to connect to specified port";
                    CustomEventArgs newEventArgs = new CustomEventArgs((int)ShimmerIdentifier.MSG_IDENTIFIER_NOTIFICATION_MESSAGE, (object)message);
                    handler(this, newEventArgs);
                }
            }
        }
        protected virtual void InitializeShimmer3SDBT()
        {
            InitializeShimmer3();
        }

        public virtual void SetConfigTime(long value)
        {

        }

        public virtual void WriteConfigTime() { }

        public virtual String SystemTime2Config()
        {
            return "";
        }

        public virtual void SetDrivePath(String path)
        {

        }
        /// <summary>
        /// Compatible with LogandStream only
        /// </summary>
        public virtual void StartStreamingandLog() { }
        /// <summary>
        /// Compatible with LogandStream only
        /// </summary>
        public virtual bool GetSync() { return false; }
        /// <summary>
        /// Compatible with LogandStream only
        /// </summary>
        public virtual bool GetUserButton() { return false; }
        /// <summary>
        /// Compatible with LogandStream only
        /// </summary>
        public virtual bool GetIAmMaster() { return false; }
        /// <summary>
        /// Compatible with LogandStream only
        /// </summary>
        public virtual bool GetSingleTouch() { return false; }
        /// <summary>
        /// Compatible with LogandStream only
        /// </summary>
        public virtual bool GetTcxo() { return false; }
        /// <summary>
        /// Compatible with LogandStream only
        /// </summary>
        public virtual bool GetExpPower() { return false; }

        /// <summary>
        /// Compatible with LogandStream only
        /// </summary>
        public virtual void SetSync(bool val) { }
        /// <summary>
        /// Compatible with LogandStream only
        /// </summary>
        public virtual void SetUserButton(bool val) { }
        /// <summary>
        /// Compatible with LogandStream only
        /// </summary>
        public virtual void SetIAmMaster(bool val) { }
        /// <summary>
        /// Compatible with LogandStream only
        /// </summary>
        public virtual void SetSingleTouch(bool val) { }
        /// <summary>
        /// Compatible with LogandStream only
        /// </summary>
        public virtual void SetTcxo(bool val) { }
        /// <summary>
        /// Compatible with LogandStream only
        /// </summary>
        public virtual void SetExpPower(bool val) { }
        /// <summary>
        /// Compatible with LogandStream only
        /// </summary>
        public virtual void SetMonitor(bool val) { }

        public virtual void SetDataReceived(bool val) { }
        /// <summary>
        /// Compatible with LogandStream only
        /// </summary>
        public virtual bool GetDataReceived() { return false; }
        /// <summary>
        /// Compatible with LogandStream only
        /// </summary>
        public virtual int GetClockTCXO() { return -1; }
        /// <summary>
        /// Compatible with LogandStream only
        /// </summary>


        public virtual void SetClockTCXO(int tcxo) { }

        public virtual int GetInterval() { return -1; }
        public virtual void SetCenter(string val) { }
        public virtual string GetCenter() { return ""; }
        public virtual void SetMyID(int val) { }
        public virtual int GetMyID() { return -1; }
        public virtual void SetNshimmer(int val) { }
        public virtual int GetNshimmer() { return -1; }
        public virtual void SetShimmerName(string val) { }
        public virtual string GetShimmerName() { return ""; }
        public virtual void SetSdDir(string val) { }
        public virtual string GetSdDir() { return ""; }
        public virtual void SetExperimentID(string val) { }
        public virtual string GetExperimentID() { return ""; }
        public virtual long GetConfigTime() { return -1; }
        public virtual void WriteCenter() { }
        public virtual void WriteShimmerName() { }
        public virtual void WriteTrial() { }
        public virtual void WriteExpID()
        {
        }

        public virtual void SetInterval(int val) { }

        public virtual string ConfigTimeToShowString(long cfgtime_in) { return ""; }

        public virtual void WriteNshimmer()
        {
        }

        public virtual void WriteMyID()
        {
        }


        void ConnectTimerElapsed(object sender, ElapsedEventArgs e)
        {
            if (GetState() == SHIMMER_STATE_CONNECTING)
            {
                //Means something has gone wrong during the connecting state
                if (IsConnectionOpen())
                {
                    CloseConnection();
                }
                SetState(SHIMMER_STATE_NONE);
            }
            TimerConnect.Stop();
        }

        public void ReadData()
        {
            List<byte> buffer = new List<byte>();
            int i;
            byte[] bufferbyte;
            ObjectCluster objectCluster;
            FlushInputConnection();
            KeepObjectCluster = null;
            UnalignedBytesReceived = new List<Byte>();

            while (!StopReading)
            {
                try
                {
                    byte b;
                    if (UnalignedBytesReceived.Count() > 0)
                    {
                        //Debug.WriteLine("Reuse Unaligned Bytes");
                        b = UnalignedBytesReceived[0];
                        UnalignedBytesReceived.RemoveAt(0);
                    }
                    else
                    {
                        b = (byte)ReadByte();
                    }
                    StreamTimeOutCount = 0;
                    if (ShimmerState == SHIMMER_STATE_STREAMING)
                    {
                        switch (b)
                        {
                            case (byte)PacketTypeShimmer2.DATA_PACKET: //Shimmer3 has the same value
                                if (IsFilled)
                                {
                                    List<byte> dataByte;
                                    dataByte = new List<byte>();
                                    int packetSize = PacketSize;
                                    packetSize = packetSize + (int)BluetoothCRCMode;
                                    if (UnalignedBytesReceived.Count > 0)
                                    {
                                        dataByte = UnalignedBytesReceived.ToArray().ToList<byte>();
                                        UnalignedBytesReceived.Clear();
                                    }
                                    int count = dataByte.Count();
                                    for (i = 0; i < (packetSize - count); i++)
                                    {
                                        dataByte.Add((byte)ReadByte());
                                    }

                                    bool check = true;
                                    if (BluetoothCRCMode != BTCRCMode.OFF)
                                    {
                                        byte[] dataArray = dataByte.ToArray();
                                        byte[] fullPacket = new byte[dataArray.Length + 1];
                                        System.Array.Copy(dataArray, 0, fullPacket, 1, dataArray.Length);
                                        fullPacket[0] = (byte)PacketTypeShimmer2.DATA_PACKET;
                                        check = CheckCrc(fullPacket, PacketSize + 1);
                                    }
                                    if (check)
                                    {
                                        //Debug.WriteLine("CRC Pass");
                                        objectCluster = BuildMsg(dataByte);

                                    }
                                    else
                                    {
                                        Debug.WriteLine("CRC Failed");
                                        //dataByte.RemoveAt(0);
                                        UnalignedBytesReceived = new List<Byte>();
                                        UnalignedBytesReceived = dataByte.ToArray().ToList();
                                        objectCluster = null;
                                    }




                                    if (KeepObjectCluster != null)
                                    {
                                        //ObjectClusterBuffer.Add(KeepObjectCluster); // dont need this if not storing packets in buffer
                                    }

                                    // check if there was a previously received packet, if there is send that, as it is a packet without error (zero-zero test), each packet starts with zero
                                    if (KeepObjectCluster != null)
                                    {
                                        //Check the time stamp
                                        if (mEnableTimeStampAlignmentCheck)
                                        {
                                            if (packetTimeStampChecker(objectCluster.RawTimeStamp, KeepObjectCluster.RawTimeStamp))
                                            {
                                                CustomEventArgs newEventArgs = new CustomEventArgs((int)ShimmerIdentifier.MSG_IDENTIFIER_DATA_PACKET, (object)KeepObjectCluster);
                                                OnNewEvent(newEventArgs);
                                            }
                                            else
                                            {
                                                System.Console.WriteLine("Throwing Packet");
                                                objectCluster = null;

                                                ReadByte();
                                            }

                                        }
                                        else
                                        {
                                            CustomEventArgs newEventArgs = new CustomEventArgs((int)ShimmerIdentifier.MSG_IDENTIFIER_DATA_PACKET, (object)KeepObjectCluster);
                                            OnNewEvent(newEventArgs);
                                        }
                                        //
                                        //packetTimeStampChecker(objectCluster.RawTimeStamp, KeepObjectCluster.RawTimeStamp);
                                        //CustomEventArgs newEventArgs = new CustomEventArgs((int)ShimmerIdentifier.MSG_IDENTIFIER_DATA_PACKET, (object)KeepObjectCluster);
                                        //OnNewEvent(newEventArgs);

                                    }
                                    KeepObjectCluster = objectCluster;


                                    buffer.Clear();
                                }
                                break;
                            case (byte)PacketTypeShimmer2.ACK_COMMAND:
                                //Since the ack always proceeds the instreamcmd
                                if (StreamingACKReceived)
                                {

                                    if (GetFirmwareIdentifier() == FW_IDENTIFIER_LOGANDSTREAM)
                                    {
                                        byte c = (byte)ReadByte(); // get the next byte and pass it the ShimmerSDBT, this should be instreamcmd
                                        if (c != (byte)ShimmerBluetooth.PacketTypeShimmer3SDBT.INSTREAM_CMD_RESPONSE)
                                        {
                                            KeepObjectCluster = null;
                                        }
                                        else
                                        {
                                            SDBT_switch(c);
                                        }
                                    }
                                    else
                                    {
                                        System.Console.WriteLine("ACK for Command while Streaming Received");
                                    }
                                }
                                else
                                {
                                    System.Console.WriteLine("ACK for Streaming Command Received");
                                    if (BluetoothCRCMode != BTCRCMode.OFF)
                                    {
                                        if (b != 0xff)
                                        {
                                            //Currently on CRC for sensor data packets are handled
                                            for (int k = 0; k < (int)BluetoothCRCMode; k++)
                                            {
                                                //Debug.WriteLine("State Connected: Throw CRC Byte");
                                                ReadByte();
                                            }
                                        }
                                    }
                                    StreamingACKReceived = true;
                                }
                                break;
                            default:
                                System.Console.WriteLine("Misaligned ByteStream Detected");
                                // If it gets here means the previous packet is invalid so make it null so it wont be added to the buffer
                                KeepObjectCluster = null;
                                break;
                        }
                    }
                    else //connected but not streaming
                    {
                        switch (b)
                        {
                            case (byte)PacketTypeShimmer2.DATA_PACKET:  //Read bytes but do nothing with them
                                if (IsFilled)
                                {
                                    List<byte> dataByte;
                                    dataByte = new List<byte>();
                                    for (i = 0; i < PacketSize; i++)
                                    {
                                        dataByte.Add((byte)ReadByte());
                                    }
                                    buffer.Clear();
                                }
                                break;
                            case (byte)PacketTypeShimmer2.INQUIRY_RESPONSE:

                                if (HardwareVersion == (int)ShimmerBluetooth.ShimmerVersion.SHIMMER2 || HardwareVersion == (int)ShimmerBluetooth.ShimmerVersion.SHIMMER2R)
                                {
                                    for (i = 0; i < 5; i++)
                                    {
                                        // get Sampling rate, accel range, config setup byte0, num chans and buffer size
                                        buffer.Add((byte)ReadByte());
                                    }
                                    for (i = 0; i < (int)buffer[3]; i++)
                                    {
                                        // read each channel type for the num channels
                                        buffer.Add((byte)ReadByte());
                                    }
                                    InterpretInquiryResponseShimmer2(buffer);
                                }
                                else if (HardwareVersion == (int)ShimmerBluetooth.ShimmerVersion.SHIMMER3)
                                {
                                    for (i = 0; i < 8; i++)
                                    {
                                        // get Sampling rate, accel range, config setup byte0, num chans and buffer size
                                        buffer.Add((byte)ReadByte());
                                    }
                                    for (i = 0; i < (int)buffer[6]; i++)
                                    {
                                        // read each channel type for the num channels
                                        buffer.Add((byte)ReadByte());
                                    }
                                    InterpretInquiryResponseShimmer3(buffer);
                                }
                                else if (HardwareVersion == (int)ShimmerBluetooth.ShimmerVersion.SHIMMER3R)
                                {
                                    for (i = 0; i < 8 + 3; i++)
                                    {
                                        // get Sampling rate, accel range, config setup byte0, num chans and buffer size
                                        buffer.Add((byte)ReadByte());
                                    }
                                    for (i = 0; i < (int)buffer[6 + 3]; i++)
                                    {
                                        // read each channel type for the num channels
                                        buffer.Add((byte)ReadByte());
                                    }
                                    InterpretInquiryResponseShimmer3R(buffer);
                                }
                                if (ShimmerState != SHIMMER_STATE_CONNECTED)
                                {
                                    SetupDevice = false; //device has been setup
                                    SetState(SHIMMER_STATE_CONNECTED);
                                }
                                buffer.Clear();
                                break;
                            case (byte)PacketTypeShimmer2.SAMPLING_RATE_RESPONSE:
                                if (HardwareVersion == (int)ShimmerBluetooth.ShimmerVersion.SHIMMER3)
                                {
                                    int value = 0;
                                    value = (int)ReadByte();
                                    value += (((int)ReadByte() << 8) & 0xFF00);
                                    ADCRawSamplingRateValue = value;
                                    SamplingRate = (double)32768 / ADCRawSamplingRateValue;
                                }
                                else if (HardwareVersion == (int)ShimmerBluetooth.ShimmerVersion.SHIMMER3R)
                                {
                                    int value = 0;
                                    value = (int)ReadByte();
                                    value += (((int)ReadByte() << 8) & 0xFF00);
                                    ADCRawSamplingRateValue = value;
                                    SamplingRate = (double)32768 / ADCRawSamplingRateValue;
                                }
                                else
                                {
                                    ADCRawSamplingRateValue = ReadByte();
                                    SamplingRate = (double)1024 / ADCRawSamplingRateValue;
                                }

                                break;
                            case (byte)PacketTypeShimmer2.ACCEL_RANGE_RESPONSE:
                                SetAccelRange(ReadByte());
                                break;
                            case (byte)PacketTypeShimmer3.ACCEL_SAMPLING_RATE_RESPONSE:
                                SetAccelSamplingRate(ReadByte());
                                break;
                            case (byte)PacketTypeShimmer3.MPU9150_GYRO_RANGE_RESPONSE:
                                SetGyroRange(ReadByte());
                                break;
                            case (byte)PacketTypeShimmer3.BAUD_RATE_COMMAND_RESPONSE:
                                SetBaudRate(ReadByte());
                                break;
                            case (byte)PacketTypeShimmer3.DETECT_EXPANSION_BOARD_RESPONSE:
                                // 2 byte response, only need byte 2nd byte
                                byte NumBytes;
                                NumBytes = (byte)ReadByte();
                                // size of byte array to read is NumBytes bytes
                                ExpansionDetectArray = new byte[NumBytes];
                                for (int p = 0; p < NumBytes; p++)
                                {
                                    ExpansionDetectArray[p] = (byte)ReadByte();
                                }
                                if (ExpansionDetectArray[0] == (int)ExpansionBoardDetectShimmer3.EXPANSION_BRIDGE_AMPLIFIER_PLUS) ExpansionBoard = "Bridge Amplifier+";
                                else if (ExpansionDetectArray[0] == (int)ExpansionBoardDetectShimmer3.EXPANSION_GSR_PLUS) ExpansionBoard = "GSR+";
                                else if (ExpansionDetectArray[0] == (int)ExpansionBoardDetectShimmer3.EXPANSION_PROTO3_MINI) ExpansionBoard = "PROTO3 Mini";
                                else if (ExpansionDetectArray[0] == (int)ExpansionBoardDetectShimmer3.EXPANSION_EXG) ExpansionBoard = "ExG";
                                else if (ExpansionDetectArray[0] == (int)ExpansionBoardDetectShimmer3.EXPANSION_PROTO3_DELUXE) ExpansionBoard = "PROTO3 Deluxe";
                                else if (ExpansionDetectArray[0] == (int)ExpansionBoardDetectShimmer3.SHIMMER_3_EXG_EXTENDED) ExpansionBoard = "EXG";
                                else if (ExpansionDetectArray[0] == (int)ExpansionBoardDetectShimmer3.SHIMMER3) ExpansionBoard = "None";
                                else if (ExpansionDetectArray[0] == (int)ExpansionBoardDetectShimmer3.EXP_BRD_HIGH_G_ACCEL) ExpansionBoard = "High G Accel";
                                else if (ExpansionDetectArray[0] == (int)ExpansionBoardDetectShimmer3.EXP_BRD_GPS) ExpansionBoard = "GPS";
                                else if (ExpansionDetectArray[0] == (int)ExpansionBoardDetectShimmer3.EXP_BRD_EXG_UNIFIED) ExpansionBoard = "EXG";
                                else if (ExpansionDetectArray[0] == (int)ExpansionBoardDetectShimmer3.EXP_BRD_GSR_UNIFIED) ExpansionBoard = "GSR+";
                                else if (ExpansionDetectArray[0] == (int)ExpansionBoardDetectShimmer3.EXP_BRD_BR_AMP_UNIFIED) ExpansionBoard = "Bridge Amplifier+";
                                else ExpansionBoard = "Unknown";
                                if (NumBytes >= 3)
                                {
                                    ExpansionBoardId = ExpansionDetectArray[0];
                                    ExpansionBoardRev = ExpansionDetectArray[1];
                                    ExpansionBoardRevSpecial = ExpansionDetectArray[2];
                                }
                                if (!ExpansionBoard.Equals("Unknown"))
                                {
                                    ExpansionBoard = string.Format("{0} (SR{1}-{2}.{3})", ExpansionBoard, ExpansionDetectArray[0], ExpansionDetectArray[1], ExpansionDetectArray[2]);
                                }
                                break;
                            case (byte)PacketTypeShimmer2.MAG_GAIN_RESPONSE:
                                SetMagRange(ReadByte());
                                break;
                            case (byte)PacketTypeShimmer2.MAG_SAMPLING_RATE_RESPONSE:
                                SetMagSamplingRate(ReadByte());
                                break;
                            case (byte)PacketTypeShimmer2.CONFIG_SETUP_BYTE0_RESPONSE:
                                SetConfigSetupByte0(ReadByte());
                                break;
                            case (byte)PacketTypeShimmer2.GSR_RANGE_RESPONSE:
                                SetGSRRange(ReadByte());
                                break;
                            case (byte)PacketTypeShimmer3.INTERNAL_EXP_POWER_ENABLE_RESPONSE:
                                SetInternalExpPower(ReadByte());
                                break;
                            case (byte)PacketTypeShimmer2.ACK_COMMAND:
                                System.Console.WriteLine("ACK Received");
                                if (mWaitingForStartStreamingACK)
                                {
                                    SetState(SHIMMER_STATE_STREAMING);
                                    mWaitingForStartStreamingACK = false;
                                    System.Console.WriteLine("ACK for Streaming Command Received");
                                    if (BluetoothCRCMode != BTCRCMode.OFF)
                                    {
                                        for (int k = 0; k < (int)BluetoothCRCMode; k++)
                                        {
                                            //Debug.WriteLine("State Connected: Throw CRC Byte");
                                            ReadByte();
                                        }
                                    }
                                    StreamingACKReceived = true;
                                }
                                break;
                            case (byte)PacketTypeShimmer2.ACCEL_CALIBRATION_RESPONSE:
                                // size is 21 bytes
                                bufferbyte = new byte[21];
                                for (int p = 0; p < 21; p++)
                                {
                                    bufferbyte[p] = (byte)ReadByte();

                                }
                                RetrieveKinematicCalibrationParametersFromPacket(bufferbyte, (byte)PacketTypeShimmer2.ACCEL_CALIBRATION_RESPONSE);
                                break;
                            case (byte)PacketTypeShimmer2.GYRO_CALIBRATION_RESPONSE:
                                // size is 21 bytes
                                bufferbyte = new byte[21];
                                for (int p = 0; p < 21; p++)
                                {
                                    bufferbyte[p] = (byte)ReadByte();

                                }
                                RetrieveKinematicCalibrationParametersFromPacket(bufferbyte, (byte)PacketTypeShimmer2.GYRO_CALIBRATION_RESPONSE);
                                break;
                            case (byte)PacketTypeShimmer2.MAG_CALIBRATION_RESPONSE:
                                // size is 21 bytes
                                bufferbyte = new byte[21];
                                for (int p = 0; p < 21; p++)
                                {
                                    bufferbyte[p] = (byte)ReadByte();

                                }
                                RetrieveKinematicCalibrationParametersFromPacket(bufferbyte, (byte)PacketTypeShimmer2.MAG_CALIBRATION_RESPONSE);
                                break;
                            case (byte)PacketTypeShimmer2.ALL_CALIBRATION_RESPONSE:
                                //Retrieve Accel
                                bufferbyte = new byte[21];
                                for (int p = 0; p < 21; p++)
                                {
                                    bufferbyte[p] = (byte)ReadByte();
                                }
                                RetrieveKinematicCalibrationParametersFromPacket(bufferbyte, (byte)PacketTypeShimmer2.ACCEL_CALIBRATION_RESPONSE);

                                //Retrieve Gyro
                                bufferbyte = new byte[21];
                                for (int p = 0; p < 21; p++)
                                {
                                    bufferbyte[p] = (byte)ReadByte();

                                }
                                RetrieveKinematicCalibrationParametersFromPacket(bufferbyte, (byte)PacketTypeShimmer2.GYRO_CALIBRATION_RESPONSE);

                                //Retrieve Mag
                                bufferbyte = new byte[21];
                                for (int p = 0; p < 21; p++)
                                {
                                    bufferbyte[p] = (byte)ReadByte();

                                }
                                RetrieveKinematicCalibrationParametersFromPacket(bufferbyte, (byte)PacketTypeShimmer2.MAG_CALIBRATION_RESPONSE);
                                if (HardwareVersion == (int)ShimmerBluetooth.ShimmerVersion.SHIMMER2 || HardwareVersion == (int)ShimmerBluetooth.ShimmerVersion.SHIMMER2R)
                                {
                                    //Retrieve EMG n ECG
                                    bufferbyte = new byte[12];
                                    for (int p = 0; p < 12; p++)
                                    {
                                        bufferbyte[p] = (byte)ReadByte();

                                    }
                                    if (bufferbyte[0] == 255 && bufferbyte[1] == 255 && bufferbyte[2] == 255 && bufferbyte[3] == 255)
                                    {
                                        DefaultEMGParams = true;
                                    }
                                    else
                                    {
                                        OffsetEMG = (double)((bufferbyte[0] & 0xFF) << 8) + (bufferbyte[1] & 0xFF);
                                        GainEMG = (double)((bufferbyte[2] & 0xFF) << 8) + (bufferbyte[3] & 0xFF);
                                        DefaultEMGParams = false;
                                    }
                                    if (bufferbyte[4] == 255 && bufferbyte[5] == 255 && bufferbyte[6] == 255 && bufferbyte[7] == 255)
                                    {
                                        DefaultECGParams = true;
                                    }
                                    else
                                    {
                                        OffsetECGLALL = (double)((bufferbyte[4] & 0xFF) << 8) + (bufferbyte[5] & 0xFF);
                                        GainECGLALL = (double)((bufferbyte[6] & 0xFF) << 8) + (bufferbyte[7] & 0xFF);
                                        OffsetECGRALL = (double)((bufferbyte[8] & 0xFF) << 8) + (bufferbyte[9] & 0xFF);
                                        GainECGRALL = (double)((bufferbyte[10] & 0xFF) << 8) + (bufferbyte[11] & 0xFF);
                                        DefaultECGParams = false;
                                    }
                                }
                                else
                                {
                                    //Retrieve Digital Accel Cal Paramters if Shimmer 3
                                    bufferbyte = new byte[21];
                                    for (int p = 0; p < 21; p++)
                                    {
                                        bufferbyte[p] = (byte)ReadByte();

                                    }
                                    RetrieveKinematicCalibrationParametersFromPacket(bufferbyte, (byte)PacketTypeShimmer3.WR_ACCEL_CALIBRATION_RESPONSE);

                                    //Retrieve Alt Mag Cal Paramters if Shimmer 3
                                    bufferbyte = new byte[21];
                                    for (int p = 0; p < 21; p++)
                                    {
                                        bufferbyte[p] = (byte)ReadByte();

                                    }
                                    RetrieveKinematicCalibrationParametersFromPacket(bufferbyte, (byte)PacketTypeShimmer3.ALT_ACCEL_CALIBRATION_RESPONSE);

                                    //Retrieve High G Accel Cal Paramters if Shimmer 3
                                    bufferbyte = new byte[21];
                                    for (int p = 0; p < 21; p++)
                                    {
                                        bufferbyte[p] = (byte)ReadByte();

                                    }
                                    RetrieveKinematicCalibrationParametersFromPacket(bufferbyte, (byte)PacketTypeShimmer3.ALT_MAG_CALIBRATION_RESPONSE);

                                }

                                break;
                            case (byte)PacketTypeShimmer3.BMP180_CALIBRATION_COEFFICIENTS_RESPONSE:
                                bufferbyte = new byte[22];
                                for (int p = 0; p < 22; p++)
                                {
                                    bufferbyte[p] = (byte)ReadByte();
                                }
                                CalculateBMP180PressureCalibrationCoefficientsResponse(bufferbyte);
                                break;
                            case (byte)PacketTypeShimmer3.BMP280_CALIBRATION_COEFFICIENTS_RESPONSE:
                                bufferbyte = new byte[24];
                                for (int p = 0; p < 24; p++)
                                {
                                    bufferbyte[p] = (byte)ReadByte();
                                }

                                CalculateBMP280PressureCalibrationCoefficientsResponse(bufferbyte);

                                break;
                            case (byte)PacketTypeShimmer3RSDBT.PRESSURE_CALIBRATION_COEFFICIENTS_RESPONSE:

                                byte[] length_chipidbytes = new byte[2];

                                for (int m = 0; m < 2; m++)
                                {
                                    length_chipidbytes[m] = (byte)ReadByte();
                                }

                                if (length_chipidbytes[1] == 0) //bmp180
                                {
                                    bufferbyte = new byte[22];
                                    for (int p = 0; p < 22; p++)
                                    {
                                        bufferbyte[p] = (byte)ReadByte();
                                    }
                                    CalculateBMP180PressureCalibrationCoefficientsResponse(bufferbyte);

                                }
                                else if (length_chipidbytes[1] == 1)    //bmp280
                                {
                                    bufferbyte = new byte[24];
                                    for (int p = 0; p < 24; p++)
                                    {
                                        bufferbyte[p] = (byte)ReadByte();
                                    }
                                    CalculateBMP280PressureCalibrationCoefficientsResponse(bufferbyte);

                                }
                                else if (length_chipidbytes[1] == 2)    //bmp390
                                {
                                    bufferbyte = new byte[21];
                                    for (int p = 0; p < 21; p++)
                                    {
                                        bufferbyte[p] = (byte)ReadByte();
                                    }
                                    CalculateBMP390PressureCalibrationCoefficientsResponse(bufferbyte);
                                }

                                break;
                            case (byte)PacketTypeShimmer2.BLINK_LED_RESPONSE:
                                bufferbyte = new byte[1];
                                bufferbyte[0] = (byte)ReadByte();
                                CurrentLEDStatus = bufferbyte[0];
                                break;
                            case (byte)PacketTypeShimmer3.VBATT_RESPONSE:
                                bufferbyte = new byte[3];
                                for (int p = 0; p < 3; p++)
                                {
                                    bufferbyte[p] = (byte)ReadByte();
                                }
                                int batteryadcvalue = (int)((bufferbyte[1] & 0xFF) << 8) + (int)(bufferbyte[0] & 0xFF);
                                ChargingStatus = bufferbyte[2];
                                BatteryVoltage = adcValToBattVoltage(batteryadcvalue);
                                break;
                            case (byte)PacketTypeShimmer2.FW_VERSION_RESPONSE:
                                // size is 21 bytes
                                bufferbyte = new byte[6];
                                for (int p = 0; p < 6; p++)
                                {
                                    bufferbyte[p] = (byte)ReadByte();

                                }
                                FirmwareIdentifier = ((double)((bufferbyte[1] & 0xFF) << 8) + (double)(bufferbyte[0] & 0xFF));

                                FirmwareMajor = (int)((bufferbyte[3] & 0xFF) << 8) + (int)(bufferbyte[2] & 0xFF);
                                FirmwareMinor = (int)(bufferbyte[4] & 0xFF);
                                FirmwareInternal = ((int)(bufferbyte[5] & 0xFF));



                                string fw_id = "";
                                if (FirmwareIdentifier == FW_IDENTIFIER_BTSTREAM)
                                    fw_id = "BtStream ";
                                else if (FirmwareIdentifier == FW_IDENTIFIER_LOGANDSTREAM)
                                    fw_id = "LogAndStream ";
                                else if (FirmwareIdentifier == FW_IDENTIFIER_SHIMMERECGMD)
                                    fw_id = "ECGmd ";
                                else
                                    fw_id = "Unknown ";
                                string temp = fw_id + FirmwareMajor.ToString() + "." + FirmwareMinor.ToString() + "." + FirmwareInternal.ToString();
                                FirmwareVersionFullName = temp;
                                SetCompatibilityCode();
                                UpdateBasedOnCompatibilityCode();
                                break;
                            case (byte)PacketTypeShimmer2.GET_SHIMMER_VERSION_RESPONSE:
                                bufferbyte = new byte[1];
                                bufferbyte[0] = (byte)ReadByte();
                                HardwareVersion = bufferbyte[0];
                                // set default calibration parameters
                                SetCompatibilityCode();
                                UpdateBasedOnCompatibilityCode();
                                break;
                            case (byte)PacketTypeShimmer3.EXG_REGS_RESPONSE:
                                //System.Console.WriteLine("EXG r r" + ChipID);
                                if (ChipID == 1)
                                {
                                    ReadByte();
                                    Exg1RegArray[0] = (byte)ReadByte();
                                    Exg1RegArray[1] = (byte)ReadByte();
                                    Exg1RegArray[2] = (byte)ReadByte();
                                    Exg1RegArray[3] = (byte)ReadByte();
                                    Exg1RegArray[4] = (byte)ReadByte();
                                    Exg1RegArray[5] = (byte)ReadByte();
                                    Exg1RegArray[6] = (byte)ReadByte();
                                    Exg1RegArray[7] = (byte)ReadByte();
                                    Exg1RegArray[8] = (byte)ReadByte();
                                    Exg1RegArray[9] = (byte)ReadByte();
                                }
                                else
                                {
                                    ReadByte();
                                    Exg2RegArray[0] = (byte)ReadByte();
                                    Exg2RegArray[1] = (byte)ReadByte();
                                    Exg2RegArray[2] = (byte)ReadByte();
                                    Exg2RegArray[3] = (byte)ReadByte();
                                    Exg2RegArray[4] = (byte)ReadByte();
                                    Exg2RegArray[5] = (byte)ReadByte();
                                    Exg2RegArray[6] = (byte)ReadByte();
                                    Exg2RegArray[7] = (byte)ReadByte();
                                    Exg2RegArray[8] = (byte)ReadByte();
                                    Exg2RegArray[9] = (byte)ReadByte();
                                }

                                break;

                            default:
                                // This is to extend log and stream functionality
                                if (GetFirmwareIdentifier() == FW_IDENTIFIER_LOGANDSTREAM)
                                {
                                    SDBT_switch(b);
                                }
                                else
                                {

                                }
                                break;
                        }
                        if (BluetoothCRCMode != BTCRCMode.OFF)
                        {
                            if (b != 0xff)
                            {
                                //Currently on CRC for sensor data packets are handled
                                for (int k = 0; k < (int)BluetoothCRCMode; k++)
                                {
                                    //Debug.WriteLine("State Connected: Throw CRC Byte");
                                    ReadByte();
                                }
                            }
                        }

                    }


                }

                catch (System.TimeoutException)
                {
                    //
                    if (ShimmerState == SHIMMER_STATE_STREAMING || ShimmerState == SHIMMER_STATE_CONNECTED || ShimmerState == SHIMMER_STATE_CONNECTING)
                    {
                        //System.Console.WriteLine("Timeout Streaming");
                        StreamTimeOutCount++;
                        if (StreamTimeOutCount % 5 == 0 && ShimmerState == SHIMMER_STATE_CONNECTED && GetFirmwareIdentifier() == FW_IDENTIFIER_LOGANDSTREAM)
                        {
                            System.Console.WriteLine("Sending Get Status Command");
                            try
                            {
                                WriteBytes(new byte[1] { (byte)ShimmerBluetooth.PacketTypeShimmer3SDBT.GET_STATUS_COMMAND }, 0, 1);
                            }
                            catch (System.TimeoutException)
                            {
                                Disconnect();
                            }
                            System.Threading.Thread.Sleep(500);
                        }
                        if (StreamTimeOutCount > 10)
                        {
                            CustomEventArgs newEventArgs = new CustomEventArgs((int)ShimmerIdentifier.MSG_IDENTIFIER_NOTIFICATION_MESSAGE, "Connection lost");
                            OnNewEvent(newEventArgs);
                            Disconnect();
                        }
                        if (GetFirmwareIdentifier() == FW_IDENTIFIER_BTSTREAM && ShimmerState == SHIMMER_STATE_CONNECTED)
                        {
                            StreamTimeOutCount = 0;
                        }
                        if (GetFirmwareIdentifier() == FW_IDENTIFIER_SHIMMERECGMD && ShimmerState == SHIMMER_STATE_CONNECTED)
                        {
                            try
                            {
                                ReadBattery();
                            }
                            catch (System.TimeoutException)
                            {
                                Disconnect();
                            }
                        }
                    }

                }
                catch (System.InvalidOperationException)
                {
                    CustomEventArgs newEventArgs = new CustomEventArgs((int)ShimmerIdentifier.MSG_IDENTIFIER_NOTIFICATION_MESSAGE, "Connection lost");
                    OnNewEvent(newEventArgs);
                    Disconnect();
                }
                catch (System.IO.IOException)
                {

                }

            }

            // only stop reading when disconnecting, so disconnect serial port here too
            CloseConnection();

        }
        public void CalculateBMP180PressureCalibrationCoefficientsResponse(byte[] bufferbyte)
        {
            AC1 = Calculatetwoscomplement((int)((int)(bufferbyte[1] & 0xFF) + ((int)(bufferbyte[0] & 0xFF) << 8)), 16);
            AC2 = Calculatetwoscomplement((int)((int)(bufferbyte[3] & 0xFF) + ((int)(bufferbyte[2] & 0xFF) << 8)), 16);
            AC3 = Calculatetwoscomplement((int)((int)(bufferbyte[5] & 0xFF) + ((int)(bufferbyte[4] & 0xFF) << 8)), 16);
            AC4 = (int)((int)(bufferbyte[7] & 0xFF) + ((int)(bufferbyte[6] & 0xFF) << 8));
            AC5 = (int)((int)(bufferbyte[9] & 0xFF) + ((int)(bufferbyte[8] & 0xFF) << 8));
            AC6 = (int)((int)(bufferbyte[11] & 0xFF) + ((int)(bufferbyte[10] & 0xFF) << 8));
            B1 = Calculatetwoscomplement((int)((int)(bufferbyte[13] & 0xFF) + ((int)(bufferbyte[12] & 0xFF) << 8)), 16);
            B2 = Calculatetwoscomplement((int)((int)(bufferbyte[15] & 0xFF) + ((int)(bufferbyte[14] & 0xFF) << 8)), 16);
            MB = Calculatetwoscomplement((int)((int)(bufferbyte[17] & 0xFF) + ((int)(bufferbyte[16] & 0xFF) << 8)), 16);
            MC = Calculatetwoscomplement((int)((int)(bufferbyte[19] & 0xFF) + ((int)(bufferbyte[18] & 0xFF) << 8)), 16);
            MD = Calculatetwoscomplement((int)((int)(bufferbyte[21] & 0xFF) + ((int)(bufferbyte[20] & 0xFF) << 8)), 16);
        }
        public void CalculateBMP280PressureCalibrationCoefficientsResponse(byte[] bufferbyte)
        {
            byte[] pressureResoRes = bufferbyte;
            dig_T1 = (int)((int)(pressureResoRes[0] & 0xFF) + ((int)(pressureResoRes[1] & 0xFF) << 8));
            dig_T2 = Calculatetwoscomplement((int)((int)(pressureResoRes[2] & 0xFF) + ((int)(pressureResoRes[3] & 0xFF) << 8)), 16);
            dig_T3 = Calculatetwoscomplement((int)((int)(pressureResoRes[4] & 0xFF) + ((int)(pressureResoRes[5] & 0xFF) << 8)), 16);

            dig_P1 = (int)((int)(pressureResoRes[6] & 0xFF) + ((int)(pressureResoRes[7] & 0xFF) << 8));
            dig_P2 = Calculatetwoscomplement((int)((int)(pressureResoRes[8] & 0xFF) + ((int)(pressureResoRes[9] & 0xFF) << 8)), 16);
            dig_P3 = Calculatetwoscomplement((int)((int)(pressureResoRes[10] & 0xFF) + ((int)(pressureResoRes[11] & 0xFF) << 8)), 16);
            dig_P4 = Calculatetwoscomplement((int)((int)(pressureResoRes[12] & 0xFF) + ((int)(pressureResoRes[13] & 0xFF) << 8)), 16);
            dig_P5 = Calculatetwoscomplement((int)((int)(pressureResoRes[14] & 0xFF) + ((int)(pressureResoRes[15] & 0xFF) << 8)), 16);
            dig_P6 = Calculatetwoscomplement((int)((int)(pressureResoRes[16] & 0xFF) + ((int)(pressureResoRes[17] & 0xFF) << 8)), 16);
            dig_P7 = Calculatetwoscomplement((int)((int)(pressureResoRes[18] & 0xFF) + ((int)(pressureResoRes[19] & 0xFF) << 8)), 16);
            dig_P8 = Calculatetwoscomplement((int)((int)(pressureResoRes[20] & 0xFF) + ((int)(pressureResoRes[21] & 0xFF) << 8)), 16);
            dig_P9 = Calculatetwoscomplement((int)((int)(pressureResoRes[22] & 0xFF) + ((int)(pressureResoRes[23] & 0xFF) << 8)), 16);

            Debug.WriteLine("dig_T1 = " + dig_T1);
            Debug.WriteLine("dig_T2 = " + dig_T2);
            Debug.WriteLine("dig_T3 = " + dig_T3);
            Debug.WriteLine("dig_P1 = " + dig_P1);
            Debug.WriteLine("dig_P2 = " + dig_P2);
            Debug.WriteLine("dig_P3 = " + dig_P3);
            Debug.WriteLine("dig_P4 = " + dig_P4);
            Debug.WriteLine("dig_P5 = " + dig_P5);
            Debug.WriteLine("dig_P6 = " + dig_P6);
            Debug.WriteLine("dig_P7 = " + dig_P7);
            Debug.WriteLine("dig_P8 = " + dig_P8);
        }
        public void CalculateBMP390PressureCalibrationCoefficientsResponse(byte[] bufferbyte)
        {
            byte[] pressureResoResTest = bufferbyte;
            double tempVar = 0;

            // 1 / 2^8
            tempVar = 0.00390625;
            Bmp3RegCalibData_ParT1 = ConcatenateBytes(pressureResoResTest[1], pressureResoResTest[0]);
            Bmp3QuantizedCalibData_ParT1 = ((double)Bmp3RegCalibData_ParT1 / tempVar);

            Bmp3RegCalibData_ParT2 = ConcatenateBytes(pressureResoResTest[3], pressureResoResTest[2]);
            tempVar = 1073741824.0;
            Bmp3QuantizedCalibData_ParT2 = ((double)Bmp3RegCalibData_ParT2 / tempVar);

            Bmp3RegCalibData_ParT3 = (sbyte)pressureResoResTest[4];
            tempVar = 281474976710656.0;
            Bmp3QuantizedCalibData_ParT3 = ((double)Bmp3RegCalibData_ParT3 / tempVar);

            Bmp3RegCalibData_ParP1 = (short)ConcatenateBytes(pressureResoResTest[6], pressureResoResTest[5]);
            tempVar = 1048576.0;
            Bmp3QuantizedCalibData_ParP1 = ((double)(Bmp3RegCalibData_ParP1 - 16384) / tempVar);

            Bmp3RegCalibData_ParP2 = (short)ConcatenateBytes(pressureResoResTest[8], pressureResoResTest[7]);
            tempVar = 536870912.0;
            Bmp3QuantizedCalibData_ParP2 = ((double)(Bmp3RegCalibData_ParP2 - 16384) / tempVar);

            Bmp3RegCalibData_ParP3 = (sbyte)pressureResoResTest[9];
            tempVar = 4294967296.0;
            Bmp3QuantizedCalibData_ParP3 = ((double)Bmp3RegCalibData_ParP3 / tempVar);

            Bmp3RegCalibData_ParP4 = (sbyte)pressureResoResTest[10];
            tempVar = 137438953472.0;
            Bmp3QuantizedCalibData_ParP4 = ((double)Bmp3RegCalibData_ParP4 / tempVar);

            Bmp3RegCalibData_ParP5 = ConcatenateBytes(pressureResoResTest[12], pressureResoResTest[11]);

            // 1 / 2^3
            tempVar = 0.125;
            Bmp3QuantizedCalibData_ParP5 = ((double)Bmp3RegCalibData_ParP5 / tempVar);

            Bmp3RegCalibData_ParP6 = ConcatenateBytes(pressureResoResTest[14], pressureResoResTest[13]);
            tempVar = 64.0;
            Bmp3QuantizedCalibData_ParP6 = ((double)Bmp3RegCalibData_ParP6 / tempVar);

            Bmp3RegCalibData_ParP7 = (sbyte)pressureResoResTest[15];
            tempVar = 256.0;
            Bmp3QuantizedCalibData_ParP7 = ((double)Bmp3RegCalibData_ParP7 / tempVar);

            Bmp3RegCalibData_ParP8 = (sbyte)pressureResoResTest[16];
            tempVar = 32768.0;
            Bmp3QuantizedCalibData_ParP8 = ((double)Bmp3RegCalibData_ParP8 / tempVar);

            Bmp3RegCalibData_ParP9 = (short)ConcatenateBytes(pressureResoResTest[18], pressureResoResTest[17]);
            tempVar = 281474976710656.0;
            Bmp3QuantizedCalibData_ParP9 = ((double)Bmp3RegCalibData_ParP9 / tempVar);

            Bmp3RegCalibData_ParP10 = (sbyte)pressureResoResTest[19];
            tempVar = 281474976710656.0;
            Bmp3QuantizedCalibData_ParP10 = ((double)Bmp3RegCalibData_ParP10 / tempVar);

            Bmp3RegCalibData_ParP11 = (sbyte)pressureResoResTest[20];
            tempVar = 36893488147419103232.0;
            Bmp3QuantizedCalibData_ParP11 = ((double)Bmp3RegCalibData_ParP11 / tempVar);

            Debug.WriteLine("par_T1 = " + Bmp3QuantizedCalibData_ParT1);
            Debug.WriteLine("par_T2 = " + Bmp3QuantizedCalibData_ParT2);
            Debug.WriteLine("par_T3 = " + Bmp3QuantizedCalibData_ParT3);
            Debug.WriteLine("par_P1 = " + Bmp3QuantizedCalibData_ParP1);
            Debug.WriteLine("par_P2 = " + Bmp3QuantizedCalibData_ParP2);
            Debug.WriteLine("par_P3 = " + Bmp3QuantizedCalibData_ParP3);
            Debug.WriteLine("par_P4 = " + Bmp3QuantizedCalibData_ParP4);
            Debug.WriteLine("par_P5 = " + Bmp3QuantizedCalibData_ParP5);
            Debug.WriteLine("par_P6 = " + Bmp3QuantizedCalibData_ParP6);
            Debug.WriteLine("par_P7 = " + Bmp3QuantizedCalibData_ParP7);
            Debug.WriteLine("par_P8 = " + Bmp3QuantizedCalibData_ParP8);
            Debug.WriteLine("par_P9 = " + Bmp3QuantizedCalibData_ParP9);
            Debug.WriteLine("par_P10 = " + Bmp3QuantizedCalibData_ParP10);
            Debug.WriteLine("par_P11 = " + Bmp3QuantizedCalibData_ParP11);
        }
        private static ushort ConcatenateBytes(byte highByte, byte lowByte)
        {
            return (ushort)((highByte << 8) | lowByte);
        }
        public virtual void SDBT_switch(byte b)
        {
        }
        public bool IsConnected()
        {
            if ((ShimmerState == SHIMMER_STATE_CONNECTED || ShimmerState == SHIMMER_STATE_STREAMING) && IsConnectionOpen())
            {
                return true;
            }
            else
            {
                return false;
            }
        }

        public void Disconnect()
        {
            try
            {
                if (ReadThread != null)
                {
                    //ReadThread.Abort();
                }
                if (ConnectThread != null)
                {
                    //ConnectThread.Abort();
                }
            }
            catch
            {
            }
            if (IsConnectionOpen() == true)
            {
                if (GetState() == ShimmerBluetooth.SHIMMER_STATE_STREAMING)
                {
                    if (GetFirmwareIdentifier() == FW_IDENTIFIER_LOGANDSTREAM)
                    {

                    }
                    else
                    {
                        //StopStreaming();
                    }

                }
                FlushConnection();

                try
                {
                    CloseConnection();
                }
                catch
                {
                }
            }
            else
            {

            }
            ObjectClusterBuffer.Clear();
            StopReading = true;
            SetState(SHIMMER_STATE_NONE);
        }

        protected void InitializeShimmer2()
        {
            if (SetupDevice == true)
            {
                WriteAccelRange(AccelRange);
                WriteGSRRange(GSRRange);
                WriteSamplingRate(SamplingRate);
                WriteSensors(SetEnabledSensors);
                WriteBufferSize(1);
            }

            ReadSamplingRate();
            ReadCalibrationParameters("All");

            //if (FirmwareVersion == 0.1)
            if (CompatibilityCode == 1)
            {
                WriteBytes(new byte[1] { (byte)PacketTypeShimmer2.GET_ACCEL_CALIBRATION_COMMAND }, 0, 1);
                System.Threading.Thread.Sleep(500);

                WriteBytes(new byte[1] { (byte)PacketTypeShimmer2.GET_GYRO_CALIBRATION_COMMAND }, 0, 1);
                System.Threading.Thread.Sleep(500);

                WriteBytes(new byte[1] { (byte)PacketTypeShimmer2.GET_MAG_CALIBRATION_COMMAND }, 0, 1);
                System.Threading.Thread.Sleep(500);
            }
            if (VersionLaterThan(1, 1, 0, 0))
            {
                WriteBytes(new byte[2] { (byte)PacketTypeShimmer2.SET_BUFFER_SIZE_COMMAND, (byte)1 }, 0, 2);
                System.Threading.Thread.Sleep(200);
                WriteBytes(new byte[1] { (byte)PacketTypeShimmer2.GET_SAMPLING_RATE_COMMAND }, 0, 1);
                System.Threading.Thread.Sleep(200);
                WriteBytes(new byte[1] { (byte)PacketTypeShimmer2.GET_MAG_GAIN_COMMAND }, 0, 1);
                System.Threading.Thread.Sleep(200);
                if (HardwareVersion != (int)ShimmerBluetooth.ShimmerVersion.SHIMMER3)
                {
                    if (!LowPowerMagEnabled)
                    {
                        double samplingRate = (double)1024 / (double)ADCRawSamplingRateValue;

                        if (samplingRate > 50)
                        {
                            WriteMagSamplingRate(6);
                        }
                        else if (samplingRate > 20)
                        {
                            WriteMagSamplingRate(5);
                        }
                        else if (samplingRate > 10)
                        {
                            WriteMagSamplingRate(4);
                        }
                        else
                        {
                            WriteMagSamplingRate(3);
                        }
                    }
                    else
                    {
                        WriteMagSamplingRate(4);
                    }
                }
                else
                {
                    double samplingRate = (double)1024 / (double)ADCRawSamplingRateValue;
                    if (!LowPowerMagEnabled)
                    {
                        if (samplingRate > 102.4)
                        {
                            WriteMagSamplingRate(7);
                        }
                        else if (samplingRate > 51.2)
                        {
                            WriteMagSamplingRate(6);
                        }
                        else if (samplingRate > 10.24)
                        {
                            WriteMagSamplingRate(4);
                        }
                        else
                        {
                            WriteMagSamplingRate(4);
                        }
                    }
                    else
                    {
                        if (samplingRate >= 1)
                        {
                            WriteMagSamplingRate(4);
                        }
                        else
                        {
                            WriteMagSamplingRate(1);
                        }
                    }
                }
            }

            // Not strictly necessary here unless the GSR sensor is selected, but easier to get this value set correctly to begin with
            ReadGSRRange();
            Inquiry();
        }

        //protected void InitializeShimmer3()
        public void InitializeShimmer3()
        {
            if (SetupDevice == true)
            {
                WriteAccelRange(AccelRange);
                WriteGSRRange(GSRRange);
                WriteGyroRange(GyroRange);
                WriteMagRange(MagGain);
                WriteSamplingRate(SamplingRate); //note that this updates the exg data rate using WriteEXGRate which updates Exg1RegArray and Exg2RegArray
                WriteInternalExpPower(InternalExpPower);
                WriteEXGConfigurations(Exg1RegArray, Exg2RegArray);
                WriteSensors(SetEnabledSensors); //this should always be the last command
                SetLowPowerAccel(LowPowerAccelEnabled);
                SetLowPowerMag(LowPowerMagEnabled);
                SetLowPowerGyro(LowPowerGyroEnabled);
            }

            ReadAccelRange();
            ReadSamplingRate();
            ReadMagRange();
            ReadGyroRange();
            ReadAccelSamplingRate();
            ReadCalibrationParameters("All");
            ReadPressureCalibrationCoefficients();
            ReadEXGConfigurations(1);
            ReadEXGConfigurations(2);
            ReadBaudRate();
            Inquiry();
        }

        public void InitializeShimmerECGMD()
        {
            if (SetupDevice == true)
            {
                WriteSamplingRate(SamplingRate); //note that this updates the exg data rate using WriteEXGRate which updates Exg1RegArray and Exg2RegArray
                WriteEXGConfigurations(Exg1RegArray, Exg2RegArray);
                WriteSensors(SetEnabledSensors); //this should always be the last command
            }

            ReadSamplingRate();
            ReadEXGConfigurations(1);
            ReadEXGConfigurations(2);
            ReadBaudRate();
            Inquiry();
        }

        public Boolean isShimmer3withUpdatedSensors()
        {
            if ((HardwareVersion == (int)ShimmerBluetooth.ShimmerVersion.SHIMMER3R)
                || (HardwareVersion == (int)ShimmerBluetooth.ShimmerVersion.SHIMMER3 && (
                    (ExpansionBoardId == (int)ExpansionBoardDetectShimmer3.EXP_BRD_GSR_UNIFIED && ExpansionBoardRev >= 3)
                    || (ExpansionBoardId == (int)ExpansionBoardDetectShimmer3.EXP_BRD_EXG_UNIFIED && ExpansionBoardRev >= 3)
                    || (ExpansionBoardId == (int)ExpansionBoardDetectShimmer3.EXP_BRD_BR_AMP_UNIFIED && ExpansionBoardRev >= 2)
                    || (ExpansionBoardId == (int)ExpansionBoardDetectShimmer3.SHIMMER3 && ExpansionBoardRev >= 6)
                    || (ExpansionBoardId == (int)ExpansionBoardDetectShimmer3.EXPANSION_PROTO3_DELUXE && ExpansionBoardRev >= 3))))//??????
            //				|| (expBrdId==HW_ID_SR_CODES.EXP_BRD_PROTO3_MINI && expBrdRev>=3) //??????
            {
                return true;
            }
            else
            {
                return false;
            }
        }

        public Boolean isShimmer3RwithHighGAccelSupport()
        {
            if ((HardwareVersion == (int)ShimmerBluetooth.ShimmerVersion.SHIMMER3R) && (
                (ExpansionBoardId == (int)ExpansionBoardDetectShimmer3.SHIMMER3)
                || (ExpansionBoardId == (int)ExpansionBoardDetectShimmer3.EXPANSION_PROTO3_DELUXE && ExpansionBoardRev == 4 && ExpansionBoardRevSpecial == 0)
                || (ExpansionBoardId == (int)ExpansionBoardDetectShimmer3.EXP_BRD_BR_AMP_UNIFIED && ExpansionBoardRev == 4 && ExpansionBoardRevSpecial == 0)
                 || (ExpansionBoardId == (int)ExpansionBoardDetectShimmer3.EXP_BRD_GSR_UNIFIED && ExpansionBoardRev == 6 && ExpansionBoardRevSpecial == 0) 
                 || (ExpansionBoardId == (int)ExpansionBoardDetectShimmer3.EXP_BRD_GSR_UNIFIED && ExpansionBoardRev == 7 && ExpansionBoardRevSpecial == 0)
                 || (ExpansionBoardId == (int)ExpansionBoardDetectShimmer3.EXP_BRD_GSR_UNIFIED && ExpansionBoardRev == 7 && ExpansionBoardRevSpecial == 1)
                 || (ExpansionBoardId == (int)ExpansionBoardDetectShimmer3.EXP_BRD_GSR_UNIFIED && ExpansionBoardRev == 8 && ExpansionBoardRevSpecial == 0) 
                || (ExpansionBoardId == (int)ExpansionBoardDetectShimmer3.EXP_BRD_EXG_UNIFIED && ExpansionBoardRev == 7 && ExpansionBoardRevSpecial == 0)
                || (ExpansionBoardId == (int)ExpansionBoardDetectShimmer3.EXP_BRD_EXG_UNIFIED && ExpansionBoardRev == 7 && ExpansionBoardRevSpecial == 1)
                || (ExpansionBoardId == (int)ExpansionBoardDetectShimmer3.EXP_BRD_EXG_UNIFIED && ExpansionBoardRev == 8 && ExpansionBoardRevSpecial == 0)))

            {
                return true;
            } else
            {
                return false;
            }
        }
        public Boolean isShimmer3RwithAltMagSupport()
        {
            if ((HardwareVersion == (int)ShimmerBluetooth.ShimmerVersion.SHIMMER3R) && (
                 (ExpansionBoardId == (int)ExpansionBoardDetectShimmer3.SHIMMER3 && ExpansionBoardRev == 11 && ExpansionBoardRevSpecial == 0)
                 || (ExpansionBoardId == (int)ExpansionBoardDetectShimmer3.EXPANSION_PROTO3_DELUXE && ExpansionBoardRev == 4 && ExpansionBoardRevSpecial == 0)
                 || (ExpansionBoardId == (int)ExpansionBoardDetectShimmer3.EXP_BRD_BR_AMP_UNIFIED && ExpansionBoardRev == 4 && ExpansionBoardRevSpecial == 0)
                  || (ExpansionBoardId == (int)ExpansionBoardDetectShimmer3.EXP_BRD_GSR_UNIFIED && ExpansionBoardRev == 6 && ExpansionBoardRevSpecial == 0)
                  || (ExpansionBoardId == (int)ExpansionBoardDetectShimmer3.EXP_BRD_GSR_UNIFIED && ExpansionBoardRev == 7 && ExpansionBoardRevSpecial == 0)
                  || (ExpansionBoardId == (int)ExpansionBoardDetectShimmer3.EXP_BRD_GSR_UNIFIED && ExpansionBoardRev == 7 && ExpansionBoardRevSpecial == 1)
                  || (ExpansionBoardId == (int)ExpansionBoardDetectShimmer3.EXP_BRD_GSR_UNIFIED && ExpansionBoardRev == 8 && ExpansionBoardRevSpecial == 0)
                 || (ExpansionBoardId == (int)ExpansionBoardDetectShimmer3.EXP_BRD_EXG_UNIFIED && ExpansionBoardRev == 7 && ExpansionBoardRevSpecial == 0)
                 || (ExpansionBoardId == (int)ExpansionBoardDetectShimmer3.EXP_BRD_EXG_UNIFIED && ExpansionBoardRev == 7 && ExpansionBoardRevSpecial == 1)
                 || (ExpansionBoardId == (int)ExpansionBoardDetectShimmer3.EXP_BRD_EXG_UNIFIED && ExpansionBoardRev == 8 && ExpansionBoardRevSpecial == 0)))

            {
                return true;
            }
            else
            {
                return false;
            }
        }


        public void InterpretInquiryResponseShimmer3R(List<byte> packet)
        {
            mEnableTimeStampAlignmentCheck = true;
            //check if this packet is sane, and not just random
            if ((packet.Count >= 4))       // max number of channels currently allowable
            {
                ADCRawSamplingRateValue = (int)packet[0] + ((((int)packet[1]) << 8) & 0xFF00);
                SamplingRate = (double)32768 / ADCRawSamplingRateValue;
                ConfigSetupByte0 = (long)packet[2] + (((long)packet[3]) << 8) + (((long)packet[4]) << 16) + (((long)packet[5]) << 24) + (((long)packet[6]) << 32) + (((long)packet[7]) << 40) + (((long)packet[8]) << 48);
                AccelHRBit = (int)((ConfigSetupByte0 >> 0) & 0x01);
                AccelLPBit = (int)((ConfigSetupByte0 >> 1) & 0x01);
                AccelRange = (int)((ConfigSetupByte0 >> 2) & 0x03);
                GyroRange = (int)((ConfigSetupByte0 >> 16) & 0x03); //gyro range lsb 8+8

                AltMagRange = (int)((ConfigSetupByte0 >> 21) & 0x07);
                AccelSamplingRate = (int)((ConfigSetupByte0 >> 4) & 0xF);
                GyroSamplingRate = (int)((ConfigSetupByte0 >> 8) & 0xFF);
                magSamplingRate = (int)((ConfigSetupByte0 >> 18) & 0x07);
                PressureResolution = (int)((ConfigSetupByte0 >> 28) & 0x03);
                GSRRange = (int)((ConfigSetupByte0 >> 25) & 0x07); 
                InternalExpPower = (int)((ConfigSetupByte0 >> 24) & 0x01);
                Mpu9150AccelRange = (int)((ConfigSetupByte0 >> 30) & 0x03); //8+8+8+6

                int MSB_Gyro_Range = (int)((ConfigSetupByte0 >> 34) & 0x01); //8+8+8+8+2
                GyroRange = GyroRange + (MSB_Gyro_Range << 2);

                int MSB_LP_MODE = (int)((ConfigSetupByte0 >> 33) & 0x01); //8+8+8+8+1
                AccelLPBit = AccelLPBit + (MSB_LP_MODE << 1);

                magSamplingRate = (int)((ConfigSetupByte0 >> 18) & 0x03);//8+8+2

                int MSB_PRESSURE_RES = (int)((ConfigSetupByte0 >> 32) & 0x01); //8+8+8+8
                PressureResolution = PressureResolution + (MSB_PRESSURE_RES << 2);

                LNAccelRange = (int)((ConfigSetupByte0 >> 30) & 0x03); //8+8+8+6

                AltAccelSamplingRate = (int)((ConfigSetupByte0 >> 38) & 0x03); //8+8+8+8+6

                AltMagSamplingRate = (int)((ConfigSetupByte0 >> 40) & 0x3F); //8+8+8+8+8

                if (HardwareVersion == (int)ShimmerBluetooth.ShimmerVersion.SHIMMER3R)
                {
                    if (magSamplingRate == 0x00)
                    {
                        LowPowerMagEnabled = true;
                    }

                    if ((AccelSamplingRate == 1))
                    {
                        LowPowerAccelEnabled = true;
                    }

                    if (GyroSamplingRate == 0x01)
                    {
                        LowPowerGyroEnabled = true;
                    }

                }

                NumberofChannels = (int)packet[6 + 3];
                BufferSize = (int)packet[7 + 3];
                ListofSensorChannels.Clear();

                for (int i = 0; i < NumberofChannels; i++)
                {
                    ListofSensorChannels.Add(packet[8 + 3 + i]);
                }
                byte[] signalIdArray = ListofSensorChannels.ToArray();
                InterpretDataPacketFormat(NumberofChannels, signalIdArray);
                IsFilled = true;


            }
        }
        public void InterpretInquiryResponseShimmer3(List<byte> packet)
        {
            //check if this packet is sane, and not just random
            if ((packet.Count >= 4))       // max number of channels currently allowable
            {
                ADCRawSamplingRateValue = (int)packet[0] + ((((int)packet[1]) << 8) & 0xFF00);
                SamplingRate = (double)32768 / ADCRawSamplingRateValue;
                ConfigSetupByte0 = (long)packet[2] + (((long)packet[3]) << 8) + (((long)packet[4]) << 16) + (((long)packet[5]) << 24);
                AccelHRBit = (int)((ConfigSetupByte0 >> 0) & 0x01);
                AccelLPBit = (int)((ConfigSetupByte0 >> 1) & 0x01);
                AccelRange = (int)((ConfigSetupByte0 >> 2) & 0x03);
                GyroRange = (int)((ConfigSetupByte0 >> 16) & 0x03);
                MagGain = (int)((ConfigSetupByte0 >> 21) & 0x07);
                AccelSamplingRate = (int)((ConfigSetupByte0 >> 4) & 0xF);
                GyroSamplingRate = (int)((ConfigSetupByte0 >> 8) & 0xFF);
                magSamplingRate = (int)((ConfigSetupByte0 >> 18) & 0x07);
                PressureResolution = (int)((ConfigSetupByte0 >> 28) & 0x03);
                GSRRange = (int)((ConfigSetupByte0 >> 25) & 0x07);
                InternalExpPower = (int)((ConfigSetupByte0 >> 24) & 0x01);
                Mpu9150AccelRange = (int)((ConfigSetupByte0 >> 30) & 0x03);

                if (HardwareVersion == (int)ShimmerBluetooth.ShimmerVersion.SHIMMER3)
                {
                    if ((magSamplingRate == 4 && ADCRawSamplingRateValue < 3200)) //3200 us the raw ADC value and not in HZ
                    {
                        LowPowerMagEnabled = true;
                    }

                    if ((AccelSamplingRate == 2 && ADCRawSamplingRateValue < 3200))
                    {
                        LowPowerAccelEnabled = true;
                    }

                    if ((GyroSamplingRate == 0xFF && ADCRawSamplingRateValue < 3200))
                    {
                        LowPowerGyroEnabled = true;
                    }
                }

                NumberofChannels = (int)packet[6];
                BufferSize = (int)packet[7];
                ListofSensorChannels.Clear();

                for (int i = 0; i < NumberofChannels; i++)
                {
                    ListofSensorChannels.Add(packet[8 + i]);
                }
                byte[] signalIdArray = ListofSensorChannels.ToArray();
                InterpretDataPacketFormat(NumberofChannels, signalIdArray);
                IsFilled = true;


            }
        }
        public void InterpretInquiryResponseShimmer2(List<byte> packet) // this is the inquiry
        {

            //check if this packet is sane, and not just random
            if ((packet.Count >= 5))
            {
                ADCRawSamplingRateValue = (int)packet[0];
                SamplingRate = (double)1024 / ADCRawSamplingRateValue;
                AccelRange = (int)packet[1];
                ConfigSetupByte0 = (int)packet[2];
                NumberofChannels = (int)packet[3];
                BufferSize = (int)packet[4];

                ListofSensorChannels.Clear();
                for (int i = 0; i < NumberofChannels; i++)
                {
                    ListofSensorChannels.Add(packet[5 + i]);
                }
                byte[] signalIdArray = ListofSensorChannels.ToArray();
                InterpretDataPacketFormat(NumberofChannels, signalIdArray);
                IsFilled = true;


            }
        }



        protected void RetrieveKinematicCalibrationParametersFromPacket(byte[] bufferCalibrationParameters, byte packetType)
        {
            String[] dataType = { "i16", "i16", "i16", "i16", "i16", "i16", "i8", "i8", "i8", "i8", "i8", "i8", "i8", "i8", "i8" };
            int[] formattedPacket = FormatDataPacketReverse(bufferCalibrationParameters, dataType); // using the datatype the calibration parameters are converted
            double[] AM = new double[9];
            for (int i = 0; i < 9; i++)
            {
                AM[i] = ((double)formattedPacket[6 + i]) / 100;
            }

            double[,] alignmentMatrix = new double[3, 3] { { AM[0], AM[1], AM[2] }, { AM[3], AM[4], AM[5] }, { AM[6], AM[7], AM[8] } };
            double[,] sensitivityMatrix = new double[3, 3] { { formattedPacket[3], 0, 0 }, { 0, formattedPacket[4], 0 }, { 0, 0, formattedPacket[5] } };
            double[,] offsetVector = { { formattedPacket[0] }, { formattedPacket[1] }, { formattedPacket[2] } };

            // the hardware version has to be checked as well 
            if (packetType == (byte)PacketTypeShimmer2.ACCEL_CALIBRATION_RESPONSE && sensitivityMatrix[0, 0] != -1 && (HardwareVersion == (int)ShimmerBluetooth.ShimmerVersion.SHIMMER2R || HardwareVersion == (int)ShimmerBluetooth.ShimmerVersion.SHIMMER2))
            {
                AlignmentMatrixAccel = alignmentMatrix;
                OffsetVectorAccel = offsetVector;
                SensitivityMatrixAccel = sensitivityMatrix;
                DefaultAccelParams = false;
            }
            else if (packetType == (byte)PacketTypeShimmer3.LNACCEL_CALIBRATION_RESPONSE && (sensitivityMatrix[0, 0] != -1 && !UtilCalibration.AllElementsAre(sensitivityMatrix, 0)))
            {
                AlignmentMatrixAccel = alignmentMatrix;
                OffsetVectorAccel = offsetVector;
                SensitivityMatrixAccel = sensitivityMatrix;
                DefaultAccelParams = false;
            }
            else if (packetType == (byte)PacketTypeShimmer2.ACCEL_CALIBRATION_RESPONSE && sensitivityMatrix[0, 0] == -1 && (HardwareVersion == (int)ShimmerBluetooth.ShimmerVersion.SHIMMER2R || HardwareVersion == (int)ShimmerBluetooth.ShimmerVersion.SHIMMER2))
            {
                DefaultAccelParams = true;
                if (AccelRange == 0)
                {
                    SensitivityMatrixAccel = SENSITIVITY_MATRIX_ACCEL_1_5G_Shimmer2;
                }
                else if (AccelRange == 1)
                {
                    SensitivityMatrixAccel = SENSITIVITY_MATRIX_ACCEL_2G_SHIMMER2;
                }
                else if (AccelRange == 2)
                {
                    SensitivityMatrixAccel = SENSITIVITY_MATRIX_ACCEL_4G_SHIMMER2;
                }
                else if (AccelRange == 3)
                {
                    SensitivityMatrixAccel = SENSITIVITY_MATRIX_ACCEL_6G_SHIMMER2;
                }
                AlignmentMatrixAccel = ALIGNMENT_MATRIX_ACCEL_SHIMMER2;
                OffsetVectorAccel = OFFSET_VECTOR_ACCEL_SHIMMER2;

            }
            else if (packetType == (byte)PacketTypeShimmer3.LNACCEL_CALIBRATION_RESPONSE && sensitivityMatrix[0, 0] == -1 && HardwareVersion == (int)ShimmerBluetooth.ShimmerVersion.SHIMMER3)
            {
                DefaultAccelParams = true;
                if (!isShimmer3withUpdatedSensors())
                {
                    SensitivityMatrixAccel = SENSITIVITY_MATRIX_LOW_NOISE_ACCEL_SHIMMER3_KCRB5_2042;
                    AlignmentMatrixAccel = ALIGNMENT_MATRIX_LOW_NOISE_ACCEL_SHIMMER3_KCRB5_2042;
                    OffsetVectorAccel = OFFSET_VECTOR_ACCEL_LOW_NOISE_SHIMMER3_KCRB5_2042;
                }
                else
                {
                    SensitivityMatrixAccel = SENSITIVITY_MATRIX_LOW_NOISE_ACCEL_SHIMMER3_KXTC9_2050;
                    AlignmentMatrixAccel = ALIGNMENT_MATRIX_LOW_NOISE_ACCEL_SHIMMER3_KXTC9_2050;
                    OffsetVectorAccel = OFFSET_VECTOR_ACCEL_LOW_NOISE_SHIMMER3_KXTC9_2050;
                }
            }
            else if (packetType == (byte)PacketTypeShimmer3.LNACCEL_CALIBRATION_RESPONSE && (sensitivityMatrix[0, 0] == -1 || UtilCalibration.AllElementsAre(sensitivityMatrix, 0))
                && HardwareVersion == (int)ShimmerBluetooth.ShimmerVersion.SHIMMER3R)
            {
                DefaultAccelParams = true;

                if (LNAccelRange == 0)
                {
                    SensitivityMatrixAccel = SENSITIVITY_MATRIX_LOW_NOISE_ACCEL_2G_SHIMMER3R_LSM6DSV;
                }
                else if (LNAccelRange == 1)
                {
                    SensitivityMatrixAccel = SENSITIVITY_MATRIX_LOW_NOISE_ACCEL_4G_SHIMMER3R_LSM6DSV;
                }
                else if (LNAccelRange == 2)
                {
                    SensitivityMatrixAccel = SENSITIVITY_MATRIX_LOW_NOISE_ACCEL_8G_SHIMMER3R_LSM6DSV;
                }
                else if (LNAccelRange == 3)
                {
                    SensitivityMatrixAccel = SENSITIVITY_MATRIX_LOW_NOISE_ACCEL_16G_SHIMMER3R_LSM6DSV;
                }
                AlignmentMatrixAccel = ALIGNMENT_MATRIX_LOW_NOISE_ACCEL_SHIMMER3R_LSM6DSV;
                OffsetVectorAccel = OFFSET_VECTOR_ACCEL_LOW_NOISE_SHIMMER3R_LSM6DSV;

            }
            else if (packetType == (byte)PacketTypeShimmer3.WR_ACCEL_CALIBRATION_RESPONSE && (sensitivityMatrix[0, 0] != -1 && !UtilCalibration.AllElementsAre(sensitivityMatrix, 0)))
            {
                AlignmentMatrixAccel2 = alignmentMatrix;
                OffsetVectorAccel2 = offsetVector;
                SensitivityMatrixAccel2 = sensitivityMatrix;
                DefaultWRAccelParams = false;
            }
            else if (packetType == (byte)PacketTypeShimmer3.WR_ACCEL_CALIBRATION_RESPONSE && sensitivityMatrix[0, 0] == -1 && HardwareVersion == (int)ShimmerBluetooth.ShimmerVersion.SHIMMER3)
            {
                DefaultWRAccelParams = true;
                if (!isShimmer3withUpdatedSensors())
                {
                    if (AccelRange == 0)
                    {
                        SensitivityMatrixAccel2 = SENSITIVITY_MATRIX_WIDE_RANGE_ACCEL_2G_SHIMMER3_LSM303DLHC;
                    }
                    else if (AccelRange == 1)
                    {
                        SensitivityMatrixAccel2 = SENSITIVITY_MATRIX_WIDE_RANGE_ACCEL_4G_SHIMMER3_LSM303DLHC;
                    }
                    else if (AccelRange == 2)
                    {
                        SensitivityMatrixAccel2 = SENSITIVITY_MATRIX_WIDE_RANGE_ACCEL_8G_SHIMMER3_LSM303DLHC;
                    }
                    else if (AccelRange == 3)
                    {
                        SensitivityMatrixAccel2 = SENSITIVITY_MATRIX_WIDE_RANGE_ACCEL_16G_SHIMMER3_LSM303DLHC;
                    }
                    AlignmentMatrixAccel2 = ALIGNMENT_MATRIX_WIDE_RANGE_ACCEL_SHIMMER3_LSM303DLHC;
                    OffsetVectorAccel2 = OFFSET_VECTOR_ACCEL_WIDE_RANGE_SHIMMER3_LSM303DLHC;
                }
                else
                {
                    if (AccelRange == 0)
                    {
                        SensitivityMatrixAccel2 = SENSITIVITY_MATRIX_WIDE_RANGE_ACCEL_2G_SHIMMER3_LSM303AH;
                    }
                    else if (AccelRange == 2)
                    {
                        SensitivityMatrixAccel2 = SENSITIVITY_MATRIX_WIDE_RANGE_ACCEL_4G_SHIMMER3_LSM303AH;
                    }
                    else if (AccelRange == 3)
                    {
                        SensitivityMatrixAccel2 = SENSITIVITY_MATRIX_WIDE_RANGE_ACCEL_8G_SHIMMER3_LSM303AH;
                    }
                    else if (AccelRange == 1)
                    {
                        SensitivityMatrixAccel2 = SENSITIVITY_MATRIX_WIDE_RANGE_ACCEL_16G_SHIMMER3_LSM303AH;
                    }
                    AlignmentMatrixAccel2 = ALIGNMENT_MATRIX_WIDE_RANGE_ACCEL_SHIMMER3_LSM303AH;
                    OffsetVectorAccel2 = OFFSET_VECTOR_ACCEL_WIDE_RANGE_SHIMMER3_LSM303AH;
                }
            }
            else if (packetType == (byte)PacketTypeShimmer3.WR_ACCEL_CALIBRATION_RESPONSE && (sensitivityMatrix[0, 0] == -1 || UtilCalibration.AllElementsAre(sensitivityMatrix, 0))
              && HardwareVersion == (int)ShimmerBluetooth.ShimmerVersion.SHIMMER3R)
            {
                DefaultWRAccelParams = true;
                if (AccelRange == 0)
                {
                    SensitivityMatrixAccel2 = SENSITIVITY_MATRIX_WIDE_RANGE_ACCEL_2G_SHIMMER3R_LIS2DW12;
                }
                else if (AccelRange == 1)
                {
                    SensitivityMatrixAccel2 = SENSITIVITY_MATRIX_WIDE_RANGE_ACCEL_4G_SHIMMER3R_LIS2DW12;
                }
                else if (AccelRange == 2)
                {
                    SensitivityMatrixAccel2 = SENSITIVITY_MATRIX_WIDE_RANGE_ACCEL_8G_SHIMMER3R_LIS2DW12;
                }
                else if (AccelRange == 3)
                {
                    SensitivityMatrixAccel2 = SENSITIVITY_MATRIX_WIDE_RANGE_ACCEL_16G_SHIMMER3R_LIS2DW12;
                }
                AlignmentMatrixAccel2 = ALIGNMENT_MATRIX_WIDE_RANGE_ACCEL_SHIMMER3R_LIS2DW12;
                OffsetVectorAccel2 = OFFSET_VECTOR_ACCEL_WIDE_RANGE_SHIMMER3R_LIS2DW12;
            }
            else if (packetType == (byte)PacketTypeShimmer3.ALT_ACCEL_CALIBRATION_RESPONSE && (sensitivityMatrix[0, 0] != -1 && !UtilCalibration.AllElementsAre(sensitivityMatrix, 0)))
            {
                AlignmentMatrixAltAccel = alignmentMatrix;
                OffsetVectorAltAccel = offsetVector;
                SensitivityMatrixAltAccel = sensitivityMatrix;
                DefaultHighGAccelParams = false;

            }
            else if (packetType == (byte)PacketTypeShimmer3.ALT_ACCEL_CALIBRATION_RESPONSE && (sensitivityMatrix[0, 0] == -1 || UtilCalibration.AllElementsAre(sensitivityMatrix, 0))
                && HardwareVersion == (int)ShimmerBluetooth.ShimmerVersion.SHIMMER3R)
            {
                DefaultHighGAccelParams = true;
                SensitivityMatrixAltAccel = SENSITIVITY_MATRIX_HIGH_G_ACCEL_200G_SHIMMER3R_ADXL371;
                AlignmentMatrixAltAccel = ALIGNMENT_MATRIX_HIGH_G_ACCEL_SHIMMER3R_ADXL371;
                OffsetVectorAltAccel = OFFSET_VECTOR_ACCEL_HIGH_G_SHIMMER3R_ADXL371;
            }
            else if (packetType == (byte)PacketTypeShimmer2.GYRO_CALIBRATION_RESPONSE && sensitivityMatrix[0, 0] != -1 && (HardwareVersion == (int)ShimmerBluetooth.ShimmerVersion.SHIMMER2R || HardwareVersion == (int)ShimmerBluetooth.ShimmerVersion.SHIMMER2))
            {
                AlignmentMatrixGyro = alignmentMatrix;
                OffsetVectorGyro = offsetVector;
                SensitivityMatrixGyro = sensitivityMatrix;
                SensitivityMatrixGyro[0, 0] = SensitivityMatrixGyro[0, 0] / 100;
                SensitivityMatrixGyro[1, 1] = SensitivityMatrixGyro[1, 1] / 100;
                SensitivityMatrixGyro[2, 2] = SensitivityMatrixGyro[2, 2] / 100;
                DefaultGyroParams = false;
            }
            else if (packetType == (byte)PacketTypeShimmer3.GYRO_CALIBRATION_RESPONSE && (sensitivityMatrix[0, 0] != -1 && !UtilCalibration.AllElementsAre(sensitivityMatrix, 0)))
            {
                AlignmentMatrixGyro = alignmentMatrix;
                OffsetVectorGyro = offsetVector;
                SensitivityMatrixGyro = sensitivityMatrix;
                SensitivityMatrixGyro[0, 0] = SensitivityMatrixGyro[0, 0] / 100;
                SensitivityMatrixGyro[1, 1] = SensitivityMatrixGyro[1, 1] / 100;
                SensitivityMatrixGyro[2, 2] = SensitivityMatrixGyro[2, 2] / 100;
                DefaultGyroParams = false;
            }
            else if (packetType == (byte)PacketTypeShimmer2.GYRO_CALIBRATION_RESPONSE && sensitivityMatrix[0, 0] == -1 && (HardwareVersion == (int)ShimmerBluetooth.ShimmerVersion.SHIMMER2R || HardwareVersion == (int)ShimmerBluetooth.ShimmerVersion.SHIMMER2))
            {
                DefaultGyroParams = true;
                SensitivityMatrixGyro = SENSITIVITY_MATRIX_GYRO_SHIMMER2;
                AlignmentMatrixGyro = ALIGNMENT_MATRIX_GYRO_SHIMMER2;
                OffsetVectorGyro = OFFSET_VECTOR_GYRO_SHIMMER2;
            }
            else if (packetType == (byte)PacketTypeShimmer3.GYRO_CALIBRATION_RESPONSE && sensitivityMatrix[0, 0] == -1 && HardwareVersion == (int)ShimmerBluetooth.ShimmerVersion.SHIMMER3)
            {
                DefaultGyroParams = true;
                if (GyroRange == 0)
                {
                    SensitivityMatrixGyro = SENSITIVITIY_MATRIX_GYRO_250DPS_SHIMMER3;
                }
                else if (GyroRange == 1)
                {
                    SensitivityMatrixGyro = SENSITIVITIY_MATRIX_GYRO_500DPS_SHIMMER3;
                }
                else if (GyroRange == 2)
                {
                    SensitivityMatrixGyro = SENSITIVITIY_MATRIX_GYRO_1000DPS_SHIMMER3;
                }
                else if (GyroRange == 3)
                {
                    SensitivityMatrixGyro = SENSITIVITIY_MATRIX_GYRO_2000DPS_SHIMMER3;
                }
                AlignmentMatrixGyro = ALIGNMENT_MATRIX_GYRO_SHIMMER3;
                OffsetVectorGyro = OFFSET_VECTOR_GYRO_SHIMMER3;

            }
            else if (packetType == (byte)PacketTypeShimmer3.GYRO_CALIBRATION_RESPONSE && (sensitivityMatrix[0, 0] == -1 || UtilCalibration.AllElementsAre(sensitivityMatrix, 0))
                && HardwareVersion == (int)ShimmerBluetooth.ShimmerVersion.SHIMMER3R)
            {
                DefaultGyroParams = true;
                if (GyroRange == 0)
                {
                    SensitivityMatrixGyro = SENSITIVITIY_MATRIX_GYRO_125DPS_SHIMMER3R_LSM6DSV;
                }
                else if (GyroRange == 1)
                {
                    SensitivityMatrixGyro = SENSITIVITIY_MATRIX_GYRO_250DPS_SHIMMER3R_LSM6DSV;
                }
                else if (GyroRange == 2)
                {
                    SensitivityMatrixGyro = SENSITIVITIY_MATRIX_GYRO_500DPS_SHIMMER3R_LSM6DSV;
                }
                else if (GyroRange == 3)
                {
                    SensitivityMatrixGyro = SENSITIVITIY_MATRIX_GYRO_1000DPS_SHIMMER3R_LSM6DSV;
                }
                else if (GyroRange == 4)
                {
                    SensitivityMatrixGyro = SENSITIVITIY_MATRIX_GYRO_2000DPS_SHIMMER3R_LSM6DSV;
                }
                else if (GyroRange == 5)
                {
                    SensitivityMatrixGyro = SENSITIVITIY_MATRIX_GYRO_4000DPS_SHIMMER3R_LSM6DSV;
                }
                AlignmentMatrixGyro = ALIGNMENT_MATRIX_GYRO_SHIMMER3R_LSM6DSV;
                OffsetVectorGyro = OFFSET_VECTOR_GYRO_SHIMMER3R_LSM6DSV;

            }
            else if (packetType == (byte)PacketTypeShimmer2.MAG_CALIBRATION_RESPONSE && sensitivityMatrix[0, 0] != -1 && (HardwareVersion == (int)ShimmerBluetooth.ShimmerVersion.SHIMMER2R || HardwareVersion == (int)ShimmerBluetooth.ShimmerVersion.SHIMMER2))
            {
                DefaultMagParams = false;
                AlignmentMatrixMag = alignmentMatrix;
                OffsetVectorMag = offsetVector;
                SensitivityMatrixMag = sensitivityMatrix;
            }
            else if (packetType == (byte)PacketTypeShimmer3.MAG_CALIBRATION_RESPONSE && (sensitivityMatrix[0, 0] != -1 && !UtilCalibration.AllElementsAre(sensitivityMatrix, 0)))
            {
                DefaultMagParams = false;
                AlignmentMatrixMag = alignmentMatrix;
                OffsetVectorMag = offsetVector;
                SensitivityMatrixMag = sensitivityMatrix;
            }
            else if (packetType == (byte)PacketTypeShimmer2.MAG_CALIBRATION_RESPONSE && sensitivityMatrix[0, 0] == -1 && (HardwareVersion == (int)ShimmerBluetooth.ShimmerVersion.SHIMMER2R || HardwareVersion == (int)ShimmerBluetooth.ShimmerVersion.SHIMMER2))
            {
                DefaultMagParams = true;
                AlignmentMatrixMag = ALIGNMENT_MATRIX_MAG_SHIMMER2;
                OffsetVectorMag = OFFSET_VECTOR_MAG_SHIMMER2;

                if (GetMagRange() == 0)
                {
                    SensitivityMatrixMag = SENSITIVITY_MATRIX_MAG_0_8GA_SHIMMER2;
                }
                else if (GetMagRange() == 1)
                {
                    SensitivityMatrixMag = SENSITIVITY_MATRIX_MAG_1_3GA_SHIMMER2;
                }
                else if (GetMagRange() == 2)
                {
                    SensitivityMatrixMag = SENSITIVITY_MATRIX_MAG_1_9GA_SHIMMER2;
                }
                else if (GetMagRange() == 3)
                {
                    SensitivityMatrixMag = SENSITIVITY_MATRIX_MAG_2_5GA_SHIMMER2;
                }
                else if (GetMagRange() == 4)
                {
                    SensitivityMatrixMag = SENSITIVITY_MATRIX_MAG_4_0GA_SHIMMER2;
                }
                else if (GetMagRange() == 5)
                {
                    SensitivityMatrixMag = SENSITIVITY_MATRIX_MAG_4_7GA_SHIMMER2;
                }
                else if (GetMagRange() == 6)
                {
                    SensitivityMatrixMag = SENSITIVITY_MATRIX_MAG_5_6GA_SHIMMER2;
                }
                else if (GetMagRange() == 7)
                {
                    SensitivityMatrixMag = SENSITIVITY_MATRIX_MAG_8_1GA_SHIMMER2;
                }

            }
            else if (packetType == (byte)PacketTypeShimmer3.MAG_CALIBRATION_RESPONSE && sensitivityMatrix[0, 0] == -1 && HardwareVersion == (int)ShimmerBluetooth.ShimmerVersion.SHIMMER3)
            {
                DefaultMagParams = true;
                if (!isShimmer3withUpdatedSensors())
                {
                    AlignmentMatrixMag = ALIGNMENT_MATRIX_MAG_SHIMMER3_LSM303DLHC;
                    OffsetVectorMag = OFFSET_VECTOR_MAG_SHIMMER3_LSM303DLHC;
                    if (GetMagRange() == 1)
                    {
                        SensitivityMatrixMag = SENSITIVITY_MATRIX_MAG_1_3GA_SHIMMER3_LSM303DLHC;
                    }
                    else if (GetMagRange() == 2)
                    {
                        SensitivityMatrixMag = SENSITIVITY_MATRIX_MAG_1_9GA_SHIMMER3_LSM303DLHC;
                    }
                    else if (GetMagRange() == 3)
                    {
                        SensitivityMatrixMag = SENSITIVITY_MATRIX_MAG_2_5GA_SHIMMER3_LSM303DLHC;
                    }
                    else if (GetMagRange() == 4)
                    {
                        SensitivityMatrixMag = SENSITIVITY_MATRIX_MAG_4_0GA_SHIMMER3_LSM303DLHC;
                    }
                    else if (GetMagRange() == 5)
                    {
                        SensitivityMatrixMag = SENSITIVITY_MATRIX_MAG_4_7GA_SHIMMER3_LSM303DLHC;
                    }
                    else if (GetMagRange() == 6)
                    {
                        SensitivityMatrixMag = SENSITIVITY_MATRIX_MAG_5_6GA_SHIMMER3_LSM303DLHC;
                    }
                    else if (GetMagRange() == 7)
                    {
                        SensitivityMatrixMag = SENSITIVITY_MATRIX_MAG_8_1GA_SHIMMER3_LSM303DLHC;
                    }
                }
                else //using Shimmer3 with updated sensors 
                {
                    AlignmentMatrixMag = ALIGNMENT_MATRIX_MAG_SHIMMER3_LSM303AH;
                    OffsetVectorMag = OFFSET_VECTOR_MAG_SHIMMER3_LSM303AH;
                    SensitivityMatrixMag = SENSITIVITY_MATRIX_MAG_50GA_SHIMMER3_LSM303AH;
                }

            }
            else if (packetType == (byte)PacketTypeShimmer3.MAG_CALIBRATION_RESPONSE && (sensitivityMatrix[0, 0] == -1 || UtilCalibration.AllElementsAre(sensitivityMatrix, 0))
                && HardwareVersion == (int)ShimmerBluetooth.ShimmerVersion.SHIMMER3R)
            {
                DefaultMagParams = true;
                /*
                AlignmentMatrixMag = ALIGNMENT_MATRIX_MAG_SHIMMER3R_LIS3MDL;
                OffsetVectorMag = OFFSET_VECTOR_MAG_SHIMMER3R_LIS3MDL;
                if (GetMagRange() == 0)
                {
                    SensitivityMatrixMag = SENSITIVITY_MATRIX_MAG_4GA_SHIMMER3R_LIS3MDL;
                }
                else if (GetMagRange() == 1)
                {
                    SensitivityMatrixMag = SENSITIVITY_MATRIX_MAG_8GA_SHIMMER3R_LIS3MDL;
                }
                else if (GetMagRange() == 2)
                {
                    SensitivityMatrixMag = SENSITIVITY_MATRIX_MAG_12GA_SHIMMER3R_LIS3MDL;
                }
                else if (GetMagRange() == 3)
                {
                    SensitivityMatrixMag = SENSITIVITY_MATRIX_MAG_16GA_SHIMMER3R_LIS3MDL;
                }
                */
                AlignmentMatrixMag2 = ALIGNMENT_MATRIX_MAG_SHIMMER3R_LIS2MDL;
                OffsetVectorMag2 = OFFSET_VECTOR_MAG_SHIMMER3R_LIS2MDL;
                SensitivityMatrixMag2 = SENSITIVITY_MATRIX_MAG_50GA_SHIMMER3R_LIS2MDL;
            }
            else if (packetType == (byte)PacketTypeShimmer3.ALT_MAG_CALIBRATION_RESPONSE && (sensitivityMatrix[0, 0] != -1 && !UtilCalibration.AllElementsAre(sensitivityMatrix, 0)))
            {
                AlignmentMatrixMag2 = alignmentMatrix;
                OffsetVectorMag2 = offsetVector;
                SensitivityMatrixMag2 = sensitivityMatrix;
                DefaultWRMagParams = false;
            }
            else if (packetType == (byte)PacketTypeShimmer3.ALT_MAG_CALIBRATION_RESPONSE && (sensitivityMatrix[0, 0] == -1 || UtilCalibration.AllElementsAre(sensitivityMatrix, 0))
                && HardwareVersion == (int)ShimmerBluetooth.ShimmerVersion.SHIMMER3R)
            {
                DefaultWRMagParams = true;
                /*
                AlignmentMatrixMag2 = ALIGNMENT_MATRIX_MAG_SHIMMER3R_LIS2MDL;
                OffsetVectorMag2 = OFFSET_VECTOR_MAG_SHIMMER3R_LIS2MDL;
                SensitivityMatrixMag2 = SENSITIVITY_MATRIX_MAG_50GA_SHIMMER3R_LIS2MDL;
                */
                AlignmentMatrixMag = ALIGNMENT_MATRIX_MAG_SHIMMER3R_LIS3MDL;
                OffsetVectorMag = OFFSET_VECTOR_MAG_SHIMMER3R_LIS3MDL;
                if (GetAltMagRange() == 0)
                {
                    SensitivityMatrixMag = SENSITIVITY_MATRIX_MAG_4GA_SHIMMER3R_LIS3MDL;
                }
                else if (GetAltMagRange() == 1)
                {
                    SensitivityMatrixMag = SENSITIVITY_MATRIX_MAG_8GA_SHIMMER3R_LIS3MDL;
                }
                else if (GetAltMagRange() == 2)
                {
                    SensitivityMatrixMag = SENSITIVITY_MATRIX_MAG_12GA_SHIMMER3R_LIS3MDL;
                }
                else if (GetAltMagRange() == 3)
                {
                    SensitivityMatrixMag = SENSITIVITY_MATRIX_MAG_16GA_SHIMMER3R_LIS3MDL;
                }
            }

        }


        protected int[] FormatDataPacketReverse(byte[] data, String[] dataType)
        {
            int iData = 0;
            int[] formattedData = new int[dataType.Length];

            for (int i = 0; i < dataType.Length; i++)
                if (dataType[i] == "u8")
                {
                    formattedData[i] = (int)data[iData];
                    iData = iData + 1;
                }
                else if (dataType[i] == "i8")
                {
                    formattedData[i] = Calculatetwoscomplement((int)((int)0xFF & data[iData]), 8);
                    iData = iData + 1;
                }
                else if (dataType[i] == "u12")
                {

                    formattedData[i] = (int)((int)(data[iData + 1] & 0xFF) + ((int)(data[iData] & 0xFF) << 8));
                    iData = iData + 2;
                }
                else if (dataType[i] == "u14")
                {

                    formattedData[i] = (int)((int)(data[iData + 1] & 0xFF) + ((int)(data[iData] & 0xFF) << 8));
                    iData = iData + 2;
                }
                else if (dataType[i] == "u16")
                {

                    formattedData[i] = (int)((int)(data[iData + 1] & 0xFF) + ((int)(data[iData] & 0xFF) << 8));
                    iData = iData + 2;
                }
                else if (dataType[i] == "i16")
                {

                    formattedData[i] = Calculatetwoscomplement((int)((int)(data[iData + 1] & 0xFF) + ((int)(data[iData] & 0xFF) << 8)), 16);
                    iData = iData + 2;
                }
            return formattedData;
        }

        protected int Calculatetwoscomplement(int signedData, int bitLength)
        {
            int newData = signedData;
            if (signedData >= (1 << (bitLength - 1)))
            {
                newData = -((signedData ^ (int)(Math.Pow(2, bitLength) - 1)) + 1);
            }

            return newData;
        }



        protected void InterpretDataPacketFormat(int nC, byte[] signalid)
        {
            String[] signalNameArray = new String[MAX_NUMBER_OF_SIGNALS];
            String[] signalDataTypeArray = new String[MAX_NUMBER_OF_SIGNALS];
            signalNameArray[0] = ShimmerConfiguration.SignalNames.TIMESTAMP;
            signalDataTypeArray[0] = "u16";
            int packetSize = 2; // Time stamp
            if (CompatibilityCode >= 6)
            {
                signalDataTypeArray[0] = "u24";
                packetSize = TimeStampPacketByteSize; // Time stamp
            }
            int enabledSensors = 0x00;
            for (int i = 0; i < nC; i++)
            {
                if ((byte)signalid[i] == (byte)0x00)
                {
                    if (HardwareVersion == (int)ShimmerVersion.SHIMMER3)
                    {
                        signalNameArray[i + 1] = Shimmer3Configuration.SignalNames.LOW_NOISE_ACCELEROMETER_X;
                        signalDataTypeArray[i + 1] = "u12";
                        packetSize = packetSize + 2;
                        enabledSensors = (enabledSensors | (int)SensorBitmapShimmer3.SENSOR_A_ACCEL);
                    }
                    else if (HardwareVersion == (int)ShimmerVersion.SHIMMER3R)
                    {
                        signalNameArray[i + 1] = Shimmer3Configuration.SignalNames.LOW_NOISE_ACCELEROMETER_X;
                        signalDataTypeArray[i + 1] = "i16";
                        packetSize = packetSize + 2;
                        enabledSensors = (enabledSensors | (int)SensorBitmapShimmer3.SENSOR_A_ACCEL);
                    }
                    else if (HardwareVersion == (int)ShimmerVersion.SHIMMER2R)
                    {
                        signalNameArray[i + 1] = Shimmer2Configuration.SignalNames.ACCELEROMETER_X;
                        signalDataTypeArray[i + 1] = "u12";
                        packetSize = packetSize + 2;
                        enabledSensors = (enabledSensors | (int)SensorBitmapShimmer2.SENSOR_ACCEL);
                    }
                }
                else if ((byte)signalid[i] == (byte)0x01)
                {
                    if (HardwareVersion == (int)ShimmerVersion.SHIMMER3)
                    {
                        signalNameArray[i + 1] = Shimmer3Configuration.SignalNames.LOW_NOISE_ACCELEROMETER_Y;
                        signalDataTypeArray[i + 1] = "u12";
                        packetSize = packetSize + 2;
                        enabledSensors = (enabledSensors | (int)SensorBitmapShimmer3.SENSOR_A_ACCEL);
                    }
                    else if (HardwareVersion == (int)ShimmerVersion.SHIMMER3 || HardwareVersion == (int)ShimmerVersion.SHIMMER3R)
                    {
                        signalNameArray[i + 1] = Shimmer3Configuration.SignalNames.LOW_NOISE_ACCELEROMETER_Y;
                        signalDataTypeArray[i + 1] = "i16";
                        packetSize = packetSize + 2;
                        enabledSensors = (enabledSensors | (int)SensorBitmapShimmer3.SENSOR_A_ACCEL);
                    }
                    else if (HardwareVersion == (int)ShimmerVersion.SHIMMER2R)
                    {
                        signalNameArray[i + 1] = Shimmer2Configuration.SignalNames.ACCELEROMETER_Y;
                        signalDataTypeArray[i + 1] = "u12";
                        packetSize = packetSize + 2;
                        enabledSensors = (enabledSensors | (int)SensorBitmapShimmer2.SENSOR_ACCEL);
                    }
                }
                else if ((byte)signalid[i] == (byte)0x02)
                {
                    if (HardwareVersion == (int)ShimmerVersion.SHIMMER3)
                    {
                        signalNameArray[i + 1] = Shimmer3Configuration.SignalNames.LOW_NOISE_ACCELEROMETER_Z;
                        signalDataTypeArray[i + 1] = "u12";
                        packetSize = packetSize + 2;
                        enabledSensors = (enabledSensors | (int)SensorBitmapShimmer2.SENSOR_ACCEL);
                    }
                    else if (HardwareVersion == (int)ShimmerVersion.SHIMMER3R)
                    {
                        signalNameArray[i + 1] = Shimmer3Configuration.SignalNames.LOW_NOISE_ACCELEROMETER_Z;
                        signalDataTypeArray[i + 1] = "i16";
                        packetSize = packetSize + 2;
                        enabledSensors = (enabledSensors | (int)SensorBitmapShimmer2.SENSOR_ACCEL);
                    }
                    else if (HardwareVersion == (int)ShimmerVersion.SHIMMER2R)
                    {
                        signalNameArray[i + 1] = Shimmer2Configuration.SignalNames.ACCELEROMETER_Z;
                        signalDataTypeArray[i + 1] = "u12";
                        packetSize = packetSize + 2;
                        enabledSensors = (enabledSensors | (int)SensorBitmapShimmer2.SENSOR_ACCEL);
                    }
                }
                else if ((byte)signalid[i] == (byte)0x03)
                {

                    if (HardwareVersion == (int)ShimmerVersion.SHIMMER3 || HardwareVersion == (int)ShimmerVersion.SHIMMER3R)
                    {
                        signalNameArray[i + 1] = Shimmer3Configuration.SignalNames.V_SENSE_BATT; //should be the battery but this will do for now
                        signalDataTypeArray[i + 1] = "i16";
                        packetSize = packetSize + 2;
                        enabledSensors = (enabledSensors | (int)SensorBitmapShimmer3.SENSOR_VBATT);
                    }
                    else if (HardwareVersion == (int)ShimmerVersion.SHIMMER2R)
                    {
                        signalNameArray[i + 1] = Shimmer2Configuration.SignalNames.GYROSCOPE_X;
                        signalDataTypeArray[i + 1] = "u12";
                        packetSize = packetSize + 2;
                        enabledSensors = (enabledSensors | (int)SensorBitmapShimmer2.SENSOR_GYRO);
                    }
                }
                else if ((byte)signalid[i] == (byte)0x04)
                {

                    if (HardwareVersion == (int)ShimmerVersion.SHIMMER3 || HardwareVersion == (int)ShimmerVersion.SHIMMER3R)
                    {
                        signalDataTypeArray[i + 1] = "i16";
                        packetSize = packetSize + 2;
                        signalNameArray[i + 1] = Shimmer3Configuration.SignalNames.WIDE_RANGE_ACCELEROMETER_X;
                        enabledSensors = (enabledSensors | (int)SensorBitmapShimmer3.SENSOR_D_ACCEL);
                    }
                    else if (HardwareVersion == (int)ShimmerVersion.SHIMMER2R)
                    {
                        signalNameArray[i + 1] = Shimmer2Configuration.SignalNames.GYROSCOPE_Y;
                        signalDataTypeArray[i + 1] = "u12";
                        packetSize = packetSize + 2;
                        enabledSensors = (enabledSensors | (int)SensorBitmapShimmer2.SENSOR_GYRO);
                    }
                }
                else if ((byte)signalid[i] == (byte)0x05)
                {
                    if (HardwareVersion == (int)ShimmerVersion.SHIMMER3 || HardwareVersion == (int)ShimmerVersion.SHIMMER3R)
                    {
                        signalDataTypeArray[i + 1] = "i16";
                        packetSize = packetSize + 2;
                        signalNameArray[i + 1] = Shimmer3Configuration.SignalNames.WIDE_RANGE_ACCELEROMETER_Y;
                        enabledSensors = (enabledSensors | (int)SensorBitmapShimmer3.SENSOR_D_ACCEL);
                    }
                    else if (HardwareVersion == (int)ShimmerVersion.SHIMMER2R)
                    {
                        signalNameArray[i + 1] = Shimmer2Configuration.SignalNames.GYROSCOPE_Z;
                        signalDataTypeArray[i + 1] = "u12";
                        packetSize = packetSize + 2;
                        enabledSensors = (enabledSensors | (int)SensorBitmapShimmer2.SENSOR_GYRO);
                    }
                }
                else if ((byte)signalid[i] == (byte)0x06)
                {
                    if (HardwareVersion == (int)ShimmerVersion.SHIMMER3 || HardwareVersion == (int)ShimmerVersion.SHIMMER3R)
                    {
                        signalDataTypeArray[i + 1] = "i16";
                        packetSize = packetSize + 2;
                        signalNameArray[i + 1] = Shimmer3Configuration.SignalNames.WIDE_RANGE_ACCELEROMETER_Z;
                        enabledSensors = (enabledSensors | (int)SensorBitmapShimmer3.SENSOR_D_ACCEL);
                    }
                    else if (HardwareVersion == (int)ShimmerVersion.SHIMMER2R)
                    {
                        signalNameArray[i + 1] = Shimmer2Configuration.SignalNames.MAGNETOMETER_X;
                        signalDataTypeArray[i + 1] = "i16";
                        packetSize = packetSize + 2;
                        enabledSensors = (enabledSensors | (int)SensorBitmapShimmer2.SENSOR_MAG);
                    }

                }
                else if ((byte)signalid[i] == (byte)0x07)
                {
                    if (HardwareVersion == (int)ShimmerVersion.SHIMMER3 || HardwareVersion == (int)ShimmerVersion.SHIMMER3R)
                    {
                        signalNameArray[i + 1] = Shimmer3Configuration.SignalNames.MAGNETOMETER_X;
                        if (!isShimmer3withUpdatedSensors())
                        {
                            signalDataTypeArray[i + 1] = "i16*";
                        }
                        else
                        {
                            signalDataTypeArray[i + 1] = "i16";
                        }
                        packetSize = packetSize + 2;
                        enabledSensors = (enabledSensors | (int)SensorBitmapShimmer3.SENSOR_MAG);
                    }
                    else if (HardwareVersion == (int)ShimmerVersion.SHIMMER2R)
                    {
                        signalDataTypeArray[i + 1] = "i16";
                        packetSize = packetSize + 2;
                        signalNameArray[i + 1] = Shimmer2Configuration.SignalNames.MAGNETOMETER_Y;
                        enabledSensors = (enabledSensors | (int)SensorBitmapShimmer2.SENSOR_MAG);
                    }


                }
                else if ((byte)signalid[i] == (byte)0x08)
                {
                    if (HardwareVersion == (int)ShimmerVersion.SHIMMER3 || HardwareVersion == (int)ShimmerVersion.SHIMMER3R)
                    {
                        signalNameArray[i + 1] = Shimmer3Configuration.SignalNames.MAGNETOMETER_Y;
                        if (!isShimmer3withUpdatedSensors())
                        {
                            signalDataTypeArray[i + 1] = "i16*";
                        }
                        else
                        {
                            signalDataTypeArray[i + 1] = "i16";
                        }
                        packetSize = packetSize + 2;
                        enabledSensors = (enabledSensors | (int)SensorBitmapShimmer3.SENSOR_MAG);
                    }
                    else if (HardwareVersion == (int)ShimmerVersion.SHIMMER2R)
                    {
                        signalDataTypeArray[i + 1] = "i16";
                        packetSize = packetSize + 2;
                        signalNameArray[i + 1] = Shimmer2Configuration.SignalNames.MAGNETOMETER_Z;
                        enabledSensors = (enabledSensors | (int)SensorBitmapShimmer2.SENSOR_MAG);
                    }

                }
                else if ((byte)signalid[i] == (byte)0x09)
                {
                    if (HardwareVersion == (int)ShimmerVersion.SHIMMER3 || HardwareVersion == (int)ShimmerVersion.SHIMMER3R)
                    {
                        signalNameArray[i + 1] = Shimmer3Configuration.SignalNames.MAGNETOMETER_Z;
                        if (!isShimmer3withUpdatedSensors())
                        {
                            signalDataTypeArray[i + 1] = "i16*";
                        }
                        else
                        {
                            signalDataTypeArray[i + 1] = "i16";
                        }
                        packetSize = packetSize + 2;
                        enabledSensors = (enabledSensors | (int)SensorBitmapShimmer3.SENSOR_MAG);
                    }
                    else if (HardwareVersion == (int)ShimmerVersion.SHIMMER2R)
                    {
                        signalNameArray[i + 1] = Shimmer2Configuration.SignalNames.ECG_RA_LL;
                        signalDataTypeArray[i + 1] = "u12";
                        packetSize = packetSize + 2;
                        enabledSensors = (enabledSensors | (int)SensorBitmapShimmer2.SENSOR_ECG);
                    }


                }
                else if ((byte)signalid[i] == (byte)0x0A)
                {
                    if (HardwareVersion == (int)ShimmerVersion.SHIMMER3)
                    {
                        signalNameArray[i + 1] = Shimmer3Configuration.SignalNames.GYROSCOPE_X;
                        signalDataTypeArray[i + 1] = "i16*";
                        packetSize = packetSize + 2;
                        enabledSensors = (enabledSensors | (int)SensorBitmapShimmer3.SENSOR_GYRO);
                    }
                    else if (HardwareVersion == (int)ShimmerVersion.SHIMMER3R)
                    {
                        signalNameArray[i + 1] = Shimmer3Configuration.SignalNames.GYROSCOPE_X;
                        signalDataTypeArray[i + 1] = "i16";
                        packetSize = packetSize + 2;
                        enabledSensors = (enabledSensors | (int)SensorBitmapShimmer3.SENSOR_GYRO);
                    }
                    else if (HardwareVersion == (int)ShimmerVersion.SHIMMER2R)
                    {

                        signalNameArray[i + 1] = Shimmer2Configuration.SignalNames.ECG_LA_LL;
                        signalDataTypeArray[i + 1] = "u12";
                        packetSize = packetSize + 2;
                        enabledSensors = (enabledSensors | (int)SensorBitmapShimmer2.SENSOR_ECG);
                    }
                }
                else if ((byte)signalid[i] == (byte)0x0B)
                {
                    if (HardwareVersion == (int)ShimmerVersion.SHIMMER3)
                    {
                        signalNameArray[i + 1] = Shimmer3Configuration.SignalNames.GYROSCOPE_Y;
                        signalDataTypeArray[i + 1] = "i16*";
                        packetSize = packetSize + 2;
                        enabledSensors = (enabledSensors | (int)SensorBitmapShimmer3.SENSOR_GYRO);
                    }
                    else if (HardwareVersion == (int)ShimmerVersion.SHIMMER3R)
                    {
                        signalNameArray[i + 1] = Shimmer3Configuration.SignalNames.GYROSCOPE_Y;
                        signalDataTypeArray[i + 1] = "i16";
                        packetSize = packetSize + 2;
                        enabledSensors = (enabledSensors | (int)SensorBitmapShimmer3.SENSOR_GYRO);
                    }
                    else if (HardwareVersion == (int)ShimmerVersion.SHIMMER2R)
                    {
                        signalNameArray[i + 1] = Shimmer2Configuration.SignalNames.GSR;
                        signalDataTypeArray[i + 1] = "u16";
                        packetSize = packetSize + 2;
                        enabledSensors = (enabledSensors | (int)SensorBitmapShimmer2.SENSOR_GSR);
                    }
                }
                else if ((byte)signalid[i] == (byte)0x0C)
                {
                    if (HardwareVersion == (int)ShimmerVersion.SHIMMER3)
                    {
                        signalNameArray[i + 1] = Shimmer3Configuration.SignalNames.GYROSCOPE_Z;
                        signalDataTypeArray[i + 1] = "i16*";
                        packetSize = packetSize + 2;
                        enabledSensors = (enabledSensors | (int)SensorBitmapShimmer3.SENSOR_GYRO);
                    }
                    else if (HardwareVersion == (int)ShimmerVersion.SHIMMER3R)
                    {
                        signalNameArray[i + 1] = Shimmer3Configuration.SignalNames.GYROSCOPE_Z;
                        signalDataTypeArray[i + 1] = "i16";
                        packetSize = packetSize + 2;
                        enabledSensors = (enabledSensors | (int)SensorBitmapShimmer3.SENSOR_GYRO);
                    }
                    else if (HardwareVersion == (int)ShimmerVersion.SHIMMER2R)
                    {
                        signalNameArray[i + 1] = Shimmer2Configuration.SignalNames.GSR_RES;
                        signalDataTypeArray[i + 1] = "u16";
                        packetSize = packetSize + 2;
                        enabledSensors = (enabledSensors | (int)SensorBitmapShimmer2.SENSOR_GSR);
                    }
                }
                else if ((byte)signalid[i] == (byte)0x0D)
                {
                    if (HardwareVersion == (int)ShimmerVersion.SHIMMER3)
                    {
                        signalNameArray[i + 1] = Shimmer3Configuration.SignalNames.EXTERNAL_ADC_A7;
                        signalDataTypeArray[i + 1] = "u12";
                        packetSize = packetSize + 2;
                        enabledSensors = (enabledSensors | (int)SensorBitmapShimmer3.SENSOR_EXT_A7);
                    }
                    else if (HardwareVersion == (int)ShimmerVersion.SHIMMER3R)
                    {
                        signalNameArray[i + 1] = Shimmer3RConfiguration.SignalNames.GEN_EXT_ADC_A0;
                        signalDataTypeArray[i + 1] = "u12";
                        packetSize = packetSize + 2;
                        enabledSensors = (enabledSensors | (int)SensorBitmapShimmer3R.SENSOR_EXT_A0);
                    }
                    else if (HardwareVersion == (int)ShimmerVersion.SHIMMER2R)
                    {
                        signalNameArray[i + 1] = Shimmer2Configuration.SignalNames.EMG;
                        signalDataTypeArray[i + 1] = "u12";
                        packetSize = packetSize + 2;
                        enabledSensors = (enabledSensors | (int)SensorBitmapShimmer2.SENSOR_EMG);
                    }
                }
                else if ((byte)signalid[i] == (byte)0x0E)
                {
                    if (HardwareVersion == (int)ShimmerVersion.SHIMMER3)
                    {
                        signalNameArray[i + 1] = Shimmer3Configuration.SignalNames.EXTERNAL_ADC_A6;
                        signalDataTypeArray[i + 1] = "u12";
                        packetSize = packetSize + 2;
                        enabledSensors = (enabledSensors | (int)SensorBitmapShimmer3.SENSOR_EXT_A6);
                    }
                    else if (HardwareVersion == (int)ShimmerVersion.SHIMMER3R)
                    {
                        signalNameArray[i + 1] = Shimmer3RConfiguration.SignalNames.GEN_EXT_ADC_A1;
                        signalDataTypeArray[i + 1] = "u12";
                        packetSize = packetSize + 2;
                        enabledSensors = (enabledSensors | (int)SensorBitmapShimmer3R.SENSOR_EXT_A1);
                    }
                    else if (HardwareVersion == (int)ShimmerVersion.SHIMMER2R)
                    {
                        signalNameArray[i + 1] = Shimmer2Configuration.SignalNames.EXPBOARD_A0;
                        signalDataTypeArray[i + 1] = "u12";
                        packetSize = packetSize + 2;
                        enabledSensors = (enabledSensors | (int)SensorBitmapShimmer2.SENSOR_EXP_BOARD_A0);
                    }
                }
                else if ((byte)signalid[i] == (byte)0x0F)
                {
                    if (HardwareVersion == (int)ShimmerVersion.SHIMMER3)
                    {
                        signalNameArray[i + 1] = Shimmer3Configuration.SignalNames.EXTERNAL_ADC_A15;
                        signalDataTypeArray[i + 1] = "u12";
                        packetSize = packetSize + 2;
                        enabledSensors = (enabledSensors | (int)SensorBitmapShimmer3.SENSOR_EXT_A15);
                    }
                    else if (HardwareVersion == (int)ShimmerVersion.SHIMMER3R)
                    {
                        signalNameArray[i + 1] = Shimmer3RConfiguration.SignalNames.GEN_EXT_ADC_A2;
                        signalDataTypeArray[i + 1] = "u12";
                        packetSize = packetSize + 2;
                        enabledSensors = (enabledSensors | (int)SensorBitmapShimmer3R.SENSOR_EXT_A2);
                    }
                    else if (HardwareVersion == (int)ShimmerVersion.SHIMMER2R)
                    {
                        signalNameArray[i + 1] = Shimmer2Configuration.SignalNames.EXPBOARD_A7;
                        signalDataTypeArray[i + 1] = "u12";
                        packetSize = packetSize + 2;
                        enabledSensors = (enabledSensors | (int)SensorBitmapShimmer2.SENSOR_EXP_BOARD_A7);
                    }
                }
                else if ((byte)signalid[i] == (byte)0x10)
                {
                    if (HardwareVersion == (int)ShimmerVersion.SHIMMER3)
                    {
                        signalNameArray[i + 1] = Shimmer3Configuration.SignalNames.INTERNAL_ADC_A1;
                        signalDataTypeArray[i + 1] = "u12";
                        packetSize = packetSize + 2;
                        enabledSensors = (enabledSensors | (int)SensorBitmapShimmer3.SENSOR_INT_A1);
                    }
                    else if (HardwareVersion == (int)ShimmerVersion.SHIMMER3R)
                    {
                        signalNameArray[i + 1] = Shimmer3RConfiguration.SignalNames.GEN_INT_ADC_A3;
                        signalDataTypeArray[i + 1] = "u12";
                        packetSize = packetSize + 2;
                        enabledSensors = (enabledSensors | (int)SensorBitmapShimmer3R.SENSOR_INT_A3);
                    }
                    else if (HardwareVersion == (int)ShimmerVersion.SHIMMER2R)
                    {
                        signalNameArray[i + 1] = Shimmer2Configuration.SignalNames.STRAIN_GAUGE_HIGH;

                        signalDataTypeArray[i + 1] = "u12";
                        packetSize = packetSize + 2;
                        enabledSensors = (enabledSensors | (int)SensorBitmapShimmer2.SENSOR_STRAIN_GAUGE);
                    }
                }
                else if ((byte)signalid[i] == (byte)0x11)
                {
                    if (HardwareVersion == (int)ShimmerVersion.SHIMMER3)
                    {
                        signalNameArray[i + 1] = Shimmer3Configuration.SignalNames.INTERNAL_ADC_A12;
                        signalDataTypeArray[i + 1] = "u12";
                        packetSize = packetSize + 2;
                        enabledSensors = (enabledSensors | (int)SensorBitmapShimmer3.SENSOR_INT_A12);
                    }
                    else if (HardwareVersion == (int)ShimmerVersion.SHIMMER3R)
                    {
                        signalNameArray[i + 1] = Shimmer3RConfiguration.SignalNames.GEN_INT_ADC_A0;
                        signalDataTypeArray[i + 1] = "u12";
                        packetSize = packetSize + 2;
                        enabledSensors = (enabledSensors | (int)SensorBitmapShimmer3R.SENSOR_INT_A0);
                    }
                    else if (HardwareVersion == (int)ShimmerVersion.SHIMMER2R)
                    {
                        signalNameArray[i + 1] = Shimmer2Configuration.SignalNames.STRAIN_GAUGE_LOW;
                        signalDataTypeArray[i + 1] = "u12";
                        packetSize = packetSize + 2;
                        enabledSensors = (enabledSensors | (int)SensorBitmapShimmer2.SENSOR_STRAIN_GAUGE);
                    }
                }
                else if ((byte)signalid[i] == (byte)0x12)
                {
                    if (HardwareVersion == (int)ShimmerVersion.SHIMMER3)
                    {
                        signalNameArray[i + 1] = Shimmer3Configuration.SignalNames.INTERNAL_ADC_A13;
                        signalDataTypeArray[i + 1] = "u12";
                        packetSize = packetSize + 2;
                        enabledSensors = (enabledSensors | (int)SensorBitmapShimmer3.SENSOR_INT_A13);
                    }
                    else if (HardwareVersion == (int)ShimmerVersion.SHIMMER3R)
                    {
                        signalNameArray[i + 1] = Shimmer3RConfiguration.SignalNames.GEN_INT_ADC_A1;
                        signalDataTypeArray[i + 1] = "u12";
                        packetSize = packetSize + 2;
                        enabledSensors = (enabledSensors | (int)SensorBitmapShimmer3R.SENSOR_INT_A1);
                    }
                    else if (HardwareVersion == (int)ShimmerVersion.SHIMMER2R)
                    {
                        signalNameArray[i + 1] = Shimmer2Configuration.SignalNames.HEART_RATE;
                        if (CompatibilityCode == 1)
                        {
                            signalDataTypeArray[i + 1] = "u8";
                            packetSize = packetSize + 1;
                        }
                        else
                        {
                            signalDataTypeArray[i + 1] = "u16";
                            packetSize = packetSize + 2;
                        }
                        enabledSensors = (enabledSensors | (int)SensorBitmapShimmer2.SENSOR_HEART);
                    }
                }
                else if ((byte)signalid[i] == (byte)0x13)
                {
                    if (HardwareVersion == (int)ShimmerVersion.SHIMMER3)
                    {
                        signalNameArray[i + 1] = Shimmer3Configuration.SignalNames.INTERNAL_ADC_A14;
                        signalDataTypeArray[i + 1] = "u12";
                        packetSize = packetSize + 2;
                        enabledSensors = (enabledSensors | (int)SensorBitmapShimmer3.SENSOR_INT_A14);
                    }
                    else if (HardwareVersion == (int)ShimmerVersion.SHIMMER3R)
                    {
                        signalNameArray[i + 1] = Shimmer3RConfiguration.SignalNames.GEN_INT_ADC_A2;
                        signalDataTypeArray[i + 1] = "u12";
                        packetSize = packetSize + 2;
                        enabledSensors = (enabledSensors | (int)SensorBitmapShimmer3R.SENSOR_INT_A2);
                    }
                }
                else if ((byte)signalid[i] == (byte)0x14)
                {

                    if (HardwareVersion == (int)ShimmerVersion.SHIMMER3R)
                    {
                        signalDataTypeArray[i + 1] = "i12*>";
                        packetSize = packetSize + 2;
                        signalNameArray[i + 1] = Shimmer3Configuration.SignalNames.HIGH_G_ACCELEROMETER_X;
                        enabledSensors = (enabledSensors | (int)SensorBitmapShimmer3.SENSOR_ACCEL_ALT);
                    }
                }
                else if ((byte)signalid[i] == (byte)0x15)
                {
                    if (HardwareVersion == (int)ShimmerVersion.SHIMMER3R)
                    {
                        signalDataTypeArray[i + 1] = "i12*>";
                        packetSize = packetSize + 2;
                        signalNameArray[i + 1] = Shimmer3Configuration.SignalNames.HIGH_G_ACCELEROMETER_Y;
                        enabledSensors = (enabledSensors | (int)SensorBitmapShimmer3.SENSOR_ACCEL_ALT);
                    }
                }
                else if ((byte)signalid[i] == (byte)0x16)
                {
                    if (HardwareVersion == (int)ShimmerVersion.SHIMMER3R)
                    {
                        signalDataTypeArray[i + 1] = "i12*>";
                        packetSize = packetSize + 2;
                        signalNameArray[i + 1] = Shimmer3Configuration.SignalNames.HIGH_G_ACCELEROMETER_Z;
                        enabledSensors = (enabledSensors | (int)SensorBitmapShimmer3.SENSOR_ACCEL_ALT);
                    }
                }
                else if ((byte)signalid[i] == (byte)0x17)
                {
                    if (HardwareVersion == (int)ShimmerVersion.SHIMMER3R)
                    {
                        signalNameArray[i + 1] = Shimmer3Configuration.SignalNames.ALT_MAGNETOMETER_X;
                        signalDataTypeArray[i + 1] = "i16"; //TBD; used Signal Datatype from Mag as placeholder
                        packetSize = packetSize + 2;
                        enabledSensors = (enabledSensors | (int)SensorBitmapShimmer3.SENSOR_MAG_ALT);
                    }
                }
                else if ((byte)signalid[i] == (byte)0x18)
                {
                    if (HardwareVersion == (int)ShimmerVersion.SHIMMER3R)
                    {
                        signalNameArray[i + 1] = Shimmer3Configuration.SignalNames.ALT_MAGNETOMETER_Y;
                        signalDataTypeArray[i + 1] = "i16"; //TBD; used Signal Datatype from Mag as placeholder
                        packetSize = packetSize + 2;
                        enabledSensors = (enabledSensors | (int)SensorBitmapShimmer3.SENSOR_MAG_ALT);
                    }
                }
                else if ((byte)signalid[i] == (byte)0x19)
                {
                    if (HardwareVersion == (int)ShimmerVersion.SHIMMER3R)
                    {
                        signalNameArray[i + 1] = Shimmer3Configuration.SignalNames.ALT_MAGNETOMETER_Z;
                        signalDataTypeArray[i + 1] = "i16"; //TBD; used Signal Datatype from Mag as placeholder
                        packetSize = packetSize + 2;
                        enabledSensors = (enabledSensors | (int)SensorBitmapShimmer3.SENSOR_MAG_ALT);
                    }
                }
                else if ((byte)signalid[i] == (byte)0x1A)
                {
                    if (HardwareVersion == (int)ShimmerVersion.SHIMMER3)
                    {
                        signalNameArray[i + 1] = Shimmer3Configuration.SignalNames.TEMPERATURE;
                        signalDataTypeArray[i + 1] = "u16r";
                        packetSize = packetSize + 2;
                        enabledSensors = (enabledSensors | (int)SensorBitmapShimmer3.SENSOR_BMP180_PRESSURE);
                    }
                    else if (HardwareVersion == (int)ShimmerVersion.SHIMMER3R)
                    {
                        signalNameArray[i + 1] = Shimmer3Configuration.SignalNames.TEMPERATURE;
                        signalDataTypeArray[i + 1] = "u24";
                        packetSize = packetSize + 3;
                        enabledSensors = (enabledSensors | (int)SensorBitmapShimmer3.SENSOR_BMP180_PRESSURE);
                    }
                }
                else if ((byte)signalid[i] == (byte)0x1B)
                {
                    if (HardwareVersion == (int)ShimmerVersion.SHIMMER3)
                    {
                        signalNameArray[i + 1] = Shimmer3Configuration.SignalNames.PRESSURE;
                        signalDataTypeArray[i + 1] = "u24r";
                        packetSize = packetSize + 3;
                        enabledSensors = (enabledSensors | (int)SensorBitmapShimmer3.SENSOR_BMP180_PRESSURE);
                    }
                    else if (HardwareVersion == (int)ShimmerVersion.SHIMMER3R)
                    {
                        signalNameArray[i + 1] = Shimmer3Configuration.SignalNames.PRESSURE;
                        signalDataTypeArray[i + 1] = "u24";
                        packetSize = packetSize + 3;
                        enabledSensors = (enabledSensors | (int)SensorBitmapShimmer3.SENSOR_BMP180_PRESSURE);
                    }
                }
                else if ((byte)signalid[i] == (byte)0x1C)
                {
                    if (HardwareVersion == (int)ShimmerVersion.SHIMMER3 || HardwareVersion == (int)ShimmerVersion.SHIMMER3R)
                    {
                        signalNameArray[i + 1] = Shimmer3Configuration.SignalNames.GSR;
                        signalDataTypeArray[i + 1] = "u16";
                        packetSize = packetSize + 2;
                        enabledSensors = (enabledSensors | (int)SensorBitmapShimmer3.SENSOR_GSR);
                    }
                }
                else if ((byte)signalid[i] == (byte)0x1D)
                {
                    if (HardwareVersion == (int)ShimmerBluetooth.ShimmerVersion.SHIMMER3 || HardwareVersion == (int)ShimmerVersion.SHIMMER3R)
                    {
                        signalNameArray[i + 1] = Shimmer3Configuration.SignalNames.EXG1_STATUS;
                        signalDataTypeArray[i + 1] = "u8";
                        packetSize = packetSize + 1;
                    }
                }
                else if ((byte)signalid[i] == (byte)0x1E)//EXG
                {
                    if (HardwareVersion == (int)ShimmerBluetooth.ShimmerVersion.SHIMMER3 || HardwareVersion == (int)ShimmerVersion.SHIMMER3R)
                    {
                        signalNameArray[i + 1] = Shimmer3Configuration.SignalNames.EXG1_CH1;
                        signalDataTypeArray[i + 1] = "i24r";
                        packetSize = packetSize + 3;
                        enabledSensors = (enabledSensors | (int)SensorBitmapShimmer3.SENSOR_EXG1_24BIT);
                    }
                }
                else if ((byte)signalid[i] == (byte)0x1F)//EXG
                {
                    if (HardwareVersion == (int)ShimmerBluetooth.ShimmerVersion.SHIMMER3 || HardwareVersion == (int)ShimmerVersion.SHIMMER3R)
                    {
                        signalNameArray[i + 1] = Shimmer3Configuration.SignalNames.EXG1_CH2;
                        signalDataTypeArray[i + 1] = "i24r";
                        packetSize = packetSize + 3;
                        enabledSensors = (enabledSensors | (int)SensorBitmapShimmer3.SENSOR_EXG1_24BIT);
                    }
                }
                else if ((byte)signalid[i] == (byte)0x20)//EXG
                {
                    if (HardwareVersion == (int)ShimmerBluetooth.ShimmerVersion.SHIMMER3 || HardwareVersion == (int)ShimmerVersion.SHIMMER3R)
                    {
                        signalNameArray[i + 1] = Shimmer3Configuration.SignalNames.EXG2_STATUS;
                        signalDataTypeArray[i + 1] = "u8";
                        packetSize = packetSize + 1;
                    }
                }
                else if ((byte)signalid[i] == (byte)0x21)//EXG
                {
                    if (HardwareVersion == (int)ShimmerBluetooth.ShimmerVersion.SHIMMER3 || HardwareVersion == (int)ShimmerVersion.SHIMMER3R)
                    {
                        signalNameArray[i + 1] = Shimmer3Configuration.SignalNames.EXG2_CH1;
                        signalDataTypeArray[i + 1] = "i24r";
                        packetSize = packetSize + 3;
                        enabledSensors = (enabledSensors | (int)SensorBitmapShimmer3.SENSOR_EXG2_24BIT);
                    }
                }
                else if ((byte)signalid[i] == (byte)0x22)//EXG
                {
                    if (HardwareVersion == (int)ShimmerBluetooth.ShimmerVersion.SHIMMER3 || HardwareVersion == (int)ShimmerVersion.SHIMMER3R)
                    {
                        signalNameArray[i + 1] = Shimmer3Configuration.SignalNames.EXG2_CH2;
                        signalDataTypeArray[i + 1] = "i24r";
                        packetSize = packetSize + 3;
                        enabledSensors = (enabledSensors | (int)SensorBitmapShimmer3.SENSOR_EXG2_24BIT);
                    }
                }
                else if ((byte)signalid[i] == (byte)0x23)//EXG
                {
                    if (HardwareVersion == (int)ShimmerBluetooth.ShimmerVersion.SHIMMER3 || HardwareVersion == (int)ShimmerVersion.SHIMMER3R)
                    {
                        signalNameArray[i + 1] = Shimmer3Configuration.SignalNames.EXG1_CH1_16BIT;
                        signalDataTypeArray[i + 1] = "i16r";
                        packetSize = packetSize + 2;
                        enabledSensors = (enabledSensors | (int)SensorBitmapShimmer3.SENSOR_EXG1_16BIT);
                    }
                }
                else if ((byte)signalid[i] == (byte)0x24)//EXG
                {
                    if (HardwareVersion == (int)ShimmerBluetooth.ShimmerVersion.SHIMMER3 || HardwareVersion == (int)ShimmerVersion.SHIMMER3R)
                    {
                        signalNameArray[i + 1] = Shimmer3Configuration.SignalNames.EXG1_CH2_16BIT;
                        signalDataTypeArray[i + 1] = "i16r";
                        packetSize = packetSize + 2;
                        enabledSensors = (enabledSensors | (int)SensorBitmapShimmer3.SENSOR_EXG1_16BIT);
                    }
                }
                else if ((byte)signalid[i] == (byte)0x25)//EXG
                {
                    if (HardwareVersion == (int)ShimmerBluetooth.ShimmerVersion.SHIMMER3 || HardwareVersion == (int)ShimmerVersion.SHIMMER3R)
                    {
                        signalNameArray[i + 1] = Shimmer3Configuration.SignalNames.EXG2_CH1_16BIT;
                        signalDataTypeArray[i + 1] = "i16r";
                        packetSize = packetSize + 2;
                        enabledSensors = (enabledSensors | (int)SensorBitmapShimmer3.SENSOR_EXG2_16BIT);
                    }
                }
                else if ((byte)signalid[i] == (byte)0x26)//EXG
                {
                    if (HardwareVersion == (int)ShimmerBluetooth.ShimmerVersion.SHIMMER3 || HardwareVersion == (int)ShimmerVersion.SHIMMER3R)
                    {
                        signalNameArray[i + 1] = Shimmer3Configuration.SignalNames.EXG2_CH2_16BIT;
                        signalDataTypeArray[i + 1] = "i16r";
                        packetSize = packetSize + 2;
                        enabledSensors = (enabledSensors | (int)SensorBitmapShimmer3.SENSOR_EXG2_16BIT);
                    }
                }
                else if ((byte)signalid[i] == (byte)0x27)//BRIDGE AMPLIFIER
                {
                    if (HardwareVersion == (int)ShimmerBluetooth.ShimmerVersion.SHIMMER3 || HardwareVersion == (int)ShimmerVersion.SHIMMER3R)
                    {
                        signalNameArray[i + 1] = Shimmer3Configuration.SignalNames.BRIGE_AMPLIFIER_HIGH;
                        signalDataTypeArray[i + 1] = "u12";
                        packetSize = packetSize + 2;
                        enabledSensors = (enabledSensors | (int)SensorBitmapShimmer3.SENSOR_BRIDGE_AMP);
                    }
                }
                else if ((byte)signalid[i] == (byte)0x28)//BRIDGE AMPLIFIER
                {
                    if (HardwareVersion == (int)ShimmerBluetooth.ShimmerVersion.SHIMMER3 || HardwareVersion == (int)ShimmerVersion.SHIMMER3R)
                    {
                        signalNameArray[i + 1] = Shimmer3Configuration.SignalNames.BRIGE_AMPLIFIER_LOW;
                        signalDataTypeArray[i + 1] = "u12";
                        packetSize = packetSize + 2;
                        enabledSensors = (enabledSensors | (int)SensorBitmapShimmer3.SENSOR_BRIDGE_AMP);
                    }
                }
                else
                {
                    signalNameArray[i + 1] = "";
                    signalDataTypeArray[i + 1] = "u12";
                    packetSize = packetSize + 2;
                }

            }
            EnabledSensors = enabledSensors;
            SignalNameArray = signalNameArray;
            SignalDataTypeArray = signalDataTypeArray;
            PacketSize = packetSize;
            //Debug.WriteLine("Packet Size : " + PacketSize + "  CRC Mode and starting byte not included");
        }

        public String[] GetSignalNameArray()
        {
            return SignalNameArray;
        }
        /// <summary>
        /// Returns the address used to connect to the device, note that if the serial port is used it should return the comport number and if the bluetooth address is used the bluetooth address should be returned;
        /// </summary>
        /// <returns></returns>
        public abstract String GetShimmerAddress();
        /// <summary>
        /// Set the address/comport used to connect to the device, therefore this depends on whether you are using the serialport/32feet/xamarin
        /// </summary>
        /// <param name="address"></param>
        public abstract void SetShimmerAddress(String address);

        protected virtual ObjectCluster BuildMsg(List<byte> packet)
        {

            ObjectCluster objectCluster = new ObjectCluster(GetShimmerAddress(), GetDeviceName());
            byte[] newPacketByte = packet.ToArray();
            long[] newPacket = ParseData(newPacketByte, SignalDataTypeArray);

            int iTimeStamp = getSignalIndex(ShimmerConfiguration.SignalNames.TIMESTAMP); //find index
            objectCluster.RawTimeStamp = (int)newPacket[iTimeStamp];
            objectCluster.Add(ShimmerConfiguration.SignalNames.TIMESTAMP, ShimmerConfiguration.SignalFormats.RAW, ShimmerConfiguration.SignalUnits.NoUnits, newPacket[iTimeStamp]);
            double calibratedTS = CalibrateTimeStamp(newPacket[iTimeStamp]);
            objectCluster.Add(ShimmerConfiguration.SignalNames.TIMESTAMP, ShimmerConfiguration.SignalFormats.CAL, ShimmerConfiguration.SignalUnits.MilliSeconds, calibratedTS);
            double time = (DateTime.UtcNow - UnixEpoch).TotalMilliseconds;
            if (FirstSystemTimestamp)
            {
                FirstSystemTimeStampValue = time;
                FirstSystemTimestamp = false;
            }
            double shimmerPCTimeStamp = FirstSystemTimeStampValue + calibratedTS;
            if (ShouldAddSystemTimestamp)
            {
                objectCluster.Add(ShimmerConfiguration.SignalNames.SYSTEM_TIMESTAMP, ShimmerConfiguration.SignalFormats.CAL, ShimmerConfiguration.SignalUnits.MilliSeconds, shimmerPCTimeStamp);
            }

            double[] accelerometer = new double[3];
            double[] gyroscope = new double[3];
            double[] magnetometer = new double[3];

            if (HardwareVersion == (int)ShimmerVersion.SHIMMER3 || HardwareVersion == (int)ShimmerVersion.SHIMMER3R)
            {
                if (((EnabledSensors & (int)SensorBitmapShimmer3.SENSOR_A_ACCEL) > 0))
                {
                    int iAccelX = getSignalIndex(Shimmer3Configuration.SignalNames.LOW_NOISE_ACCELEROMETER_X); //find index
                    int iAccelY = getSignalIndex(Shimmer3Configuration.SignalNames.LOW_NOISE_ACCELEROMETER_Y); //find index
                    int iAccelZ = getSignalIndex(Shimmer3Configuration.SignalNames.LOW_NOISE_ACCELEROMETER_Z); //find index
                    double[] datatemp = new double[3] { newPacket[iAccelX], newPacket[iAccelY], newPacket[iAccelZ] };
                    datatemp = UtilCalibration.CalibrateInertialSensorData(datatemp, AlignmentMatrixAccel, SensitivityMatrixAccel, OffsetVectorAccel);
                    string units;
                    if (DefaultAccelParams)
                    {
                        units = ShimmerConfiguration.SignalUnits.MeterPerSecondSquared_DefaultCal;
                    }
                    else
                    {
                        units = ShimmerConfiguration.SignalUnits.MeterPerSecondSquared;
                    }
                    objectCluster.Add(Shimmer3Configuration.SignalNames.LOW_NOISE_ACCELEROMETER_X, ShimmerConfiguration.SignalFormats.RAW, ShimmerConfiguration.SignalUnits.NoUnits, newPacket[iAccelX]);
                    objectCluster.Add(Shimmer3Configuration.SignalNames.LOW_NOISE_ACCELEROMETER_X, ShimmerConfiguration.SignalFormats.CAL, units, datatemp[0]);
                    objectCluster.Add(Shimmer3Configuration.SignalNames.LOW_NOISE_ACCELEROMETER_Y, ShimmerConfiguration.SignalFormats.RAW, ShimmerConfiguration.SignalUnits.NoUnits, newPacket[iAccelY]);
                    objectCluster.Add(Shimmer3Configuration.SignalNames.LOW_NOISE_ACCELEROMETER_Y, ShimmerConfiguration.SignalFormats.CAL, units, datatemp[1]);
                    objectCluster.Add(Shimmer3Configuration.SignalNames.LOW_NOISE_ACCELEROMETER_Z, ShimmerConfiguration.SignalFormats.RAW, ShimmerConfiguration.SignalUnits.NoUnits, newPacket[iAccelZ]);
                    objectCluster.Add(Shimmer3Configuration.SignalNames.LOW_NOISE_ACCELEROMETER_Z, ShimmerConfiguration.SignalFormats.CAL, units, datatemp[2]);
                    accelerometer[0] = datatemp[0];
                    accelerometer[1] = datatemp[1];
                    accelerometer[2] = datatemp[2];
                }
                if (((EnabledSensors & (int)SensorBitmapShimmer3.SENSOR_D_ACCEL) > 0))
                {
                    int iAccelX = getSignalIndex(Shimmer3Configuration.SignalNames.WIDE_RANGE_ACCELEROMETER_X); //find index
                    int iAccelY = getSignalIndex(Shimmer3Configuration.SignalNames.WIDE_RANGE_ACCELEROMETER_Y); //find index
                    int iAccelZ = getSignalIndex(Shimmer3Configuration.SignalNames.WIDE_RANGE_ACCELEROMETER_Z); //find index
                    double[] datatemp = new double[3] { newPacket[iAccelX], newPacket[iAccelY], newPacket[iAccelZ] };
                    datatemp = UtilCalibration.CalibrateInertialSensorData(datatemp, AlignmentMatrixAccel2, SensitivityMatrixAccel2, OffsetVectorAccel2);
                    string units;
                    if (DefaultWRAccelParams)
                    {
                        units = ShimmerConfiguration.SignalUnits.MeterPerSecondSquared_DefaultCal;
                    }
                    else
                    {
                        units = ShimmerConfiguration.SignalUnits.MeterPerSecondSquared;
                    }
                    objectCluster.Add(Shimmer3Configuration.SignalNames.WIDE_RANGE_ACCELEROMETER_X, ShimmerConfiguration.SignalFormats.RAW, ShimmerConfiguration.SignalUnits.NoUnits, newPacket[iAccelX]);
                    objectCluster.Add(Shimmer3Configuration.SignalNames.WIDE_RANGE_ACCELEROMETER_X, ShimmerConfiguration.SignalFormats.CAL, units, datatemp[0]);
                    objectCluster.Add(Shimmer3Configuration.SignalNames.WIDE_RANGE_ACCELEROMETER_Y, ShimmerConfiguration.SignalFormats.RAW, ShimmerConfiguration.SignalUnits.NoUnits, newPacket[iAccelY]);
                    objectCluster.Add(Shimmer3Configuration.SignalNames.WIDE_RANGE_ACCELEROMETER_Y, ShimmerConfiguration.SignalFormats.CAL, units, datatemp[1]);
                    objectCluster.Add(Shimmer3Configuration.SignalNames.WIDE_RANGE_ACCELEROMETER_Z, ShimmerConfiguration.SignalFormats.RAW, ShimmerConfiguration.SignalUnits.NoUnits, newPacket[iAccelZ]);
                    objectCluster.Add(Shimmer3Configuration.SignalNames.WIDE_RANGE_ACCELEROMETER_Z, ShimmerConfiguration.SignalFormats.CAL, units, datatemp[2]);

                    accelerometer[0] = datatemp[0];
                    accelerometer[1] = datatemp[1];
                    accelerometer[2] = datatemp[2];

                }
                if (((EnabledSensors & (int)SensorBitmapShimmer3.SENSOR_ACCEL_ALT) > 0))
                {
                    int iAccelX = getSignalIndex(Shimmer3Configuration.SignalNames.HIGH_G_ACCELEROMETER_X); //find index
                    int iAccelY = getSignalIndex(Shimmer3Configuration.SignalNames.HIGH_G_ACCELEROMETER_Y); //find index
                    int iAccelZ = getSignalIndex(Shimmer3Configuration.SignalNames.HIGH_G_ACCELEROMETER_Z); //find index
                    double[] datatemp = new double[3] { newPacket[iAccelX], newPacket[iAccelY], newPacket[iAccelZ] };
                    datatemp = UtilCalibration.CalibrateInertialSensorData(datatemp, AlignmentMatrixAltAccel, SensitivityMatrixAltAccel, OffsetVectorAltAccel);
                    string units;
                    if (DefaultHighGAccelParams)
                    {
                        units = ShimmerConfiguration.SignalUnits.MeterPerSecondSquared_DefaultCal;
                    }
                    else
                    {
                        units = ShimmerConfiguration.SignalUnits.MeterPerSecondSquared;
                    }
                    objectCluster.Add(Shimmer3Configuration.SignalNames.HIGH_G_ACCELEROMETER_X, ShimmerConfiguration.SignalFormats.RAW, ShimmerConfiguration.SignalUnits.NoUnits, newPacket[iAccelX]);
                    objectCluster.Add(Shimmer3Configuration.SignalNames.HIGH_G_ACCELEROMETER_X, ShimmerConfiguration.SignalFormats.CAL, units, datatemp[0]);
                    objectCluster.Add(Shimmer3Configuration.SignalNames.HIGH_G_ACCELEROMETER_Y, ShimmerConfiguration.SignalFormats.RAW, ShimmerConfiguration.SignalUnits.NoUnits, newPacket[iAccelY]);
                    objectCluster.Add(Shimmer3Configuration.SignalNames.HIGH_G_ACCELEROMETER_Y, ShimmerConfiguration.SignalFormats.CAL, units, datatemp[1]);
                    objectCluster.Add(Shimmer3Configuration.SignalNames.HIGH_G_ACCELEROMETER_Z, ShimmerConfiguration.SignalFormats.RAW, ShimmerConfiguration.SignalUnits.NoUnits, newPacket[iAccelZ]);
                    objectCluster.Add(Shimmer3Configuration.SignalNames.HIGH_G_ACCELEROMETER_Z, ShimmerConfiguration.SignalFormats.CAL, units, datatemp[2]);

                    accelerometer[0] = datatemp[0];
                    accelerometer[1] = datatemp[1];
                    accelerometer[2] = datatemp[2];

                }
                if (((EnabledSensors & (int)SensorBitmapShimmer3.SENSOR_GYRO) > 0))
                {
                    int iGyroX = getSignalIndex(Shimmer3Configuration.SignalNames.GYROSCOPE_X);
                    int iGyroY = getSignalIndex(Shimmer3Configuration.SignalNames.GYROSCOPE_Y);
                    int iGyroZ = getSignalIndex(Shimmer3Configuration.SignalNames.GYROSCOPE_Z);
                    double[] datatemp = new double[3] { newPacket[iGyroX], newPacket[iGyroY], newPacket[iGyroZ] };
                    datatemp = UtilCalibration.CalibrateInertialSensorData(datatemp, AlignmentMatrixGyro, SensitivityMatrixGyro, OffsetVectorGyro);
                    string units;
                    if (DefaultGyroParams)
                    {
                        units = ShimmerConfiguration.SignalUnits.DegreePerSecond_DefaultCal;
                    }
                    else
                    {
                        units = ShimmerConfiguration.SignalUnits.DegreePerSecond;
                    }
                    objectCluster.Add(Shimmer3Configuration.SignalNames.GYROSCOPE_X, ShimmerConfiguration.SignalFormats.RAW, ShimmerConfiguration.SignalUnits.NoUnits, newPacket[iGyroX]);
                    objectCluster.Add(Shimmer3Configuration.SignalNames.GYROSCOPE_X, ShimmerConfiguration.SignalFormats.CAL, units, datatemp[0]);
                    objectCluster.Add(Shimmer3Configuration.SignalNames.GYROSCOPE_Y, ShimmerConfiguration.SignalFormats.RAW, ShimmerConfiguration.SignalUnits.NoUnits, newPacket[iGyroY]);
                    objectCluster.Add(Shimmer3Configuration.SignalNames.GYROSCOPE_Y, ShimmerConfiguration.SignalFormats.CAL, units, datatemp[1]);
                    objectCluster.Add(Shimmer3Configuration.SignalNames.GYROSCOPE_Z, ShimmerConfiguration.SignalFormats.RAW, ShimmerConfiguration.SignalUnits.NoUnits, newPacket[iGyroZ]);
                    objectCluster.Add(Shimmer3Configuration.SignalNames.GYROSCOPE_Z, ShimmerConfiguration.SignalFormats.CAL, units, datatemp[2]);

                    gyroscope[0] = datatemp[0] * Math.PI / 180;
                    gyroscope[1] = datatemp[1] * Math.PI / 180;
                    gyroscope[2] = datatemp[2] * Math.PI / 180;

                    if (EnableGyroOnTheFlyCalibration)
                    {
                        GyroXRawList.Add(newPacket[iGyroX]);
                        GyroYRawList.Add(newPacket[iGyroY]);
                        GyroZRawList.Add(newPacket[iGyroZ]);
                        if (GyroXRawList.Count > ListSizeGyroOnTheFly)
                        {
                            GyroXRawList.RemoveAt(0);
                            GyroYRawList.RemoveAt(0);
                            GyroZRawList.RemoveAt(0);
                        }
                        GyroXCalList.Add(datatemp[0]);
                        GyroYCalList.Add(datatemp[1]);
                        GyroZCalList.Add(datatemp[2]);
                        if (GyroXCalList.Count > ListSizeGyroOnTheFly)
                        {
                            GyroXCalList.RemoveAt(0);
                            GyroYCalList.RemoveAt(0);
                            GyroZCalList.RemoveAt(0);

                            if (GetStandardDeviation(GyroXCalList) < ThresholdGyroOnTheFly && GetStandardDeviation(GyroYCalList) < ThresholdGyroOnTheFly && GetStandardDeviation(GyroZCalList) < ThresholdGyroOnTheFly)
                            {
                                OffsetVectorGyro[0, 0] = GyroXRawList.Average();
                                OffsetVectorGyro[1, 0] = GyroYRawList.Average();
                                OffsetVectorGyro[2, 0] = GyroZRawList.Average();
                            }
                        }
                    }
                }
                if (((EnabledSensors & (int)SensorBitmapShimmer3.SENSOR_MAG) > 0))
                {
                    int iMagX = getSignalIndex(Shimmer3Configuration.SignalNames.MAGNETOMETER_X);
                    int iMagY = getSignalIndex(Shimmer3Configuration.SignalNames.MAGNETOMETER_Y);
                    int iMagZ = getSignalIndex(Shimmer3Configuration.SignalNames.MAGNETOMETER_Z);
                    double[] datatemp = new double[3] { newPacket[iMagX], newPacket[iMagY], newPacket[iMagZ] };
                    datatemp = UtilCalibration.CalibrateInertialSensorData(datatemp, AlignmentMatrixMag, SensitivityMatrixMag, OffsetVectorMag);
                    string units;
                    if (DefaultMagParams)
                    {
                        units = ShimmerConfiguration.SignalUnits.Local_DefaultCal;
                    }
                    else
                    {
                        units = ShimmerConfiguration.SignalUnits.Local;
                    }
                    objectCluster.Add(Shimmer3Configuration.SignalNames.MAGNETOMETER_X, ShimmerConfiguration.SignalFormats.RAW, ShimmerConfiguration.SignalUnits.NoUnits, newPacket[iMagX]);
                    objectCluster.Add(Shimmer3Configuration.SignalNames.MAGNETOMETER_X, ShimmerConfiguration.SignalFormats.CAL, units, datatemp[0]);
                    objectCluster.Add(Shimmer3Configuration.SignalNames.MAGNETOMETER_Y, ShimmerConfiguration.SignalFormats.RAW, ShimmerConfiguration.SignalUnits.NoUnits, newPacket[iMagY]);
                    objectCluster.Add(Shimmer3Configuration.SignalNames.MAGNETOMETER_Y, ShimmerConfiguration.SignalFormats.CAL, units, datatemp[1]);
                    objectCluster.Add(Shimmer3Configuration.SignalNames.MAGNETOMETER_Z, ShimmerConfiguration.SignalFormats.RAW, ShimmerConfiguration.SignalUnits.NoUnits, newPacket[iMagZ]);
                    objectCluster.Add(Shimmer3Configuration.SignalNames.MAGNETOMETER_Z, ShimmerConfiguration.SignalFormats.CAL, units, datatemp[2]);

                    magnetometer[0] = datatemp[0];
                    magnetometer[1] = datatemp[1];
                    magnetometer[2] = datatemp[2];
                }
                if (((EnabledSensors & (int)SensorBitmapShimmer3.SENSOR_MAG_ALT) > 0))
                {
                    int iMagX = getSignalIndex(Shimmer3Configuration.SignalNames.ALT_MAGNETOMETER_X);
                    int iMagY = getSignalIndex(Shimmer3Configuration.SignalNames.ALT_MAGNETOMETER_Y);
                    int iMagZ = getSignalIndex(Shimmer3Configuration.SignalNames.ALT_MAGNETOMETER_Z);
                    double[] datatemp = new double[3] { newPacket[iMagX], newPacket[iMagY], newPacket[iMagZ] };
                    datatemp = UtilCalibration.CalibrateInertialSensorData(datatemp, AlignmentMatrixMag2, SensitivityMatrixMag2, OffsetVectorMag2);
                    string units;
                    if (DefaultWRMagParams)
                    {
                        units = ShimmerConfiguration.SignalUnits.Local_DefaultCal;
                    }
                    else
                    {
                        units = ShimmerConfiguration.SignalUnits.Local;
                    }
                    objectCluster.Add(Shimmer3Configuration.SignalNames.ALT_MAGNETOMETER_X, ShimmerConfiguration.SignalFormats.RAW, ShimmerConfiguration.SignalUnits.NoUnits, newPacket[iMagX]);
                    objectCluster.Add(Shimmer3Configuration.SignalNames.ALT_MAGNETOMETER_X, ShimmerConfiguration.SignalFormats.CAL, units, datatemp[0]);
                    objectCluster.Add(Shimmer3Configuration.SignalNames.ALT_MAGNETOMETER_Y, ShimmerConfiguration.SignalFormats.RAW, ShimmerConfiguration.SignalUnits.NoUnits, newPacket[iMagY]);
                    objectCluster.Add(Shimmer3Configuration.SignalNames.ALT_MAGNETOMETER_Y, ShimmerConfiguration.SignalFormats.CAL, units, datatemp[1]);
                    objectCluster.Add(Shimmer3Configuration.SignalNames.ALT_MAGNETOMETER_Z, ShimmerConfiguration.SignalFormats.RAW, ShimmerConfiguration.SignalUnits.NoUnits, newPacket[iMagZ]);
                    objectCluster.Add(Shimmer3Configuration.SignalNames.ALT_MAGNETOMETER_Z, ShimmerConfiguration.SignalFormats.CAL, units, datatemp[2]);

                    magnetometer[0] = datatemp[0];
                    magnetometer[1] = datatemp[1];
                    magnetometer[2] = datatemp[2];
                }

                if (((EnabledSensors & (int)SensorBitmapShimmer3.SENSOR_VBATT) > 0))
                {
                    int index = getSignalIndex(Shimmer3Configuration.SignalNames.V_SENSE_BATT);
                    double datatemp = newPacket[index];
                    if (HardwareVersion == (int)ShimmerBluetooth.ShimmerVersion.SHIMMER3R)
                    {
                        //datatemp = CalibrateShimmer3RAdcChannel(datatemp) * 1.988;
                        datatemp = (CalibrateU12AdcValue(datatemp, 0, 3, 1) * 1.988);
                    }
                    else
                    {
                        datatemp = (CalibrateU12AdcValue(datatemp, 0, 3, 1) * 1.988);
                    }

                    if (datatemp < 3400 && datatemp > 3000)
                    {
                        //System.Threading.Thread.Sleep(500);
                        if (CurrentLEDStatus != 1)
                        {
                            WriteBytes(new byte[2] { (byte)ShimmerBluetooth.PacketTypeShimmer2.SET_BLINK_LED, (byte)1 }, 0, 2);
                            CurrentLEDStatus = 1;
                        }
                    }
                    else if (datatemp <= 3000)
                    {
                        //System.Threading.Thread.Sleep(500);
                        if (CurrentLEDStatus != 2)
                        {
                            WriteBytes(new byte[2] { (byte)ShimmerBluetooth.PacketTypeShimmer2.SET_BLINK_LED, (byte)2 }, 0, 2);
                            CurrentLEDStatus = 2;
                        }
                    }
                    else
                    {
                        if (CurrentLEDStatus != 0)
                        {
                            WriteBytes(new byte[2] { (byte)ShimmerBluetooth.PacketTypeShimmer2.SET_BLINK_LED, (byte)0 }, 0, 2);
                            CurrentLEDStatus = 0;
                        }
                    }
                    objectCluster.Add(Shimmer3Configuration.SignalNames.V_SENSE_BATT, ShimmerConfiguration.SignalFormats.RAW, ShimmerConfiguration.SignalUnits.NoUnits, newPacket[index]);
                    objectCluster.Add(Shimmer3Configuration.SignalNames.V_SENSE_BATT, ShimmerConfiguration.SignalFormats.CAL, ShimmerConfiguration.SignalUnits.MilliVolts, datatemp);
                }
                if (((EnabledSensors & (int)SensorBitmapShimmer3.SENSOR_EXT_A7) > 0))
                {

                    if (HardwareVersion == (int)ShimmerBluetooth.ShimmerVersion.SHIMMER3R)
                    {
                        int index = getSignalIndex(Shimmer3RConfiguration.SignalNames.GEN_EXT_ADC_A0);
                        double datatemp = newPacket[index];
                        datatemp = CalibrateShimmer3RAdcChannel(datatemp);
                        objectCluster.Add(Shimmer3RConfiguration.SignalNames.GEN_EXT_ADC_A0, ShimmerConfiguration.SignalFormats.RAW, ShimmerConfiguration.SignalUnits.NoUnits, newPacket[index]);
                        objectCluster.Add(Shimmer3RConfiguration.SignalNames.GEN_EXT_ADC_A0, ShimmerConfiguration.SignalFormats.CAL, ShimmerConfiguration.SignalUnits.MilliVolts, datatemp);

                    }
                    else
                    {
                        int index = getSignalIndex(Shimmer3Configuration.SignalNames.EXTERNAL_ADC_A7);
                        double datatemp = newPacket[index];
                        datatemp = (CalibrateU12AdcValue(datatemp, 0, 3, 1));
                        objectCluster.Add(Shimmer3Configuration.SignalNames.EXTERNAL_ADC_A7, ShimmerConfiguration.SignalFormats.RAW, ShimmerConfiguration.SignalUnits.NoUnits, newPacket[index]);
                        objectCluster.Add(Shimmer3Configuration.SignalNames.EXTERNAL_ADC_A7, ShimmerConfiguration.SignalFormats.CAL, ShimmerConfiguration.SignalUnits.MilliVolts, datatemp);

                    }
                }
                if (((EnabledSensors & (int)SensorBitmapShimmer3.SENSOR_EXT_A6) > 0))
                {

                    if (HardwareVersion == (int)ShimmerBluetooth.ShimmerVersion.SHIMMER3R)
                    {
                        int index = getSignalIndex(Shimmer3RConfiguration.SignalNames.GEN_EXT_ADC_A1);
                        double datatemp = newPacket[index];
                        datatemp = CalibrateShimmer3RAdcChannel(datatemp);
                        objectCluster.Add(Shimmer3RConfiguration.SignalNames.GEN_EXT_ADC_A1, ShimmerConfiguration.SignalFormats.RAW, ShimmerConfiguration.SignalUnits.NoUnits, newPacket[index]);
                        objectCluster.Add(Shimmer3RConfiguration.SignalNames.GEN_EXT_ADC_A1, ShimmerConfiguration.SignalFormats.CAL, ShimmerConfiguration.SignalUnits.MilliVolts, datatemp);

                    }
                    else
                    {
                        int index = getSignalIndex(Shimmer3Configuration.SignalNames.EXTERNAL_ADC_A6);
                        double datatemp = newPacket[index];
                        datatemp = (CalibrateU12AdcValue(datatemp, 0, 3, 1));
                        objectCluster.Add(Shimmer3Configuration.SignalNames.EXTERNAL_ADC_A6, ShimmerConfiguration.SignalFormats.RAW, ShimmerConfiguration.SignalUnits.NoUnits, newPacket[index]);
                        objectCluster.Add(Shimmer3Configuration.SignalNames.EXTERNAL_ADC_A6, ShimmerConfiguration.SignalFormats.CAL, ShimmerConfiguration.SignalUnits.MilliVolts, datatemp);

                    }
                }
                if (((EnabledSensors & (int)SensorBitmapShimmer3.SENSOR_EXT_A15) > 0))
                {
                    if (HardwareVersion == (int)ShimmerBluetooth.ShimmerVersion.SHIMMER3R)
                    {
                        int index = getSignalIndex(Shimmer3RConfiguration.SignalNames.GEN_EXT_ADC_A2);
                        double datatemp = newPacket[index];
                        datatemp = CalibrateShimmer3RAdcChannel(datatemp);
                        objectCluster.Add(Shimmer3RConfiguration.SignalNames.GEN_EXT_ADC_A2, ShimmerConfiguration.SignalFormats.RAW, ShimmerConfiguration.SignalUnits.NoUnits, newPacket[index]);
                        objectCluster.Add(Shimmer3RConfiguration.SignalNames.GEN_EXT_ADC_A2, ShimmerConfiguration.SignalFormats.CAL, ShimmerConfiguration.SignalUnits.MilliVolts, datatemp);

                    }
                    else
                    {
                        int index = getSignalIndex(Shimmer3Configuration.SignalNames.EXTERNAL_ADC_A15);
                        double datatemp = newPacket[index];
                        datatemp = (CalibrateU12AdcValue(datatemp, 0, 3, 1));
                        objectCluster.Add(Shimmer3Configuration.SignalNames.EXTERNAL_ADC_A15, ShimmerConfiguration.SignalFormats.RAW, ShimmerConfiguration.SignalUnits.NoUnits, newPacket[index]);
                        objectCluster.Add(Shimmer3Configuration.SignalNames.EXTERNAL_ADC_A15, ShimmerConfiguration.SignalFormats.CAL, ShimmerConfiguration.SignalUnits.MilliVolts, datatemp);

                    }
                }
                if (((EnabledSensors & (int)SensorBitmapShimmer3.SENSOR_INT_A1) > 0))
                {

                    if (HardwareVersion == (int)ShimmerBluetooth.ShimmerVersion.SHIMMER3R)
                    {
                        int index = getSignalIndex(Shimmer3RConfiguration.SignalNames.GEN_INT_ADC_A3);
                        double datatemp = newPacket[index];
                        datatemp = CalibrateShimmer3RAdcChannel(datatemp);
                        objectCluster.Add(Shimmer3RConfiguration.SignalNames.GEN_INT_ADC_A3, ShimmerConfiguration.SignalFormats.RAW, ShimmerConfiguration.SignalUnits.NoUnits, newPacket[index]);
                        objectCluster.Add(Shimmer3RConfiguration.SignalNames.GEN_INT_ADC_A3, ShimmerConfiguration.SignalFormats.CAL, ShimmerConfiguration.SignalUnits.MilliVolts, datatemp);

                    }
                    else
                    {
                        int index = getSignalIndex(Shimmer3Configuration.SignalNames.INTERNAL_ADC_A1);
                        double datatemp = newPacket[index];
                        datatemp = (CalibrateU12AdcValue(datatemp, 0, 3, 1));
                        objectCluster.Add(Shimmer3Configuration.SignalNames.INTERNAL_ADC_A1, ShimmerConfiguration.SignalFormats.RAW, ShimmerConfiguration.SignalUnits.NoUnits, newPacket[index]);
                        objectCluster.Add(Shimmer3Configuration.SignalNames.INTERNAL_ADC_A1, ShimmerConfiguration.SignalFormats.CAL, ShimmerConfiguration.SignalUnits.MilliVolts, datatemp);

                    }
                }
                if (((EnabledSensors & (int)SensorBitmapShimmer3.SENSOR_INT_A12) > 0))
                {
                    if (HardwareVersion == (int)ShimmerBluetooth.ShimmerVersion.SHIMMER3R)
                    {
                        int index = getSignalIndex(Shimmer3RConfiguration.SignalNames.GEN_INT_ADC_A0);
                        double datatemp = newPacket[index];
                        datatemp = CalibrateShimmer3RAdcChannel(datatemp);
                        objectCluster.Add(Shimmer3RConfiguration.SignalNames.GEN_INT_ADC_A0, ShimmerConfiguration.SignalFormats.RAW, ShimmerConfiguration.SignalUnits.NoUnits, newPacket[index]);
                        objectCluster.Add(Shimmer3RConfiguration.SignalNames.GEN_INT_ADC_A0, ShimmerConfiguration.SignalFormats.CAL, ShimmerConfiguration.SignalUnits.MilliVolts, datatemp);

                    }
                    else
                    {
                        int index = getSignalIndex(Shimmer3Configuration.SignalNames.INTERNAL_ADC_A12);
                        double datatemp = newPacket[index];
                        datatemp = (CalibrateU12AdcValue(datatemp, 0, 3, 1));
                        objectCluster.Add(Shimmer3Configuration.SignalNames.INTERNAL_ADC_A12, ShimmerConfiguration.SignalFormats.RAW, ShimmerConfiguration.SignalUnits.NoUnits, newPacket[index]);
                        objectCluster.Add(Shimmer3Configuration.SignalNames.INTERNAL_ADC_A12, ShimmerConfiguration.SignalFormats.CAL, ShimmerConfiguration.SignalUnits.MilliVolts, datatemp);

                    }
                }
                if (((EnabledSensors & (int)SensorBitmapShimmer3.SENSOR_INT_A13) > 0))
                {
                    if (HardwareVersion == (int)ShimmerBluetooth.ShimmerVersion.SHIMMER3R)
                    {
                        int index = getSignalIndex(Shimmer3RConfiguration.SignalNames.GEN_INT_ADC_A1);
                        double datatemp = newPacket[index];
                        datatemp = CalibrateShimmer3RAdcChannel(datatemp);
                        objectCluster.Add(Shimmer3RConfiguration.SignalNames.GEN_INT_ADC_A1, ShimmerConfiguration.SignalFormats.RAW, ShimmerConfiguration.SignalUnits.NoUnits, newPacket[index]);
                        objectCluster.Add(Shimmer3RConfiguration.SignalNames.GEN_INT_ADC_A1, ShimmerConfiguration.SignalFormats.CAL, ShimmerConfiguration.SignalUnits.MilliVolts, datatemp);

                    }
                    else
                    {
                        int index = getSignalIndex(Shimmer3Configuration.SignalNames.INTERNAL_ADC_A13);
                        double datatemp = newPacket[index];
                        datatemp = (CalibrateU12AdcValue(datatemp, 0, 3, 1));
                        objectCluster.Add(Shimmer3Configuration.SignalNames.INTERNAL_ADC_A13, ShimmerConfiguration.SignalFormats.RAW, ShimmerConfiguration.SignalUnits.NoUnits, newPacket[index]);
                        objectCluster.Add(Shimmer3Configuration.SignalNames.INTERNAL_ADC_A13, ShimmerConfiguration.SignalFormats.CAL, ShimmerConfiguration.SignalUnits.MilliVolts, datatemp);

                    }
                }
                if (((EnabledSensors & (int)SensorBitmapShimmer3.SENSOR_INT_A14) > 0))
                {

                    if (HardwareVersion == (int)ShimmerBluetooth.ShimmerVersion.SHIMMER3R)
                    {
                        int index = getSignalIndex(Shimmer3RConfiguration.SignalNames.GEN_INT_ADC_A2);
                        double datatemp = newPacket[index];
                        datatemp = CalibrateShimmer3RAdcChannel(datatemp);
                        objectCluster.Add(Shimmer3RConfiguration.SignalNames.GEN_INT_ADC_A2, ShimmerConfiguration.SignalFormats.RAW, ShimmerConfiguration.SignalUnits.NoUnits, newPacket[index]);
                        objectCluster.Add(Shimmer3RConfiguration.SignalNames.GEN_INT_ADC_A2, ShimmerConfiguration.SignalFormats.CAL, ShimmerConfiguration.SignalUnits.MilliVolts, datatemp);

                    }
                    else
                    {
                        int index = getSignalIndex(Shimmer3Configuration.SignalNames.INTERNAL_ADC_A14);
                        double datatemp = newPacket[index];
                        datatemp = (CalibrateU12AdcValue(datatemp, 0, 3, 1));
                        objectCluster.Add(Shimmer3Configuration.SignalNames.INTERNAL_ADC_A14, ShimmerConfiguration.SignalFormats.RAW, ShimmerConfiguration.SignalUnits.NoUnits, newPacket[index]);
                        objectCluster.Add(Shimmer3Configuration.SignalNames.INTERNAL_ADC_A14, ShimmerConfiguration.SignalFormats.CAL, ShimmerConfiguration.SignalUnits.MilliVolts, datatemp);

                    }
                }

                if (((EnabledSensors & (int)SensorBitmapShimmer3.SENSOR_BMP180_PRESSURE) > 0))
                {
                    double[] bmpX80caldata = new double[2];
                    int iUP = getSignalIndex(Shimmer3Configuration.SignalNames.PRESSURE);
                    int iUT = getSignalIndex(Shimmer3Configuration.SignalNames.TEMPERATURE);
                    double UP;
                    double UT;

                    if (isShimmer3withUpdatedSensors())
                    {
                        if (HardwareVersion == (int)ShimmerBluetooth.ShimmerVersion.SHIMMER3R)
                        {
                            //CA-124
                            UT = (double)newPacket[iUT];
                            UP = (double)newPacket[iUP];
                            double[] datatemp = new double[2] { newPacket[iUP], newPacket[iUT] };
                            bmpX80caldata = CalibratePressure390SensorData(UP, UT);
                        }
                        else
                        {
                            UT = (double)newPacket[iUT];
                            UP = (double)newPacket[iUP];
                            UT = UT * Math.Pow(2, 4);
                            UP = UP / Math.Pow(2, 4);
                            double[] datatemp = new double[2] { newPacket[iUP], newPacket[iUT] };
                            bmpX80caldata = CalibratePressure280SensorData(UP, UT);
                        }
                    }
                    else
                    {
                        UT = (double)newPacket[iUT];
                        UP = (double)newPacket[iUP];
                        UP = UP / Math.Pow(2, 8 - PressureResolution);
                        double[] datatemp = new double[2] { newPacket[iUP], newPacket[iUT] };
                        bmpX80caldata = CalibratePressure180SensorData(UP, datatemp[1]);
                    }

                    objectCluster.Add(Shimmer3Configuration.SignalNames.PRESSURE, ShimmerConfiguration.SignalFormats.RAW, ShimmerConfiguration.SignalUnits.NoUnits, newPacket[iUP]);
                    objectCluster.Add(Shimmer3Configuration.SignalNames.PRESSURE, ShimmerConfiguration.SignalFormats.CAL, ShimmerConfiguration.SignalUnits.KiloPascal, bmpX80caldata[0] / 1000);
                    objectCluster.Add(Shimmer3Configuration.SignalNames.TEMPERATURE, ShimmerConfiguration.SignalFormats.RAW, ShimmerConfiguration.SignalUnits.NoUnits, newPacket[iUT]);
                    objectCluster.Add(Shimmer3Configuration.SignalNames.TEMPERATURE, ShimmerConfiguration.SignalFormats.CAL, ShimmerConfiguration.SignalUnits.Celcius, bmpX80caldata[1]);
                }
                if (((EnabledSensors & (int)SensorBitmapShimmer3.SENSOR_GSR) > 0))
                {
                    int iGSR = getSignalIndex(Shimmer3Configuration.SignalNames.GSR);
                    int newGSRRange = -1; // initialized to -1 so it will only come into play if mGSRRange = 4  
                    double datatemp = newPacket[iGSR];
                    double gsrResistanceKOhms = -1;

                    int bitMask = 4095;
                    if (HardwareVersion == (int)ShimmerBluetooth.ShimmerVersion.SHIMMER3R)
                    {
                        bitMask = 16383;
                    }

                    //double p1 = 0, p2 = 0;
                    if (GSRRange == 4)
                    {
                        newGSRRange = (49152 & (int)datatemp) >> 14;
                    }
                    datatemp = (double)((int)datatemp & bitMask);
                    if (GSRRange == 0 || newGSRRange == 0)
                    {
                        //Note that from FW 1.0 onwards the MSB of the GSR data contains the range
                        // the polynomial function used for calibration has been deprecated, it is replaced with a linear function
                        //p1 = 0.0363;
                        //p2 = -24.8617;

                        //Changed to new GSR algorithm using non inverting amp
                        //p1 = 0.0373;
                        //p2 = -24.9915;
                        gsrResistanceKOhms = CalibrateGsrDataToResistanceFromAmplifierEq(datatemp, 0);
                    }
                    else if (GSRRange == 1 || newGSRRange == 1)
                    {
                        //p1 = 0.0051;
                        //p2 = -3.8357;
                        //Changed to new GSR algorithm using non inverting amp
                        //p1 = 0.0054;
                        //p2 = -3.5194;
                        gsrResistanceKOhms = CalibrateGsrDataToResistanceFromAmplifierEq(datatemp, 1);
                    }
                    else if (GSRRange == 2 || newGSRRange == 2)
                    {
                        //p1 = 0.0015;
                        //p2 = -1.0067;
                        //Changed to new GSR algorithm using non inverting amp
                        //p1 = 0.0015;
                        //p2 = -1.0163;
                        gsrResistanceKOhms = CalibrateGsrDataToResistanceFromAmplifierEq(datatemp, 2);
                    }
                    else if (GSRRange == 3 || newGSRRange == 3)
                    {
                        //p1 = 4.4513e-04;
                        //p2 = -0.3193;
                        //Changed to new GSR algorithm using non inverting amp
                        //p1 = 4.5580e-04;
                        //p2 = -0.3014;
                        if (datatemp < GSR_UNCAL_LIMIT_RANGE3)
                        {
                            datatemp = GSR_UNCAL_LIMIT_RANGE3;
                        }
                        gsrResistanceKOhms = CalibrateGsrDataToResistanceFromAmplifierEq(datatemp, 3);
                    }
                    //Changed to new GSR algorithm using non inverting amp
                    //datatemp = CalibrateGsrData(datatemp, p1, p2);
                    gsrResistanceKOhms = NudgeGsrResistance(gsrResistanceKOhms, GSRRange);
                    double gsrConductanceUSiemens = (1.0 / gsrResistanceKOhms) * 1000;
                    objectCluster.Add(Shimmer3Configuration.SignalNames.GSR, ShimmerConfiguration.SignalFormats.RAW, ShimmerConfiguration.SignalUnits.NoUnits, newPacket[iGSR]);
                    objectCluster.Add(Shimmer3Configuration.SignalNames.GSR, ShimmerConfiguration.SignalFormats.CAL, ShimmerConfiguration.SignalUnits.KiloOhms, gsrResistanceKOhms);
                    objectCluster.Add(Shimmer3Configuration.SignalNames.GSR_CONDUCTANCE, ShimmerConfiguration.SignalFormats.CAL, ShimmerConfiguration.SignalUnits.MicroSiemens, gsrConductanceUSiemens);
                }
                if ((EnabledSensors & (int)SensorBitmapShimmer3.SENSOR_EXG1_24BIT) > 0)
                {
                    int iStatus = getSignalIndex(Shimmer3Configuration.SignalNames.EXG1_STATUS);
                    int iCh1 = getSignalIndex(Shimmer3Configuration.SignalNames.EXG1_CH1);
                    int iCh2 = getSignalIndex(Shimmer3Configuration.SignalNames.EXG1_CH2);
                    double[] datatemp = new double[3] { newPacket[iStatus], newPacket[iCh1], newPacket[iCh2] };
                    int gain = ConvertEXGGainSettingToValue((Exg1RegArray[3] >> 4) & 7);
                    datatemp[1] = datatemp[1] * (((2.42 * 1000) / gain) / (Math.Pow(2, 23) - 1));
                    gain = ConvertEXGGainSettingToValue((Exg1RegArray[4] >> 4) & 7);
                    datatemp[2] = datatemp[2] * (((2.42 * 1000) / gain) / (Math.Pow(2, 23) - 1));
                    objectCluster.Add(Shimmer3Configuration.SignalNames.EXG1_STATUS, ShimmerConfiguration.SignalFormats.RAW, ShimmerConfiguration.SignalUnits.NoUnits, newPacket[iStatus]);
                    if (IsDefaultRespirationConfigurationEnabled() || IsDefaultECGConfigurationEnabled())
                    {
                        objectCluster.Add(Shimmer3Configuration.SignalNames.ECG_LL_RA, ShimmerConfiguration.SignalFormats.RAW, ShimmerConfiguration.SignalUnits.NoUnits, newPacket[iCh1]);
                        objectCluster.Add(Shimmer3Configuration.SignalNames.ECG_LL_RA, ShimmerConfiguration.SignalFormats.CAL, ShimmerConfiguration.SignalUnits.MilliVolts, datatemp[1]);
                        objectCluster.Add(Shimmer3Configuration.SignalNames.ECG_LA_RA, ShimmerConfiguration.SignalFormats.RAW, ShimmerConfiguration.SignalUnits.NoUnits, newPacket[iCh2]);
                        objectCluster.Add(Shimmer3Configuration.SignalNames.ECG_LA_RA, ShimmerConfiguration.SignalFormats.CAL, ShimmerConfiguration.SignalUnits.MilliVolts, datatemp[2]);
                    }
                    else if (IsDefaultEMGConfigurationEnabled())
                    {
                        objectCluster.Add(Shimmer3Configuration.SignalNames.EMG_CH1, ShimmerConfiguration.SignalFormats.RAW, ShimmerConfiguration.SignalUnits.NoUnits, newPacket[iCh1]);
                        objectCluster.Add(Shimmer3Configuration.SignalNames.EMG_CH1, ShimmerConfiguration.SignalFormats.CAL, ShimmerConfiguration.SignalUnits.MilliVolts, datatemp[1]);
                        objectCluster.Add(Shimmer3Configuration.SignalNames.EMG_CH2, ShimmerConfiguration.SignalFormats.RAW, ShimmerConfiguration.SignalUnits.NoUnits, newPacket[iCh2]);
                        objectCluster.Add(Shimmer3Configuration.SignalNames.EMG_CH2, ShimmerConfiguration.SignalFormats.CAL, ShimmerConfiguration.SignalUnits.MilliVolts, datatemp[2]);
                    }
                    else
                    {
                        objectCluster.Add(Shimmer3Configuration.SignalNames.EXG1_CH1, ShimmerConfiguration.SignalFormats.RAW, ShimmerConfiguration.SignalUnits.NoUnits, newPacket[iCh1]);
                        objectCluster.Add(Shimmer3Configuration.SignalNames.EXG1_CH1, ShimmerConfiguration.SignalFormats.CAL, ShimmerConfiguration.SignalUnits.MilliVolts, datatemp[1]);
                        objectCluster.Add(Shimmer3Configuration.SignalNames.EXG1_CH2, ShimmerConfiguration.SignalFormats.RAW, ShimmerConfiguration.SignalUnits.NoUnits, newPacket[iCh2]);
                        objectCluster.Add(Shimmer3Configuration.SignalNames.EXG1_CH2, ShimmerConfiguration.SignalFormats.CAL, ShimmerConfiguration.SignalUnits.MilliVolts, datatemp[2]);
                    }
                }
                if ((EnabledSensors & (int)SensorBitmapShimmer3.SENSOR_EXG2_24BIT) > 0)
                {
                    int iStatus = getSignalIndex(Shimmer3Configuration.SignalNames.EXG2_STATUS);
                    int iCh1 = getSignalIndex(Shimmer3Configuration.SignalNames.EXG2_CH1);
                    int iCh2 = getSignalIndex(Shimmer3Configuration.SignalNames.EXG2_CH2);
                    double[] datatemp = new double[3] { newPacket[iStatus], newPacket[iCh1], newPacket[iCh2] };
                    int gain = ConvertEXGGainSettingToValue((Exg2RegArray[3] >> 4) & 7);
                    datatemp[1] = datatemp[1] * (((2.42 * 1000) / gain) / (Math.Pow(2, 23) - 1));
                    gain = ConvertEXGGainSettingToValue((Exg2RegArray[4] >> 4) & 7);
                    datatemp[2] = datatemp[2] * (((2.42 * 1000) / gain) / (Math.Pow(2, 23) - 1));
                    objectCluster.Add(Shimmer3Configuration.SignalNames.EXG2_STATUS, ShimmerConfiguration.SignalFormats.RAW, ShimmerConfiguration.SignalUnits.NoUnits, newPacket[iStatus]);
                    if (IsDefaultRespirationConfigurationEnabled())
                    {
                        objectCluster.Add(Shimmer3Configuration.SignalNames.ECG_RESP, ShimmerConfiguration.SignalFormats.RAW, ShimmerConfiguration.SignalUnits.NoUnits, newPacket[iCh1]);
                        objectCluster.Add(Shimmer3Configuration.SignalNames.ECG_RESP, ShimmerConfiguration.SignalFormats.CAL, ShimmerConfiguration.SignalUnits.MilliVolts, datatemp[1]);
                        objectCluster.Add(Shimmer3Configuration.SignalNames.ECG_VX_RL, ShimmerConfiguration.SignalFormats.RAW, ShimmerConfiguration.SignalUnits.NoUnits, newPacket[iCh2]);
                        objectCluster.Add(Shimmer3Configuration.SignalNames.ECG_VX_RL, ShimmerConfiguration.SignalFormats.CAL, ShimmerConfiguration.SignalUnits.MilliVolts, datatemp[2]);
                    }
                    else if (IsDefaultECGConfigurationEnabled())
                    {
                        objectCluster.Add(Shimmer3Configuration.SignalNames.EXG2_CH1, ShimmerConfiguration.SignalFormats.RAW, ShimmerConfiguration.SignalUnits.NoUnits, newPacket[iCh1]);
                        objectCluster.Add(Shimmer3Configuration.SignalNames.EXG2_CH1, ShimmerConfiguration.SignalFormats.CAL, ShimmerConfiguration.SignalUnits.MilliVolts, datatemp[1]);
                        objectCluster.Add(Shimmer3Configuration.SignalNames.ECG_VX_RL, ShimmerConfiguration.SignalFormats.RAW, ShimmerConfiguration.SignalUnits.NoUnits, newPacket[iCh2]);
                        objectCluster.Add(Shimmer3Configuration.SignalNames.ECG_VX_RL, ShimmerConfiguration.SignalFormats.CAL, ShimmerConfiguration.SignalUnits.MilliVolts, datatemp[2]);
                    }
                    else if (IsDefaultEMGConfigurationEnabled())
                    {
                        objectCluster.Add(Shimmer3Configuration.SignalNames.EXG2_CH1, ShimmerConfiguration.SignalFormats.RAW, ShimmerConfiguration.SignalUnits.NoUnits, newPacket[iCh1]);
                        objectCluster.Add(Shimmer3Configuration.SignalNames.EXG2_CH1, ShimmerConfiguration.SignalFormats.CAL, ShimmerConfiguration.SignalUnits.MilliVolts, datatemp[1]);
                        objectCluster.Add(Shimmer3Configuration.SignalNames.EXG2_CH2, ShimmerConfiguration.SignalFormats.RAW, ShimmerConfiguration.SignalUnits.NoUnits, newPacket[iCh2]);
                        objectCluster.Add(Shimmer3Configuration.SignalNames.EXG2_CH2, ShimmerConfiguration.SignalFormats.CAL, ShimmerConfiguration.SignalUnits.MilliVolts, datatemp[2]);
                    }
                    else
                    {
                        objectCluster.Add(Shimmer3Configuration.SignalNames.EXG2_CH1, ShimmerConfiguration.SignalFormats.RAW, ShimmerConfiguration.SignalUnits.NoUnits, newPacket[iCh1]);
                        objectCluster.Add(Shimmer3Configuration.SignalNames.EXG2_CH1, ShimmerConfiguration.SignalFormats.CAL, ShimmerConfiguration.SignalUnits.MilliVolts, datatemp[1]);
                        objectCluster.Add(Shimmer3Configuration.SignalNames.EXG2_CH2, ShimmerConfiguration.SignalFormats.RAW, ShimmerConfiguration.SignalUnits.NoUnits, newPacket[iCh2]);
                        objectCluster.Add(Shimmer3Configuration.SignalNames.EXG2_CH2, ShimmerConfiguration.SignalFormats.CAL, ShimmerConfiguration.SignalUnits.MilliVolts, datatemp[2]);
                    }
                }
                if ((EnabledSensors & (int)SensorBitmapShimmer3.SENSOR_EXG1_16BIT) > 0)
                {
                    int iStatus = getSignalIndex(Shimmer3Configuration.SignalNames.EXG1_STATUS);
                    int iCh1 = getSignalIndex(Shimmer3Configuration.SignalNames.EXG1_CH1_16BIT);
                    int iCh2 = getSignalIndex(Shimmer3Configuration.SignalNames.EXG1_CH2_16BIT);
                    double[] datatemp = new double[3] { newPacket[iStatus], newPacket[iCh1], newPacket[iCh2] };
                    int gain = ConvertEXGGainSettingToValue((Exg1RegArray[3] >> 4) & 7);
                    datatemp[1] = datatemp[1] * (((2.42 * 1000) / (gain * 2)) / (Math.Pow(2, 15) - 1));
                    gain = ConvertEXGGainSettingToValue((Exg1RegArray[4] >> 4) & 7);
                    datatemp[2] = datatemp[2] * (((2.42 * 1000) / (gain * 2)) / (Math.Pow(2, 15) - 1));
                    objectCluster.Add(Shimmer3Configuration.SignalNames.EXG1_STATUS, ShimmerConfiguration.SignalFormats.RAW, ShimmerConfiguration.SignalUnits.NoUnits, newPacket[iStatus]);
                    if (IsDefaultRespirationConfigurationEnabled() || IsDefaultECGConfigurationEnabled())
                    {
                        objectCluster.Add(Shimmer3Configuration.SignalNames.ECG_LL_RA, ShimmerConfiguration.SignalFormats.RAW, ShimmerConfiguration.SignalUnits.NoUnits, newPacket[iCh1]);
                        objectCluster.Add(Shimmer3Configuration.SignalNames.ECG_LL_RA, ShimmerConfiguration.SignalFormats.CAL, ShimmerConfiguration.SignalUnits.MilliVolts, datatemp[1]);
                        objectCluster.Add(Shimmer3Configuration.SignalNames.ECG_LA_RA, ShimmerConfiguration.SignalFormats.RAW, ShimmerConfiguration.SignalUnits.NoUnits, newPacket[iCh2]);
                        objectCluster.Add(Shimmer3Configuration.SignalNames.ECG_LA_RA, ShimmerConfiguration.SignalFormats.CAL, ShimmerConfiguration.SignalUnits.MilliVolts, datatemp[2]);
                    }
                    else if (IsDefaultEMGConfigurationEnabled())
                    {
                        objectCluster.Add(Shimmer3Configuration.SignalNames.EMG_CH1, ShimmerConfiguration.SignalFormats.RAW, ShimmerConfiguration.SignalUnits.NoUnits, newPacket[iCh1]);
                        objectCluster.Add(Shimmer3Configuration.SignalNames.EMG_CH1, ShimmerConfiguration.SignalFormats.CAL, ShimmerConfiguration.SignalUnits.MilliVolts, datatemp[1]);
                        objectCluster.Add(Shimmer3Configuration.SignalNames.EMG_CH2, ShimmerConfiguration.SignalFormats.RAW, ShimmerConfiguration.SignalUnits.NoUnits, newPacket[iCh2]);
                        objectCluster.Add(Shimmer3Configuration.SignalNames.EMG_CH2, ShimmerConfiguration.SignalFormats.CAL, ShimmerConfiguration.SignalUnits.MilliVolts, datatemp[2]);
                    }
                    else
                    {
                        objectCluster.Add(Shimmer3Configuration.SignalNames.EXG1_CH1_16BIT, ShimmerConfiguration.SignalFormats.RAW, ShimmerConfiguration.SignalUnits.NoUnits, newPacket[iCh1]);
                        objectCluster.Add(Shimmer3Configuration.SignalNames.EXG1_CH1_16BIT, ShimmerConfiguration.SignalFormats.CAL, ShimmerConfiguration.SignalUnits.MilliVolts, datatemp[1]);
                        objectCluster.Add(Shimmer3Configuration.SignalNames.EXG1_CH2_16BIT, ShimmerConfiguration.SignalFormats.RAW, ShimmerConfiguration.SignalUnits.NoUnits, newPacket[iCh2]);
                        objectCluster.Add(Shimmer3Configuration.SignalNames.EXG1_CH2_16BIT, ShimmerConfiguration.SignalFormats.CAL, ShimmerConfiguration.SignalUnits.MilliVolts, datatemp[2]);
                    }
                }
                if ((EnabledSensors & (int)SensorBitmapShimmer3.SENSOR_EXG2_16BIT) > 0)
                {
                    int iStatus = getSignalIndex(Shimmer3Configuration.SignalNames.EXG2_STATUS);
                    int iCh1 = getSignalIndex(Shimmer3Configuration.SignalNames.EXG2_CH1_16BIT);
                    int iCh2 = getSignalIndex(Shimmer3Configuration.SignalNames.EXG2_CH2_16BIT);
                    double[] datatemp = new double[3] { newPacket[iStatus], newPacket[iCh1], newPacket[iCh2] };
                    int gain = ConvertEXGGainSettingToValue((Exg2RegArray[3] >> 4) & 7);
                    datatemp[1] = datatemp[1] * (((2.42 * 1000) / (gain * 2)) / (Math.Pow(2, 15) - 1));
                    gain = ConvertEXGGainSettingToValue((Exg2RegArray[4] >> 4) & 7);
                    datatemp[2] = datatemp[2] * (((2.42 * 1000) / (gain * 2)) / (Math.Pow(2, 15) - 1));
                    objectCluster.Add(Shimmer3Configuration.SignalNames.EXG2_STATUS, ShimmerConfiguration.SignalFormats.RAW, ShimmerConfiguration.SignalUnits.NoUnits, newPacket[iStatus]);

                    if (IsDefaultRespirationConfigurationEnabled())
                    {
                        objectCluster.Add(Shimmer3Configuration.SignalNames.ECG_RESP, ShimmerConfiguration.SignalFormats.RAW, ShimmerConfiguration.SignalUnits.NoUnits, newPacket[iCh1]);
                        objectCluster.Add(Shimmer3Configuration.SignalNames.ECG_RESP, ShimmerConfiguration.SignalFormats.CAL, ShimmerConfiguration.SignalUnits.MilliVolts, datatemp[1]);
                        objectCluster.Add(Shimmer3Configuration.SignalNames.ECG_VX_RL, ShimmerConfiguration.SignalFormats.RAW, ShimmerConfiguration.SignalUnits.NoUnits, newPacket[iCh2]);
                        objectCluster.Add(Shimmer3Configuration.SignalNames.ECG_VX_RL, ShimmerConfiguration.SignalFormats.CAL, ShimmerConfiguration.SignalUnits.MilliVolts, datatemp[2]);
                    }
                    else if (IsDefaultECGConfigurationEnabled())
                    {
                        objectCluster.Add(Shimmer3Configuration.SignalNames.EXG2_CH1, ShimmerConfiguration.SignalFormats.RAW, ShimmerConfiguration.SignalUnits.NoUnits, newPacket[iCh1]);
                        objectCluster.Add(Shimmer3Configuration.SignalNames.EXG2_CH1, ShimmerConfiguration.SignalFormats.CAL, ShimmerConfiguration.SignalUnits.MilliVolts, datatemp[1]);
                        objectCluster.Add(Shimmer3Configuration.SignalNames.ECG_VX_RL, ShimmerConfiguration.SignalFormats.RAW, ShimmerConfiguration.SignalUnits.NoUnits, newPacket[iCh2]);
                        objectCluster.Add(Shimmer3Configuration.SignalNames.ECG_VX_RL, ShimmerConfiguration.SignalFormats.CAL, ShimmerConfiguration.SignalUnits.MilliVolts, datatemp[2]);
                    }
                    else if (IsDefaultEMGConfigurationEnabled())
                    {
                        objectCluster.Add(Shimmer3Configuration.SignalNames.EXG2_CH1, ShimmerConfiguration.SignalFormats.RAW, ShimmerConfiguration.SignalUnits.NoUnits, newPacket[iCh1]);
                        objectCluster.Add(Shimmer3Configuration.SignalNames.EXG2_CH1, ShimmerConfiguration.SignalFormats.CAL, ShimmerConfiguration.SignalUnits.MilliVolts, datatemp[1]);
                        objectCluster.Add(Shimmer3Configuration.SignalNames.EXG2_CH2, ShimmerConfiguration.SignalFormats.RAW, ShimmerConfiguration.SignalUnits.NoUnits, newPacket[iCh2]);
                        objectCluster.Add(Shimmer3Configuration.SignalNames.EXG2_CH2, ShimmerConfiguration.SignalFormats.CAL, ShimmerConfiguration.SignalUnits.MilliVolts, datatemp[2]);
                    }
                    else
                    {
                        objectCluster.Add(Shimmer3Configuration.SignalNames.EXG2_CH1_16BIT, ShimmerConfiguration.SignalFormats.RAW, ShimmerConfiguration.SignalUnits.NoUnits, newPacket[iCh1]);
                        objectCluster.Add(Shimmer3Configuration.SignalNames.EXG2_CH1_16BIT, ShimmerConfiguration.SignalFormats.CAL, ShimmerConfiguration.SignalUnits.MilliVolts, datatemp[1]);
                        objectCluster.Add(Shimmer3Configuration.SignalNames.EXG2_CH2_16BIT, ShimmerConfiguration.SignalFormats.RAW, ShimmerConfiguration.SignalUnits.NoUnits, newPacket[iCh2]);
                        objectCluster.Add(Shimmer3Configuration.SignalNames.EXG2_CH2_16BIT, ShimmerConfiguration.SignalFormats.CAL, ShimmerConfiguration.SignalUnits.MilliVolts, datatemp[2]);
                    }
                }
                if ((EnabledSensors & (int)SensorBitmapShimmer3.SENSOR_BRIDGE_AMP) > 0)
                {
                    int iSGHigh = getSignalIndex(Shimmer3Configuration.SignalNames.BRIGE_AMPLIFIER_HIGH);
                    int iSGLow = getSignalIndex(Shimmer3Configuration.SignalNames.BRIGE_AMPLIFIER_LOW);
                    double[] datatemp = new double[2] { newPacket[iSGHigh], newPacket[iSGLow] };
                    if (HardwareVersion == (int)ShimmerBluetooth.ShimmerVersion.SHIMMER3R)
                    {
                        datatemp[0] = CalibrateShimmer3RAdcChannel(datatemp[0]);
                        datatemp[1] = CalibrateShimmer3RAdcChannel(datatemp[1]);
                    }
                    else
                    {
                        datatemp[0] = CalibrateU12AdcValue(datatemp[0], OffsetSGHigh, VRef, GainSGHigh);
                        datatemp[1] = CalibrateU12AdcValue(datatemp[1], OffsetSGLow, VRef, GainSGLow);
                    }

                    objectCluster.Add(Shimmer3Configuration.SignalNames.BRIGE_AMPLIFIER_HIGH, ShimmerConfiguration.SignalFormats.RAW, ShimmerConfiguration.SignalUnits.NoUnits, newPacket[iSGHigh]);
                    objectCluster.Add(Shimmer3Configuration.SignalNames.BRIGE_AMPLIFIER_HIGH, ShimmerConfiguration.SignalFormats.CAL, ShimmerConfiguration.SignalUnits.MilliVolts, datatemp[0]);
                    objectCluster.Add(Shimmer3Configuration.SignalNames.BRIGE_AMPLIFIER_LOW, ShimmerConfiguration.SignalFormats.RAW, ShimmerConfiguration.SignalUnits.NoUnits, newPacket[iSGLow]);
                    objectCluster.Add(Shimmer3Configuration.SignalNames.BRIGE_AMPLIFIER_LOW, ShimmerConfiguration.SignalFormats.CAL, ShimmerConfiguration.SignalUnits.MilliVolts, datatemp[1]);
                }
                if ((((EnabledSensors & (int)SensorBitmapShimmer3.SENSOR_A_ACCEL) > 0) || ((EnabledSensors & (int)SensorBitmapShimmer3.SENSOR_D_ACCEL) > 0) || ((EnabledSensors & (int)SensorBitmapShimmer3.SENSOR_ACCEL_ALT) > 0))
                 && ((EnabledSensors & (int)SensorBitmapShimmer3.SENSOR_GYRO) > 0) && (((EnabledSensors & (int)SensorBitmapShimmer3.SENSOR_MAG) > 0) || ((EnabledSensors & (int)SensorBitmapShimmer3.SENSOR_MAG_ALT) > 0))
                 && Orientation3DEnabled)
                {
                    if (OrientationAlgo == null)
                    {
                        OrientationAlgo = new GradDes3DOrientation(0.4, (1 / this.GetSamplingRate()), 1, 0, 0, 0);
                    }
                    Quaternion q = OrientationAlgo.update(accelerometer[0], accelerometer[1], accelerometer[2], gyroscope[0], gyroscope[1], gyroscope[2], magnetometer[0], magnetometer[1], magnetometer[2]);
                    double theta, Rx, Ry, Rz, rho;
                    rho = Math.Acos(q.q1);
                    theta = rho * 2;
                    Rx = q.q2 / Math.Sin(rho);
                    Ry = q.q3 / Math.Sin(rho);
                    Rz = q.q4 / Math.Sin(rho);
                    objectCluster.Add(Shimmer3Configuration.SignalNames.AXIS_ANGLE_A, ShimmerConfiguration.SignalFormats.CAL, ShimmerConfiguration.SignalUnits.Local, theta);
                    objectCluster.Add(Shimmer3Configuration.SignalNames.AXIS_ANGLE_X, ShimmerConfiguration.SignalFormats.CAL, ShimmerConfiguration.SignalUnits.Local, Rx);
                    objectCluster.Add(Shimmer3Configuration.SignalNames.AXIS_ANGLE_Y, ShimmerConfiguration.SignalFormats.CAL, ShimmerConfiguration.SignalUnits.Local, Ry);
                    objectCluster.Add(Shimmer3Configuration.SignalNames.AXIS_ANGLE_Z, ShimmerConfiguration.SignalFormats.CAL, ShimmerConfiguration.SignalUnits.Local, Rz);
                    objectCluster.Add(Shimmer3Configuration.SignalNames.QUATERNION_0, ShimmerConfiguration.SignalFormats.CAL, ShimmerConfiguration.SignalUnits.Local, q.q1);
                    objectCluster.Add(Shimmer3Configuration.SignalNames.QUATERNION_1, ShimmerConfiguration.SignalFormats.CAL, ShimmerConfiguration.SignalUnits.Local, q.q2);
                    objectCluster.Add(Shimmer3Configuration.SignalNames.QUATERNION_2, ShimmerConfiguration.SignalFormats.CAL, ShimmerConfiguration.SignalUnits.Local, q.q3);
                    objectCluster.Add(Shimmer3Configuration.SignalNames.QUATERNION_3, ShimmerConfiguration.SignalFormats.CAL, ShimmerConfiguration.SignalUnits.Local, q.q4);
                }
            }
            else if (HardwareVersion == (int)ShimmerVersion.SHIMMER2R)
            { //start of Shimmer2

                if (((EnabledSensors & (int)SensorBitmapShimmer2.SENSOR_ACCEL) > 0))
                {
                    int iAccelX = getSignalIndex(Shimmer2Configuration.SignalNames.ACCELEROMETER_X); //find index
                    int iAccelY = getSignalIndex(Shimmer2Configuration.SignalNames.ACCELEROMETER_Y); //find index
                    int iAccelZ = getSignalIndex(Shimmer2Configuration.SignalNames.ACCELEROMETER_Z); //find index
                    double[] datatemp = new double[3] { newPacket[iAccelX], newPacket[iAccelY], newPacket[iAccelZ] };
                    datatemp = UtilCalibration.CalibrateInertialSensorData(datatemp, AlignmentMatrixAccel, SensitivityMatrixAccel, OffsetVectorAccel);
                    string units;
                    if (DefaultAccelParams)
                    {
                        units = ShimmerConfiguration.SignalUnits.MeterPerSecondSquared_DefaultCal;
                    }
                    else
                    {
                        units = ShimmerConfiguration.SignalUnits.MeterPerSecondSquared;
                    }
                    objectCluster.Add(Shimmer2Configuration.SignalNames.ACCELEROMETER_X, ShimmerConfiguration.SignalFormats.RAW, ShimmerConfiguration.SignalUnits.NoUnits, newPacket[iAccelX]);
                    objectCluster.Add(Shimmer2Configuration.SignalNames.ACCELEROMETER_X, ShimmerConfiguration.SignalFormats.CAL, units, datatemp[0]);
                    objectCluster.Add(Shimmer2Configuration.SignalNames.ACCELEROMETER_Y, ShimmerConfiguration.SignalFormats.RAW, ShimmerConfiguration.SignalUnits.NoUnits, newPacket[iAccelY]);
                    objectCluster.Add(Shimmer2Configuration.SignalNames.ACCELEROMETER_Y, ShimmerConfiguration.SignalFormats.CAL, units, datatemp[1]);
                    objectCluster.Add(Shimmer2Configuration.SignalNames.ACCELEROMETER_Z, ShimmerConfiguration.SignalFormats.RAW, ShimmerConfiguration.SignalUnits.NoUnits, newPacket[iAccelZ]);
                    objectCluster.Add(Shimmer2Configuration.SignalNames.ACCELEROMETER_Z, ShimmerConfiguration.SignalFormats.CAL, units, datatemp[2]);
                    accelerometer[0] = datatemp[0];
                    accelerometer[1] = datatemp[1];
                    accelerometer[2] = datatemp[2];
                }

                if (((EnabledSensors & (int)SensorBitmapShimmer2.SENSOR_GYRO) > 0))
                {
                    int iGyroX = getSignalIndex(Shimmer2Configuration.SignalNames.GYROSCOPE_X);
                    int iGyroY = getSignalIndex(Shimmer2Configuration.SignalNames.GYROSCOPE_Y);
                    int iGyroZ = getSignalIndex(Shimmer2Configuration.SignalNames.GYROSCOPE_Z);
                    double[] datatemp = new double[3] { newPacket[iGyroX], newPacket[iGyroY], newPacket[iGyroZ] };
                    datatemp = UtilCalibration.CalibrateInertialSensorData(datatemp, AlignmentMatrixGyro, SensitivityMatrixGyro, OffsetVectorGyro);
                    string units;
                    if (DefaultGyroParams)
                    {
                        units = ShimmerConfiguration.SignalUnits.DegreePerSecond_DefaultCal;
                    }
                    else
                    {
                        units = ShimmerConfiguration.SignalUnits.DegreePerSecond;
                    }
                    objectCluster.Add(Shimmer2Configuration.SignalNames.GYROSCOPE_X, ShimmerConfiguration.SignalFormats.RAW, ShimmerConfiguration.SignalUnits.NoUnits, newPacket[iGyroX]);
                    objectCluster.Add(Shimmer2Configuration.SignalNames.GYROSCOPE_X, ShimmerConfiguration.SignalFormats.CAL, units, datatemp[0]);
                    objectCluster.Add(Shimmer2Configuration.SignalNames.GYROSCOPE_Y, ShimmerConfiguration.SignalFormats.RAW, ShimmerConfiguration.SignalUnits.NoUnits, newPacket[iGyroY]);
                    objectCluster.Add(Shimmer2Configuration.SignalNames.GYROSCOPE_Y, ShimmerConfiguration.SignalFormats.CAL, units, datatemp[1]);
                    objectCluster.Add(Shimmer2Configuration.SignalNames.GYROSCOPE_Z, ShimmerConfiguration.SignalFormats.RAW, ShimmerConfiguration.SignalUnits.NoUnits, newPacket[iGyroZ]);
                    objectCluster.Add(Shimmer2Configuration.SignalNames.GYROSCOPE_Z, ShimmerConfiguration.SignalFormats.CAL, units, datatemp[2]);

                    gyroscope[0] = datatemp[0] * Math.PI / 180;
                    gyroscope[1] = datatemp[1] * Math.PI / 180;
                    gyroscope[2] = datatemp[2] * Math.PI / 180;

                    if (EnableGyroOnTheFlyCalibration)
                    {
                        GyroXRawList.Add(newPacket[iGyroX]);
                        GyroYRawList.Add(newPacket[iGyroY]);
                        GyroZRawList.Add(newPacket[iGyroZ]);
                        if (GyroXRawList.Count > ListSizeGyroOnTheFly)
                        {
                            GyroXRawList.RemoveAt(0);
                            GyroYRawList.RemoveAt(0);
                            GyroZRawList.RemoveAt(0);
                        }
                        GyroXCalList.Add(datatemp[0]);
                        GyroYCalList.Add(datatemp[1]);
                        GyroZCalList.Add(datatemp[2]);
                        if (GyroXCalList.Count > ListSizeGyroOnTheFly)
                        {
                            GyroXCalList.RemoveAt(0);
                            GyroYCalList.RemoveAt(0);
                            GyroZCalList.RemoveAt(0);

                            if (GetStandardDeviation(GyroXCalList) < ThresholdGyroOnTheFly && GetStandardDeviation(GyroYCalList) < ThresholdGyroOnTheFly && GetStandardDeviation(GyroZCalList) < ThresholdGyroOnTheFly)
                            {
                                OffsetVectorGyro[0, 0] = GyroXRawList.Average();
                                OffsetVectorGyro[1, 0] = GyroYRawList.Average();
                                OffsetVectorGyro[2, 0] = GyroZRawList.Average();
                            }
                        }
                    }
                }
                if (((EnabledSensors & (int)SensorBitmapShimmer2.SENSOR_MAG) > 0))
                {
                    int iMagX = getSignalIndex(Shimmer2Configuration.SignalNames.MAGNETOMETER_X);
                    int iMagY = getSignalIndex(Shimmer2Configuration.SignalNames.MAGNETOMETER_Y);
                    int iMagZ = getSignalIndex(Shimmer2Configuration.SignalNames.MAGNETOMETER_Z);
                    double[] datatemp = new double[3] { newPacket[iMagX], newPacket[iMagY], newPacket[iMagZ] };
                    datatemp = UtilCalibration.CalibrateInertialSensorData(datatemp, AlignmentMatrixMag, SensitivityMatrixMag, OffsetVectorMag);
                    string units;
                    if (DefaultMagParams)
                    {
                        units = ShimmerConfiguration.SignalUnits.Local_DefaultCal;
                    }
                    else
                    {
                        units = ShimmerConfiguration.SignalUnits.Local;
                    }
                    objectCluster.Add(Shimmer2Configuration.SignalNames.MAGNETOMETER_X, ShimmerConfiguration.SignalFormats.RAW, ShimmerConfiguration.SignalUnits.NoUnits, newPacket[iMagX]);
                    objectCluster.Add(Shimmer2Configuration.SignalNames.MAGNETOMETER_X, ShimmerConfiguration.SignalFormats.CAL, units, datatemp[0]);
                    objectCluster.Add(Shimmer2Configuration.SignalNames.MAGNETOMETER_Y, ShimmerConfiguration.SignalFormats.RAW, ShimmerConfiguration.SignalUnits.NoUnits, newPacket[iMagY]);
                    objectCluster.Add(Shimmer2Configuration.SignalNames.MAGNETOMETER_Y, ShimmerConfiguration.SignalFormats.CAL, units, datatemp[1]);
                    objectCluster.Add(Shimmer2Configuration.SignalNames.MAGNETOMETER_Z, ShimmerConfiguration.SignalFormats.RAW, ShimmerConfiguration.SignalUnits.NoUnits, newPacket[iMagZ]);
                    objectCluster.Add(Shimmer2Configuration.SignalNames.MAGNETOMETER_Z, ShimmerConfiguration.SignalFormats.CAL, units, datatemp[2]);

                    magnetometer[0] = datatemp[0];
                    magnetometer[1] = datatemp[1];
                    magnetometer[2] = datatemp[2];
                }

                if (((EnabledSensors & (int)SensorBitmapShimmer2.SENSOR_GSR) > 0))
                {
                    int iGSR = getSignalIndex(Shimmer2Configuration.SignalNames.GSR);
                    int newGSRRange = -1; // initialized to -1 so it will only come into play if mGSRRange = 4  
                    double datatemp = newPacket[iGSR];
                    double gsrResistanceKOhms = -1;
                    //double p1 = 0, p2 = 0;
                    if (GSRRange == 4)
                    {
                        newGSRRange = (49152 & (int)datatemp) >> 14;
                    }
                    datatemp = (double)((int)datatemp & 4095);
                    if (GSRRange == 0 || newGSRRange == 0)
                    { //Note that from FW 1.0 onwards the MSB of the GSR data contains the range
                        // the polynomial function used for calibration has been deprecated, it is replaced with a linear function
                        //p1 = 0.0373;
                        //p2 = -24.9915;
                        gsrResistanceKOhms = CalibrateGsrDataToResistanceFromAmplifierEq(datatemp, 0);
                    }
                    else if (GSRRange == 1 || newGSRRange == 1)
                    {
                        //p1 = 0.0054;
                        //p2 = -3.5194;
                        gsrResistanceKOhms = CalibrateGsrDataToResistanceFromAmplifierEq(datatemp, 1);
                    }
                    else if (GSRRange == 2 || newGSRRange == 2)
                    {
                        //p1 = 0.0015;
                        //p2 = -1.0163;
                        gsrResistanceKOhms = CalibrateGsrDataToResistanceFromAmplifierEq(datatemp, 2);
                    }
                    else if (GSRRange == 3 || newGSRRange == 3)
                    {
                        //p1 = 4.5580e-04;
                        //p2 = -0.3014;
                        if (datatemp < GSR_UNCAL_LIMIT_RANGE3)
                        {
                            datatemp = GSR_UNCAL_LIMIT_RANGE3;
                        }
                        gsrResistanceKOhms = CalibrateGsrDataToResistanceFromAmplifierEq(datatemp, 3);
                    }
                    //datatemp = CalibrateGsrData(datatemp, p1, p2);
                    gsrResistanceKOhms = NudgeGsrResistance(gsrResistanceKOhms, GSRRange);
                    double gsrConductanceUSiemens = (1.0 / gsrResistanceKOhms) * 1000;
                    objectCluster.Add(Shimmer2Configuration.SignalNames.GSR, ShimmerConfiguration.SignalFormats.RAW, ShimmerConfiguration.SignalUnits.NoUnits, newPacket[iGSR]);
                    objectCluster.Add(Shimmer2Configuration.SignalNames.GSR, ShimmerConfiguration.SignalFormats.CAL, ShimmerConfiguration.SignalUnits.KiloOhms, gsrResistanceKOhms);
                    objectCluster.Add(Shimmer2Configuration.SignalNames.GSR_CONDUCTANCE, ShimmerConfiguration.SignalFormats.CAL, ShimmerConfiguration.SignalUnits.MicroSiemens, gsrConductanceUSiemens);

                }
                if (((EnabledSensors & (int)SensorBitmapShimmer2.SENSOR_ECG) > 0))
                {
                    int iECGRALL = getSignalIndex(Shimmer2Configuration.SignalNames.ECG_RA_LL);
                    int iECGLALL = getSignalIndex(Shimmer2Configuration.SignalNames.ECG_LA_LL);
                    double[] datatemp = new double[2] { newPacket[iECGRALL], newPacket[iECGLALL] };
                    datatemp[0] = CalibrateU12AdcValue(datatemp[0], OffsetECGRALL, 3, GainECGRALL);
                    datatemp[1] = CalibrateU12AdcValue(datatemp[1], OffsetECGLALL, 3, GainECGLALL);
                    string units = ShimmerConfiguration.SignalUnits.MilliVolts;
                    if (DefaultECGParams)
                    {
                        units = ShimmerConfiguration.SignalUnits.MilliVolts_DefaultCal;
                    }
                    objectCluster.Add(Shimmer2Configuration.SignalNames.ECG_RA_LL, ShimmerConfiguration.SignalFormats.RAW, ShimmerConfiguration.SignalUnits.NoUnits, newPacket[iECGRALL]);
                    objectCluster.Add(Shimmer2Configuration.SignalNames.ECG_RA_LL, ShimmerConfiguration.SignalFormats.CAL, units, datatemp[0]);
                    objectCluster.Add(Shimmer2Configuration.SignalNames.ECG_LA_LL, ShimmerConfiguration.SignalFormats.RAW, ShimmerConfiguration.SignalUnits.NoUnits, newPacket[iECGLALL]);
                    objectCluster.Add(Shimmer2Configuration.SignalNames.ECG_LA_LL, ShimmerConfiguration.SignalFormats.CAL, units, datatemp[1]);
                }
                if (((EnabledSensors & (int)SensorBitmapShimmer2.SENSOR_EMG) > 0))
                {
                    int iEMG = getSignalIndex(Shimmer2Configuration.SignalNames.EMG);
                    double datatemp = newPacket[iEMG];
                    datatemp = CalibrateU12AdcValue(datatemp, OffsetEMG, 3, GainEMG);
                    string units = ShimmerConfiguration.SignalUnits.MilliVolts;
                    if (DefaultEMGParams)
                    {
                        units = ShimmerConfiguration.SignalUnits.MilliVolts_DefaultCal;
                    }
                    objectCluster.Add(Shimmer2Configuration.SignalNames.EMG, ShimmerConfiguration.SignalFormats.RAW, ShimmerConfiguration.SignalUnits.NoUnits, newPacket[iEMG]);
                    objectCluster.Add(Shimmer2Configuration.SignalNames.EMG, ShimmerConfiguration.SignalFormats.CAL, units, datatemp);
                }
                if (((EnabledSensors & (int)SensorBitmapShimmer2.SENSOR_STRAIN_GAUGE) > 0))
                {
                    int iSGHigh = getSignalIndex(Shimmer2Configuration.SignalNames.STRAIN_GAUGE_HIGH);
                    int iSGLow = getSignalIndex(Shimmer2Configuration.SignalNames.STRAIN_GAUGE_LOW);
                    double[] datatemp = new double[2] { newPacket[iSGHigh], newPacket[iSGLow] };
                    datatemp[0] = CalibrateU12AdcValue(datatemp[0], OffsetSGHigh, VRef, GainSGHigh);
                    datatemp[1] = CalibrateU12AdcValue(datatemp[1], OffsetSGLow, VRef, GainSGLow);
                    objectCluster.Add(Shimmer2Configuration.SignalNames.STRAIN_GAUGE_HIGH, ShimmerConfiguration.SignalFormats.RAW, ShimmerConfiguration.SignalUnits.NoUnits, newPacket[iSGHigh]);
                    objectCluster.Add(Shimmer2Configuration.SignalNames.STRAIN_GAUGE_HIGH, ShimmerConfiguration.SignalFormats.CAL, ShimmerConfiguration.SignalUnits.MilliVolts, datatemp[0]);
                    objectCluster.Add(Shimmer2Configuration.SignalNames.STRAIN_GAUGE_LOW, ShimmerConfiguration.SignalFormats.RAW, ShimmerConfiguration.SignalUnits.NoUnits, newPacket[iSGLow]);
                    objectCluster.Add(Shimmer2Configuration.SignalNames.STRAIN_GAUGE_LOW, ShimmerConfiguration.SignalFormats.CAL, ShimmerConfiguration.SignalUnits.MilliVolts, datatemp[1]);
                }
                if (((EnabledSensors & (int)SensorBitmapShimmer2.SENSOR_HEART) > 0))
                {
                    int iHeartRate = getSignalIndex(Shimmer2Configuration.SignalNames.HEART_RATE);
                    double datatemp = newPacket[iHeartRate];
                    double cal = datatemp;
                    //if (FirmwareVersion == 0.1)
                    if (CompatibilityCode == 1)
                    {

                    }
                    else
                    {
                        if (datatemp == 0)
                        {
                            cal = LastKnownHeartRate;
                        }
                        else
                        {
                            cal = (int)(1024 / datatemp * 60);
                            LastKnownHeartRate = (int)cal;
                        }
                    }
                    objectCluster.Add(Shimmer2Configuration.SignalNames.HEART_RATE, ShimmerConfiguration.SignalFormats.RAW, ShimmerConfiguration.SignalUnits.NoUnits, newPacket[iHeartRate]);
                    objectCluster.Add(Shimmer2Configuration.SignalNames.HEART_RATE, ShimmerConfiguration.SignalFormats.CAL, "BPM", cal);
                }
                if (((EnabledSensors & (int)SensorBitmapShimmer2.SENSOR_EXP_BOARD_A0) > 0))
                {
                    int iA0 = getSignalIndex(Shimmer2Configuration.SignalNames.EXPBOARD_A0);
                    double datatemp = newPacket[iA0];
                    datatemp = CalibrateU12AdcValue(datatemp, 0, 3, 1) * 1.988;
                    if (GetPMux())
                    {
                        objectCluster.Add(Shimmer2Configuration.SignalNames.V_SENSE_REG, ShimmerConfiguration.SignalFormats.RAW, ShimmerConfiguration.SignalUnits.NoUnits, newPacket[iA0]);
                        objectCluster.Add(Shimmer2Configuration.SignalNames.V_SENSE_REG, ShimmerConfiguration.SignalFormats.CAL, ShimmerConfiguration.SignalUnits.MilliVolts, datatemp);
                    }
                    else
                    {
                        objectCluster.Add(Shimmer2Configuration.SignalNames.EXPBOARD_A0, ShimmerConfiguration.SignalFormats.RAW, ShimmerConfiguration.SignalUnits.NoUnits, newPacket[iA0]);
                        objectCluster.Add(Shimmer2Configuration.SignalNames.EXPBOARD_A0, ShimmerConfiguration.SignalFormats.CAL, ShimmerConfiguration.SignalUnits.MilliVolts, datatemp);
                    }
                }
                if (((EnabledSensors & (int)SensorBitmapShimmer2.SENSOR_EXP_BOARD_A7) > 0))
                {
                    int iA7 = getSignalIndex(Shimmer2Configuration.SignalNames.EXPBOARD_A7);
                    double datatemp = newPacket[iA7];
                    datatemp = CalibrateU12AdcValue(datatemp, 0, 3, 1) * 2;
                    if (GetPMux())
                    {
                        objectCluster.Add(Shimmer2Configuration.SignalNames.V_SENSE_BATT, ShimmerConfiguration.SignalFormats.RAW, ShimmerConfiguration.SignalUnits.NoUnits, newPacket[iA7]);
                        objectCluster.Add(Shimmer2Configuration.SignalNames.V_SENSE_BATT, ShimmerConfiguration.SignalFormats.CAL, ShimmerConfiguration.SignalUnits.MilliVolts, datatemp);
                        if (datatemp < 3400)
                        {
                            //System.Threading.Thread.Sleep(500);
                            if (CurrentLEDStatus == 0)
                            {
                                WriteBytes(new byte[2] { (byte)ShimmerBluetooth.PacketTypeShimmer2.SET_BLINK_LED, (byte)1 }, 0, 2);
                                CurrentLEDStatus = 1;

                            }
                            else
                            {
                                //System.Threading.Thread.Sleep(500);
                                if (CurrentLEDStatus == 1)
                                {
                                    WriteBytes(new byte[2] { (byte)ShimmerBluetooth.PacketTypeShimmer2.SET_BLINK_LED, (byte)0 }, 0, 2);
                                    CurrentLEDStatus = 0;

                                }
                            }
                        }
                    }
                    else
                    {
                        objectCluster.Add(Shimmer2Configuration.SignalNames.EXPBOARD_A7, ShimmerConfiguration.SignalFormats.RAW, ShimmerConfiguration.SignalUnits.NoUnits, newPacket[iA7]);
                        objectCluster.Add(Shimmer2Configuration.SignalNames.EXPBOARD_A7, ShimmerConfiguration.SignalFormats.CAL, ShimmerConfiguration.SignalUnits.MilliVolts, datatemp);
                    }

                }
                if (((EnabledSensors & (int)SensorBitmapShimmer2.SENSOR_ACCEL) > 0) && ((EnabledSensors & (int)SensorBitmapShimmer2.SENSOR_GYRO) > 0) && ((EnabledSensors & (int)SensorBitmapShimmer2.SENSOR_MAG) > 0) && Orientation3DEnabled)
                {
                    if (OrientationAlgo == null)
                    {
                        OrientationAlgo = new GradDes3DOrientation(0.4, 1 / this.GetSamplingRate(), 1, 0, 0, 0);
                    }
                    Quaternion q = OrientationAlgo.update(accelerometer[0], accelerometer[1], accelerometer[2], gyroscope[0], gyroscope[1], gyroscope[2], magnetometer[0], magnetometer[1], magnetometer[2]);
                    double theta, Rx, Ry, Rz, rho;
                    rho = Math.Acos(q.q1);
                    theta = rho * 2;
                    Rx = q.q2 / Math.Sin(rho);
                    Ry = q.q3 / Math.Sin(rho);
                    Rz = q.q4 / Math.Sin(rho);
                    objectCluster.Add(Shimmer2Configuration.SignalNames.AXIS_ANGLE_A, ShimmerConfiguration.SignalFormats.CAL, ShimmerConfiguration.SignalUnits.Local, theta);
                    objectCluster.Add(Shimmer2Configuration.SignalNames.AXIS_ANGLE_X, ShimmerConfiguration.SignalFormats.CAL, ShimmerConfiguration.SignalUnits.Local, Rx);
                    objectCluster.Add(Shimmer2Configuration.SignalNames.AXIS_ANGLE_Y, ShimmerConfiguration.SignalFormats.CAL, ShimmerConfiguration.SignalUnits.Local, Ry);
                    objectCluster.Add(Shimmer2Configuration.SignalNames.AXIS_ANGLE_Z, ShimmerConfiguration.SignalFormats.CAL, ShimmerConfiguration.SignalUnits.Local, Rz);
                    objectCluster.Add(Shimmer2Configuration.SignalNames.QUATERNION_0, ShimmerConfiguration.SignalFormats.CAL, ShimmerConfiguration.SignalUnits.Local, q.q1);
                    objectCluster.Add(Shimmer2Configuration.SignalNames.QUATERNION_1, ShimmerConfiguration.SignalFormats.CAL, ShimmerConfiguration.SignalUnits.Local, q.q2);
                    objectCluster.Add(Shimmer2Configuration.SignalNames.QUATERNION_2, ShimmerConfiguration.SignalFormats.CAL, ShimmerConfiguration.SignalUnits.Local, q.q3);
                    objectCluster.Add(Shimmer2Configuration.SignalNames.QUATERNION_3, ShimmerConfiguration.SignalFormats.CAL, ShimmerConfiguration.SignalUnits.Local, q.q4);
                }
            }
            return objectCluster;
        }


        /**
	 * Converts the raw packet byte values, into the corresponding calibrated and uncalibrated sensor values, the Instruction String determines the output 
	 * @param newPacket a byte array containing the current received packet
	 * @param Instructions an array string containing the commands to execute. It is currently not fully supported
	 * @return
	 */

        protected long[] ParseData(byte[] data, String[] dataType)
        {
            int iData = 0;
            long[] formattedData = new long[dataType.Length];

            for (int i = 0; i < dataType.Length; i++)
                if (dataType[i] == "u8")
                {
                    formattedData[i] = (int)data[iData];
                    iData = iData + 1;
                }
                else if (dataType[i] == "i8")
                {
                    formattedData[i] = Calculatetwoscomplement((int)((int)0xFF & data[iData]), 8);
                    iData = iData + 1;
                }
                else if (dataType[i] == "u12")
                {
                    formattedData[i] = (int)((int)(data[iData] & 0xFF) + ((int)(data[iData + 1] & 0xFF) << 8));
                    iData = iData + 2;
                }
                else if (dataType[i] == "i12>")
                {
                    formattedData[i] = Calculatetwoscomplement((int)((int)(data[iData] & 0xFF) + ((int)(data[iData + 1] & 0xFF) << 8)), 16);
                    formattedData[i] = formattedData[i] >> 4; // shift right by 4 bits
                    iData = iData + 2;
                }
                else if (dataType[i] == "i12*>")
                {
                    // MSB byte shifted left 4-bits or'd with upper 4-bits of LSB byte which are bit-shifted right by 4
                    formattedData[i] = ((int)(data[iData] & 0xFF) << 4) | ((int)(data[iData + 1] & 0xFF) >> 4);
                    // Two's complement on the resulting 12-bit number
                    formattedData[i] = Calculatetwoscomplement((int)formattedData[i], 12);
                    iData = iData + 2;
                }
                else if (dataType[i] == "u16")
                {
                    formattedData[i] = (int)((int)(data[iData] & 0xFF) + ((int)(data[iData + 1] & 0xFF) << 8));
                    iData = iData + 2;
                }
                else if (dataType[i] == "u16r")
                {
                    formattedData[i] = (int)((int)(data[iData + 1] & 0xFF) + ((int)(data[iData + 0] & 0xFF) << 8));
                    iData = iData + 2;
                }
                else if (dataType[i] == "i16")
                {
                    formattedData[i] = Calculatetwoscomplement((int)((int)(data[iData] & 0xFF) + ((int)(data[iData + 1] & 0xFF) << 8)), 16);
                    //formattedData[i]=ByteBuffer.wrap(arrayb).order(ByteOrder.LITTLE_ENDIAN).getShort();
                    iData = iData + 2;
                }
                else if (dataType[i] == "i16*")
                {
                    formattedData[i] = Calculatetwoscomplement((int)((int)(data[iData + 1] & 0xFF) + ((int)(data[iData] & 0xFF) << 8)), 16);
                    //formattedData[i]=ByteBuffer.wrap(arrayb).order(ByteOrder.LITTLE_ENDIAN).getShort();
                    iData = iData + 2;
                }
                else if (dataType[i] == "i16r")
                {
                    formattedData[i] = Calculatetwoscomplement((int)((int)(data[iData + 1] & 0xFF) + ((int)(data[iData] & 0xFF) << 8)), 16);
                    //formattedData[i]=ByteBuffer.wrap(arrayb).order(ByteOrder.LITTLE_ENDIAN).getShort();
                    iData = iData + 2;
                }
                else if (dataType[i] == "u24")
                {
                    long xmsb = ((long)(data[iData + 2] & 0xFF) << 16);
                    long msb = ((long)(data[iData + 1] & 0xFF) << 8);
                    long lsb = ((long)(data[iData + 0] & 0xFF));
                    formattedData[i] = xmsb + msb + lsb;
                    iData = iData + 3;
                }
                else if (dataType[i] == "u24r")
                {
                    long xmsb = ((long)(data[iData + 0] & 0xFF) << 16);
                    long msb = ((long)(data[iData + 1] & 0xFF) << 8);
                    long lsb = ((long)(data[iData + 2] & 0xFF));
                    formattedData[i] = xmsb + msb + lsb;
                    iData = iData + 3;
                }
                else if (dataType[i] == "i24r")
                {
                    long xmsb = ((long)(data[iData + 0] & 0xFF) << 16);
                    long msb = ((long)(data[iData + 1] & 0xFF) << 8);
                    long lsb = ((long)(data[iData + 2] & 0xFF));
                    formattedData[i] = xmsb + msb + lsb;
                    formattedData[i] = Calculatetwoscomplement((int)formattedData[i], 24);
                    iData = iData + 3;
                }
            return formattedData;
        }

        protected int getSignalIndex(String signalName)
        {
            int iSignal = 0; //used to be -1, putting to zero ensure it works eventhough it might be wrong SR30
            for (int i = 0; i < SignalNameArray.Length; i++)
            {
                if (signalName == SignalNameArray[i])
                {
                    iSignal = i;
                }
            }

            return iSignal;
        }

        public void WriteSensors(int sensors)
        {
            if (SensorConflictCheck(sensors))   //pass is true
            {
                if (HardwareVersion != (int)ShimmerBluetooth.ShimmerVersion.SHIMMER3 && HardwareVersion != (int)ShimmerBluetooth.ShimmerVersion.SHIMMER3R)
                {
                    if ((sensors & (int)SensorBitmapShimmer2.SENSOR_VBATT) > 0)
                    {
                        sensors = sensors | (int)SensorBitmapShimmer2.SENSOR_EXP_BOARD_A7;
                        sensors = sensors | (int)SensorBitmapShimmer2.SENSOR_EXP_BOARD_A0;
                    }

                    WriteBytes(new byte[3] { (byte) PacketTypeShimmer2.SET_SENSORS_COMMAND,
                        (byte)(sensors & 0xff), (byte)(sensors>>8 & 0xff)}, 0, 3);

                    if ((sensors & (int)SensorBitmapShimmer2.SENSOR_GYRO) > 0)
                    {
                        System.Threading.Thread.Sleep(7000);
                    }
                    else
                    {
                        System.Threading.Thread.Sleep(500);
                    }
                    if (!SetupDevice) //the initialize method will already end with an inquiry
                    {
                        WriteBytes(new byte[1] { (byte)PacketTypeShimmer2.INQUIRY_COMMAND }, 0, 1);
                    }
                    // give the shimmer a chance to process the previous command (required?)
                    System.Threading.Thread.Sleep(500);
                    if (GetFirmwareIdentifier() == FW_IDENTIFIER_LOGANDSTREAM)
                    {
                        //WriteSdConfigFile();
                    }

                }
                else
                {
                    byte firstByte = (byte)(sensors & 0xff);
                    byte secondByte = (byte)(sensors >> 8 & 0xff);
                    byte thirdByte = (byte)(sensors >> 16 & 0xff);
                    WriteBytes(new byte[4] { (byte)PacketTypeShimmer2.SET_SENSORS_COMMAND, firstByte, secondByte, thirdByte }, 0, 4);
                    System.Threading.Thread.Sleep(1000);
                    if (!SetupDevice) //the initialize method will already end with an inquiry
                    {
                        WriteBytes(new byte[1] { (byte)PacketTypeShimmer2.INQUIRY_COMMAND }, 0, 1);
                    }
                    // give the shimmer a chance to process the previous command (required?)
                    System.Threading.Thread.Sleep(500);
                }
                ReadCalibrationParameters("All");
                System.Threading.Thread.Sleep(1000);
            }
        }

        public virtual void WriteSdConfigFile() { }

        protected bool SensorConflictCheck(int enabledSensors)
        {
            bool pass = true;
            if (HardwareVersion == (int)ShimmerVersion.SHIMMER2
                || HardwareVersion == (int)ShimmerVersion.SHIMMER2R
                || HardwareVersion == (int)ShimmerVersion.SHIMMER1)
            {
                if (((enabledSensors & 0xFF) & (int)SensorBitmapShimmer2.SENSOR_GYRO) > 0)
                {
                    if (((enabledSensors & 0xFF) & (int)SensorBitmapShimmer2.SENSOR_EMG) > 0)
                    {
                        pass = false;
                    }
                    else if (((enabledSensors & 0xFF) & (int)SensorBitmapShimmer2.SENSOR_ECG) > 0)
                    {
                        pass = false;
                    }
                    else if (((enabledSensors & 0xFF) & (int)SensorBitmapShimmer2.SENSOR_GSR) > 0)
                    {
                        pass = false;
                    }
                    else if (((enabledSensors & 0xFF00) & (int)SensorBitmapShimmer2.SENSOR_STRAIN_GAUGE) > 0)
                    {
                        pass = false;
                    }
                }

                if (((enabledSensors & 0xFF) & (int)SensorBitmapShimmer2.SENSOR_MAG) > 0)
                {
                    if (((enabledSensors & 0xFF) & (int)SensorBitmapShimmer2.SENSOR_EMG) > 0)
                    {
                        pass = false;
                    }
                    else if (((enabledSensors & 0xFF) & (int)SensorBitmapShimmer2.SENSOR_ECG) > 0)
                    {
                        pass = false;
                    }
                    else if (((enabledSensors & 0xFF) & (int)SensorBitmapShimmer2.SENSOR_GSR) > 0)
                    {
                        pass = false;
                    }
                    else if (((enabledSensors & 0xFF00) & (int)SensorBitmapShimmer2.SENSOR_STRAIN_GAUGE) > 0)
                    {
                        pass = false;
                    }
                }

                if (((enabledSensors & 0xFF) & (int)SensorBitmapShimmer2.SENSOR_EMG) > 0)
                {
                    if (((enabledSensors & 0xFF) & (int)SensorBitmapShimmer2.SENSOR_GYRO) > 0)
                    {
                        pass = false;
                    }
                    else if (((enabledSensors & 0xFF) & (int)SensorBitmapShimmer2.SENSOR_MAG) > 0)
                    {
                        pass = false;
                    }
                    else if (((enabledSensors & 0xFF) & (int)SensorBitmapShimmer2.SENSOR_ECG) > 0)
                    {
                        pass = false;
                    }
                    else if (((enabledSensors & 0xFF) & (int)SensorBitmapShimmer2.SENSOR_GSR) > 0)
                    {
                        pass = false;
                    }
                    else if (((enabledSensors & 0xFF00) & (int)SensorBitmapShimmer2.SENSOR_STRAIN_GAUGE) > 0)
                    {
                        pass = false;
                    }
                }

                if (((enabledSensors & 0xFF) & (int)SensorBitmapShimmer2.SENSOR_ECG) > 0)
                {
                    if (((enabledSensors & 0xFF) & (int)SensorBitmapShimmer2.SENSOR_GYRO) > 0)
                    {
                        pass = false;
                    }
                    else if (((enabledSensors & 0xFF) & (int)SensorBitmapShimmer2.SENSOR_MAG) > 0)
                    {
                        pass = false;
                    }
                    else if (((enabledSensors & 0xFF) & (int)SensorBitmapShimmer2.SENSOR_EMG) > 0)
                    {
                        pass = false;
                    }
                    else if (((enabledSensors & 0xFF) & (int)SensorBitmapShimmer2.SENSOR_GSR) > 0)
                    {
                        pass = false;
                    }
                    else if (((enabledSensors & 0xFF00) & (int)SensorBitmapShimmer2.SENSOR_STRAIN_GAUGE) > 0)
                    {
                        pass = false;
                    }
                }

                if (((enabledSensors & 0xFF) & (int)SensorBitmapShimmer2.SENSOR_GSR) > 0)
                {
                    if (((enabledSensors & 0xFF) & (int)SensorBitmapShimmer2.SENSOR_GYRO) > 0)
                    {
                        pass = false;
                    }
                    else if (((enabledSensors & 0xFF) & (int)SensorBitmapShimmer2.SENSOR_MAG) > 0)
                    {
                        pass = false;
                    }
                    else if (((enabledSensors & 0xFF) & (int)SensorBitmapShimmer2.SENSOR_EMG) > 0)
                    {
                        pass = false;
                    }
                    else if (((enabledSensors & 0xFF) & (int)SensorBitmapShimmer2.SENSOR_ECG) > 0)
                    {
                        pass = false;
                    }
                    else if (((enabledSensors & 0xFF00) & (int)SensorBitmapShimmer2.SENSOR_STRAIN_GAUGE) > 0)
                    {
                        pass = false;
                    }
                }

                if (((enabledSensors & 0xFF00) & (int)SensorBitmapShimmer2.SENSOR_STRAIN_GAUGE) > 0)
                {
                    if (((enabledSensors & 0xFF) & (int)SensorBitmapShimmer2.SENSOR_GYRO) > 0)
                    {
                        pass = false;
                    }
                    else if (((enabledSensors & 0xFF) & (int)SensorBitmapShimmer2.SENSOR_MAG) > 0)
                    {
                        pass = false;
                    }
                    else if (((enabledSensors & 0xFF) & (int)SensorBitmapShimmer2.SENSOR_EMG) > 0)
                    {
                        pass = false;
                    }
                    else if (((enabledSensors & 0xFF) & (int)SensorBitmapShimmer2.SENSOR_ECG) > 0)
                    {
                        pass = false;
                    }
                    else if (((enabledSensors & 0xFF) & (int)SensorBitmapShimmer2.SENSOR_GSR) > 0)
                    {
                        pass = false;
                    }
                    else if (GetVReg() == true)
                    { // if the 5volt reg is set 
                        pass = false;
                    }
                }

                if (((enabledSensors & 0xFF) & (int)SensorBitmapShimmer2.SENSOR_EXP_BOARD_A0) > 0)
                {
                    if (((enabledSensors & 0xFFFFF) & (int)SensorBitmapShimmer3.SENSOR_VBATT) > 0)
                    {
                        pass = false;
                    }
                    else if (GetPMux() == true)
                    {
                        WritePMux(0);
                    }
                }

                if (((enabledSensors & 0xFF) & (int)SensorBitmapShimmer2.SENSOR_EXP_BOARD_A7) > 0)
                {
                    if (((enabledSensors & 0xFFFFF) & (int)SensorBitmapShimmer3.SENSOR_VBATT) > 0)
                    {
                        pass = false;
                    }
                    else if (GetPMux() == true)
                    {
                        WritePMux(0);
                    }
                }

                if (((enabledSensors & 0xFFFFF) & (int)SensorBitmapShimmer2.SENSOR_VBATT) > 0)
                {
                    if (((enabledSensors & 0xFF) & (int)SensorBitmapShimmer2.SENSOR_EXP_BOARD_A7) > 0)
                    {
                        pass = false;
                    }
                    if (((enabledSensors & 0xFF) & (int)SensorBitmapShimmer2.SENSOR_EXP_BOARD_A0) > 0)
                    {
                        pass = false;
                    }
                    if (((enabledSensors & 0xFFFFF) & (int)SensorBitmapShimmer3.SENSOR_VBATT) > 0)
                    {
                        if (GetPMux() == false)
                        {

                            WritePMux(1);
                        }
                    }
                }
                if (!pass)
                {

                }
            }
            else
            {
                //Shimmer3
                if (((enabledSensors & 0xFFFFFF) & (int)SensorBitmapShimmer3.SENSOR_INT_A14) > 0)
                {
                    if ((enabledSensors & (int)SensorBitmapShimmer3.SENSOR_GSR) > 0)
                    {
                        pass = false;
                    }
                    if ((enabledSensors & (int)SensorBitmapShimmer3.SENSOR_EXG1_24BIT) > 0)
                    {
                        pass = false;
                    }
                    if ((enabledSensors & (int)SensorBitmapShimmer3.SENSOR_EXG2_24BIT) > 0)
                    {
                        pass = false;
                    }
                    if ((enabledSensors & (int)SensorBitmapShimmer3.SENSOR_EXG1_16BIT) > 0)
                    {
                        pass = false;
                    }
                    if ((enabledSensors & (int)SensorBitmapShimmer3.SENSOR_EXG2_16BIT) > 0)
                    {
                        pass = false;
                    }
                    if (((enabledSensors & 0xFF00) & (int)SensorBitmapShimmer3.SENSOR_BRIDGE_AMP) > 0)
                    {
                        pass = false;
                    }
                }
                if (((enabledSensors & 0xFFFFFF) & (int)SensorBitmapShimmer3.SENSOR_INT_A1) > 0)
                {
                    if ((enabledSensors & (int)SensorBitmapShimmer3.SENSOR_GSR) > 0)
                    {
                        pass = false;
                    }
                    if ((enabledSensors & (int)SensorBitmapShimmer3.SENSOR_EXG1_24BIT) > 0)
                    {
                        pass = false;
                    }
                    if ((enabledSensors & (int)SensorBitmapShimmer3.SENSOR_EXG2_24BIT) > 0)
                    {
                        pass = false;
                    }
                    if ((enabledSensors & (int)SensorBitmapShimmer3.SENSOR_EXG1_16BIT) > 0)
                    {
                        pass = false;
                    }
                    if ((enabledSensors & (int)SensorBitmapShimmer3.SENSOR_EXG2_16BIT) > 0)
                    {
                        pass = false;
                    }
                }
                if (((enabledSensors & 0xFFFFFF) & (int)SensorBitmapShimmer3.SENSOR_INT_A12) > 0)
                {
                    if ((enabledSensors & (int)SensorBitmapShimmer3.SENSOR_EXG1_24BIT) > 0)
                    {
                        pass = false;
                    }
                    if ((enabledSensors & (int)SensorBitmapShimmer3.SENSOR_EXG2_24BIT) > 0)
                    {
                        pass = false;
                    }
                    if ((enabledSensors & (int)SensorBitmapShimmer3.SENSOR_EXG1_16BIT) > 0)
                    {
                        pass = false;
                    }
                    if ((enabledSensors & (int)SensorBitmapShimmer3.SENSOR_EXG2_16BIT) > 0)
                    {
                        pass = false;
                    }
                    if (((enabledSensors & 0xFF00) & (int)SensorBitmapShimmer3.SENSOR_BRIDGE_AMP) > 0)
                    {
                        pass = false;
                    }
                }
                if (((enabledSensors & 0xFFFFFF) & (int)SensorBitmapShimmer3.SENSOR_INT_A13) > 0)
                {
                    if ((enabledSensors & (int)SensorBitmapShimmer3.SENSOR_EXG1_24BIT) > 0)
                    {
                        pass = false;
                    }
                    if ((enabledSensors & (int)SensorBitmapShimmer3.SENSOR_EXG2_24BIT) > 0)
                    {
                        pass = false;
                    }
                    if ((enabledSensors & (int)SensorBitmapShimmer3.SENSOR_EXG1_16BIT) > 0)
                    {
                        pass = false;
                    }
                    if ((enabledSensors & (int)SensorBitmapShimmer3.SENSOR_EXG2_16BIT) > 0)
                    {
                        pass = false;
                    }
                    if (((enabledSensors & 0xFF00) & (int)SensorBitmapShimmer3.SENSOR_BRIDGE_AMP) > 0)
                    {
                        pass = false;
                    }
                }
                if ((enabledSensors & (int)SensorBitmapShimmer3.SENSOR_GSR) > 0)
                {
                    if (((enabledSensors & 0xFFFFFF) & (int)SensorBitmapShimmer3.SENSOR_INT_A14) > 0)
                    {
                        pass = false;
                    }
                    if (((enabledSensors & 0xFFFFFF) & (int)SensorBitmapShimmer3.SENSOR_INT_A1) > 0)
                    {
                        pass = false;
                    }
                    if ((enabledSensors & (int)SensorBitmapShimmer3.SENSOR_EXG1_24BIT) > 0)
                    {
                        pass = false;
                    }
                    if ((enabledSensors & (int)SensorBitmapShimmer3.SENSOR_EXG2_24BIT) > 0)
                    {
                        pass = false;
                    }
                    if ((enabledSensors & (int)SensorBitmapShimmer3.SENSOR_EXG1_16BIT) > 0)
                    {
                        pass = false;
                    }
                    if ((enabledSensors & (int)SensorBitmapShimmer3.SENSOR_EXG2_16BIT) > 0)
                    {
                        pass = false;
                    }
                    if (((enabledSensors & 0xFF00) & (int)SensorBitmapShimmer3.SENSOR_BRIDGE_AMP) > 0)
                    {
                        pass = false;
                    }
                }
                if ((enabledSensors & (int)SensorBitmapShimmer3.SENSOR_EXG1_24BIT) > 0)
                {
                    if (((enabledSensors & 0xFFFFFF) & (int)SensorBitmapShimmer3.SENSOR_INT_A14) > 0)
                    {
                        pass = false;
                    }
                    if (((enabledSensors & 0xFFFFFF) & (int)SensorBitmapShimmer3.SENSOR_INT_A1) > 0)
                    {
                        pass = false;
                    }
                    if (((enabledSensors & 0xFFFFFF) & (int)SensorBitmapShimmer3.SENSOR_INT_A12) > 0)
                    {
                        pass = false;
                    }
                    if (((enabledSensors & 0xFFFFFF) & (int)SensorBitmapShimmer3.SENSOR_INT_A13) > 0)
                    {
                        pass = false;
                    }
                    if ((enabledSensors & (int)SensorBitmapShimmer3.SENSOR_GSR) > 0)
                    {
                        pass = false;
                    }
                    if ((enabledSensors & (int)SensorBitmapShimmer3.SENSOR_EXG1_16BIT) > 0)
                    {
                        pass = false;
                    }
                    if ((enabledSensors & (int)SensorBitmapShimmer3.SENSOR_EXG2_16BIT) > 0)
                    {
                        pass = false;
                    }
                    if (((enabledSensors & 0xFF00) & (int)SensorBitmapShimmer3.SENSOR_BRIDGE_AMP) > 0)
                    {
                        pass = false;
                    }
                }
                if ((enabledSensors & (int)SensorBitmapShimmer3.SENSOR_EXG2_24BIT) > 0)
                {
                    if (((enabledSensors & 0xFFFFFF) & (int)SensorBitmapShimmer3.SENSOR_INT_A14) > 0)
                    {
                        pass = false;
                    }
                    if (((enabledSensors & 0xFFFFFF) & (int)SensorBitmapShimmer3.SENSOR_INT_A1) > 0)
                    {
                        pass = false;
                    }
                    if (((enabledSensors & 0xFFFFFF) & (int)SensorBitmapShimmer3.SENSOR_INT_A12) > 0)
                    {
                        pass = false;
                    }
                    if (((enabledSensors & 0xFFFFFF) & (int)SensorBitmapShimmer3.SENSOR_INT_A13) > 0)
                    {
                        pass = false;
                    }
                    if ((enabledSensors & (int)SensorBitmapShimmer3.SENSOR_GSR) > 0)
                    {
                        pass = false;
                    }
                    if ((enabledSensors & (int)SensorBitmapShimmer3.SENSOR_EXG1_16BIT) > 0)
                    {
                        pass = false;
                    }
                    if ((enabledSensors & (int)SensorBitmapShimmer3.SENSOR_EXG2_16BIT) > 0)
                    {
                        pass = false;
                    }
                    if (((enabledSensors & 0xFF00) & (int)SensorBitmapShimmer3.SENSOR_BRIDGE_AMP) > 0)
                    {
                        pass = false;
                    }
                }
                if ((enabledSensors & (int)SensorBitmapShimmer3.SENSOR_EXG1_16BIT) > 0)
                {
                    if (((enabledSensors & 0xFFFFFF) & (int)SensorBitmapShimmer3.SENSOR_INT_A14) > 0)
                    {
                        pass = false;
                    }
                    if (((enabledSensors & 0xFFFFFF) & (int)SensorBitmapShimmer3.SENSOR_INT_A1) > 0)
                    {
                        pass = false;
                    }
                    if (((enabledSensors & 0xFFFFFF) & (int)SensorBitmapShimmer3.SENSOR_INT_A12) > 0)
                    {
                        pass = false;
                    }
                    if (((enabledSensors & 0xFFFFFF) & (int)SensorBitmapShimmer3.SENSOR_INT_A13) > 0)
                    {
                        pass = false;
                    }
                    if ((enabledSensors & (int)SensorBitmapShimmer3.SENSOR_GSR) > 0)
                    {
                        pass = false;
                    }
                    if ((enabledSensors & (int)SensorBitmapShimmer3.SENSOR_EXG1_24BIT) > 0)
                    {
                        pass = false;
                    }
                    if ((enabledSensors & (int)SensorBitmapShimmer3.SENSOR_EXG2_24BIT) > 0)
                    {
                        pass = false;
                    }
                    if (((enabledSensors & 0xFF00) & (int)SensorBitmapShimmer3.SENSOR_BRIDGE_AMP) > 0)
                    {
                        pass = false;
                    }
                }
                if ((enabledSensors & (int)SensorBitmapShimmer3.SENSOR_EXG2_16BIT) > 0)
                {
                    if (((enabledSensors & 0xFFFFFF) & (int)SensorBitmapShimmer3.SENSOR_INT_A14) > 0)
                    {
                        pass = false;
                    }
                    if (((enabledSensors & 0xFFFFFF) & (int)SensorBitmapShimmer3.SENSOR_INT_A1) > 0)
                    {
                        pass = false;
                    }
                    if (((enabledSensors & 0xFFFFFF) & (int)SensorBitmapShimmer3.SENSOR_INT_A12) > 0)
                    {
                        pass = false;
                    }
                    if (((enabledSensors & 0xFFFFFF) & (int)SensorBitmapShimmer3.SENSOR_INT_A13) > 0)
                    {
                        pass = false;
                    }
                    if ((enabledSensors & (int)SensorBitmapShimmer3.SENSOR_GSR) > 0)
                    {
                        pass = false;
                    }
                    if ((enabledSensors & (int)SensorBitmapShimmer3.SENSOR_EXG1_24BIT) > 0)
                    {
                        pass = false;
                    }
                    if ((enabledSensors & (int)SensorBitmapShimmer3.SENSOR_EXG2_24BIT) > 0)
                    {
                        pass = false;
                    }
                    if (((enabledSensors & 0xFF00) & (int)SensorBitmapShimmer3.SENSOR_BRIDGE_AMP) > 0)
                    {
                        pass = false;
                    }
                }
                if (((enabledSensors & 0xFF00) & (int)SensorBitmapShimmer3.SENSOR_BRIDGE_AMP) > 0)
                {
                    if (((enabledSensors & 0xFFFFFF) & (int)SensorBitmapShimmer3.SENSOR_INT_A14) > 0)
                    {
                        pass = false;
                    }
                    if (((enabledSensors & 0xFFFFFF) & (int)SensorBitmapShimmer3.SENSOR_INT_A12) > 0)
                    {
                        pass = false;
                    }
                    if (((enabledSensors & 0xFFFFFF) & (int)SensorBitmapShimmer3.SENSOR_INT_A13) > 0)
                    {
                        pass = false;
                    }
                    if ((enabledSensors & (int)SensorBitmapShimmer3.SENSOR_GSR) > 0)
                    {
                        pass = false;
                    }
                    if ((enabledSensors & (int)SensorBitmapShimmer3.SENSOR_EXG1_24BIT) > 0)
                    {
                        pass = false;
                    }
                    if ((enabledSensors & (int)SensorBitmapShimmer3.SENSOR_EXG2_24BIT) > 0)
                    {
                        pass = false;
                    }
                    if ((enabledSensors & (int)SensorBitmapShimmer3.SENSOR_EXG1_16BIT) > 0)
                    {
                        pass = false;
                    }
                    if ((enabledSensors & (int)SensorBitmapShimmer3.SENSOR_EXG2_16BIT) > 0)
                    {
                        pass = false;
                    }
                }
            }
            return pass;
        }

        public virtual void StartStreaming()
        {
            if (ShimmerState == SHIMMER_STATE_CONNECTED)
            {
                if (ShimmerState != SHIMMER_STATE_STREAMING)
                {
                    StreamingACKReceived = false;
                    StreamTimeOutCount = 0;
                    LastReceivedTimeStamp = 0;
                    CurrentTimeStampCycle = 0;
                    //A stream start: the next sample is the first one, and the pair
                    //above cannot say so on their own.
                    HasPreviousTimeStamp = false;
                    LastReceivedCalibratedTimeStamp = -1;
                    FirstTimeCalTime = true;
                    FirstSystemTimestamp = true;
                    PacketLossCount = 0;
                    PacketReceptionRate = 100;
                    KeepObjectCluster = null; //This is important and is required!
                    OrientationAlgo = null;
                    mWaitingForStartStreamingACK = true;
                    UnalignedBytesReceived = new List<Byte>();
                    WriteBytes(new byte[1] { (byte)PacketTypeShimmer2.START_STREAMING_COMMAND }, 0, 1);
                }
            }
        }

        public bool VersionLaterThan(int id, int v_major, int v_minor, int v_internal)
        {
            if (GetFirmwareIdentifier() == id)
            {
                if (GetFirmwareMajor() > v_major)
                {
                    return true;
                }
                else if (GetFirmwareMajor() == v_major)
                {
                    if (GetFirmwareMinor() > v_minor)
                    {
                        return true;
                    }
                    else if (GetFirmwareMinor() == v_minor)
                    {
                        if (GetFirmwareInternal() >= v_internal)
                            return true;
                    }
                }
            }
            return false;
        }
        public double GetFirmwareMajor()
        {
            return FirmwareMajor;
        }
        public double GetFirmwareMinor()
        {
            return FirmwareMinor;
        }

        public virtual void StopStreaming()
        {

            WriteBytes(new byte[1] { (byte)PacketTypeShimmer2.STOP_STREAMING_COMMAND }, 0, 1);
            System.Threading.Thread.Sleep(200);
            FlushInputConnection();
            ObjectClusterBuffer.Clear();
            if (IsConnectionOpen())
            {
                SetState(SHIMMER_STATE_CONNECTED);
            }
        }

        public int GetPacketBufferSize()
        {
            return ObjectClusterBuffer.Count;
        }

        public String GetDeviceName()
        {
            return DeviceName;
        }

        public int GetShimmerVersion()
        {
            return HardwareVersion;
        }

        public double GetSamplingRate()
        {
            return SamplingRate;
        }

        public long GetEnabledSensors()
        {
            return EnabledSensors;
        }

        public int GetMagRange()
        {
            return MagGain;
        }

        protected void SetMagRange(int range)
        {
            MagGain = range;
        }

        public int GetBlinkLED()
        {
            return CurrentLEDStatus;
        }

        public int GetAccelHRBit()
        {
            return AccelHRBit;
        }
        public int GetAccelLPBit()
        {
            return AccelLPBit;
        }
        public int GetMpu9150AccelRange()
        {
            return Mpu9150AccelRange;
        }
        public int GetGyroSamplingRate()
        {
            return GyroSamplingRate;
        }

        public int GetAccelRange()
        {
            return AccelRange;
        }

        public int GeAltMagRate()
        {
            return AltMagSamplingRate;
        }

        public int GetAltMagRange()
        {
            return AltMagRange;
        }

        public int GetLNAccelRange()
        {
            return LNAccelRange;
        }

        protected void SetAccelRange(int range)
        {
            AccelRange = range;
        }
        protected void SetLNAccelRange(int range)
        {
            LNAccelRange = range;
        }

        public int GetGyroRange()
        {
            return GyroRange;
        }

        protected void SetGyroRange(int range)
        {
            GyroRange = range;
        }

        public int GetGSRRange()
        {
            return GSRRange;
        }

        protected void SetGSRRange(int range)
        {
            GSRRange = range;
        }

        public int GetMagSamplingRate()
        {
            return magSamplingRate;
        }

        protected void SetMagSamplingRate(int rate)
        {
            magSamplingRate = rate;
        }

        protected void SetAccelSamplingRate(int rate)
        {
            AccelSamplingRate = rate;
        }

        public int GetAccelSamplingRate()
        {
            return AccelSamplingRate;
        }

        public int GetInternalExpPower()
        {
            return InternalExpPower;
        }

        protected void SetInternalExpPower(int value)
        {
            InternalExpPower = value;
        }

        public int GetPressureResolution()
        {
            return PressureResolution;
        }

        protected void SetPressureResolution(int setting)
        {
            PressureResolution = setting;
        }

        protected void SetVReg(bool val)
        {
            if (val)
            {
                ConfigSetupByte0 |= (int)ConfigSetupByte0Bitmap.Config5VReg;
            }
            else
            {
                ConfigSetupByte0 &= ~(int)ConfigSetupByte0Bitmap.Config5VReg;
            }
        }

        public bool GetVReg()
        {
            if ((ConfigSetupByte0 & (int)ConfigSetupByte0Bitmap.Config5VReg) == 0)
            {
                return false;
            }
            else
            {
                return true;
            }
        }

        protected void SetConfigSetupByte0(int val)
        {
            ConfigSetupByte0 = val;
        }

        public int GetConfigSetupByte0()
        {
            return (int)ConfigSetupByte0;
        }

        public byte[] GetEXG1RegisterContents()
        {
            return Exg1RegArray;
        }
        /// <summary>
        /// Where index starts at 0
        /// </summary>
        /// <param name="index"></param>
        /// <returns></returns>
        public byte GetEXG1RegisterByte(int index)
        {
            return Exg1RegArray[index];
        }

        public byte[] GetEXG2RegisterContents()
        {
            return Exg2RegArray;
        }

        public byte GetEXG2RegisterByte(int index)
        {
            return Exg2RegArray[index];
        }

        protected void SetEXG1RegisterContents(byte[] exgReg)
        {
            for (int i = 0; i < exgReg.Length; i++)
            {
                Exg1RegArray[i] = exgReg[i];
            }
        }

        protected void SetEXG2RegisterContents(byte[] exgReg)
        {
            for (int i = 0; i < exgReg.Length; i++)
            {
                Exg2RegArray[i] = exgReg[i];
            }
        }

        protected void SetPMux(bool val)
        {
            if (val)
            {
                ConfigSetupByte0 |= (int)ConfigSetupByte0Bitmap.ConfigPMux;
            }
            else
            {
                ConfigSetupByte0 &= ~(int)ConfigSetupByte0Bitmap.ConfigPMux;
            }
        }

        public bool GetPMux()
        {
            //return pMux;
            if ((ConfigSetupByte0 & (int)ConfigSetupByte0Bitmap.ConfigPMux) == 0)
            {
                return false;
            }
            else
            {
                return true;
            }
        }

        protected void SetBaudRate(int baud)
        {
            BaudRate = baud;
        }

        public int GetBaudRate()
        {
            return BaudRate;
        }

        public int GetExpansionBoardID()
        {
            return ExpansionBoardId;
        }

        public String GetExpansionBoard()
        {
            return ExpansionBoard;
        }

        public void Set3DOrientation(bool value)
        {
            Orientation3DEnabled = value;
        }

        public bool Is3DOrientationEnabled()
        {
            return Orientation3DEnabled;
        }

        public void SetGyroOnTheFlyCalibration(Boolean enable, int bufferSize, double threshold)
        {
            EnableGyroOnTheFlyCalibration = enable;
            if (enable)
            {
                ListSizeGyroOnTheFly = bufferSize;
                ThresholdGyroOnTheFly = threshold;
            }
        }

        public bool IsGyroOnTheFlyCalEnabled()
        {
            return EnableGyroOnTheFlyCalibration;
        }

        public string GetFirmwareVersionFullName()
        {
            return FirmwareVersionFullName;
        }

        public double GetFirmwareIdentifier()
        {
            return FirmwareIdentifier;
        }

        public int GetFirmwareInternal()
        {
            return FirmwareInternal;
        }

        public int GetCompatibilityCode()
        {
            return CompatibilityCode;
        }

        protected void UpdateBasedOnCompatibilityCode()
        {
            if (CompatibilityCode < 6)
            {
                TimeStampPacketByteSize = 2;
                TimeStampPacketRawMaxValue = 65536;// 16777216 or 65536 
            }
            else if (CompatibilityCode >= 6)
            {
                TimeStampPacketByteSize = 3;
                TimeStampPacketRawMaxValue = 16777216;// 16777216 or 65536 
            }
        }
        /// <summary>
        /// 
        /// </summary>
        /// <returns></returns>
        /// <exception cref="Exception">If device is not connected/streaming</exception>
        public bool IsCRCSupported()
        {
            if (GetState() == SHIMMER_STATE_CONNECTED ||
                GetState() == SHIMMER_STATE_STREAMING ||
                GetState() == SHIMMER_STATE_CONNECTING)
            {
                if (GetCompatibilityCode() >= 8)
                {
                    return true;
                }
            }
            else
            {
                throw new Exception("Device needs to be connected, connecting or streaming");
            }
            return false;
        }

        /// <summary>
        /// 
        /// </summary>
        /// <returns></returns>
        /// <exception cref="Exception">If device is not connected/streaming</exception>
        public bool IsGetRadioVersionSupported()
        {
            if (GetState() == SHIMMER_STATE_CONNECTED ||
                GetState() == SHIMMER_STATE_STREAMING ||
                GetState() == SHIMMER_STATE_CONNECTING)
            {
                if (GetCompatibilityCode() >= 8)
                {
                    return true;
                }
            }
            else
            {
                throw new Exception("Device needs to be connected, connecting or streaming");
            }
            return false;
        }

        public bool IsTwoByteStatusResponseSupported()
        {
            if (HardwareVersion == (int)ShimmerVersion.SHIMMER3R)
            {
                if (FirmwareIdentifier == ShimmerBluetooth.FW_IDENTIFIER_LOGANDSTREAM)   //LogAndStream
                {
                    if (compareVersions(1, 0, 24))
                    {
                        return true;
                    }
                }
            }
            return false;
        }

        protected void SetCompatibilityCode()
        {
            CompatibilityCode = 0;
            if (HardwareVersion == (int)ShimmerVersion.SHIMMER3)
            {
                if (FirmwareIdentifier == ShimmerBluetooth.FW_IDENTIFIER_BTSTREAM)    //BtStream //Also Used For EXG MD, 9/2/2018: No longer used for newer ECGMD fw versions
                {
                    if (compareVersions(0, 8))//(FirmwareVersion >= 0.8)
                    {
                        CompatibilityCode = 7;
                    }
                    else if (compareVersions(0, 7, 3))//(FirmwareVersion >= 0.7 && FirmwareInternal>2)
                    {
                        CompatibilityCode = 7; //skip CompatibilityCode = 6 since functionality of code 6 and 7 was introduced at the same time 
                    }
                    else if (compareVersions(0, 5) && (!compareVersions(0, 7, 3)))//(FirmwareVersion >= 0.5 && (FirmwareVersion <= 0.7 && FirmwareInternal <= 2))
                    {
                        CompatibilityCode = 5;
                    }
                    else if (compareVersions(0, 4))//FirmwareVersion == 0.4)
                    {
                        CompatibilityCode = 4;
                    }
                    else if (compareVersions(0, 3))//FirmwareVersion == 0.3)
                    {
                        CompatibilityCode = 3;
                    }
                    else if (compareVersions(0, 2))//FirmwareVersion == 0.2)
                    {
                        CompatibilityCode = 2;
                    }
                    else if (compareVersions(0, 1))//FirmwareVersion == 0.1)
                    {
                        CompatibilityCode = 1;
                    }
                }
                else if (FirmwareIdentifier == ShimmerBluetooth.FW_IDENTIFIER_LOGANDSTREAM)   //LogAndStream
                {
                    if (compareVersions(0, 16, 6)) //(FirmwareVersion >= 0.16.6)
                    {
                        CompatibilityCode = 9;
                    }
                    else if (compareVersions(0, 13, 7)) //(FirmwareVersion >= 0.13.7)
                    {
                        CompatibilityCode = 8;
                    }
                    else if (compareVersions(0, 5, 4) || compareVersions(0, 6))//(FirmwareVersion >= 0.5 && FirmwareInternal >= 4  || FirmwareVersion == 0.6)
                    {
                        CompatibilityCode = 6;
                    }
                    else if ((compareVersions(0, 3)) && (!compareVersions(0, 5)))//(FirmwareVersion >= 0.3 && FirmwareVersion<0.5)
                    {
                        CompatibilityCode = 5;
                    }
                    else if (compareVersions(0, 2)) //(FirmwareVersion == 0.2)
                    {
                        CompatibilityCode = 4;
                    }
                    else if (compareVersions(0, 1)) //(FirmwareVersion == 0.1)
                    {
                        CompatibilityCode = 3;
                    }
                    else
                    {
                        CompatibilityCode = 6;
                    }
                }
                else if (FirmwareIdentifier == ShimmerBluetooth.FW_IDENTIFIER_SHIMMERECGMD)
                {
                    CompatibilityCode = 7;
                }
            }
            else if (HardwareVersion == (int)ShimmerVersion.SHIMMER2R)
            {
                if (FirmwareIdentifier == 1)    //BtStream
                {
                    if (compareVersions(1, 2))//FirmwareVersion == 1.2)
                    {
                        CompatibilityCode = 1;
                    }
                }
            }
            else if (HardwareVersion == (int)ShimmerVersion.SHIMMER3R)
            {
                if (FirmwareIdentifier == ShimmerBluetooth.FW_IDENTIFIER_LOGANDSTREAM)   //LogAndStream
                {
                    CompatibilityCode = 8;
                }
            }
        }

        protected void SetState(int state)
        {
            if (ShimmerState == SHIMMER_STATE_CONNECTED)
            {

            }

            Boolean stateChanging = false;
            if (ShimmerState != state)
            {
                stateChanging = true;
            }

            ShimmerState = state;
            /*EventHandler handler = UICallback;
            if (handler != null)
            {
                CustomEventArgs newEventArgs = new CustomEventArgs((int)ShimmerIdentifier.MSG_IDENTIFIER_STATE_CHANGE, (object)state);
                handler(this, newEventArgs);
            }
             */
            if (stateChanging)
            {
                CustomEventArgs newEventArgs = new CustomEventArgs((int)ShimmerIdentifier.MSG_IDENTIFIER_STATE_CHANGE, (object)state);
                OnNewEvent(newEventArgs);
            }
        }

        public int GetState()
        {
            return ShimmerState;
        }

        public String GetStateString()
        {
            if (ShimmerState == SHIMMER_STATE_CONNECTED)
            {
                return "Connected";
            }
            else if (ShimmerState == SHIMMER_STATE_CONNECTING)
            {
                return "Connecting";
            }
            else if (ShimmerState == SHIMMER_STATE_STREAMING)
            {
                return "Streaming";
            }
            else
            {
                return "None";
            }
        }

        /// <summary>
        /// ToggleLED (Shimmer2R/Shimmer3)
        /// </summary>
        public void ToggleLED()
        {
            if (IsConnectionOpen())
            {
                WriteBytes(new byte[1] { (byte)PacketTypeShimmer2.TOGGLE_LED_COMMAND }, 0, 1);
            }
        }

        /// <summary>
        /// Read Sampling rate
        /// </summary>
        public void ReadSamplingRate()
        {
            WriteBytes(new byte[1] { (byte)PacketTypeShimmer2.GET_SAMPLING_RATE_COMMAND }, 0, 1);
            System.Threading.Thread.Sleep(200);
        }

        /// <summary>
        /// Read Magnetometer range (Shimmer2R/Shimmer3)
        /// </summary>
        public void ReadMagRange()
        {
            WriteBytes(new byte[1] { (byte)PacketTypeShimmer2.GET_MAG_GAIN_COMMAND }, 0, 1);
            System.Threading.Thread.Sleep(200);
        }

        /// <summary>
        /// Read Accelerometer Range (Shimmer2R/Shimmer3 Wide Range Accel)
        /// </summary>
        public void ReadAccelRange()
        {
            WriteBytes(new byte[1] { (byte)PacketTypeShimmer2.GET_ACCEL_RANGE_COMMAND }, 0, 1);
            System.Threading.Thread.Sleep(200);
        }

        /// <summary>
        /// Read Accelerometer Range (Shimmer2R/Shimmer3 Wide Range Accel)
        /// </summary>
        protected void ReadLNAccelRange()
        {
            WriteBytes(new byte[1] { (byte)PacketTypeShimmer3RSDBT.GET_ALT_ACCEL_RANGE_COMMAND }, 0, 1);
            System.Threading.Thread.Sleep(200);
        }

        /// <summary>
        /// Read Gyro Range (Shimmer3)
        /// </summary>
        public void ReadGyroRange()
        {
            WriteBytes(new byte[1] { (byte)PacketTypeShimmer3.GET_MPU9150_GYRO_RANGE_COMMAND }, 0, 1);
            System.Threading.Thread.Sleep(200);
        }

        /// <summary>
        /// Read GSR Range (Shimmer2r/Shimmer3)
        /// </summary>
        public void ReadGSRRange()
        {
            WriteBytes(new byte[1] { (byte)PacketTypeShimmer2.GET_GSR_RANGE_COMMAND }, 0, 1);
            System.Threading.Thread.Sleep(500);
        }

        /// <summary>
        /// Read Mag rate (Shimmer2r/Shimmer3)
        /// </summary>
        public void ReadMagSamplingRate()
        {
            WriteBytes(new byte[1] { (byte)PacketTypeShimmer2.GET_MAG_SAMPLING_RATE_COMMAND }, 0, 1);
            System.Threading.Thread.Sleep(200);
        }

        /// <summary>
        /// Read Accel Rate (Shimmer3)
        /// </summary>
        public void ReadAccelSamplingRate()
        {
            if (HardwareVersion == (int)ShimmerVersion.SHIMMER3)
            {
                WriteBytes(new byte[1] { (byte)PacketTypeShimmer3.GET_ACCEL_SAMPLING_RATE_COMMAND }, 0, 1);
                System.Threading.Thread.Sleep(200);
            }
        }

        /// <summary>
        /// Read Accel Calibration (Shimmer2R/Shimmer3)
        /// </summary>
        public void ReadAccelCalibration()
        {
            WriteBytes(new byte[1] { (byte)PacketTypeShimmer2.GET_ACCEL_CALIBRATION_COMMAND }, 0, 1);
            System.Threading.Thread.Sleep(500);
        }

        /// <summary>
        /// Read Gyro Calibration (Shimmer2R/Shimmer3)
        /// </summary>
        public void ReadGyroCalibration()
        {
            WriteBytes(new byte[1] { (byte)PacketTypeShimmer2.GET_GYRO_CALIBRATION_COMMAND }, 0, 1);
            System.Threading.Thread.Sleep(500);
        }

        /// <summary>
        /// ReadMagCalibration(Shimmer2R/Shimmer3)
        /// </summary>
        public void ReadMagCalibration()
        {
            WriteBytes(new byte[1] { (byte)PacketTypeShimmer2.GET_MAG_CALIBRATION_COMMAND }, 0, 1);
            System.Threading.Thread.Sleep(500);
        }

        /// <summary>
        /// Read internal Exp power (Shimmer3)
        /// </summary>
        public void ReadInternalExpPower()
        {
            WriteBytes(new byte[1] { (byte)PacketTypeShimmer3.GET_INTERNAL_EXP_POWER_ENABLE_COMMAND }, 0, 1);
            System.Threading.Thread.Sleep(200);
        }

        /// <summary>
        /// Read blink led (Shimmer2R/Shimmer3)
        /// </summary>
        public void ReadBlinkLED()
        {
            WriteBytes(new byte[1] { (byte)PacketTypeShimmer2.GET_BLINK_LED }, 0, 1);
            System.Threading.Thread.Sleep(200);
        }

        public void ReadBattery()
        {
            WriteBytes(new byte[1] { (byte)PacketTypeShimmer3.GET_VBATT_COMMAND }, 0, 1);
            System.Threading.Thread.Sleep(200);
        }

        /// <summary>
        /// Read ConfigByte0 (Shimmer3/Shimmer2r)
        /// </summary>
        public void ReadConfigByte0()
        {
            WriteBytes(new byte[1] { (byte)PacketTypeShimmer2.GET_CONFIG_SETUP_BYTE0_COMMAND }, 0, 1);
            System.Threading.Thread.Sleep(200);
        }

        /// <summary>
        /// Read Baud Rate (Shimmer3)
        /// Baud Rate change only supported in BtStream v0.4.0 or later and LogAndStream v0.3.0 or later
        /// </summary>
        public void ReadBaudRate()
        {
            if (HardwareVersion == (int)ShimmerVersion.SHIMMER3 && CompatibilityCode >= 4)
            {
                WriteBytes(new byte[1] { (byte)PacketTypeShimmer3.GET_BAUD_RATE_COMMAND }, 0, 1);
                System.Threading.Thread.Sleep(200);
            }
        }

        /// <summary>
        /// Read Expansion board (Shimmer3)
        /// Detect Expansion Board only supported in BtStream v0.4.0 or later and LogAndStream v0.3.0 or later
        /// </summary>
        public void ReadExpansionBoard()
        {
            if ((HardwareVersion == (int)ShimmerVersion.SHIMMER3 && CompatibilityCode >= 4)
                || HardwareVersion == (int)ShimmerVersion.SHIMMER3R)
            {
                WriteBytes(new byte[3] { (byte)PacketTypeShimmer3.GET_EXPANSION_BOARD_COMMAND, 3, 0 }, 0, 3);
                System.Threading.Thread.Sleep(200);
            }
        }

        /// <summary>
        /// Read calibration parameters (Shimmer2R/Shimmer3)
        /// </summary>
        /// <param name="sensors"></param>
        public void ReadCalibrationParameters(String sensors)
        {
            if (String.Equals(sensors, "All", StringComparison.Ordinal))
            {
                WriteBytes(new byte[1] { (byte)PacketTypeShimmer2.GET_ALL_CALIBRATION_COMMAND }, 0, 1);
                System.Threading.Thread.Sleep(300);
            }
            else if (String.Equals(sensors, "Accelerometer", StringComparison.Ordinal))
            {
                WriteBytes(new byte[1] { (byte)PacketTypeShimmer2.GET_ACCEL_CALIBRATION_COMMAND }, 0, 1);
                System.Threading.Thread.Sleep(300);
            }
            else if (String.Equals(sensors, "Gyroscope", StringComparison.Ordinal))
            {
                WriteBytes(new byte[1] { (byte)PacketTypeShimmer2.GET_GYRO_CALIBRATION_COMMAND }, 0, 1);
                System.Threading.Thread.Sleep(300);
            }
            else if (String.Equals(sensors, "Magnetometer", StringComparison.Ordinal))
            {
                WriteBytes(new byte[1] { (byte)PacketTypeShimmer2.GET_MAG_CALIBRATION_COMMAND }, 0, 1);
                System.Threading.Thread.Sleep(300);
            }
        }

        /// <summary>
        /// Get pressure calibration coefficients
        /// </summary>
        public void ReadPressureCalibrationCoefficients()
        {
            if (HardwareVersion == (int)ShimmerVersion.SHIMMER3)
            {
                if (FirmwareIdentifier == FW_IDENTIFIER_LOGANDSTREAM && CompatibilityCode > 8)
                {
                    WriteBytes(new byte[1] { (byte)PacketTypeShimmer3RSDBT.GET_PRESSURE_CALIBRATION_COEFFICIENTS_COMMAND }, 0, 1);
                    System.Threading.Thread.Sleep(800);
                }
                else if (isShimmer3withUpdatedSensors())
                {
                    WriteBytes(new byte[1] { (byte)PacketTypeShimmer3.GET_BMP280_CALIBRATION_COEFFICIENTS_COMMAND }, 0, 1);
                    System.Threading.Thread.Sleep(800);
                }
                else
                {
                    if (CompatibilityCode > 1)
                    {
                        WriteBytes(new byte[1] { (byte)PacketTypeShimmer3.GET_BMP180_CALIBRATION_COEFFICIENTS_COMMAND }, 0, 1);
                        System.Threading.Thread.Sleep(800);
                    }
                }
            }
            else if (HardwareVersion == (int)ShimmerVersion.SHIMMER3R)
            {
                WriteBytes(new byte[1] { (byte)PacketTypeShimmer3RSDBT.GET_PRESSURE_CALIBRATION_COEFFICIENTS_COMMAND }, 0, 1);
                System.Threading.Thread.Sleep(800);
            }
        }

        /// <summary>
        /// Read the EXG Configuration of the EXG daughter Board (Shimmer3), chipid = 1 or chipid = 2
        /// </summary>
        /// <param name="chipID">chipID = 1 or chipID = 2</param>
        public void ReadEXGConfigurations(int chipID)
        {
            //System.Console.WriteLine("EXG" + chipID);
            ChipID = chipID;
            //if ((FirmwareIdentifier == 1 && FirmwareVersion >= 0.3) || FirmwareIdentifier == 3)
            if (CompatibilityCode >= 3)
            {
                if (ChipID == 1 || ChipID == 2)
                {
                    System.Threading.Thread.Sleep(300);
                    WriteBytes(new byte[4] { (byte)PacketTypeShimmer3.GET_EXG_REGS_COMMAND, (byte)(ChipID - 1), 0, 10 }, 0, 4);
                    System.Threading.Thread.Sleep(300);
                }
            }
        }

        public void ReadShimmerName()
        {
            if (HardwareVersion == (int)ShimmerVersion.SHIMMER3 && CompatibilityCode >= 4)
            {
                WriteBytes(new byte[1] { (byte)ShimmerBluetooth.PacketTypeShimmer3SDBT.GET_SHIMMERNAME_COMMAND }, 0, 1);
                System.Threading.Thread.Sleep(300);
            }
        }

        public void ReadExpID()
        {
            WriteBytes(new byte[1] { (byte)ShimmerBluetooth.PacketTypeShimmer3SDBT.GET_EXPID_COMMAND }, 0, 1);
            System.Threading.Thread.Sleep(300);
        }

        public void ReadConfigTime()
        {
            WriteBytes(new byte[1] { (byte)ShimmerBluetooth.PacketTypeShimmer3SDBT.GET_CONFIGTIME_COMMAND }, 0, 1);
            System.Threading.Thread.Sleep(300);
        }

        public void Inquiry()
        {
            WriteBytes(new byte[1] { (byte)PacketTypeShimmer2.INQUIRY_COMMAND }, 0, 1);
            System.Threading.Thread.Sleep(200);
        }

        public BTCRCMode GetCRCMode()
        {
            return BluetoothCRCMode;
        }

        public void WriteCRCMode(BTCRCMode mode)
        {
            if (IsCRCSupported())
            {
                WriteBytes(new byte[2] { (byte)InstructionsSet.SetCrcCommand, ((byte)mode) }, 0, 2);
                System.Threading.Thread.Sleep(300);
                BluetoothCRCMode = mode;
            }
            else
            {
                throw new Exception("CRC not supported on this firmware");
            }
        }

        /// <summary>
        /// Rate is set to Hz. Note that when using shimmer ecg/emg, setting the sampling rate also update the configuration bytes on the exg chip. Because of this you should always try to use writesamplingrate after a command such as WriteEXGConfigurations. Unless you are certain you are setting the correct data rate via WriteEXGConfigurations.
        /// </summary>
        /// <param name="rate"></param>
        public void WriteSamplingRate(double rate)
        {
            SamplingRate = rate;
            if (!(HardwareVersion == (int)ShimmerVersion.SHIMMER3) && !(HardwareVersion == (int)ShimmerVersion.SHIMMER3R))
            {
                rate = 1024 / rate; //the equivalent hex setting

                WriteBytes(new byte[2] { (byte)PacketTypeShimmer2.SET_SAMPLING_RATE_COMMAND, (byte)Math.Round(rate) }, 0, 2);
            }
            else
            {
                int samplingByteValue = (int)(32768 / rate);
                WriteBytes(new byte[3] { (byte)PacketTypeShimmer2.SET_SAMPLING_RATE_COMMAND, (byte)(samplingByteValue & 0xFF), (byte)((samplingByteValue >> 8) & 0xFF) }, 0, 3);
            }
            System.Threading.Thread.Sleep(200);

            //now check to see that the internal sensor rates are close to the sampling rate value
            if (GetFirmwareIdentifier() != FW_IDENTIFIER_SHIMMERECGMD)
            {
                SetLowPowerMag(LowPowerMagEnabled);
            }
            if ((HardwareVersion == (int)ShimmerVersion.SHIMMER3) || (HardwareVersion == (int)ShimmerVersion.SHIMMER3R))
            {
                if (GetFirmwareIdentifier() != FW_IDENTIFIER_SHIMMERECGMD)
                {
                    SetLowPowerGyro(LowPowerGyroEnabled);
                    SetLowPowerAccel(LowPowerAccelEnabled);
                }
                if (SamplingRate <= 125)
                {
                    WriteEXGRate(0);
                }
                else if (SamplingRate <= 250)
                {
                    WriteEXGRate(1);
                }
                else if (SamplingRate <= 500)
                {
                    WriteEXGRate(2);
                }
                else if (SamplingRate <= 1000)
                {
                    WriteEXGRate(3);
                }
                else if (SamplingRate <= 2000)
                {
                    WriteEXGRate(4);
                }
                else if (SamplingRate <= 4000)
                {
                    WriteEXGRate(5);
                }
                else
                {
                    WriteEXGRate(6);
                }

            }
            System.Threading.Thread.Sleep(200);
        }

        /// <summary>
        /// Write Mag Range For Shimmer2R and Shimmer3. Shimmer2R: 0,1,2,3,4,5,6 = 0.7,1.0,1.5,2.0,3.2,3.8,4.5 . 
        /// Shimmer3: 1,2,3,4,5,6,7 = 1.3, 1.9, 2.5, 4.0, 4.7, 5.6, 8.1
        /// Not supported for updated Shimmer3s which are using LSM303AHTR and Shimmer3Rs using LIS2MDL
        /// To determine if your Shimmer 3 device is using updated sensors please see isShimmer3withUpdatedSensors
        /// </summary>
        /// <param name="range">0-6 for Shimmer2R and 1-7 for Shimmer3</param>
        public void WriteMagRange(int range)
        {
            if (FirmwareVersionFullName.Equals("BoilerPlate 0.1.0"))
            {
            }
            else
            {
                if (isShimmer3withUpdatedSensors() && HardwareVersion == (int)ShimmerBluetooth.ShimmerVersion.SHIMMER3)
                {

                }
                else
                {
                    WriteBytes(new byte[2] { (byte)PacketTypeShimmer2.SET_MAG_GAIN_COMMAND, (byte)range }, 0, 2);
                    System.Threading.Thread.Sleep(250);
                    MagGain = range;
                }
            }
        }


        /// <summary>
        /// This is used to set the gyro range on the Shimmer3. Options are 0,1,2,3. Where 0 = 250 Degree/s, 1 = 500 Degree/s, 2 = 1000 Degree/s, 3 = 2000 Degree/s
        /// </summary>
        /// <param name="range">Range between 0 and 3</param>
        public void WriteGyroRange(int range)
        {
            if (HardwareVersion == (int)ShimmerVersion.SHIMMER3 || HardwareVersion == (int)ShimmerVersion.SHIMMER3R)
            {
                WriteBytes(new byte[2] { (byte)PacketTypeShimmer3.SET_MPU9150_GYRO_RANGE_COMMAND, (byte)range }, 0, 2);
                System.Threading.Thread.Sleep(250);
                GyroRange = range;
            }
        }
        /// <summary>
        /// This is used to set the GSR Range for both Shimmer2R and Shimmer3. Range is between 0 and 4. 0 = 8-63kOhm, 1 = 63-220kOhm, 2 = 220-680kOhm, 3 = 680kOhm-4.7MOhm, 4 = Auto range
        /// </summary>
        /// <param name="range"> Range between 0 and 4</param>
        public void WriteGSRRange(int range)
        {
            WriteBytes(new byte[2] { (byte)PacketTypeShimmer2.SET_GSR_RANGE_COMMAND, (byte)range }, 0, 2);
            GSRRange = range;
            System.Threading.Thread.Sleep(200);
        }
        /// <summary>
        /// This is used to set the accel range of the Shimmer2r and the range of the Wide Range Accel of the Shimmer3 device. 
        /// Shimmer2r options - 0,1,2,3 = 1.5g,2g,4g,6g. 
        /// Shimmer3 options - 0,1,2,3 = 2g,4g,8g,16g. 
        /// Shimmer3 with updated sensor options - 0,1,2,3 = 2g,16g,4g,8g. 
        /// To determine if your Shimmer 3 device is using updated sensors please see isShimmer3withUpdatedSensors
        /// </summary>
        /// <param name="range">A value between 0-3</param>
        public void WriteAccelRange(int range)
        {
            WriteBytes(new byte[2] { (byte)PacketTypeShimmer2.SET_ACCEL_RANGE_COMMAND, (byte)range }, 0, 2);
            System.Threading.Thread.Sleep(250);
            AccelRange = range;
        }

        public void WriteLNAccelRange(int range)
        {
            if (HardwareVersion == (int)ShimmerBluetooth.ShimmerVersion.SHIMMER3R)
            {
                WriteBytes(new byte[2] { (byte)PacketTypeShimmer3RSDBT.SET_ALT_ACCEL_RANGE_COMMAND, (byte)range }, 0, 2);
                System.Threading.Thread.Sleep(250);
                LNAccelRange = range;
            }
            else
            {
                throw new Exception("Not Supported for this Hardware Type");
            }
        }
        public void WriteAltMagRate(int rate)
        {
            if (HardwareVersion == (int)ShimmerBluetooth.ShimmerVersion.SHIMMER3R)
            {
                WriteBytes(new byte[2] { (byte)PacketTypeShimmer3RSDBT.SET_ALT_MAG_SAMPLING_RATE_COMMAND, (byte)rate }, 0, 2);
                System.Threading.Thread.Sleep(250);
                AltMagSamplingRate = rate;
            }
            else
            {
                throw new Exception("Not Supported for this Hardware Type");
            }
        }

        public void WriteAltMagRange(int range)
        {
            if (HardwareVersion == (int)ShimmerBluetooth.ShimmerVersion.SHIMMER3R)
            {
                WriteBytes(new byte[2] { (byte)PacketTypeShimmer2.SET_MAG_GAIN_COMMAND, (byte)range }, 0, 2);
                System.Threading.Thread.Sleep(250);
                AltMagRange = range;
            }
            else
            {
                throw new Exception("Not Supported for this Hardware Type");
            }
        }

        public void WriteBlinkLED(int value)
        {
            WriteBytes(new byte[2] { (byte)ShimmerBluetooth.PacketTypeShimmer2.SET_BLINK_LED, (byte)value }, 0, 2);
            CurrentLEDStatus = value;
        }

        /// <summary>
        /// This is to set the pressure resolution of the BMP180 Pressure sensor on the Shimmer3. There are four different settings 0,1,2,3 with 0 being the lowest resolution and 3 the highest.
        /// This is to set the pressure resolution of the BMP390 Pressure sensor on the Shimmer3R. There are four different settings 0,1,2,3,4,5 with 0 being the lowest resolution and 5 the highest.
        /// </summary>
        /// <param name="setting">A value between 0 and 3, 3 being highest resolution and 0 lowest for Shimmer3, and 0-5 for Shimmer3R</param>
        public void WritePressureResolution(int setting)
        {
            if (HardwareVersion == (int)ShimmerVersion.SHIMMER3 || HardwareVersion == (int)ShimmerVersion.SHIMMER3R)
            {
                WriteBytes(new byte[2] { (byte)PacketTypeShimmer3.SET_BMP180_PRES_RESOLUTION_COMMAND, (byte)setting }, 0, 2);
                PressureResolution = setting;
                System.Threading.Thread.Sleep(200);
            }
        }

        protected void WriteMagSamplingRate(int rate)
        {
            if (FirmwareVersionFullName.Equals("BoilerPlate 0.1.0"))
            {
            }
            else
            {
                mTempIntValue = rate;
                magSamplingRate = rate;
                WriteBytes(new byte[2] { (byte)PacketTypeShimmer2.SET_MAG_SAMPLING_RATE_COMMAND, (byte)rate }, 0, 2);
                System.Threading.Thread.Sleep(200);
            }
        }

        protected void WriteWRAccelSamplingRate(int rate)
        {
            if (HardwareVersion == (int)ShimmerVersion.SHIMMER3 || HardwareVersion == (int)ShimmerVersion.SHIMMER3R)
            {
                AccelSamplingRate = rate;
                mTempIntValue = rate;
                WriteBytes(new byte[2] { (byte)PacketTypeShimmer3.SET_ACCEL_SAMPLING_RATE_COMMAND, (byte)rate }, 0, 2);
                System.Threading.Thread.Sleep(200);
            }
        }

        protected void WriteGyroSamplingRate(int rate)
        {
            if (HardwareVersion == (int)ShimmerVersion.SHIMMER3 || HardwareVersion == (int)ShimmerVersion.SHIMMER3R)
            {
                GyroSamplingRate = rate;
                mTempIntValue = rate;
                WriteBytes(new byte[2] { (byte)PacketTypeShimmer3.SET_MPU9150_SAMPLING_RATE_COMMAND, (byte)rate }, 0, 2);
                System.Threading.Thread.Sleep(200);
            }
        }
        /// <summary>
        /// This is only used for Shimmer3, and can be used to power the PPG sensor. See GSR+ board and PPG Sensor.
        /// </summary>
        /// <param name="expPower">Set to 1 to enable and 0 to disable</param>
        public void WriteInternalExpPower(int expPower)
        {
            WriteBytes(new byte[2] { (byte)PacketTypeShimmer3.SET_INTERNAL_EXP_POWER_ENABLE_COMMAND, (byte)expPower }, 0, 2);
            System.Threading.Thread.Sleep(200);
            InternalExpPower = expPower;
        }

        /// <summary>
        /// This is only applicable for Shimmer2R
        /// </summary>
        /// <param name="value">Set to 1 to enable and 0 to disable</param>
        public void Write5VReg(int value)
        {
            if (value == 1)
            {
                SetVReg(true);
            }
            else
            {
                SetVReg(false);
            }
            WriteBytes(new byte[2] { (byte)PacketTypeShimmer2.SET_5V_REGULATOR_COMMAND, (byte)value }, 0, 2);
            System.Threading.Thread.Sleep(200);
        }

        /// <summary>
        /// This is used to enable or disable the power multiplexer on the Shimmer2r. This should be enabled to monitor the battery voltage on the Shimmer2r via A0 (VSenseReg) and A7(VSenseBatt).
        /// </summary>
        /// <param name="value">Set to 1 to enable and 0 to disable</param>
        public void WritePMux(int value)
        {
            if (value == 1)
            {
                SetPMux(true);
            }
            else
            {
                SetPMux(false);
            }
            WriteBytes(new byte[2] { (byte)PacketTypeShimmer2.SET_POWER_MUX_COMMAND, (byte)value }, 0, 2);
            System.Threading.Thread.Sleep(200);
        }
        /// <summary>
        /// This sets the tx buffer size of BTStream, currently C# API only supports buffersize=1
        /// </summary>
        /// <param name="size"></param>
        public void WriteBufferSize(int size)
        {
            WriteBytes(new byte[2] { (byte)PacketTypeShimmer2.SET_BUFFER_SIZE_COMMAND, (byte)size }, 0, 2);
            System.Threading.Thread.Sleep(200);
        }

        /// <summary>
        /// This is used to set the baud rate range on the Shimmer3. Options are 0..10 where 0 = 115200, 1 = 1200, 2 = 2400, 3 = 4800, 4 = 9600, 5 = 19200, 6 = 38400, 7 = 57600, 8 = 230400, 9 = 460800, 10 = 921600
        /// Baud Rate change only supported in BtStream v0.4.0 or later and LogAndStream v0.3.0 or later
        /// </summary>
        /// <param name="baud">Baud between 0 and 10</param>
        public void WriteBaudRate(int baud)
        {
            if (HardwareVersion == (int)ShimmerVersion.SHIMMER3 && CompatibilityCode >= 4)
            {
                WriteBytes(new byte[2] { (byte)PacketTypeShimmer3.SET_BAUD_RATE_COMMAND, (byte)baud }, 0, 2);
                System.Threading.Thread.Sleep(200);
                BaudRate = baud;
            }
        }

        /// <summary>
        /// This is used to set the battery frequency on the Shimmer3. It takes a 4 byte argument (little endian), that tells the shimmer to sample the battery after that many data packets
        /// Battery frequency change only supported in BtStream v0.8.0 or later and LogAndStream v0.7.0 or later but it is not still handled by the API so we set it to 0
        /// </summary>
        /// <param name="freq">Frequency</param>
        [Obsolete("This method is deprecated and no longer supported")]
        public void WriteBatteryFrequency(int freq)
        {
            if (HardwareVersion == (int)ShimmerVersion.SHIMMER3 && ((FirmwareIdentifier == FW_IDENTIFIER_LOGANDSTREAM && CompatibilityCode > 6) || (FirmwareIdentifier == FW_IDENTIFIER_BTSTREAM && CompatibilityCode >= 7)))
            {
                WriteBytes(new byte[5] { (byte)PacketTypeShimmer3.SET_VBATT_FREQ_COMMAND, (byte)0x00, (byte)0x00, (byte)0x00, (byte)0x00 }, 0, 5);
                System.Threading.Thread.Sleep(200);
            }
        }

        /// <summary>
        /// This is used to get pc time and writes the 8 byte value to shimmer device. 
        /// </summary>
        public virtual void writeRealWorldClock() { }

        /// <summary>
        /// This is used to set the Magnetometer (Shimmer2R and Shimmer3) to low power mode, where the internal sampling rate of the Magnetometer chip is reduced to 10 Hz for Shimmer2r 
        /// , 15Hz for Shimmer3, and 10 Hz for Shimmer3 with updated sensors.
        /// To determine if your Shimmer 3 device is using updated sensors please see isShimmer3withUpdatedSensors
        /// </summary>
        /// <param name="enable">Set to true to enable</param>
        public void SetLowPowerMag(bool enable)
        {
            LowPowerMagEnabled = enable;
            if (HardwareVersion == (int)ShimmerVersion.SHIMMER3) //Shimmer3
            {
                if (!LowPowerMagEnabled)
                {
                    if (isShimmer3withUpdatedSensors())
                    {
                        if (SamplingRate >= 100)
                        {
                            WriteMagSamplingRate(3);
                        }
                        else if (SamplingRate >= 50)
                        {
                            WriteMagSamplingRate(2);
                        }
                        else if (SamplingRate >= 20)
                        {
                            WriteMagSamplingRate(1);
                        }
                        else if (SamplingRate >= 10)
                        {
                            WriteMagSamplingRate(0);
                        }
                        else
                        {
                            WriteMagSamplingRate(0);
                        }
                    }
                    else
                    {
                        if (SamplingRate >= 50)
                        {
                            WriteMagSamplingRate(6);
                        }
                        else if (SamplingRate >= 20)
                        {
                            WriteMagSamplingRate(5);
                        }
                        else if (SamplingRate >= 10)
                        {
                            WriteMagSamplingRate(4);
                        }
                        else
                        {
                            WriteMagSamplingRate(3);
                        }
                    }
                }
                else //Low power mag for shimmer3 enabled
                {
                    if (isShimmer3withUpdatedSensors())
                    {
                        WriteMagSamplingRate(0);
                    }
                    else
                    {
                        WriteMagSamplingRate(4);
                    }
                }
            }
            else if (HardwareVersion == (int)ShimmerVersion.SHIMMER3R) //Shimmer3R
            {
                if (!LowPowerMagEnabled)
                {
                    if (SamplingRate > 50)
                    {
                        WriteMagSamplingRate(0x03); //(100Hz)
                    }
                    else if (SamplingRate > 20)
                    {
                        WriteMagSamplingRate(0x02); //(50Hz)
                    }
                    else if (SamplingRate > 10)
                    {
                        WriteMagSamplingRate(0x01); //(20Hz)
                    }
                    else
                    {
                        WriteMagSamplingRate(0x00); //(10Hz)
                    }
                }
                else //Low power mag for shimmer3R enabled 
                {
                    WriteMagSamplingRate(0x00);
                }
            }
            else //Shimmer2r
            {
                if (!LowPowerMagEnabled)
                {
                    if (SamplingRate <= 1)
                    {
                        WriteMagSamplingRate(1);
                    }
                    else if (SamplingRate <= 15)
                    {
                        WriteMagSamplingRate(4);
                    }
                    else if (SamplingRate <= 30)
                    {
                        WriteMagSamplingRate(5);
                    }
                    else if (SamplingRate <= 75)
                    {
                        WriteMagSamplingRate(6);
                    }
                    else
                    {
                        WriteMagSamplingRate(7);
                    }
                }
                else
                {
                    if (SamplingRate >= 10)
                    {
                        WriteMagSamplingRate(4);
                    }
                    else
                    {
                        WriteMagSamplingRate(1);
                    }
                }
            }
        }
        /// <summary>
        /// This checks whether the time stamp has increased correctly
        /// </summary>
        /// <param name="timeStamp2"> This is the most recent timestamp</param>
        /// <param name="timeStamp1"> this is the previously received timestamp</param>
        /// <returns></returns>
        public virtual bool packetTimeStampChecker(int timeStamp2, int timeStamp1)
        {
            int upperLimit = ADCRawSamplingRateValue + (int)(ADCRawSamplingRateValue * 0.05);
            int lowerLimit = ADCRawSamplingRateValue - (int)(ADCRawSamplingRateValue * 0.05);
            if ((timeStamp2 - timeStamp1) < 0)
            {
                timeStamp2 = timeStamp2 + TimeStampPacketRawMaxValue;

            }
            int difference = timeStamp2 - timeStamp1;
            if ((difference) > lowerLimit && (difference) < upperLimit)
            {
                return true;
            }
            else
            {
                System.Console.WriteLine(difference);
            }
            return false;
        }



        /// <summary>
        /// This sets the Gyroscope on the Shimmer3 to low power mode, where in low power mode the internal sampling rate of the Gyroscope is reduced to 31.25Hz. Note for the 3R both the Gyro and LN Accel is set to low power mode. For the 3R low power mode results in a sampling rate of 1.875Hz
        /// </summary>
        /// <param name="enable">Set to true to enable</param>
        public void SetLowPowerGyro(bool enable)
        {
            LowPowerGyroEnabled = enable;
            if (HardwareVersion == (int)ShimmerBluetooth.ShimmerVersion.SHIMMER3)
            {
                if (!LowPowerGyroEnabled)
                {
                    if (SamplingRate <= 51.28)
                    {
                        WriteGyroSamplingRate(0x9B);
                    }
                    else if (SamplingRate <= 102.56)
                    {
                        WriteGyroSamplingRate(0x4D);
                    }
                    else if (SamplingRate <= 129.03)
                    {
                        WriteGyroSamplingRate(0x3D);
                    }
                    else if (SamplingRate <= 173.91)
                    {
                        WriteGyroSamplingRate(0x2D);
                    }
                    else if (SamplingRate <= 205.13)
                    {
                        WriteGyroSamplingRate(0x26);
                    }
                    else if (SamplingRate <= 258.06)
                    {
                        WriteGyroSamplingRate(0x1E);
                    }
                    else if (SamplingRate <= 533.33)
                    {
                        WriteGyroSamplingRate(0xE);
                    }
                    else
                    {
                        WriteGyroSamplingRate(6);
                    }
                }
                else
                {
                    WriteGyroSamplingRate(0xFF);
                }
            }
            else if (HardwareVersion == (int)ShimmerBluetooth.ShimmerVersion.SHIMMER3R)
            {
                if (!LowPowerGyroEnabled)
                {
                    if (SamplingRate <= 7.5)
                    {
                        WriteGyroSamplingRate(0x02);
                    }
                    else if (SamplingRate <= 30)
                    {
                        WriteGyroSamplingRate(0x04);
                    }
                    else if (SamplingRate <= 60)
                    {
                        WriteGyroSamplingRate(0x05);
                    }
                    else if (SamplingRate <= 120)
                    {
                        WriteGyroSamplingRate(0x06);
                    }
                    else if (SamplingRate <= 240)
                    {
                        WriteGyroSamplingRate(0x07);
                    }
                    else if (SamplingRate <= 480)
                    {
                        WriteGyroSamplingRate(0x08);
                    }
                    else if (SamplingRate <= 960)
                    {
                        WriteGyroSamplingRate(0x09);
                    }
                    else if (SamplingRate <= 1920)
                    {
                        WriteGyroSamplingRate(0x0A);
                    }
                    else if (SamplingRate <= 3840)
                    {
                        WriteGyroSamplingRate(0x0B);
                    }
                    else if (SamplingRate <= 7680)
                    {
                        WriteGyroSamplingRate(0x0C);
                    }
                }
                else
                {
                    WriteGyroSamplingRate(0x01);
                }
            }
        }

        /// <summary>
        /// This sets the Wide Range Accel on the Shimmer3 to low power mode, where in low power mode the internal sampling rate of the wide range accelerometer is reduced to 10Hz for Shimmer3 devices, and 12.5Hz for Shimmer3 devices with updated sensors.
        /// To determine if your Shimmer 3 device is using updated sensors please see isShimmer3withUpdatedSensors
        /// </summary>
        /// <param name="enable">Set to true to enable low power mode</param>
        public void SetLowPowerAccel(bool enable)
        {
            LowPowerAccelEnabled = enable;
            if (!LowPowerAccelEnabled)
            {
                if (HardwareVersion == (int)ShimmerBluetooth.ShimmerVersion.SHIMMER3)
                {
                    if (isShimmer3withUpdatedSensors())
                    {
                        if (SamplingRate <= 12.5)
                        {
                            WriteWRAccelSamplingRate(1);
                        }
                        else if (SamplingRate <= 25)
                        {
                            WriteWRAccelSamplingRate(2);
                        }
                        else if (SamplingRate <= 50)
                        {
                            WriteWRAccelSamplingRate(3);
                        }
                        else if (SamplingRate <= 100)
                        {
                            WriteWRAccelSamplingRate(4);
                        }
                        else if (SamplingRate <= 200)
                        {
                            WriteWRAccelSamplingRate(5);
                        }
                        else if (SamplingRate <= 400)
                        {
                            WriteWRAccelSamplingRate(6);
                        }
                        else if (SamplingRate <= 800)
                        {
                            WriteWRAccelSamplingRate(7);
                        }
                        else if (SamplingRate <= 1600)
                        {
                            WriteWRAccelSamplingRate(8);
                        }
                        else if (SamplingRate <= 3200)
                        {
                            WriteWRAccelSamplingRate(9);
                        }
                        else
                        {
                            WriteWRAccelSamplingRate(10);
                        }
                    }
                    else
                    {
                        //enableLowResolutionMode(false);
                        if (SamplingRate <= 1)
                        {
                            WriteWRAccelSamplingRate(1);
                        }
                        else if (SamplingRate <= 10)
                        {
                            WriteWRAccelSamplingRate(2);
                        }
                        else if (SamplingRate <= 25)
                        {
                            WriteWRAccelSamplingRate(3);
                        }
                        else if (SamplingRate <= 50)
                        {
                            WriteWRAccelSamplingRate(4);
                        }
                        else if (SamplingRate <= 100)
                        {
                            WriteWRAccelSamplingRate(5);
                        }
                        else if (SamplingRate <= 200)
                        {
                            WriteWRAccelSamplingRate(6);
                        }
                        else
                        {
                            WriteWRAccelSamplingRate(7);
                        }
                    }
                }
                else if (HardwareVersion == (int)ShimmerBluetooth.ShimmerVersion.SHIMMER3R)
                {
                    if (SamplingRate <= 12.5)
                    {
                        WriteWRAccelSamplingRate(1);
                    }
                    else if (SamplingRate <= 25)
                    {
                        WriteWRAccelSamplingRate(3);
                    }
                    else if (SamplingRate <= 50)
                    {
                        WriteWRAccelSamplingRate(4);
                    }
                    else if (SamplingRate <= 100)
                    {
                        WriteWRAccelSamplingRate(5);
                    }
                    else if (SamplingRate <= 200)
                    {
                        WriteWRAccelSamplingRate(6);
                    }
                    else if (SamplingRate <= 400)
                    {
                        WriteWRAccelSamplingRate(7);
                    }
                    else if (SamplingRate <= 800)
                    {
                        WriteWRAccelSamplingRate(8);
                    }
                    else
                    {
                        WriteWRAccelSamplingRate(9);
                    }
                }
            }
            else
            {
                if (HardwareVersion == (int)ShimmerBluetooth.ShimmerVersion.SHIMMER3)
                {
                    if (isShimmer3withUpdatedSensors())
                    {
                        WriteWRAccelSamplingRate(1);
                    }
                    else
                    {
                        WriteWRAccelSamplingRate(2);
                    }
                }
                else if (HardwareVersion == (int)ShimmerBluetooth.ShimmerVersion.SHIMMER3R)
                {
                    WriteWRAccelSamplingRate(1);
                }
            }
        }

        /// <summary>
        /// This can be used to check the registers on the ExG Daughter board and determine whether it is using default ECG configurations
        /// </summary>
        /// <returns>Returns true if defaul ECG configurations is being used</returns>
        public bool IsDefaultExgTestSignalConfigurationEnabled()
        {
            bool isUsing = false;
            if (((Exg1RegArray[3] & 15) == 5) && ((Exg1RegArray[4] & 15) == 5) && ((Exg2RegArray[3] & 15) == 5) && ((Exg2RegArray[4] & 15) == 5))
            {
                isUsing = true;
            }
            return isUsing;
        }

        /// <summary>
        /// This can be used to check the registers on the ExG Daughter board and determine whether it is using default ECG configurations
        /// </summary>
        /// <returns>Returns true if defaul ECG configurations is being used</returns>
        public bool IsDefaultECGConfigurationEnabled()
        {
            bool isUsing = false;
            if (((Exg1RegArray[3] & 15) == 0) && ((Exg1RegArray[4] & 15) == 0) && ((Exg2RegArray[3] & 15) == 0) && ((Exg2RegArray[4] & 15) == 7))
            {
                isUsing = true;
            }
            return isUsing;
        }
        /// <summary>
        /// This can be used to check the registers on the ExG Daughter board and determine whether it is using default EMG configurations
        /// </summary>
        /// <returns>Returns true if defaul EMG configurations is being used</returns>
        public bool IsDefaultEMGConfigurationEnabled()
        {
            bool isUsing = false;
            if (((Exg1RegArray[3] & 15) == 9) && ((Exg1RegArray[4] & 15) == 0) && ((Exg2RegArray[3] & 15) == 1) && ((Exg2RegArray[4] & 15) == 1))
            {
                isUsing = true;
            }

            return isUsing;
        }

        /// <summary>
        /// This can be used to check the registers on the ExG Daughter board and determine whether it is using default respiration configurations
        /// </summary>
        /// <returns>Returns true if default respiration configurations is being used</returns>
        public bool IsDefaultRespirationConfigurationEnabled()
        {
            bool isUsing = false;
            if (((Exg1RegArray[3] & 15) == 0) && ((Exg1RegArray[4] & 15) == 0) && ((Exg2RegArray[3] & 15) == 0) && ((Exg2RegArray[4] & 15) == 0))
            {
                isUsing = true;
            }

            return isUsing;
        }

        protected int ConvertEXGGainSettingToValue(int setting)
        {
            if (setting == 0)
            {
                return 6;
            }
            else if (setting == 1)
            {
                return 1;
            }
            else if (setting == 2)
            {
                return 2;
            }
            else if (setting == 3)
            {
                return 3;
            }
            else if (setting == 4)
            {
                return 4;
            }
            else if (setting == 5)
            {
                return 8;
            }
            else if (setting == 6)
            {
                return 12;
            }
            else
            {
                return -1; // -1 means invalid value
            }
        }

        protected int ConvertEXGGainValuetoSetting(int value)
        {
            if (value == 6)
            {
                return 0;
            }
            else if (value == 1)
            {
                return 1;
            }
            else if (value == 2)
            {
                return 2;
            }
            else if (value == 3)
            {
                return 3;
            }
            else if (value == 4)
            {
                return 4;
            }
            else if (value == 8)
            {
                return 5;
            }
            else if (value == 12)
            {
                return 6;
            }
            else
            {
                return -1; // -1 means invalid value
            }
        }


        /// <summary>
        /// This is used to configure the two ExG chips on the ExG daughter board
        /// </summary>
        /// <param name="valuesChip1">A byte array of 10 representing the contents to be writting to EXG chip 1 </param>
        /// <param name="valuesChip2">A byte array of 10 representing the contents to be writting to EXG chip 2 </param>
        public void WriteEXGConfigurations(byte[] valuesChip1, byte[] valuesChip2)
        {
            if (IsConnectionOpen())
            {
                if (CompatibilityCode >= 3)
                {
                    WriteBytes(new byte[1] { (byte)PacketTypeShimmer3.SET_EXG_REGS_COMMAND }, 0, 1);
                    WriteBytes(new byte[1] { (byte)0 }, 0, 1); //CHIPID1
                    WriteBytes(new byte[1] { (byte)0 }, 0, 1);
                    WriteBytes(new byte[1] { (byte)10 }, 0, 1);
                    for (int i = 0; i < 10; i++)
                    {
                        WriteBytes(new byte[1] { valuesChip1[i] }, 0, 1);
                        Exg1RegArray[i] = valuesChip1[i];
                    }

                    System.Threading.Thread.Sleep(500);

                    WriteBytes(new byte[1] { (byte)PacketTypeShimmer3.SET_EXG_REGS_COMMAND }, 0, 1);
                    WriteBytes(new byte[1] { (byte)1 }, 0, 1); //CHIPID2
                    WriteBytes(new byte[1] { (byte)0 }, 0, 1);
                    WriteBytes(new byte[1] { (byte)10 }, 0, 1);
                    for (int i = 0; i < 10; i++)
                    {
                        WriteBytes(new byte[1] { valuesChip2[i] }, 0, 1);
                        Exg2RegArray[i] = valuesChip2[i];
                    }
                    System.Threading.Thread.Sleep(500);
                }
            }
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="exgGain"> where 0 = 6x Gain, 1 = 1x , 2 = 2x , 3 = 3x, 4 = 4x, 5 = 8x, 6 = 12x </param>
        public void WriteEXGGain(byte exgGain)
        {
            if (IsConnectionOpen())
            {
                if (CompatibilityCode >= 3)
                {

                    byte exg1ar4 = (byte)(Exg1RegArray[3] & 0x8F); //143 = 1000 1111b
                    byte exg1ar5 = (byte)(Exg1RegArray[4] & 0x8F); //143 = 1000 1111b
                    exg1ar4 = (byte)(exg1ar4 | (exgGain << 4));
                    exg1ar5 = (byte)(exg1ar5 | (exgGain << 4));
                    byte[] exgChip1ToBeWritten = Exg1RegArray;
                    exgChip1ToBeWritten[3] = exg1ar4;
                    exgChip1ToBeWritten[4] = exg1ar5;

                    byte exg2ar4 = (byte)(Exg2RegArray[3] & 0x8F); //143 = 1000 1111b
                    byte exg2ar5 = (byte)(Exg2RegArray[4] & 0x8F); //143 = 1000 1111b
                    exg2ar4 = (byte)(exg2ar4 | (exgGain << 4));
                    exg2ar5 = (byte)(exg2ar5 | (exgGain << 4));
                    byte[] exgChip2ToBeWritten = Exg2RegArray;
                    exgChip2ToBeWritten[3] = exg2ar4;
                    exgChip2ToBeWritten[4] = exg2ar5;

                    /*WriteBytes(new byte[1] { (byte)PacketTypeShimmer3.SET_EXG_REGS_COMMAND }, 0, 1);
                    WriteBytes(new byte[1] { (byte)0 }, 0, 1); //CHIPID1
                    WriteBytes(new byte[1] { (byte)0 }, 0, 1); //Starting Index In the Register
                    WriteBytes(new byte[1] { (byte)10 }, 0, 1); //Number Of Bytes Being Written
                    WriteBytes(exgChip1ToBeWritten,0,10);
                    Exg1RegArray = exgChip1ToBeWritten;

                    System.Threading.Thread.Sleep(200);

                    WriteBytes(new byte[1] { (byte)PacketTypeShimmer3.SET_EXG_REGS_COMMAND }, 0, 1);
                    WriteBytes(new byte[1] { (byte)1 }, 0, 1); //CHIPID2
                    WriteBytes(new byte[1] { (byte)0 }, 0, 1); //Starting Index In the Register
                    WriteBytes(new byte[1] { (byte)10 }, 0, 1); //Number Of Bytes Being Written
                    WriteBytes(exgChip2ToBeWritten, 0, 1);
                    Exg2RegArray = exgChip2ToBeWritten;
                    System.Threading.Thread.Sleep(200);
                    */

                    WriteEXGConfigurations(exgChip1ToBeWritten, exgChip2ToBeWritten);
                }
            }
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="exgRate">where 0=125SPS ; 1=250SPS; 2=500SPS; 3=1000SPS; 4=2000SPS</param>
        public void WriteEXGRate(byte exgRate)
        {
            if (IsConnectionOpen())
            {
                if (CompatibilityCode >= 3)
                {

                    byte exg1ar1 = (byte)(Exg1RegArray[0] & 0xF8); //248
                    exg1ar1 = (byte)(exg1ar1 | exgRate);

                    byte exg2ar1 = (byte)(Exg2RegArray[0] & 0xF8); //248
                    exg2ar1 = (byte)(exg2ar1 | exgRate);

                    WriteBytes(new byte[1] { (byte)PacketTypeShimmer3.SET_EXG_REGS_COMMAND }, 0, 1);
                    WriteBytes(new byte[1] { (byte)0 }, 0, 1); //CHIPID1
                    WriteBytes(new byte[1] { (byte)0 }, 0, 1); //Starting Index In the Register
                    WriteBytes(new byte[1] { (byte)1 }, 0, 1); //Number Of Bytes Being Written
                    WriteBytes(new byte[1] { exg1ar1 }, 0, 1);
                    Exg1RegArray[0] = exg1ar1;

                    System.Threading.Thread.Sleep(500);

                    WriteBytes(new byte[1] { (byte)PacketTypeShimmer3.SET_EXG_REGS_COMMAND }, 0, 1);
                    WriteBytes(new byte[1] { (byte)1 }, 0, 1); //CHIPID2
                    WriteBytes(new byte[1] { (byte)0 }, 0, 1); //Starting Index In the Register
                    WriteBytes(new byte[1] { (byte)1 }, 0, 1); //Number Of Bytes Being Written
                    WriteBytes(new byte[1] { exg2ar1 }, 0, 1);
                    Exg2RegArray[0] = exg2ar1;
                    System.Threading.Thread.Sleep(500);
                }
            }
        }
        /// <summary>
        /// To be used with SHIMMERECGMD/SHIMMEREXGMD
        /// </summary>
        /// <param name="value">Increment value (in y-axis) of sawtooth waveform, max value is 16777215 if the value specified is larger it will default to 5000, same if the value is less than 0</param>
        public void StartStreamingEXGSawtoothTestSignal(int value)
        {

            if (ShimmerState == SHIMMER_STATE_CONNECTED)
            {
                if (ShimmerState != SHIMMER_STATE_STREAMING)
                {
                    StreamingACKReceived = false;
                    StreamTimeOutCount = 0;
                    LastReceivedTimeStamp = 0;
                    CurrentTimeStampCycle = 0;
                    //A stream start: the next sample is the first one, and the pair
                    //above cannot say so on their own.
                    HasPreviousTimeStamp = false;
                    LastReceivedCalibratedTimeStamp = -1;
                    FirstTimeCalTime = true;
                    FirstSystemTimestamp = true;
                    PacketLossCount = 0;
                    PacketReceptionRate = 100;
                    KeepObjectCluster = null; //This is important and is required!
                    UnalignedBytesReceived = new List<Byte>();
                    OrientationAlgo = null;
                    mWaitingForStartStreamingACK = true;
                    if (value > 16777215 || value < 0)
                    {
                        //5000 default
                        //{0x9B, 0x88, 0x13, 0x00}
                        WriteBytes(new byte[4] { (byte)155, (byte)136, (byte)19, (byte)0 }, 0, 4);
                    }
                    else
                    {
                        byte[] valuebytearray = BitConverter.GetBytes(value);
                        WriteBytes(new byte[4] { (byte)155, valuebytearray[0], valuebytearray[1], valuebytearray[2] }, 0, 4);
                    }
                }
            }

        }

        protected double CalibrateGsrData(double gsrUncalibratedData, double p1, double p2)
        {
            gsrUncalibratedData = (double)((int)gsrUncalibratedData & 4095);
            //the following polynomial is deprecated and has been replaced with a more accurate linear one, see GSR user guide for further details
            //double gsrCalibratedData = (p1*Math.pow(gsrUncalibratedData,4)+p2*Math.pow(gsrUncalibratedData,3)+p3*Math.pow(gsrUncalibratedData,2)+p4*gsrUncalibratedData+p5)/1000;
            //the following is the new linear method see user GSR user guide for further details
            double gsrCalibratedData = (1 / (p1 * gsrUncalibratedData + p2)) * 1000; //kohms 
            return gsrCalibratedData;
        }

        protected double[] CalibratePressure280SensorData(double UP, double UT)
        {
            double adc_T = UT;
            double adc_P = UP;

            // Returns temperature in DegC, double precision. Output value of “51.23” equals 51.23 DegC.
            // t_fine carries fine temperature as global value
            double var1 = ((adc_T) / 16384.0 - dig_T1 / 1024.0) * dig_T2;
            double var2 = (((adc_T) / 131072.0 - dig_T1 / 8192.0) * (adc_T / 131072.0 - dig_T1 / 8192.0)) * dig_T3;
            double t_fine = var1 + var2;
            double T = t_fine / 5120.0;
            //		double fTemp = T * 1.8 + 32; // Fahrenheit
            //		T = T/100.0;

            // Returns pressure in Pa as double. Output value of “96386.2” equals 96386.2 Pa = 963.862 hPa
            var1 = (t_fine / 2.0) - 64000.0;
            var2 = var1 * var1 * dig_P6 / 32768.0;
            var2 = var2 + var1 * dig_P5 * 2.0;
            var2 = (var2 / 4.0) + (dig_P4 * 65536.0);
            var1 = (dig_P3 * var1 * var1 / 524288.0 + dig_P2 * var1) / 524288.0;
            var1 = (1.0 + var1 / 32768.0) * dig_P1;
            if (var1 == 0.0)
            {
                //			return 0; // avoid exception caused by division by zero
            }
            double p = 1048576.0 - adc_P;
            p = (p - (var2 / 4096.0)) * 6250.0 / var1;
            var1 = dig_P9 * p * p / 2147483648.0;
            var2 = p * dig_P8 / 32768.0;
            p = p + (var1 + var2 + dig_P7) / 16.0;


            double[] caldata = new double[2];
            caldata[0] = p;
            caldata[1] = T;///10; // TODO divided by 10 in BMP180, needed here?
            return caldata;
        }
        protected double[] CalibratePressure390SensorData(double UP, double UT)
        {
            double uncompTemp = UT;
            double partialDataT1;
            double partialDataT2;

            partialDataT1 = uncompTemp - Bmp3QuantizedCalibData_ParT1;
            partialDataT2 = partialDataT1 * Bmp3QuantizedCalibData_ParT2;

            // Update the compensated temperature in calib structure since this is needed for pressure calculation
            Bmp3QuantizedCalibData_TLin = partialDataT2 + (partialDataT1 * partialDataT1) *
                                                 Bmp3QuantizedCalibData_ParT3;

            // Returns compensated temperature
            if (Bmp3QuantizedCalibData_TLin < BMP3_MIN_TEMP_DOUBLE)
            {
                Bmp3QuantizedCalibData_TLin = BMP3_MIN_TEMP_DOUBLE;
            }

            if (Bmp3QuantizedCalibData_TLin > BMP3_MAX_TEMP_DOUBLE)
            {
                Bmp3QuantizedCalibData_TLin = BMP3_MAX_TEMP_DOUBLE;
            }


            // Variable to store the compensated pressure
            double compPress;

            // Temporary variables used for compensation
            double partialData1;
            double partialData2;
            double partialData3;
            double partialData4;
            double partialOut1;
            double partialOut2;

            partialData1 = Bmp3QuantizedCalibData_ParP6 * Bmp3QuantizedCalibData_TLin;
            partialData2 = Bmp3QuantizedCalibData_ParP7 * PowBmp3(Bmp3QuantizedCalibData_TLin, 2);
            partialData3 = Bmp3QuantizedCalibData_ParP8 * PowBmp3(Bmp3QuantizedCalibData_TLin, 3);
            partialOut1 = Bmp3QuantizedCalibData_ParP5 + partialData1 + partialData2 + partialData3;

            partialData1 = Bmp3QuantizedCalibData_ParP2 * Bmp3QuantizedCalibData_TLin;
            partialData2 = Bmp3QuantizedCalibData_ParP3 * PowBmp3(Bmp3QuantizedCalibData_TLin, 2);
            partialData3 = Bmp3QuantizedCalibData_ParP4 * PowBmp3(Bmp3QuantizedCalibData_TLin, 3);
            partialOut2 = UP *
                          (Bmp3QuantizedCalibData_ParP1 + partialData1 + partialData2 + partialData3);

            partialData1 = PowBmp3((double)UP, 2);
            partialData2 = Bmp3QuantizedCalibData_ParP9 + Bmp3QuantizedCalibData_ParP10 * Bmp3QuantizedCalibData_TLin;
            partialData3 = partialData1 * partialData2;
            partialData4 = partialData3 + PowBmp3((double)UP, 3) * Bmp3QuantizedCalibData_ParP11;
            compPress = partialOut1 + partialOut2 + partialData4;

            if (compPress < BMP3_MIN_PRES_DOUBLE)
            {
                compPress = BMP3_MIN_PRES_DOUBLE;
            }

            if (compPress > BMP3_MAX_PRES_DOUBLE)
            {
                compPress = BMP3_MAX_PRES_DOUBLE;
            }

            double[] caldata = new double[2];
            caldata[0] = compPress;
            caldata[1] = Bmp3QuantizedCalibData_TLin;
            return caldata;
        }

        // Method to compute power, equivalent to pow_bmp3 function in C
        private static double PowBmp3(double baseValue, int exponent)
        {
            return Math.Pow(baseValue, exponent);
        }

        protected int countDigits(int number)
        {
            if (number == 0)
            {
                return 1;
            }
            return (int)Math.Floor(Math.Log10(Math.Abs(number)) + 1);
        }

        protected double[] CalibratePressure180SensorData(double UP, double UT)
        {
            double X1 = (UT - AC6) * AC5 / 32768;
            double X2 = (MC * 2048 / (X1 + MD));
            double B5 = X1 + X2;
            double T = (B5 + 8) / 16;

            double B6 = B5 - 4000;
            X1 = (B2 * (Math.Pow(B6, 2) / 4096)) / 2048;
            X2 = AC2 * B6 / 2048;
            double X3 = X1 + X2;
            double B3 = (((AC1 * 4 + X3) * (1 << PressureResolution) + 2)) / 4;
            X1 = AC3 * B6 / 8192;
            X2 = (B1 * (Math.Pow(B6, 2) / 4096)) / 65536;
            X3 = ((X1 + X2) + 2) / 4;
            double B4 = AC4 * (X3 + 32768) / 32768;
            double B7 = (UP - B3) * (50000 >> PressureResolution);
            double p = 0;
            if (B7 < 2147483648L)
            { //0x80000000
                p = (B7 * 2) / B4;
            }
            else
            {
                p = (B7 / B4) * 2;
            }
            X1 = ((p / 256.0) * (p / 256.0) * 3038) / 65536;
            X2 = (-7357 * p) / 65536;
            p = p + ((X1 + X2 + 3791) / 16);

            double[] caldata = new double[2];
            caldata[0] = p;
            caldata[1] = T / 10;
            return caldata;
        }

        protected double CalibrateMspAdcChannel(double unCalData)
        {
            double offset = 0; double vRefP = 3; double gain = 1;
            double calData = CalibrateU12AdcValue(unCalData, offset, vRefP, gain);
            return calData;
        }

        protected double CalibrateShimmer3RAdcChannel(double unCalData)
        {
            double offset = 0; double vRefP = 3; double gain = 1;
            double calData = CalibrateU12AdcValue(unCalData, offset, vRefP, gain);
            return calData;
        }

        protected double CalibrateU12AdcValue(double uncalibratedData, double offset, double vRefP, double gain)
        {
            double calibratedData = (uncalibratedData - offset) * (((vRefP * 1000) / gain) / 4095);
            return calibratedData;
        }

        protected double CalibrateU14AdcValue(double uncalibratedData, double offset, double vRefP, double gain)
        {
            double calibratedData = (uncalibratedData - offset) * (((vRefP * 1000) / gain) / 16383);
            return calibratedData;
        }

        public double getBatteryVoltage()
        {
            return BatteryVoltage;
        }

        public int getBatteryChargingStatus()
        {
            return ChargingStatus;
        }

        protected static double adcValToBattVoltage(int adcVal)
        {
            double calibratedData = calibrateU12AdcValue(adcVal, 0.0, 3.0, 1.0);
            double battVoltage = ((calibratedData * 1.988)) / 1000; // 1.988 is due to components on the Shimmmer, 1000 is to convert to volts
            return battVoltage;
        }
        protected static double calibrateU12AdcValue(double uncalibratedData, double offset, double vRefP, double gain)
        {
            double calibratedData = (uncalibratedData - offset) * (((vRefP * 1000) / gain) / 4095);
            return calibratedData;
        }

        protected double GetStandardDeviation(List<double> doubleList)
        {
            double average = doubleList.Average();
            double sumOfDerivation = 0;
            foreach (double value in doubleList)
            {
                sumOfDerivation += (value) * (value);
            }
            double sumOfDerivationAverage = sumOfDerivation / doubleList.Count;
            return Math.Sqrt(sumOfDerivationAverage - (average * average));
        }

        public void ClearPacketBuffer()
        {
            ObjectClusterBuffer.Clear();
        }

        protected void OnNewEvent(EventArgs e)
        {
            EventHandler invoker = UICallback;

            if (invoker != null) invoker(this, e);
        }

        public bool compareVersions(int compMajor, int compMinor, int compInternal)
        {

            if ((FirmwareMajor > compMajor)
                    || (FirmwareMajor == compMajor && FirmwareMinor > compMinor)
                    || (FirmwareMajor == compMajor && FirmwareMinor == compMinor && FirmwareInternal >= compInternal))
            {
                return true; // if FW ID is the same and version is greater or equal 
            }
            return false; // if less or not the same FW ID
        }

        public bool compareVersions(int compMajor, int compMinor)
        {

            if ((FirmwareMajor > compMajor)
                    || (FirmwareMajor == compMajor && FirmwareMinor >= compMinor))
            {
                return true; // if FW ID is the same and version is greater or equal 
            }
            return false; // if less or not the same FW ID
        }

        public bool compareVersions(int fwIdentifier, int compMajor, int compMinor, int compInternal)
        {

            if (fwIdentifier == FirmwareIdentifier)
            {
                return compareVersions(compMajor, compMinor, compInternal); // if FW ID is the same and version is greater or equal 
            }
            return false; // if less or not the same FW ID
        }

        protected double CalibrateGsrDataToResistanceFromAmplifierEq(double gsrUncalibratedData, int range)
        {
            double rFeedback = SHIMMER3_GSR_REF_RESISTORS_KOHMS[range];
            double volts = 0;
            if (HardwareVersion == (int)ShimmerBluetooth.ShimmerVersion.SHIMMER3R)
            {
                volts = CalibrateShimmer3RAdcChannel(gsrUncalibratedData) / 1000.0;
            }
            else
            {
                volts = CalibrateMspAdcChannel(gsrUncalibratedData) / 1000.0;
            }
            double rSource = rFeedback / ((volts / 0.5) - 1.0);
            return rSource;
        }

        protected void ReadMemCommand(int command, int address, int size)
        {
            if (HardwareVersion == (int)ShimmerBluetooth.ShimmerVersion.SHIMMER3R)
            {
                byte[] memLengthToRead = new byte[] { (byte)size };
                byte[] memAddressToRead = BitConverter.GetBytes((short)(address & 0xFFFF));
                Array.Reverse(memAddressToRead);

                byte[] instructionBuffer = new byte[1 + memLengthToRead.Length + memAddressToRead.Length];
                instructionBuffer[0] = (byte)command;
                Array.Copy(memLengthToRead, 0, instructionBuffer, 1, memLengthToRead.Length);
                Array.Copy(memAddressToRead, 0, instructionBuffer, 1 + memLengthToRead.Length, memAddressToRead.Length);

                WriteBytes(instructionBuffer, 0, instructionBuffer.Length);
            }
            else
            {
                throw new Exception("Not Supported HW version");
            }
        }

        protected double NudgeGsrResistance(double gsrResistanceKOhms, int gsrRangeSetting)
        {
            if (gsrRangeSetting != 4)
            {
                return UtilCalibration.NudgeDouble(gsrResistanceKOhms, SHIMMER3_GSR_RESISTANCE_MIN_MAX_KOHMS[gsrRangeSetting, 0], SHIMMER3_GSR_RESISTANCE_MIN_MAX_KOHMS[gsrRangeSetting, 1]);
            }
            return gsrResistanceKOhms;
        }

        /** Check the CRC stored at the end of the byte array 
         * @param msg the input byte array
         * @return a boolean value value, true if CRC matches and false if CRC doesn't match
         */
        public static bool ShimmerUartCrcCheck(byte[] msg)
        {
            byte[] crc = ShimmerUartCrcCalc(msg, msg.Length - 2);

            if ((crc[0] == msg[msg.Length - 2])
                    && (crc[1] == msg[msg.Length - 1]))
                return true;
            else
                return false;
        }

        /** Calculate the CRC per byte
         * @param crc the start CRC value
         * @param b the byte to calculate the CRC on
         * @return the new CRC value
         */
        protected static int ShimmerUartCrcByte(int crc, byte b)
        {
            crc &= 0xFFFF;
            //crc = ((crc & 0xFFFF) >>> 8) | ((crc & 0xFFFF) << 8);
            crc = GetUnsignedRightShift((crc & 0xFFFF), 8) | ((crc & 0xFFFF) << 8);
            crc ^= (b & 0xFF);
            //crc ^= (crc & 0xFF) >>> 4;
            crc ^= GetUnsignedRightShift((crc & 0xFF), 4);
            crc ^= crc << 12;
            crc ^= (crc & 0xFF) << 5;
            crc &= 0xFFFF;
            return crc;
        }

        private static int GetUnsignedRightShift(int value, int s)
        {
            return (int)((uint)value >> s);
        }

        /** Calculate the CRC for a byte array. array[0] is CRC LSB, array[1] is CRC MSB 
	     * @param msg the input byte array
	     * @param len the length of the byte array to calculate the CRC on
	     * @return the calculated CRC value
	     */
        public static byte[] ShimmerUartCrcCalc(byte[] msg, int len)
        {
            int CRC_INIT = 0xB0CA;
            int crcCalc;
            int i;

            crcCalc = ShimmerUartCrcByte(CRC_INIT, msg[0]);
            for (i = 1; i < len; i++)
            {
                crcCalc = ShimmerUartCrcByte(crcCalc, (msg[i]));
            }
            if (len % 2 > 0)
            {
                crcCalc = ShimmerUartCrcByte(crcCalc, (byte)0x00);
            }

            byte[] crcCalcArray = new byte[2];
            crcCalcArray[0] = (byte)(crcCalc & 0xFF);  // CRC LSB
            crcCalcArray[1] = (byte)((crcCalc >> 8) & 0xFF); // CRC MSB 

            return crcCalcArray;
        }

        public bool CheckCrc(byte[] bufferTemp, int length)
        {
            byte[] crcCalc = ShimmerUartCrcCalc(bufferTemp, length);

            if (BluetoothCRCMode == BTCRCMode.ONE_BYTE || BluetoothCRCMode == BTCRCMode.TWO_BYTE)
            {
                // + 1 as this is the location of the CRC's LSB
                if (bufferTemp[PacketSize + 1] != crcCalc[0])
                {
                    return false;
                }
            }

            if (BluetoothCRCMode == BTCRCMode.TWO_BYTE)
            {
                // + 2 as this is the location of the CRC's MSB
                if (bufferTemp[PacketSize + 2] != crcCalc[1])
                {
                    return false;
                }
            }
            return true;
        }

    }
    
    
    

    // Define the bmp3_data class
    public class Bmp3Data
    {
        // Compensated temperature
        public double Temperature { get; set; }

        // Compensated pressure
        public double Pressure { get; set; }
    }
    public class Bmp3UncompData
    {
        // Un-compensated pressure
        public ulong Pressure { get; set; }

        // Un-compensated temperature
        public long Temperature { get; set; }
    }


    public class CustomEventArgs : EventArgs
    {

        protected int EventIdentifier;
        protected object Object;
        protected int MinorEventIdentifier = -1;

        public CustomEventArgs(int indicator, object obj)
        {
            this.EventIdentifier = indicator;
            this.Object = obj;
        }

        public CustomEventArgs(int indicator, object obj, int minorIndicator)
        {
            this.EventIdentifier = indicator;
            this.Object = obj;
            this.MinorEventIdentifier = minorIndicator;
        }

        public int getIndicator()
        {
            return EventIdentifier;
        }

        public object getObject()
        {
            return Object;
        }

        public int getMinorIndication()
        {
            return MinorEventIdentifier;
        }



    }

    /// <summary>
    /// Contains Configurations specific to Shimmer devices (e.g. both Shimmer2 and Shimmer3)
    /// </summary>
    public class ShimmerConfiguration
    {
        public class SignalNames
        {
            public static readonly String TIMESTAMP = "Timestamp";
            public static readonly String SYSTEM_TIMESTAMP = "System Timestamp";
            public static readonly String SYSTEM_TIMESTAMP_PLOT = "System Timestamp Plot";
        }
        public class SignalFormats
        {
            public static readonly String CAL = "CAL";
            public static readonly String RAW = "RAW";
        }

        public class SignalUnits
        {
            public static readonly String MilliSeconds = "mSecs";
            public static readonly String NoUnits = "no units";
            public static readonly String MeterPerSecondSquared = "m/(sec^2)";
            public static readonly String MeterPerSecondSquared_DefaultCal = "m/(sec^2)*";
            public static readonly String DegreePerSecond = "deg/sec";
            public static readonly String DegreePerSecond_DefaultCal = "deg/sec*";
            public static readonly String MilliVolts = "mVolts";
            public static readonly String MilliVolts_DefaultCal = "mVolts*";
            public static readonly String KiloPascal = "kPa";
            public static readonly String Celcius = "Celcius*";
            public static readonly String Local = "local";
            public static readonly String Local_DefaultCal = "local*";
            public static readonly String KiloOhms = "kOhms";
            public static readonly String MicroSiemens = "uSiemens";
            public static readonly String NanoAmpere = "nA";
        }
    }

    /// <summary>
    /// Contains Configurations specific to Shimmer2 devices
    /// </summary>
    public class Shimmer2Configuration
    {

        public class SignalNames
        {
            public static readonly String ACCELEROMETER_X = "Accelerometer X";
            public static readonly String ACCELEROMETER_Y = "Accelerometer Y";
            public static readonly String ACCELEROMETER_Z = "Accelerometer Z";
            public static readonly String V_SENSE_BATT = "VSenseBatt";
            public static readonly String V_SENSE_REG = "VSenseReg";
            public static readonly String MAGNETOMETER_X = "Magnetometer X";
            public static readonly String MAGNETOMETER_Y = "Magnetometer Y";
            public static readonly String MAGNETOMETER_Z = "Magnetometer Z";
            public static readonly String GYROSCOPE_X = "Gyroscope X";
            public static readonly String GYROSCOPE_Y = "Gyroscope Y";
            public static readonly String GYROSCOPE_Z = "Gyroscope Z";
            public static readonly String EXPBOARD_A0 = "Exp Board A0";
            public static readonly String EXPBOARD_A7 = "Exp Board A7";
            public static readonly String GSR = "GSR";
            public static readonly String GSR_CONDUCTANCE = "GSR Conductance";
            public static readonly String GSR_RES = "GSR Res";
            public static readonly String ECG_RA_LL = "ECG RA LL";
            public static readonly String ECG_LA_LL = "ECG LA LL";
            public static readonly String EMG = "EMG";
            public static readonly String STRAIN_GAUGE_HIGH = "Strain Gauge High";
            public static readonly String STRAIN_GAUGE_LOW = "Strain Gauge Low";
            public static readonly String QUATERNION_0 = "Quaternion 0";
            public static readonly String QUATERNION_1 = "Quaternion 1";
            public static readonly String QUATERNION_2 = "Quaternion 2";
            public static readonly String QUATERNION_3 = "Quaternion 3";
            public static readonly String HEART_RATE = "Heart Rate";
            public static readonly String AXIS_ANGLE_A = "Axis Angle A";
            public static readonly String AXIS_ANGLE_X = "Axis Angle X";
            public static readonly String AXIS_ANGLE_Y = "Axis Angle Y";
            public static readonly String AXIS_ANGLE_Z = "Axis Angle Z";
        }


    }

    /// <summary>
    /// Contains Configurations specific to Shimmer3 devices
    /// </summary>
    public class Shimmer3Configuration
    {
        public static readonly byte[] EXG_ECG_CONFIGURATION_CHIP1 = new byte[] { 0x00, 0xA0, 0x10, 0x40, 0x40, 0x2D, 0x00, 0x00, 0x02, 0x03 };
        public static readonly byte[] EXG_ECG_CONFIGURATION_CHIP2 = new byte[] { 0x00, 0xA0, 0x10, 0x40, 0x47, 0x00, 0x00, 0x00, 0x02, 0x01 };
        public static readonly byte[] EXG_EMG_CONFIGURATION_CHIP1 = new byte[] { 0x00, 0xA0, 0x10, 0x69, 0x60, 0x20, 0x00, 0x00, 0x02, 0x03 };
        public static readonly byte[] EXG_EMG_CONFIGURATION_CHIP2 = new byte[] { 0x00, 0xA0, 0x10, 0xE1, 0xE1, 0x00, 0x00, 0x00, 0x02, 0x01 };
        public static readonly byte[] EXG_TEST_SIGNAL_CONFIGURATION_CHIP1 = new byte[] { 0x00, 0xA3, 0x10, 0x45, 0x45, 0x00, 0x00, 0x00, 0x02, 0x01 };
        public static readonly byte[] EXG_TEST_SIGNAL_CONFIGURATION_CHIP2 = new byte[] { 0x00, 0xA3, 0x10, 0x45, 0x45, 0x00, 0x00, 0x00, 0x02, 0x01 };

        public class SignalNames
        {
            public static readonly String LOW_NOISE_ACCELEROMETER_X = "Low Noise Accelerometer X";
            public static readonly String LOW_NOISE_ACCELEROMETER_Y = "Low Noise Accelerometer Y";
            public static readonly String LOW_NOISE_ACCELEROMETER_Z = "Low Noise Accelerometer Z";
            public static readonly String V_SENSE_BATT = "VSenseBatt";
            public static readonly String WIDE_RANGE_ACCELEROMETER_X = "Wide Range Accelerometer X";
            public static readonly String WIDE_RANGE_ACCELEROMETER_Y = "Wide Range Accelerometer Y";
            public static readonly String WIDE_RANGE_ACCELEROMETER_Z = "Wide Range Accelerometer Z";
            public static readonly String HIGH_G_ACCELEROMETER_X = "High G Accelerometer X";
            public static readonly String HIGH_G_ACCELEROMETER_Y = "High G Accelerometer Y";
            public static readonly String HIGH_G_ACCELEROMETER_Z = "High G Accelerometer Z";
            public static readonly String MAGNETOMETER_X = "Magnetometer X";
            public static readonly String MAGNETOMETER_Y = "Magnetometer Y";
            public static readonly String MAGNETOMETER_Z = "Magnetometer Z";
            public static readonly String ALT_MAGNETOMETER_X = "Alt Magnetometer X";
            public static readonly String ALT_MAGNETOMETER_Y = "Alt Magnetometer Y";
            public static readonly String ALT_MAGNETOMETER_Z = "Alt Magnetometer Z";
            public static readonly String GYROSCOPE_X = "Gyroscope X";
            public static readonly String GYROSCOPE_Y = "Gyroscope Y";
            public static readonly String GYROSCOPE_Z = "Gyroscope Z";
            public static readonly String EXTERNAL_ADC_A7 = "External ADC A7";
            public static readonly String EXTERNAL_ADC_A6 = "External ADC A6";
            public static readonly String EXTERNAL_ADC_A15 = "External ADC A15";
            public static readonly String INTERNAL_ADC_A1 = "Internal ADC A1";
            public static readonly String INTERNAL_ADC_A12 = "Internal ADC A12";
            public static readonly String INTERNAL_ADC_A13 = "Internal ADC A13";
            public static readonly String INTERNAL_ADC_A14 = "Internal ADC A14";
            public static readonly String PRESSURE = "Pressure";
            public static readonly String TEMPERATURE = "Temperature";
            public static readonly String GSR = "GSR";
            public static readonly String GSR_CONDUCTANCE = "GSR Conductance";
            public static readonly String EXG1_STATUS = "EXG1 Status";
            public static readonly String EXG2_STATUS = "EXG2 Status";
            public static readonly String ECG_LL_RA = "ECG LL-RA";
            public static readonly String ECG_LA_RA = "ECG LA-RA";
            public static readonly String ECG_VX_RL = "ECG Vx-RL";
            public static readonly String ECG_RESP = "ECG RESP";
            public static readonly String EMG_CH1 = "EMG CH1";
            public static readonly String EMG_CH2 = "EMG CH2";
            public static readonly String EXG1_CH1 = "EXG1 CH1";
            public static readonly String EXG1_CH2 = "EXG1 CH2";
            public static readonly String EXG2_CH1 = "EXG2 CH1";
            public static readonly String EXG2_CH2 = "EXG2 CH2";
            public static readonly String EXG1_CH1_16BIT = "EXG1 CH1 16Bit";
            public static readonly String EXG1_CH2_16BIT = "EXG1 CH2 16Bit";
            public static readonly String EXG2_CH1_16BIT = "EXG2 CH1 16Bit";
            public static readonly String EXG2_CH2_16BIT = "EXG2 CH2 16Bit";
            public static readonly String BRIGE_AMPLIFIER_HIGH = "Bridge Amplifier High";
            public static readonly String BRIGE_AMPLIFIER_LOW = "Bridge Amplifier Low";
            public static readonly String QUATERNION_0 = "Quaternion 0";
            public static readonly String QUATERNION_1 = "Quaternion 1";
            public static readonly String QUATERNION_2 = "Quaternion 2";
            public static readonly String QUATERNION_3 = "Quaternion 3";
            public static readonly String AXIS_ANGLE_A = "Axis Angle A";
            public static readonly String AXIS_ANGLE_X = "Axis Angle X";
            public static readonly String AXIS_ANGLE_Y = "Axis Angle Y";
            public static readonly String AXIS_ANGLE_Z = "Axis Angle Z";
        }


    }

    public class Shimmer3RConfiguration
    {
        public class SignalNames
        {
            //3R
            /*public static readonly String EXTERNAL_ADC_A9 = "External ADC A9";
            public static readonly String EXTERNAL_ADC_A11 = "External ADC A11";
            public static readonly String EXTERNAL_ADC_A12 = "External ADC A12";
            public static readonly String INTERNAL_ADC_A17 = "Internal ADC A17";
            public static readonly String INTERNAL_ADC_A10 = "Internal ADC A10";
            public static readonly String INTERNAL_ADC_A15 = "Internal ADC A15";
            public static readonly String INTERNAL_ADC_A16 = "Internal ADC A16";
            */
            public static readonly String GEN_EXT_ADC_A0 = "External ADC A0";
            public static readonly String GEN_EXT_ADC_A1 = "External ADC A1";
            public static readonly String GEN_EXT_ADC_A2 = "External ADC A2";

            public static readonly String GEN_INT_ADC_A0 = "Internal ADC A0";
            public static readonly String GEN_INT_ADC_A1 = "Internal ADC A1";
            public static readonly String GEN_INT_ADC_A2 = "Internal ADC A2";
            public static readonly String GEN_INT_ADC_A3 = "Internal ADC A3";
            public static readonly String GEN_INT_ADC_A4 = "Internal ADC A4";

            //3R end
        }
    }
}

