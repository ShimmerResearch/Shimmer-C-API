using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Drawing;
using System.Linq;
using System.Text;
using System.Windows.Forms;

namespace ShimmerAPI
{
    public partial class Configure : UserControl
    {
        public Control PControlForm;
        public static String[] SamplingRatesStringShimmer3 = new string[] { "1Hz", "10.2Hz", "51.2Hz", "102.4Hz", "204.8Hz", "256Hz", "512Hz", "1024kHz" };
        public static String[] SamplingRatesStringShimmer2 = { "0Hz (Off)", "10.2Hz", "51.2Hz", "102.4Hz", "128Hz", "170.7Hz", "204.8Hz", "256Hz", "512Hz", "1024Hz" };
        private int ReturnEnabledSensors = 0;

        public Configure()
        {
            InitializeComponent();
        }

        public Configure(Control controlForm)
            : this()
        {
            PControlForm = controlForm;
        }

        private void Configure_Load(object sender, EventArgs e)
        {
            if (PControlForm.shimmer.GetShimmerVersion() == (int)Shimmer.ShimmerVersion.SHIMMER3)
            {
                checkBoxSensor1.Text = "Low Noise Accelerometer";
                checkBoxSensor2.Text = "Wide Range Accelerometer";
                checkBoxSensor3.Text = "Gyroscope";
                checkBoxSensor4.Text = "Magnetometer";
                checkBoxSensor5.Text = "Battery Monitor";
                checkBoxSensor6.Text = "Ext A7";
                checkBoxSensor7.Text = "Ext A6";
                checkBoxSensor8.Text = "Ext A15";
                checkBoxSensor9.Text = "Int A1";
                checkBoxSensor10.Text = "Int A12";
                checkBoxSensor11.Text = "Int A13";
                checkBoxSensor12.Text = "Int A14";
                checkBoxSensor13.Text = "Pressure";
                checkBoxSensor14.Text = "GSR";
                checkBoxSensor15.Text = "EXG1";
                checkBoxSensor16.Text = "EXG2";
                checkBoxSensor17.Text = "EXG1 16Bit";
                checkBoxSensor18.Text = "EXG2 16Bit";
                checkBoxSensor19.Text = "Strain Gauge";
                checkBoxSensor12.Visible = true;
                checkBoxSensor13.Visible = true;
                checkBoxSensor14.Visible = true;
                checkBoxSensor15.Visible = true;
                checkBoxSensor16.Visible = true;
                checkBoxSensor17.Visible = true;
                checkBoxSensor18.Visible = true;
                checkBoxSensor19.Visible = true;

                comboBoxSamplingRate.Items.AddRange(SamplingRatesStringShimmer3);
                comboBoxMagRange.Items.AddRange(Shimmer.LIST_OF_MAG_RANGE_SHIMMER3);
                comboBoxAccelRange.Items.AddRange(Shimmer.LIST_OF_ACCEL_RANGE_SHIMMER3);
                comboBoxGSRRange.Items.AddRange(Shimmer.LIST_OF_GSR_RANGE);
                comboBoxGyroRange.Items.AddRange(Shimmer.LIST_OF_GYRO_RANGE_SHIMMER3);
                comboBoxPressureRes.Items.AddRange(Shimmer.LIST_OF_PRESSURE_RESOLUTION_SHIMMER3);

                if (PControlForm.shimmer.GetFirmwareVersion() > 0.1 || PControlForm.shimmer.GetFirmwareInternal() >= 5) // gsr only supported from BTStream 0.1.5 onwards. 
                {
                    checkBoxSensor14.Enabled = true;
                }
                else
                {
                    checkBoxSensor14.Enabled = false;
                }
                if ((PControlForm.shimmer.GetFirmwareVersion() == 0.2 & PControlForm.shimmer.GetFirmwareInternal() >= 8) || (PControlForm.shimmer.GetFirmwareVersion() >= 0.3))// exg only supported from BTStream 0.2.8 onwards. 
                {
                    //ExG
                    checkBoxSensor15.Enabled = true;
                    checkBoxSensor16.Enabled = true;
                    checkBoxSensor17.Enabled = true;
                    checkBoxSensor18.Enabled = true;
                }
                else
                {
                    checkBoxSensor15.Enabled = false;
                    checkBoxSensor16.Enabled = false;
                    checkBoxSensor17.Enabled = false;
                    checkBoxSensor18.Enabled = false;
                }
                if ((PControlForm.shimmer.GetFirmwareVersion() == 0.3 & PControlForm.shimmer.GetFirmwareInternal() >= 1) || (PControlForm.shimmer.GetFirmwareVersion() >= 0.4))// strain gauge only supported from BTStream 0.3.1 onwards. 
                {
                    //Strain Gauge
                    checkBoxSensor19.Enabled = true;
                }
                else
                {
                    checkBoxSensor19.Enabled = false;
                }
            }
            else
            {
                checkBoxSensor1.Text = "Accelerometer";
                checkBoxSensor2.Text = "Gyroscope";
                checkBoxSensor3.Text = "Magnetometer";
                checkBoxSensor4.Text = "Battery Monitor";
                checkBoxSensor5.Text = "ECG";
                checkBoxSensor6.Text = "EMG";
                checkBoxSensor7.Text = "GSR";
                checkBoxSensor8.Text = "Exp Board ADC0";
                checkBoxSensor9.Text = "Exp Board ADC7";
                checkBoxSensor10.Text = "Strain Gauge";
                checkBoxSensor11.Text = "Heart Rate";
                checkBoxSensor12.Visible = false;
                checkBoxSensor13.Visible = false;
                checkBoxSensor14.Visible = false;
                checkBoxSensor15.Visible = false;
                checkBoxSensor16.Visible = false;
                checkBoxSensor17.Visible = false;
                checkBoxSensor18.Visible = false;
                checkBoxSensor19.Visible = false;

                comboBoxMagRange.Items.AddRange(Shimmer.LIST_OF_MAG_RANGE_SHIMMER2);
                comboBoxSamplingRate.Items.AddRange(SamplingRatesStringShimmer2);
                comboBoxAccelRange.Items.AddRange(Shimmer.LIST_OF_ACCEL_RANGE_SHIMMER2);
                comboBoxGSRRange.Items.AddRange(Shimmer.LIST_OF_GSR_RANGE_SHIMMER2);
            }

            checkBox5VReg.Enabled = false;
            checkBoxVoltageMon.Enabled = false;
            checkBox3DOrientation.Enabled = false;
            checkBoxGyroOnTheFly.Enabled = false;
            checkBoxLowPowerMag.Enabled = false;
            checkBoxLowPowerAccel.Enabled = false;
            checkBoxLowPowerGyro.Enabled = false;
            checkBoxIntExpPower.Enabled = false;

            comboBoxMagRange.Enabled = false;
            comboBoxAccelRange.Enabled = false;
            comboBoxGSRRange.Enabled = false;
            comboBoxGyroRange.Enabled = false;
            comboBoxPressureRes.Enabled = false;

            if (PControlForm.GetLoggingFormat() == ",")
            {
                checkBoxComma.Checked = true;
                checkBoxTab.Checked = false;
            }
            else if (PControlForm.GetLoggingFormat() == "\t")
            {
                checkBoxComma.Checked = false;
                checkBoxTab.Checked = true;
            }

            ConfigSetup();
        }

