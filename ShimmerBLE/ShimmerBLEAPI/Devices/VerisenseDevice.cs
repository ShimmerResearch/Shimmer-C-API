using Newtonsoft.Json;
using shimmer.Helpers;
using shimmer.Models;
using shimmer.Sensors;
using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.Serialization.Formatters.Binary;
using System.Text;
using static shimmer.Models.OpConfigPayload;

namespace ShimmerBLEAPI.Devices
{
    /// <summary>
    /// This class represents a basic verisense device. It contains the device level settings. 
    /// </summary>
    [Serializable]
    public abstract class VerisenseDevice
    {
        public static readonly DeviceByteSetting Unknown_Device_Setting = new DeviceByteSetting("Unknown", -1, "Unknown");

        public static readonly String ExceptionMsgNotSupported = "Not Supported";

        /// <summary>
        /// Each variable represents a group of operational configuration settings <see cref="DeviceByteArraySettings"/>
        /// </summary>
        public class DefaultVerisenseConfiguration
        {
            public static readonly DeviceByteArraySettings Unknown_Device_OpConfig_Setting = new DeviceByteArraySettings("Unknown", null, "Unknown");
            public static readonly DeviceByteArraySettings Accel1_Default_Setting = new DeviceByteArraySettings("ACCEL 1",new byte[] {0x5A,0x97,0x00,0x00,0x00,0x30,0x30,0x00,0x7F,0x00,0xD8,0x0F,0x09,0x16,0x24,0x0C,0x80,0x00,0x00,0x00,0x00,0x00,0x00,0x00,0x00,0x00,0x00,0x00,0x00,0x00,0x03,0xF4,0x18,0x3C,0x00,0x0A,0x0F,0x00,0x18,0x3C,0x00,0x0A,0x0F,0x00,0x18,0x3C,0x00,0x0A,0x0F,0x00,0xFF,0xFF,0xAA,0x01,0x03,0x3C,0x00,0x0E,0x00,0x00,0x63,0x28,0xCC,0xCC,0x1E,0x00,0x0A,0x00,0x00,0x00,0x00,0x01}, "SensorLIS2DW12 - 16G, 25Hz, Using Low Power, Lower Power Mode 1");
            public static readonly DeviceByteArraySettings Accel2_Gyro_Default_Setting = new DeviceByteArraySettings("ACCEL 2 and Gyro", new byte[] {0x5A,0x77,0x00,0x00,0x00,0x30,0x20,0x00,0x7F,0x00,0xD8,0x0F,0x09,0x16,0x24,0x2C,0x80,0x00,0x00,0x00,0x00,0x00,0x00,0x00,0x00,0x00,0x00,0x00,0x00,0x00,0x03,0xF4,0x18,0x3C,0x00,0x0A,0x0F,0x00,0x18,0x3C,0x00,0x0A,0x0F,0x00,0x18,0x3C,0x00,0x0A,0x0F,0x00,0xFF,0xFF,0xAA,0x01,0x03,0x3C,0x00,0x0E,0x00,0x00,0x63,0x28,0xCC,0xCC,0x1E,0x00,0x0A,0x00,0x00,0x00,0x00,0x01}, "SensorLSM6DS3 - Accel 16G, Gyro 2000deg/s, 26Hz, Threshold 1");
            public static readonly DeviceByteArraySettings GSR_Batt_Accel1_Default_Setting = new DeviceByteArraySettings("GSR, Battery and Accel 1", new byte[] { 0x5A,0x97,0x80,0x02,0x00,0x30,0x20,0x00,0x7F,0x00,0x00,0x00,0x00,0x00,0x00,0x00,0x80,0x00,0x00,0x00,0x00,0x00,0x00,0x00,0x00,0x00,0x00,0x00,0x00,0x00,0x03,0xF4,0x18,0x3C,0x00,0x0A,0x0F,0x00,0x18,0x3C,0x00,0x0A,0x0F,0x00,0x18,0x3C,0x00,0x0A,0x0F,0x00,0x17,0x04,0xFF,0xFF,0xFF,0x3C,0x00,0x0E,0x00,0x00,0x63,0x28,0xCC,0xCC,0x1E,0x00,0x0A,0x00,0x00,0x00,0x00,0x01}, "GSR and Battery, 51.2Hz, GSR Auto Range. Accel (SensorLIS2DW12) at 8G, 25Hz, Using Low Power, Lower Power Mode 1");
            public static readonly DeviceByteArraySettings GSR_Batt_Default_Setting = new DeviceByteArraySettings("GSR and Battery", new byte[] { 0x5A, 0x17, 0x80, 0x02, 0x00, 0x30, 0x20, 0x00, 0x7F, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x80, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x03, 0xF4, 0x18, 0x3C, 0x00, 0x0A, 0x0F, 0x00, 0x18, 0x3C, 0x00, 0x0A, 0x0F, 0x00, 0x18, 0x3C, 0x00, 0x0A, 0x0F, 0x00, 0x17, 0x04, 0xFF, 0xFF, 0xFF, 0x3C, 0x00, 0x0E, 0x00, 0x00, 0x63, 0x28, 0xCC, 0xCC, 0x1E, 0x00, 0x0A, 0x00, 0x00, 0x00, 0x00, 0x01 }, "GSR and Battery, 51.2Hz, GSR Auto Range.");
            public static readonly DeviceByteArraySettings PPG_Default_Setting = new DeviceByteArraySettings("PPG", new byte[] { 0x5A, 0x17, 0x74, 0x00, 0x00, 0x00, 0x20, 0x00, 0x7F, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x80, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x03, 0xF4, 0x18, 0x3C, 0x00, 0x0A, 0x0F, 0x00, 0x18, 0x3C, 0x00, 0x0A, 0x0F, 0x00, 0x18, 0x3C, 0x00, 0x0A, 0x0F, 0x00, 0x00, 0xFF, 0xFF, 0xFF, 0xFF, 0x00, 0x00, 0x00, 0x00, 0x00, 0x63, 0x28, 0xCC, 0xCC, 0x1E, 0x00, 0x0A, 0x00, 0x00, 0x00, 0x00, 0x01 }, "Red Green IR Blue Leds enabled, 50 Hz, Sample Average = 1, Range 4, 420\u00B5s Pulse Width");
            public static readonly DeviceByteArraySettings[] Settings = new DeviceByteArraySettings[]{ Unknown_Device_OpConfig_Setting, Accel1_Default_Setting, Accel2_Gyro_Default_Setting, GSR_Batt_Accel1_Default_Setting, GSR_Batt_Default_Setting, PPG_Default_Setting };
        }

        public enum HardwareIdentifier
        {
            UNKNOWN = 00,
            VERISENSE_IMU_01 = 01,
            VERISENSE_IMU = 61,
            VERISENSE_GSR_PLUS = 62,
            VERISENSE_PPG = 63,
            VERISENSE_DEV_BOARD = 64,
            VERISENSE_PULSE_PLUS = 68
        }
        public CommunicationType CommType = CommunicationType.BLE;
        public enum CommunicationType
        {
            BLE = 00,
            SerialPort = 01
        }

