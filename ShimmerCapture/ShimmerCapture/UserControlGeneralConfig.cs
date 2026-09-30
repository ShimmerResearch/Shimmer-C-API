//Shimmer Capture
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Drawing;
using System.Data;
using System.Linq;
using System.Text;
using System.Windows.Forms;
using static ShimmerAPI.ShimmerBluetooth;

namespace ShimmerAPI
{
    public partial class UserControlGeneralConfig : UserControl
    {

        public Configuration PConfiguration;
        public static String[] SamplingRatesStringShimmer3 = new string[] { "1Hz", "10.2Hz", "51.2Hz", "102.4Hz", "204.8Hz", "256Hz", "512Hz", "1024Hz" };
        public static String[] SamplingRatesStringShimmer2 = { "0Hz (Off)", "10.2Hz", "51.2Hz", "102.4Hz", "128Hz", "170.7Hz", "204.8Hz", "256Hz", "512Hz", "1024Hz" };
        private int ReturnEnabledSensors = 0;
        private int previousShimmerState = -1;
        public Boolean BaudRateChangeFlag = false;
        public static String ExpansionBoard = "";
        public Boolean firstConnectFlag = false;
        private static Boolean lostConnectionFlag = true;

        public static bool usingLinux
        {
            get
            {
                int p = (int)Environment.OSVersion.Platform;
                return (p == 4) || (p == 6) || (p == 128);
            }
        }

        public UserControlGeneralConfig()
        {
            InitializeComponent();
        }