        private void ConfigSetup()
        {
            checkEnabledSensors();
            double samplingRate = PControlForm.shimmer.GetSamplingRate();
            if (PControlForm.shimmer.GetShimmerVersion() == (int)Shimmer.ShimmerVersion.SHIMMER3)
            {
                checkBox5VReg.Enabled = false;
                checkBoxVoltageMon.Enabled = false;
                checkBox3DOrientation.Enabled = true;
                checkBoxGyroOnTheFly.Enabled = true;
                checkBoxLowPowerAccel.Enabled = true;
                checkBoxLowPowerGyro.Enabled = true;
                if (PControlForm.shimmer.GetFirmwareVersion() > 0.1 || PControlForm.shimmer.GetFirmwareInternal() >= 5) // gsr only supported from BTStream 0.1.5 onwards. 
                {
                    checkBoxIntExpPower.Enabled = true;
                }
                else
                {
                    checkBoxIntExpPower.Enabled = false;
                }

                if (samplingRate > 1000)
                {
                    comboBoxSamplingRate.SelectedIndex = 7;
                }
                else if (samplingRate > 500)
                {
                    comboBoxSamplingRate.SelectedIndex = 6;
                }
                else if (samplingRate > 250)
                {
                    comboBoxSamplingRate.SelectedIndex = 5;
                }
                else if (samplingRate > 200)
                {
                    comboBoxSamplingRate.SelectedIndex = 4;
                }
                else if (samplingRate > 100)
                {
                    comboBoxSamplingRate.SelectedIndex = 3;
                }
                else if (samplingRate > 50)
                {
                    comboBoxSamplingRate.SelectedIndex = 2;
                }
                else if (samplingRate > 10)
                {
                    comboBoxSamplingRate.SelectedIndex = 1;
                }
                else
                {
                    comboBoxSamplingRate.SelectedIndex = 0;
                }

                if (PControlForm.shimmer.GetInternalExpPower() == 1)
                {
                    checkBoxIntExpPower.Checked = true;
                }
                else
                {
                    checkBoxIntExpPower.Checked = false;
                }

                comboBoxMagRange.SelectedIndex = PControlForm.shimmer.GetMagRange() - 1;
                comboBoxGyroRange.SelectedIndex = PControlForm.shimmer.GetGyroRange();
                comboBoxPressureRes.SelectedIndex = PControlForm.shimmer.GetPressureResolution();

                checkBoxEnablePPGtoHR.Checked = PControlForm.GetEnablePPGtoHR();
                numericUpDownBeatsToAve.Value = PControlForm.GetNumberOfBeatsToAve();
                if (checkBoxSensor11.Checked && checkBoxIntExpPower.Checked)
                {
                    groupBoxPPGtoHR.Enabled = true;
                }
                else
                {
                    groupBoxPPGtoHR.Enabled = false;
                    checkBoxEnablePPGtoHR.Checked = false;
                }

                checkBoxEnableECGtoHR.Checked = PControlForm.GetEnableECGtoHR();
                if (checkBoxSensor15.Checked || checkBoxSensor17.Checked)
                {
                    groupBoxECGtoHR.Enabled = true;
                }
                else
                {
                    groupBoxECGtoHR.Enabled = false;
                }
            }
            else
            {
                checkBoxVoltageMon.Enabled = true;
                checkBox3DOrientation.Enabled = true;
                checkBoxGyroOnTheFly.Enabled = true;
                checkBoxLowPowerAccel.Enabled = false;
                checkBoxLowPowerGyro.Enabled = false;
                checkBoxIntExpPower.Enabled = false;

                if (samplingRate > 1000)
                {
                    comboBoxSamplingRate.SelectedIndex = 9;
                }
                if (samplingRate > 500)
                {
                    comboBoxSamplingRate.SelectedIndex = 8;
                }
                else if (samplingRate > 250)
                {
                    comboBoxSamplingRate.SelectedIndex = 7;
                }
                else if (samplingRate > 200)
                {
                    comboBoxSamplingRate.SelectedIndex = 6;
                }
                else if (samplingRate > 150)
                {
                    comboBoxSamplingRate.SelectedIndex = 5;
                }
                else if (samplingRate > 120)
                {
                    comboBoxSamplingRate.SelectedIndex = 4;
                }
                else if (samplingRate > 100)
                {
                    comboBoxSamplingRate.SelectedIndex = 3;
                }
                else if (samplingRate > 50)
                {
                    comboBoxSamplingRate.SelectedIndex = 2;
                }
                else if (samplingRate > 10)
                {
                    comboBoxSamplingRate.SelectedIndex = 1;
                }
                else
                {
                    comboBoxSamplingRate.SelectedIndex = 0;
                }

                checkBoxEnableECGtoHR.Checked = PControlForm.GetEnableECGtoHR();
                if (checkBoxSensor5.Checked)
                {
                    groupBoxECGtoHR.Enabled = true;
                }
                else
                {
                    groupBoxECGtoHR.Enabled = false;
                }
            }

            //CheckBoxes
            checkBoxLowPowerMag.Checked = PControlForm.shimmer.LowPowerMagEnabled;
            checkBoxLowPowerAccel.Checked = PControlForm.shimmer.LowPowerAccelEnabled;
            checkBoxLowPowerGyro.Checked = PControlForm.shimmer.LowPowerGyroEnabled;
            checkBox5VReg.Checked = PControlForm.shimmer.GetVReg();
            checkBoxVoltageMon.Checked = PControlForm.shimmer.GetPMux();
            checkBox3DOrientation.Checked = PControlForm.shimmer.Is3DOrientationEnabled();
            checkBoxGyroOnTheFly.Checked = PControlForm.shimmer.IsGyroOnTheFlyCalEnabled();

            //ComboBoxes
            comboBoxAccelRange.SelectedIndex = PControlForm.shimmer.GetAccelRange();
            comboBoxGSRRange.SelectedIndex = PControlForm.shimmer.GetGSRRange();
        }