        /// <summary>
        /// Each variable represents a Bluetooth 5 power level setting
        /// </summary>
        public static class BT5RadioOutputPower
        {
            public static readonly DeviceByteSetting Power_Unknown = new DeviceByteSetting("Unknown", -1, -1);
            public static readonly DeviceByteSetting Power_1 = new DeviceByteSetting("+8dBm", 8, 8);
            public static readonly DeviceByteSetting Power_2 = new DeviceByteSetting("+7dBm", 7, 8);
            public static readonly DeviceByteSetting Power_3 = new DeviceByteSetting("+6dBm", 6, 8);
            public static readonly DeviceByteSetting Power_4 = new DeviceByteSetting("+5dBm", 5, 8);
            public static readonly DeviceByteSetting Power_5 = new DeviceByteSetting("+4dBm", 4, 8);
            public static readonly DeviceByteSetting Power_6 = new DeviceByteSetting("+3dBm", 3, 8);
            public static readonly DeviceByteSetting Power_7 = new DeviceByteSetting("+2dBm", 2, 8);
            public static readonly DeviceByteSetting Power_8 = new DeviceByteSetting("0dBm", 0, 8);
            public static readonly DeviceByteSetting Power_9 = new DeviceByteSetting("-4dBm", 252, 8);
            public static readonly DeviceByteSetting Power_10 = new DeviceByteSetting("-8dBm", 248, 8);
            public static readonly DeviceByteSetting Power_11 = new DeviceByteSetting("-12dBm", 244, 8);
            public static readonly DeviceByteSetting Power_12 = new DeviceByteSetting("-16dBm", 240, 8);
            public static readonly DeviceByteSetting Power_13 = new DeviceByteSetting("-20dBm", 236, 8);
            public static readonly DeviceByteSetting Power_14 = new DeviceByteSetting("-40dBm", 255, 8);
            public static readonly DeviceByteSetting Power_15 = new DeviceByteSetting("-40dBm", 216, 8);
            public static readonly DeviceByteSetting[] Settings = { Power_Unknown, Power_1, Power_2, Power_3, Power_4, Power_5, Power_6, Power_7, Power_8, Power_9, Power_10, Power_11, Power_12, Power_13, Power_14, Power_15, };
        }

        protected StatusPayload Status { get; set; }
        protected ProdConfigPayload ProdConfig { get; set; }
        protected TimePayload Time { get; set; }
        protected OpConfigPayload OpConfig { get; set; }
        protected BasePayload WriteResponse { get; set; }
        protected PendingEventsPayload PendingEvents { get; set; }
        protected LogEventsPayload LogEvents { get; set; }

        public Guid Asm_uuid { get; set; }
        public String ComPort { get; set; }
        protected string ASMName { get; set; }
        protected Dictionary<int, Sensor> SensorList = new Dictionary<int, Sensor>();


        protected void CreateSensorMap()
        {
            //ADC	1
            //ACCEL_1 2
            //GYRO_ACCEL2 3
            //PPG 4
            //BioZ    5
            SensorList.Add(1, new SensorGSR());
            SensorList.Add(2, new SensorLIS2DW12());
            SensorList.Add(3, new SensorLSM6DS3());
            SensorList.Add(4, new SensorPPG());

        }

        /// <summary>
        /// To clone a device, this is used for configuring the device. The idea will be to create a 'clone' of the verisense device, update the sensor/setting within the clone. Generate the operation config bytes and transmit said bytes to the physical Verisense device.
        /// </summary>
        /// <param name="device">an existing verisense device that provides the operational config bytes</param>
        public VerisenseDevice(VerisenseDevice device)
        {
            OpConfig = new OpConfigPayload();
            OpConfig.ConfigurationBytes = new byte[device.OpConfig.ConfigurationBytes.Length];
            Array.Copy(device.OpConfig.ConfigurationBytes, OpConfig.ConfigurationBytes, device.OpConfig.ConfigurationBytes.Length); //deep copy
            CreateSensorMap();
            UpdateDeviceAndSensorConfiguration();
        }

        /// <summary>
        /// To create a new device
        /// </summary>
        public VerisenseDevice()
        {
            CreateSensorMap();
        }

        /// <summary>
        /// This is to maintain the device level settings
        /// </summary>
        /// <param name="deviceOpConfig"></param>
        /// <param name="opConfigToWrite"></param>
        /// <returns></returns>
        protected byte[] UpdateDefaultDeviceConfigBytes(OpConfigPayload deviceOpConfig, byte[] opConfigToWrite)
        {
            //OpConfig.ConfigurationBytes[(int)ConfigurationBytesIndexName.GEN_CFG_0] = DefaultConfigBytes.ConfigurationBytes[(int)ConfigurationBytesIndexName.GEN_CFG_0];
            opConfigToWrite[(int)ConfigurationBytesIndexName.START_TIME] = deviceOpConfig.ConfigurationBytes[(int)ConfigurationBytesIndexName.START_TIME];
            opConfigToWrite[(int)ConfigurationBytesIndexName.START_TIME + 1] = deviceOpConfig.ConfigurationBytes[(int)ConfigurationBytesIndexName.START_TIME + 1];
            opConfigToWrite[(int)ConfigurationBytesIndexName.START_TIME + 2] = deviceOpConfig.ConfigurationBytes[(int)ConfigurationBytesIndexName.START_TIME +2];
            opConfigToWrite[(int)ConfigurationBytesIndexName.START_TIME + 3] = deviceOpConfig.ConfigurationBytes[(int)ConfigurationBytesIndexName.START_TIME + 3];
            opConfigToWrite[(int)ConfigurationBytesIndexName.END_TIME] = deviceOpConfig.ConfigurationBytes[(int)ConfigurationBytesIndexName.END_TIME];
            opConfigToWrite[(int)ConfigurationBytesIndexName.END_TIME + 1] = deviceOpConfig.ConfigurationBytes[(int)ConfigurationBytesIndexName.END_TIME + 1];
            opConfigToWrite[(int)ConfigurationBytesIndexName.END_TIME + 2] = deviceOpConfig.ConfigurationBytes[(int)ConfigurationBytesIndexName.END_TIME + 2];
            opConfigToWrite[(int)ConfigurationBytesIndexName.END_TIME + 3] = deviceOpConfig.ConfigurationBytes[(int)ConfigurationBytesIndexName.END_TIME + 3];
            opConfigToWrite[(int)ConfigurationBytesIndexName.BLE_RETRY_COUNT] = deviceOpConfig.ConfigurationBytes[(int)ConfigurationBytesIndexName.BLE_RETRY_COUNT];
            opConfigToWrite[(int)ConfigurationBytesIndexName.BLE_TX_POWER] = deviceOpConfig.ConfigurationBytes[(int)ConfigurationBytesIndexName.BLE_TX_POWER];
            opConfigToWrite[(int)ConfigurationBytesIndexName.BLE_DATA_TRANS_WKUP_INT_HRS] = deviceOpConfig.ConfigurationBytes[(int)ConfigurationBytesIndexName.BLE_DATA_TRANS_WKUP_INT_HRS];
            opConfigToWrite[(int)ConfigurationBytesIndexName.BLE_DATA_TRANS_WKUP_TIME] = deviceOpConfig.ConfigurationBytes[(int)ConfigurationBytesIndexName.BLE_DATA_TRANS_WKUP_TIME];
            opConfigToWrite[(int)ConfigurationBytesIndexName.BLE_DATA_TRANS_WKUP_TIME + 1] = deviceOpConfig.ConfigurationBytes[(int)ConfigurationBytesIndexName.BLE_DATA_TRANS_WKUP_TIME + 1];
            opConfigToWrite[(int)ConfigurationBytesIndexName.BLE_DATA_TRANS_WKUP_DUR] = deviceOpConfig.ConfigurationBytes[(int)ConfigurationBytesIndexName.BLE_DATA_TRANS_WKUP_DUR];
            opConfigToWrite[(int)ConfigurationBytesIndexName.BLE_DATA_TRANS_RETRY_INT] = deviceOpConfig.ConfigurationBytes[(int)ConfigurationBytesIndexName.BLE_DATA_TRANS_RETRY_INT];
            opConfigToWrite[(int)ConfigurationBytesIndexName.BLE_STATUS_WKUP_INT_HRS] = deviceOpConfig.ConfigurationBytes[(int)ConfigurationBytesIndexName.BLE_STATUS_WKUP_INT_HRS];
            opConfigToWrite[(int)ConfigurationBytesIndexName.BLE_STATUS_WKUP_TIME] = deviceOpConfig.ConfigurationBytes[(int)ConfigurationBytesIndexName.BLE_STATUS_WKUP_TIME];
            opConfigToWrite[(int)ConfigurationBytesIndexName.BLE_STATUS_WKUP_TIME + 1] = deviceOpConfig.ConfigurationBytes[(int)ConfigurationBytesIndexName.BLE_STATUS_WKUP_TIME + 1];
            opConfigToWrite[(int)ConfigurationBytesIndexName.BLE_STATUS_WKUP_DUR] = deviceOpConfig.ConfigurationBytes[(int)ConfigurationBytesIndexName.BLE_STATUS_WKUP_DUR];
            opConfigToWrite[(int)ConfigurationBytesIndexName.BLE_STATUS_RETRY_INT] = deviceOpConfig.ConfigurationBytes[(int)ConfigurationBytesIndexName.BLE_STATUS_RETRY_INT];
            opConfigToWrite[(int)ConfigurationBytesIndexName.BLE_RTC_SYNC_WKUP_INT_HRS] = deviceOpConfig.ConfigurationBytes[(int)ConfigurationBytesIndexName.BLE_RTC_SYNC_WKUP_INT_HRS];
            opConfigToWrite[(int)ConfigurationBytesIndexName.BLE_RTC_SYNC_WKUP_TIME] = deviceOpConfig.ConfigurationBytes[(int)ConfigurationBytesIndexName.BLE_RTC_SYNC_WKUP_TIME];
            opConfigToWrite[(int)ConfigurationBytesIndexName.BLE_RTC_SYNC_WKUP_TIME + 1] = deviceOpConfig.ConfigurationBytes[(int)ConfigurationBytesIndexName.BLE_RTC_SYNC_WKUP_TIME + 1];
            opConfigToWrite[(int)ConfigurationBytesIndexName.BLE_RTC_SYNC_WKUP_DUR] = deviceOpConfig.ConfigurationBytes[(int)ConfigurationBytesIndexName.BLE_RTC_SYNC_WKUP_DUR];
            opConfigToWrite[(int)ConfigurationBytesIndexName.BLE_RTC_SYNC_RETRY_INT] = deviceOpConfig.ConfigurationBytes[(int)ConfigurationBytesIndexName.BLE_RTC_SYNC_RETRY_INT];
            opConfigToWrite[(int)ConfigurationBytesIndexName.ADAPTIVE_SCHEDULER_INT] = deviceOpConfig.ConfigurationBytes[(int)ConfigurationBytesIndexName.ADAPTIVE_SCHEDULER_INT];
            opConfigToWrite[(int)ConfigurationBytesIndexName.ADAPTIVE_SCHEDULER_INT + 1] = deviceOpConfig.ConfigurationBytes[(int)ConfigurationBytesIndexName.ADAPTIVE_SCHEDULER_INT + 1];
            opConfigToWrite[(int)ConfigurationBytesIndexName.ADAPTIVE_SCHEDULER_FAILCOUNT_MAX] = deviceOpConfig.ConfigurationBytes[(int)ConfigurationBytesIndexName.ADAPTIVE_SCHEDULER_FAILCOUNT_MAX];

            return opConfigToWrite;
        }