        private void UserControlGeneralConfig_Load(object sender, EventArgs e)
        {
            if (usingLinux)
            {
                this.Width = 880;   //+85
                this.Height = 459;  //+20
            }

            try
            {
                PConfiguration = (Configuration)this.Parent.Parent.Parent;
                
                if (PConfiguration.PControlForm.ShimmerDevice.GetShimmerVersion() == (int)ShimmerBluetooth.ShimmerVersion.SHIMMER3
                    || PConfiguration.PControlForm.ShimmerDevice.GetShimmerVersion() == (int)ShimmerBluetooth.ShimmerVersion.SHIMMER3R)
                {
                    PConfiguration.PControlForm.ShimmerDevice.UICallback += this.HandleEvent;

                    checkBoxSensor1.Text = "Low Noise Accelerometer";
                    checkBoxSensor2.Text = "Wide Range Accelerometer";
                    checkBoxSensor3.Text = "Gyroscope";
                    checkBoxSensor4.Text = "Magnetometer";
                    checkBoxSensor5.Text = "Battery Monitor";
                    if (PConfiguration.PControlForm.ShimmerDevice.GetShimmerVersion() == (int)ShimmerBluetooth.ShimmerVersion.SHIMMER3)
                    {
                        checkBoxSensor6.Text = "Ext A7";
                        checkBoxSensor7.Text = "Ext A6";
                        checkBoxSensor8.Text = "Ext A15";
                        checkBoxSensor9.Text = "Int A1";
                        checkBoxSensor10.Text = "Int A12";
                        checkBoxSensor11.Text = "Int A13";
                        checkBoxSensor12.Text = "Int A14";
                    }
                    if (PConfiguration.PControlForm.ShimmerDevice.GetShimmerVersion() == (int)ShimmerBluetooth.ShimmerVersion.SHIMMER3R)
                    {
                        checkBoxSensor6.Text = "Ext A0";
                        checkBoxSensor7.Text = "Ext A1";
                        checkBoxSensor8.Text = "Ext A2";
                        checkBoxSensor9.Text = "Int A3";
                        checkBoxSensor10.Text = "Int A0";
                        checkBoxSensor11.Text = "Int A1";
                        checkBoxSensor12.Text = "Int A2";
                    }

                    checkBoxSensor13.Text = "Pressure && Temperature";
                    checkBoxSensor14.Text = "GSR";
                    checkBoxSensor15.Text = "ECG";                  //Change ExG -> ECG/EMG
                    checkBoxSensor16.Text = "EMG";
                    checkBoxSensor17.Text = "ExG Test Signal";
                    checkBoxSensor23.Text = "Respiration";
                    checkBoxSensorHGAccel.Text = "High G Accel";
                    checkBoxSensorAltMag.Text = "Alt Mag";

                    checkBoxSensor19.Text = "Bridge Amplifier";
                    checkBoxSensor11.Visible = true;
                    checkBoxSensor12.Visible = true;
                    checkBoxSensor13.Visible = true;
                    if (PConfiguration.PControlForm.ShimmerDevice.GetExpansionBoardID() == (int)ExpansionBoardDetectShimmer3.EXPANSION_GSR_PLUS
                        || PConfiguration.PControlForm.ShimmerDevice.GetExpansionBoardID() == (int)ExpansionBoardDetectShimmer3.EXP_BRD_GSR_UNIFIED)
                    {
                        checkBoxSensor14.Visible = true;
                    } else
                    {
                        checkBoxSensor14.Visible = false;
                    }

                    if (PConfiguration.PControlForm.ShimmerDevice.GetExpansionBoardID() == (int)ExpansionBoardDetectShimmer3.EXPANSION_BRIDGE_AMPLIFIER_PLUS
                        || PConfiguration.PControlForm.ShimmerDevice.GetExpansionBoardID() == (int)ExpansionBoardDetectShimmer3.EXP_BRD_BR_AMP_UNIFIED)
                    {
                        checkBoxSensor19.Visible = true;
                    }
                    else
                    {
                        checkBoxSensor19.Visible = false;
                    }

                    if (PConfiguration.PControlForm.ShimmerDevice.GetExpansionBoardID() == (int)ExpansionBoardDetectShimmer3.EXPANSION_EXG
                        || PConfiguration.PControlForm.ShimmerDevice.GetExpansionBoardID() == (int)ExpansionBoardDetectShimmer3.EXP_BRD_EXG_UNIFIED
                        || PConfiguration.PControlForm.ShimmerDevice.GetExpansionBoardID() == (int)ExpansionBoardDetectShimmer3.SHIMMER_3_EXG_EXTENDED)
                    {
                        checkBoxSensor15.Visible = true;
                        checkBoxSensor16.Visible = true;
                        checkBoxSensor17.Visible = true;
                        checkBoxSensor23.Visible = true;
                    } else
                    {
                        checkBoxSensor15.Visible = false;
                        checkBoxSensor16.Visible = false;
                        checkBoxSensor17.Visible = false;
                        checkBoxSensor23.Visible = false;
                    }


                    checkBoxVoltageMon.Checked = false;
                    labelAccelRange.Text = "WR Accelerometer Range";

                    comboBoxBaudRate.Items.AddRange(ShimmerBluetooth.LIST_OF_BAUD_RATE);
                    comboBoxBaudRate.AutoCompleteMode = AutoCompleteMode.SuggestAppend;
                    comboBoxBaudRate.AutoCompleteSource = AutoCompleteSource.ListItems;
                    comboBoxBaudRate.DropDownStyle = ComboBoxStyle.DropDownList;

                    comboBoxSamplingRate.Items.AddRange(SamplingRatesStringShimmer3);
                    comboBoxSamplingRate.AutoCompleteMode = AutoCompleteMode.SuggestAppend;
                    comboBoxSamplingRate.AutoCompleteSource = AutoCompleteSource.ListItems;
                    comboBoxSamplingRate.DropDownStyle = ComboBoxStyle.DropDownList;
                    if (PConfiguration.PControlForm.ShimmerDevice.GetShimmerVersion() == (int)ShimmerBluetooth.ShimmerVersion.SHIMMER3R)
                    {
                        comboBoxMagRange.Items.AddRange(ShimmerBluetooth.LIST_OF_MAG_RANGE_SHIMMER3R);
                    } else
                    {
                        comboBoxMagRange.Items.AddRange(ShimmerBluetooth.LIST_OF_MAG_RANGE_SHIMMER3);
                    }
                    
                    comboBoxMagRange.AutoCompleteMode = AutoCompleteMode.SuggestAppend;
                    comboBoxMagRange.AutoCompleteSource = AutoCompleteSource.ListItems;
                    comboBoxMagRange.DropDownStyle = ComboBoxStyle.DropDownList;

                    comboBoxAccelRange.Items.AddRange(ShimmerBluetooth.LIST_OF_ACCEL_RANGE_SHIMMER3);
                    comboBoxAccelRange.AutoCompleteMode = AutoCompleteMode.SuggestAppend;
                    comboBoxAccelRange.AutoCompleteSource = AutoCompleteSource.ListItems;
                    comboBoxAccelRange.DropDownStyle = ComboBoxStyle.DropDownList;

                    comboBoxLNAccelRange.Items.AddRange(ShimmerBluetooth.LIST_OF_ACCEL_RANGE_SHIMMER3);
                    comboBoxLNAccelRange.AutoCompleteMode = AutoCompleteMode.SuggestAppend;
                    comboBoxLNAccelRange.AutoCompleteSource = AutoCompleteSource.ListItems;
                    comboBoxLNAccelRange.DropDownStyle = ComboBoxStyle.DropDownList;

                    comboBoxAltMagRate.Items.AddRange(ShimmerBluetooth.LIST_OF_ALT_MAG_RATE_SHIMMER3R);
                    comboBoxAltMagRate.AutoCompleteMode = AutoCompleteMode.SuggestAppend;
                    comboBoxAltMagRate.AutoCompleteSource = AutoCompleteSource.ListItems;
                    comboBoxAltMagRate.DropDownStyle = ComboBoxStyle.DropDownList;
                    

                    if (PConfiguration.PControlForm.ShimmerDevice.GetShimmerVersion() == (int)ShimmerBluetooth.ShimmerVersion.SHIMMER3R)
                    {
                        comboBoxLNAccelRange.Enabled = true;
                        comboBoxLNAccelRange.Visible = true;
                        comboBoxAltMagRate.Enabled = true;
                        comboBoxAltMagRate.Visible = true;
                    }
                    else
                    {
                        comboBoxLNAccelRange.Enabled = false;
                        comboBoxLNAccelRange.Visible = false;
                        comboBoxAltMagRate.Enabled = false;
                        comboBoxAltMagRate.Visible = false;
                    }

                    comboBoxGSRRange.Items.AddRange(ShimmerBluetooth.LIST_OF_GSR_RANGE);
                    comboBoxGSRRange.AutoCompleteMode = AutoCompleteMode.SuggestAppend;
                    comboBoxGSRRange.AutoCompleteSource = AutoCompleteSource.ListItems;
                    comboBoxGSRRange.DropDownStyle = ComboBoxStyle.DropDownList;
                    if (PConfiguration.PControlForm.ShimmerDevice.GetShimmerVersion() == (int)ShimmerBluetooth.ShimmerVersion.SHIMMER3R)
                    {
                        comboBoxGyroRange.Items.AddRange(ShimmerBluetooth.LIST_OF_GYRO_RANGE_SHIMMER3R);
                    }
                    else
                    {
                        comboBoxGyroRange.Items.AddRange(ShimmerBluetooth.LIST_OF_GYRO_RANGE_SHIMMER3);
                    }
                    comboBoxGyroRange.AutoCompleteMode = AutoCompleteMode.SuggestAppend;
                    comboBoxGyroRange.AutoCompleteSource = AutoCompleteSource.ListItems;
                    comboBoxGyroRange.DropDownStyle = ComboBoxStyle.DropDownList;
                    if (PConfiguration.PControlForm.ShimmerDevice.GetShimmerVersion() == (int)ShimmerBluetooth.ShimmerVersion.SHIMMER3) {
                        comboBoxPressureRes.Items.AddRange(ShimmerBluetooth.LIST_OF_PRESSURE_RESOLUTION_SHIMMER3);
                    } else if (PConfiguration.PControlForm.ShimmerDevice.GetShimmerVersion() == (int)ShimmerBluetooth.ShimmerVersion.SHIMMER3R)
                    {
                        if (PConfiguration.PControlForm.ShimmerDevice.isBmp581InUse())
                        {
                            comboBoxPressureRes.Items.AddRange(ShimmerBluetooth.LIST_OF_PRESSURE_RESOLUTION_SHIMMER3R_BMP581);
                        }
                        else
                        {
                            comboBoxPressureRes.Items.AddRange(ShimmerBluetooth.LIST_OF_PRESSURE_RESOLUTION_SHIMMER3R);
                        }
                    }

                    comboBoxPressureRes.AutoCompleteMode = AutoCompleteMode.SuggestAppend;
                    comboBoxPressureRes.AutoCompleteSource = AutoCompleteSource.ListItems;
                    comboBoxPressureRes.DropDownStyle = ComboBoxStyle.DropDownList;


                    String[] ListofPPGADCChannels = new String[4];
                    if (PConfiguration.PControlForm.ShimmerDevice.GetShimmerVersion() == (int)ShimmerBluetooth.ShimmerVersion.SHIMMER3)
                    {
                        ListofPPGADCChannels[0] = "Internal ADC A1";
                        ListofPPGADCChannels[1] = "Internal ADC A12";
                        ListofPPGADCChannels[2] = "Internal ADC A13";
                        ListofPPGADCChannels[3] = "Internal ADC A14";
                    } else
                    {
                        ListofPPGADCChannels[0] = Shimmer3RConfiguration.SignalNames.GEN_INT_ADC_A3;
                        ListofPPGADCChannels[1] = Shimmer3RConfiguration.SignalNames.GEN_INT_ADC_A0;
                        ListofPPGADCChannels[2] = Shimmer3RConfiguration.SignalNames.GEN_INT_ADC_A1;
                        ListofPPGADCChannels[3] = Shimmer3RConfiguration.SignalNames.GEN_INT_ADC_A2;
                    }
                    comboBoxPPGAdcChannel.Items.Clear();
                    comboBoxPPGAdcChannel.Items.AddRange(ListofPPGADCChannels);
                    comboBoxPPGAdcChannel.Enabled = true;
                    comboBoxPPGAdcChannel.AutoCompleteMode = AutoCompleteMode.SuggestAppend;
                    comboBoxPPGAdcChannel.AutoCompleteSource = AutoCompleteSource.ListItems;
                    comboBoxPPGAdcChannel.DropDownStyle = ComboBoxStyle.DropDownList;

                    String[] ListofECGChannels = new String[4];
                    ListofECGChannels[0] = "ECG LL-RA";
                    ListofECGChannels[1] = "ECG LA-RA";
                    ListofECGChannels[2] = "EXG2 CH1";
                    ListofECGChannels[3] = "ECG Vx-RL";
                    comboBoxSelectECGChannel.Items.Clear();
                    comboBoxSelectECGChannel.Items.AddRange(ListofECGChannels);
                    comboBoxSelectECGChannel.Enabled = true;
                    comboBoxSelectECGChannel.AutoCompleteMode = AutoCompleteMode.SuggestAppend;
                    comboBoxSelectECGChannel.AutoCompleteSource = AutoCompleteSource.ListItems;
                    comboBoxSelectECGChannel.DropDownStyle = ComboBoxStyle.DropDownList;


                    String[] ListofExGResolution = new String[2];
                    ListofExGResolution[0] = "24 Bit";
                    ListofExGResolution[1] = "16 Bit";
                    comboBoxExGResolution.Items.Clear();
                    comboBoxExGResolution.Items.AddRange(ListofExGResolution);
                    comboBoxExGResolution.Enabled = true;
                    comboBoxExGResolution.AutoCompleteMode = AutoCompleteMode.SuggestAppend;
                    comboBoxExGResolution.AutoCompleteSource = AutoCompleteSource.ListItems;
                    comboBoxExGResolution.DropDownStyle = ComboBoxStyle.DropDownList;

                    comboBoxExgGain.Items.Clear();
                    comboBoxExgGain.Items.AddRange(ShimmerBluetooth.LIST_OF_EXG_GAINS_SHIMMER3);
                    comboBoxExgGain.Items.Add("Custom");
                    comboBoxExgGain.AutoCompleteMode = AutoCompleteMode.SuggestAppend;
                    comboBoxExgGain.AutoCompleteSource = AutoCompleteSource.ListItems;
                    comboBoxExgGain.DropDownStyle = ComboBoxStyle.DropDownList;


                    if (PConfiguration.PControlForm.ShimmerDevice.GetFirmwareIdentifier()== ShimmerBluetooth.FW_IDENTIFIER_LOGANDSTREAM
                        //|| PConfiguration.PControlForm.ShimmerDevice.GetFirmwareVersion() > 0.1 || PConfiguration.PControlForm.ShimmerDevice.GetFirmwareInternal() >= 5) // gsr only supported from BTStream 0.1.5 onwards. 
                        || PConfiguration.PControlForm.ShimmerDevice.compareVersions(ShimmerBluetooth.FW_IDENTIFIER_BTSTREAM,0,1,5))
                    {
                        checkBoxSensor14.Enabled = true;
                    }
                    else
                    {
                        checkBoxSensor14.Enabled = false;
                    }
                    // exg only supported from BTStream 0.2.8 onwards. 
                    if (PConfiguration.PControlForm.ShimmerDevice.VersionLaterThan(ShimmerBluetooth.FW_IDENTIFIER_BTSTREAM, 0, 2, 8) ||
                        PConfiguration.PControlForm.ShimmerDevice.VersionLaterThan(ShimmerBluetooth.FW_IDENTIFIER_LOGANDSTREAM, 0, 1, 0) ||
                        PConfiguration.PControlForm.ShimmerDevice.VersionLaterThan(ShimmerBluetooth.FW_IDENTIFIER_SHIMMERECGMD, 0, 0, 1))
                    {

                        //ExG
                        checkBoxSensor15.Enabled = true;
                        checkBoxSensor16.Enabled = true;
                        checkBoxSensor17.Enabled = true;
                        checkBoxSensor23.Enabled = true;
                        
                    }
                    else
                    {
                        checkBoxSensor15.Enabled = false;
                        checkBoxSensor16.Enabled = false;
                        checkBoxSensor17.Enabled = false;
                        checkBoxSensor23.Enabled = false;
                    }
                    // strain gauge only supported from BTStream 0.3.1 onwards. 
                    if (PConfiguration.PControlForm.ShimmerDevice.VersionLaterThan(1, 0, 2, 8) ||
                        PConfiguration.PControlForm.ShimmerDevice.VersionLaterThan(3, 0, 1, 4))
                    {
                        //Bridge Amplifier
                        checkBoxSensor19.Enabled = true;
                    }
                    else
                    {
                        checkBoxSensor19.Enabled = false;
                    }

                    if (PConfiguration.PControlForm.ShimmerDevice.isShimmer3RwithHighGAccelSupport())
                    {
                        
                        checkBoxSensorHGAccel.Enabled = true;
                        checkBoxSensorHGAccel.Visible = true;
                        
                    } else
                    {
                        checkBoxSensorHGAccel.Enabled = false;
                        checkBoxSensorHGAccel.Visible = false;
                        
                    }
                    if (PConfiguration.PControlForm.ShimmerDevice.isShimmer3RwithAltMagSupport())
                    {                        
                        checkBoxSensorAltMag.Enabled = true;
                        checkBoxSensorAltMag.Visible = true;
                    }
                    else
                    {                        
                        checkBoxSensorAltMag.Enabled = false;
                        checkBoxSensorAltMag.Visible = false;
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
                    checkBoxSensor11.Visible = false;
                    checkBoxSensor12.Visible = false;
                    checkBoxSensor13.Visible = false;
                    checkBoxSensor14.Visible = false;
                    checkBoxSensor15.Visible = false;
                    checkBoxSensor16.Visible = false;
                    checkBoxSensor17.Visible = false;
                    checkBoxSensor23.Visible = false;

                    checkBoxSensor19.Visible = false;

                    checkBoxSensor18.Visible = false;
                    checkBoxSensor18.Enabled = false;
                    checkBoxSensor20.Visible = false;
                    checkBoxSensor20.Enabled = false;
                    checkBoxSensor21.Visible = false;
                    checkBoxSensor21.Enabled = false;
                    checkBoxSensor22.Visible = false;
                    checkBoxSensor22.Enabled = false;

                    checkBoxSensor4.Enabled = false;
                    labelAccelRange.Text = "Accelerometer Range";
                    comboBoxBaudRate.Items.AddRange(ShimmerBluetooth.LIST_OF_BAUD_RATE);
                    comboBoxBaudRate.AutoCompleteMode = AutoCompleteMode.SuggestAppend;
                    comboBoxBaudRate.AutoCompleteSource = AutoCompleteSource.ListItems;
                    comboBoxBaudRate.DropDownStyle = ComboBoxStyle.DropDownList;

                    comboBoxMagRange.Items.AddRange(ShimmerBluetooth.LIST_OF_MAG_RANGE_SHIMMER2);
                    comboBoxMagRange.AutoCompleteMode = AutoCompleteMode.SuggestAppend;
                    comboBoxMagRange.AutoCompleteSource = AutoCompleteSource.ListItems;
                    comboBoxMagRange.DropDownStyle = ComboBoxStyle.DropDownList;

                    comboBoxSamplingRate.Items.AddRange(SamplingRatesStringShimmer2);
                    comboBoxSamplingRate.AutoCompleteMode = AutoCompleteMode.SuggestAppend;
                    comboBoxSamplingRate.AutoCompleteSource = AutoCompleteSource.ListItems;
                    comboBoxSamplingRate.DropDownStyle = ComboBoxStyle.DropDownList;

                    comboBoxAccelRange.Items.AddRange(ShimmerBluetooth.LIST_OF_ACCEL_RANGE_SHIMMER2);
                    comboBoxAccelRange.AutoCompleteMode = AutoCompleteMode.SuggestAppend;
                    comboBoxAccelRange.AutoCompleteSource = AutoCompleteSource.ListItems;
                    comboBoxAccelRange.DropDownStyle = ComboBoxStyle.DropDownList;
                    
                    comboBoxGSRRange.Items.AddRange(ShimmerBluetooth.LIST_OF_GSR_RANGE_SHIMMER2);
                    comboBoxGSRRange.AutoCompleteMode = AutoCompleteMode.SuggestAppend;
                    comboBoxGSRRange.AutoCompleteSource = AutoCompleteSource.ListItems;
                    comboBoxGSRRange.DropDownStyle = ComboBoxStyle.DropDownList;
                    
                    String[] ListofECGChannels = new String[2];
                    ListofECGChannels[0] = "ECG RA LL";
                    ListofECGChannels[1] = "ECG LA LL";
                    comboBoxSelectECGChannel.Items.Clear();
                    comboBoxSelectECGChannel.Items.AddRange(ListofECGChannels);
                    comboBoxSelectECGChannel.Enabled = true;
                    comboBoxPPGAdcChannel.Enabled = false;
                    comboBoxExGResolution.Enabled = false;
                    comboBoxExgGain.Enabled = false;
                    comboBoxBaudRate.Enabled = false;
                    buttonDetectExpansionBoard.Enabled = false;
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

                if (PConfiguration.PControlForm.GetLoggingFormat() == ",")
                {
                    checkBoxComma.Checked = true;
                    checkBoxTab.Checked = false;
                }
                else if (PConfiguration.PControlForm.GetLoggingFormat() == "\t")
                {
                    checkBoxComma.Checked = false;
                    checkBoxTab.Checked = true;
                }

                ConfigSetup();
            }

            catch (InvalidCastException)
            {

            }
            catch (NullReferenceException)
            {

            }
        }
        
        

        private void ConfigSetup()
        {

            double samplingRate = PConfiguration.PControlForm.ShimmerDevice.GetSamplingRate();
            if (PConfiguration.PControlForm.ShimmerDevice.GetShimmerVersion() == (int)ShimmerBluetooth.ShimmerVersion.SHIMMER3 || PConfiguration.PControlForm.ShimmerDevice.GetShimmerVersion() == (int)ShimmerBluetooth.ShimmerVersion.SHIMMER3R)
            {
                checkBox5VReg.Enabled = false;
                checkBoxVoltageMon.Enabled = false;
                checkBox3DOrientation.Enabled = true;
                checkBoxGyroOnTheFly.Enabled = true;
                checkBoxLowPowerAccel.Enabled = true;
                checkBoxLowPowerGyro.Enabled = true;
                if (PConfiguration.PControlForm.ShimmerDevice.GetShimmerVersion() == (int)ShimmerBluetooth.ShimmerVersion.SHIMMER3)
                {
                    checkBoxLowPowerGyro.Text = "Enable Gyro LP Mode";
                } else if (PConfiguration.PControlForm.ShimmerDevice.GetShimmerVersion() == (int)ShimmerBluetooth.ShimmerVersion.SHIMMER3R)
                {
                    checkBoxLowPowerGyro.Text = "Enable LN Accel and Gyro LP Mode";
                }

                

                //if (PConfiguration.PControlForm.ShimmerDevice.GetFirmwareVersion() > 0.1 || PConfiguration.PControlForm.ShimmerDevice.GetFirmwareInternal() >= 5) // gsr only supported from BTStream 0.1.5 onwards. 
                if (PConfiguration.PControlForm.ShimmerDevice.GetFirmwareIdentifier() == ShimmerBluetooth.FW_IDENTIFIER_LOGANDSTREAM
                        //|| PConfiguration.PControlForm.ShimmerDevice.GetFirmwareVersion() > 0.1 || PConfiguration.PControlForm.ShimmerDevice.GetFirmwareInternal() >= 5) // gsr only supported from BTStream 0.1.5 onwards. 
                        || PConfiguration.PControlForm.ShimmerDevice.compareVersions(ShimmerBluetooth.FW_IDENTIFIER_BTSTREAM, 0, 1, 5))
                {
                    checkBoxIntExpPower.Enabled = true;
                }
                else
                {
                    checkBoxIntExpPower.Enabled = false;
                }

                if (PConfiguration.PControlForm.ShimmerDevice.GetCompatibilityCode() >= 4) // baud rate change only supported from BtStream 0.4.0 onwards and LogAndStream v0.3.0 onwards. 
                {
                    comboBoxBaudRate.Enabled = true;
                    buttonDetectExpansionBoard.Enabled = true;
                    textBoxExpansionBoard.Text = ExpansionBoard;
                }
                else
                {
                    comboBoxBaudRate.Enabled = false;
                    buttonDetectExpansionBoard.Enabled = false;
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

                byte[] reg1 = PConfiguration.PControlForm.ShimmerDevice.GetEXG1RegisterContents();
                byte[] reg2 = PConfiguration.PControlForm.ShimmerDevice.GetEXG2RegisterContents();

                int gainexg1ch1 = ConvertEXGGainSettingToValue((reg1[3] >> 4) & 7);
                int gainexg1ch2 = ConvertEXGGainSettingToValue((reg1[4] >> 4) & 7);
                int gainexg2ch1 = ConvertEXGGainSettingToValue((reg2[3] >> 4) & 7);
                int gainexg2ch2 = ConvertEXGGainSettingToValue((reg2[4] >> 4) & 7);

                if (PConfiguration.PControlForm.ShimmerDevice.GetShimmerVersion() == (int)ShimmerBluetooth.ShimmerVersion.SHIMMER3
                    || PConfiguration.PControlForm.ShimmerDevice.GetShimmerVersion() == (int)ShimmerBluetooth.ShimmerVersion.SHIMMER3R)
                {
                    this.comboBoxExgGain.SelectedIndexChanged -= new System.EventHandler(this.comboBoxExgGain_SelectedIndexChanged);

                    //if the gain is the same
                    if (gainexg1ch1 == gainexg1ch2 && gainexg2ch1 == gainexg2ch2 && gainexg1ch1 == gainexg2ch1)
                    {
                        if (gainexg1ch1 == 1)
                        {
                            comboBoxExgGain.SelectedIndex = 0;
                        }
                        else if (gainexg1ch1 == 2)
                        {
                            comboBoxExgGain.SelectedIndex = 1;
                        }
                        else if (gainexg1ch1 == 3)
                        {
                            comboBoxExgGain.SelectedIndex = 2;
                        }
                        else if (gainexg1ch1 == 4)
                        {
                            comboBoxExgGain.SelectedIndex = 3;
                        }
                        else if (gainexg1ch1 == 6)
                        {
                            comboBoxExgGain.SelectedIndex = 4;
                        }
                        else if (gainexg1ch1 == 8)
                        {
                            comboBoxExgGain.SelectedIndex = 5;
                        }
                        else if (gainexg1ch1 == 12)
                        {
                            comboBoxExgGain.SelectedIndex = 6;
                        }
                        if (comboBoxExgGain.Items.Count == 8)
                        {
                            comboBoxExgGain.Items.RemoveAt(7);
                        }
                    }
                    else
                    {
                        if (comboBoxExgGain.Items.Count == 7)
                        {
                            comboBoxExgGain.Items.Add("Custom");
                        }
                        comboBoxExgGain.SelectedIndex = 7;
                    }
                }
                this.comboBoxExgGain.SelectedIndexChanged += new System.EventHandler(this.comboBoxExgGain_SelectedIndexChanged);

                if (PConfiguration.PControlForm.ShimmerDevice.GetShimmerVersion() == (int)ShimmerBluetooth.ShimmerVersion.SHIMMER3R) {
                    comboBoxMagRange.SelectedIndex = PConfiguration.PControlForm.ShimmerDevice.GetMagRange();
                } else
                {
                    comboBoxMagRange.SelectedIndex = PConfiguration.PControlForm.ShimmerDevice.GetMagRange() - 1;
                }
                comboBoxGyroRange.SelectedIndex = PConfiguration.PControlForm.ShimmerDevice.GetGyroRange();

                comboBoxPressureRes.SelectedIndex = PConfiguration.PControlForm.ShimmerDevice.GetPressureResolution();
                comboBoxBaudRate.SelectedIndex = PConfiguration.PControlForm.ShimmerDevice.GetBaudRate();
                comboBoxExGResolution.SelectedIndex = 0;

                numericUpDownBeatsToAve.Value = PConfiguration.PControlForm.GetNumberOfBeatsToAve();
                numericUpDownBeatsToAveECG.Value = PConfiguration.PControlForm.GetNumberOfBeatsToAveECG();
                
            }
            else
            {
                checkBoxVoltageMon.Enabled = true;
                checkBox3DOrientation.Enabled = true;
                checkBoxGyroOnTheFly.Enabled = true;
                checkBoxLowPowerAccel.Enabled = false;
                checkBoxLowPowerGyro.Enabled = false;
                checkBoxIntExpPower.Enabled = false;
                comboBoxBaudRate.Enabled = false;
                buttonDetectExpansionBoard.Enabled = false;

                textBoxExpansionBoard.Text = "";

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

                checkBoxEnableECGtoHR.Checked = PConfiguration.PControlForm.GetEnableECGtoHR();
                comboBoxMagRange.SelectedIndex = PConfiguration.PControlForm.ShimmerDevice.GetMagRange();
            }

            //CheckBoxes
            checkBoxLowPowerMag.Checked = PConfiguration.PControlForm.ShimmerDevice.LowPowerMagEnabled;
            checkBoxLowPowerAccel.Checked = PConfiguration.PControlForm.ShimmerDevice.LowPowerAccelEnabled;
            checkBoxLowPowerGyro.Checked = PConfiguration.PControlForm.ShimmerDevice.LowPowerGyroEnabled;
            checkBox5VReg.Checked = PConfiguration.PControlForm.ShimmerDevice.GetVReg();
            if (PConfiguration.PControlForm.ShimmerDevice.GetShimmerVersion() != (int)ShimmerBluetooth.ShimmerVersion.SHIMMER3
                && PConfiguration.PControlForm.ShimmerDevice.GetShimmerVersion() != (int)ShimmerBluetooth.ShimmerVersion.SHIMMER3R)
            {
                checkBoxVoltageMon.Checked = PConfiguration.PControlForm.ShimmerDevice.GetPMux();
            }
            checkBox3DOrientation.Checked = PConfiguration.PControlForm.ShimmerDevice.Is3DOrientationEnabled();
            checkBoxGyroOnTheFly.Checked = PConfiguration.PControlForm.ShimmerDevice.IsGyroOnTheFlyCalEnabled();

            //ComboBoxes
            int accelRange = PConfiguration.PControlForm.ShimmerDevice.GetAccelRange();
            int lnAccelRange = PConfiguration.PControlForm.ShimmerDevice.GetLNAccelRange();
            int altMagRate = PConfiguration.PControlForm.ShimmerDevice.GeAltMagRate();
            if (!PConfiguration.PControlForm.ShimmerDevice.isShimmer3withUpdatedSensors())
            {
                comboBoxAccelRange.SelectedIndex = accelRange;
            }
            else //updated Shimmer3 (0,2,3,1) 
            {
                if (PConfiguration.PControlForm.ShimmerDevice.GetShimmerVersion() == (int)ShimmerBluetooth.ShimmerVersion.SHIMMER3)
                {
                    if (accelRange == 0)
                    {
                        comboBoxAccelRange.SelectedIndex = 0;
                    }
                    else if (accelRange == 1)
                    {
                        comboBoxAccelRange.SelectedIndex = 3;
                    }
                    else if (accelRange == 2)
                    {
                        comboBoxAccelRange.SelectedIndex = 1;
                    }
                    else if (accelRange == 3)
                    {
                        comboBoxAccelRange.SelectedIndex = 2;
                    }
                } else if (PConfiguration.PControlForm.ShimmerDevice.GetShimmerVersion() == (int)ShimmerBluetooth.ShimmerVersion.SHIMMER3R)
                {
                    if (accelRange == 0)
                    {
                        comboBoxAccelRange.SelectedIndex = 0;
                    }
                    else if (accelRange == 1)
                    {
                        comboBoxAccelRange.SelectedIndex = 1;
                    }
                    else if (accelRange == 2)
                    {
                        comboBoxAccelRange.SelectedIndex = 2;
                    }
                    else if (accelRange == 3)
                    {
                        comboBoxAccelRange.SelectedIndex = 3;
                    }

                    if (lnAccelRange == 0)
                    {
                        comboBoxLNAccelRange.SelectedIndex = 0;
                    }
                    else if (lnAccelRange == 1)
                    {
                        comboBoxLNAccelRange.SelectedIndex = 1;
                    }
                    else if (lnAccelRange == 2)
                    {
                        comboBoxLNAccelRange.SelectedIndex = 2;
                    }
                    else if (lnAccelRange == 3)
                    {
                        comboBoxLNAccelRange.SelectedIndex = 3;
                    }

                    if (altMagRate == 0x00)
                    {
                        comboBoxAltMagRate.SelectedIndex = 0;
                    }
                    else if (altMagRate == 0x38)
                    {
                        comboBoxAltMagRate.SelectedIndex = 1;
                    }
                    else if (altMagRate == 0x3A)
                    {
                        comboBoxAltMagRate.SelectedIndex = 2;
                    }
                    else if (altMagRate == 0x3E)
                    {
                        comboBoxAltMagRate.SelectedIndex = 3;
                    }
                    else if (altMagRate == 0x31)
                    {
                        comboBoxAltMagRate.SelectedIndex = 4;
                    }
                    else if (altMagRate == 0x21)
                    {
                        comboBoxAltMagRate.SelectedIndex = 5;
                    }
                    else if (altMagRate == 0x11)
                    {
                        comboBoxAltMagRate.SelectedIndex = 6;
                    }
                    else if (altMagRate == 0x01)
                    {
                        comboBoxAltMagRate.SelectedIndex = 7;
                    }
                }
            }


            comboBoxGSRRange.SelectedIndex = PConfiguration.PControlForm.ShimmerDevice.GetGSRRange();

            checkEnabledSensors();

            if (PConfiguration.PControlForm.ShimmerDevice.GetShimmerVersion() == (int)ShimmerBluetooth.ShimmerVersion.SHIMMER2R)
            {
                if (checkBoxSensor5.Checked)
                {
                    groupBoxECGtoHR.Enabled = true;
                }
                else
                {
                    groupBoxECGtoHR.Enabled = false;
                }
            }

            checkBoxEnablePPGtoHR.Checked = PConfiguration.PControlForm.GetEnablePPGtoHR(); // must be called after enabled sensors

            if (PConfiguration.PControlForm.ShimmerDevice.GetInternalExpPower() == 1)
            {
                checkBoxIntExpPower.Checked = true;
            }
            else
            {
                checkBoxIntExpPower.Checked = false;
            }

            if (checkBoxIntExpPower.Checked)
            {
                groupBoxPPGtoHR.Enabled = true;
            }
            else
            {
                groupBoxPPGtoHR.Enabled = false;
                checkBoxEnablePPGtoHR.Checked = false;
            }

            checkBoxEnableECGtoHR.Checked = PConfiguration.PControlForm.GetEnableECGtoHR();
            if (PConfiguration.PControlForm.ShimmerDevice.GetShimmerVersion() == (int)ShimmerBluetooth.ShimmerVersion.SHIMMER3
                || PConfiguration.PControlForm.ShimmerDevice.GetShimmerVersion() == (int)ShimmerBluetooth.ShimmerVersion.SHIMMER3R)
            {
                if (checkBoxSensor15.Checked)
                {
                    groupBoxECGtoHR.Enabled = true;
                }
                else
                {
                    groupBoxECGtoHR.Enabled = false;
                }
            }
            if (PConfiguration.PControlForm.ShimmerDevice.GetShimmerVersion() == (int)ShimmerBluetooth.ShimmerVersion.SHIMMER3R)
            {
                labelMagRange.Text = "Alt Mag Range";
            }

        }

        private void checkEnabledSensors()
        {
            long enabledSensors = PConfiguration.PControlForm.ShimmerDevice.GetEnabledSensors();
            int shimmerVersion = PConfiguration.PControlForm.ShimmerDevice.GetShimmerVersion();
            if (shimmerVersion != (int)ShimmerBluetooth.ShimmerVersion.SHIMMER3
                && shimmerVersion != (int)ShimmerBluetooth.ShimmerVersion.SHIMMER3R)
            {
                if (((enabledSensors & 0xFF) & (int)ShimmerBluetooth.SensorBitmapShimmer2.SENSOR_ACCEL) > 0)
                {
                    checkBoxSensor1.Checked = true;
                    comboBoxAccelRange.Enabled = true;
                }
                else
                {
                    checkBoxSensor1.Checked = false;
                    comboBoxAccelRange.Enabled = false;
                }
                if (((enabledSensors & 0xFF) & (int)ShimmerBluetooth.SensorBitmapShimmer2.SENSOR_GYRO) > 0)
                {
                    checkBoxSensor2.Checked = true;
                }
                else
                {
                    checkBoxSensor2.Checked = false;
                }
                if (((enabledSensors & 0xFF) & (int)ShimmerBluetooth.SensorBitmapShimmer2.SENSOR_MAG) > 0)
                {
                    checkBoxSensor3.Checked = true;
                    checkBoxLowPowerMag.Enabled = true;

                    if (!PConfiguration.PControlForm.ShimmerDevice.GetFirmwareVersionFullName().Equals("BoilerPlate 0.1.0"))
                    {
                        checkBoxLowPowerMag.Enabled = true;
                        comboBoxMagRange.Enabled = true;
                        comboBoxMagRange.SelectedIndex = PConfiguration.PControlForm.ShimmerDevice.GetMagRange();
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
                if (((enabledSensors & 0xFF) & (int)ShimmerBluetooth.SensorBitmapShimmer2.SENSOR_GSR) > 0)
                {
                    checkBoxSensor7.Checked = true;
                    comboBoxGSRRange.Enabled = true;
                }
                else
                {
                    checkBoxSensor7.Checked = false;
                    comboBoxGSRRange.Enabled = false;
                }
                if (((enabledSensors & 0xFF) & (int)ShimmerBluetooth.SensorBitmapShimmer2.SENSOR_ECG) > 0)
                {
                    checkBoxSensor5.Checked = true;

                    if (PConfiguration.PControlForm.ECGSignalName.Equals("ECG RA LL"))
                    {
                        comboBoxSelectECGChannel.SelectedIndex = 0;
                    }
                    else if (PConfiguration.PControlForm.ECGSignalName.Equals("ECG LA LL"))
                    {
                        comboBoxSelectECGChannel.SelectedIndex = 1;
                    }
                    else
                    {
                        comboBoxSelectECGChannel.SelectedIndex = 0;
                    }
                }
                else
                {
                    checkBoxSensor5.Checked = false;
                }
                if (((enabledSensors & 0xFF) & (int)ShimmerBluetooth.SensorBitmapShimmer2.SENSOR_EMG) > 0)
                {
                    checkBoxSensor6.Checked = true;
                }
                else
                {
                    checkBoxSensor6.Checked = false;
                }
                if (((enabledSensors & 0xFF00) & (int)ShimmerBluetooth.SensorBitmapShimmer2.SENSOR_STRAIN_GAUGE) > 0)
                {
                    checkBoxSensor10.Checked = true;
                    checkBox5VReg.Enabled = false;
                }
                else
                {
                    checkBoxSensor10.Checked = false;
                    checkBox5VReg.Enabled = true;
                }
                if (((enabledSensors & 0xFF00) & (int)ShimmerBluetooth.SensorBitmapShimmer2.SENSOR_HEART) > 0)
                {
                    checkBoxSensor11.Checked = true;
                }
                else
                {
                    checkBoxSensor11.Checked = false;
                }
                if ((((enabledSensors & 0xFF) & (int)ShimmerBluetooth.SensorBitmapShimmer2.SENSOR_EXP_BOARD_A0) > 0))  //&& getPMux() == 0
                {
                    checkBoxSensor8.Checked = true;
                }
                else
                {
                    checkBoxSensor8.Checked = false;
                }
                if ((((enabledSensors & 0xFF) & (int)ShimmerBluetooth.SensorBitmapShimmer2.SENSOR_EXP_BOARD_A7) > 0))  //&& getPMux() == 0)
                {
                    checkBoxSensor9.Checked = true;
                }
                else
                {
                    checkBoxSensor9.Checked = false;
                }
                if (((enabledSensors & 0xFFFF) & (int)ShimmerBluetooth.SensorBitmapShimmer3.SENSOR_VBATT) > 0)
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
                if ((enabledSensors & (int)ShimmerBluetooth.SensorBitmapShimmer3.SENSOR_A_ACCEL) > 0)
                {
                    checkBoxSensor1.Checked = true;
                    if (shimmerVersion == (int)ShimmerBluetooth.ShimmerVersion.SHIMMER3R) { 
                        comboBoxLNAccelRange.Enabled = true;
                    }
                }
                else
                {
                    checkBoxSensor1.Checked = false;
                    if (shimmerVersion == (int)ShimmerBluetooth.ShimmerVersion.SHIMMER3R)
                    {
                        comboBoxLNAccelRange.Enabled = false;
                    }
                }
                if ((enabledSensors & (int)ShimmerBluetooth.SensorBitmapShimmer3.SENSOR_D_ACCEL) > 0)
                {
                    checkBoxSensor2.Checked = true;
                    comboBoxAccelRange.Enabled = true;
                }
                else
                {
                    checkBoxSensor2.Checked = false;
                    comboBoxAccelRange.Enabled = false;
                }
                if (((enabledSensors & 0xFF) & (int)ShimmerBluetooth.SensorBitmapShimmer3.SENSOR_GYRO) > 0)
                {
                    checkBoxSensor3.Checked = true;
                    comboBoxGyroRange.Enabled = true;
                }
                else
                {
                    checkBoxSensor3.Checked = false;
                    comboBoxGyroRange.Enabled = false;
                }
                if (((enabledSensors & 0xFF) & (int)ShimmerBluetooth.SensorBitmapShimmer3.SENSOR_MAG) > 0)
                {
                    checkBoxSensor4.Checked = true;
                    if (shimmerVersion == (int)ShimmerBluetooth.ShimmerVersion.SHIMMER3 && PConfiguration.PControlForm.ShimmerDevice.isShimmer3withUpdatedSensors()) //new mag sensor (updated Shimmer 3) has no variable mag range
                    {
                        checkBoxLowPowerMag.Enabled = false;
                        checkBoxLowPowerMag.Visible = false;
                        comboBoxMagRange.Visible = false;
                        comboBoxMagRange.Enabled = false;
                        labelMagRange.Visible = false;
                    }
                    else
                    {
                        checkBoxLowPowerMag.Visible = true;
                        checkBoxLowPowerMag.Enabled = true;
                        comboBoxMagRange.Visible = true;
                        if (shimmerVersion != (int)ShimmerBluetooth.ShimmerVersion.SHIMMER3R)
                        {   //On Shimmer3R this is the alt mag range, so only enable if the alt mag checkbox is selected (below)
                            comboBoxMagRange.Enabled = true;
                        }
                        labelMagRange.Visible = true;
                    }
                }
                else
                {
                    checkBoxSensor4.Checked = false;
                    checkBoxLowPowerMag.Enabled = false;
                    comboBoxMagRange.Enabled = false;
                }
                if (((enabledSensors & 0xFFFFFF) & (int)ShimmerBluetooth.SensorBitmapShimmer3.SENSOR_MAG_ALT) > 0)
                {
                    if (shimmerVersion == (int)ShimmerBluetooth.ShimmerVersion.SHIMMER3R)
                    {
                        comboBoxAltMagRate.Enabled = true;
                        comboBoxMagRange.Enabled = true; //This is the alt mag range on Shimmer3R
                    }
                } else
                {
                    if (shimmerVersion == (int)ShimmerBluetooth.ShimmerVersion.SHIMMER3R)
                    {
                        comboBoxAltMagRate.Enabled = false;
                        comboBoxMagRange.Enabled = false; //This is the alt mag range on Shimmer3R
                    }
                }
                if (((enabledSensors & 0xFFFF) & (int)ShimmerBluetooth.SensorBitmapShimmer3.SENSOR_VBATT) > 0)
                {
                    checkBoxSensor5.Checked = true;
                }
                else
                {
                    checkBoxSensor5.Checked = false;
                }
                if (((enabledSensors & 0xFFFFFF) & (int)ShimmerBluetooth.SensorBitmapShimmer3.SENSOR_EXT_A15) > 0)
                {
                    checkBoxSensor8.Checked = true;
                }
                else
                {
                    checkBoxSensor8.Checked = false;
                }
                if (((enabledSensors & 0xFFFFFF) & (int)ShimmerBluetooth.SensorBitmapShimmer3.SENSOR_EXT_A7) > 0)
                {
                    checkBoxSensor6.Checked = true;
                }
                else
                {
                    checkBoxSensor6.Checked = false;
                }
                if (((enabledSensors & 0xFFFFFF) & (int)ShimmerBluetooth.SensorBitmapShimmer3.SENSOR_EXT_A6) > 0)
                {
                    checkBoxSensor7.Checked = true;
                }
                else
                {
                    checkBoxSensor7.Checked = false;
                }
                if (((enabledSensors & 0xFFFFFF) & (int)ShimmerBluetooth.SensorBitmapShimmer3.SENSOR_INT_A1) > 0)
                {
                    checkBoxSensor9.Checked = true;
                }
                else
                {
                    checkBoxSensor9.Checked = false;
                }
                if (((enabledSensors & 0xFFFFFF) & (int)ShimmerBluetooth.SensorBitmapShimmer3.SENSOR_INT_A12) > 0)
                {
                    checkBoxSensor10.Checked = true;
                }
                else
                {
                    checkBoxSensor10.Checked = false;
                }
                if (((enabledSensors & 0xFFFFFF) & (int)ShimmerBluetooth.SensorBitmapShimmer3.SENSOR_INT_A13) > 0)
                {
                    checkBoxSensor11.Checked = true;
                }
                else
                {
                    checkBoxSensor11.Checked = false;
                }
                if (((enabledSensors & 0xFFFFFF) & (int)ShimmerBluetooth.SensorBitmapShimmer3.SENSOR_INT_A14) > 0)
                {
                    checkBoxSensor12.Checked = true;
                }
                else
                {
                    checkBoxSensor12.Checked = false;
                }
                if ((enabledSensors & (int)ShimmerBluetooth.SensorBitmapShimmer3.SENSOR_BMP180_PRESSURE) > 0)
                {
                    checkBoxSensor13.Checked = true;
                    comboBoxPressureRes.Enabled = true;
                }
                else
                {
                    checkBoxSensor13.Checked = false;
                    comboBoxPressureRes.Enabled = false;
                }
                if ((enabledSensors & (int)ShimmerBluetooth.SensorBitmapShimmer3.SENSOR_GSR) > 0)
                {
                    checkBoxSensor14.Checked = true;
                    comboBoxGSRRange.Enabled = true;
                }
                else
                {
                    checkBoxSensor14.Checked = false;
                    comboBoxGSRRange.Enabled = false;
                }

                if (((enabledSensors & 0xFFFFFF) & (int)ShimmerBluetooth.SensorBitmapShimmer3.SENSOR_INT_A1) > 0)
                {
                    if (PConfiguration.PControlForm.PPGSignalName.Equals(Shimmer3Configuration.SignalNames.INTERNAL_ADC_A1))
                    {
                        comboBoxPPGAdcChannel.SelectedIndex = 0;
                    }
                }

                if (((enabledSensors & 0xFFFFFF) & (int)ShimmerBluetooth.SensorBitmapShimmer3.SENSOR_INT_A12) > 0)
                {
                    if (PConfiguration.PControlForm.PPGSignalName.Equals(Shimmer3Configuration.SignalNames.INTERNAL_ADC_A12)
                        || PConfiguration.PControlForm.PPGSignalName.Equals(Shimmer3RConfiguration.SignalNames.GEN_INT_ADC_A0))
                    {
                        comboBoxPPGAdcChannel.SelectedIndex = 1;
                    }

                }

                if (((enabledSensors & 0xFFFFFF) & (int)ShimmerBluetooth.SensorBitmapShimmer3.SENSOR_INT_A13) > 0)
                {
                    if (PConfiguration.PControlForm.PPGSignalName.Equals(Shimmer3Configuration.SignalNames.INTERNAL_ADC_A13)
                         || PConfiguration.PControlForm.PPGSignalName.Equals(Shimmer3RConfiguration.SignalNames.GEN_INT_ADC_A1))
                    {
                        comboBoxPPGAdcChannel.SelectedIndex = 2;
                    }

                }

                if (((enabledSensors & 0xFFFFFF) & (int)ShimmerBluetooth.SensorBitmapShimmer3.SENSOR_INT_A14) > 0)
                {
                    if (PConfiguration.PControlForm.PPGSignalName.Equals(Shimmer3Configuration.SignalNames.INTERNAL_ADC_A14))
                    {
                        comboBoxPPGAdcChannel.SelectedIndex = 3;
                    }
                }

                if (((((enabledSensors & 0xFFFFFF) & (int)ShimmerBluetooth.SensorBitmapShimmer3.SENSOR_INT_A1) == 0) && (((enabledSensors & 0xFFFFFF) & (int)ShimmerBluetooth.SensorBitmapShimmer3.SENSOR_INT_A12) == 0) && (((enabledSensors & 0xFFFFFF) & (int)ShimmerBluetooth.SensorBitmapShimmer3.SENSOR_INT_A13) == 0) && (((enabledSensors & 0xFFFFFF) & (int)ShimmerBluetooth.SensorBitmapShimmer3.SENSOR_INT_A14) == 0)) && (PConfiguration.PControlForm.ShimmerDevice.GetInternalExpPower() == 1))
                {
                    /*
                    MessageBox.Show("Internal Exp Power is enabled but no internal ADC has been enabled. Disable Internal Exp Power to conserve battery.", Control.ApplicationName,
                                   MessageBoxButtons.OK, MessageBoxIcon.Warning);
                     */
                }

                //if ((enabledSensors & (int)Shimmer.SensorBitmapShimmer3.SENSOR_EXG1_24BIT) > 0)
                //{
                //    checkBoxSensor15.Checked = true;
                //}
                //else
                //{
                //    checkBoxSensor15.Checked = false;
                //}
                //if ((enabledSensors & (int)Shimmer.SensorBitmapShimmer3.SENSOR_EXG2_24BIT) > 0)
                //{
                //    checkBoxSensor16.Checked = true;
                //}
                //else
                //{
                //    checkBoxSensor16.Checked = false;
                //}
                //if ((enabledSensors & (int)Shimmer.SensorBitmapShimmer3.SENSOR_EXG1_16BIT) > 0)
                //{
                //    checkBoxSensor17.Checked = true;
                //}
                //else
                //{
                //    checkBoxSensor17.Checked = false;
                //}
                //if ((enabledSensors & (int)Shimmer.SensorBitmapShimmer3.SENSOR_EXG2_16BIT) > 0)
                //{
                //    checkBoxSensor18.Checked = true;
                //}
                //else
                //{
                //    checkBoxSensor18.Checked = false;
                //}
                // ExG -> ECG/EMG

                checkBoxSensor18.Visible = false;
                checkBoxSensor18.Enabled = false;
                checkBoxSensor20.Visible = false;
                checkBoxSensor20.Enabled = false;
                checkBoxSensor21.Visible = false;
                checkBoxSensor21.Enabled = false;
                checkBoxSensor22.Visible = false;
                checkBoxSensor22.Enabled = false;

                if (PConfiguration.PControlForm.ShimmerDevice.GetShimmerVersion() == (int)ShimmerBluetooth.ShimmerVersion.SHIMMER3
                    || PConfiguration.PControlForm.ShimmerDevice.GetShimmerVersion() == (int)ShimmerBluetooth.ShimmerVersion.SHIMMER3R)
                {
                    if ((enabledSensors & (int)ShimmerBluetooth.SensorBitmapShimmer3.SENSOR_EXG1_24BIT) > 0 && (enabledSensors & (int)ShimmerBluetooth.SensorBitmapShimmer3.SENSOR_EXG2_24BIT) > 0)
                    {
                        comboBoxExGResolution.SelectedIndex = 0;
                        if (PConfiguration.PControlForm.ShimmerDevice.IsDefaultECGConfigurationEnabled())
                        {
                            checkBoxSensor15.Checked = true; //15 ecg 
                            checkBoxSensor16.Checked = false;
                            checkBoxSensor17.Checked = false;
                            checkBoxSensor23.Checked = false;
                        }
                        else if (PConfiguration.PControlForm.ShimmerDevice.IsDefaultEMGConfigurationEnabled())
                        {
                            checkBoxSensor15.Checked = false; //15 ecg 
                            checkBoxSensor16.Checked = true;
                            checkBoxSensor17.Checked = false;
                            checkBoxSensor23.Checked = false;

                        }
                        else if (PConfiguration.PControlForm.ShimmerDevice.IsDefaultExgTestSignalConfigurationEnabled())
                        {
                            checkBoxSensor15.Checked = false; //15 ecg 
                            checkBoxSensor16.Checked = false;
                            checkBoxSensor17.Checked = true;
                            checkBoxSensor23.Checked = false;
                        }
                        else if (PConfiguration.PControlForm.ShimmerDevice.IsDefaultRespirationConfigurationEnabled())
                        {
                            checkBoxSensor15.Checked = false; //15 ecg 
                            checkBoxSensor16.Checked = false;
                            checkBoxSensor17.Checked = false;
                            checkBoxSensor23.Checked = true;
                        }
                        else
                        {
                            checkBoxSensor15.Checked = false; //15 ecg 
                            checkBoxSensor16.Checked = false;
                            checkBoxSensor17.Checked = false;
                            checkBoxSensor23.Checked = false;
                        }
                    }
                    else if ((enabledSensors & (int)ShimmerBluetooth.SensorBitmapShimmer3.SENSOR_EXG1_24BIT) > 0)
                    {
                        comboBoxExGResolution.SelectedIndex = 0;
                        if (PConfiguration.PControlForm.ShimmerDevice.IsDefaultEMGConfigurationEnabled())
                        {
                            checkBoxSensor15.Checked = false; //15 ecg 
                            checkBoxSensor16.Checked = true;
                            checkBoxSensor17.Checked = false;
                            checkBoxSensor23.Checked = false;
                        }

                    }
                    else if ((enabledSensors & (int)ShimmerBluetooth.SensorBitmapShimmer3.SENSOR_EXG1_16BIT) > 0 && (enabledSensors & (int)ShimmerBluetooth.SensorBitmapShimmer3.SENSOR_EXG2_16BIT) > 0)
                    {
                        comboBoxExGResolution.SelectedIndex = 1;
                        if (PConfiguration.PControlForm.ShimmerDevice.IsDefaultECGConfigurationEnabled())
                        {
                            checkBoxSensor15.Checked = true; //15 ecg 
                            checkBoxSensor16.Checked = false;
                            checkBoxSensor17.Checked = false;
                            checkBoxSensor23.Checked = false;

                        }
                        else if (PConfiguration.PControlForm.ShimmerDevice.IsDefaultEMGConfigurationEnabled())
                        {
                            checkBoxSensor15.Checked = false; //15 ecg 
                            checkBoxSensor16.Checked = true;
                            checkBoxSensor17.Checked = false;
                            checkBoxSensor23.Checked = false;

                        }
                        else if (PConfiguration.PControlForm.ShimmerDevice.IsDefaultExgTestSignalConfigurationEnabled())
                        {
                            checkBoxSensor15.Checked = false; //15 ecg 
                            checkBoxSensor16.Checked = false;
                            checkBoxSensor17.Checked = true;
                            checkBoxSensor23.Checked = false;
                        }
                        else if (PConfiguration.PControlForm.ShimmerDevice.IsDefaultRespirationConfigurationEnabled())
                        {
                            checkBoxSensor15.Checked = false; //15 ecg 
                            checkBoxSensor16.Checked = false;
                            checkBoxSensor17.Checked = false;
                            checkBoxSensor23.Checked = true;
                        }
                        else
                        {
                            checkBoxSensor15.Checked = false; //15 ecg 
                            checkBoxSensor16.Checked = false;
                            checkBoxSensor17.Checked = false;
                            checkBoxSensor23.Checked = false;

                        }
                    }
                    else if ((enabledSensors & (int)ShimmerBluetooth.SensorBitmapShimmer3.SENSOR_EXG1_16BIT) > 0)
                    {
                        comboBoxExGResolution.SelectedIndex = 1;
                        if (PConfiguration.PControlForm.ShimmerDevice.IsDefaultEMGConfigurationEnabled())
                        {
                            checkBoxSensor15.Checked = false; //15 ecg 
                            checkBoxSensor16.Checked = true;
                            checkBoxSensor17.Checked = false;
                            checkBoxSensor23.Checked = false;

                        }
                    }
                    else
                    {
                        comboBoxExGResolution.Enabled = false;
                        comboBoxExgGain.Enabled = false;

                    }
                    if ((enabledSensors & (int)ShimmerBluetooth.SensorBitmapShimmer3.SENSOR_EXG1_16BIT) > 0 || (enabledSensors & (int)ShimmerBluetooth.SensorBitmapShimmer3.SENSOR_EXG2_16BIT) > 0 || (enabledSensors & (int)ShimmerBluetooth.SensorBitmapShimmer3.SENSOR_EXG2_24BIT) > 0 || (enabledSensors & (int)ShimmerBluetooth.SensorBitmapShimmer3.SENSOR_EXG2_24BIT) > 0)
                    {
                        if (!PConfiguration.PControlForm.ShimmerDevice.IsDefaultECGConfigurationEnabled() && !PConfiguration.PControlForm.ShimmerDevice.IsDefaultEMGConfigurationEnabled() && !PConfiguration.PControlForm.ShimmerDevice.IsDefaultExgTestSignalConfigurationEnabled() && !PConfiguration.PControlForm.ShimmerDevice.IsDefaultRespirationConfigurationEnabled())
                        {
                            MessageBox.Show("Custom ExG Configuration Detected.", Control.ApplicationName,
                                   MessageBoxButtons.OK, MessageBoxIcon.Warning);
                            checkBoxSensor18.Visible = true;
                            checkBoxSensor18.Enabled = true;
                            checkBoxSensor20.Visible = true;
                            checkBoxSensor20.Enabled = true;
                            checkBoxSensor21.Visible = true;
                            checkBoxSensor21.Enabled = true;
                            checkBoxSensor22.Visible = true;
                            checkBoxSensor22.Enabled = true;
                            checkBoxSensor21.Text = "ExG1 16Bit";
                            checkBoxSensor22.Text = "ExG2 16Bit";
                            checkBoxSensor18.Text = "ExG1 24Bit";
                            checkBoxSensor20.Text = "ExG2 24Bit";
                            comboBoxExGResolution.Enabled = false;

                            if ((enabledSensors & (int)ShimmerBluetooth.SensorBitmapShimmer3.SENSOR_EXG1_16BIT) > 0)
                            {
                                checkBoxSensor21.Checked = true;

                            }
                            if ((enabledSensors & (int)ShimmerBluetooth.SensorBitmapShimmer3.SENSOR_EXG2_16BIT) > 0)
                            {
                                checkBoxSensor22.Checked = true;

                            }
                            if ((enabledSensors & (int)ShimmerBluetooth.SensorBitmapShimmer3.SENSOR_EXG1_24BIT) > 0)
                            {
                                checkBoxSensor18.Checked = true;

                            }
                            if ((enabledSensors & (int)ShimmerBluetooth.SensorBitmapShimmer3.SENSOR_EXG2_24BIT) > 0)
                            {
                                checkBoxSensor20.Checked = true;

                            }
                        }
                    }

                    if (PConfiguration.PControlForm.ECGSignalName.Equals(Shimmer3Configuration.SignalNames.ECG_LL_RA))
                    {
                        comboBoxSelectECGChannel.SelectedIndex = 0;
                    }
                    else if (PConfiguration.PControlForm.ECGSignalName.Equals(Shimmer3Configuration.SignalNames.ECG_LA_RA))
                    {
                        comboBoxSelectECGChannel.SelectedIndex = 1;
                    }
                    else if (PConfiguration.PControlForm.ECGSignalName.Equals(Shimmer3Configuration.SignalNames.EXG2_CH1))
                    {
                        comboBoxSelectECGChannel.SelectedIndex = 2;
                    }
                    else if (PConfiguration.PControlForm.ECGSignalName.Equals(Shimmer3Configuration.SignalNames.ECG_VX_RL))
                    {
                        comboBoxSelectECGChannel.SelectedIndex = 3;
                    }
                    else
                    {
                        comboBoxSelectECGChannel.SelectedIndex = 0;
                    }
                }

                if ((enabledSensors & (int)ShimmerBluetooth.SensorBitmapShimmer3.SENSOR_ACCEL_ALT) > 0)
                {
                    checkBoxSensorHGAccel.Checked = true;
                }
                else
                {
                    checkBoxSensorHGAccel.Checked = false;
                }

                if ((enabledSensors & (int)ShimmerBluetooth.SensorBitmapShimmer3.SENSOR_MAG_ALT) > 0)
                {
                    checkBoxSensorAltMag.Checked = true;
                }
                else
                {
                    checkBoxSensorAltMag.Checked = false;
                }

                if (((enabledSensors & 0xFF00) & (int)ShimmerBluetooth.SensorBitmapShimmer3.SENSOR_BRIDGE_AMP) > 0)
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
            if (PConfiguration.PControlForm.ShimmerDevice.GetShimmerVersion() == (int)ShimmerBluetooth.ShimmerVersion.SHIMMER3
                || PConfiguration.PControlForm.ShimmerDevice.GetShimmerVersion() == (int)ShimmerBluetooth.ShimmerVersion.SHIMMER3R)
            {
                if (checkBoxSensor1.Checked)
                {
                    comboBoxLNAccelRange.Enabled = true;
                }
                else
                {
                    comboBoxLNAccelRange.Enabled = false;
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
            if (PConfiguration.PControlForm.ShimmerDevice.GetShimmerVersion() == (int)ShimmerBluetooth.ShimmerVersion.SHIMMER3
                || PConfiguration.PControlForm.ShimmerDevice.GetShimmerVersion() == (int)ShimmerBluetooth.ShimmerVersion.SHIMMER3R)
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
                    //checkBoxSensor9.Checked = false;
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
            if (PConfiguration.PControlForm.ShimmerDevice.GetShimmerVersion() == (int)ShimmerBluetooth.ShimmerVersion.SHIMMER3
                || PConfiguration.PControlForm.ShimmerDevice.GetShimmerVersion() == (int)ShimmerBluetooth.ShimmerVersion.SHIMMER3R)
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
                    //checkBoxSensor9.Checked = false;
                    if (!PConfiguration.PControlForm.ShimmerDevice.GetFirmwareVersionFullName().Equals("BoilerPlate 0.1.0"))
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
            int shimmerVersion = PConfiguration.PControlForm.ShimmerDevice.GetShimmerVersion();
            if (shimmerVersion == (int)ShimmerBluetooth.ShimmerVersion.SHIMMER3 || shimmerVersion == (int)ShimmerBluetooth.ShimmerVersion.SHIMMER3R)
            {
                if (checkBoxSensor4.Checked)
                {
                    if(shimmerVersion == (int)ShimmerBluetooth.ShimmerVersion.SHIMMER3)
                    {   //On Shimmer3R, this is the alt mag range, so do not enable if main mag is checked
                        comboBoxMagRange.Enabled = true;
                    }
                    checkBoxLowPowerMag.Enabled = true;
                }
                else
                {
                    if (shimmerVersion == (int)ShimmerBluetooth.ShimmerVersion.SHIMMER3)
                    {   //On Shimmer3R, this is the alt mag range, so do not disable if main mag is unchecked
                        comboBoxMagRange.Enabled = false;
                    }
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
            if (PConfiguration.PControlForm.ShimmerDevice.GetShimmerVersion() == (int)ShimmerBluetooth.ShimmerVersion.SHIMMER3 || PConfiguration.PControlForm.ShimmerDevice.GetShimmerVersion() == (int)ShimmerBluetooth.ShimmerVersion.SHIMMER3R)
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
            if (PConfiguration.PControlForm.ShimmerDevice.GetShimmerVersion() == (int)ShimmerBluetooth.ShimmerVersion.SHIMMER3 || PConfiguration.PControlForm.ShimmerDevice.GetShimmerVersion() == (int)ShimmerBluetooth.ShimmerVersion.SHIMMER3R)
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
                    //checkBoxSensor9.Checked = false;
                    checkBoxSensor10.Checked = false;
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
            if (PConfiguration.PControlForm.ShimmerDevice.GetShimmerVersion() == (int)ShimmerBluetooth.ShimmerVersion.SHIMMER3 || PConfiguration.PControlForm.ShimmerDevice.GetShimmerVersion() == (int)ShimmerBluetooth.ShimmerVersion.SHIMMER3R)
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
                    //checkBoxSensor9.Checked = false;
                    checkBoxSensor10.Checked = false;
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
            if (PConfiguration.PControlForm.ShimmerDevice.GetShimmerVersion() == (int)ShimmerBluetooth.ShimmerVersion.SHIMMER3 || PConfiguration.PControlForm.ShimmerDevice.GetShimmerVersion() == (int)ShimmerBluetooth.ShimmerVersion.SHIMMER3R)
            {
                if (checkBoxSensor9.Checked)
                {
                    checkBoxSensor14.Checked = false;
                    checkBoxSensor15.Checked = false;
                    checkBoxSensor16.Checked = false;
                    checkBoxSensor17.Checked = false;
                    checkBoxSensor23.Checked = false;

                    groupBoxECGtoHR.Enabled = false;
                    checkBoxEnableECGtoHR.Checked = false;
                    //Disable ExG tab
                    PConfiguration.tabControl1.TabPages[1].Enabled = false;
                    if (checkBoxIntExpPower.Checked)
                    {
                        groupBoxPPGtoHR.Enabled = true;
                    }
                }
                else
                {
                    if ((!checkBoxSensor12.Checked && !checkBoxSensor11.Checked && !checkBoxSensor10.Checked && !checkBoxSensor9.Checked))
                    {
                        groupBoxPPGtoHR.Enabled = false;
                    }
                    if (checkBoxIntExpPower.Checked == false)
                    {
                        groupBoxPPGtoHR.Enabled = false;
                    }
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
            if (PConfiguration.PControlForm.ShimmerDevice.GetShimmerVersion() == (int)ShimmerBluetooth.ShimmerVersion.SHIMMER3 || PConfiguration.PControlForm.ShimmerDevice.GetShimmerVersion() == (int)ShimmerBluetooth.ShimmerVersion.SHIMMER3R)
            {
                if (checkBoxSensor10.Checked)
                {
                    checkBoxSensor15.Checked = false;
                    checkBoxSensor16.Checked = false;
                    checkBoxSensor17.Checked = false;
                    checkBoxSensor23.Checked = false;

                    checkBoxSensor19.Checked = false;
                    groupBoxECGtoHR.Enabled = false;
                    checkBoxEnableECGtoHR.Checked = false;
                    //Disable ExG tab
                    PConfiguration.tabControl1.TabPages[1].Enabled = false;

                    if (checkBoxIntExpPower.Checked)
                    {
                        groupBoxPPGtoHR.Enabled = true;
                    }
                }
                else
                {
                    if ((!checkBoxSensor12.Checked && !checkBoxSensor11.Checked && !checkBoxSensor10.Checked && !checkBoxSensor9.Checked))
                    {
                        groupBoxPPGtoHR.Enabled = false;
                    }
                    if (checkBoxIntExpPower.Checked == false)
                    {
                        groupBoxPPGtoHR.Enabled = false;
                    }
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
            if (PConfiguration.PControlForm.ShimmerDevice.GetShimmerVersion() == (int)ShimmerBluetooth.ShimmerVersion.SHIMMER3 || PConfiguration.PControlForm.ShimmerDevice.GetShimmerVersion() == (int)ShimmerBluetooth.ShimmerVersion.SHIMMER3R)
            {
                if (checkBoxSensor11.Checked)
                {
                    checkBoxSensor15.Checked = false;
                    checkBoxSensor16.Checked = false;
                    checkBoxSensor17.Checked = false;
                    checkBoxSensor23.Checked = false;

                    checkBoxSensor19.Checked = false;
                    if (checkBoxIntExpPower.Checked)
                    {
                        groupBoxPPGtoHR.Enabled = true;
                    }
                    groupBoxECGtoHR.Enabled = false;
                    checkBoxEnableECGtoHR.Checked = false;
                    //Disable ExG tab
                    PConfiguration.tabControl1.TabPages[1].Enabled = false;
                }
                else
                {
                    if ((!checkBoxSensor12.Checked && !checkBoxSensor11.Checked && !checkBoxSensor10.Checked && !checkBoxSensor9.Checked))
                    {
                        groupBoxPPGtoHR.Enabled = false;
                    }
                    if (checkBoxIntExpPower.Checked == false)
                    {
                        groupBoxPPGtoHR.Enabled = false;
                    }
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
            if (PConfiguration.PControlForm.ShimmerDevice.GetShimmerVersion() == (int)ShimmerBluetooth.ShimmerVersion.SHIMMER3 || PConfiguration.PControlForm.ShimmerDevice.GetShimmerVersion() == (int)ShimmerBluetooth.ShimmerVersion.SHIMMER3R)
            {
                if (checkBoxSensor12.Checked)
                {
                    checkBoxSensor14.Checked = false;
                    checkBoxSensor15.Checked = false;
                    checkBoxSensor16.Checked = false;
                    checkBoxSensor17.Checked = false;
                    checkBoxSensor23.Checked = false;
                    checkBoxSensor19.Checked = false;
                    groupBoxECGtoHR.Enabled = false;
                    checkBoxEnableECGtoHR.Checked = false;
                    //Disable ExG tab
                    PConfiguration.tabControl1.TabPages[1].Enabled = false;
                    if (checkBoxIntExpPower.Checked)
                    {
                        groupBoxPPGtoHR.Enabled = true;
                    }
                }
                else
                {
                    if ((!checkBoxSensor12.Checked && !checkBoxSensor11.Checked && !checkBoxSensor10.Checked && !checkBoxSensor9.Checked))
                    {
                        groupBoxPPGtoHR.Enabled = false;
                    }
                    if (checkBoxIntExpPower.Checked == false)
                    {
                        groupBoxPPGtoHR.Enabled = false;
                    }

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
            if (PConfiguration.PControlForm.ShimmerDevice.GetShimmerVersion() == (int)ShimmerBluetooth.ShimmerVersion.SHIMMER3 || PConfiguration.PControlForm.ShimmerDevice.GetShimmerVersion() == (int)ShimmerBluetooth.ShimmerVersion.SHIMMER3R)
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
            if (PConfiguration.PControlForm.ShimmerDevice.GetShimmerVersion() == (int)ShimmerBluetooth.ShimmerVersion.SHIMMER3 || PConfiguration.PControlForm.ShimmerDevice.GetShimmerVersion() == (int)ShimmerBluetooth.ShimmerVersion.SHIMMER3R)
            {
                if (checkBoxSensor14.Checked)
                {
                    comboBoxGSRRange.Enabled = true;
                    checkBoxSensor12.Checked = false;
                    checkBoxSensor9.Checked = false;
                    checkBoxSensor15.Checked = false;
                    checkBoxSensor16.Checked = false;
                    checkBoxSensor17.Checked = false;
                    checkBoxSensor23.Checked = false;
                    checkBoxSensor19.Checked = false;
                    groupBoxECGtoHR.Enabled = false;
                    checkBoxEnableECGtoHR.Checked = false;
                    //Disable ExG tab
                    PConfiguration.tabControl1.TabPages[1].Enabled = false;
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

        private void checkBoxSensor15_Click(object sender, EventArgs e) //ECG
        {
            if (PConfiguration.PControlForm.ShimmerDevice.GetShimmerVersion() == (int)ShimmerBluetooth.ShimmerVersion.SHIMMER3 || PConfiguration.PControlForm.ShimmerDevice.GetShimmerVersion() == (int)ShimmerBluetooth.ShimmerVersion.SHIMMER3R)
            {
                if (checkBoxSensor15.Checked)
                {
                    byte[] exg1Reg = { 2, 160, 16, 64, 64, 45, 0, 0, 2, 3 };
                    byte[] exg2Reg = { 2, 160, 16, 64, 71, 0, 0, 0, 2, 1 };
                    string sr = PConfiguration.userControlGeneralConfig1.comboBoxSamplingRate.SelectedItem.ToString();
                    string subsr = sr.Substring(0, sr.Length - 2);
                    double samplingRate = Double.Parse(subsr);

                    int exgSR = getExGSamplingSetting(samplingRate);

                    //adjust for sampling rate
                    exg1Reg[0] = (byte)(((exg1Reg[0] >> 3) << 3) | exgSR);
                    exg2Reg[0] = (byte)(((exg2Reg[0] >> 3) << 3) | exgSR);

                    byte[] reg1 = PConfiguration.ExgReg1UI;
                    byte[] reg2 = PConfiguration.ExgReg2UI;
                    byte byte4exg1 = (byte)((exg1Reg[3] & 143) | (0x40)); // recommended ExG gain for ECG is 4, so set gain to 4 when ECG is enabled
                    byte byte5exg1 = (byte)((exg1Reg[4] & 143) | (0x40));
                    byte byte4exg2 = (byte)((exg2Reg[3] & 143) | (0x40));
                    byte byte5exg2 = (byte)((exg2Reg[4] & 143) | (0x40));
                    exg1Reg[3] = byte4exg1;
                    exg1Reg[4] = byte5exg1;
                    exg2Reg[3] = byte4exg2;
                    exg2Reg[4] = byte5exg2;

                    comboBoxExGResolution.SelectedIndex = 0;
                    comboBoxExgGain.SelectedIndex = 3;

                    PConfiguration.ExgReg1UI = exg1Reg;
                    PConfiguration.ExgReg2UI = exg2Reg;
                }
            }
        }

        private void checkBoxSensor16_Click(object sender, EventArgs e) //EMG
        {
            if (PConfiguration.PControlForm.ShimmerDevice.GetShimmerVersion() == (int)ShimmerBluetooth.ShimmerVersion.SHIMMER3 || PConfiguration.PControlForm.ShimmerDevice.GetShimmerVersion() == (int)ShimmerBluetooth.ShimmerVersion.SHIMMER3R)
            {
                if (checkBoxSensor16.Checked)
                {

                    byte[] exg1Reg = { 2, 160, 16, 105, 96, 0, 0, 0, 2, 3 };
                    byte[] exg2Reg = { 2, 160, 16, 129, 129, 0, 0, 0, 2, 1 };
                    string sr = PConfiguration.userControlGeneralConfig1.comboBoxSamplingRate.SelectedItem.ToString();
                    string subsr = sr.Substring(0, sr.Length - 2);
                    double samplingRate = Double.Parse(subsr);

                    int exgSR = getExGSamplingSetting(samplingRate);

                    //adjust for sampling rate
                    exg1Reg[0] = (byte)(((exg1Reg[0] >> 3) << 3) | exgSR);
                    exg2Reg[0] = (byte)(((exg2Reg[0] >> 3) << 3) | exgSR);

                    byte[] reg1 = PConfiguration.ExgReg1UI;
                    byte[] reg2 = PConfiguration.ExgReg2UI;
                    byte byte4exg1 = (byte)((exg1Reg[3] & 143) | (0x60)); // recommended ExG gain for EMG is 12 (byte value 0x60)
                    byte byte5exg1 = (byte)((exg1Reg[4] & 143) | (0x60));
                    byte byte4exg2 = (byte)((exg2Reg[3] & 143) | (0x60));
                    byte byte5exg2 = (byte)((exg2Reg[4] & 143) | (0x60));
                    exg1Reg[3] = byte4exg1;
                    exg1Reg[4] = byte5exg1;
                    exg2Reg[3] = byte4exg2;
                    exg2Reg[4] = byte5exg2;

                    checkBoxSensor15.Checked = false;
                    checkBoxSensor17.Checked = false;
                    checkBoxSensor23.Checked = false;

                    comboBoxExGResolution.SelectedIndex = 0;
                    comboBoxExgGain.SelectedIndex = 6;

                    PConfiguration.ExgReg1UI = exg1Reg;
                    PConfiguration.ExgReg2UI = exg2Reg;
                }
            }
        }

        private void checkBoxSensor17_Click(object sender, EventArgs e)
        {
            if (PConfiguration.PControlForm.ShimmerDevice.GetShimmerVersion() == (int)ShimmerBluetooth.ShimmerVersion.SHIMMER3 || PConfiguration.PControlForm.ShimmerDevice.GetShimmerVersion() == (int)ShimmerBluetooth.ShimmerVersion.SHIMMER3R)
            {
                if (checkBoxSensor17.Checked)
                {
                    byte[] exg1Reg = { 2, 163, 16, 5, 5, 0, 0, 0, 2, 1 };
                    byte[] exg2Reg = { 2, 163, 16, 5, 5, 0, 0, 0, 2, 1 };
                    string sr = PConfiguration.userControlGeneralConfig1.comboBoxSamplingRate.SelectedItem.ToString();
                    string subsr = sr.Substring(0, sr.Length - 2);
                    double samplingRate = Double.Parse(subsr);

                    int exgSR = getExGSamplingSetting(samplingRate);

                    //adjust for sampling rate
                    exg1Reg[0] = (byte)(((exg1Reg[0] >> 3) << 3) | exgSR);
                    exg2Reg[0] = (byte)(((exg2Reg[0] >> 3) << 3) | exgSR);

                    byte[] reg1 = PConfiguration.ExgReg1UI;
                    byte[] reg2 = PConfiguration.ExgReg2UI;
                    byte byte4exg1 = (byte)((exg1Reg[3] & 143) | (0x40)); // recommended ExG gain for ExG Test Signal is 4 (0x40 byte value) so set this when ExG Test enabled
                    byte byte5exg1 = (byte)((exg1Reg[4] & 143) | (0x40));
                    byte byte4exg2 = (byte)((exg2Reg[3] & 143) | (0x40));
                    byte byte5exg2 = (byte)((exg2Reg[4] & 143) | (0x40));
                    exg1Reg[3] = byte4exg1;
                    exg1Reg[4] = byte5exg1;
                    exg2Reg[3] = byte4exg2;
                    exg2Reg[4] = byte5exg2;

                    checkBoxSensor15.Checked = false;
                    checkBoxSensor16.Checked = false;
                    checkBoxSensor23.Checked = false;
                    comboBoxExGResolution.Enabled = true;
                    comboBoxExgGain.Enabled = true;
                    comboBoxExGResolution.SelectedIndex = 0;
                    comboBoxExgGain.SelectedIndex = 3;

                    PConfiguration.ExgReg1UI = exg1Reg;
                    PConfiguration.ExgReg2UI = exg2Reg;
                }
            }
        }

        private void checkBoxAltMag_Click(object sender, EventArgs e)
        {
            //altmag enable sr 
            if (checkBoxSensorAltMag.Checked)
            {
                comboBoxAltMagRate.Enabled = true;
                comboBoxMagRange.Enabled = true;
            } else
            {
                comboBoxAltMagRate.Enabled = false;
                comboBoxMagRange.Enabled = false;
            }
        }

        private void checkBoxSensor24_Click(object sender, EventArgs e)
        {

        }

        private void checkBoxSensor23_Click(object sender, EventArgs e)
        {
            if (PConfiguration.PControlForm.ShimmerDevice.GetShimmerVersion() == (int)ShimmerBluetooth.ShimmerVersion.SHIMMER3 || PConfiguration.PControlForm.ShimmerDevice.GetShimmerVersion() == (int)ShimmerBluetooth.ShimmerVersion.SHIMMER3R)
            {
                if (checkBoxSensor23.Checked)
                {
                    byte[] exg1Reg = { 2, 168, 16, 64, 64, 32, 0, 0, 2, 1 };
                    byte[] exg2Reg = { 2, 160, 16, 64, 64, 0, 0, 0, 234, 1 };
                    string sr = PConfiguration.userControlGeneralConfig1.comboBoxSamplingRate.SelectedItem.ToString();
                    string subsr = sr.Substring(0, sr.Length - 2);
                    double samplingRate = Double.Parse(subsr);

                    int exgSR = getExGSamplingSetting(samplingRate);

                    //adjust for sampling rate
                    exg1Reg[0] = (byte)(((exg1Reg[0] >> 3) << 3) | exgSR);
                    exg2Reg[0] = (byte)(((exg2Reg[0] >> 3) << 3) | exgSR);

                    byte[] reg1 = PConfiguration.ExgReg1UI;
                    byte[] reg2 = PConfiguration.ExgReg2UI;
                    byte byte4exg1 = (byte)((exg1Reg[3] & 143) | (0x40)); // recommended ExG gain for ExG Test Signal is 4 (0x40 byte value) so set this when ExG Test enabled
                    byte byte5exg1 = (byte)((exg1Reg[4] & 143) | (0x40));
                    byte byte4exg2 = (byte)((exg2Reg[3] & 143) | (0x40));
                    byte byte5exg2 = (byte)((exg2Reg[4] & 143) | (0x40));
                    exg1Reg[3] = byte4exg1;
                    exg1Reg[4] = byte5exg1;
                    exg2Reg[3] = byte4exg2;
                    exg2Reg[4] = byte5exg2;

                    checkBoxSensor15.Checked = false;
                    checkBoxSensor16.Checked = false;
                    checkBoxSensor17.Checked = false;
                    comboBoxExGResolution.Enabled = true;
                    comboBoxExgGain.Enabled = true;
                    comboBoxExGResolution.SelectedIndex = 0;
                    comboBoxExgGain.SelectedIndex = 3;

                    PConfiguration.ExgReg1UI = exg1Reg;
                    PConfiguration.ExgReg2UI = exg2Reg;
                }
            }
        }


        private void checkBoxSensor19_Click(object sender, EventArgs e)
        {
            if (PConfiguration.PControlForm.ShimmerDevice.GetShimmerVersion() == (int)ShimmerBluetooth.ShimmerVersion.SHIMMER3 || PConfiguration.PControlForm.ShimmerDevice.GetShimmerVersion() == (int)ShimmerBluetooth.ShimmerVersion.SHIMMER3R)
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
                    checkBoxSensor23.Checked = false;
                    groupBoxPPGtoHR.Enabled = false;
                    checkBoxEnablePPGtoHR.Checked = false;
                    groupBoxECGtoHR.Enabled = false;
                    checkBoxEnableECGtoHR.Checked = false;
                    //Disable ExG tab
                    PConfiguration.tabControl1.TabPages[1].Enabled = false;
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
            int shimmerVersion = PConfiguration.PControlForm.ShimmerDevice.GetShimmerVersion();
            if (shimmerVersion == (int)ShimmerBluetooth.ShimmerVersion.SHIMMER3 || shimmerVersion == (int)ShimmerBluetooth.ShimmerVersion.SHIMMER3R)
            {
                if (checkBox3DOrientation.Checked)
                {
                    checkBoxSensor1.Checked = true;
                    checkBoxSensor2.Checked = true;
                    checkBoxSensor3.Checked = true;
                    checkBoxSensor4.Checked = true;
                    comboBoxLNAccelRange.Enabled = true;
                    comboBoxAccelRange.Enabled = true;
                    comboBoxGyroRange.Enabled = true;
                    if (shimmerVersion == (int)ShimmerBluetooth.ShimmerVersion.SHIMMER3)
                    {   //On Shimmer3R, this is the alt mag range, so don't enable it
                        comboBoxMagRange.Enabled = true;
                    }
                    checkBoxLowPowerMag.Enabled = true;

                    MessageBox.Show("If both Wide Range and Low Noise Accelerometer are enabled, Wide Range will be used by the Orientation Algorithm");
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

                    comboBoxAccelRange.Enabled = true;
                    if (!PConfiguration.PControlForm.ShimmerDevice.GetFirmwareVersionFullName().Equals("BoilerPlate 0.1.0"))
                    {
                        comboBoxMagRange.Enabled = true;
                    }
                    checkBoxLowPowerMag.Enabled = true;
                }
                else
                {

                }
            }
        }

        private void checkBoxGyroOnTheFly_Click(object sender, EventArgs e)
        {
            if (PConfiguration.PControlForm.ShimmerDevice.GetShimmerVersion() == (int)ShimmerBluetooth.ShimmerVersion.SHIMMER3 || PConfiguration.PControlForm.ShimmerDevice.GetShimmerVersion() == (int)ShimmerBluetooth.ShimmerVersion.SHIMMER3R)
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
            if (PConfiguration.PControlForm.ShimmerDevice.GetShimmerVersion() != (int)ShimmerBluetooth.ShimmerVersion.SHIMMER3)
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
            if (PConfiguration.PControlForm.ShimmerDevice.GetShimmerVersion() == (int)ShimmerBluetooth.ShimmerVersion.SHIMMER3 || PConfiguration.PControlForm.ShimmerDevice.GetShimmerVersion() == (int)ShimmerBluetooth.ShimmerVersion.SHIMMER3R)
            {
                
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
            PConfiguration.PControlForm.ShimmerDevice.ToggleLED();
        }

        private void buttonDetectExpansionBoard_Click(object sender, EventArgs e)
        {
            if (PConfiguration.PControlForm.ShimmerDevice.GetShimmerVersion() == 3 && PConfiguration.PControlForm.ShimmerDevice.GetCompatibilityCode() >= 4)
            {
                PConfiguration.PControlForm.ShimmerDevice.ReadExpansionBoard();
                ExpansionBoard = PConfiguration.PControlForm.ShimmerDevice.GetExpansionBoard();
                textBoxExpansionBoard.Text = ExpansionBoard;
            }
        }

        public void ApplyConfigurationChanges()
        {
            int selectedIndexSamplingRate = comboBoxSamplingRate.SelectedIndex;
            double samplingRate = -1;
            if (PConfiguration.PControlForm.ShimmerDevice.GetShimmerVersion() == (int)ShimmerBluetooth.ShimmerVersion.SHIMMER3 || PConfiguration.PControlForm.ShimmerDevice.GetShimmerVersion() == (int)ShimmerBluetooth.ShimmerVersion.SHIMMER3R)
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
                    PConfiguration.PControlForm.ShimmerDevice.WriteInternalExpPower(1);
                    PConfiguration.PControlForm.ShimmerDevice.SetExpPower(true);
                }
                else
                {
                    PConfiguration.PControlForm.ShimmerDevice.WriteInternalExpPower(0);
                    PConfiguration.PControlForm.ShimmerDevice.SetExpPower(false);
                }

                //ECGmd unit does not contain IMU's - IMU configuration calls are not supported
                if (PConfiguration.PControlForm.ShimmerDevice.GetFirmwareIdentifier() != (int)ShimmerBluetooth.FW_IDENTIFIER_SHIMMERECGMD)
                {
                    if (PConfiguration.PControlForm.ShimmerDevice.GetShimmerVersion() == (int)ShimmerBluetooth.ShimmerVersion.SHIMMER3)
                    {
                        PConfiguration.PControlForm.ShimmerDevice.WriteMagRange(comboBoxMagRange.SelectedIndex + 1);
                    } else if (PConfiguration.PControlForm.ShimmerDevice.GetShimmerVersion() == (int)ShimmerBluetooth.ShimmerVersion.SHIMMER3R)
                    {
                        PConfiguration.PControlForm.ShimmerDevice.WriteAltMagRange(comboBoxMagRange.SelectedIndex);
                    }
                    PConfiguration.PControlForm.ShimmerDevice.WriteGyroRange(comboBoxGyroRange.SelectedIndex);
                    PConfiguration.PControlForm.ShimmerDevice.WritePressureResolution(comboBoxPressureRes.SelectedIndex);
                }
                PConfiguration.PControlForm.ShimmerDevice.WriteBaudRate(comboBoxBaudRate.SelectedIndex);

                if (checkBoxIntExpPower.Checked)
                {
                    PConfiguration.PControlForm.PPGSignalName = comboBoxPPGAdcChannel.Text;
                    PConfiguration.PControlForm.EnablePPGtoHR(checkBoxEnablePPGtoHR.Checked);
                    PConfiguration.PControlForm.SetNumberOfBeatsToAve((int)numericUpDownBeatsToAve.Value);
                    PConfiguration.PControlForm.SetNumberOfBeatsToAveECG((int)numericUpDownBeatsToAveECG.Value);
                    if (numericUpDownBeatsToAve.Value != 1)
                    {
                        MessageBox.Show("IBI PPG is not functional when number of beats to average is not 1");
                    }
                }
                else
                {
                    PConfiguration.PControlForm.EnablePPGtoHR(false);
                }

                if (checkBoxSensor15.Checked || checkBoxSensor17.Checked)
                {
          
                    if (samplingRate <= 128)
                    {
                        MessageBox.Show("ECG to HR algorithm not enabled, a sampling rate of 128Hz and above is required.");
                        checkBoxEnableECGtoHR.Checked = false;
                    } else
                    {
                        PConfiguration.PControlForm.ECGSignalName = comboBoxSelectECGChannel.Text;
                        PConfiguration.PControlForm.EnableECGtoHR(checkBoxEnableECGtoHR.Checked);
                    }
                }
                else
                {
                    PConfiguration.PControlForm.EnableECGtoHR(false);
                }

            }
            else if (PConfiguration.PControlForm.ShimmerDevice.GetShimmerVersion() == (int)ShimmerBluetooth.ShimmerVersion.SHIMMER2R
                || PConfiguration.PControlForm.ShimmerDevice.GetShimmerVersion() == (int)ShimmerBluetooth.ShimmerVersion.SHIMMER2)
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
                    PConfiguration.PControlForm.ShimmerDevice.Write5VReg(1);
                }
                else
                {
                    PConfiguration.PControlForm.ShimmerDevice.Write5VReg(0);
                }
                if (checkBoxVoltageMon.Checked)
                {
                    PConfiguration.PControlForm.ShimmerDevice.WritePMux(1);
                }
                else
                {
                    PConfiguration.PControlForm.ShimmerDevice.WritePMux(0);
                }
                if (!PConfiguration.PControlForm.ShimmerDevice.GetFirmwareVersionFullName().Equals("BoilerPlate 0.1.0"))
                {
                    PConfiguration.PControlForm.ShimmerDevice.WriteMagRange(comboBoxMagRange.SelectedIndex);
                }

                if (checkBoxSensor5.Checked)
                {
                    if (samplingRate <= 128)
                    {
                        MessageBox.Show("ECG to HR algorithm not enabled, a sampling rate of 128Hz and above is required.");
                        checkBoxEnableECGtoHR.Checked = false;
                    } else
                    {
                        PConfiguration.PControlForm.ECGSignalName = comboBoxSelectECGChannel.Text;
                        PConfiguration.PControlForm.EnableECGtoHR(checkBoxEnableECGtoHR.Checked);
                    }
                }
                else
                {
                    PConfiguration.PControlForm.EnableECGtoHR(false);
                }
            }
            PConfiguration.PControlForm.ShimmerDevice.WriteSamplingRate(samplingRate);

            //ECGmd unit does not contain IMU's - IMU configuration calls are not supported
            if (PConfiguration.PControlForm.ShimmerDevice.GetFirmwareIdentifier() != (int)ShimmerBluetooth.FW_IDENTIFIER_SHIMMERECGMD)
                {
                PConfiguration.PControlForm.ShimmerDevice.SetLowPowerMag(checkBoxLowPowerMag.Checked);
                PConfiguration.PControlForm.ShimmerDevice.SetLowPowerAccel(checkBoxLowPowerAccel.Checked);
                PConfiguration.PControlForm.ShimmerDevice.SetLowPowerGyro(checkBoxLowPowerGyro.Checked);
                PConfiguration.PControlForm.ShimmerDevice.Set3DOrientation(checkBox3DOrientation.Checked);
                PConfiguration.PControlForm.ShimmerDevice.SetGyroOnTheFlyCalibration(checkBoxGyroOnTheFly.Checked, 100, 1.2);
            }
           
            if (!PConfiguration.PControlForm.ShimmerDevice.isShimmer3withUpdatedSensors())
            {
                //ECGmd unit does not contain IMU's - IMU configuration calls are not supported
                if (PConfiguration.PControlForm.ShimmerDevice.GetFirmwareIdentifier() != (int)ShimmerBluetooth.FW_IDENTIFIER_SHIMMERECGMD)
                {
                    PConfiguration.PControlForm.ShimmerDevice.WriteAccelRange(comboBoxAccelRange.SelectedIndex);
                }
            }
            else //updated Shimmer3 (0,2,3,1)
            {
                if (PConfiguration.PControlForm.ShimmerDevice.GetShimmerVersion() != (int)ShimmerBluetooth.ShimmerVersion.SHIMMER3R)
                {
                    if (comboBoxAccelRange.SelectedIndex == 0)
                    {
                        PConfiguration.PControlForm.ShimmerDevice.WriteAccelRange(0);
                    }
                    else if (comboBoxAccelRange.SelectedIndex == 1)
                    {
                        PConfiguration.PControlForm.ShimmerDevice.WriteAccelRange(2);
                    }
                    else if (comboBoxAccelRange.SelectedIndex == 2)
                    {
                        PConfiguration.PControlForm.ShimmerDevice.WriteAccelRange(3);
                    }
                    else if (comboBoxAccelRange.SelectedIndex == 3)
                    {
                        PConfiguration.PControlForm.ShimmerDevice.WriteAccelRange(1);
                    }
                }
                if (PConfiguration.PControlForm.ShimmerDevice.GetShimmerVersion() == (int)ShimmerBluetooth.ShimmerVersion.SHIMMER3R)
                {
                    if (comboBoxAccelRange.SelectedIndex == 0)
                    {
                        PConfiguration.PControlForm.ShimmerDevice.WriteAccelRange(0);
                    }
                    else if (comboBoxAccelRange.SelectedIndex == 1)
                    {
                        PConfiguration.PControlForm.ShimmerDevice.WriteAccelRange(1);
                    }
                    else if (comboBoxAccelRange.SelectedIndex == 2)
                    {
                        PConfiguration.PControlForm.ShimmerDevice.WriteAccelRange(2);
                    }
                    else if (comboBoxAccelRange.SelectedIndex == 3)
                    {
                        PConfiguration.PControlForm.ShimmerDevice.WriteAccelRange(3);
                    }

                    if (comboBoxLNAccelRange.SelectedIndex == 0)
                    {
                        PConfiguration.PControlForm.ShimmerDevice.WriteLNAccelRange(0);
                    }
                    else if (comboBoxLNAccelRange.SelectedIndex == 1)
                    {
                        PConfiguration.PControlForm.ShimmerDevice.WriteLNAccelRange(1);
                    }
                    else if (comboBoxLNAccelRange.SelectedIndex == 2)
                    {
                        PConfiguration.PControlForm.ShimmerDevice.WriteLNAccelRange(2);
                    }
                    else if (comboBoxLNAccelRange.SelectedIndex == 3)
                    {
                        PConfiguration.PControlForm.ShimmerDevice.WriteLNAccelRange(3);
                    }

                    if (comboBoxAltMagRate.SelectedIndex == 0)
                    {
                        PConfiguration.PControlForm.ShimmerDevice.WriteAltMagRate(0x00);
                    }
                    else if (comboBoxAltMagRate.SelectedIndex == 1)
                    {
                        PConfiguration.PControlForm.ShimmerDevice.WriteAltMagRate(0x38);
                    }
                    else if (comboBoxAltMagRate.SelectedIndex == 2)
                    {
                        PConfiguration.PControlForm.ShimmerDevice.WriteAltMagRate(0x3A);
                    }
                    else if (comboBoxAltMagRate.SelectedIndex == 3)
                    {
                        PConfiguration.PControlForm.ShimmerDevice.WriteAltMagRate(0x3E);
                    }
                    else if (comboBoxAltMagRate.SelectedIndex == 4)
                    {
                        PConfiguration.PControlForm.ShimmerDevice.WriteAltMagRate(0x31);
                    }
                    else if (comboBoxAltMagRate.SelectedIndex == 5)
                    {
                        PConfiguration.PControlForm.ShimmerDevice.WriteAltMagRate(0x21);
                    }
                    else if (comboBoxAltMagRate.SelectedIndex == 6)
                    {
                        PConfiguration.PControlForm.ShimmerDevice.WriteAltMagRate(0x11);
                    }
                    else if (comboBoxAltMagRate.SelectedIndex == 7)
                    {
                        PConfiguration.PControlForm.ShimmerDevice.WriteAltMagRate(0x01);
                    }

                }

            }
            
            
            //ECGmd unit does not contain IMU's - IMU configuration calls are not supported
            if (PConfiguration.PControlForm.ShimmerDevice.GetFirmwareIdentifier() != (int)ShimmerBluetooth.FW_IDENTIFIER_SHIMMERECGMD)
            {
                PConfiguration.PControlForm.ShimmerDevice.WriteGSRRange(comboBoxGSRRange.SelectedIndex);
            }


                if (checkBoxComma.Checked)
            {
                PConfiguration.PControlForm.SetLoggingFormat(",");
            }
            else
            {
                PConfiguration.PControlForm.SetLoggingFormat("\t");
            }


            EnableSensors();    //Called last

            if (BaudRateChangeFlag)
            { // to change baud rate, must disconnect then reconnect to the Shimmer
                PConfiguration.PControlForm.ShimmerDevice.Disconnect();
                System.Threading.Thread.Sleep(200);
                PConfiguration.PControlForm.ShimmerDevice.StartConnectThread();
            }
        }

        private void EnableSensors()
        {
            if (PConfiguration.PControlForm.ShimmerDevice.GetShimmerVersion() == (int)ShimmerBluetooth.ShimmerVersion.SHIMMER3 || PConfiguration.PControlForm.ShimmerDevice.GetShimmerVersion() == (int)ShimmerBluetooth.ShimmerVersion.SHIMMER3R)
            {
                if (checkBoxSensor1.Checked)
                {
                    ReturnEnabledSensors = ReturnEnabledSensors | (int)ShimmerBluetooth.SensorBitmapShimmer3.SENSOR_A_ACCEL;
                }
                if (checkBoxSensor2.Checked)
                {
                    ReturnEnabledSensors = ReturnEnabledSensors | (int)ShimmerBluetooth.SensorBitmapShimmer3.SENSOR_D_ACCEL;
                }
                if (checkBoxSensor3.Checked)
                {
                    ReturnEnabledSensors = ReturnEnabledSensors | (int)ShimmerBluetooth.SensorBitmapShimmer3.SENSOR_GYRO;
                }
                if (checkBoxSensor4.Checked)
                {
                    ReturnEnabledSensors = ReturnEnabledSensors | (int)ShimmerBluetooth.SensorBitmapShimmer3.SENSOR_MAG;
                }
                if (checkBoxSensor5.Checked)
                {
                    ReturnEnabledSensors = ReturnEnabledSensors | (int)ShimmerBluetooth.SensorBitmapShimmer3.SENSOR_VBATT;
                }
                if (checkBoxSensor6.Checked)
                {
                    ReturnEnabledSensors = ReturnEnabledSensors | (int)ShimmerBluetooth.SensorBitmapShimmer3.SENSOR_EXT_A7;
                }
                if (checkBoxSensor7.Checked)
                {
                    ReturnEnabledSensors = ReturnEnabledSensors | (int)ShimmerBluetooth.SensorBitmapShimmer3.SENSOR_EXT_A6;
                }
                if (checkBoxSensor8.Checked)
                {
                    ReturnEnabledSensors = ReturnEnabledSensors | (int)ShimmerBluetooth.SensorBitmapShimmer3.SENSOR_EXT_A15;
                }
                if (checkBoxSensor9.Checked)
                {
                    ReturnEnabledSensors = ReturnEnabledSensors | (int)ShimmerBluetooth.SensorBitmapShimmer3.SENSOR_INT_A1;
                }
                if (checkBoxSensor10.Checked)
                {
                    ReturnEnabledSensors = ReturnEnabledSensors | (int)ShimmerBluetooth.SensorBitmapShimmer3.SENSOR_INT_A12;
                }
                if (checkBoxSensor11.Checked)
                {
                    ReturnEnabledSensors = ReturnEnabledSensors | (int)ShimmerBluetooth.SensorBitmapShimmer3.SENSOR_INT_A13;
                }
                if (checkBoxSensor12.Checked)
                {
                    ReturnEnabledSensors = ReturnEnabledSensors | (int)ShimmerBluetooth.SensorBitmapShimmer3.SENSOR_INT_A14;
                }
                if (checkBoxSensor13.Checked)
                {
                    ReturnEnabledSensors = ReturnEnabledSensors | (int)ShimmerBluetooth.SensorBitmapShimmer3.SENSOR_BMP180_PRESSURE;
                }
                if (checkBoxSensor14.Checked)
                {
                    ReturnEnabledSensors = ReturnEnabledSensors | (int)ShimmerBluetooth.SensorBitmapShimmer3.SENSOR_GSR;
                }
                if (checkBoxSensor15.Checked)
                {
                    //textBoxEXGChip1Reg1.Text = "2-160-16-64-64-45-0-0-2-3";
                    //textBoxEXGChip2Reg1.Text = "2-160-16-64-71-0-0-0-2-1";
                    //Hex : textBoxEXGChip1Reg1.Text = "02-A0-10-40-40-2D-00-00-02-03";
                    //Hex : textBoxEXGChip2Reg1.Text = "02-A0-10-40-47-00-00-00-02-01";
                    // byte[] reg1 ={ 2, (byte)160, 16, 64, 64, 45, 0, 0, 2, 3 };
                    // byte[] reg2 = { 2, (byte)160, 16, 64, 71, 0, 0, 0, 2, 1 };

                    if (comboBoxExGResolution.SelectedIndex == 0)
                    {
                        ReturnEnabledSensors = ReturnEnabledSensors | (int)ShimmerBluetooth.SensorBitmapShimmer3.SENSOR_EXG1_24BIT;
                        ReturnEnabledSensors = ReturnEnabledSensors | (int)ShimmerBluetooth.SensorBitmapShimmer3.SENSOR_EXG2_24BIT;
                    }
                    else
                    {
                        ReturnEnabledSensors = ReturnEnabledSensors | (int)ShimmerBluetooth.SensorBitmapShimmer3.SENSOR_EXG1_16BIT;
                        ReturnEnabledSensors = ReturnEnabledSensors | (int)ShimmerBluetooth.SensorBitmapShimmer3.SENSOR_EXG2_16BIT;
                    }
                    //check exg configuration

                    //update sampling rate and exg gain

                    PConfiguration.PControlForm.ShimmerDevice.WriteEXGConfigurations(PConfiguration.ExgReg1UI, PConfiguration.ExgReg2UI);
                }
                if (checkBoxSensor16.Checked)
                {
                    //textBoxEXGChip1Reg1.Text = "2-160-16-105-96-0-0-0-2-3";
                    //textBoxEXGChip2Reg1.Text = "2-160-16-129-129-0-0-0-2-1";
                    //Hex : textBoxEXGChip1Reg1.Text = "02-A0-10-69-60-00-00-00-02-03";
                    //Hex : textBoxEXGChip2Reg1.Text = "02-A0-10-81-81-00-00-00-02-01";
                    //byte[] reg1 = { 2, (byte)160, 16, 105, 96, 0, 0, 0, 2, 3 };
                    //byte[] reg2 = { 2, (byte)160, 16, 225, 225, 0, 0, 0, 2, 1 };


                    if (comboBoxExGResolution.SelectedIndex == 0)
                    {
                        ReturnEnabledSensors = ReturnEnabledSensors | (int)ShimmerBluetooth.SensorBitmapShimmer3.SENSOR_EXG1_24BIT;

                    }
                    else
                    {
                        ReturnEnabledSensors = ReturnEnabledSensors | (int)ShimmerBluetooth.SensorBitmapShimmer3.SENSOR_EXG1_16BIT;

                    }
                    PConfiguration.PControlForm.ShimmerDevice.WriteEXGConfigurations(PConfiguration.ExgReg1UI, PConfiguration.ExgReg2UI);
                }
                if (checkBoxSensor17.Checked)
                {
                    if (comboBoxExGResolution.SelectedIndex == 0)
                    {
                        ReturnEnabledSensors = ReturnEnabledSensors | (int)ShimmerBluetooth.SensorBitmapShimmer3.SENSOR_EXG1_24BIT;
                        ReturnEnabledSensors = ReturnEnabledSensors | (int)ShimmerBluetooth.SensorBitmapShimmer3.SENSOR_EXG2_24BIT;
                    }
                    else
                    {
                        ReturnEnabledSensors = ReturnEnabledSensors | (int)ShimmerBluetooth.SensorBitmapShimmer3.SENSOR_EXG1_16BIT;
                        ReturnEnabledSensors = ReturnEnabledSensors | (int)ShimmerBluetooth.SensorBitmapShimmer3.SENSOR_EXG2_16BIT;
                    }
                    PConfiguration.PControlForm.ShimmerDevice.WriteEXGConfigurations(PConfiguration.ExgReg1UI, PConfiguration.ExgReg2UI);
                }

                if (checkBoxSensor23.Checked)
                {
                    if (comboBoxExGResolution.SelectedIndex == 0)
                    {
                        ReturnEnabledSensors = ReturnEnabledSensors | (int)ShimmerBluetooth.SensorBitmapShimmer3.SENSOR_EXG1_24BIT;
                        ReturnEnabledSensors = ReturnEnabledSensors | (int)ShimmerBluetooth.SensorBitmapShimmer3.SENSOR_EXG2_24BIT;
                    }
                    else
                    {
                        ReturnEnabledSensors = ReturnEnabledSensors | (int)ShimmerBluetooth.SensorBitmapShimmer3.SENSOR_EXG1_16BIT;
                        ReturnEnabledSensors = ReturnEnabledSensors | (int)ShimmerBluetooth.SensorBitmapShimmer3.SENSOR_EXG2_16BIT;
                    }
                    PConfiguration.PControlForm.ShimmerDevice.WriteEXGConfigurations(PConfiguration.ExgReg1UI, PConfiguration.ExgReg2UI);
                }

                if (checkBoxSensor19.Checked)
                {
                    ReturnEnabledSensors = ReturnEnabledSensors | (int)ShimmerBluetooth.SensorBitmapShimmer3.SENSOR_BRIDGE_AMP;
                }

                if (checkBoxSensor18.Checked)
                {
                    ReturnEnabledSensors = ReturnEnabledSensors | (int)ShimmerBluetooth.SensorBitmapShimmer3.SENSOR_EXG1_24BIT;
                }

                if (checkBoxSensor20.Checked)
                {
                    ReturnEnabledSensors = ReturnEnabledSensors | (int)ShimmerBluetooth.SensorBitmapShimmer3.SENSOR_EXG2_24BIT;
                }

                if (checkBoxSensor21.Checked)
                {
                    ReturnEnabledSensors = ReturnEnabledSensors | (int)ShimmerBluetooth.SensorBitmapShimmer3.SENSOR_EXG1_16BIT;
                }

                if (checkBoxSensor22.Checked)
                {
                    ReturnEnabledSensors = ReturnEnabledSensors | (int)ShimmerBluetooth.SensorBitmapShimmer3.SENSOR_EXG2_16BIT;
                }

                if (checkBoxSensor18.Checked || checkBoxSensor20.Checked || checkBoxSensor21.Checked || checkBoxSensor22.Checked) //CUSTOM EXG
                {
                    PConfiguration.PControlForm.ShimmerDevice.WriteEXGConfigurations(PConfiguration.ExgReg1UI, PConfiguration.ExgReg2UI);
                }

                if (checkBoxSensorHGAccel.Checked)
                {
                    ReturnEnabledSensors = ReturnEnabledSensors | (int)ShimmerBluetooth.SensorBitmapShimmer3.SENSOR_ACCEL_ALT;
                }
                if (checkBoxSensorAltMag.Checked)
                {
                    ReturnEnabledSensors = ReturnEnabledSensors | (int)ShimmerBluetooth.SensorBitmapShimmer3.SENSOR_MAG_ALT;
                }
            }
            else
            {
                if (checkBoxSensor1.Checked)
                {
                    ReturnEnabledSensors = ReturnEnabledSensors | (int)ShimmerBluetooth.SensorBitmapShimmer2.SENSOR_ACCEL;
                }
                if (checkBoxSensor2.Checked)
                {
                    ReturnEnabledSensors = ReturnEnabledSensors | (int)ShimmerBluetooth.SensorBitmapShimmer2.SENSOR_GYRO;
                }
                if (checkBoxSensor3.Checked)
                {
                    ReturnEnabledSensors = ReturnEnabledSensors | (int)ShimmerBluetooth.SensorBitmapShimmer2.SENSOR_MAG;
                }
                if (checkBoxSensor4.Checked)
                {
                    ReturnEnabledSensors = ReturnEnabledSensors | (int)ShimmerBluetooth.SensorBitmapShimmer3.SENSOR_VBATT;
                }
                if (checkBoxSensor5.Checked)
                {
                    ReturnEnabledSensors = ReturnEnabledSensors | (int)ShimmerBluetooth.SensorBitmapShimmer2.SENSOR_ECG;
                }
                if (checkBoxSensor6.Checked)
                {
                    ReturnEnabledSensors = ReturnEnabledSensors | (int)ShimmerBluetooth.SensorBitmapShimmer2.SENSOR_EMG;
                }
                if (checkBoxSensor7.Checked)
                {
                    ReturnEnabledSensors = ReturnEnabledSensors | (int)ShimmerBluetooth.SensorBitmapShimmer2.SENSOR_GSR;
                }
                if (checkBoxSensor8.Checked && !checkBoxVoltageMon.Checked)
                {
                    ReturnEnabledSensors = ReturnEnabledSensors | (int)ShimmerBluetooth.SensorBitmapShimmer2.SENSOR_EXP_BOARD_A0;
                }

                if (checkBoxSensor9.Checked && !checkBoxVoltageMon.Checked)
                {
                    ReturnEnabledSensors = ReturnEnabledSensors | (int)ShimmerBluetooth.SensorBitmapShimmer2.SENSOR_EXP_BOARD_A7;
                }

                if (checkBoxVoltageMon.Checked)
                {
                    ReturnEnabledSensors = ReturnEnabledSensors | (int)ShimmerBluetooth.SensorBitmapShimmer2.SENSOR_VBATT;
                }
                if (checkBoxSensor10.Checked)
                {
                    ReturnEnabledSensors = ReturnEnabledSensors | (int)ShimmerBluetooth.SensorBitmapShimmer2.SENSOR_STRAIN_GAUGE;
                }
                if (checkBoxSensor11.Checked)
                {
                    ReturnEnabledSensors = ReturnEnabledSensors | (int)ShimmerBluetooth.SensorBitmapShimmer2.SENSOR_HEART;
                }
            }

            PConfiguration.PControlForm.ShimmerDevice.WriteSensors(ReturnEnabledSensors);
            ReturnEnabledSensors = 0;
        }

        public int GetUIEnabledSensors()
        {
            int returnEnabledSensors = 0;
            if (PConfiguration.PControlForm.ShimmerDevice.GetShimmerVersion() == (int)ShimmerBluetooth.ShimmerVersion.SHIMMER3 || PConfiguration.PControlForm.ShimmerDevice.GetShimmerVersion() == (int)ShimmerBluetooth.ShimmerVersion.SHIMMER3R)
            {
                if (checkBoxSensor1.Checked)
                {
                    returnEnabledSensors = returnEnabledSensors | (int)ShimmerBluetooth.SensorBitmapShimmer3.SENSOR_A_ACCEL;
                }
                if (checkBoxSensor2.Checked)
                {
                    returnEnabledSensors = returnEnabledSensors | (int)ShimmerBluetooth.SensorBitmapShimmer3.SENSOR_D_ACCEL;
                }
                if (checkBoxSensor3.Checked)
                {
                    returnEnabledSensors = returnEnabledSensors | (int)ShimmerBluetooth.SensorBitmapShimmer3.SENSOR_GYRO;
                }
                if (checkBoxSensor4.Checked)
                {
                    returnEnabledSensors = returnEnabledSensors | (int)ShimmerBluetooth.SensorBitmapShimmer3.SENSOR_MAG;
                }
                if (checkBoxSensor5.Checked)
                {
                    returnEnabledSensors = returnEnabledSensors | (int)ShimmerBluetooth.SensorBitmapShimmer3.SENSOR_VBATT;
                }
                if (checkBoxSensor6.Checked)
                {
                    returnEnabledSensors = returnEnabledSensors | (int)ShimmerBluetooth.SensorBitmapShimmer3.SENSOR_EXT_A7;
                }
                if (checkBoxSensor7.Checked)
                {
                    returnEnabledSensors = returnEnabledSensors | (int)ShimmerBluetooth.SensorBitmapShimmer3.SENSOR_EXT_A6;
                }
                if (checkBoxSensor8.Checked)
                {
                    returnEnabledSensors = returnEnabledSensors | (int)ShimmerBluetooth.SensorBitmapShimmer3.SENSOR_EXT_A15;
                }
                if (checkBoxSensor9.Checked)
                {
                    returnEnabledSensors = returnEnabledSensors | (int)ShimmerBluetooth.SensorBitmapShimmer3.SENSOR_INT_A1;
                }
                if (checkBoxSensor10.Checked)
                {
                    returnEnabledSensors = returnEnabledSensors | (int)ShimmerBluetooth.SensorBitmapShimmer3.SENSOR_INT_A12;
                }
                if (checkBoxSensor11.Checked)
                {
                    returnEnabledSensors = returnEnabledSensors | (int)ShimmerBluetooth.SensorBitmapShimmer3.SENSOR_INT_A13;
                }
                if (checkBoxSensor12.Checked)
                {
                    returnEnabledSensors = returnEnabledSensors | (int)ShimmerBluetooth.SensorBitmapShimmer3.SENSOR_INT_A14;
                }
                if (checkBoxSensor13.Checked)
                {
                    returnEnabledSensors = returnEnabledSensors | (int)ShimmerBluetooth.SensorBitmapShimmer3.SENSOR_BMP180_PRESSURE;
                }
                if (checkBoxSensor14.Checked)
                {
                    returnEnabledSensors = returnEnabledSensors | (int)ShimmerBluetooth.SensorBitmapShimmer3.SENSOR_GSR;
                }
                if (checkBoxSensor15.Checked)
                {

                    if (comboBoxExGResolution.SelectedIndex == 0)
                    {
                        returnEnabledSensors = returnEnabledSensors | (int)ShimmerBluetooth.SensorBitmapShimmer3.SENSOR_EXG1_24BIT;
                        returnEnabledSensors = returnEnabledSensors | (int)ShimmerBluetooth.SensorBitmapShimmer3.SENSOR_EXG2_24BIT;
                    }
                    else
                    {
                        returnEnabledSensors = returnEnabledSensors | (int)ShimmerBluetooth.SensorBitmapShimmer3.SENSOR_EXG1_16BIT;
                        returnEnabledSensors = returnEnabledSensors | (int)ShimmerBluetooth.SensorBitmapShimmer3.SENSOR_EXG2_16BIT;
                    }
                    //check exg configuration

                    //update sampling rate and exg gain

                }
                if (checkBoxSensor16.Checked)
                {
                    if (comboBoxExGResolution.SelectedIndex == 0)
                    {
                        returnEnabledSensors = returnEnabledSensors | (int)ShimmerBluetooth.SensorBitmapShimmer3.SENSOR_EXG1_24BIT;

                    }
                    else
                    {
                        returnEnabledSensors = returnEnabledSensors | (int)ShimmerBluetooth.SensorBitmapShimmer3.SENSOR_EXG1_16BIT;

                    }

                }
                if (checkBoxSensor17.Checked)
                {
                    if (comboBoxExGResolution.SelectedIndex == 0)
                    {
                        returnEnabledSensors = returnEnabledSensors | (int)ShimmerBluetooth.SensorBitmapShimmer3.SENSOR_EXG1_24BIT;
                        returnEnabledSensors = returnEnabledSensors | (int)ShimmerBluetooth.SensorBitmapShimmer3.SENSOR_EXG2_24BIT;
                    }
                    else
                    {
                        returnEnabledSensors = returnEnabledSensors | (int)ShimmerBluetooth.SensorBitmapShimmer3.SENSOR_EXG1_16BIT;
                        returnEnabledSensors = returnEnabledSensors | (int)ShimmerBluetooth.SensorBitmapShimmer3.SENSOR_EXG2_16BIT;
                    }

                }
                if (checkBoxSensor23.Checked)
                {
                    if (comboBoxExGResolution.SelectedIndex == 0)
                    {
                        returnEnabledSensors = returnEnabledSensors | (int)ShimmerBluetooth.SensorBitmapShimmer3.SENSOR_EXG1_24BIT;
                        returnEnabledSensors = returnEnabledSensors | (int)ShimmerBluetooth.SensorBitmapShimmer3.SENSOR_EXG2_24BIT;
                    }
                    else
                    {
                        returnEnabledSensors = returnEnabledSensors | (int)ShimmerBluetooth.SensorBitmapShimmer3.SENSOR_EXG1_16BIT;
                        returnEnabledSensors = returnEnabledSensors | (int)ShimmerBluetooth.SensorBitmapShimmer3.SENSOR_EXG2_16BIT;
                    }

                }

                if (checkBoxSensor19.Checked)
                {
                    returnEnabledSensors = returnEnabledSensors | (int)ShimmerBluetooth.SensorBitmapShimmer3.SENSOR_BRIDGE_AMP;
                }

                if (checkBoxSensor18.Checked)
                {
                    returnEnabledSensors = returnEnabledSensors | (int)ShimmerBluetooth.SensorBitmapShimmer3.SENSOR_EXG1_24BIT;
                }

                if (checkBoxSensor20.Checked)
                {
                    returnEnabledSensors = returnEnabledSensors | (int)ShimmerBluetooth.SensorBitmapShimmer3.SENSOR_EXG2_24BIT;
                }

                if (checkBoxSensor21.Checked)
                {
                    returnEnabledSensors = returnEnabledSensors | (int)ShimmerBluetooth.SensorBitmapShimmer3.SENSOR_EXG1_16BIT;
                }

                if (checkBoxSensor22.Checked)
                {
                    returnEnabledSensors = returnEnabledSensors | (int)ShimmerBluetooth.SensorBitmapShimmer3.SENSOR_EXG2_16BIT;
                }
            }
            else
            {
                if (checkBoxSensor1.Checked)
                {
                    returnEnabledSensors = returnEnabledSensors | (int)ShimmerBluetooth.SensorBitmapShimmer2.SENSOR_ACCEL;
                }
                if (checkBoxSensor2.Checked)
                {
                    returnEnabledSensors = returnEnabledSensors | (int)ShimmerBluetooth.SensorBitmapShimmer2.SENSOR_GYRO;
                }
                if (checkBoxSensor3.Checked)
                {
                    returnEnabledSensors = returnEnabledSensors | (int)ShimmerBluetooth.SensorBitmapShimmer2.SENSOR_MAG;
                }
                if (checkBoxSensor4.Checked)
                {
                    returnEnabledSensors = returnEnabledSensors | (int)ShimmerBluetooth.SensorBitmapShimmer3.SENSOR_VBATT;
                }
                if (checkBoxSensor5.Checked)
                {
                    returnEnabledSensors = returnEnabledSensors | (int)ShimmerBluetooth.SensorBitmapShimmer2.SENSOR_ECG;
                }
                if (checkBoxSensor6.Checked)
                {
                    returnEnabledSensors = returnEnabledSensors | (int)ShimmerBluetooth.SensorBitmapShimmer2.SENSOR_EMG;
                }
                if (checkBoxSensor7.Checked)
                {
                    returnEnabledSensors = returnEnabledSensors | (int)ShimmerBluetooth.SensorBitmapShimmer2.SENSOR_GSR;
                }
                if (checkBoxSensor8.Checked && !checkBoxVoltageMon.Checked)
                {
                    returnEnabledSensors = returnEnabledSensors | (int)ShimmerBluetooth.SensorBitmapShimmer2.SENSOR_EXP_BOARD_A0;
                }

                if (checkBoxSensor9.Checked && !checkBoxVoltageMon.Checked)
                {
                    returnEnabledSensors = returnEnabledSensors | (int)ShimmerBluetooth.SensorBitmapShimmer2.SENSOR_EXP_BOARD_A7;
                }

                if (checkBoxVoltageMon.Checked)
                {
                    returnEnabledSensors = returnEnabledSensors | (int)ShimmerBluetooth.SensorBitmapShimmer2.SENSOR_VBATT;
                }
                if (checkBoxSensor10.Checked)
                {
                    returnEnabledSensors = returnEnabledSensors | (int)ShimmerBluetooth.SensorBitmapShimmer2.SENSOR_STRAIN_GAUGE;
                }
                if (checkBoxSensor11.Checked)
                {
                    returnEnabledSensors = returnEnabledSensors | (int)ShimmerBluetooth.SensorBitmapShimmer2.SENSOR_HEART;
                }
            }

            return returnEnabledSensors;

        }

        private void numericUpDownBeatsToAve_ValueChanged(object sender, EventArgs e)
        {

        }

        private void groupBoxSettings_Enter(object sender, EventArgs e)
        {

        }

        private void comboBoxSamplingRate_SelectedIndexChanged(object sender, EventArgs e)
        {
            if (PConfiguration.PControlForm.ShimmerDevice.GetShimmerVersion() == (int)ShimmerBluetooth.ShimmerVersion.SHIMMER3 || PConfiguration.PControlForm.ShimmerDevice.GetShimmerVersion() == (int)ShimmerBluetooth.ShimmerVersion.SHIMMER3R)
            {
                byte[] exg1Reg = PConfiguration.ExgReg1UI;
                byte[] exg2Reg = PConfiguration.ExgReg2UI;
                string sr = PConfiguration.userControlGeneralConfig1.comboBoxSamplingRate.SelectedItem.ToString();
                string subsr = sr.Substring(0, sr.Length - 2);
                double samplingRate = Double.Parse(subsr);

                int exgSR = getExGSamplingSetting(samplingRate);
               
                //adjust for sampling rate
                exg1Reg[0] = (byte)(((exg1Reg[0] >> 3) << 3) | exgSR);
                exg2Reg[0] = (byte)(((exg2Reg[0] >> 3) << 3) | exgSR);

                PConfiguration.ExgReg1UI = exg1Reg;
                PConfiguration.ExgReg2UI = exg2Reg;
            }
        }

        private void buttonApply_Click(object sender, EventArgs e)
        {
            buttonApply.Text = "Applying";
            buttonApply.Enabled = false;
            if (comboBoxBaudRate.SelectedIndex != PConfiguration.PControlForm.ShimmerDevice.GetBaudRate() && PConfiguration.PControlForm.ShimmerDevice.GetShimmerVersion() == (int)ShimmerBluetooth.ShimmerVersion.SHIMMER3)
            {
                BaudRateChangeFlag = true;
            }
            else
            {
                BaudRateChangeFlag = false;
            }
            ApplyConfigurationChanges();

            if (!BaudRateChangeFlag)
            {
                updateConfigurations();
            }
            else
            {
               firstConnectFlag = false; 
               this.ParentForm.Close();
            }
        }

        private void updateConfigurations()
        {
            if (PConfiguration.PControlForm.ShimmerDevice.GetFirmwareIdentifier() == 3)
            {
                PConfiguration.PControlForm.ShimmerDevice.SetConfigTime(Convert.ToInt32(PConfiguration.PControlForm.ShimmerDevice.SystemTime2Config()));
                PConfiguration.PControlForm.ShimmerDevice.WriteConfigTime();
                //PConfiguration.PControlForm.ShimmerDevice.WriteSdConfigFile();
            }

            EnableButtons(ShimmerBluetooth.SHIMMER_STATE_CONNECTED);

            if (PConfiguration.PControlForm.ShimmerDevice.Is3DOrientationEnabled())
            {
                PConfiguration.PControlForm.ToolStripMenuItemShow3DOrientation.Enabled = true;
            }
            else
            {
                PConfiguration.PControlForm.ToolStripMenuItemShow3DOrientation.Enabled = false;
            }
            BaudRateChangeFlag = false;
            PConfiguration.PControlForm.AppendTextBox("Configuration done.");
            MessageBox.Show("Configurations changed.", ShimmerLogAndStream.AppNameCapture,
                            MessageBoxButtons.OK, MessageBoxIcon.Information);
        }

        private void EnableButtons(int state)
        {
            if (InvokeRequired)
            {
                this.Invoke(new Action<int>(EnableButtons), new object[] { state });
                return;
            }

            if (state == (int)ShimmerBluetooth.SHIMMER_STATE_CONNECTED)
            {
                buttonApply.Text = "Apply";
                buttonApply.Enabled = true;
            }

            else
            {
                
            }
        }

        private void checkBoxVoltageMon_CheckedChanged_1(object sender, EventArgs e)
        {

            if (PConfiguration.PControlForm.ShimmerDevice.GetShimmerVersion() == (int)ShimmerBluetooth.ShimmerVersion.SHIMMER2R && checkBoxVoltageMon.Checked)
            {
                checkBoxSensor8.Text = "VSenseReg";
                checkBoxSensor9.Text = "VSenseBatt";
                checkBoxSensor8.Enabled = false;
                checkBoxSensor9.Enabled = false;
                checkBoxSensor8.Checked = true;
                checkBoxSensor9.Checked = true;
            }
            else
            {
                if (PConfiguration.PControlForm.ShimmerDevice.GetShimmerVersion() == (int)ShimmerBluetooth.ShimmerVersion.SHIMMER2R)
                {
                    checkBoxSensor8.Text = "Exp Board ADC0";
                    checkBoxSensor9.Text = "Exp Board ADC7";
                    checkBoxSensor8.Enabled = true;
                    checkBoxSensor9.Enabled = true;

                }
            }
        }

        private void labelAccelRange_Click(object sender, EventArgs e)
        {

        }

        private void comboBoxPPGAdcChannel_SelectedIndexChanged(object sender, EventArgs e)
        {
            if (comboBoxPPGAdcChannel.SelectedIndex == 0)
            {
                checkBoxSensor9.Checked = true;
            }
            else if (comboBoxPPGAdcChannel.SelectedIndex == 1)
            {
                checkBoxSensor10.Checked = true;
            }
            else if (comboBoxPPGAdcChannel.SelectedIndex == 2)
            {
                checkBoxSensor11.Checked = true;
            }
            else if (comboBoxPPGAdcChannel.SelectedIndex == 3)
            {
                checkBoxSensor12.Checked = true;
            }
        }

        private void checkBoxEnablePPGtoHR_CheckedChanged(object sender, EventArgs e)
        {
            if (checkBoxEnablePPGtoHR.Checked)
            {
                if (checkBoxSensor9.Checked || checkBoxSensor10.Checked || checkBoxSensor11.Checked || checkBoxSensor12.Checked)
                {
                    checkBoxEnablePPGtoHR.Checked = true;
                }
                if (!checkBoxSensor9.Checked && !checkBoxSensor10.Checked && !checkBoxSensor11.Checked && !checkBoxSensor12.Checked)
                {
                    MessageBox.Show("Please enable either Sensor Int A1/A12/A13/A14", Control.ApplicationName,
                               MessageBoxButtons.OK, MessageBoxIcon.Warning);
                }
            }
        }

        private void checkBoxSensor9_CheckedChanged(object sender, EventArgs e)
        {
            if (PConfiguration.PControlForm.ShimmerDevice.GetShimmerVersion() == (int)ShimmerBluetooth.ShimmerVersion.SHIMMER3 || PConfiguration.PControlForm.ShimmerDevice.GetShimmerVersion() == (int)ShimmerBluetooth.ShimmerVersion.SHIMMER3R)
            {
                if (checkBoxSensor9.Checked)
                {
                    comboBoxPPGAdcChannel.SelectedIndex = 0;
                }
                else
                {
                    if (checkBoxSensor10.Checked)
                    {
                        comboBoxPPGAdcChannel.SelectedIndex = 1;
                    }
                    else if (checkBoxSensor11.Checked)
                    {
                        comboBoxPPGAdcChannel.SelectedIndex = 2;
                    }
                    else if (checkBoxSensor12.Checked)
                    {
                        comboBoxPPGAdcChannel.SelectedIndex = 3;
                    }

                    if (!checkBoxSensor9.Checked && !checkBoxSensor10.Checked && !checkBoxSensor11.Checked && !checkBoxSensor12.Checked)
                    {
                        checkBoxEnablePPGtoHR.Checked = false;
                    }
                }
            }
        }

        private void checkBoxSensor10_CheckedChanged(object sender, EventArgs e)
        {
            if (PConfiguration.PControlForm.ShimmerDevice.GetShimmerVersion() == (int)ShimmerBluetooth.ShimmerVersion.SHIMMER3 || PConfiguration.PControlForm.ShimmerDevice.GetShimmerVersion() == (int)ShimmerBluetooth.ShimmerVersion.SHIMMER3R)
            {
                if (checkBoxSensor10.Checked)
                {
                    comboBoxPPGAdcChannel.SelectedIndex = 1;
                }
                else
                {
                    if (checkBoxSensor9.Checked)
                    {
                        comboBoxPPGAdcChannel.SelectedIndex = 0;
                    }
                    else if (checkBoxSensor11.Checked)
                    {
                        comboBoxPPGAdcChannel.SelectedIndex = 2;
                    }
                    else if (checkBoxSensor12.Checked)
                    {
                        comboBoxPPGAdcChannel.SelectedIndex = 3;
                    }
                    if (!checkBoxSensor9.Checked && !checkBoxSensor10.Checked && !checkBoxSensor11.Checked && !checkBoxSensor12.Checked)
                    {
                        checkBoxEnablePPGtoHR.Checked = false;
                    }
                }
            }
        }

        private void checkBoxSensor11_CheckedChanged(object sender, EventArgs e)
        {
            if (PConfiguration.PControlForm.ShimmerDevice.GetShimmerVersion() == (int)ShimmerBluetooth.ShimmerVersion.SHIMMER3 || PConfiguration.PControlForm.ShimmerDevice.GetShimmerVersion() == (int)ShimmerBluetooth.ShimmerVersion.SHIMMER3R)
            {
                if (checkBoxSensor11.Checked)
                {
                    comboBoxPPGAdcChannel.SelectedIndex = 2;
                }
                else
                {
                    if (checkBoxSensor10.Checked)
                    {
                        comboBoxPPGAdcChannel.SelectedIndex = 1;
                    }
                    else if (checkBoxSensor9.Checked)
                    {
                        comboBoxPPGAdcChannel.SelectedIndex = 0;
                    }
                    else if (checkBoxSensor12.Checked)
                    {
                        comboBoxPPGAdcChannel.SelectedIndex = 3;
                    }
                    if (!checkBoxSensor9.Checked && !checkBoxSensor10.Checked && !checkBoxSensor11.Checked && !checkBoxSensor12.Checked)
                    {
                        checkBoxEnablePPGtoHR.Checked = false;
                    }
                }
            }
        }

        private void checkBoxSensor12_CheckedChanged(object sender, EventArgs e)
        {
            if (PConfiguration.PControlForm.ShimmerDevice.GetShimmerVersion() == (int)ShimmerBluetooth.ShimmerVersion.SHIMMER3 || PConfiguration.PControlForm.ShimmerDevice.GetShimmerVersion() == (int)ShimmerBluetooth.ShimmerVersion.SHIMMER3R)
            {
                if (checkBoxSensor12.Checked)
                {
                    comboBoxPPGAdcChannel.SelectedIndex = 3;
                }
                else
                {
                    if (checkBoxSensor10.Checked)
                    {
                        comboBoxPPGAdcChannel.SelectedIndex = 1;
                    }
                    else if (checkBoxSensor11.Checked)
                    {
                        comboBoxPPGAdcChannel.SelectedIndex = 2;
                    }
                    else if (checkBoxSensor9.Checked)
                    {
                        comboBoxPPGAdcChannel.SelectedIndex = 0;
                    }
                    if (!checkBoxSensor9.Checked && !checkBoxSensor10.Checked && !checkBoxSensor11.Checked && !checkBoxSensor12.Checked)
                    {
                        checkBoxEnablePPGtoHR.Checked = false;
                    }
                }
            }
        }

        private void checkBoxEnableECGtoHR_CheckedChanged(object sender, EventArgs e)
        {
            /*
            if (!PConfiguration.PControlForm.ShimmerDevice.IsDefaultECGConfigurationEnabled())
            {
                MessageBox.Show("Please set exg configuration to ECG", Control.ApplicationName,
                               MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
            */
        }

        private void checkBoxIntExpPower_CheckedChanged(object sender, EventArgs e)
        {
            if ((checkBoxSensor9.Checked == false) && (checkBoxSensor10.Checked == false) && (checkBoxSensor11.Checked == false) && (checkBoxSensor12.Checked == false) && (checkBoxIntExpPower.Checked == true))
            {
                MessageBox.Show("Internal Exp Power is enabled but no internal ADC has been enabled. Disable Internal Exp Power to conserve battery.", Control.ApplicationName,
                               MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
            else
            {
                if (checkBoxIntExpPower.Checked)
                {
                    groupBoxPPGtoHR.Enabled = true;
                }
                else
                {
                    groupBoxPPGtoHR.Enabled = false;
                    checkBoxEnablePPGtoHR.Checked = false;
                }
            }
        }

        private void checkBoxSensor14_CheckedChanged(object sender, EventArgs e)
        {
            if (PConfiguration.PControlForm.ShimmerDevice.GetShimmerVersion() == (int)ShimmerBluetooth.ShimmerVersion.SHIMMER3 || PConfiguration.PControlForm.ShimmerDevice.GetShimmerVersion() == (int)ShimmerBluetooth.ShimmerVersion.SHIMMER3R)
            {
                if (checkBoxSensor14.Checked)
                {
                    comboBoxGSRRange.Enabled = true;
                }
                else
                {
                    comboBoxGSRRange.Enabled = false;
                }
            }
        }

        private void checkBoxSensor15_CheckedChanged(object sender, EventArgs e)
        {
            if (PConfiguration.PControlForm.ShimmerDevice.GetShimmerVersion() == (int)ShimmerBluetooth.ShimmerVersion.SHIMMER3 || PConfiguration.PControlForm.ShimmerDevice.GetShimmerVersion() == (int)ShimmerBluetooth.ShimmerVersion.SHIMMER3R)
            {
                if (checkBoxSensor15.Checked)
                {
                    checkBoxSensor12.Checked = false;
                    checkBoxSensor9.Checked = false;
                    checkBoxSensor10.Checked = false;
                    checkBoxSensor11.Checked = false;
                    checkBoxSensor14.Checked = false;
                    checkBoxSensor16.Checked = false;
                    checkBoxSensor17.Checked = false;
                    checkBoxSensor23.Checked = false;
                    checkBoxSensor19.Checked = false;
                    groupBoxPPGtoHR.Enabled = false;
                    checkBoxEnablePPGtoHR.Checked = false;
                    groupBoxECGtoHR.Enabled = true;
                    //Enable ExG tab
                    PConfiguration.tabControl1.TabPages[1].Enabled = true;

                    checkBoxSensor18.Visible = false;
                    checkBoxSensor18.Enabled = false;
                    checkBoxSensor18.Checked = false;
                    checkBoxSensor20.Visible = false;
                    checkBoxSensor20.Enabled = false;
                    checkBoxSensor20.Checked = false;
                    checkBoxSensor21.Visible = false;
                    checkBoxSensor21.Enabled = false;
                    checkBoxSensor21.Checked = false;
                    checkBoxSensor22.Visible = false;
                    checkBoxSensor22.Enabled = false;
                    checkBoxSensor22.Checked = false;

                    byte[] exg1Reg = { 2, 160, 16, 64, 64, 45, 0, 0, 2, 3 };
                    byte[] exg2Reg = { 2, 160, 16, 64, 71, 0, 0, 0, 2, 1 };
                    string sr = PConfiguration.userControlGeneralConfig1.comboBoxSamplingRate.SelectedItem.ToString();
                    string subsr = sr.Substring(0, sr.Length - 2);
                    double samplingRate = Double.Parse(subsr);

                    int exgSR = getExGSamplingSetting(samplingRate);

                    //adjust for sampling rate
                    exg1Reg[0] = (byte)(((exg1Reg[0] >> 3) << 3) | exgSR);
                    exg2Reg[0] = (byte)(((exg2Reg[0] >> 3) << 3) | exgSR);

                    if (comboBoxExgGain.SelectedIndex != 7) // if not custom, adjust accordingly otherwise ignore
                    {
                        int gain = (int)Double.Parse(PConfiguration.userControlGeneralConfig1.comboBoxExgGain.SelectedItem.ToString());
                        int gainSetting = ConvertEXGGainValuetoSetting(gain);
                        byte byte4exg1 = (byte)((exg1Reg[3] & 143) | (gainSetting << 4));
                        byte byte5exg1 = (byte)((exg1Reg[4] & 143) | (gainSetting << 4));
                        byte byte4exg2 = (byte)((exg2Reg[3] & 143) | (gainSetting << 4));
                        byte byte5exg2 = (byte)((exg2Reg[4] & 143) | (gainSetting << 4));
                        exg1Reg[3] = byte4exg1;
                        exg1Reg[4] = byte5exg1;
                        exg2Reg[3] = byte4exg2;
                        exg2Reg[4] = byte5exg2;
                    }

                    checkBoxSensor16.Checked = false;
                    checkBoxSensor17.Checked = false;
                    checkBoxSensor23.Checked = false;

                    comboBoxExGResolution.Enabled = true;
                    comboBoxExgGain.Enabled = true;

                    PConfiguration.ExgReg1UI = exg1Reg;
                    PConfiguration.ExgReg2UI = exg2Reg;
                }
                else
                {
                    comboBoxExGResolution.Enabled = false;
                    comboBoxExgGain.Enabled = false;
                    groupBoxECGtoHR.Enabled = false;
                    checkBoxEnableECGtoHR.Checked = false;
                    if (!checkBoxSensor16.Checked && !checkBoxSensor17.Checked && !checkBoxSensor23.Checked)
                    {
                        PConfiguration.tabControl1.TabPages[1].Enabled = false;
                    }
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

        private void comboBoxExGResolution_SelectedIndexChanged(object sender, EventArgs e)
        {

        }

        private void comboBoxExgGain_SelectedIndexChanged(object sender, EventArgs e)
        {
            byte[] exg1Reg = PConfiguration.ExgReg1UI;
            byte[] exg2Reg = PConfiguration.ExgReg2UI;

            if (comboBoxExgGain.SelectedIndex != 7) // if not custom, adjust accordingly otherwise ignore
            {
                int gain = (int)Double.Parse(PConfiguration.userControlGeneralConfig1.comboBoxExgGain.SelectedItem.ToString());
                int gainSetting = ConvertEXGGainValuetoSetting(gain);
                byte byte4exg1 = (byte)((exg1Reg[3] & 143) | (gainSetting << 4));
                byte byte5exg1 = (byte)((exg1Reg[4] & 143) | (gainSetting << 4));
                byte byte4exg2 = (byte)((exg2Reg[3] & 143) | (gainSetting << 4));
                byte byte5exg2 = (byte)((exg2Reg[4] & 143) | (gainSetting << 4));
                exg1Reg[3] = byte4exg1;
                exg1Reg[4] = byte5exg1;
                exg2Reg[3] = byte4exg2;
                exg2Reg[4] = byte5exg2;
                PConfiguration.ExgReg1UI = exg1Reg;
                PConfiguration.ExgReg2UI = exg2Reg;
            }

        }


        private void checkBoxSensor16_CheckedChanged(object sender, EventArgs e)
        {

            if (PConfiguration.PControlForm.ShimmerDevice.GetShimmerVersion() == (int)ShimmerBluetooth.ShimmerVersion.SHIMMER3 || PConfiguration.PControlForm.ShimmerDevice.GetShimmerVersion() == (int)ShimmerBluetooth.ShimmerVersion.SHIMMER3R)
            {
                if (checkBoxSensor16.Checked)
                {
                    checkBoxSensor12.Checked = false;
                    checkBoxSensor9.Checked = false;
                    checkBoxSensor10.Checked = false;
                    checkBoxSensor11.Checked = false;
                    checkBoxSensor14.Checked = false;
                    checkBoxSensor15.Checked = false;
                    checkBoxSensor17.Checked = false;
                    checkBoxSensor23.Checked = false;
                    checkBoxSensor19.Checked = false;
                    groupBoxPPGtoHR.Enabled = false;
                    checkBoxEnablePPGtoHR.Checked = false;
                    groupBoxECGtoHR.Enabled = false;
                    checkBoxEnableECGtoHR.Checked = false;
                    //Enable ExG tab
                    PConfiguration.tabControl1.TabPages[1].Enabled = true;

                    checkBoxSensor18.Visible = false;
                    checkBoxSensor18.Enabled = false;
                    checkBoxSensor18.Checked = false;
                    checkBoxSensor20.Visible = false;
                    checkBoxSensor20.Enabled = false;
                    checkBoxSensor20.Checked = false;
                    checkBoxSensor21.Visible = false;
                    checkBoxSensor21.Enabled = false;
                    checkBoxSensor21.Checked = false;
                    checkBoxSensor22.Visible = false;
                    checkBoxSensor22.Enabled = false;
                    checkBoxSensor22.Checked = false;

                    byte[] exg1Reg = { 2, 160, 16, 105, 96, 0, 0, 0, 2, 3 };
                    byte[] exg2Reg = { 2, 160, 16, 129, 129, 0, 0, 0, 2, 1 };
                    string sr = PConfiguration.userControlGeneralConfig1.comboBoxSamplingRate.SelectedItem.ToString();
                    string subsr = sr.Substring(0, sr.Length - 2);
                    double samplingRate = Double.Parse(subsr);

                    int exgSR = getExGSamplingSetting(samplingRate);

                    //adjust for sampling rate
                    exg1Reg[0] = (byte)(((exg1Reg[0] >> 3) << 3) | exgSR);
                    exg2Reg[0] = (byte)(((exg2Reg[0] >> 3) << 3) | exgSR);

                    if (comboBoxExgGain.SelectedIndex != 7) // if not custom, adjust accordingly otherwise ignore
                    {
                        int gain = (int)Double.Parse(PConfiguration.userControlGeneralConfig1.comboBoxExgGain.SelectedItem.ToString());
                        int gainSetting = ConvertEXGGainValuetoSetting(gain);
                        byte byte4exg1 = (byte)((exg1Reg[3] & 143) | (gainSetting << 4)); 
                        byte byte5exg1 = (byte)((exg1Reg[4] & 143) | (gainSetting << 4));
                        byte byte4exg2 = (byte)((exg2Reg[3] & 143) | (gainSetting << 4));
                        byte byte5exg2 = (byte)((exg2Reg[4] & 143) | (gainSetting << 4));
                        exg1Reg[3] = byte4exg1;
                        exg1Reg[4] = byte5exg1;
                        exg2Reg[3] = byte4exg2;
                        exg2Reg[4] = byte5exg2;
                    }

                    checkBoxSensor15.Checked = false;
                    checkBoxSensor17.Checked = false;
                    checkBoxSensor23.Checked = false;
                    comboBoxExGResolution.Enabled = true;
                    comboBoxExgGain.Enabled = true;

                    PConfiguration.ExgReg1UI = exg1Reg;
                    PConfiguration.ExgReg2UI = exg2Reg;

                }
                else
                {
                    comboBoxExGResolution.Enabled = false;
                    comboBoxExgGain.Enabled = false;
                    if (!checkBoxSensor15.Checked && !checkBoxSensor17.Checked && !checkBoxSensor23.Checked)
                    {
                        PConfiguration.tabControl1.TabPages[1].Enabled = false;
                    }
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

        private void checkBoxSensor17_CheckedChanged(object sender, EventArgs e)
        {

            if (PConfiguration.PControlForm.ShimmerDevice.GetShimmerVersion() == (int)ShimmerBluetooth.ShimmerVersion.SHIMMER3 || PConfiguration.PControlForm.ShimmerDevice.GetShimmerVersion() == (int)ShimmerBluetooth.ShimmerVersion.SHIMMER3R)
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
                    checkBoxSensor23.Checked = false;
                    checkBoxSensor19.Checked = false;
                    groupBoxPPGtoHR.Enabled = false;
                    checkBoxEnablePPGtoHR.Checked = false;
                    groupBoxECGtoHR.Enabled = false;
                    //Enable ExG tab
                    PConfiguration.tabControl1.TabPages[1].Enabled = true;

                    checkBoxSensor18.Visible = false;
                    checkBoxSensor18.Enabled = false;
                    checkBoxSensor18.Checked = false;
                    checkBoxSensor20.Visible = false;
                    checkBoxSensor20.Enabled = false;
                    checkBoxSensor20.Checked = false;
                    checkBoxSensor21.Visible = false;
                    checkBoxSensor21.Enabled = false;
                    checkBoxSensor21.Checked = false;
                    checkBoxSensor22.Visible = false;
                    checkBoxSensor22.Enabled = false;
                    checkBoxSensor22.Checked = false;


                    byte[] exg1Reg = { 2, 163, 16, 5, 5, 0, 0, 0, 2, 1 };
                    byte[] exg2Reg = { 2, 163, 16, 5, 5, 0, 0, 0, 2, 1 };
                    string sr = PConfiguration.userControlGeneralConfig1.comboBoxSamplingRate.SelectedItem.ToString();
                    string subsr = sr.Substring(0, sr.Length - 2);
                    double samplingRate = Double.Parse(subsr);

                    int exgSR = getExGSamplingSetting(samplingRate);

                    //adjust for sampling rate
                    exg1Reg[0] = (byte)(((exg1Reg[0] >> 3) << 3) | exgSR);
                    exg2Reg[0] = (byte)(((exg2Reg[0] >> 3) << 3) | exgSR);

                    if (comboBoxExgGain.SelectedIndex != 7) // if not custom, adjust according to comboboxexggain
                    {
                        int gain = (int)Double.Parse(PConfiguration.userControlGeneralConfig1.comboBoxExgGain.SelectedItem.ToString());
                        int gainSetting = ConvertEXGGainValuetoSetting(gain);
                        byte byte4exg1 = (byte)((exg1Reg[3] & 143) | (gainSetting << 4));
                        byte byte5exg1 = (byte)((exg1Reg[4] & 143) | (gainSetting << 4));
                        byte byte4exg2 = (byte)((exg2Reg[3] & 143) | (gainSetting << 4));
                        byte byte5exg2 = (byte)((exg2Reg[4] & 143) | (gainSetting << 4));
                        exg1Reg[3] = byte4exg1;
                        exg1Reg[4] = byte5exg1;
                        exg2Reg[3] = byte4exg2;
                        exg2Reg[4] = byte5exg2;
                    }

                    checkBoxSensor15.Checked = false;
                    checkBoxSensor16.Checked = false;
                    comboBoxExGResolution.Enabled = true;
                    comboBoxExgGain.Enabled = true;
                     
                    PConfiguration.ExgReg1UI = exg1Reg;
                    PConfiguration.ExgReg2UI = exg2Reg;

                }
                else
                {
                    comboBoxExGResolution.Enabled = false;
                    comboBoxExgGain.Enabled = false;
                    groupBoxECGtoHR.Enabled = false;
                    checkBoxEnableECGtoHR.Checked = false;
                    if (!checkBoxSensor15.Checked && !checkBoxSensor16.Checked && !checkBoxSensor23.Checked)
                    {
                        PConfiguration.tabControl1.TabPages[1].Enabled = false;
                    }
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
        private void checkBoxSensor24_CheckedChanged(object sender, EventArgs e)
        {

        }
        private void checkBoxSensor25_CheckedChanged(object sender, EventArgs e)
        {

        }
        private void checkBoxSensor23_CheckedChanged(object sender, EventArgs e)
        {

            if (PConfiguration.PControlForm.ShimmerDevice.GetShimmerVersion() == (int)ShimmerBluetooth.ShimmerVersion.SHIMMER3 || PConfiguration.PControlForm.ShimmerDevice.GetShimmerVersion() == (int)ShimmerBluetooth.ShimmerVersion.SHIMMER3R)
            {
                if (checkBoxSensor23.Checked)
                {

                    checkBoxSensor12.Checked = false;
                    checkBoxSensor9.Checked = false;
                    checkBoxSensor10.Checked = false;
                    checkBoxSensor11.Checked = false;
                    checkBoxSensor14.Checked = false;
                    checkBoxSensor15.Checked = false;
                    checkBoxSensor16.Checked = false;
                    checkBoxSensor17.Checked = false;
                    checkBoxSensor19.Checked = false;
                    groupBoxPPGtoHR.Enabled = false;
                    checkBoxEnablePPGtoHR.Checked = false;
                    groupBoxECGtoHR.Enabled = false;
                    //Enable ExG tab
                    PConfiguration.tabControl1.TabPages[1].Enabled = true;

                    checkBoxSensor18.Visible = false;
                    checkBoxSensor18.Enabled = false;
                    checkBoxSensor18.Checked = false;
                    checkBoxSensor20.Visible = false;
                    checkBoxSensor20.Enabled = false;
                    checkBoxSensor20.Checked = false;
                    checkBoxSensor21.Visible = false;
                    checkBoxSensor21.Enabled = false;
                    checkBoxSensor21.Checked = false;
                    checkBoxSensor22.Visible = false;
                    checkBoxSensor22.Enabled = false;
                    checkBoxSensor22.Checked = false;

                    byte[] exg1Reg = { 2, 168, 16, 64, 64, 32, 0, 0, 2, 1 };
                    byte[] exg2Reg = { 2, 160, 16, 64, 64, 0, 0, 0, 234, 1 };
                    string sr = PConfiguration.userControlGeneralConfig1.comboBoxSamplingRate.SelectedItem.ToString();
                    string subsr = sr.Substring(0, sr.Length - 2);
                    double samplingRate = Double.Parse(subsr);

                    int exgSR = getExGSamplingSetting(samplingRate);

                    //adjust for sampling rate
                    exg1Reg[0] = (byte)(((exg1Reg[0] >> 3) << 3) | exgSR);
                    exg2Reg[0] = (byte)(((exg2Reg[0] >> 3) << 3) | exgSR);

                    if (comboBoxExgGain.SelectedIndex != 7) // if not custom, adjust according to comboboxexggain
                    {
                        int gain = (int)Double.Parse(PConfiguration.userControlGeneralConfig1.comboBoxExgGain.SelectedItem.ToString());
                        int gainSetting = ConvertEXGGainValuetoSetting(gain);
                        byte byte4exg1 = (byte)((exg1Reg[3] & 143) | (gainSetting << 4));
                        byte byte5exg1 = (byte)((exg1Reg[4] & 143) | (gainSetting << 4));
                        byte byte4exg2 = (byte)((exg2Reg[3] & 143) | (gainSetting << 4));
                        byte byte5exg2 = (byte)((exg2Reg[4] & 143) | (gainSetting << 4));
                        exg1Reg[3] = byte4exg1;
                        exg1Reg[4] = byte5exg1;
                        exg2Reg[3] = byte4exg2;
                        exg2Reg[4] = byte5exg2;
                    }

                    checkBoxSensor15.Checked = false;
                    checkBoxSensor16.Checked = false;
                    checkBoxSensor17.Checked = false;
                    comboBoxExGResolution.Enabled = true;
                    comboBoxExgGain.Enabled = true;

                    PConfiguration.ExgReg1UI = exg1Reg;
                    PConfiguration.ExgReg2UI = exg2Reg;

                }
                else
                {
                    comboBoxExGResolution.Enabled = false;
                    comboBoxExgGain.Enabled = false;
                    groupBoxECGtoHR.Enabled = false;
                    checkBoxEnableECGtoHR.Checked = false;
                    if (!checkBoxSensor15.Checked && !checkBoxSensor16.Checked && !checkBoxSensor17.Checked)
                    {
                        PConfiguration.tabControl1.TabPages[1].Enabled = false;
                    }
                }
            }
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

        private void checkBoxSensor18_CheckedChanged(object sender, EventArgs e)
        {
            if (checkBoxSensor18.Checked)
            {

                checkBoxSensor21.Checked = false;
                checkBoxSensor22.Checked = false;


                byte[] exg1Reg = PConfiguration.ExgReg1UI;
                byte[] exg2Reg = PConfiguration.ExgReg2UI;
                string sr = PConfiguration.userControlGeneralConfig1.comboBoxSamplingRate.SelectedItem.ToString();
                string subsr = sr.Substring(0, sr.Length - 2);
                double samplingRate = Double.Parse(subsr);

                int exgSR = getExGSamplingSetting(samplingRate);

                //adjust for sampling rate
                exg1Reg[0] = (byte)(((exg1Reg[0] >> 3) << 3) | exgSR);
                exg2Reg[0] = (byte)(((exg2Reg[0] >> 3) << 3) | exgSR);

                if (comboBoxExgGain.SelectedIndex != 7) // if not custom, adjust accordingly otherwise ignore
                {
                    int gain = (int)Double.Parse(PConfiguration.userControlGeneralConfig1.comboBoxExgGain.SelectedItem.ToString());
                    int gainSetting = ConvertEXGGainValuetoSetting(gain);
                    byte byte4exg1 = (byte)((exg1Reg[3] & 143) | (gainSetting << 4));
                    byte byte5exg1 = (byte)((exg1Reg[4] & 143) | (gainSetting << 4));
                    byte byte4exg2 = (byte)((exg2Reg[3] & 143) | (gainSetting << 4));
                    byte byte5exg2 = (byte)((exg2Reg[4] & 143) | (gainSetting << 4));
                    exg1Reg[3] = byte4exg1;
                    exg1Reg[4] = byte5exg1;
                    exg2Reg[3] = byte4exg2;
                    exg2Reg[4] = byte5exg2;
                }
                else
                {
                    byte[] reg1 = PConfiguration.ExgReg1UI;
                    byte[] reg2 = PConfiguration.ExgReg2UI;
                    byte byte4exg1 = (byte)((exg1Reg[3] & 143) | (reg1[3] & 112));
                    byte byte5exg1 = (byte)((exg1Reg[4] & 143) | (reg1[4] & 112));
                    byte byte4exg2 = (byte)((exg2Reg[3] & 143) | (reg2[3] & 112));
                    byte byte5exg2 = (byte)((exg2Reg[4] & 143) | (reg2[4] & 112));
                    exg1Reg[3] = byte4exg1;
                    exg1Reg[4] = byte5exg1;
                    exg2Reg[3] = byte4exg2;
                    exg2Reg[4] = byte5exg2;
                }
                PConfiguration.ExgReg1UI = exg1Reg;
                PConfiguration.ExgReg2UI = exg2Reg;
            }
        }

        private void checkBoxSensor20_CheckedChanged(object sender, EventArgs e)
        {
            if (checkBoxSensor20.Checked)
            {

                checkBoxSensor21.Checked = false;
                checkBoxSensor22.Checked = false;

                byte[] exg1Reg = PConfiguration.ExgReg1UI;
                byte[] exg2Reg = PConfiguration.ExgReg2UI;
                string sr = PConfiguration.userControlGeneralConfig1.comboBoxSamplingRate.SelectedItem.ToString();
                string subsr = sr.Substring(0, sr.Length - 2);
                double samplingRate = Double.Parse(subsr);

                int exgSR = getExGSamplingSetting(samplingRate);

                //adjust for sampling rate
                exg1Reg[0] = (byte)(((exg1Reg[0] >> 3) << 3) | exgSR);
                exg2Reg[0] = (byte)(((exg2Reg[0] >> 3) << 3) | exgSR);

                if (comboBoxExgGain.SelectedIndex != 7) // if not custom, adjust accordingly otherwise ignore
                {
                    int gain = (int)Double.Parse(PConfiguration.userControlGeneralConfig1.comboBoxExgGain.SelectedItem.ToString());
                    int gainSetting = ConvertEXGGainValuetoSetting(gain);
                    byte byte4exg1 = (byte)((exg1Reg[3] & 143) | (gainSetting << 4));
                    byte byte5exg1 = (byte)((exg1Reg[4] & 143) | (gainSetting << 4));
                    byte byte4exg2 = (byte)((exg2Reg[3] & 143) | (gainSetting << 4));
                    byte byte5exg2 = (byte)((exg2Reg[4] & 143) | (gainSetting << 4));
                    exg1Reg[3] = byte4exg1;
                    exg1Reg[4] = byte5exg1;
                    exg2Reg[3] = byte4exg2;
                    exg2Reg[4] = byte5exg2;
                }
                else
                {
                    byte[] reg1 = PConfiguration.ExgReg1UI;
                    byte[] reg2 = PConfiguration.ExgReg2UI;
                    byte byte4exg1 = (byte)((exg1Reg[3] & 143) | (reg1[3] & 112));
                    byte byte5exg1 = (byte)((exg1Reg[4] & 143) | (reg1[4] & 112));
                    byte byte4exg2 = (byte)((exg2Reg[3] & 143) | (reg2[3] & 112));
                    byte byte5exg2 = (byte)((exg2Reg[4] & 143) | (reg2[4] & 112));
                    exg1Reg[3] = byte4exg1;
                    exg1Reg[4] = byte5exg1;
                    exg2Reg[3] = byte4exg2;
                    exg2Reg[4] = byte5exg2;
                }
                PConfiguration.ExgReg1UI = exg1Reg;
                PConfiguration.ExgReg2UI = exg2Reg;
            }
        }

        private void checkBoxSensor21_CheckedChanged(object sender, EventArgs e)
        {
            if (checkBoxSensor21.Checked)
            {

                checkBoxSensor18.Checked = false;
                checkBoxSensor20.Checked = false;

                byte[] exg1Reg = PConfiguration.ExgReg1UI;
                byte[] exg2Reg = PConfiguration.ExgReg2UI;
                string sr = PConfiguration.userControlGeneralConfig1.comboBoxSamplingRate.SelectedItem.ToString();
                string subsr = sr.Substring(0, sr.Length - 2);
                double samplingRate = Double.Parse(subsr);

                int exgSR = getExGSamplingSetting(samplingRate);

                //adjust for sampling rate
                exg1Reg[0] = (byte)(((exg1Reg[0] >> 3) << 3) | exgSR);
                exg2Reg[0] = (byte)(((exg2Reg[0] >> 3) << 3) | exgSR);

                if (comboBoxExgGain.SelectedIndex != 7) // if not custom, adjust accordingly otherwise ignore
                {
                    int gain = (int)Double.Parse(PConfiguration.userControlGeneralConfig1.comboBoxExgGain.SelectedItem.ToString());
                    int gainSetting = ConvertEXGGainValuetoSetting(gain);
                    byte byte4exg1 = (byte)((exg1Reg[3] & 143) | (gainSetting << 4));
                    byte byte5exg1 = (byte)((exg1Reg[4] & 143) | (gainSetting << 4));
                    byte byte4exg2 = (byte)((exg2Reg[3] & 143) | (gainSetting << 4));
                    byte byte5exg2 = (byte)((exg2Reg[4] & 143) | (gainSetting << 4));
                    exg1Reg[3] = byte4exg1;
                    exg1Reg[4] = byte5exg1;
                    exg2Reg[3] = byte4exg2;
                    exg2Reg[4] = byte5exg2;
                }
                else
                {
                    byte[] reg1 = PConfiguration.ExgReg1UI;
                    byte[] reg2 = PConfiguration.ExgReg2UI;
                    byte byte4exg1 = (byte)((exg1Reg[3] & 143) | (reg1[3] & 112));
                    byte byte5exg1 = (byte)((exg1Reg[4] & 143) | (reg1[4] & 112));
                    byte byte4exg2 = (byte)((exg2Reg[3] & 143) | (reg2[3] & 112));
                    byte byte5exg2 = (byte)((exg2Reg[4] & 143) | (reg2[4] & 112));
                    exg1Reg[3] = byte4exg1;
                    exg1Reg[4] = byte5exg1;
                    exg2Reg[3] = byte4exg2;
                    exg2Reg[4] = byte5exg2;
                }
                PConfiguration.ExgReg1UI = exg1Reg;
                PConfiguration.ExgReg2UI = exg2Reg;
            }
        }

        private void checkBoxSensor22_CheckedChanged(object sender, EventArgs e)
        {
            if (checkBoxSensor22.Checked)
            {

                checkBoxSensor18.Checked = false;
                checkBoxSensor20.Checked = false;

                byte[] exg1Reg = PConfiguration.ExgReg1UI;
                byte[] exg2Reg = PConfiguration.ExgReg2UI;
                string sr = PConfiguration.userControlGeneralConfig1.comboBoxSamplingRate.SelectedItem.ToString();
                string subsr = sr.Substring(0, sr.Length - 2);
                double samplingRate = Double.Parse(subsr);

                int exgSR = getExGSamplingSetting(samplingRate);

                //adjust for sampling rate
                exg1Reg[0] = (byte)(((exg1Reg[0] >> 3) << 3) | exgSR);
                exg2Reg[0] = (byte)(((exg2Reg[0] >> 3) << 3) | exgSR);

                if (comboBoxExgGain.SelectedIndex != 7) // if not custom, adjust accordingly otherwise ignore
                {
                    int gain = (int)Double.Parse(PConfiguration.userControlGeneralConfig1.comboBoxExgGain.SelectedItem.ToString());
                    int gainSetting = ConvertEXGGainValuetoSetting(gain);
                    byte byte4exg1 = (byte)((exg1Reg[3] & 143) | (gainSetting << 4));
                    byte byte5exg1 = (byte)((exg1Reg[4] & 143) | (gainSetting << 4));
                    byte byte4exg2 = (byte)((exg2Reg[3] & 143) | (gainSetting << 4));
                    byte byte5exg2 = (byte)((exg2Reg[4] & 143) | (gainSetting << 4));
                    exg1Reg[3] = byte4exg1;
                    exg1Reg[4] = byte5exg1;
                    exg2Reg[3] = byte4exg2;
                    exg2Reg[4] = byte5exg2;
                }
                PConfiguration.ExgReg1UI = exg1Reg;
                PConfiguration.ExgReg2UI = exg2Reg;
            }
        }

        /// <summary>
        /// This can be used to check the registers on the ExG Daughter board and determine whether it is using default ECG configurations
        /// </summary>
        /// <returns>Returns true if defaul ECG configurations is being used</returns>
        public bool IsDefaultExgTestSignalConfigurationEnabled(byte[] Exg1RegArray, byte[] Exg2RegArray)
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
        public bool IsDefaultECGConfigurationEnabled(byte[] Exg1RegArray, byte[] Exg2RegArray)
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
        public bool IsDefaultEMGConfigurationEnabled(byte[] Exg1RegArray, byte[] Exg2RegArray)
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
        public bool IsDefaultRespirationConfigurationEnabled(byte[] Exg1RegArray, byte[] Exg2RegArray)
        {
            bool isUsing = false;
            if (((Exg1RegArray[3] & 15) == 0) && ((Exg1RegArray[4] & 15) == 0) && ((Exg2RegArray[3] & 15) == 0) && ((Exg2RegArray[4] & 15) == 0))
            {
                isUsing = true;
            }

            return isUsing;
        }

        public int ConvertEXGGainValuetoSetting(int value)
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

        public int getExGSamplingSetting(double samplingRate)
        {
            if (samplingRate < 125)
            {
                return 0;
            }
            else if (samplingRate < 250)
            {
                return 1;
            }
            else if (samplingRate < 500)
            {
                return 2;
            }
            else if (samplingRate < 1000)
            {
                return 3;
            }
            else if (samplingRate < 2000)
            {
                return 4;
            }
            else if (samplingRate < 4000)
            {
                return 5;
            }
            else if (samplingRate < 8000)
            {
                return 6;
            }
            return -1;
        }

        //this just updates the UI and the UI click/checks shouldnt affect the settings
        public void ForceExGConfigurationUpdate(byte[] reg1, byte[] reg2)
        {
            checkBoxSensor18.Visible = false;
            checkBoxSensor18.Enabled = false;
            checkBoxSensor20.Visible = false;
            checkBoxSensor20.Enabled = false;
            checkBoxSensor21.Visible = false;
            checkBoxSensor21.Enabled = false;
            checkBoxSensor22.Visible = false;
            checkBoxSensor22.Enabled = false;
            long enabledSensors = PConfiguration.EnabledSensorsUI;
            if (IsDefaultExgTestSignalConfigurationEnabled(reg1, reg2) && (((enabledSensors & (int)ShimmerBluetooth.SensorBitmapShimmer3.SENSOR_EXG1_16BIT) > 0) && (((enabledSensors & (int)ShimmerBluetooth.SensorBitmapShimmer3.SENSOR_EXG2_16BIT) > 0)) || (((enabledSensors & (int)ShimmerBluetooth.SensorBitmapShimmer3.SENSOR_EXG1_24BIT) > 0) && ((enabledSensors & (int)ShimmerBluetooth.SensorBitmapShimmer3.SENSOR_EXG2_24BIT) > 0))))
            {
                checkBoxSensor17.Checked = true;
            }
            else if (IsDefaultEMGConfigurationEnabled(reg1, reg2) && (((enabledSensors & (int)ShimmerBluetooth.SensorBitmapShimmer3.SENSOR_EXG1_16BIT) > 0) && (((enabledSensors & (int)ShimmerBluetooth.SensorBitmapShimmer3.SENSOR_EXG2_16BIT) == 0)) || (((enabledSensors & (int)ShimmerBluetooth.SensorBitmapShimmer3.SENSOR_EXG1_24BIT) > 0) && ((enabledSensors & (int)ShimmerBluetooth.SensorBitmapShimmer3.SENSOR_EXG2_24BIT) == 0))))
            {
                checkBoxSensor16.Checked = true;
            }
            else if (IsDefaultECGConfigurationEnabled(reg1, reg2) && (((enabledSensors & (int)ShimmerBluetooth.SensorBitmapShimmer3.SENSOR_EXG1_16BIT) > 0) && (((enabledSensors & (int)ShimmerBluetooth.SensorBitmapShimmer3.SENSOR_EXG2_16BIT) > 0)) || (((enabledSensors & (int)ShimmerBluetooth.SensorBitmapShimmer3.SENSOR_EXG1_24BIT) > 0) && ((enabledSensors & (int)ShimmerBluetooth.SensorBitmapShimmer3.SENSOR_EXG2_24BIT) > 0))))
            {
                checkBoxSensor15.Checked = true;
            }
            else if (IsDefaultRespirationConfigurationEnabled(reg1, reg2) && (((enabledSensors & (int)ShimmerBluetooth.SensorBitmapShimmer3.SENSOR_EXG1_16BIT) > 0) && (((enabledSensors & (int)ShimmerBluetooth.SensorBitmapShimmer3.SENSOR_EXG2_16BIT) > 0)) || (((enabledSensors & (int)ShimmerBluetooth.SensorBitmapShimmer3.SENSOR_EXG1_24BIT) > 0) && ((enabledSensors & (int)ShimmerBluetooth.SensorBitmapShimmer3.SENSOR_EXG2_24BIT) > 0))))
            {
                checkBoxSensor23.Checked = true;
            }
            else
            {

            }

            int gainexg1ch1 = ConvertEXGGainSettingToValue((reg1[3] >> 4) & 7);
            int gainexg1ch2 = ConvertEXGGainSettingToValue((reg1[4] >> 4) & 7);
            int gainexg2ch1 = ConvertEXGGainSettingToValue((reg2[3] >> 4) & 7);
            int gainexg2ch2 = ConvertEXGGainSettingToValue((reg2[4] >> 4) & 7);

            this.comboBoxExgGain.SelectedIndexChanged -= new System.EventHandler(this.comboBoxExgGain_SelectedIndexChanged);
            if (PConfiguration.PControlForm.ShimmerDevice.GetShimmerVersion() == (int)ShimmerBluetooth.ShimmerVersion.SHIMMER3 || PConfiguration.PControlForm.ShimmerDevice.GetShimmerVersion() == (int)ShimmerBluetooth.ShimmerVersion.SHIMMER3R)
            {
                //if the gain is the same
                if (gainexg1ch1 == gainexg1ch2 && gainexg2ch1 == gainexg2ch2 && gainexg1ch1 == gainexg2ch1)
                {
                    if (gainexg1ch1 == 1)
                    {
                        comboBoxExgGain.SelectedIndex = 0;
                    }
                    else if (gainexg1ch1 == 2)
                    {
                        comboBoxExgGain.SelectedIndex = 1;
                    }
                    else if (gainexg1ch1 == 3)
                    {
                        comboBoxExgGain.SelectedIndex = 2;
                    }
                    else if (gainexg1ch1 == 4)
                    {
                        comboBoxExgGain.SelectedIndex = 3;
                    }
                    else if (gainexg1ch1 == 6)
                    {
                        comboBoxExgGain.SelectedIndex = 4;
                    }
                    else if (gainexg1ch1 == 8)
                    {
                        comboBoxExgGain.SelectedIndex = 5;
                    }
                    else if (gainexg1ch1 == 12)
                    {
                        comboBoxExgGain.SelectedIndex = 6;
                    }
                    if (comboBoxExgGain.Items.Count == 8)
                    {
                        comboBoxExgGain.Items.RemoveAt(7);
                    }
                }
                else
                {

                }

            }
            this.comboBoxExgGain.SelectedIndexChanged += new System.EventHandler(this.comboBoxExgGain_SelectedIndexChanged);
        }

        private void comboBoxBaudRate_SelectionChangeCommitted(object sender, EventArgs e)
        {

            if (comboBoxBaudRate.SelectedIndex != PConfiguration.PControlForm.ShimmerDevice.GetBaudRate())
            {
                BaudRateChangeFlag = true;
            }
            else
            {
                BaudRateChangeFlag = false;
            }

        }

        public void HandleEvent(object sender, EventArgs args) 
        {

            CustomEventArgs eventArgs = (CustomEventArgs)args;
            int indicator = eventArgs.getIndicator();
            switch (indicator)
            {
                case (int)ShimmerBluetooth.ShimmerIdentifier.MSG_IDENTIFIER_STATE_CHANGE:
                    
                    int state = (int)eventArgs.getObject();
                    if (state == (int)ShimmerBluetooth.SHIMMER_STATE_NONE)
                    {
                        if ((previousShimmerState == (int)ShimmerBluetooth.SHIMMER_STATE_CONNECTING) && lostConnectionFlag && BaudRateChangeFlag)
                        {
                            lostConnectionFlag = false;                    
                            MessageBox.Show("Connection Lost with Shimmer while changing baud rate", ShimmerLogAndStream.AppNameCapture,
                                            MessageBoxButtons.OK, MessageBoxIcon.Information);
                            System.Threading.Thread.Sleep(200);
                        }
                        if (BaudRateChangeFlag)
                        {
                            firstConnectFlag = true;
                        }
                    }
                    else if (state == (int)ShimmerBluetooth.SHIMMER_STATE_CONNECTING)
                    {
                        
                    }
                    else if (state == (int)ShimmerBluetooth.SHIMMER_STATE_CONNECTED)
                    {
                        lostConnectionFlag = true;
                    }
                    else // streaming
                    {
                        
                    }

                    previousShimmerState = state;

                break;

            }

        }
    }
}