        private void checkEnabledSensors()
        {
            int enabledSensors = PControlForm.shimmer.GetEnabledSensors();
            if (PControlForm.shimmer.GetShimmerVersion() != (int)Shimmer.ShimmerVersion.SHIMMER3)
            {
                if (((enabledSensors & 0xFF) & (int)Shimmer.SensorBitmapShimmer2.SENSOR_ACCEL) > 0)
                {
                    checkBoxSensor1.Checked = true;
                    comboBoxAccelRange.Enabled = true;
                }
                else
                {
                    checkBoxSensor1.Checked = false;
                    comboBoxAccelRange.Enabled = false;
                }
                if (((enabledSensors & 0xFF) & (int)Shimmer.SensorBitmapShimmer2.SENSOR_GYRO) > 0)
                {
                    checkBoxSensor2.Checked = true;
                }
                else
                {
                    checkBoxSensor2.Checked = false;
                }
                if (((enabledSensors & 0xFF) & (int)Shimmer.SensorBitmapShimmer2.SENSOR_MAG) > 0)
                {
                    checkBoxSensor3.Checked = true;
                    checkBoxLowPowerMag.Enabled = true;

                    if (!PControlForm.shimmer.GetFirmwareVersionFullName().Equals("BoilerPlate 0.1.0"))
                    {
                        checkBoxLowPowerMag.Enabled = true;
                        comboBoxMagRange.Enabled = true;
                        comboBoxMagRange.SelectedIndex = PControlForm.shimmer.GetMagRange();
                    }
                    else
                    {
                        checkBoxLowPowerMag.Enabled = false;
                        comboBoxMagRange.Enabled = false;
                    }
                }
                else
                {
                    checkBoxSensor3.Checked = false;
                    checkBoxLowPowerMag.Enabled = false;
                    comboBoxMagRange.Enabled = false;
                }
                if (((enabledSensors & 0xFF) & (int)Shimmer.SensorBitmapShimmer2.SENSOR_GSR) > 0)
                {
                    checkBoxSensor7.Checked = true;
                    comboBoxGSRRange.Enabled = true;
                }
                else
                {
                    checkBoxSensor7.Checked = false;
                    comboBoxGSRRange.Enabled = false;
                }
                if (((enabledSensors & 0xFF) & (int)Shimmer.SensorBitmapShimmer2.SENSOR_ECG) > 0)
                {
                    checkBoxSensor5.Checked = true;
                }
                else
                {
                    checkBoxSensor5.Checked = false;
                }
                if (((enabledSensors & 0xFF) & (int)Shimmer.SensorBitmapShimmer2.SENSOR_EMG) > 0)
                {
                    checkBoxSensor6.Checked = true;
                }
                else
                {
                    checkBoxSensor6.Checked = false;
                }
                if (((enabledSensors & 0xFF00) & (int)Shimmer.SensorBitmapShimmer2.SENSOR_STRAIN_GAUGE) > 0)
                {
                    checkBoxSensor10.Checked = true;
                    checkBox5VReg.Enabled = false;
                }
                else
                {
                    checkBoxSensor10.Checked = false;
                    checkBox5VReg.Enabled = true;
                }
                if (((enabledSensors & 0xFF00) & (int)Shimmer.SensorBitmapShimmer2.SENSOR_HEART) > 0)
                {
                    checkBoxSensor11.Checked = true;
                }
                else
                {
                    checkBoxSensor11.Checked = false;
                }
                if ((((enabledSensors & 0xFF) & (int)Shimmer.SensorBitmapShimmer2.SENSOR_EXP_BOARD_A0) > 0))  //&& getPMux() == 0
                {
                    checkBoxSensor8.Checked = true;
                }
                else
                {
                    checkBoxSensor8.Checked = false;
                }
                if ((((enabledSensors & 0xFF) & (int)Shimmer.SensorBitmapShimmer2.SENSOR_EXP_BOARD_A7) > 0))  //&& getPMux() == 0)
                {
                    checkBoxSensor9.Checked = true;
                }
                else
                {
                    checkBoxSensor9.Checked = false;
                }
                if (((enabledSensors & 0xFFFF) & (int)Shimmer.SensorBitmapShimmer3.SENSOR_VBATT) > 0)
                {
                    checkBoxSensor4.Checked = true;
                }
                else
                {
                    checkBoxSensor4.Checked = false;
                }

            }
            else
            {
                if ((enabledSensors & (int)Shimmer.SensorBitmapShimmer3.SENSOR_A_ACCEL) > 0)
                {
                    checkBoxSensor1.Checked = true;
                }
                else
                {
                    checkBoxSensor1.Checked = false;
                }
                if ((enabledSensors & (int)Shimmer.SensorBitmapShimmer3.SENSOR_D_ACCEL) > 0)
                {
                    checkBoxSensor2.Checked = true;
                    comboBoxAccelRange.Enabled = true;
                }
                else
                {
                    checkBoxSensor2.Checked = false;
                    comboBoxAccelRange.Enabled = false;
                }
                if (((enabledSensors & 0xFF) & (int)Shimmer.SensorBitmapShimmer3.SENSOR_MPU9150_GYRO) > 0)
                {
                    checkBoxSensor3.Checked = true;
                    comboBoxGyroRange.Enabled = true;
                }
                else
                {
                    checkBoxSensor3.Checked = false;
                    comboBoxGyroRange.Enabled = false;
                }
                if (((enabledSensors & 0xFF) & (int)Shimmer.SensorBitmapShimmer3.SENSOR_LSM303DLHC_MAG) > 0)
                {
                    checkBoxSensor4.Checked = true;
                    checkBoxLowPowerMag.Enabled = true;
                    comboBoxMagRange.Enabled = true;
                }
                else
                {
                    checkBoxSensor4.Checked = false;
                    checkBoxLowPowerMag.Enabled = false;
                    comboBoxMagRange.Enabled = false;
                }
                if (((enabledSensors & 0xFFFF) & (int)Shimmer.SensorBitmapShimmer3.SENSOR_VBATT) > 0)
                {
                    checkBoxSensor5.Checked = true;
                }
                else
                {
                    checkBoxSensor5.Checked = false;
                }
                if (((enabledSensors & 0xFFFFFF) & (int)Shimmer.SensorBitmapShimmer3.SENSOR_EXT_A15) > 0)
                {
                    checkBoxSensor8.Checked = true;
                }
                else
                {
                    checkBoxSensor8.Checked = false;
                }
                if (((enabledSensors & 0xFFFFFF) & (int)Shimmer.SensorBitmapShimmer3.SENSOR_EXT_A7) > 0)
                {
                    checkBoxSensor6.Checked = true;
                }
                else
                {
                    checkBoxSensor6.Checked = false;
                }
                if (((enabledSensors & 0xFFFFFF) & (int)Shimmer.SensorBitmapShimmer3.SENSOR_EXT_A6) > 0)
                {
                    checkBoxSensor7.Checked = true;
                }
                else
                {
                    checkBoxSensor7.Checked = false;
                }
                if (((enabledSensors & 0xFFFFFF) & (int)Shimmer.SensorBitmapShimmer3.SENSOR_INT_A1) > 0)
                {
                    checkBoxSensor9.Checked = true;
                }
                else
                {
                    checkBoxSensor9.Checked = false;
                }
                if (((enabledSensors & 0xFFFFFF) & (int)Shimmer.SensorBitmapShimmer3.SENSOR_INT_A12) > 0)
                {
                    checkBoxSensor10.Checked = true;
                }
                else
                {
                    checkBoxSensor10.Checked = false;
                }
                if (((enabledSensors & 0xFFFFFF) & (int)Shimmer.SensorBitmapShimmer3.SENSOR_INT_A13) > 0)
                {
                    checkBoxSensor11.Checked = true;
                }
                else
                {
                    checkBoxSensor11.Checked = false;
                }
                if (((enabledSensors & 0xFFFFFF) & (int)Shimmer.SensorBitmapShimmer3.SENSOR_INT_A14) > 0)
                {
                    checkBoxSensor12.Checked = true;
                }
                else
                {
                    checkBoxSensor12.Checked = false;
                }
                if ((enabledSensors & (int)Shimmer.SensorBitmapShimmer3.SENSOR_BMP180_PRESSURE) > 0)
                {
                    checkBoxSensor13.Checked = true;
                    comboBoxPressureRes.Enabled = true;
                }
                else
                {
                    checkBoxSensor13.Checked = false;
                    comboBoxPressureRes.Enabled = false;
                }
                if ((enabledSensors & (int)Shimmer.SensorBitmapShimmer3.SENSOR_GSR) > 0)
                {
                    checkBoxSensor14.Checked = true;
                    comboBoxGSRRange.Enabled = true;
                }
                else
                {
                    checkBoxSensor14.Checked = false;
                    comboBoxGSRRange.Enabled = false;
                }
                if ((enabledSensors & (int)Shimmer.SensorBitmapShimmer3.SENSOR_EXG1_24BIT) > 0)
                {
                    checkBoxSensor15.Checked = true;
                }
                else
                {
                    checkBoxSensor15.Checked = false;
                }
                if ((enabledSensors & (int)Shimmer.SensorBitmapShimmer3.SENSOR_EXG2_24BIT) > 0)
                {
                    checkBoxSensor16.Checked = true;
                }
                else
                {
                    checkBoxSensor16.Checked = false;
                }
                if ((enabledSensors & (int)Shimmer.SensorBitmapShimmer3.SENSOR_EXG1_16BIT) > 0)
                {
                    checkBoxSensor17.Checked = true;
                }
                else
                {
                    checkBoxSensor17.Checked = false;
                }
                if ((enabledSensors & (int)Shimmer.SensorBitmapShimmer3.SENSOR_EXG2_16BIT) > 0)
                {
                    checkBoxSensor18.Checked = true;
                }
                else
                {
                    checkBoxSensor18.Checked = false;
                }
                if (((enabledSensors & 0xFF00) & (int)Shimmer.SensorBitmapShimmer3.SENSOR_STRAIN) > 0)
                {
                    checkBoxSensor19.Checked = true;
                }
                else
                {
                    checkBoxSensor19.Checked = false;
                }
            }
        }