        /// <summary>
        /// Turns on/off the logging of data
        /// </summary>
        /// <param name="enabled"></param>
        public void setLoggingEnabled(bool enabled)
        {
            if (enabled)
            {
                OpConfig.ConfigurationBytes[(int)ConfigurationBytesIndexName.GEN_CFG_0] = (byte)(OpConfig.ConfigurationBytes[(int)ConfigurationBytesIndexName.GEN_CFG_0] | 0b00000001);
            } else
            {
                OpConfig.ConfigurationBytes[(int)ConfigurationBytesIndexName.GEN_CFG_0] = (byte)(OpConfig.ConfigurationBytes[(int)ConfigurationBytesIndexName.GEN_CFG_0] & 0b11111110);
            }
        }

        /// <summary>
        /// Disable/Enable the Verisense Device
        /// </summary>
        /// <param name="enabled"></param>
        public void setDeviceEnabled(bool enabled)
        {
            if (enabled)
            {
                OpConfig.ConfigurationBytes[(int)ConfigurationBytesIndexName.GEN_CFG_0] = (byte)(OpConfig.ConfigurationBytes[(int)ConfigurationBytesIndexName.GEN_CFG_0] | 0b00000010);
            }
            else
            {
                OpConfig.ConfigurationBytes[(int)ConfigurationBytesIndexName.GEN_CFG_0] = (byte)(OpConfig.ConfigurationBytes[(int)ConfigurationBytesIndexName.GEN_CFG_0] & 0b11111101);
            }
        }

        /// <summary>
        /// Disable/Enable bluetooth. Firmware before V2.01.003 cannot run with it disabled, so an
        /// operational config write keeps it enabled for that firmware: see
        /// <see cref="EnforceBluetoothOffFirmwareGuard"/> (DEV-1096)
        /// </summary>
        /// <param name="enabled"></param>
        public void setBluetoothEnabled(bool enabled)
        {
            if (enabled)
            {
                OpConfig.ConfigurationBytes[(int)ConfigurationBytesIndexName.GEN_CFG_0] = (byte)(OpConfig.ConfigurationBytes[(int)ConfigurationBytesIndexName.GEN_CFG_0] | 0b00010000);
            }
            else
            {
                OpConfig.ConfigurationBytes[(int)ConfigurationBytesIndexName.GEN_CFG_0] = (byte)(OpConfig.ConfigurationBytes[(int)ConfigurationBytesIndexName.GEN_CFG_0] & 0b11101111);
            }
        }

        /// <summary>
        /// Disable/Enable USB
        /// </summary>
        /// <param name="enabled"></param>
        public void setUSBEnabled(bool enabled)
        {
            if (enabled)
            {
                OpConfig.ConfigurationBytes[(int)ConfigurationBytesIndexName.GEN_CFG_0] = (byte)(OpConfig.ConfigurationBytes[(int)ConfigurationBytesIndexName.GEN_CFG_0] | 0b00001000);
            }
            else
            {
                OpConfig.ConfigurationBytes[(int)ConfigurationBytesIndexName.GEN_CFG_0] = (byte)(OpConfig.ConfigurationBytes[(int)ConfigurationBytesIndexName.GEN_CFG_0] & 0b11110111);
            }
        }

        /// <summary>
        /// Selects which HR channel is used when running the on board heart-rate detection algorithm. The corresponding PPG LED channel also needs to be enabled for this to work correctly. The output of this algorithm is available via the Heart-Rate BLE GATT service.
        /// </summary>
        /// <param name="led">0=Infra-red LED ,1=Red LED 2=Green LED,3=Blue LED</param>
        public void SetHRPPGChannel(int led)
        {
            if (led >= 0 && led <= 3)
            {
                OpConfig.ConfigurationBytes[(int)ConfigurationBytesIndexName.GEN_CFG_2] = (byte)(OpConfig.ConfigurationBytes[(int)ConfigurationBytesIndexName.GEN_CFG_2] & 0b00111111);
                OpConfig.ConfigurationBytes[(int)ConfigurationBytesIndexName.GEN_CFG_2] = (byte)(OpConfig.ConfigurationBytes[(int)ConfigurationBytesIndexName.GEN_CFG_2] | (led << 6));
            }
            else throw new Exception(ExceptionMsgNotSupported);

        }

        /// <summary>
        /// The duration in minutes wait for inactivity (either motion or Bluetooth connectivity) before putting the sensor in a stand-by state (i.e., stop recording and stop BLE advertising). Once motion is detected again, the sensor will resume BLE advertising only waiting for a Bluetooth connection and subsequent request to resume recording.
        /// <param name="timeout">0=Off , 63 is max value</param>
        public void SetInactiveTimeout(int timeout)
        {
            if (timeout >= 0 && timeout <= 63)
            {
                OpConfig.ConfigurationBytes[(int)ConfigurationBytesIndexName.INACTIVE_TIMEOUT] = (byte)(OpConfig.ConfigurationBytes[(int)ConfigurationBytesIndexName.INACTIVE_TIMEOUT] & 0xC0);
                OpConfig.ConfigurationBytes[(int)ConfigurationBytesIndexName.INACTIVE_TIMEOUT] = (byte)(OpConfig.ConfigurationBytes[(int)ConfigurationBytesIndexName.INACTIVE_TIMEOUT] | (byte)timeout);
            }
            else throw new Exception(ExceptionMsgNotSupported);
        }

