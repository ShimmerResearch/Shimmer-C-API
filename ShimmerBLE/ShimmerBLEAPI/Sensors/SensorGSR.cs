using shimmer.Helpers;
using shimmer.Sensors;
using ShimmerAPI;
using ShimmerAPI.Utilities;
using ShimmerBLEAPI.Devices;
using System;
using System.Collections.Generic;
using System.Text;
using static shimmer.Models.OpConfigPayload;
using static ShimmerBLEAPI.Devices.VerisenseDevice;

namespace shimmer.Sensors
{
    /// <summary>
	/// This class contains the configuration settings for sensor GSR. Every sensor should belong to a device
	/// </summary>
    public class SensorGSR : Sensor
    {

        protected SensorSetting GSRRangeSetting = Sensor.UnknownSetting;
        protected SensorSetting GSRRateSetting = Sensor.UnknownSetting;
        protected SensorSetting GSROversamplingRateSetting = Sensor.UnknownSetting;
        public const double LIMIT_FOR_MINIMUM_VALID_GSR_CONDUCTANCE_US = 0.03;
        public const int GSR_UNCAL_LIMIT_RANGE3_SR62 = 683;
        /// <summary>
        /// Range-3 codes below this are raised to it before calibration so that an open circuit
        /// reads as open, which only works if the limit is above the amplifier reference. 1138 is
        /// the first code above 0.5 V at the SR68's 1.8 V full scale (0.5 V = code 1137.5), so it
        /// also clears the 0.4986 V that CalibrateGsrDataToKOhmsUsingAmplifierEq divides by (code
        /// 1134.3); the Java driver divides by 0.5 V, and 1138 is correct under both. It was 1134,
        /// the last code below 0.4986 V: that decoded to a negative resistance, nudged to 8 kOhm,
        /// so an open circuit read 125 uS and counted as Connected (DEV-1067).
        /// </summary>
        public const int GSR_UNCAL_LIMIT_RANGE3_SR68 = 1138;

        /// <summary>
        /// GSR range setting
        /// </summary>
        public static class GSRRange
        {
            public static readonly SensorSetting Range_Unknown = Sensor.UnknownSetting;
            public static readonly SensorSetting Range_0 = new SensorSetting("Range 0", 0, new double[] { 8.0, 63.0 }, "8.0kOhm to 63.0kOhm");
            public static readonly SensorSetting Range_1 = new SensorSetting("Range 1", 1, new double[] { 63.0, 220.0 }, "63.0kOhm to 220.0kOhm");
            public static readonly SensorSetting Range_2 = new SensorSetting("Range 2", 2, new double[] { 220.0, 680.0 }, "220.0kOhm to 680.0kOhm");
            public static readonly SensorSetting Range_3 = new SensorSetting("Range 3", 3, new double[] { 680.0, 4700.0 }, "680.0kOhm to 4700.0kOhm");
            public static readonly SensorSetting Range_Auto = new SensorSetting("Auto Range", 4, new double[] { 8.0, 4700.0 }, "8.0kOhm to 4700.0kOhm");
            public static readonly SensorSetting[] Settings = { Range_Unknown, Range_0, Range_1, Range_2, Range_3, Range_Auto };
        }
        /// <summary>
        /// GSR sample rate setting
        /// </summary>
        public static class GSRRate
        {
            public static readonly SensorSetting Rate_Unknown = Sensor.UnknownSetting;
            public static readonly SensorSetting Freq_5_12Hz = new SensorSetting("5.12Hz", 33, 5.12);
            public static readonly SensorSetting Freq_10_24Hz = new SensorSetting("10.24Hz", 30, 10.24);
            public static readonly SensorSetting Freq_20_48Hz = new SensorSetting("20.48Hz", 27, 20.48);
            public static readonly SensorSetting Freq_51_2Hz = new SensorSetting("51.2Hz", 23, 51.2);
            public static readonly SensorSetting Freq_128Hz = new SensorSetting("128Hz", 19, 128);
            public static readonly SensorSetting[] Settings = { Rate_Unknown, Freq_5_12Hz, Freq_10_24Hz, Freq_20_48Hz, Freq_51_2Hz, Freq_128Hz };
        }
        /// <summary>
        /// ADC oversampling rate setting
        /// </summary>
        public static class ADCOversamplingRate
        {
            public static readonly SensorSetting ADC_Oversampling_Unknown = Sensor.UnknownSetting;
            public static readonly SensorSetting ADC_Oversampling_Disabled = new SensorSetting("ADC_Oversampling_Disabled", 0, 0);
            public static readonly SensorSetting ADC_Oversampling_2x = new SensorSetting("ADC_Oversampling_2x", 1, 2);
            public static readonly SensorSetting ADC_Oversampling_4x = new SensorSetting("ADC_Oversampling_4x", 2, 4);
            public static readonly SensorSetting ADC_Oversampling_8x = new SensorSetting("ADC_Oversampling_8x", 3, 8);
            public static readonly SensorSetting ADC_Oversampling_16x = new SensorSetting("ADC_Oversampling_16x", 4, 16);
            public static readonly SensorSetting ADC_Oversampling_32x = new SensorSetting("ADC_Oversampling_32x", 5, 32);
            public static readonly SensorSetting ADC_Oversampling_64x = new SensorSetting("ADC_Oversampling_64x", 6, 64);
            public static readonly SensorSetting ADC_Oversampling_128x = new SensorSetting("ADC_Oversampling_128x", 7, 128);
            public static readonly SensorSetting ADC_Oversampling_256x = new SensorSetting("ADC_Oversampling_256x", 8, 256);
            public static readonly SensorSetting[] Settings = { ADC_Oversampling_Unknown, ADC_Oversampling_Disabled, ADC_Oversampling_2x, ADC_Oversampling_4x, ADC_Oversampling_8x, ADC_Oversampling_16x, ADC_Oversampling_32x, ADC_Oversampling_64x, ADC_Oversampling_128x, ADC_Oversampling_256x };
        }
        /// <summary>
        /// Unknown means the GSR sensor is disabled, connected means the GSR electrodes are very likely to have contact with the subject, disconnected means the GSR electrodes are unlikely to have contact with the subject
        /// </summary>
        public enum GSRConnectivityLevel
        {
            Unknown,
            Disconnected,
            Connected
        }