        private void checkBoxSensor1_Click(object sender, EventArgs e)
        {
            if (PControlForm.shimmer.GetShimmerVersion() == (int)Shimmer.ShimmerVersion.SHIMMER3)
            {
                if (checkBoxSensor1.Checked)
                {
                    
                }
                else
                {
                    if (!checkBoxSensor2.Checked)
                    {
                        checkBox3DOrientation.Checked = false;
                    }
                }
            }
            else
            {
                if (checkBoxSensor1.Checked)
                {
                    comboBoxAccelRange.Enabled = true;
                }
                else
                {
                    comboBoxAccelRange.Enabled = false;
                    checkBox3DOrientation.Checked = false;
                }
            }
        }

        private void checkBoxSensor2_Click(object sender, EventArgs e)
        {
            if (PControlForm.shimmer.GetShimmerVersion() == (int)Shimmer.ShimmerVersion.SHIMMER3)
            {
                if (checkBoxSensor2.Checked)
                {
                    comboBoxAccelRange.Enabled = true;
                }
                else
                {
                    comboBoxAccelRange.Enabled = false;
                    if (!checkBoxSensor1.Checked)
                    {
                        checkBox3DOrientation.Checked = false;
                    }
                }
            }
            else
            {
                if (checkBoxSensor2.Checked)
                {
                    checkBoxSensor5.Checked = false;
                    checkBoxSensor6.Checked = false;
                    checkBoxSensor7.Checked = false;
                    checkBoxSensor9.Checked = false;
                    groupBoxECGtoHR.Enabled = false;
                    checkBoxEnableECGtoHR.Checked = false;
                }
                else
                {
                    checkBox3DOrientation.Checked = false;
                }
            }
        }

        private void checkBoxSensor3_Click(object sender, EventArgs e)
        {
            if (PControlForm.shimmer.GetShimmerVersion() == (int)Shimmer.ShimmerVersion.SHIMMER3)
            {
                if (checkBoxSensor3.Checked)
                {
                    comboBoxGyroRange.Enabled = true;
                }
                else
                {
                    comboBoxGyroRange.Enabled = false;
                    checkBox3DOrientation.Checked = false;
                }
            }
            else
            {
                if (checkBoxSensor3.Checked)
                {
                    checkBoxSensor5.Checked = false;
                    checkBoxSensor6.Checked = false;
                    checkBoxSensor7.Checked = false;
                    checkBoxSensor9.Checked = false;
                    if (!PControlForm.shimmer.GetFirmwareVersionFullName().Equals("BoilerPlate 0.1.0"))
                    {
                        comboBoxMagRange.Enabled = true;
                        checkBoxLowPowerMag.Enabled = true;
                    }
                    groupBoxECGtoHR.Enabled = false;
                    checkBoxEnableECGtoHR.Checked = false;
                }
                else
                {
                    comboBoxMagRange.Enabled = false;
                    checkBoxLowPowerMag.Enabled = false;
                    checkBox3DOrientation.Checked = false;
                }
            }
        }

        private void checkBoxSensor4_Click(object sender, EventArgs e)
        {
            if (PControlForm.shimmer.GetShimmerVersion() == (int)Shimmer.ShimmerVersion.SHIMMER3)
            {
                if (checkBoxSensor4.Checked)
                {
                    comboBoxMagRange.Enabled = true;
                    checkBoxLowPowerMag.Enabled = true;
                }
                else
                {
                    comboBoxMagRange.Enabled = false;
                    checkBoxLowPowerMag.Enabled = false;
                    checkBox3DOrientation.Checked = false;
                }
            }
            else
            {
                if (checkBoxSensor4.Checked)
                {

                }
                else
                {

                }
            }
        }