        /// <summary>
        /// Linked with the INACTIVE_TIMEOUT setting (see SetInactiveTimeout method), this controls whether the sensor resumes recording automatically when motion is detected.  
        /// </summary>
        /// <param name="enabled"></param>
        public void SetResumeRecOnActivityEnabled(bool enabled)
        {
            if (enabled)
            {
                OpConfig.ConfigurationBytes[(int)ConfigurationBytesIndexName.INACTIVE_TIMEOUT] = (byte)(OpConfig.ConfigurationBytes[(int)ConfigurationBytesIndexName.INACTIVE_TIMEOUT] | 0b01000000);
            }
            else
            {
                OpConfig.ConfigurationBytes[(int)ConfigurationBytesIndexName.INACTIVE_TIMEOUT] = (byte)(OpConfig.ConfigurationBytes[(int)ConfigurationBytesIndexName.INACTIVE_TIMEOUT] & 0b10111111);
            }
        }


        /// <summary>
        /// This feature only applies to sensors have LEDs, i.e.:SR62, Wrist GSR+, SR64, Development board, SR68.7.1 and >=SR68.8.0, Wrist Pulse+. Please contact us if you require further clarification.
        /// </summary>
        /// <param name="ledMode">0=Off , 1=On , 2=Low Power , 3=Unused</param>
        public void SetLEDMode(int ledMode)
        {
            if (ledMode >= 0 && ledMode <= 3)
            {
                OpConfig.ConfigurationBytes[(int)ConfigurationBytesIndexName.GEN_CFG_3] = (byte)(OpConfig.ConfigurationBytes[(int)ConfigurationBytesIndexName.GEN_CFG_3] & 0b11111100);
                OpConfig.ConfigurationBytes[(int)ConfigurationBytesIndexName.GEN_CFG_3] = (byte)(OpConfig.ConfigurationBytes[(int)ConfigurationBytesIndexName.GEN_CFG_3] | (ledMode));
            }
            else throw new Exception(ExceptionMsgNotSupported);
        }

        /// <summary>
        /// This enables an on-board step-counter channel. This requires the Accelerometer ('Accel2’) to be enabled in devices that feature the LSM6DS3 IMU. The output of this is available in the Running Speed and Cadence BLE GATT service. The step count automatically resets at midnight. The sampling rate of the accelerometer must be set to be >= 26 Hz.
        /// </summary>
        /// <param name="enabled"></param>
        public void SetStepCountEnabled(bool enabled)
        {
            if (enabled)
            {
                OpConfig.ConfigurationBytes[(int)ConfigurationBytesIndexName.GEN_CFG_2] = (byte)(OpConfig.ConfigurationBytes[(int)ConfigurationBytesIndexName.GEN_CFG_2] | 0b00100000);
            }
            else
            {
                OpConfig.ConfigurationBytes[(int)ConfigurationBytesIndexName.GEN_CFG_2] = (byte)(OpConfig.ConfigurationBytes[(int)ConfigurationBytesIndexName.GEN_CFG_2] & 0b11011111);
            }
        }

        /// <summary>
        /// When enabled Bluetooth is turned on based on a schedule as set out by the configuration options. When disabled Bluetooth is enabled and the sensor is always open to connections.
        /// </summary>
        /// <param name="enabled"></param>
        public void SetPendingEventsEnabled(bool enabled)
        {
            if (!enabled)
            {
                OpConfig.ConfigurationBytes[(int)ConfigurationBytesIndexName.GEN_CFG_2] = (byte)(OpConfig.ConfigurationBytes[(int)ConfigurationBytesIndexName.GEN_CFG_2] | 0b00010000);
            }
            else
            {
                OpConfig.ConfigurationBytes[(int)ConfigurationBytesIndexName.GEN_CFG_2] = (byte)(OpConfig.ConfigurationBytes[(int)ConfigurationBytesIndexName.GEN_CFG_2] & 0b11101111);
            }
        }

        /// <summary>
        /// This enables a battery charger voltage channel for monitoring that status of the LTC4123 charger chip – if present on the sensor. This is primarily used for internal development and testing.
        /// </summary>
        /// <param name="enabled"></param>
        public void SetVPROGEnabled(bool enabled)
        {
            if (enabled)
            {
                OpConfig.ConfigurationBytes[(int)ConfigurationBytesIndexName.GEN_CFG_2] = (byte)(OpConfig.ConfigurationBytes[(int)ConfigurationBytesIndexName.GEN_CFG_2] | 0b00000100);
            }
            else
            {
                OpConfig.ConfigurationBytes[(int)ConfigurationBytesIndexName.GEN_CFG_2] = (byte)(OpConfig.ConfigurationBytes[(int)ConfigurationBytesIndexName.GEN_CFG_2] & 0b11111011);
            }
        }

        /// <summary>
        /// Use to turn off and on the battery voltage data collection (‘0’=off, ‘1’=on). 
        /// </summary>
        /// <param name="enabled"></param>
        public void SetVBATTEnabled(bool enabled)
        {
            if (enabled)
            {
                OpConfig.ConfigurationBytes[(int)ConfigurationBytesIndexName.GEN_CFG_2] = (byte)(OpConfig.ConfigurationBytes[(int)ConfigurationBytesIndexName.GEN_CFG_2] | 0b00000010);
            }
            else
            {
                OpConfig.ConfigurationBytes[(int)ConfigurationBytesIndexName.GEN_CFG_2] = (byte)(OpConfig.ConfigurationBytes[(int)ConfigurationBytesIndexName.GEN_CFG_2] & 0b11111101);
            }
        }

        /// <summary>
        /// Used to set the battery type that is currently connected to the sensor. This is used internally in the firmware to manage how the sensor utilises the battery in order to ensure optimum battery life.
        /// </summary>
        /// <param name="type">0 = Non-rechargeable (Zinc-Air) battery ; 1 = Rechargeable (NiMH) battery</param>
        public void SetBattTypeEnabled(int type)
        {
            if (type==0)
            {
                OpConfig.ConfigurationBytes[(int)ConfigurationBytesIndexName.GEN_CFG_2] = (byte)(OpConfig.ConfigurationBytes[(int)ConfigurationBytesIndexName.GEN_CFG_2] | 0b00000001);
            }
            else if (type==1)
            {
                OpConfig.ConfigurationBytes[(int)ConfigurationBytesIndexName.GEN_CFG_2] = (byte)(OpConfig.ConfigurationBytes[(int)ConfigurationBytesIndexName.GEN_CFG_2] & 0b11111110);
            } else
            {
                throw new Exception(ExceptionMsgNotSupported);
            }
        }



        // MINUTES SINCE 1 JAN 1970
        /// <summary>
        /// The clock system on a Verisense sensor is in local time (e.g. unix time expressed in your time zone, e.g. for Kuala Lumpur unixtime + 08:00) 
        /// </summary>
        /// <param name="time">The local time</param>
        public void SetStartTimeInMinutes(long time)
        {
            byte[] minutesPassed = new byte[4];
            minutesPassed = BitConverter.GetBytes(time);
            OpConfig.ConfigurationBytes[(int)ConfigurationBytesIndexName.START_TIME] = minutesPassed[0];
            OpConfig.ConfigurationBytes[(int)ConfigurationBytesIndexName.START_TIME + 1] = minutesPassed[1];
            OpConfig.ConfigurationBytes[(int)ConfigurationBytesIndexName.START_TIME + 2] = minutesPassed[2];
            OpConfig.ConfigurationBytes[(int)ConfigurationBytesIndexName.START_TIME + 3] = minutesPassed[3];
        }

        /// <summary>
        /// The clock system on a Verisense sensor is in local time (e.g. unix time expressed in your time zone, e.g. for Kuala Lumpur unixtime + 08:00) 
        /// </summary>
        /// <returns></returns>
        public int GetStartTimeinMinutes()
        {
            byte[] minutesPassed = new byte[4];
            Array.Copy(OpConfig.ConfigurationBytes, (int)ConfigurationBytesIndexName.START_TIME, minutesPassed, 0, 4);
            int time = BitConverter.ToInt32(minutesPassed, 0);
            return time;
        }