        protected double[] SHIMMER3_GSR_REF_RESISTORS_KOHMS = new double[] {
    40.2, 		//# Range 0
    287.0, 		//# Range 1
    1000.0, 	//# Range 2
    3300.0}; 	//# Range 3

        protected double[] SR68_GSR_REF_RESISTORS_KOHMS = new double[] {
    21.0, 		//# Range 0
    150.0, 		//# Range 1
    562.0,  	//# Range 2
    1740.0};    //# Range 3

        protected bool GSREnabled;
        protected bool BattEnabled;
        public static readonly string SensorName = "GSR";
        private GSRConnectivityLevel ConnectivityCheck = GSRConnectivityLevel.Unknown;

        /// <summary>
        /// This is used to store and retrieve the sensor data in the object cluster
        /// </summary>
        public static class ObjectClusterSensorName
        {
            public static String GSR = "GSR";
            public static String Batt = "Batt";
        }
        /// <summary>
        /// Unknown mean the GSR sensor is disabled, connected means the GSR electrodes are very likely to have contact with the subject, disconnected means the GSR electrodes are unlikely to have contact with the subject
        /// </summary>
        /// <returns></returns>
        public GSRConnectivityLevel GetGSRConnectivityLevel()
        {
            return ConnectivityCheck;
        }

        /// <summary>
		/// Update the operational configuration byte array based on current sensor configuration
		/// </summary>
		/// <param name="operationalConfigBytes">byte array to be update</param>
		/// <returns></returns>
        public override byte[] GenerateOperationConfig(byte[] operationalConfigBytes)
        {

            if (GSREnabled)
            {
                operationalConfigBytes[(int)ConfigurationBytesIndexName.GEN_CFG_1] = (byte)(operationalConfigBytes[(int)ConfigurationBytesIndexName.GEN_CFG_1] | 0b10000000);
            } else
            {
                operationalConfigBytes[(int)ConfigurationBytesIndexName.GEN_CFG_1] = (byte)(operationalConfigBytes[(int)ConfigurationBytesIndexName.GEN_CFG_1] & 0b01111111);
            }

            if (BattEnabled)
            {
                operationalConfigBytes[(int)ConfigurationBytesIndexName.GEN_CFG_2] = (byte)(operationalConfigBytes[(int)ConfigurationBytesIndexName.GEN_CFG_2] | 0b00000010);
            }
            else
            {
                operationalConfigBytes[(int)ConfigurationBytesIndexName.GEN_CFG_2] = (byte)(operationalConfigBytes[(int)ConfigurationBytesIndexName.GEN_CFG_2] & 0b11111101);
            }

            operationalConfigBytes[(int)ConfigurationBytesIndexName.ADC_CHANNEL_SETTINGS_0] = (byte)((operationalConfigBytes[(int)ConfigurationBytesIndexName.ADC_CHANNEL_SETTINGS_0] & 0b11000000) | (GSRRateSetting.GetConfigurationValue()));
            operationalConfigBytes[(int)ConfigurationBytesIndexName.ADC_CHANNEL_SETTINGS_1] = (byte)((operationalConfigBytes[(int)ConfigurationBytesIndexName.ADC_CHANNEL_SETTINGS_1] & 0b11111000) | (GSRRangeSetting.GetConfigurationValue()));
            operationalConfigBytes[(int)ConfigurationBytesIndexName.ADC_CHANNEL_SETTINGS_1] = (byte)((operationalConfigBytes[(int)ConfigurationBytesIndexName.ADC_CHANNEL_SETTINGS_1] & 0b00001111) | (GSROversamplingRateSetting.GetConfigurationValue() << 4));

            return operationalConfigBytes;
        }