        private void checkBoxSensor5_Click(object sender, EventArgs e)
        {
            if (PControlForm.shimmer.GetShimmerVersion() == (int)Shimmer.ShimmerVersion.SHIMMER3)
            {
                if (checkBoxSensor5.Checked)
                {

                }
                else
                {

                }
            }
            else
            {
                if (checkBoxSensor5.Checked)
                {
                    checkBoxSensor2.Checked = false;
                    checkBoxSensor3.Checked = false;
                    checkBoxSensor6.Checked = false;
                    checkBoxSensor7.Checked = false;
                    checkBoxSensor10.Checked = false;
                    groupBoxECGtoHR.Enabled = true;
                }
                else
                {
                    groupBoxECGtoHR.Enabled = false;
                    checkBoxEnableECGtoHR.Checked = false;
                }
            }
        }

        private void checkBoxSensor6_Click(object sender, EventArgs e)
        {
            if (PControlForm.shimmer.GetShimmerVersion() == (int)Shimmer.ShimmerVersion.SHIMMER3)
            {
                if (checkBoxSensor6.Checked)
                {

                }
                else
                {

                }
            }
            else
            {
                if (checkBoxSensor6.Checked)
                {
                    checkBoxSensor2.Checked = false;
                    checkBoxSensor3.Checked = false;
                    checkBoxSensor5.Checked = false;
                    checkBoxSensor7.Checked = false;
                    checkBoxSensor9.Checked = false;
                    groupBoxECGtoHR.Enabled = false;
                    checkBoxEnableECGtoHR.Checked = false;
                }
                else
                {

                }
            }
        }

        private void checkBoxSensor7_Click(object sender, EventArgs e)
        {
            if (PControlForm.shimmer.GetShimmerVersion() == (int)Shimmer.ShimmerVersion.SHIMMER3)
            {
                if (checkBoxSensor7.Checked)
                {
                    
                }
                else
                {
                    
                }
            }
            else
            {
                if (checkBoxSensor7.Checked)
                {
                    comboBoxGSRRange.Enabled = true;
                    checkBoxSensor2.Checked = false;
                    checkBoxSensor3.Checked = false;
                    checkBoxSensor5.Checked = false;
                    checkBoxSensor6.Checked = false;
                    checkBoxSensor9.Checked = false;
                    groupBoxECGtoHR.Enabled = false;
                    checkBoxEnableECGtoHR.Checked = false;
                }
                else
                {
                    comboBoxGSRRange.Enabled = false;
                }
            }
        }

        private void checkBoxSensor9_Click(object sender, EventArgs e)
        {
            if (PControlForm.shimmer.GetShimmerVersion() == (int)Shimmer.ShimmerVersion.SHIMMER3)
            {
                if (checkBoxSensor9.Checked)
                {
                    checkBoxSensor14.Checked = false;
                    checkBoxSensor15.Checked = false;
                    checkBoxSensor16.Checked = false;
                    checkBoxSensor17.Checked = false;
                    checkBoxSensor18.Checked = false;
                    groupBoxECGtoHR.Enabled = false;
                    checkBoxEnableECGtoHR.Checked = false;
                }
                else
                {

                }
            }
            else
            {
                if (checkBoxSensor9.Checked)
                {

                }
                else
                {

                }
            }
        }

        private void checkBoxSensor10_Click(object sender, EventArgs e)
        {
            if (PControlForm.shimmer.GetShimmerVersion() == (int)Shimmer.ShimmerVersion.SHIMMER3)
            {
                if (checkBoxSensor10.Checked)
                {
                    checkBoxSensor15.Checked = false;
                    checkBoxSensor16.Checked = false;
                    checkBoxSensor17.Checked = false;
                    checkBoxSensor18.Checked = false;
                    checkBoxSensor19.Checked = false;
                    groupBoxECGtoHR.Enabled = false;
                    checkBoxEnableECGtoHR.Checked = false;
                }
                else
                {

                }
            }
            else
            {
                if (checkBoxSensor10.Checked)
                {
                    checkBoxSensor2.Checked = false;
                    checkBoxSensor3.Checked = false;
                    checkBoxSensor5.Checked = false;
                    checkBoxSensor6.Checked = false;
                    checkBoxSensor7.Checked = false;
                    checkBox5VReg.Enabled = false;
                    groupBoxECGtoHR.Enabled = false;
                    checkBoxEnableECGtoHR.Checked = false;
                }
                else
                {
                    checkBox5VReg.Enabled = true;
                }
            }
        }

        private void checkBoxSensor11_Click(object sender, EventArgs e)
        {
            if (PControlForm.shimmer.GetShimmerVersion() == (int)Shimmer.ShimmerVersion.SHIMMER3)
            {
                if (checkBoxSensor11.Checked)
                {
                    checkBoxSensor15.Checked = false;
                    checkBoxSensor16.Checked = false;
                    checkBoxSensor17.Checked = false;
                    checkBoxSensor18.Checked = false;
                    checkBoxSensor19.Checked = false;
                    if (checkBoxIntExpPower.Checked)
                    {
                        groupBoxPPGtoHR.Enabled = true;
                    }
                    groupBoxECGtoHR.Enabled = false;
                    checkBoxEnableECGtoHR.Checked = false;
                }
                else
                {
                    groupBoxPPGtoHR.Enabled = false;
                    checkBoxEnablePPGtoHR.Checked = false;
                }
            }
            else
            {
                if (checkBoxSensor11.Checked)
                {
                    
                }
                else
                {
                    
                }
            }
        }

        private void checkBoxSensor12_Click(object sender, EventArgs e)
        {
            if (PControlForm.shimmer.GetShimmerVersion() == (int)Shimmer.ShimmerVersion.SHIMMER3)
            {
                if (checkBoxSensor12.Checked)
                {
                    checkBoxSensor14.Checked = false;
                    checkBoxSensor15.Checked = false;
                    checkBoxSensor16.Checked = false;
                    checkBoxSensor17.Checked = false;
                    checkBoxSensor18.Checked = false;
                    checkBoxSensor19.Checked = false;
                    groupBoxECGtoHR.Enabled = false;
                    checkBoxEnableECGtoHR.Checked = false;
                }
                else
                {

                }
            }
            else
            {
                if (checkBoxSensor12.Checked)
                {
                    
                }
                else
                {
                    
                }
            }
        }

        private void checkBoxSensor13_Click(object sender, EventArgs e)
        {
            if (PControlForm.shimmer.GetShimmerVersion() == (int)Shimmer.ShimmerVersion.SHIMMER3)
            {
                if (checkBoxSensor13.Checked)
                {
                    comboBoxPressureRes.Enabled = true;
                }
                else
                {
                    comboBoxPressureRes.Enabled = false;
                }
            }
            else
            {
                if (checkBoxSensor13.Checked)
                {

                }
                else
                {

                }
            }
        }