        /// <summary>
        /// The clock system on a Verisense sensor is in local time (e.g. unix time expressed in your time zone, e.g. for Kuala Lumpur unixtime + 08:00) 
        /// </summary>
        /// <param name="time">The local time</param>
        public void SetEndTimeInMinutes(long time)
        {
            byte[] minutesPassed = new byte[4];
            minutesPassed = BitConverter.GetBytes(time);
            OpConfig.ConfigurationBytes[(int)ConfigurationBytesIndexName.END_TIME] = minutesPassed[0];
            OpConfig.ConfigurationBytes[(int)ConfigurationBytesIndexName.END_TIME + 1] = minutesPassed[1];
            OpConfig.ConfigurationBytes[(int)ConfigurationBytesIndexName.END_TIME + 2] = minutesPassed[2];
            OpConfig.ConfigurationBytes[(int)ConfigurationBytesIndexName.END_TIME + 3] = minutesPassed[3];
        }

        /// <summary>
        /// The clock system on a Verisense sensor is in local time (e.g. unix time expressed in your time zone, e.g. for Kuala Lumpur unixtime + 08:00) 
        /// </summary>
        /// <returns></returns>
        public int GetEndTimeinMinutes()
        {
            byte[] minutesPassed = new byte[4];
            Array.Copy(OpConfig.ConfigurationBytes, (int)ConfigurationBytesIndexName.END_TIME, minutesPassed, 0, 4);
            int time = BitConverter.ToInt32(minutesPassed, 0);
            return time;
        }

        /// <summary>
        /// Converts a Unix time in minutes to UTC+0 time
        /// </summary>
        /// <param name="unixTimeStampInMinute">Unix time in minute</param>
        /// <returns>UTC+0 time</returns>
        public DateTime ConvertUnixTSInMinuteIntoDateTime(int unixTimeStampInMinute)
        {
            DateTimeOffset dateTimeOffset = DateTimeOffset.FromUnixTimeSeconds(unixTimeStampInMinute*60);
            return dateTimeOffset.DateTime;
        }

        /// <summary>
        /// Converts a date time to Unix time in minutes
        /// </summary>
        /// <param name="datetime"></param>
        /// <returns>Unix time in minutes</returns>
        public long ConvertDateTimeIntoUnixTSInMinute(DateTime datetime)
        {
            var date = new DateTime(1970, 1, 1, 0, 0, 0, datetime.Kind);
            var unixTimestamp = System.Convert.ToInt64((datetime - date).TotalMinutes);
            return unixTimestamp;
        }

        /// <summary>
        /// The number of BLE wake-up retries to carry out if there is any pending events
        /// </summary>
        /// <param name="count"></param>
        public void SetBLERetryCount(int count)
        {
            OpConfig.ConfigurationBytes[(int)ConfigurationBytesIndexName.BLE_RETRY_COUNT] = (byte)count;
        }

        /// <summary>
        /// Returns the number of BLE wake-up retries to carry out if there is any pending events
        /// </summary>
        /// <returns></returns>
        public int GetBLERetryCount()
        {
            return OpConfig.ConfigurationBytes[(int)ConfigurationBytesIndexName.BLE_RETRY_COUNT];
        }

        /// <summary>
        /// Bluetooth power setting
        /// </summary>
        /// <param name="power"></param>
        public void SetBLETXPower(int power)
        {
            OpConfig.ConfigurationBytes[(int)ConfigurationBytesIndexName.BLE_TX_POWER] = (byte)power;
        }

        /// <summary>
        /// Returns Bluetooth power setting
        /// </summary>
        /// <returns></returns>
        public DeviceByteSetting GetBLETXPower()
        {
            int power = OpConfig.ConfigurationBytes[(int)ConfigurationBytesIndexName.BLE_TX_POWER];
            DeviceByteSetting powerSetting = GetDeviceSettingFromConfigurationValue(BT5RadioOutputPower.Settings, power);
            return powerSetting;
        }

        /// <summary>
        /// Data transfer interval in hours
        /// </summary>
        /// <param name="interval">interval in hours</param>
        public void SetDataTransferInterval(int interval)
        {
            OpConfig.ConfigurationBytes[(int)ConfigurationBytesIndexName.BLE_DATA_TRANS_WKUP_INT_HRS] = (byte)interval;
        }

        /// <summary>
        /// Data transfer interval in hours
        /// </summary>
        /// <returns>interval in hours</returns>
        public int GetDataTransferInterval()
        {
            return OpConfig.ConfigurationBytes[(int)ConfigurationBytesIndexName.BLE_DATA_TRANS_WKUP_INT_HRS];
        }

        /// <summary>
        /// Data transfer start time in minutes
        /// </summary>
        /// <param name="startTime">start time in minutes eg 540 if start time is 9:00am</param>
        public void SetDataTransferStartTime(int startTime)
        {
            byte[] minutesUntilStart = new byte[2];
            minutesUntilStart = BitConverter.GetBytes(startTime);
            OpConfig.ConfigurationBytes[(int)ConfigurationBytesIndexName.BLE_DATA_TRANS_WKUP_TIME] = minutesUntilStart[0];
            OpConfig.ConfigurationBytes[(int)ConfigurationBytesIndexName.BLE_DATA_TRANS_WKUP_TIME + 1] = minutesUntilStart[1];
        }

        /// <summary>
        /// Data transfer start time in minutes
        /// </summary>
        /// <returns>start time in minutes eg 540 if start time is 9:00am</returns>
        public int GetDataTransferStartTime()
        {
            byte[] minutesUntilStart = new byte[4];
            Array.Copy(OpConfig.ConfigurationBytes, (int)ConfigurationBytesIndexName.BLE_DATA_TRANS_WKUP_TIME, minutesUntilStart, 0, 2);
            int time = BitConverter.ToInt32(minutesUntilStart, 0);
            return time;
        }

        /// <summary>
        /// The duration in minutes for the device to wait for a connection for data transfer
        /// </summary>
        /// <param name="duration">duration in minutes</param>
        public void SetDataTransferDuration(int duration)
        {
            OpConfig.ConfigurationBytes[(int)ConfigurationBytesIndexName.BLE_DATA_TRANS_WKUP_DUR] = (byte)duration;
        }

        /// <summary>
        /// The duration in minutes for the device to wait for a connection
        /// </summary>
        /// <returns>duration in minutes</returns>
        public int GetDataTransferDuration()
        {
            return OpConfig.ConfigurationBytes[(int)ConfigurationBytesIndexName.BLE_DATA_TRANS_WKUP_DUR];
        }

        /// <summary>
        /// The number of minute's interval the ASM sensor will wait before re-trying to transfer its data in the event of a previous data transfer failure.
        /// </summary>
        /// <param name="interval">retry interval in minutes</param>
        public void SetDataTransferRetryInterval(int interval)
        {
            byte[] retryInterval = new byte[2];
            retryInterval = BitConverter.GetBytes(interval);
            OpConfig.ConfigurationBytes[(int)ConfigurationBytesIndexName.BLE_DATA_TRANS_RETRY_INT] = retryInterval[0];
            OpConfig.ConfigurationBytes[(int)ConfigurationBytesIndexName.BLE_DATA_TRANS_RETRY_INT + 1] = retryInterval[1];
        }

        /// <summary>
        /// The number of minute's interval the ASM sensor will wait before re-trying to transfer its data in the event of a previous data transfer failure.
        /// </summary>
        /// <returns>retry interval in minutes</returns>
        public int GetDataTransferRetryInterval()
        {
            byte[] retryInterval = new byte[4];
            Array.Copy(OpConfig.ConfigurationBytes, (int)ConfigurationBytesIndexName.BLE_DATA_TRANS_RETRY_INT, retryInterval, 0, 2);
            int time = BitConverter.ToInt32(retryInterval, 0);
            return time;
        }