        /// <summary>
		/// Returns the sensor name
		/// </summary>
		/// <returns></returns>
        public override string GetSensorName()
        {
            return SensorName;
        }

        /// <summary>
		/// Initialize the configuration settings using the operational config bytes provided
		/// </summary>
		/// <param name="operationalConfigBytes"></param>
        public override void InitializeUsingOperationConfig(byte[] operationalConfigBytes)
        {
            if ((operationalConfigBytes[(int)ConfigurationBytesIndexName.GEN_CFG_1] >> 7) == 1)
            {
                GSREnabled = true;
            }
            else
            {
                GSREnabled = false;
            }

            if ((operationalConfigBytes[(int)ConfigurationBytesIndexName.GEN_CFG_2] & 0b00000010) > 1)
            {
                BattEnabled = true;
            }
            else
            {
                BattEnabled = false;
            }

            int rate = operationalConfigBytes[(int)ConfigurationBytesIndexName.ADC_CHANNEL_SETTINGS_0] & 0b00111111;
            GSRRateSetting = GetSensorSettingFromConfigurationValue(GSRRate.Settings, rate);
            int range = operationalConfigBytes[(int)ConfigurationBytesIndexName.ADC_CHANNEL_SETTINGS_1] & 0b00000111;
            GSRRangeSetting = GetSensorSettingFromConfigurationValue(GSRRange.Settings, range);
            int oversamplingRate = (operationalConfigBytes[(int)ConfigurationBytesIndexName.ADC_CHANNEL_SETTINGS_1] >> 4);
            GSROversamplingRateSetting = GetSensorSettingFromConfigurationValue(ADCOversamplingRate.Settings, oversamplingRate);
        }

        /// <summary>
		/// Parse the raw payload data received
		/// </summary>
		/// <param name="payload">the payload data that is received from the sensor</param>
		/// <param name="deviceID">the uuid for the address of which is used to connect to via BLE "00000000-0000-0000-0000-e7452c6d6f14" note that the uuid across OS (android vs iOS) can differ</param>
		/// <returns></returns>
        public override List<ObjectCluster> ParsePayloadData(byte[] payload, string deviceID)
        {
            var numberofBytesPerSample = 2;
            if (GSREnabled && BattEnabled)
            {
                numberofBytesPerSample = 4;
            }
            var numberofSamples = payload.Length / numberofBytesPerSample;
            List<ObjectCluster> listOfOJCs = new List<ObjectCluster>();
            for (int i = 0; i < numberofSamples; i++)
            {
                int startingIndex = numberofBytesPerSample * i;
                byte[] sampleData = new byte[numberofBytesPerSample];
                Array.Copy(payload, startingIndex, sampleData, 0, numberofBytesPerSample);
                ObjectCluster ojc = new ObjectCluster(deviceID, deviceID);

                //ojc.RawTimeStamp = (int)tick;

                listOfOJCs.Add(ParseSensorData(sampleData, ojc));
                //if (ShimmerBLEEvent != null)
                //ShimmerBLEEvent.Invoke(null, new ShimmerBLEEventData { ASMID = Asm_uuid.ToString(), CurrentEvent = VerisenseBLEEvent.NewDataPacket, ObjMsg = ojc });
            }
            return listOfOJCs;
        }