        private void checkBoxSensor14_Click(object sender, EventArgs e)
        {
            if (PControlForm.shimmer.GetShimmerVersion() == (int)Shimmer.ShimmerVersion.SHIMMER3)
            {
                if (checkBoxSensor14.Checked)
                {
                    comboBoxGSRRange.Enabled = true;
                    checkBoxSensor12.Checked = false;
                    checkBoxSensor9.Checked = false;
                    checkBoxSensor15.Checked = false;
                    checkBoxSensor16.Checked = false;
                    checkBoxSensor17.Checked = false;
                    checkBoxSensor18.Checked = false;
                    checkBoxSensor19.Checked = false;
                    groupBoxECGtoHR.Enabled = false;
                    checkBoxEnableECGtoHR.Checked = false;
                }
                else
                {
                    comboBoxGSRRange.Enabled = false;
                }
            }
            else
            {
                if (checkBoxSensor14.Checked)
                {

                }
                else
                {

                }
            }
        }

        private void checkBoxSensor15_Click(object sender, EventArgs e)
        {
            if (PControlForm.shimmer.GetShimmerVersion() == (int)Shimmer.ShimmerVersion.SHIMMER3)
            {
                if (checkBoxSensor15.Checked)
                {
                    checkBoxSensor12.Checked = false;
                    checkBoxSensor9.Checked = false;
                    checkBoxSensor10.Checked = false;
                    checkBoxSensor11.Checked = false;
                    checkBoxSensor14.Checked = false;
                    checkBoxSensor17.Checked = false;
                    checkBoxSensor18.Checked = false;
                    checkBoxSensor19.Checked = false;
                    groupBoxPPGtoHR.Enabled = false;
                    checkBoxEnablePPGtoHR.Checked = false;
                    groupBoxECGtoHR.Enabled = true;
                }
                else
                {
                    groupBoxECGtoHR.Enabled = false;
                    checkBoxEnableECGtoHR.Checked = false;
                }
            }
            else
            {
                if (checkBoxSensor15.Checked)
                {
                    
                }
                else
                {
                    
                }
            }
        }

        private void checkBoxSensor16_Click(object sender, EventArgs e)
        {
            if (PControlForm.shimmer.GetShimmerVersion() == (int)Shimmer.ShimmerVersion.SHIMMER3)
            {
                if (checkBoxSensor16.Checked)
                {
                    checkBoxSensor12.Checked = false;
                    checkBoxSensor9.Checked = false;
                    checkBoxSensor10.Checked = false;
                    checkBoxSensor11.Checked = false;
                    checkBoxSensor14.Checked = false;
                    checkBoxSensor17.Checked = false;
                    checkBoxSensor18.Checked = false;
                    checkBoxSensor19.Checked = false;
                    groupBoxPPGtoHR.Enabled = false;
                    checkBoxEnablePPGtoHR.Checked = false;
                    if (!checkBoxSensor15.Checked)
                    {
                        groupBoxECGtoHR.Enabled = false;
                        checkBoxEnableECGtoHR.Checked = false;
                    }
                }
                else
                {
                }
            }
            else
            {
                if (checkBoxSensor16.Checked)
                {
                    
                }
                else
                {
                    
                }
            }
        }

        private void checkBoxSensor17_Click(object sender, EventArgs e)
        {
            if (PControlForm.shimmer.GetShimmerVersion() == (int)Shimmer.ShimmerVersion.SHIMMER3)
            {
                if (checkBoxSensor17.Checked)
                {
                    checkBoxSensor12.Checked = false;
                    checkBoxSensor9.Checked = false;
                    checkBoxSensor10.Checked = false;
                    checkBoxSensor11.Checked = false;
                    checkBoxSensor14.Checked = false;
                    checkBoxSensor15.Checked = false;
                    checkBoxSensor16.Checked = false;
                    checkBoxSensor19.Checked = false;
                    groupBoxPPGtoHR.Enabled = false;
                    checkBoxEnablePPGtoHR.Checked = false;
                    groupBoxECGtoHR.Enabled = true;
                }
                else
                {
                    groupBoxECGtoHR.Enabled = false;
                    checkBoxEnableECGtoHR.Checked = false;
                }
            }
            else
            {
                if (checkBoxSensor17.Checked)
                {

                }
                else
                {
                    
                }
            }
        }

        private void checkBoxSensor18_Click(object sender, EventArgs e)
        {
            if (PControlForm.shimmer.GetShimmerVersion() == (int)Shimmer.ShimmerVersion.SHIMMER3)
            {
                if (checkBoxSensor18.Checked)
                {
                    checkBoxSensor12.Checked = false;
                    checkBoxSensor9.Checked = false;
                    checkBoxSensor10.Checked = false;
                    checkBoxSensor11.Checked = false;
                    checkBoxSensor14.Checked = false;
                    checkBoxSensor15.Checked = false;
                    checkBoxSensor16.Checked = false;
                    checkBoxSensor19.Checked = false;
                    groupBoxPPGtoHR.Enabled = false;
                    checkBoxEnablePPGtoHR.Checked = false;
                    if (!checkBoxSensor17.Checked)
                    {
                        groupBoxECGtoHR.Enabled = false;
                        checkBoxEnableECGtoHR.Checked = false;
                    }
                }
                else
                {

                }
            }
            else
            {
                if (checkBoxSensor18.Checked)
                {
                    
                }
                else
                {
                    
                }
            }
        }

        private void checkBoxSensor19_Click(object sender, EventArgs e)
        {
            if (PControlForm.shimmer.GetShimmerVersion() == (int)Shimmer.ShimmerVersion.SHIMMER3)
            {
                if (checkBoxSensor19.Checked)
                {
                    checkBoxSensor10.Checked = false;
                    checkBoxSensor11.Checked = false;
                    checkBoxSensor12.Checked = false;
                    checkBoxSensor14.Checked = false;
                    checkBoxSensor15.Checked = false;
                    checkBoxSensor16.Checked = false;
                    checkBoxSensor17.Checked = false;
                    checkBoxSensor18.Checked = false;
                    groupBoxPPGtoHR.Enabled = false;
                    checkBoxEnablePPGtoHR.Checked = false;
                    groupBoxECGtoHR.Enabled = false;
                    checkBoxEnableECGtoHR.Checked = false;
                }
                else
                {

                }
            }
            else
            {
                if (checkBoxSensor19.Checked)
                {

                }
                else
                {

                }
            }
        }

        private void checkBox3DOrientation_Click(object sender, EventArgs e)
        {
            if (PControlForm.shimmer.GetShimmerVersion() == (int)Shimmer.ShimmerVersion.SHIMMER3)
            {
                if (checkBox3DOrientation.Checked)
                {
                    checkBoxSensor1.Checked = true;
                    checkBoxSensor2.Checked = true;
                    checkBoxSensor3.Checked = true;
                    checkBoxSensor4.Checked = true;
                }
                else
                {

                }
            }
            else
            {
                if (checkBox3DOrientation.Checked)
                {
                    checkBoxSensor1.Checked = true;
                    checkBoxSensor2.Checked = true;
                    checkBoxSensor3.Checked = true;
                }
                else
                {
                    
                }
            }
        }