        /// <summary>
        /// The number of hours interval between each of the status report.
        /// </summary>
        /// <param name="interval">interval in hours</param>
        public void SetStatusInterval(int interval)
        {
            OpConfig.ConfigurationBytes[(int)ConfigurationBytesIndexName.BLE_STATUS_WKUP_INT_HRS] = (byte)interval;
        }

        /// <summary>
        /// The number of hours interval between each of the status report.
        /// </summary>
        /// <returns>interval in hours</returns>
        public int GetStatusInterval()
        {
            return OpConfig.ConfigurationBytes[(int)ConfigurationBytesIndexName.BLE_STATUS_WKUP_INT_HRS];
        }

        /// <summary>
        /// The time that the status is sent by the sensor
        /// </summary>
        /// <param name="time">start time in minutes eg 540 if start time is 9:00am</param>
        public void SetStatusStartTime(int time)
        {
            byte[] startTime = new byte[2];
            startTime = BitConverter.GetBytes(time);
            OpConfig.ConfigurationBytes[(int)ConfigurationBytesIndexName.BLE_STATUS_WKUP_TIME] = startTime[0];
            OpConfig.ConfigurationBytes[(int)ConfigurationBytesIndexName.BLE_STATUS_WKUP_TIME + 1] = startTime[1];
        }

        /// <summary>
        /// The time that the status is sent by the sensor
        /// </summary>
        /// <returns>start time in minutes eg 540 if start time is 9:00am</returns>
        public int GetStatusStartTime()
        {
            byte[] startTime = new byte[4];
            Array.Copy(OpConfig.ConfigurationBytes, (int)ConfigurationBytesIndexName.BLE_STATUS_WKUP_TIME, startTime, 0, 2);
            int time = BitConverter.ToInt32(startTime, 0);
            return time;
        }

        /// <summary>
        /// The duration in minutes for the device to wait for a connection for status transfer
        /// </summary>
        /// <param name="interval">duration in minutes</param>
        public void SetStatusDuration(int interval)
        {
            OpConfig.ConfigurationBytes[(int)ConfigurationBytesIndexName.BLE_STATUS_WKUP_DUR] = (byte)interval;
        }

        /// <summary>
        /// The duration in minutes for the device to wait for a connection for status transfer
        /// </summary>
        /// <returns>duration in minutes</returns>
        public int GetStatusDuration()
        {
            return OpConfig.ConfigurationBytes[(int)ConfigurationBytesIndexName.BLE_STATUS_WKUP_DUR];
        }

        /// <summary>
        /// The number of minute's interval the ASM sensor will wait before re-trying to send its status in the event of a previous data transfer failure
        /// </summary>
        /// <param name="interval">retry interval in minutes</param>
        public void SetStatusRetryInterval(int interval)
        {
            byte[] retryInterval = new byte[2];
            retryInterval = BitConverter.GetBytes(interval);
            OpConfig.ConfigurationBytes[(int)ConfigurationBytesIndexName.BLE_STATUS_RETRY_INT] = retryInterval[0];
            OpConfig.ConfigurationBytes[(int)ConfigurationBytesIndexName.BLE_STATUS_RETRY_INT + 1] = retryInterval[1];
        }

        /// <summary>
        /// The number of minute's interval the ASM sensor will wait before re-trying to send its status in the event of a previous data transfer failure
        /// </summary>
        /// <returns>retry interval in minutes</returns>
        public int GetStatusRetryInterval()
        {
            byte[] retryInterval = new byte[4];
            Array.Copy(OpConfig.ConfigurationBytes, (int)ConfigurationBytesIndexName.BLE_STATUS_RETRY_INT, retryInterval, 0, 2);
            int time = BitConverter.ToInt32(retryInterval, 0);
            return time;
        }

        /// <summary>
        /// Real-world clock synchronisation interval in hours
        /// </summary>
        /// <param name="interval">interval in hours</param>
        public void SetRTCSyncInterval(int interval)
        {
            OpConfig.ConfigurationBytes[(int)ConfigurationBytesIndexName.BLE_RTC_SYNC_WKUP_INT_HRS] = (byte)interval;
        }

        /// <summary>
        /// Real-world clock synchronisation interval in hours
        /// </summary>
        /// <returns>interval in hours</returns>
        public int GetRTCSyncInterval()
        {
            return OpConfig.ConfigurationBytes[(int)ConfigurationBytesIndexName.BLE_RTC_SYNC_WKUP_INT_HRS];
        }

        /// <summary>
        /// Real-world clock synchronisation start time in minutes
        /// </summary>
        /// <param name="time">start time in minutes eg 540 if start time is 9:00am</param>
        public void SetRTCSyncTime(int time)
        {
            byte[] startTime = new byte[2];
            startTime = BitConverter.GetBytes(time);
            OpConfig.ConfigurationBytes[(int)ConfigurationBytesIndexName.BLE_RTC_SYNC_WKUP_TIME] = startTime[0];
            OpConfig.ConfigurationBytes[(int)ConfigurationBytesIndexName.BLE_RTC_SYNC_WKUP_TIME + 1] = startTime[1];
        }

        /// <summary>
        /// Real-world clock synchronisation start time in minutes
        /// </summary>
        /// <returns>start time in minutes eg 540 if start time is 9:00am</returns>
        public int GetRTCSyncTime()
        {
            byte[] startTime = new byte[4];
            Array.Copy(OpConfig.ConfigurationBytes, (int)ConfigurationBytesIndexName.BLE_RTC_SYNC_WKUP_TIME, startTime, 0, 2);
            int time = BitConverter.ToInt32(startTime, 0);
            return time;
        }

        /// <summary>
        /// The duration in minutes for the device to wait for a connection for real-world clock synchronisation
        /// </summary>
        /// <param name="duration">duration in minutes</param>
        public void SetRTCSyncDuration(int duration)
        {
            OpConfig.ConfigurationBytes[(int)ConfigurationBytesIndexName.BLE_RTC_SYNC_WKUP_DUR] = (byte)duration;
        }

        /// <summary>
        /// The duration in minutes for the device to wait for a connection for real-world clock synchronisation
        /// </summary>
        /// <returns>duration in minutes</returns>
        public int GetRTCSyncDuration()
        {
            return OpConfig.ConfigurationBytes[(int)ConfigurationBytesIndexName.BLE_RTC_SYNC_WKUP_DUR];
        }

        /// <summary>
        /// The number of minute's interval the ASM sensor will wait before re-trying real-world clock synchronisation in the event of a previous real-world clock synchronisation failure
        /// </summary>
        /// <param name="interval">interval in minutes</param>
        public void SetRTCSyncRetryInterval(int interval)
        {
            byte[] retryInterval = new byte[2];
            retryInterval = BitConverter.GetBytes(interval);
            OpConfig.ConfigurationBytes[(int)ConfigurationBytesIndexName.BLE_RTC_SYNC_RETRY_INT] = retryInterval[0];
            OpConfig.ConfigurationBytes[(int)ConfigurationBytesIndexName.BLE_RTC_SYNC_RETRY_INT + 1] = retryInterval[1];
        }

        /// <summary>
        /// The number of minute's interval the ASM sensor will wait before re-trying real-world clock synchronisation in the event of a previous real-world clock synchronisation failure
        /// </summary>
        /// <returns>interval in minutes</returns>
        public int GetRTCSyncRetryInterval()
        {
            byte[] retryInterval = new byte[4];
            Array.Copy(OpConfig.ConfigurationBytes, (int)ConfigurationBytesIndexName.BLE_RTC_SYNC_RETRY_INT, retryInterval, 0, 2);
            int time = BitConverter.ToInt32(retryInterval, 0);
            return time;
        }