        /// <summary>
		/// Parse a single sample and store in the object cluster provided. This is typically used to parse all the samples within a payload. 
		/// </summary>
		/// <param name="ojc"></param>
		/// <param name="sample">one sample</param>
        public override ObjectCluster ParseSensorData(byte[] sample, ObjectCluster ojc)
        {
            int battStartOfIndex = 0;
            int gsrStartOfIndex = 0;
            if (BattEnabled && GSREnabled)
            {
                gsrStartOfIndex = 2;
            }

            if (GSREnabled)
            {
                var gsrraw = BitConverter.ToInt16(sample, gsrStartOfIndex);
                var gsrAdcValueUnCal = (short)(gsrraw & 4095);
                ojc.Add(ObjectClusterSensorName.GSR, ShimmerConfiguration.SignalFormats.RAW, ShimmerConfiguration.SignalUnits.NoUnits, gsrraw);
                var currentGSRRange = GSRRangeSetting.GetConfigurationValue();
                if (currentGSRRange == 4)
                {
                    currentGSRRange = (gsrraw >> 14) & 0x03;
                }
                if (currentGSRRange == 3)
                {
                    if (DeviceHardwareIdentifier.Equals(HardwareIdentifier.VERISENSE_PULSE_PLUS))
                    {
                        if (gsrAdcValueUnCal < GSR_UNCAL_LIMIT_RANGE3_SR68)
                        {
                            gsrAdcValueUnCal = GSR_UNCAL_LIMIT_RANGE3_SR68;
                        }
                    }
                    else if (DeviceHardwareIdentifier.Equals(HardwareIdentifier.VERISENSE_GSR_PLUS))
                    {
                        if (gsrAdcValueUnCal < GSR_UNCAL_LIMIT_RANGE3_SR62)
                        {
                            gsrAdcValueUnCal = GSR_UNCAL_LIMIT_RANGE3_SR62;
                        }
                    } 
                }
                var calVolts = VerisenseDevice.CalibrateADCValueToVolts(gsrAdcValueUnCal,DeviceHardwareIdentifier);
                var gsrResistanceKOhms = CalibrateGsrDataToKOhmsUsingAmplifierEq(calVolts, currentGSRRange);
                //nudge
                gsrResistanceKOhms = NudgeGSRResistance(gsrResistanceKOhms);
                ojc.Add(ObjectClusterSensorName.GSR, ShimmerConfiguration.SignalFormats.CAL, ShimmerConfiguration.SignalUnits.KiloOhms, gsrResistanceKOhms);
                var gsrConductanceUS = ConvertkOhmToUSiemens(gsrResistanceKOhms);
                ojc.Add(ObjectClusterSensorName.GSR, ShimmerConfiguration.SignalFormats.CAL, ShimmerConfiguration.SignalUnits.MicroSiemens, gsrConductanceUS);
                if (gsrConductanceUS> LIMIT_FOR_MINIMUM_VALID_GSR_CONDUCTANCE_US)
                {
                    ConnectivityCheck = GSRConnectivityLevel.Connected;
                } else
                {
                    ConnectivityCheck = GSRConnectivityLevel.Disconnected;
                }
            } else
            {
                ConnectivityCheck = GSRConnectivityLevel.Unknown;
            }

            if (BattEnabled)
            {
                var battraw = BitConverter.ToInt16(sample, battStartOfIndex);
                battraw = (short)(battraw & 4095);
                ojc.Add(ObjectClusterSensorName.Batt, ShimmerConfiguration.SignalFormats.RAW, ShimmerConfiguration.SignalUnits.NoUnits, battraw);
                var voltage = VerisenseDevice.CalibrateADCValueToMilliVolts(battraw,DeviceHardwareIdentifier);
                ojc.Add(ObjectClusterSensorName.Batt, ShimmerConfiguration.SignalFormats.CAL, ShimmerConfiguration.SignalUnits.MilliVolts, voltage);
            }

            return ojc; 
        }

        /// <summary>
        /// Returns true if battery is enabled
        /// </summary>
        public bool IsBattEnabled()
        {
            return BattEnabled;
        }