        private void checkBoxGyroOnTheFly_Click(object sender, EventArgs e)
        {
            if (PControlForm.shimmer.GetShimmerVersion() == (int)Shimmer.ShimmerVersion.SHIMMER3)
            {
                if (checkBoxGyroOnTheFly.Checked)
                {
                    checkBoxSensor3.Checked = true;
                }
                else
                {

                }
            }
            else
            {
                if (checkBoxGyroOnTheFly.Checked)
                {
                    checkBoxSensor2.Checked = true;
                }
                else
                {

                }
            }
        }

        private void checkBox5VReg_Click(object sender, EventArgs e)
        {
            if (PControlForm.shimmer.GetShimmerVersion() != (int)Shimmer.ShimmerVersion.SHIMMER3)
            {
                if (checkBox5VReg.Checked)
                {
                    checkBoxSensor9.Checked = false;
                }
                else
                {

                }
            }
        }

        private void checkBoxIntExpPower_Click(object sender, EventArgs e)
        {
            if (PControlForm.shimmer.GetShimmerVersion() == (int)Shimmer.ShimmerVersion.SHIMMER3)
            {
                if (checkBoxIntExpPower.Checked)
                {
                    if (checkBoxSensor11.Checked)
                    {
                        groupBoxPPGtoHR.Enabled = true;
                    }
                }
                else
                {
                    groupBoxPPGtoHR.Enabled = false;
                    checkBoxEnablePPGtoHR.Checked = false;
                }
            }
        }

        private void checkBoxComma_CheckedChanged(object sender, EventArgs e)
        {
            if (checkBoxComma.Checked)
            {
                checkBoxTab.Checked = false;
            }
        }

        private void checkBoxTab_CheckedChanged(object sender, EventArgs e)
        {
            if (checkBoxTab.Checked)
            {
                checkBoxComma.Checked = false;
            }
        }

        private void buttonToggleLED_Click(object sender, EventArgs e)
        {
            PControlForm.shimmer.ToggleLED();
        }

        private void buttonCancel_Click(object sender, EventArgs e)
        {
            //this.Close();
        }

        private void buttonOk_Click(object sender, EventArgs e)
        {
            ConfigurationDone();
        }

        private void ConfigurationDone()
        {
            int selectedIndexSamplingRate = comboBoxSamplingRate.SelectedIndex;
            double samplingRate = -1;
            if (PControlForm.shimmer.GetShimmerVersion() == (int)Shimmer.ShimmerVersion.SHIMMER3)
            {
                switch (selectedIndexSamplingRate)
                {
                    case 0:
                        samplingRate = 1;
                        break;
                    case 1:
                        samplingRate = 10.2;
                        break;
                    case 2:
                        samplingRate = 51.2;
                        break;
                    case 3:
                        samplingRate = 102.4;
                        break;
                    case 4:
                        samplingRate = 204.8;
                        break;
                    case 5:
                        samplingRate = 256;
                        break;
                    case 6:
                        samplingRate = 512;
                        break;
                    case 7:
                        samplingRate = 1024;
                        break;
                }
                if (checkBoxIntExpPower.Checked)
                {
                    PControlForm.shimmer.WriteInternalExpPower(1);
                }
                else
                {
                    PControlForm.shimmer.WriteInternalExpPower(0);
                }
                PControlForm.shimmer.WriteMagRange(comboBoxMagRange.SelectedIndex + 1);
                PControlForm.shimmer.WriteGyroRange(comboBoxGyroRange.SelectedIndex);
                PControlForm.shimmer.WritePressureResolution(comboBoxPressureRes.SelectedIndex);

                if (checkBoxSensor11.Checked && checkBoxIntExpPower.Checked)
                {
                    PControlForm.EnablePPGtoHR(checkBoxEnablePPGtoHR.Checked);
                    PControlForm.SetNumberOfBeatsToAve((int)numericUpDownBeatsToAve.Value);
                }
                else
                {
                    PControlForm.EnablePPGtoHR(false);
                }

                if (checkBoxSensor15.Checked || checkBoxSensor17.Checked)
                {
                    PControlForm.EnableECGtoHR(checkBoxEnableECGtoHR.Checked);
                }
                else
                {
                    PControlForm.EnableECGtoHR(false);
                }
            }
            else
            {
                switch (selectedIndexSamplingRate)
                {
                    case 0:
                        samplingRate = 0;
                        break;
                    case 1:
                        samplingRate = 10.2;
                        break;
                    case 2:
                        samplingRate = 51.2;
                        break;
                    case 3:
                        samplingRate = 102.4;
                        break;
                    case 4:
                        samplingRate = 128;
                        break;
                    case 5:
                        samplingRate = 170.7;
                        break;
                    case 6:
                        samplingRate = 204.8;
                        break;
                    case 7:
                        samplingRate = 256;
                        break;
                    case 8:
                        samplingRate = 512;
                        break;
                    case 9:
                        samplingRate = 1024;
                        break;
                }
                if (checkBox5VReg.Checked)
                {
                    PControlForm.shimmer.Write5VReg(1);
                }
                else
                {
                    PControlForm.shimmer.Write5VReg(0);
                }
                if (checkBoxVoltageMon.Checked)
                {
                    PControlForm.shimmer.WritePMux(1);
                }
                else
                {
                    PControlForm.shimmer.WritePMux(0);
                }
                if (!PControlForm.shimmer.GetFirmwareVersionFullName().Equals("BoilerPlate 0.1.0"))
                {
                    PControlForm.shimmer.WriteMagRange(comboBoxMagRange.SelectedIndex);
                }

                if (checkBoxSensor5.Checked)
                {
                    PControlForm.EnableECGtoHR(checkBoxEnableECGtoHR.Checked);
                }
                else
                {
                    PControlForm.EnableECGtoHR(false);
                }
            }
            PControlForm.shimmer.WriteSamplingRate(samplingRate);
            PControlForm.shimmer.SetLowPowerMag(checkBoxLowPowerMag.Checked);
            PControlForm.shimmer.SetLowPowerAccel(checkBoxLowPowerAccel.Checked);
            PControlForm.shimmer.SetLowPowerGyro(checkBoxLowPowerGyro.Checked);
            PControlForm.shimmer.Set3DOrientation(checkBox3DOrientation.Checked);
            PControlForm.shimmer.SetGyroOnTheFlyCalibration(checkBoxGyroOnTheFly.Checked);
            PControlForm.shimmer.WriteAccelRange(comboBoxAccelRange.SelectedIndex);
            PControlForm.shimmer.WriteGSRRange(comboBoxGSRRange.SelectedIndex);

            if (checkBoxComma.Checked)
            {
                PControlForm.SetLoggingFormat(",");
            }
            else
            {
                PControlForm.SetLoggingFormat("\t");
            }

            EnableSensors();    //Called last
        }