        /// <summary>
        /// The number of minute’s interval the ASM sensor will wait after a failed connection attempt before turning on the scheduler again
        /// </summary>
        /// <param name="interval">interval in minutes, if this value is set to either 0 or 65535 then the adaptive scheduler will never be turned on</param>
        public void SetAdaptiveSchedulerInterval(int interval)
        {
            byte[] adaptiveSchedulerInterval = new byte[2];
            adaptiveSchedulerInterval = BitConverter.GetBytes(interval);
            OpConfig.ConfigurationBytes[(int)ConfigurationBytesIndexName.ADAPTIVE_SCHEDULER_INT] = adaptiveSchedulerInterval[0];
            OpConfig.ConfigurationBytes[(int)ConfigurationBytesIndexName.ADAPTIVE_SCHEDULER_INT + 1] = adaptiveSchedulerInterval[1];
        }

        /// <summary>
        /// The number of minute’s interval the ASM sensor will wait after a failed connection attempt before turning on the scheduler again
        /// </summary>
        /// <returns>interval in minutes</returns>
        public int GetAdaptiveSchedulerInterval()
        {
            byte[] adaptiveSchedulerInterval = new byte[4];
            Array.Copy(OpConfig.ConfigurationBytes, (int)ConfigurationBytesIndexName.ADAPTIVE_SCHEDULER_INT, adaptiveSchedulerInterval, 0, 2);
            int time = BitConverter.ToInt32(adaptiveSchedulerInterval, 0);
            return time;
        }

        /// <summary>
        /// Each time the sensor fails to clear all pending events during a scheduled wake-up event, a fail counter is incremented. 
        /// When the fail counter reaches the adaptive scheduler maximum fail count, the sensor will turn on the adaptive scheduler and the scheduler will be set to wake-up in <see cref="GetAdaptiveSchedulerInterval"/> minutes
        /// </summary>
        /// <param name="count">adaptive scheduler maximum fail count</param>
        public void SetAdaptiveSchedulerMaxFailCount(int count)
        {
            OpConfig.ConfigurationBytes[(int)ConfigurationBytesIndexName.ADAPTIVE_SCHEDULER_FAILCOUNT_MAX] = (byte)count;
        }

        /// <summary>
        /// Each time the sensor fails to clear all pending events during a scheduled wake-up event, a fail counter is incremented. 
        /// When the fail counter reaches the adaptive scheduler maximum fail count, the sensor will turn on the adaptive scheduler and the scheduler will be set to wake-up in <see cref="GetAdaptiveSchedulerInterval"/> minutes
        /// </summary>
        /// <returns>adaptive scheduler maximum fail count</returns>
        public int GetAdaptiveSchedulerMaxFailCount()
        {
            return OpConfig.ConfigurationBytes[(int)ConfigurationBytesIndexName.ADAPTIVE_SCHEDULER_FAILCOUNT_MAX];
        }

        /// <summary>
        /// Is the logging of data enabled
        /// </summary>
        /// <exception cref="Exception">Thrown if op config is null</exception>
        public bool IsLoggingEnabled()
        {
            if (OpConfig.ConfigurationBytes == null)
            {
                throw new Exception("Configuration Bytes Unknown");
            }
            if ((int)(OpConfig.ConfigurationBytes[(int)ConfigurationBytesIndexName.GEN_CFG_0] & 0b00000001) == 1)
            {
                return true;
            }
            else
            {
                return false;
            }
        }

        /// <summary>
        /// Is the sensor enabled
        /// </summary>
        /// <exception cref="Exception">Thrown if op config is null</exception>
        public bool IsDeviceEnabled()
        {
            if (OpConfig.ConfigurationBytes == null)
            {
                throw new Exception("Configuration Bytes Unknown");
            }
            if ((int)(OpConfig.ConfigurationBytes[(int)ConfigurationBytesIndexName.GEN_CFG_0] & 0b00000010) == 2)
            {
                return true;
            }
            else
            {
                return false;
            }
        }

        /// <summary>
        /// Is bluetooth enabled (bluetooth will always be enabled when USB powered)
        /// </summary>
        /// <exception cref="Exception">Thrown if op config is null</exception>
        public bool IsBluetoothEnabled()
        {
            if (OpConfig.ConfigurationBytes == null)
            {
                throw new Exception("Configuration Bytes Unknown");
            }
            if ((int)(OpConfig.ConfigurationBytes[(int)ConfigurationBytesIndexName.GEN_CFG_0] & 0b00010000) == 16)
            {
                return true;
            }
            else
            {
                return false;
            }
        }

        /// <summary>
        /// Is USB enabled
        /// </summary>
        /// <exception cref="Exception">Thrown if op config is null</exception>
        public bool IsUSBEnabled()
        {
            if (OpConfig.ConfigurationBytes == null)
            {
                throw new Exception("Configuration Bytes Unknown");
            }
            if ((int)(OpConfig.ConfigurationBytes[(int)ConfigurationBytesIndexName.GEN_CFG_0] & 0b00001000) == 8)
            {
                return true;
            }
            else
            {
                return false;
            }
        }

        /// <summary>
        /// Return sensor based on the sensor name or null if sensor not found
        /// </summary>
        /// <param name="key">The sensor name eg "Accel1"</param>
        /// <returns></returns>
        public Sensor GetSensor(string key)
        {
            foreach (Sensor sensor in SensorList.Values)
            {
                if (sensor.GetSensorName().Equals(key))
                {
                    return sensor;
                }
            }
            return null;
        }

        /// <summary>
        /// Update device and sensor configuration. It is called when prod config or op config are updated
        /// </summary>
        public void UpdateDeviceAndSensorConfiguration()
        {
            foreach (Sensor sensor in SensorList.Values)
            {
                if (ProdConfig != null)
                {
                    sensor.SetDeviceHardwareIdentifier(ProdConfig.HardwareIdentifier);
                } else
                {
                    sensor.SetDeviceHardwareIdentifier(HardwareIdentifier.UNKNOWN);
                }
                if (OpConfig != null)
                {
                    sensor.InitializeUsingOperationConfig(OpConfig.ConfigurationBytes);
                }
            }
        }
        /// <summary>
        /// Generate configuration bytes based on current sensors configuration
        /// </summary>
        /// <returns></returns>
        public virtual byte[] GenerateConfigurationBytes()
        {
            var configBytes = OpConfig.ConfigurationBytes;
            foreach (Sensor sensor in SensorList.Values)
            {
                configBytes = sensor.GenerateOperationConfig(configBytes);
            }
            return configBytes;
        }

        /// <summary>
        /// Returns operational config byte array
        /// </summary>
        /// <returns></returns>
        public byte[] GetOperationalConfigByteArray()
        {
            return OpConfig.ConfigurationBytes;
        }

        /// <summary>
        /// Returns production config byte array
        /// </summary>
        /// <returns></returns>
        public byte[] GetProductionConfigByteArray()
        {
            return BitHelper.MSBByteArray(ProdConfig.Payload.Replace("-", "")).ToArray();
        }

        /// <summary>
        /// Returns status payload
        /// </summary>
        /// <returns></returns>
        public StatusPayload GetStatus()
        {
            return Status;
        }

        /// <summary>
        /// Returns log events payload
        /// </summary>
        /// <returns></returns>
        public LogEventsPayload GetLogEvents()
        {
            return LogEvents;
        }

        /// <summary>
        /// Returns time payload
        /// </summary>
        public TimePayload GetLastReceivedRTC()
        {
            return Time;
        }

        /// <summary>
        /// Returns production config payload
        /// </summary>
        /// <returns></returns>
        public ProdConfigPayload GetProductionConfig()
        {
            return ProdConfig;
        }

        /// <summary>
        /// Calibrate ADC Value to millivolts. Output varies based on id
        /// </summary>
        /// <param name="uncalValue"></param>
        /// <param name="id">the hardware identifier enum</param>
        /// <returns>Voltage in millivolts</returns>
        public static double CalibrateADCValueToMilliVolts(int uncalValue, HardwareIdentifier id)
        {
            return CalibrateADCValueToVolts(uncalValue, id)*1000;
        }