        /// <summary>
        /// Returns true if GSR is enabled
        /// </summary>
        public bool IsGSREnabled()
        {
            return GSREnabled;
        }
        /// <summary>
        /// Note this automatically disables the battery as adc oversampling is enabled by default. Should you want both ADCs to be enabled, 
        /// please proceed to disabling ADC Oversampling and then enabling the battery adc
        /// </summary>
        /// <param name="enabled"></param>
        public void SetGSREnabled(bool enabled)
        {

            GSREnabled = enabled;
            if (enabled)
            {
                BattEnabled = false;
                SetOversamplingRate(ADCOversamplingRate.ADC_Oversampling_64x);
            }
            else
            {
                SetOversamplingRate(ADCOversamplingRate.ADC_Oversampling_Disabled);
            }
        }
        /// <summary>
        /// Batt cannot be enabled if both gsr and adc oversampling is enabled
        /// </summary>
        /// <param name="enabled"></param>
        public void SetBattEnabled(bool enabled)
        {
            if(GSREnabled && !GSROversamplingRateSetting.Equals(ADCOversamplingRate.ADC_Oversampling_Disabled) && enabled)
            {
                throw new Exception("Unable to enable Battery when GSR and GSROversamplingRate is enabled");
            }
            BattEnabled = enabled;
        }
        /// <summary>
        /// Set GSR range setting
        /// </summary>
        /// <param name="range"><see cref="GSRRange"/></param>
        public void SetGSRRange(SensorSetting range)
        {
            GSRRangeSetting = range;
        }
        /// <summary>
        /// Returns GSR range setting
        /// </summary>
        /// <returns></returns>
        public SensorSetting GetGSRRange()
        {
            return GSRRangeSetting;
        }
        /// <summary>
        /// Set GSR sample rate setting
        /// </summary>
        /// <param name="rate"><see cref="GSRRate"/></param>
        public override void SetSamplingRate(SensorSetting rate)
        {
            GSRRateSetting = rate;
        }
        /// <summary>
        /// Returns GSR sample rate setting
        /// </summary>
        /// <returns></returns>
        public override SensorSetting GetSamplingRate()
        {
            return GSRRateSetting;
        }

        /// <summary>
        /// Over sampling rate can only be used when GSR is enabled and Batt is disabled
        /// </summary>
        /// <param name="rate"><see cref="ADCOversamplingRate"/></param>
        public void SetOversamplingRate(SensorSetting rate)
        {
            if (GSREnabled && BattEnabled && !rate.Equals(ADCOversamplingRate.ADC_Oversampling_Disabled))
            {
                throw new Exception("Unable to set over sampling rate when GSR and Batt is enabled. ");
            }
            GSROversamplingRateSetting = rate;
        }
        /// <summary>
        /// Returns over sampling rate setting
        /// </summary>
        public SensorSetting GetOversamplingRate()
        {
            return GSROversamplingRateSetting;
        }
        /// <summary>
        /// Calibrate GSR Data to KOhms using the amplifier equation. The output may differs with different hardware
        /// </summary>
        /// <param name="range"></param>
        /// <param name="volts"></param>
        /// <returns></returns>
        public double CalibrateGsrDataToKOhmsUsingAmplifierEq(double volts, int range)
        {
            var rFeedback = SHIMMER3_GSR_REF_RESISTORS_KOHMS[range];
            if (DeviceHardwareIdentifier.Equals(HardwareIdentifier.VERISENSE_PULSE_PLUS))
            {
                rFeedback = SR68_GSR_REF_RESISTORS_KOHMS[range];
            }
            var gsr_ref_voltage = 0.5;
            if (DeviceHardwareIdentifier.Equals(VerisenseDevice.HardwareIdentifier.VERISENSE_PULSE_PLUS))
            {
                gsr_ref_voltage = 0.4986;
            }
            var rSource = rFeedback / ((volts / gsr_ref_voltage) - 1.0);
            return rSource;
        }
        /*
        def nudgeGsrResistance(gsrResistanceKOhms, gsrRangeSetting):
    if gsrRangeSetting is not 4:
        minMax = SHIMMER3_GSR_RESISTANCE_MIN_MAX_KOHMS[gsrRangeSetting]
        return UtilFunctions.nudgeDouble(gsrResistanceKOhms, minMax[0], minMax[1])
    return gsrResistanceKOhms
        */
        /// <summary>
        /// Nudge GSR resistance
        /// </summary>
        /// <returns></returns>
        public double NudgeGSRResistance(double gsrResistancekOHMs) 
        {
            if (GSRRangeSetting.GetConfigurationValue() == 4)
            {
                /* If auto-range is enabled, limit the lower range of the resistance due to circuit design */
                return Math.Max(((double[])GSRRangeSetting.GetSettingsValue())[0], gsrResistancekOHMs);
            }
            else
            {
                return UtilCalibration.NudgeDouble(gsrResistancekOHMs, ((double[])GSRRangeSetting.GetSettingsValue())[0], ((double[])GSRRangeSetting.GetSettingsValue())[1]);
            }
        }
        /// <summary>
        /// Converts kOhms To Microsiemens
        /// </summary>
        /// <returns></returns>
        public static double ConvertkOhmToUSiemens(double gsrResistanceKOhms)
        {
            return 1000.0 / gsrResistanceKOhms;
        }       
    }
}