        private void EnableSensors()
        {
            if (PControlForm.shimmer.GetShimmerVersion() == (int)Shimmer.ShimmerVersion.SHIMMER3)
            {
                if (checkBoxSensor1.Checked)
                {
                    ReturnEnabledSensors = ReturnEnabledSensors | (int)Shimmer.SensorBitmapShimmer3.SENSOR_A_ACCEL;
                }
                if (checkBoxSensor2.Checked)
                {
                    ReturnEnabledSensors = ReturnEnabledSensors | (int)Shimmer.SensorBitmapShimmer3.SENSOR_D_ACCEL;
                }
                if (checkBoxSensor3.Checked)
                {
                    ReturnEnabledSensors = ReturnEnabledSensors | (int)Shimmer.SensorBitmapShimmer3.SENSOR_MPU9150_GYRO;
                }
                if (checkBoxSensor4.Checked)
                {
                    ReturnEnabledSensors = ReturnEnabledSensors | (int)Shimmer.SensorBitmapShimmer3.SENSOR_LSM303DLHC_MAG;
                }
                if (checkBoxSensor5.Checked)
                {
                    ReturnEnabledSensors = ReturnEnabledSensors | (int)Shimmer.SensorBitmapShimmer3.SENSOR_VBATT;
                }
                if (checkBoxSensor6.Checked)
                {
                    ReturnEnabledSensors = ReturnEnabledSensors | (int)Shimmer.SensorBitmapShimmer3.SENSOR_EXT_A7;
                }
                if (checkBoxSensor7.Checked)
                {
                    ReturnEnabledSensors = ReturnEnabledSensors | (int)Shimmer.SensorBitmapShimmer3.SENSOR_EXT_A6;
                }
                if (checkBoxSensor8.Checked)
                {
                    ReturnEnabledSensors = ReturnEnabledSensors | (int)Shimmer.SensorBitmapShimmer3.SENSOR_EXT_A15;
                }
                if (checkBoxSensor9.Checked)
                {
                    ReturnEnabledSensors = ReturnEnabledSensors | (int)Shimmer.SensorBitmapShimmer3.SENSOR_INT_A1;
                }
                if (checkBoxSensor10.Checked)
                {
                    ReturnEnabledSensors = ReturnEnabledSensors | (int)Shimmer.SensorBitmapShimmer3.SENSOR_INT_A12;
                }
                if (checkBoxSensor11.Checked)
                {
                    ReturnEnabledSensors = ReturnEnabledSensors | (int)Shimmer.SensorBitmapShimmer3.SENSOR_INT_A13;
                }
                if (checkBoxSensor12.Checked)
                {
                    ReturnEnabledSensors = ReturnEnabledSensors | (int)Shimmer.SensorBitmapShimmer3.SENSOR_INT_A14;
                }
                if (checkBoxSensor13.Checked)
                {
                    ReturnEnabledSensors = ReturnEnabledSensors | (int)Shimmer.SensorBitmapShimmer3.SENSOR_BMP180_PRESSURE;
                }
                if (checkBoxSensor14.Checked)
                {
                    ReturnEnabledSensors = ReturnEnabledSensors | (int)Shimmer.SensorBitmapShimmer3.SENSOR_GSR;
                }
                if (checkBoxSensor15.Checked)
                {
                    ReturnEnabledSensors = ReturnEnabledSensors | (int)Shimmer.SensorBitmapShimmer3.SENSOR_EXG1_24BIT;
                }
                if (checkBoxSensor16.Checked)
                {
                    ReturnEnabledSensors = ReturnEnabledSensors | (int)Shimmer.SensorBitmapShimmer3.SENSOR_EXG2_24BIT;
                }
                if (checkBoxSensor17.Checked)
                {
                    ReturnEnabledSensors = ReturnEnabledSensors | (int)Shimmer.SensorBitmapShimmer3.SENSOR_EXG1_16BIT;
                }
                if (checkBoxSensor18.Checked)
                {
                    ReturnEnabledSensors = ReturnEnabledSensors | (int)Shimmer.SensorBitmapShimmer3.SENSOR_EXG2_16BIT;
                }
                if (checkBoxSensor19.Checked)
                {
                    ReturnEnabledSensors = ReturnEnabledSensors | (int)Shimmer.SensorBitmapShimmer3.SENSOR_STRAIN;
                }
            }
            else
            {
                if (checkBoxSensor1.Checked)
                {
                    ReturnEnabledSensors = ReturnEnabledSensors | (int)Shimmer.SensorBitmapShimmer2.SENSOR_ACCEL;
                }
                if (checkBoxSensor2.Checked)
                {
                    ReturnEnabledSensors = ReturnEnabledSensors | (int)Shimmer.SensorBitmapShimmer2.SENSOR_GYRO;
                }
                if (checkBoxSensor3.Checked)
                {
                    ReturnEnabledSensors = ReturnEnabledSensors | (int)Shimmer.SensorBitmapShimmer2.SENSOR_MAG;
                }
                if (checkBoxSensor4.Checked)
                {
                    ReturnEnabledSensors = ReturnEnabledSensors | (int)Shimmer.SensorBitmapShimmer3.SENSOR_VBATT;
                }
                if (checkBoxSensor5.Checked)
                {
                    ReturnEnabledSensors = ReturnEnabledSensors | (int)Shimmer.SensorBitmapShimmer2.SENSOR_ECG;
                }
                if (checkBoxSensor6.Checked)
                {
                    ReturnEnabledSensors = ReturnEnabledSensors | (int)Shimmer.SensorBitmapShimmer2.SENSOR_EMG;
                }
                if (checkBoxSensor7.Checked)
                {
                    ReturnEnabledSensors = ReturnEnabledSensors | (int)Shimmer.SensorBitmapShimmer2.SENSOR_GSR;
                }
                if (checkBoxSensor8.Checked)
                {
                    ReturnEnabledSensors = ReturnEnabledSensors | (int)Shimmer.SensorBitmapShimmer2.SENSOR_EXP_BOARD_A0;
                }
                if (checkBoxSensor9.Checked)
                {
                    ReturnEnabledSensors = ReturnEnabledSensors | (int)Shimmer.SensorBitmapShimmer2.SENSOR_EXP_BOARD_A7;
                }
                if (checkBoxSensor10.Checked)
                {
                    ReturnEnabledSensors = ReturnEnabledSensors | (int)Shimmer.SensorBitmapShimmer2.SENSOR_STRAIN_GAUGE;
                }
                if (checkBoxSensor11.Checked)
                {
                    ReturnEnabledSensors = ReturnEnabledSensors | (int)Shimmer.SensorBitmapShimmer2.SENSOR_HEART;
                }
            }
            PControlForm.shimmer.WriteSensors(ReturnEnabledSensors);
            ReturnEnabledSensors = 0;
        }

        private void numericUpDownBeatsToAve_ValueChanged(object sender, EventArgs e)
        {

        }

        private void groupBoxSettings_Enter(object sender, EventArgs e)
        {

        }
    }
}