        /// <summary>
        /// Calibrate ADC Value to volts. Output varies based on id
        /// </summary>
        /// <param name="uncalValue"></param>
        /// <param name="id">the hardware identifier enum</param>
        /// <returns>Voltage in volts</returns>
        public static double CalibrateADCValueToVolts(int uncalValue, HardwareIdentifier id)
        {
            var adcRange = Math.Pow(2, 12) - 1;  // 12-bit
            var refVoltage = (1.8 / 4.0);
            if (id.Equals(HardwareIdentifier.VERISENSE_GSR_PLUS))
            {
                refVoltage = (3.0 / 4.0);
            }
            var adcScaling = (1.0 / 4.0);
            var adcOffset = 0;
            var calValue = ((((uncalValue - adcOffset) * refVoltage) / adcRange) / adcScaling);
            return calValue;
        }

        /// <summary>
        /// Search for settings using the display name in the array provided. Returns <see cref="DefaultVerisenseConfiguration.Unknown_Device_OpConfig_Setting"/> if setting not found
        /// </summary>
        /// <param name="settings"></param>
        /// <param name="displayName">the display name of the setting</param>
        public static DeviceByteArraySettings GetDeviceOpSettingFromDisplayName(DeviceByteArraySettings[] settings, string displayName)
        {
            foreach (DeviceByteArraySettings setting in settings)
            {
                if (setting.GetDisplayName().Equals(displayName))
                {
                    return setting;
                }
            }
            return DefaultVerisenseConfiguration.Unknown_Device_OpConfig_Setting;
        }

        /// <summary>
        /// Each instance of this class represents a group of device settings
        /// </summary>
        public class DeviceByteArraySettings
        {
            protected string DisplayName;
            protected byte[] OperationalConfigurationBytes;
            protected string Description = "";

            /// <summary>
            /// Create a device byte array settings
            /// </summary>
            /// <param name="displayName"></param>
            /// <param name="operationalConfigurationBytes"></param>
            /// <param name="description"></param>
            public DeviceByteArraySettings(string displayName, byte[] operationalConfigurationBytes, string description)
            {
                DisplayName = displayName;
                OperationalConfigurationBytes = operationalConfigurationBytes;
                Description = description;
            }

            /// <summary>
            /// Returns the display name
            /// </summary>
            public string GetDisplayName() { return DisplayName; }
            /// <summary>
            /// Returns the operational configuration bytes
            /// </summary>
            public byte[] GetOperationalConfigurationBytes() { return OperationalConfigurationBytes; }
            /// <summary>
            /// Returns the description
            /// </summary>
            public string GetDescription() { return Description; }
        }

        /// <summary>
        /// Returns true if the input version is smaller or equal
        /// </summary>
        /// <param name="compMajor"></param>
        /// <param name="compMinor"></param>
        /// <param name="compInternal"></param>
        /// <exception cref="Exception">Thrown if prod config is null</exception>
        /// <returns></returns>
        public bool MeetsMinimumFWRequirement(int compMajor, int compMinor, int compInternal)
        {
            if (ProdConfig != null)
            {
                if ((compMajor < ProdConfig.REV_FW_MAJOR)
                        || (ProdConfig.REV_FW_MAJOR == compMajor && compMinor < ProdConfig.REV_FW_MINOR)
                        || (ProdConfig.REV_FW_MAJOR == compMajor && ProdConfig.REV_FW_MINOR == compMinor && compInternal <= ProdConfig.REV_FW_INTERNAL))
                {
                    return true; // if the version is smaller or equal 
                }
            } else
            {
                throw new Exception("Production Config Unknown");
            }
            return false; // if less
        }

        /// <summary>
        /// The first firmware on which BLUETOOTH_EN = 0 leaves USB working, V2.01.003 (DEV-1096).
        /// Before it, ASM_Production handled USB only while its SoftDevice was on, and started the
        /// SoftDevice only for Bluetooth. A sensor with Bluetooth off was then reachable over
        /// neither: USB never enumerated, and a write that turned Bluetooth off over USB stopped
        /// USB at once. Only SWD could recover it.
        /// </summary>
        public const int BluetoothOffMinFwMajor = 2;
        public const int BluetoothOffMinFwMinor = 1;
        public const int BluetoothOffMinFwInternal = 3;

        const byte GenCfg0BluetoothEnabledMask = 0b00010000;

        /// <summary>
        /// Whether the sensor's firmware can run with Bluetooth disabled. False when its version is
        /// unknown: no production config read, or a major version of 0xFF, the erased EEPROM value,
        /// which no release has.
        /// </summary>
        /// <returns></returns>
        public bool SupportsBluetoothOff()
        {
            if (ProdConfig == null || ProdConfig.REV_FW_MAJOR == 0xFF)
            {
                return false;
            }
            return MeetsMinimumFWRequirement(BluetoothOffMinFwMajor, BluetoothOffMinFwMinor, BluetoothOffMinFwInternal);
        }

        /// <summary>
        /// Keep Bluetooth enabled in operational config bytes bound for this sensor if its firmware
        /// cannot run without it (<see cref="SupportsBluetoothOff"/>). That includes firmware older
        /// than V2.00.007, which ignores BLUETOOTH_EN: the bit still stays in the sensor's EEPROM,
        /// and an update to V2.00.007 - V2.01.002 would then strand the sensor.
        /// </summary>
        /// <param name="opConfig">The operational config, from its 0x5A header; changed in place</param>
        /// <returns>True if a correction was applied</returns>
        public bool EnforceBluetoothOffFirmwareGuard(byte[] opConfig)
        {
            int index = (int)ConfigurationBytesIndexName.GEN_CFG_0;
            if (opConfig == null || opConfig.Length <= index)
            {
                return false;
            }
            if ((opConfig[index] & GenCfg0BluetoothEnabledMask) != 0 || SupportsBluetoothOff())
            {
                return false;
            }
            opConfig[index] |= GenCfg0BluetoothEnabledMask;
            return true;
        }

        /// <summary>
        /// Search for setting using the configuration byte in the array provided
        /// </summary>
        /// <param name="settings"></param>
        /// <param name="value">the configuration byte value</param>
        /// <returns></returns>
        public static DeviceByteSetting GetDeviceSettingFromConfigurationValue(DeviceByteSetting[] settings, int value)
        {
            foreach (DeviceByteSetting setting in settings)
            {
                if (setting.GetConfigurationByte() == value)
                {
                    return setting;
                }
            }
            return new DeviceByteSetting("Unknown", -1, -1);
        }

        /// <summary>
        /// Search for setting using the display name in the array provided
        /// </summary>
        /// <param name="settings"></param>
        /// <param name="displayName"></param>
        /// <returns></returns>
        public static DeviceByteSetting GetDeviceOpSettingFromDisplayName(DeviceByteSetting[] settings, string displayName)
        {
            foreach (DeviceByteSetting setting in settings)
            {
                if (setting.GetDisplayName().Equals(displayName))
                {
                    return setting;
                }
            }
            return Unknown_Device_Setting;
        }

        /// <summary>
        /// Each instance of this class represents a device setting
        /// </summary>
        public class DeviceByteSetting
        {
            protected string DisplayName;
            protected int OperationalConfigurationByte;
            protected Object SettingsValue;
            protected string Description = "";

            /// <summary>
            /// Create a new device setting
            /// </summary>
            /// <param name="displayName"></param>
            /// <param name="operationalConfigurationByte"></param>
            /// <param name="settingsValue"></param>
            public DeviceByteSetting(string displayName, int operationalConfigurationByte, Object settingsValue)
            {
                DisplayName = displayName;
                OperationalConfigurationByte = operationalConfigurationByte;
                SettingsValue = settingsValue;
            }

            /// <summary>
            /// Returns display name
            /// </summary>
            public string GetDisplayName() { return DisplayName; }
            /// <summary>
            /// Returns operational configuration byte
            /// </summary>
            public int GetConfigurationByte() { return OperationalConfigurationByte; }
            /// <summary>
            /// Returns setting value
            /// </summary>
            public Object GetSettingsValue() { return SettingsValue; }
            /// <summary>
            /// Returns description
            /// </summary>
            public string GetDescription() { return Description; }

        }
    }
}
