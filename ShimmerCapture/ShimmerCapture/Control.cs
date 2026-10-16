using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Drawing;
using System.Linq;
using System.Text;
using System.Windows.Forms;
using System.Threading;
using System.IO;
using System.IO.Ports;
using ZedGraph;
using ShimmerAPI;
using ShimmerLibrary;
using static ShimmerLibrary.ECGToHRAdaptive;
//using ExceptionReporting;   // For ExceptionReporting

/*
 * Changes since 0.1.15
 * - Method for ExG lead off dectection by decoding ExG status bytes
 * - UI TextBoxes and Labels for ExG lead off detection
 * 
 * Changes 0.1.10
 * - Updated ppg channel selection
 * - various UI fixes to configuration page
 * - moved ecg to hr conversion to after filtering
 * 
 * Changes since rev 0.1.9
 * - write to sd when each tab page is updated
 * - won't send a stop command before disconnect
 * 
 * * Changes since rev 0.1.5
 * - updates according to Weibo's latest work
 * 
 * Changes since rev 0.1.4 (ShimmerCapture)
 * - Updated Shimmer.cs and ShimmerSDBT.cs and ShimmerBluetooth.cs 
 * 
 * Changes since rev 0.13
 * - 3D Orientation cube
 * - Logging moved to separate class
 * - PPG options in config window
 * - ECG-HR algorithm added not tested
 * - Bug with zed graph - show point values - fixed
 * - HPF for PPG - 0.5 corner frequency
 * - streamlining Shimmer classes - filtering, PPG-HR, ECG-HR
 * - changes to config window
 *      - general config, exg and sd log config all in same window - different tab for each
 * - using ShimmerSDBT object instead of Shimmer
 * - ExG -> ECG/EMG (Shimmer3)
 * - ExG reg bytes in Hex (just on GUI)
 * - ExG reg byte1 (both chips) being updated to match sampling rate (for default configs too). 
 * 
 * + changes in API - see API header
 * */

namespace ShimmerAPI
{
    public partial class Control : Form
    {
        public static readonly string ApplicationName = Application.ProductName.ToString().Replace("_", " ");
        //        private string versionNumber = Application.ProductVersion.ToString().Substring(0, Application.ProductVersion.ToString().LastIndexOf(".")).ToLower();
        private string versionNumber = Application.ProductVersion.ToString().ToLower();

        private delegate void ShowChannelTextBoxesCallback(int i);
        private delegate void ShowChannelLabelsCallback(List<String> s);
        private delegate void UpdateChannelsCallback(List<Double> d);
        private delegate void UpdateExGLeadOffDetectionCallback(double d1, double d2);
        private int ShowTBcount = 0;
        List<TextBox> ListofTextBox = new List<TextBox>();
        List<System.Windows.Forms.Label> ListofLabels = new List<System.Windows.Forms.Label>();
        List<String> ListofSignals = new List<String>();

        //public string status_text = "";
        private Configuration Configure;
        private Logging WriteToFile;
        private Orientation3D Orientation3DForm;
        private System.IO.Ports.SerialPort SerialPort = new SerialPort();
        private string ComPort;
        public ShimmerLogAndStream ShimmerDevice = null;
        //Plot
        private ZedGraph.ZedGraphControl ZedGraphControl2 = new ZedGraph.ZedGraphControl(); //These need to be defined here for Linux. Otherwise can't later be added
        private ZedGraph.ZedGraphControl ZedGraphControl3 = new ZedGraph.ZedGraphControl();
        private GraphPane MyPaneGraph1;
        private GraphPane MyPaneGraph2;
        private GraphPane MyPaneGraph3;
        public static int CountXAxisDataPoints = 0;
        private List<RollingPointPairList> DataListGraph1 = new List<RollingPointPairList>();
        private List<LineItem> CurvesListGraph1 = new List<LineItem>();
        private List<RollingPointPairList> DataListGraph2 = new List<RollingPointPairList>();
        private List<LineItem> CurvesListGraph2 = new List<LineItem>();
        private List<RollingPointPairList> DataListGraph3 = new List<RollingPointPairList>();
        private List<LineItem> CurvesListGraph3 = new List<LineItem>();
        private int NumberOfTracesCountGraph1 = 0;
        private int NumberOfTracesCountGraph2 = 0;
        private int NumberOfTracesCountGraph3 = 0;
        private static int maxNumberOfTracesToPlot = 60;
        public CheckBox[] CheckBoxArrayGroup1;
        public CheckBox[] CheckBoxArrayGroup2;
        public CheckBox[] CheckBoxArrayGroup3;
        public static bool FirstTime = true;
        private List<String> SelectedSignalNameGroup1 = new List<String>();
        private List<String> SelectedSignalNameGroup2 = new List<String>();
        private List<String> SelectedSignalNameGroup3 = new List<String>();
        private Random Rnd = new Random();
        private List<String> ShimmerIdSetup = new List<String>();
        private bool IsGraph2Visible = false;
        private bool IsGraph3Visible = false;
        private List<String> StreamingSignalNamesRaw = new List<String>();
        private List<String> StreamingSignalNamesCal = new List<String>();
        //Write to file
        private String Delimeter = ",";
        //Cube
        private double[,] SetOriMatrix = new double[,] { { 1, 0, 0 }, { 0, -1, 0 }, { 0, 0, -1 } };
        private Boolean SetOrientation = false;
        private Boolean Is3DCubeOpen = false;
        //PPG-HR
        private PPGToHRAlgorithm PPGtoHeartRateCalculation;
        private Boolean EnablePPGtoHRConversion = false;
        private int NumberOfHeartBeatsToAverage = 1;
        private int NumberOfHeartBeatsToAverageECG = 1;
        private int TrainingPeriodPPG = 10; //5 second buffer
        //ECG-HR
        private ECGToHRAdaptive ECGtoHRAdaptive;
        private Boolean EnableECGtoHRConversion = false;
        private int TrainingPeriodECG = 10; //5 second buffer
        //Filters
        Filter NQF_Exg1Ch1;
        Filter NQF_Exg1Ch2;
        Filter NQF_Exg2Ch1;
        Filter NQF_Exg2Ch2;
        Filter LPF_PPG;
        Filter HPF_PPG;
        Filter HPF_Exg1Ch1;
        Filter HPF_Exg1Ch2;
        Filter HPF_Exg2Ch1;
        Filter HPF_Exg2Ch2;
        Filter BSF_Exg1Ch1;
        Filter BSF_Exg1Ch2;
        Filter BSF_Exg2Ch1;
        Filter BSF_Exg2Ch2;
        public bool EnableHPF_0_05HZ = false;
        public bool EnableNQF = false;
        public bool EnableHPF_0_5HZ = false;
        public bool EnableHPF_5HZ = false;
        public bool EnableBSF_49_51HZ = false;
        public bool EnableBSF_59_61HZ = false;
        public String PPGSignalName = Shimmer3Configuration.SignalNames.INTERNAL_ADC_A13; //This is used to identify which signal to feed into the PPF to HR algorithm
        //ExG
        public String ECGSignalName = Shimmer3Configuration.SignalNames.ECG_LL_RA; //This is used to identify which signal to feed into the ECG to HR algorithm
        private int ExGLeadOffCounter = 0;
        private int ExGLeadOffCounterSize = 0;
        private Color[] TraceColours = new Color[60]
        {   // 30 Colours
            Color.Black, Color.DarkRed, Color.DarkGreen, Color.DarkBlue, Color.DarkOrange, Color.DarkMagenta,
            Color.Maroon, Color.ForestGreen, Color.MidnightBlue, Color.Indigo, Color.Teal, Color.Olive, Color.MediumBlue,
            Color.SaddleBrown, Color.Firebrick, Color.SeaGreen, Color.RoyalBlue, Color.Chocolate, Color.SlateBlue,
            Color.Crimson, Color.MediumVioletRed, Color.DarkCyan, Color.SteelBlue, Color.Brown, Color.Peru,
            Color.DarkSlateBlue, Color.DarkSlateGray, Color.DarkOliveGreen, Color.CadetBlue, Color.MediumSeaGreen,
            // Additional 30 Colours
            Color.Red, Color.Green, Color.Blue, Color.Orange, Color.Magenta, Color.MediumSlateBlue, Color.DarkTurquoise,
            Color.Tomato, Color.MediumOrchid, Color.MediumTurquoise, Color.CornflowerBlue, Color.Purple,
            Color.RosyBrown, Color.DarkKhaki, Color.MediumAquamarine, Color.LightSeaGreen, Color.LightSlateGray,
            Color.Goldenrod, Color.IndianRed, Color.DimGray, Color.MediumSpringGreen, Color.Sienna, Color.DeepSkyBlue,
            Color.LawnGreen, Color.HotPink, Color.DarkGoldenrod, Color.Chartreuse, Color.Navy, Color.DeepPink, Color.MediumTurquoise };
        int count = 0;

        public static bool usingLinux
        {
            get
            {
                int p = (int)Environment.OSVersion.Platform;
                return (p == 4) || (p == 6) || (p == 128);
            }
        }

        public Control()
        {
            InitializeComponent();
            // Catch unhandled exceptions
            Application.ThreadException += new ExceptionEventHandler().ApplicationThreadException;
            AppDomain.CurrentDomain.UnhandledException += new ExceptionEventHandler().CurrentDomainUnhandledException;
        }

        private void ControlForm_Load(object sender, EventArgs e)
        {
            buttonSetBlinkLED.Visible = false;
            checkBoxTSACheck.Visible = true;
            buttonStreamandLog.Visible = false;
            buttonStopStreamandLog.Visible = false;
            buttonReadDirectory.Visible = false;
            button1.Visible = false;
            button2.Visible = false;
            textBoxSawtoothValue.Visible = false;
            labelPRR.Visible = false;
            if (!usingLinux)
            {
                this.ShowInTaskbar = true;
            }
            this.Text = ApplicationName + " v" + versionNumber;
            tsStatusLabel.Text = "";
            ComPort = comboBoxComPorts.Text;

            buttonReload.Enabled = true;
            String[] names = SerialPort.GetPortNames();
            foreach (String s in names)
            {
                comboBoxComPorts.Items.Add(s);
            }
            comboBoxComPorts.AutoCompleteMode = AutoCompleteMode.SuggestAppend;
            comboBoxComPorts.AutoCompleteSource = AutoCompleteSource.ListItems;

            this.Resize += new System.EventHandler(this.FormResize);

            if (usingLinux)
            {
                ZedGraphControl1.Size = new System.Drawing.Size(this.Size.Width - 1150, ZedGraphControl1.Size.Height);
                label1.Visible = false;
                buttonAddGraph.Visible = false;
                label2.Visible = false;
                buttonRemoveGraph.Visible = false;
                //ZedGraphControl2.Size = new System.Drawing.Size(this.Size.Width - 1000, ZedGraphControl2.Size.Height);
                //ZedGraphControl3.Size = new System.Drawing.Size(this.Size.Width - 1000, ZedGraphControl3.Size.Height);
            }

            // Dynamically create and add checkboxes for each panel
            CheckBoxArrayGroup1 = CreateCheckBoxesForPanel(panel1, 1, CheckBoxArrayGroup1_CheckedChanged);
            CheckBoxArrayGroup2 = CreateCheckBoxesForPanel(panel2, 61, CheckBoxArrayGroup2_CheckedChanged);
            CheckBoxArrayGroup3 = CreateCheckBoxesForPanel(panel3, 121, CheckBoxArrayGroup3_CheckedChanged);

            InitializeGraphs();
            initializeExGLeadOff();
        }

        private CheckBox[] CreateCheckBoxesForPanel(Panel panel, int startingIndex, EventHandler eventHandler)
        {
            const int numCheckBoxes = 60;   // 60 Checkboxes per Panel
            const int rowHeight = 20;       // Height of row
            const int colXLeft = 1;         // Left column X
            const int colXRight = 231;      // Right column X
            const int startY = 9;           // Initial Y position
            CheckBox[] checkBoxes = new CheckBox[numCheckBoxes];

            for (int i = 0; i < numCheckBoxes; i++)
            {
                CheckBox cb = new CheckBox();
                cb.Visible = false;
                cb.AutoSize = true;

                // Calculate row & point coords
                int row = i / 2;
                int x = (i % 2 == 0) ? colXLeft : colXRight;
                int y = startY + row * (rowHeight);

                cb.Location = new Point(x, y);
                cb.CheckedChanged += eventHandler;
                checkBoxes[i] = cb;
                panel.Controls.Add(cb);
            }
            return checkBoxes;
        }

        public void ChangeStatusLabel(string text)
        {
            if (InvokeRequired)
            {
                this.Invoke(new Action<string>(ChangeStatusLabel), new object[] { text });
                return;
            }
            tsStatusLabel.Text = text;
        }

        public string GetStatusLabelText()
        {
            return tsStatusLabel.Text;
        }

        private void initializeExGLeadOff()
        {
            textBoxLeadOffStatus1.Text += "";
            textBoxLeadOffStatus2.Text += "";
            textBoxLeadOffStatus3.Text += "";
            textBoxLeadOffStatus4.Text += "";
            textBoxLeadOffStatus5.Text += "";
            labelExGLeadOffDetection.Visible = false;
            labelLeadOffStatus1.Visible = false;
            labelLeadOffStatus2.Visible = false;
            labelLeadOffStatus3.Visible = false;
            labelLeadOffStatus4.Visible = false;
            labelLeadOffStatus5.Visible = false;
            textBoxLeadOffStatus1.Visible = false;
            textBoxLeadOffStatus2.Visible = false;
            textBoxLeadOffStatus3.Visible = false;
            textBoxLeadOffStatus4.Visible = false;
            textBoxLeadOffStatus5.Visible = false;
        }

        private void InitializeGraphs()
        {
            if (!usingLinux)
            {
                this.ZedGraphControl2.BackColor = System.Drawing.SystemColors.AppWorkspace;
                this.ZedGraphControl2.BackgroundImageLayout = System.Windows.Forms.ImageLayout.None;
                this.ZedGraphControl2.Location = new System.Drawing.Point(31, 443);
                this.ZedGraphControl2.Name = "ZedGraphControl2";
                this.ZedGraphControl2.ScrollGrace = 0D;
                this.ZedGraphControl2.ScrollMaxX = 0D;
                this.ZedGraphControl2.ScrollMaxY = 0D;
                this.ZedGraphControl2.ScrollMaxY2 = 0D;
                this.ZedGraphControl2.ScrollMinX = 0D;
                this.ZedGraphControl2.ScrollMinY = 0D;
                this.ZedGraphControl2.ScrollMinY2 = 0D;
                this.ZedGraphControl2.Size = new System.Drawing.Size(490, 297);
                this.ZedGraphControl2.TabIndex = 45;
                this.ZedGraphControl2.Visible = false;
                this.Controls.Add(this.ZedGraphControl2);

                this.ZedGraphControl3.BackColor = System.Drawing.SystemColors.AppWorkspace;
                this.ZedGraphControl3.BackgroundImageLayout = System.Windows.Forms.ImageLayout.None;
                this.ZedGraphControl3.Location = new System.Drawing.Point(31, 756);
                this.ZedGraphControl3.Name = "ZedGraphControl3";
                this.ZedGraphControl3.ScrollGrace = 0D;
                this.ZedGraphControl3.ScrollMaxX = 0D;
                this.ZedGraphControl3.ScrollMaxY = 0D;
                this.ZedGraphControl3.ScrollMaxY2 = 0D;
                this.ZedGraphControl3.ScrollMinX = 0D;
                this.ZedGraphControl3.ScrollMinY = 0D;
                this.ZedGraphControl3.ScrollMinY2 = 0D;
                this.ZedGraphControl3.Size = new System.Drawing.Size(490, 297);
                this.ZedGraphControl3.TabIndex = 80;
                this.ZedGraphControl3.Visible = false;
                this.Controls.Add(this.ZedGraphControl3);
            }
            ZedGraphControl1.GraphPane.CurveList.Clear();
            MyPaneGraph1 = ZedGraphControl1.GraphPane;
            ZedGraphControl1.IsShowPointValues = false;
            ZedGraphControl1.IsShowCursorValues = false;

            ZedGraphControl2.GraphPane.CurveList.Clear();
            MyPaneGraph2 = ZedGraphControl2.GraphPane;
            ZedGraphControl2.IsShowPointValues = false;
            ZedGraphControl2.IsShowCursorValues = false;

            ZedGraphControl3.GraphPane.CurveList.Clear();
            MyPaneGraph3 = ZedGraphControl3.GraphPane;
            ZedGraphControl3.IsShowPointValues = false;
            ZedGraphControl3.IsShowCursorValues = false;

            MyPaneGraph1.XAxis.IsVisible = false;
            MyPaneGraph1.YAxis.Title.Text = "Amplitude";
            MyPaneGraph1.Title.IsVisible = false;
            MyPaneGraph1.Legend.Position = LegendPos.Float;
            MyPaneGraph1.Legend.Location = new Location(0f, 0f, CoordType.ChartFraction, AlignH.Left, AlignV.Top);
            MyPaneGraph1.YAxis.MajorGrid.IsZeroLine = false;

            MyPaneGraph2.XAxis.IsVisible = false;
            MyPaneGraph2.YAxis.Title.Text = "Amplitude";
            MyPaneGraph2.Title.IsVisible = false;
            MyPaneGraph2.Legend.Position = LegendPos.Float;
            MyPaneGraph2.Legend.Location = new Location(0f, 0f, CoordType.ChartFraction, AlignH.Left, AlignV.Top);
            MyPaneGraph2.YAxis.MajorGrid.IsZeroLine = false;

            MyPaneGraph3.XAxis.IsVisible = false;
            MyPaneGraph3.YAxis.Title.Text = "Amplitude";
            MyPaneGraph3.Title.IsVisible = false;
            MyPaneGraph3.Legend.Position = LegendPos.Float;
            MyPaneGraph3.Legend.Location = new Location(0f, 0f, CoordType.ChartFraction, AlignH.Left, AlignV.Top);
            MyPaneGraph3.YAxis.MajorGrid.IsZeroLine = false;
        }

        private void FormResize(object sender, EventArgs e)
        {
            int scale1 = 350;
            int scale2 = 145;
            int scale3 = 400;
            if (usingLinux)
            {
                System.Console.WriteLine("Using Linux");
                scale1 = 400;
                scale2 = 195;
                scale3 = 450;
            }

            System.Console.WriteLine("FORM RESIZE");
            /*
            for (int i = 0; i < 30; i++)
            {
                if (i % 2 == 0) //Checkboxes LHS
                {
                    CheckBoxArrayGroup1[i].Location = new System.Drawing.Point(this.Size.Width - scale1, CheckBoxArrayGroup1[i].Location.Y);
                    CheckBoxArrayGroup2[i].Location = new System.Drawing.Point(this.Size.Width - scale1, CheckBoxArrayGroup2[i].Location.Y);
                    CheckBoxArrayGroup3[i].Location = new System.Drawing.Point(this.Size.Width - scale1, CheckBoxArrayGroup3[i].Location.Y);
                }
                else //Checkboxes RHS
                {
                    CheckBoxArrayGroup1[i].Location = new System.Drawing.Point(this.Size.Width - scale2, CheckBoxArrayGroup1[i].Location.Y);
                    CheckBoxArrayGroup2[i].Location = new System.Drawing.Point(this.Size.Width - scale2, CheckBoxArrayGroup2[i].Location.Y);
                    CheckBoxArrayGroup3[i].Location = new System.Drawing.Point(this.Size.Width - scale2, CheckBoxArrayGroup3[i].Location.Y);
                }
            }
            
            ZedGraphControl1.Size = new System.Drawing.Size(this.Size.Width - scale3, ZedGraphControl1.Size.Height);
            ZedGraphControl2.Size = new System.Drawing.Size(this.Size.Width - scale3, ZedGraphControl2.Size.Height);
            ZedGraphControl3.Size = new System.Drawing.Size(this.Size.Width - scale3, ZedGraphControl3.Size.Height);*/
        }

        private void CheckBoxArrayGroup1_CheckedChanged(object sender, EventArgs e)
        {
            try
            {
                SelectedSignalNameGroup1.Clear();
                for (int i = 0; i < CheckBoxArrayGroup1.Length; i++)
                {
                    if (CheckBoxArrayGroup1[i].Checked && CheckBoxArrayGroup1[i].Enabled)
                    {
                        SelectedSignalNameGroup1.Add(CheckBoxArrayGroup1[i].Name);
                        //Create new textbox

                    }
                }
            }
            catch (ArgumentOutOfRangeException)
            {

            }
        }

        public void ShowChannelLabels(List<String> names)
        {
            if (this.buttonConnect.InvokeRequired)  // will be in the same thread as the controls to be added
            {
                ShowChannelLabelsCallback d = new ShowChannelLabelsCallback(ShowChannelLabels);
                this.Invoke(d, names);
            }
            else
            {
                for (int i = 0; i < names.Count; i++)
                {
                    System.Windows.Forms.Label lbl = new System.Windows.Forms.Label();
                    lbl.Text = names[i];
                    lbl.Size = new Size(250, 17);
                    lbl.Location = new Point(900, 150 + ((ListofLabels.Count - 1) * 22));
                    ListofLabels.Add(lbl);
                    this.Controls.Add(lbl);
                }
            }
        }


        public void ShowChannelTextBoxes(int count)
        {
            if (this.buttonConnect.InvokeRequired)  // will be in the same thread as the controls to be added
            {
                ShowChannelTextBoxesCallback d = new ShowChannelTextBoxesCallback(ShowChannelTextBoxes);
                this.Invoke(d, count);
            }
            else
            {
                for (int i = 0; i < count; i++)
                {
                    TextBox txtBX = new TextBox();
                    txtBX.Text = ListofTextBox.Count.ToString();
                    txtBX.Location = new Point(1150, 150 + ((ListofTextBox.Count - 1) * 22));
                    ListofTextBox.Add(txtBX);
                    this.Controls.Add(txtBX);
                }
            }
        }

        public void UpdateChannelTextBoxes(List<Double> listofdata)
        {
            if (this.buttonConnect.InvokeRequired)  // will be in the same thread as the controls to be added
            {
                UpdateChannelsCallback d = new UpdateChannelsCallback(UpdateChannelTextBoxes);
                this.Invoke(d, listofdata);
            }
            else
            {
                if (ListofTextBox.Count == listofdata.Count)
                {
                    for (int i = 0; i < listofdata.Count; i++)
                    {
                        ListofTextBox[i].Text = (Math.Truncate(listofdata[i] * 100) / 100).ToString();
                    }
                }
            }
        }

        public void updateExGLeadOffTextBoxes(double ExG1status, double ExG2status)
        {
            if (this.buttonConnect.InvokeRequired)  // will be in the same thread as the controls to be added
            {
                UpdateExGLeadOffDetectionCallback d = new UpdateExGLeadOffDetectionCallback(updateExGLeadOffTextBoxes);
                this.Invoke(d, ExG1status, ExG2status);
            }
            else
            {
                textBoxLeadOffStatus1.Text = ((((byte)ExG1status & (1 << 2)) == 0).ToString());
                textBoxLeadOffStatus2.Text = ((((byte)ExG1status & (1 << 3)) == 0).ToString());
                textBoxLeadOffStatus3.Text = ((((byte)ExG1status & (1 << 0)) == 0).ToString());
                textBoxLeadOffStatus4.Text = ((((byte)ExG2status & (1 << 2)) == 0).ToString());
                textBoxLeadOffStatus5.Text = ((((byte)ExG1status & (1 << 4)) == 0).ToString());
            }
        }

        private void RemoveAllTextBox()
        {
            foreach (TextBox tx in ListofTextBox)
            {
                this.Controls.Remove(tx);
            }
            foreach (System.Windows.Forms.Label l in ListofLabels)
            {
                this.Controls.Remove(l);
            }
            ListofTextBox.Clear();
            ListofSignals.Clear();
            ListofLabels.Clear();
            UserControlGeneralConfig.ExpansionBoard = "";

        }

        private void CheckBoxArrayGroup2_CheckedChanged(object sender, EventArgs e)
        {
            try
            {
                SelectedSignalNameGroup2.Clear();
                for (int i = 0; i < CheckBoxArrayGroup2.Length; i++)
                {
                    if (CheckBoxArrayGroup2[i].Checked && CheckBoxArrayGroup2[i].Enabled)
                    {
                        SelectedSignalNameGroup2.Add(CheckBoxArrayGroup2[i].Name);
                    }
                }
            }
            catch (ArgumentOutOfRangeException)
            {

            }
        }

        private void CheckBoxArrayGroup3_CheckedChanged(object sender, EventArgs e)
        {
            try
            {
                SelectedSignalNameGroup3.Clear();
                for (int i = 0; i < CheckBoxArrayGroup3.Length; i++)
                {
                    if (CheckBoxArrayGroup3[i].Checked && CheckBoxArrayGroup3[i].Enabled)
                    {
                        SelectedSignalNameGroup3.Add(CheckBoxArrayGroup3[i].Name);
                    }
                }
            }
            catch (ArgumentOutOfRangeException)
            {

            }
        }

        private void configureToolStripMenuItem_Click(object sender, EventArgs e)
        {
            Configure = new Configuration(this);
            if (Configure.ShowDialog(this) == DialogResult.OK)
            {
                foreach (CheckBox cb in CheckBoxArrayGroup1)
                {
                    cb.Checked = false;
                    cb.Visible = false;
                }

                foreach (CheckBox cb in CheckBoxArrayGroup2)
                {
                    cb.Checked = false;
                    cb.Visible = false;
                }

                foreach (CheckBox cb in CheckBoxArrayGroup3)
                {
                    cb.Checked = false;
                    cb.Visible = false;
                }

                if (ShimmerDevice.Is3DOrientationEnabled())
                {
                    ToolStripMenuItemShow3DOrientation.Enabled = true;
                }
                else
                {
                    ToolStripMenuItemShow3DOrientation.Enabled = false;
                }
            }
        }

        private void ToolStripMenuItemQuit_Click(object sender, EventArgs e)
        {
            if (WriteToFile != null)
            {
                WriteToFile.CloseFile();
                ToolStripMenuItemSaveToCSV.Checked = false;
            }

            if (ShimmerDevice != null)
            {
                if (ShimmerDevice.GetState() == (int)ShimmerBluetooth.SHIMMER_STATE_STREAMING)
                {
                    if (ShimmerDevice.GetFirmwareIdentifier() == 3)
                    {

                    }
                    else
                    {
                        ShimmerDevice.StopStreaming();
                    }
                }
                ShimmerDevice.Disconnect();
                if (Orientation3DForm != null)
                {
                    Orientation3DForm.Close();
                }
            }
            Application.Exit();
        }

        private void ToolStripMenuItemSaveToCSV_Click(object sender, EventArgs e)
        {
            if (ToolStripMenuItemSaveToCSV.Checked)
            {
                ToolStripMenuItemSaveToCSV.Checked = false;
                WriteToFile.CloseFile();
            }
            else
            {
                openDialog.CheckFileExists = false;
                if (openDialog.ShowDialog() == DialogResult.OK)
                {
                    WriteToFile = new Logging(openDialog.FileName, Delimeter);
                    ToolStripMenuItemSaveToCSV.Checked = true;
                }
            }
        }

        private void ToolStripMenuItemShow3DOrientation_Click(object sender, EventArgs e)
        {
            if (!ToolStripMenuItemShow3DOrientation.Checked)
            {   
                ToolStripMenuItemShow3DOrientation.Checked = true;
                Orientation3DForm = new Orientation3D();
                Orientation3DForm.setControl(this);
                Orientation3DForm.Show();
                if ((ShimmerDevice.GetEnabledSensors() & (int)ShimmerBluetooth.SensorBitmapShimmer3.SENSOR_D_ACCEL) > 0){
                    MessageBox.Show("Wide Range Accelerometer Being Used for Orientation Calculation, to use Low Noise disable Wide Range Accelerometer");
                } else if ((ShimmerDevice.GetEnabledSensors() & (int)ShimmerBluetooth.SensorBitmapShimmer3.SENSOR_A_ACCEL) > 0)
                {
                    MessageBox.Show("Low Noise Accelerometer Being Used for Orientation Calculation, to use Wide Range for Orientation Calculation Enable It");
                }
                Is3DCubeOpen = true;
            }
            else
            {
                ToolStripMenuItemShow3DOrientation.Checked = false;
                Orientation3DForm.Close();
                Is3DCubeOpen = false;
            }
        }

        public void SetupCheckboxesGroup1(string shimmerId, string[] namesRaw, string[] namesCal)
        {
            if (InvokeRequired)
            {
                this.Invoke(new Action<string, string[], string[]>(SetupCheckboxesGroup1), new object[] { shimmerId, namesRaw, namesCal });
                return;
            }

            foreach (CheckBox cb in CheckBoxArrayGroup1)
            {
                cb.Name = "";
            }

            int count = 0;
            for (int j = 0; j < namesRaw.Length; j++)
            {
                if (j >= CheckBoxArrayGroup1.Length || count >= CheckBoxArrayGroup1.Length || j >= namesRaw.Length)
                {
                    return;
                }
                CheckBoxArrayGroup1[count].Visible = true;
                CheckBoxArrayGroup1[count].Text = namesRaw[j];
                CheckBoxArrayGroup1[count].Name = namesRaw[j];
                int l = namesRaw[j].Length;
                String signalNameNoFormat = namesRaw[j].Substring(0, l - 4);
                String signalNameCalFormat = signalNameNoFormat + " CAL";
                //If same signal name with CAL format exists, add it to next checkbox. Otherwise skip next checkbox.
                if (namesCal.Contains(signalNameCalFormat))
                {
                    CheckBoxArrayGroup1[count + 1].Visible = true;
                    CheckBoxArrayGroup1[count + 1].Text = "CAL";
                    CheckBoxArrayGroup1[count + 1].Name = signalNameCalFormat;
                }
                count += 2;
            }
            for (int j = 0; j < namesCal.Length; j++)
            {
                if (j >= CheckBoxArrayGroup1.Length || count >= CheckBoxArrayGroup1.Length || j >= namesCal.Length)
                {
                    return;
                }
                //If CAL signal already added to checkbox, skip it. 
                bool skip = false;
                foreach (CheckBox cb in CheckBoxArrayGroup1)
                {
                    if (cb.Name.Equals(namesCal[j]))
                    {
                        skip = true;
                    }
                }
                if (!skip)
                {
                    CheckBoxArrayGroup1[count].Visible = true;
                    CheckBoxArrayGroup1[count].Text = namesCal[j];
                    CheckBoxArrayGroup1[count].Name = namesCal[j];
                    count++;
                }
            }
        }

        public void SetupCheckboxesGroup2(string shimmerId, string[] namesRaw, string[] namesCal)
        {
            if (InvokeRequired)
            {
                this.Invoke(new Action<string, string[], string[]>(SetupCheckboxesGroup2), new object[] { shimmerId, namesRaw, namesCal });
                return;
            }

            if (IsGraph2Visible)
            {
                foreach (CheckBox cb in CheckBoxArrayGroup2)
                {
                    cb.Name = "";
                }
                int count = 0;
                for (int j = 0; j < namesRaw.Length; j++)
                {
                    if (j >= CheckBoxArrayGroup2.Length || count >= CheckBoxArrayGroup2.Length || j >= namesRaw.Length)
                    {
                        return;
                    }
                    CheckBoxArrayGroup2[count].Visible = true;
                    CheckBoxArrayGroup2[count].Text = namesRaw[j];
                    CheckBoxArrayGroup2[count].Name = namesRaw[j];
                    int l = namesRaw[j].Length;
                    String signalNameNoFormat = namesRaw[j].Substring(0, l - 4);
                    String signalNameCalFormat = signalNameNoFormat + " CAL";
                    //If same signal name with CAL format exists, add it to next checkbox. Otherwise skip next checkbox.
                    if (namesCal.Contains(signalNameCalFormat))
                    {
                        CheckBoxArrayGroup2[count + 1].Visible = true;
                        CheckBoxArrayGroup2[count + 1].Text = "CAL";
                        CheckBoxArrayGroup2[count + 1].Name = signalNameCalFormat;
                    }
                    count += 2;
                }
                for (int j = 0; j < namesCal.Length; j++)
                {
                    if (j >= CheckBoxArrayGroup2.Length || count >= CheckBoxArrayGroup2.Length || j >= namesCal.Length)
                    {
                        return;
                    }
                    //If CAL signal already added to checkbox, skip it. 
                    bool skip = false;
                    foreach (CheckBox cb in CheckBoxArrayGroup2)
                    {
                        if (cb.Name.Equals(namesCal[j]))
                        {
                            skip = true;
                        }
                    }
                    if (!skip)
                    {
                        CheckBoxArrayGroup2[count].Visible = true;
                        CheckBoxArrayGroup2[count].Text = namesCal[j];
                        CheckBoxArrayGroup2[count].Name = namesCal[j];
                        count++;
                    }
                }
            }
        }

        public void SetupCheckboxesGroup3(string shimmerId, string[] namesRaw, string[] namesCal)
        {
            if (InvokeRequired)
            {
                this.Invoke(new Action<string, string[], string[]>(SetupCheckboxesGroup3), new object[] { shimmerId, namesRaw, namesCal });
                return;
            }

            if (IsGraph3Visible)
            {
                foreach (CheckBox cb in CheckBoxArrayGroup3)
                {
                    cb.Name = "";
                }
                int count = 0;
                for (int j = 0; j < namesRaw.Length; j++)
                {
                    if (j >= CheckBoxArrayGroup3.Length || count >= CheckBoxArrayGroup3.Length || j >= namesRaw.Length)
                    {
                        return;
                    }
                    CheckBoxArrayGroup3[count].Visible = true;
                    CheckBoxArrayGroup3[count].Text = namesRaw[j];
                    CheckBoxArrayGroup3[count].Name = namesRaw[j];
                    int l = namesRaw[j].Length;
                    String signalNameNoFormat = namesRaw[j].Substring(0, l - 4);
                    String signalNameCalFormat = signalNameNoFormat + " CAL";
                    //If same signal name with CAL format exists, add it to next checkbox. Otherwise skip next checkbox.
                    if (namesCal.Contains(signalNameCalFormat))
                    {
                        CheckBoxArrayGroup3[count + 1].Visible = true;
                        CheckBoxArrayGroup3[count + 1].Text = "CAL";
                        CheckBoxArrayGroup3[count + 1].Name = signalNameCalFormat;
                    }
                    count += 2;
                }
                for (int j = 0; j < namesCal.Length; j++)
                {
                    if (j >= CheckBoxArrayGroup3.Length || count >= CheckBoxArrayGroup3.Length || j >= namesCal.Length)
                    {
                        return;
                    }
                    //If CAL signal already added to checkbox, skip it. 
                    bool skip = false;
                    foreach (CheckBox cb in CheckBoxArrayGroup3)
                    {
                        if (cb.Name.Equals(namesCal[j]))
                        {
                            skip = true;
                        }
                    }
                    if (!skip)
                    {
                        CheckBoxArrayGroup3[count].Visible = true;
                        CheckBoxArrayGroup3[count].Text = namesCal[j];
                        CheckBoxArrayGroup3[count].Name = namesCal[j];
                        count++;
                    }
                }
            }
        }

        public void SetupGraph1(int numberOfTraces, String[] signalNames)
        {
            if (numberOfTraces > maxNumberOfTracesToPlot)
            {
                numberOfTraces = maxNumberOfTracesToPlot;
            }
            if (NumberOfTracesCountGraph1 == 0)
            {
                DataListGraph1.Clear();
                CurvesListGraph1.Clear();
                MyPaneGraph1.CurveList.Clear();
            }

            for (int j = 0; j < numberOfTraces; j++)
            {
                DataListGraph1.Add(new RollingPointPairList(1000));
                CurvesListGraph1.Add(MyPaneGraph1.AddCurve(signalNames[j], DataListGraph1[j], TraceColours[j], SymbolType.None));
                CurvesListGraph1[j].Line.Width = 1.5F;
                NumberOfTracesCountGraph1++;
                if (NumberOfTracesCountGraph1 >= CheckBoxArrayGroup1.Length)
                {
                    return;
                }
            }
            ZedGraphControl1.AxisChange();
        }

        public void SetupGraph2(int numberOfTraces, String[] signalNames)
        {
            if (numberOfTraces > maxNumberOfTracesToPlot)
            {
                numberOfTraces = maxNumberOfTracesToPlot;
            }
            if (NumberOfTracesCountGraph2 == 0)
            {
                DataListGraph2.Clear();
                CurvesListGraph2.Clear();
                MyPaneGraph2.CurveList.Clear();
            }

            for (int j = 0; j < numberOfTraces; j++)
            {
                DataListGraph2.Add(new RollingPointPairList(1000));
                CurvesListGraph2.Add(MyPaneGraph2.AddCurve(signalNames[j], DataListGraph1[j], TraceColours[j], SymbolType.None));
                CurvesListGraph2[j].Line.Width = 1.5F;
                NumberOfTracesCountGraph2++;
                if (NumberOfTracesCountGraph2 >= CheckBoxArrayGroup2.Length)
                {
                    return;
                }
            }
            ZedGraphControl2.AxisChange();
        }

        public void SetupGraph3(int numberOfTraces, String[] signalNames)
        {
            if (numberOfTraces > maxNumberOfTracesToPlot)
            {
                numberOfTraces = maxNumberOfTracesToPlot;
            }
            if (NumberOfTracesCountGraph3 == 0)
            {
                DataListGraph3.Clear();
                CurvesListGraph3.Clear();
                MyPaneGraph3.CurveList.Clear();
            }

            for (int j = 0; j < numberOfTraces; j++)
            {
                DataListGraph3.Add(new RollingPointPairList(1000));
                CurvesListGraph3.Add(MyPaneGraph3.AddCurve(signalNames[j], DataListGraph1[j], TraceColours[j], SymbolType.None));
                CurvesListGraph3[j].Line.Width = 1.5F;
                NumberOfTracesCountGraph3++;
                if (NumberOfTracesCountGraph3 >= CheckBoxArrayGroup3.Length)
                {
                    return;
                }
            }
            ZedGraphControl3.AxisChange();
        }

        private void MyContextMenuBuilder(ZedGraphControl control, ContextMenuStrip menuStrip)
        {
            foreach (ToolStripMenuItem item in menuStrip.Items)
            {
                if ((string)item.Tag == "show_val")
                {
                    //menuStrip.Items.Remove(item);
                    item.Enabled = false;
                    break;
                }
            }
        }

        public void DrawingData(string[] signalName, string[] formats, double[] data)
        {
            if (MyPaneGraph1.CurveList == null || MyPaneGraph2.CurveList == null || MyPaneGraph3.CurveList == null)
                return;

            if ((MyPaneGraph1.CurveList.Count <= 0) || (MyPaneGraph2.CurveList.Count <= 0) || (MyPaneGraph3.CurveList.Count <= 0))
                return;

            for (int i = 0; i < MyPaneGraph1.CurveList.Count; i++)
            {
                MyPaneGraph1.CurveList[i].IsVisible = false;
                MyPaneGraph1.CurveList[i].Label.IsVisible = false;
                MyPaneGraph2.CurveList[i].IsVisible = false;
                MyPaneGraph2.CurveList[i].Label.IsVisible = false;
                MyPaneGraph3.CurveList[i].IsVisible = false;
                MyPaneGraph3.CurveList[i].Label.IsVisible = false;
                LineItem curve1 = MyPaneGraph1.CurveList[i] as LineItem;
                LineItem curve2 = MyPaneGraph2.CurveList[i] as LineItem;
                LineItem curve3 = MyPaneGraph3.CurveList[i] as LineItem;
                if (curve1 == null || curve2 == null || curve3 == null)
                    return;
                if (DataListGraph1[i] == null || DataListGraph2[i] == null || DataListGraph3[i] == null)
                    return;
            }

            for (int j = 0; j < MyPaneGraph1.CurveList.Count; j++)
            {
                DataListGraph1[j].Add(CountXAxisDataPoints, data[j]);
                DataListGraph2[j].Add(CountXAxisDataPoints, data[j]);
                DataListGraph3[j].Add(CountXAxisDataPoints, data[j]);
                //System.Console.WriteLine("j: " + j + "" + "CountXAxisDataPoints: " + CountXAxisDataPoints + "" + "data[j]: " + data[j]);
            }

            for (int k = 0; k < SelectedSignalNameGroup1.Count; k++)
            {
                int index = MyPaneGraph1.CurveList.IndexOf(SelectedSignalNameGroup1[k]);
                if (index != -1)
                {
                    MyPaneGraph1.CurveList[index].IsVisible = true;
                    MyPaneGraph1.CurveList[index].Label.IsVisible = true;
                }
            }

            if (IsGraph2Visible)
            {
                for (int k = 0; k < SelectedSignalNameGroup2.Count; k++)
                {
                    int index = MyPaneGraph2.CurveList.IndexOf(SelectedSignalNameGroup2[k]);
                    if (index != -1)
                    {
                        MyPaneGraph2.CurveList[index].IsVisible = true;
                        MyPaneGraph2.CurveList[index].Label.IsVisible = true;
                    }
                }
            }

            if (IsGraph3Visible)
            {
                for (int k = 0; k < SelectedSignalNameGroup3.Count; k++)
                {
                    int index = MyPaneGraph3.CurveList.IndexOf(SelectedSignalNameGroup3[k]);
                    if (index != -1)
                    {
                        MyPaneGraph3.CurveList[index].IsVisible = true;
                        MyPaneGraph3.CurveList[index].Label.IsVisible = true;
                    }
                }
            }

            Scale xScale1 = MyPaneGraph1.XAxis.Scale;
            xScale1.Max = CountXAxisDataPoints;
            xScale1.Min = CountXAxisDataPoints - 1000;

            Scale xScale2 = MyPaneGraph2.XAxis.Scale;
            xScale2.Max = CountXAxisDataPoints;
            xScale2.Min = CountXAxisDataPoints - 1000;

            Scale xScale3 = MyPaneGraph3.XAxis.Scale;
            xScale3.Max = CountXAxisDataPoints;
            xScale3.Min = CountXAxisDataPoints - 1000;

            //Invalidate() is called to update the data displayed on the graph. This produces a lag. 
            //Only update every 'factor' number of samples
            if (ShimmerDevice.GetSamplingRate() > 1000)
            {
                int factor = 50;
                if (IsGraph2Visible && IsGraph3Visible)
                {
                    factor = 60;
                }
                else if (IsGraph2Visible && (!IsGraph3Visible))
                {
                    factor = 80;
                }

                if (usingLinux)
                {
                    System.Console.WriteLine("Using Linux");
                    factor = 500;
                }

                if (CountXAxisDataPoints % factor == 0)
                {
                    ZedGraphControl1.AxisChange();
                    ZedGraphControl1.Invalidate();
                    MyContextMenuBuilder(ZedGraphControl1, ZedGraphControl1.ContextMenuStrip);

                    ZedGraphControl2.AxisChange();
                    ZedGraphControl2.Invalidate();
                    MyContextMenuBuilder(ZedGraphControl2, ZedGraphControl2.ContextMenuStrip);

                    ZedGraphControl3.AxisChange();
                    ZedGraphControl3.Invalidate();
                    MyContextMenuBuilder(ZedGraphControl3, ZedGraphControl3.ContextMenuStrip);
                }
            }
            else if (ShimmerDevice.GetSamplingRate() > 500)
            {
                int factor = 100;
                if (IsGraph2Visible && IsGraph3Visible)
                {
                    factor = 200;
                }
                else if (IsGraph2Visible && (!IsGraph3Visible))
                {
                    factor = 200;
                }
                if (usingLinux)
                {
                    System.Console.WriteLine("Using Linux");
                    factor = 250;
                }
                if (CountXAxisDataPoints % factor == 0)
                {
                    ZedGraphControl1.AxisChange();
                    ZedGraphControl1.Invalidate();
                    MyContextMenuBuilder(ZedGraphControl1, ZedGraphControl1.ContextMenuStrip);

                    ZedGraphControl2.AxisChange();
                    ZedGraphControl2.Invalidate();
                    MyContextMenuBuilder(ZedGraphControl2, ZedGraphControl2.ContextMenuStrip);

                    ZedGraphControl3.AxisChange();
                    ZedGraphControl3.Invalidate();
                    MyContextMenuBuilder(ZedGraphControl3, ZedGraphControl3.ContextMenuStrip);
                }
            }
            else if (ShimmerDevice.GetSamplingRate() > 200)
            {
                int factor = 40;
                if (IsGraph2Visible && IsGraph3Visible)
                {
                    factor = 40;
                }
                else if (IsGraph2Visible && (!IsGraph3Visible))
                {
                    factor = 80;
                }
                if (usingLinux)
                {
                    System.Console.WriteLine("Using Linux");
                    factor = 100;
                }
                if (CountXAxisDataPoints % factor == 0)
                {
                    ZedGraphControl1.AxisChange();
                    ZedGraphControl1.Invalidate();
                    MyContextMenuBuilder(ZedGraphControl1, ZedGraphControl1.ContextMenuStrip);

                    ZedGraphControl2.AxisChange();
                    ZedGraphControl2.Invalidate();
                    MyContextMenuBuilder(ZedGraphControl2, ZedGraphControl2.ContextMenuStrip);

                    ZedGraphControl3.AxisChange();
                    ZedGraphControl3.Invalidate();
                    MyContextMenuBuilder(ZedGraphControl3, ZedGraphControl3.ContextMenuStrip);
                }
            }
            else
            {
                int factor = 10;
                if (IsGraph2Visible && IsGraph3Visible)
                {
                    factor = 20;
                }
                else if (IsGraph2Visible && (!IsGraph3Visible))
                {
                    factor = 20;
                }
                if (usingLinux)
                {
                    System.Console.WriteLine("Using Linux");
                    factor = 50;
                }
                if (CountXAxisDataPoints % factor == 0)
                {
                    ZedGraphControl1.AxisChange();
                    ZedGraphControl1.Invalidate();
                    MyContextMenuBuilder(ZedGraphControl1, ZedGraphControl1.ContextMenuStrip);

                    ZedGraphControl2.AxisChange();
                    ZedGraphControl2.Invalidate();
                    MyContextMenuBuilder(ZedGraphControl2, ZedGraphControl2.ContextMenuStrip);

                    ZedGraphControl3.AxisChange();
                    ZedGraphControl3.Invalidate();
                    MyContextMenuBuilder(ZedGraphControl3, ZedGraphControl3.ContextMenuStrip);
                }
            }
            CountXAxisDataPoints++;
        }

        public void AppendTextBox(string value)
        {
            if (InvokeRequired)
            {
                this.Invoke(new Action<string>(AppendTextBox), new object[] { value });
                return;
            }
            textBoxShimmerState.Text = value;
        }

        public string GetLoggingFormat()
        {
            return Delimeter;
        }

        public void SetLoggingFormat(string format)
        {
            Delimeter = format;
        }

        private void buttonAddGraph_Click(object sender, EventArgs e)
        {
            if (IsGraph2Visible && (!IsGraph3Visible))
            {
                int spacing = 10;
                IsGraph3Visible = true;
                ZedGraphControl3.Location = new Point(ZedGraphControl1.Location.X, ZedGraphControl2.Bottom + spacing);
                ZedGraphControl3.Visible = true;
                SetupCheckboxesGroup3(ShimmerDevice.GetDeviceName(), StreamingSignalNamesRaw.ToArray(), StreamingSignalNamesCal.ToArray());

                if (!usingLinux)
                {
                    int w = this.Size.Width;
                    if (this.Size.Height < 1200)
                    {
                        this.Size = new System.Drawing.Size(w, 1200);
                    }
                }
            }
            else if (!IsGraph2Visible)
            {
                int spacing = 10;
                IsGraph2Visible = true;
                ZedGraphControl2.Location = new Point(ZedGraphControl1.Location.X, ZedGraphControl1.Bottom + spacing);
                ZedGraphControl2.Visible = true;
                SetupCheckboxesGroup2(ShimmerDevice.GetDeviceName(), StreamingSignalNamesRaw.ToArray(), StreamingSignalNamesCal.ToArray());

                if (!usingLinux)
                {
                    int w = this.Size.Width;
                    if (this.Size.Height < 797)
                    {
                        this.Size = new System.Drawing.Size(w, 797);
                    }
                }
            }
        }

        private void buttonRemoveGraph_Click(object sender, EventArgs e)
        {
            if (IsGraph3Visible)
            {
                ZedGraphControl3.Visible = false;
                IsGraph3Visible = false;
                foreach (CheckBox cb in CheckBoxArrayGroup3)
                {
                    cb.Visible = false;
                    cb.Checked = false;
                }
            }
            else if (IsGraph2Visible)
            {
                ZedGraphControl2.Visible = false;
                IsGraph2Visible = false;
                foreach (CheckBox cb in CheckBoxArrayGroup2)
                {
                    cb.Visible = false;
                    cb.Checked = false;
                }
            }
        }

        private void ClearGraphData(ZedGraph.ZedGraphControl graphControl, List<RollingPointPairList> dataList)
        {
            if (graphControl.GraphPane.CurveList == null || dataList == null)
                return;

            foreach (var list in dataList)
            {
                list.Clear(); 
            }
            graphControl.Invalidate(); // Refresh 
        }

        private void buttonConnect_Click(object sender, EventArgs e)
        {
            RemoveAllTextBox();
            Connect();
        }

        public void Connect()
        {
            if (ShimmerDevice != null)
            {
                ShimmerDevice.UICallback -= this.HandleEvent;
            }
            // btsd changes1
            if (comboBoxComPorts.Text.Contains("COM") || comboBoxComPorts.Text.Contains("dev"))
            {
                System.Console.WriteLine("TRY CONNECT" + comboBoxComPorts.Text);
                ShimmerDevice = new ShimmerLogAndStreamSystemSerialPort("Shimmer", comboBoxComPorts.Text);
            }
            else
            {
                ShimmerDevice = new ShimmerLogAndStream32FeetBLE("Shimmer", comboBoxComPorts.Text, ShimmerAPI.ShimmerDevice.ShimmerVersion.SHIMMER3R);
            }
            ShimmerDevice.UICallback += this.HandleEvent;

            //for Shimmer and ShimmerSDBT
            ShimmerDevice.SetShimmerAddress(comboBoxComPorts.Text);

            //for Shimmer32Feet and ShimmerSDBT32Feet
            //shimmer.SetAddress("00066666940E");
            bool connect = true; // check to connect one at a time

            if (ShimmerDevice.GetState() != ShimmerBluetooth.SHIMMER_STATE_CONNECTED)
            {
                if (connect)
                {
                    ShimmerDevice.StartConnectThread();
                    connect = false;
                }
            }
        }

        private void buttonDisconnect_Click(object sender, EventArgs e)
        {
            RemoveAllTextBox();

            if (WriteToFile != null)
            {
                WriteToFile.CloseFile();
                ToolStripMenuItemSaveToCSV.Checked = false;
            }

            labelPRR.Visible = false;
            if (ShimmerDevice != null)
            {
                if (ShimmerDevice.GetState() == (int)ShimmerBluetooth.SHIMMER_STATE_STREAMING)
                {
                    if (ShimmerDevice.GetFirmwareIdentifier() == 3)
                    {

                    }
                    else
                    {
                        ShimmerDevice.StopStreaming();
                    }
                }
                ShimmerDevice.Disconnect();
                if (Orientation3DForm != null)
                {
                    Orientation3DForm.Close();
                }
            }
            ShimmerIdSetup.Clear();
            StreamingSignalNamesRaw.Clear();
            StreamingSignalNamesCal.Clear();

            // clear all plot checkboxes
            foreach (CheckBox cb in CheckBoxArrayGroup1)
            {
                cb.Checked = false;
                cb.Visible = false;
            }

            foreach (CheckBox cb in CheckBoxArrayGroup2)
            {
                cb.Checked = false;
                cb.Visible = false;
            }

            foreach (CheckBox cb in CheckBoxArrayGroup3)
            {
                cb.Checked = false;
                cb.Visible = false;
            }
        }

        public void EnablePPGtoHR(bool enable)
        {
            EnablePPGtoHRConversion = enable;
        }

        public bool GetEnablePPGtoHR()
        {
            return EnablePPGtoHRConversion;
        }

        public void SetNumberOfBeatsToAve(int number)
        {
            NumberOfHeartBeatsToAverage = number;
        }

        public void SetNumberOfBeatsToAveECG(int number)
        {
            NumberOfHeartBeatsToAverageECG = number;
        }

        public int GetNumberOfBeatsToAve()
        {
            return NumberOfHeartBeatsToAverage;
        }

        public int GetNumberOfBeatsToAveECG()
        {
            return NumberOfHeartBeatsToAverageECG;
        }

        public void EnableECGtoHR(bool enable)
        {
            EnableECGtoHRConversion = enable;
        }

        public bool GetEnableECGtoHR()
        {
            return EnableECGtoHRConversion;
        }

        private void buttonStart_Click(object sender, EventArgs e)
        {
            FirstTime = true;
            labelPRR.Visible = true;
            buttonStart_Click1();
        }

        // btsd changes1
        public void buttonStart_Click1()
        {

            SetupFilters();

            //ECG-HR Conversion
            if (EnableECGtoHRConversion)
            {
                ECGtoHRAdaptive = new ECGToHRAdaptive(ShimmerDevice.GetSamplingRate());
            }

            CountXAxisDataPoints = 0;
            CountXAxisDataPoints++;
            ShimmerDevice.StartStreamingandLog();
        }


        private void buttonStop_Click(object sender, EventArgs e)
        {
            RemoveAllTextBox();
            labelPRR.Visible = false;
            ShimmerDevice.StopStreaming();
            buttonStop_Click1();
        }

        //private delegate void EnableButtonsCallback(int state);

        public void buttonStop_Click1()
        {
            RemoveAllTextBox();
            labelPRR.Visible = false;
            if (WriteToFile != null)
            {
                WriteToFile.CloseFile();
                ToolStripMenuItemSaveToCSV.Checked = false;
            }
            ShimmerIdSetup.Clear();
            StreamingSignalNamesRaw.Clear();
            StreamingSignalNamesCal.Clear();
            NumberOfTracesCountGraph1 = 0;
            NumberOfTracesCountGraph2 = 0;
            NumberOfTracesCountGraph3 = 0;
            // EnableHPF_0_05HZ
            // EnableHPF_0_5HZ
            // EnableHPF_5HZ
            // EnableBSF_49_51HZ
            // EnableBSF_59_61HZ

            if (EnablePPGtoHRConversion)
            {
                if (LPF_PPG != null)
                {
                    LPF_PPG.resetBuffers();
                }
                if (HPF_PPG != null)
                {
                    HPF_PPG.resetBuffers();
                }
            }
            if (EnableHPF_0_05HZ || EnableHPF_0_5HZ || EnableHPF_5HZ)
            {
                try
                {
                    HPF_Exg1Ch1.resetBuffers();
                    HPF_Exg1Ch2.resetBuffers();
                }
                catch
                {
                }
            }
            if (EnableNQF)
            {
                try
                {
                    NQF_Exg1Ch1.resetBuffers();
                    NQF_Exg1Ch2.resetBuffers();
                }
                catch
                {
                }
            }
            if (EnableNQF)
            {
                try
                {
                    NQF_Exg2Ch1.resetBuffers();
                    NQF_Exg2Ch2.resetBuffers();
                }
                catch
                {
                }
            }
            if (EnableHPF_0_05HZ || EnableHPF_0_5HZ || EnableHPF_5HZ)
            {
                try
                {
                    HPF_Exg2Ch1.resetBuffers();
                    HPF_Exg2Ch2.resetBuffers();
                }
                catch
                {
                }
            }
            if (EnableBSF_49_51HZ || EnableBSF_59_61HZ)
            {
                try
                {
                    BSF_Exg1Ch1.resetBuffers();
                    BSF_Exg1Ch2.resetBuffers();
                }
                catch
                {
                }
            }
            if (EnableBSF_49_51HZ || EnableBSF_59_61HZ)
            {
                try
                {
                    BSF_Exg2Ch1.resetBuffers();
                    BSF_Exg2Ch2.resetBuffers();
                }
                catch
                {
                }
            }
            // clear all plot checkboxes
            foreach (CheckBox cb in CheckBoxArrayGroup1)
            {
                cb.Checked = false;
                cb.Visible = false;
            }

            foreach (CheckBox cb in CheckBoxArrayGroup2)
            {
                cb.Checked = false;
                cb.Visible = false;
            }

            foreach (CheckBox cb in CheckBoxArrayGroup3)
            {
                cb.Checked = false;
                cb.Visible = false;
            }

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
                if (ShimmerDevice.GetFirmwareIdentifier() == ShimmerBluetooth.FW_IDENTIFIER_SHIMMERECGMD)
                {
                    button2.Visible = true;
                    textBoxSawtoothValue.Visible = true;
                    button2.Enabled = true;
                    textBoxSawtoothValue.Enabled = true;
                }

                buttonConnect.Enabled = false;
                //checkBoxTSACheck.Visible = true;
                checkBoxTSACheck.Checked = ShimmerDevice.mEnableTimeStampAlignmentCheck;
                buttonDisconnect.Enabled = true;
                if (ShimmerDevice.GetFirmwareIdentifier() == ShimmerBluetooth.FW_IDENTIFIER_LOGANDSTREAM)
                {
                    buttonStreamandLog.Enabled = true;
                    buttonReadDirectory.Enabled = true;
                    buttonReadDirectory.Visible = true;
                    buttonStreamandLog.Visible = true;
                    buttonStopStreamandLog.Visible = true;
                    buttonStopStreamandLog.Enabled = false;
                    
                    if (ShimmerDevice.IsCRCSupported())
                    {
                        checkBoxCRC.Visible = true;
                        checkBoxCRC.Enabled = true;

                        if (ShimmerDevice.GetCRCMode().Equals(ShimmerBluetooth.BTCRCMode.OFF))
                        {
                            checkBoxCRC.Checked = false;
                        } else
                        {
                            checkBoxCRC.Checked = true;
                        }

                    } else
                    {
                        checkBoxCRC.Visible = false;
                        checkBoxCRC.Enabled = false;
                    }
                }
                else
                {
                    buttonStreamandLog.Visible = false;
                    buttonStopStreamandLog.Visible = false;
                    buttonReadDirectory.Visible = false;
                }
                buttonStream.Enabled = true;
                buttonStop.Enabled = false;
                configureToolStripMenuItem.Enabled = true;
                buttonStop_Click1();
            }
            // btsd changes3 start
            else if (state == (int)ShimmerBluetooth.SHIMMER_STATE_CONNECTING)
            {
                buttonConnect.Enabled = false;
                buttonDisconnect.Enabled = false;
                buttonStreamandLog.Enabled = false;
                buttonStopStreamandLog.Enabled = false;
                buttonReadDirectory.Enabled = false;
                buttonStream.Enabled = false;
                buttonStop.Enabled = false;
                configureToolStripMenuItem.Enabled = false;
                ToolStripMenuItemShow3DOrientation.Enabled = false;
            }
            // btsd changes3 end
            else if (state == (int)ShimmerBluetooth.SHIMMER_STATE_NONE)
            {
                button2.Visible = false;
                textBoxSawtoothValue.Visible = false;
                buttonConnect.Enabled = true;
                buttonDisconnect.Enabled = false;
                buttonStreamandLog.Enabled = false;
                buttonStopStreamandLog.Enabled = false;
                buttonReadDirectory.Enabled = false;
                buttonStream.Enabled = false;
                buttonStop.Enabled = false;
                configureToolStripMenuItem.Enabled = false;
                ToolStripMenuItemShow3DOrientation.Enabled = false;
                checkBoxCRC.Visible = false;
            }
            else if (state == (int)ShimmerBluetooth.SHIMMER_STATE_STREAMING)
            {
                button2.Enabled = false;
                textBoxSawtoothValue.Enabled = false;
                buttonConnect.Enabled = false;
                buttonDisconnect.Enabled = true;
                buttonStreamandLog.Enabled = false;
                buttonStopStreamandLog.Enabled = true;
                buttonStream.Enabled = false;
                buttonStop.Enabled = true;
                // btsd changes1
                configureToolStripMenuItem.Enabled = true;
                labelPRR.Visible = true;
                checkBoxCRC.Enabled = false;
            }

            Boolean leadOffDetectionEnabled = getLeadOffDetectionEnabled(); // method returns true if ECG lead off detection is enabled

            labelExGLeadOffDetection.Visible = leadOffDetectionEnabled;
            labelLeadOffStatus1.Visible = leadOffDetectionEnabled;
            labelLeadOffStatus2.Visible = leadOffDetectionEnabled;
            labelLeadOffStatus3.Visible = leadOffDetectionEnabled;
            labelLeadOffStatus4.Visible = leadOffDetectionEnabled;
            labelLeadOffStatus5.Visible = leadOffDetectionEnabled;
            textBoxLeadOffStatus1.Visible = leadOffDetectionEnabled;
            textBoxLeadOffStatus2.Visible = leadOffDetectionEnabled;
            textBoxLeadOffStatus3.Visible = leadOffDetectionEnabled;
            textBoxLeadOffStatus4.Visible = leadOffDetectionEnabled;
            textBoxLeadOffStatus5.Visible = leadOffDetectionEnabled;

        }

        private Boolean getLeadOffDetectionEnabled() // method returns true if ECG lead off detection is enabled
        {

            if (((ShimmerDevice.GetEnabledSensors() & (int)ShimmerBluetooth.SensorBitmapShimmer3.SENSOR_EXG1_16BIT) > 0
                                    && (ShimmerDevice.GetEnabledSensors() & (int)ShimmerBluetooth.SensorBitmapShimmer3.SENSOR_EXG2_16BIT) > 0) ||
                                    (((ShimmerDevice.GetEnabledSensors() & (int)ShimmerBluetooth.SensorBitmapShimmer3.SENSOR_EXG1_24BIT) > 0
                                    && (ShimmerDevice.GetEnabledSensors() & (int)ShimmerBluetooth.SensorBitmapShimmer3.SENSOR_EXG2_24BIT) > 0)))
            {

                if ((ShimmerDevice.GetState() == (int)ShimmerBluetooth.SHIMMER_STATE_STREAMING) && (ShimmerDevice.GetShimmerVersion() == (int)ShimmerBluetooth.ShimmerVersion.SHIMMER3 || ShimmerDevice.GetShimmerVersion() == (int)ShimmerBluetooth.ShimmerVersion.SHIMMER3R) && (ShimmerDevice.IsDefaultECGConfigurationEnabled()) && (((ShimmerDevice.GetEXG1RegisterByte(1) & 0x40) != 0) && ((ShimmerDevice.GetEXG2RegisterByte(1) & 0x40) != 0)))
                {
                    return true;
                }
                else
                {
                    return false;
                }
            }
            else
            {
                return false;
            }
        }

        public void HandleEvent(object sender, EventArgs args)
        {
            CustomEventArgs eventArgs = (CustomEventArgs)args;
            int indicator = eventArgs.getIndicator();

            switch (indicator)
            {
                case (int)ShimmerBluetooth.ShimmerIdentifier.MSG_IDENTIFIER_STATE_CHANGE:

                    System.Diagnostics.Debug.Write(((ShimmerBluetooth)sender).GetDeviceName() + " State = " + ((ShimmerBluetooth)sender).GetStateString() + System.Environment.NewLine);
                    int state = (int)eventArgs.getObject();
                    if (state == (int)ShimmerBluetooth.SHIMMER_STATE_CONNECTED)
                    {
                        AppendTextBox("Connected");
                        String label = "Connected to " + ShimmerDevice.GetShimmerAddress() + ". Firmware Version: " + ShimmerDevice.GetFirmwareVersionFullName();
                        if (ShimmerDevice.GetRadioVersion().Length > 0)
                        {
                            label = label + ". Radio Version: " + ShimmerDevice.GetRadioVersion();
                        }
                        ChangeStatusLabel(label);
                        EnableButtons((int)ShimmerBluetooth.SHIMMER_STATE_CONNECTED);
                        //buttonStop_Click1();
                    }
                    else if (state == (int)ShimmerBluetooth.SHIMMER_STATE_CONNECTING)
                    {
                        AppendTextBox("Connecting");
                        ChangeStatusLabel("Connecting");
                        // btsd changes3 start
                        EnableButtons((int)ShimmerBluetooth.SHIMMER_STATE_CONNECTING);
                        // btsd changes3 end
                    }
                    else if (state == (int)ShimmerBluetooth.SHIMMER_STATE_NONE)
                    {
                        AppendTextBox("Disconnected");
                        ChangeStatusLabel("Disconnected");
                        EnableButtons((int)ShimmerBluetooth.SHIMMER_STATE_NONE);
                    }
                    else if (state == (int)ShimmerBluetooth.SHIMMER_STATE_STREAMING)
                    {
                        AppendTextBox("Streaming");
                        ChangeStatusLabel("Streaming");
                        EnableButtons((int)ShimmerBluetooth.SHIMMER_STATE_STREAMING);
                        FirstTime = true;
                    }
                    break;
                case (int)ShimmerBluetooth.ShimmerIdentifier.MSG_IDENTIFIER_NOTIFICATION_MESSAGE:
                    string message = (string)eventArgs.getObject();
                    System.Diagnostics.Debug.Write(((ShimmerBluetooth)sender).GetDeviceName() + message + System.Environment.NewLine);
                    //Message BOX
                    int minorIdentifier = eventArgs.getMinorIndication();
                    if (minorIdentifier == (int)ShimmerLogAndStream.ShimmerSDBTMinorIdentifier.MSG_WARNING)
                    {
                        MessageBox.Show(message, Control.ApplicationName,
                            MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    }
                    else if (minorIdentifier == (int)ShimmerLogAndStream.ShimmerSDBTMinorIdentifier.MSG_EXTRA_REMOVABLE_DEVICES_DETECTED)
                    {
                        MessageBox.Show(message, "Message");
                        FolderBrowserDialog fbd = new FolderBrowserDialog();
                        DialogResult result = fbd.ShowDialog();
                        ShimmerDevice.SetDrivePath(fbd.SelectedPath);
                    }
                    else if (minorIdentifier == (int)ShimmerLogAndStream.ShimmerSDBTMinorIdentifier.MSG_ERROR)
                    {
                        MessageBox.Show(message, Control.ApplicationName, MessageBoxButtons.OK, MessageBoxIcon.Error);
                    }
                    else if (message.Equals("Connection lost"))
                    {
                        MessageBox.Show("Connection with device lost while streaming", Control.ApplicationName,
                            MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    }
                    else
                    {
                        ChangeStatusLabel(message);
                    }

                    break;
                case (int)ShimmerBluetooth.ShimmerIdentifier.MSG_IDENTIFIER_DATA_PACKET:
                    // this is essential to ensure the object is not a reference
                    ObjectCluster objectCluster = new ObjectCluster((ObjectCluster)eventArgs.getObject());
                    List<String> names = objectCluster.GetNames();
                    List<String> formats = objectCluster.GetFormats();
                    List<String> units = objectCluster.GetUnits();
                    List<Double> data = objectCluster.GetData();

                    if (EnablePPGtoHRConversion)
                    {
                        int index = objectCluster.GetIndex(PPGSignalName, ShimmerConfiguration.SignalFormats.CAL);
                        int indexts = objectCluster.GetIndex(ShimmerConfiguration.SignalNames.TIMESTAMP, ShimmerConfiguration.SignalFormats.CAL);
                        if (index != -1)
                        {
                            double dataFilteredLP = LPF_PPG.filterData(data[index]);
                            double dataFilteredHP = HPF_PPG.filterData(dataFilteredLP);
                            double[] dataTS = new double[] { data[indexts] };
                            //int heartRate = (int)Math.Round(PPGtoHeartRateCalculation.ppgToHrConversion(dataFilteredHP, dataTS[0]));
                            DataPPGToHROutput datahr = PPGtoHeartRateCalculation.ppgToHrandIbiConversion(dataFilteredHP, dataTS[0]);
                            int heartRate = (int)datahr.getHeartRate();
                            names.Add("Heart Rate PPG");
                            formats.Add("");
                            units.Add("Beats/min");
                            data.Add(heartRate);
                            //add inter beat interval
                            names.Add("IBI PPG");
                            formats.Add("");
                            units.Add("ms");
                            data.Add(datahr.getInterBeatInterval());

                        }
                    }


                    //3D Orientation - Cube
                    if (ShimmerDevice.Is3DOrientationEnabled() && Is3DCubeOpen)
                    {
                        double[,] rotMatrix = new double[3, 3];
                        double theta = (objectCluster.GetData(Shimmer3Configuration.SignalNames.AXIS_ANGLE_A, ShimmerConfiguration.SignalFormats.CAL)).GetData();
                        double Rx = (objectCluster.GetData(Shimmer3Configuration.SignalNames.AXIS_ANGLE_X, ShimmerConfiguration.SignalFormats.CAL)).GetData();
                        double Ry = (objectCluster.GetData(Shimmer3Configuration.SignalNames.AXIS_ANGLE_Y, ShimmerConfiguration.SignalFormats.CAL)).GetData();
                        double Rz = (objectCluster.GetData(Shimmer3Configuration.SignalNames.AXIS_ANGLE_Z, ShimmerConfiguration.SignalFormats.CAL)).GetData();
                        double q1 = (objectCluster.GetData(Shimmer3Configuration.SignalNames.QUATERNION_0, ShimmerConfiguration.SignalFormats.CAL)).GetData();
                        double q2 = (objectCluster.GetData(Shimmer3Configuration.SignalNames.QUATERNION_1, ShimmerConfiguration.SignalFormats.CAL)).GetData();
                        double q3 = (objectCluster.GetData(Shimmer3Configuration.SignalNames.QUATERNION_2, ShimmerConfiguration.SignalFormats.CAL)).GetData();
                        double q4 = (objectCluster.GetData(Shimmer3Configuration.SignalNames.QUATERNION_3, ShimmerConfiguration.SignalFormats.CAL)).GetData();

                        //convert to a rotation matrix
                        rotMatrix[0, 0] = 2 * q1 * q1 - 1 + 2 * q2 * q2;
                        rotMatrix[0, 1] = 2 * (q2 * q3 - q1 * q4);
                        rotMatrix[0, 2] = 2 * (q2 * q4 + q1 * q3);
                        rotMatrix[1, 0] = 2 * (q2 * q3 + q1 * q4);
                        rotMatrix[1, 1] = 2 * q1 * q1 - 1 + 2 * q3 * q3;
                        rotMatrix[1, 2] = 2 * (q3 * q4 - q1 * q2);
                        rotMatrix[2, 0] = 2 * (q2 * q4 - q1 * q3);
                        rotMatrix[2, 1] = 2 * (q3 * q4 + q1 * q2);
                        rotMatrix[2, 2] = 2 * q1 * q1 - 1 + 2 * q4 * q4;

                        // set function
                        if (SetOrientation)
                        {
                            SetOriMatrix = matrixinverse3x3(rotMatrix);
                            SetOrientation = false;
                        }

                        rotMatrix = matrixmultiplication(SetOriMatrix, rotMatrix);
                        AxisAngle aa = new AxisAngle(new Matrix3d(rotMatrix[0, 0], rotMatrix[0, 1], rotMatrix[0, 2], rotMatrix[1, 0], rotMatrix[1, 1], rotMatrix[1, 2], rotMatrix[2, 0], rotMatrix[2, 1], rotMatrix[2, 2]));
                        try
                        {
                            Orientation3DForm.setAxisAngle(aa.angle * 180 / Math.PI, aa.x, aa.y, aa.z);
                        }
                        catch (System.NullReferenceException)
                        {

                        }
                    }

                    //ExG filtering
                    if (ShimmerDevice.GetShimmerVersion() == (int)ShimmerBluetooth.ShimmerVersion.SHIMMER3
                        || ShimmerDevice.GetShimmerVersion() == (int)ShimmerBluetooth.ShimmerVersion.SHIMMER3R)
                    {
                        if (EnableHPF_0_05HZ || EnableHPF_0_5HZ || EnableHPF_5HZ)
                        {
                            int[] index1 = new int[2];
                            int[] index2 = new int[2];
                            if ((ShimmerDevice.GetEnabledSensors() & (int)ShimmerBluetooth.SensorBitmapShimmer3.SENSOR_EXG1_24BIT) > 0)
                            {
                                if (ShimmerDevice.IsDefaultECGConfigurationEnabled())
                                {
                                    try
                                    {
                                        index1[0] = objectCluster.GetIndex(Shimmer3Configuration.SignalNames.ECG_LL_RA, ShimmerConfiguration.SignalFormats.CAL);
                                        double[] filteredData1 = HPF_Exg1Ch1.filterData(new double[] { data[index1[0]] });
                                        data[index1[0]] = filteredData1[0];
                                        index1[1] = objectCluster.GetIndex(Shimmer3Configuration.SignalNames.ECG_LA_RA, ShimmerConfiguration.SignalFormats.CAL);
                                        double[] filteredData2 = HPF_Exg1Ch2.filterData(new double[] { data[index1[1]] });
                                        data[index1[1]] = filteredData2[0];
                                    }
                                    catch
                                    {
                                    }
                                }
                                else if (ShimmerDevice.IsDefaultEMGConfigurationEnabled())
                                {
                                    try
                                    {
                                        index1[0] = objectCluster.GetIndex(Shimmer3Configuration.SignalNames.EMG_CH1, ShimmerConfiguration.SignalFormats.CAL);
                                        double[] filteredData1 = HPF_Exg1Ch1.filterData(new double[] { data[index1[0]] });
                                        data[index1[0]] = filteredData1[0];
                                        index1[1] = objectCluster.GetIndex(Shimmer3Configuration.SignalNames.EMG_CH2, ShimmerConfiguration.SignalFormats.CAL);
                                        double[] filteredData2 = HPF_Exg1Ch2.filterData(new double[] { data[index1[1]] });
                                        data[index1[1]] = filteredData2[0];
                                    }
                                    catch
                                    {
                                    }
                                }
                                else
                                {
                                    try
                                    {
                                        index1[0] = objectCluster.GetIndex(Shimmer3Configuration.SignalNames.EXG1_CH1, ShimmerConfiguration.SignalFormats.CAL);
                                        double[] filteredData1 = HPF_Exg1Ch1.filterData(new double[] { data[index1[0]] });
                                        data[index1[0]] = filteredData1[0];
                                        index1[1] = objectCluster.GetIndex(Shimmer3Configuration.SignalNames.EXG1_CH2, ShimmerConfiguration.SignalFormats.CAL);
                                        double[] filteredData2 = HPF_Exg1Ch2.filterData(new double[] { data[index1[1]] });
                                        data[index1[1]] = filteredData2[0];
                                    }
                                    catch
                                    {
                                    }
                                }
                            }
                            if ((ShimmerDevice.GetEnabledSensors() & (int)ShimmerBluetooth.SensorBitmapShimmer3.SENSOR_EXG2_24BIT) > 0)
                            {
                                if (ShimmerDevice.IsDefaultECGConfigurationEnabled())
                                {
                                    try
                                    {
                                        index2[0] = objectCluster.GetIndex(Shimmer3Configuration.SignalNames.EXG2_CH1, ShimmerConfiguration.SignalFormats.CAL);
                                        double[] filteredData1 = HPF_Exg2Ch1.filterData(new double[] { data[index2[0]] });
                                        data[index2[0]] = filteredData1[0];
                                        index2[1] = objectCluster.GetIndex(Shimmer3Configuration.SignalNames.ECG_VX_RL, ShimmerConfiguration.SignalFormats.CAL);
                                        double[] filteredData2 = HPF_Exg2Ch2.filterData(new double[] { data[index2[1]] });
                                        data[index2[1]] = filteredData2[0];
                                    }
                                    catch
                                    {
                                    }
                                }
                                else if (ShimmerDevice.IsDefaultEMGConfigurationEnabled())
                                {
                                    try
                                    {
                                        index2[0] = objectCluster.GetIndex(Shimmer3Configuration.SignalNames.EXG2_CH1, ShimmerConfiguration.SignalFormats.CAL);
                                        double[] filteredData1 = HPF_Exg2Ch1.filterData(new double[] { data[index2[0]] });
                                        data[index2[0]] = filteredData1[0];
                                        index2[1] = objectCluster.GetIndex(Shimmer3Configuration.SignalNames.EXG2_CH2, ShimmerConfiguration.SignalFormats.CAL);
                                        double[] filteredData2 = HPF_Exg2Ch2.filterData(new double[] { data[index2[1]] });
                                        data[index2[1]] = filteredData2[0];
                                    }
                                    catch
                                    {
                                    }
                                }
                                else
                                {
                                    try
                                    {
                                        index2[0] = objectCluster.GetIndex(Shimmer3Configuration.SignalNames.EXG2_CH1, ShimmerConfiguration.SignalFormats.CAL);
                                        double[] filteredData1 = HPF_Exg2Ch1.filterData(new double[] { data[index2[0]] });
                                        data[index2[0]] = filteredData1[0];
                                        index2[1] = objectCluster.GetIndex(Shimmer3Configuration.SignalNames.EXG2_CH2, ShimmerConfiguration.SignalFormats.CAL);
                                        double[] filteredData2 = HPF_Exg2Ch2.filterData(new double[] { data[index2[1]] });
                                        data[index2[1]] = filteredData2[0];
                                    }
                                    catch
                                    {
                                    }
                                }
                            }

                            if ((ShimmerDevice.GetEnabledSensors() & (int)ShimmerBluetooth.SensorBitmapShimmer3.SENSOR_EXG1_16BIT) > 0)
                            {
                                if (ShimmerDevice.IsDefaultECGConfigurationEnabled())
                                {
                                    try
                                    {
                                        index1[0] = objectCluster.GetIndex(Shimmer3Configuration.SignalNames.ECG_LL_RA, ShimmerConfiguration.SignalFormats.CAL);
                                        double[] filteredData1 = HPF_Exg1Ch1.filterData(new double[] { data[index1[0]] });
                                        data[index1[0]] = filteredData1[0];
                                        index1[1] = objectCluster.GetIndex(Shimmer3Configuration.SignalNames.ECG_LA_RA, ShimmerConfiguration.SignalFormats.CAL);
                                        double[] filteredData2 = HPF_Exg1Ch2.filterData(new double[] { data[index1[1]] });
                                        data[index1[1]] = filteredData2[0];
                                    }
                                    catch
                                    {
                                    }
                                }
                                else if (ShimmerDevice.IsDefaultEMGConfigurationEnabled())
                                {
                                    try
                                    {
                                        index1[0] = objectCluster.GetIndex(Shimmer3Configuration.SignalNames.EMG_CH1, ShimmerConfiguration.SignalFormats.CAL);
                                        double[] filteredData1 = HPF_Exg1Ch1.filterData(new double[] { data[index1[0]] });
                                        data[index1[0]] = filteredData1[0];
                                        index1[1] = objectCluster.GetIndex(Shimmer3Configuration.SignalNames.EMG_CH2, ShimmerConfiguration.SignalFormats.CAL);
                                        double[] filteredData2 = HPF_Exg1Ch2.filterData(new double[] { data[index1[1]] });
                                        data[index1[1]] = filteredData2[0];
                                    }
                                    catch
                                    {
                                    }
                                }
                                else
                                {
                                    try
                                    {
                                        index1[0] = objectCluster.GetIndex(Shimmer3Configuration.SignalNames.EXG1_CH1_16BIT, ShimmerConfiguration.SignalFormats.CAL);
                                        double[] filteredData1 = HPF_Exg1Ch1.filterData(new double[] { data[index1[0]] });
                                        data[index1[0]] = filteredData1[0];
                                        index1[1] = objectCluster.GetIndex(Shimmer3Configuration.SignalNames.EXG1_CH2_16BIT, ShimmerConfiguration.SignalFormats.CAL);
                                        double[] filteredData2 = HPF_Exg1Ch2.filterData(new double[] { data[index1[1]] });
                                        data[index1[1]] = filteredData2[0];
                                    }
                                    catch
                                    {
                                    }
                                }
                            }
                            if ((ShimmerDevice.GetEnabledSensors() & (int)ShimmerBluetooth.SensorBitmapShimmer3.SENSOR_EXG2_16BIT) > 0)
                            {
                                if (ShimmerDevice.IsDefaultECGConfigurationEnabled())
                                {
                                    try
                                    {
                                        index2[0] = objectCluster.GetIndex(Shimmer3Configuration.SignalNames.EXG2_CH1, ShimmerConfiguration.SignalFormats.CAL);
                                        double[] filteredData1 = HPF_Exg2Ch1.filterData(new double[] { data[index2[0]] });
                                        data[index2[0]] = filteredData1[0];
                                        index2[1] = objectCluster.GetIndex(Shimmer3Configuration.SignalNames.ECG_VX_RL, ShimmerConfiguration.SignalFormats.CAL);
                                        double[] filteredData2 = HPF_Exg2Ch2.filterData(new double[] { data[index2[1]] });
                                        data[index2[1]] = filteredData2[0];
                                    }
                                    catch
                                    {
                                    }
                                }
                                else if (ShimmerDevice.IsDefaultEMGConfigurationEnabled())
                                {
                                    try
                                    {
                                        index2[0] = objectCluster.GetIndex(Shimmer3Configuration.SignalNames.EXG2_CH1, ShimmerConfiguration.SignalFormats.CAL);
                                        double[] filteredData1 = HPF_Exg2Ch1.filterData(new double[] { data[index2[0]] });
                                        data[index2[0]] = filteredData1[0];
                                        index2[1] = objectCluster.GetIndex(Shimmer3Configuration.SignalNames.EXG2_CH2, ShimmerConfiguration.SignalFormats.CAL);
                                        double[] filteredData2 = HPF_Exg2Ch2.filterData(new double[] { data[index2[1]] });
                                        data[index2[1]] = filteredData2[0];
                                    }
                                    catch
                                    {

                                    }
                                }
                                else
                                {
                                    try
                                    {
                                        index2[0] = objectCluster.GetIndex(Shimmer3Configuration.SignalNames.EXG2_CH1_16BIT, ShimmerConfiguration.SignalFormats.CAL);
                                        double[] filteredData1 = HPF_Exg2Ch1.filterData(new double[] { data[index2[0]] });
                                        data[index2[0]] = filteredData1[0];
                                        index2[1] = objectCluster.GetIndex(Shimmer3Configuration.SignalNames.EXG2_CH2_16BIT, ShimmerConfiguration.SignalFormats.CAL);
                                        double[] filteredData2 = HPF_Exg2Ch2.filterData(new double[] { data[index2[1]] });
                                        data[index2[1]] = filteredData2[0];
                                    }
                                    catch
                                    {
                                    }
                                }
                            }
                        }
                        if (EnableBSF_49_51HZ || EnableBSF_59_61HZ)
                        {
                            int[] index1 = new int[4];
                            int[] index2 = new int[4];
                            if ((ShimmerDevice.GetEnabledSensors() & (int)ShimmerBluetooth.SensorBitmapShimmer3.SENSOR_EXG1_24BIT) > 0)
                            {
                                if (ShimmerDevice.IsDefaultECGConfigurationEnabled())
                                {
                                    try
                                    {
                                        index1[0] = objectCluster.GetIndex(Shimmer3Configuration.SignalNames.ECG_LL_RA, ShimmerConfiguration.SignalFormats.CAL);
                                        double[] filteredData1 = BSF_Exg1Ch1.filterData(new double[] { data[index1[0]] });
                                        data[index1[0]] = filteredData1[0];
                                        index1[1] = objectCluster.GetIndex(Shimmer3Configuration.SignalNames.ECG_LA_RA, ShimmerConfiguration.SignalFormats.CAL);
                                        double[] filteredData2 = BSF_Exg1Ch2.filterData(new double[] { data[index1[1]] });
                                        data[index1[1]] = filteredData2[0];
                                    }
                                    catch
                                    {
                                    }
                                }
                                else if (ShimmerDevice.IsDefaultEMGConfigurationEnabled())
                                {
                                    try
                                    {
                                        index1[0] = objectCluster.GetIndex(Shimmer3Configuration.SignalNames.EMG_CH1, ShimmerConfiguration.SignalFormats.CAL);
                                        double[] filteredData1 = BSF_Exg1Ch1.filterData(new double[] { data[index1[0]] });
                                        data[index1[0]] = filteredData1[0];
                                        index1[1] = objectCluster.GetIndex(Shimmer3Configuration.SignalNames.EMG_CH2, ShimmerConfiguration.SignalFormats.CAL);
                                        double[] filteredData2 = BSF_Exg1Ch2.filterData(new double[] { data[index1[1]] });
                                        data[index1[1]] = filteredData2[0];
                                    }
                                    catch
                                    {
                                    }
                                }
                                else
                                {
                                    try
                                    {
                                        index1[0] = objectCluster.GetIndex(Shimmer3Configuration.SignalNames.EXG1_CH1, ShimmerConfiguration.SignalFormats.CAL);
                                        double[] filteredData1 = BSF_Exg1Ch1.filterData(new double[] { data[index1[0]] });
                                        data[index1[0]] = filteredData1[0];
                                        index1[1] = objectCluster.GetIndex(Shimmer3Configuration.SignalNames.EXG1_CH2, ShimmerConfiguration.SignalFormats.CAL);
                                        double[] filteredData2 = BSF_Exg1Ch2.filterData(new double[] { data[index1[1]] });
                                        data[index1[1]] = filteredData2[0];
                                    }
                                    catch
                                    {
                                    }
                                }
                            }
                            if ((ShimmerDevice.GetEnabledSensors() & (int)ShimmerBluetooth.SensorBitmapShimmer3.SENSOR_EXG2_24BIT) > 0)
                            {
                                if (ShimmerDevice.IsDefaultECGConfigurationEnabled())
                                {
                                    try
                                    {
                                        index2[0] = objectCluster.GetIndex(Shimmer3Configuration.SignalNames.EXG2_CH1, ShimmerConfiguration.SignalFormats.CAL);
                                        double[] filteredData1 = BSF_Exg2Ch1.filterData(new double[] { data[index2[0]] });
                                        data[index2[0]] = filteredData1[0];
                                        index2[1] = objectCluster.GetIndex(Shimmer3Configuration.SignalNames.ECG_VX_RL, ShimmerConfiguration.SignalFormats.CAL);
                                        double[] filteredData2 = BSF_Exg2Ch2.filterData(new double[] { data[index2[1]] });
                                        data[index2[1]] = filteredData2[0];
                                    }
                                    catch
                                    {
                                    }
                                }
                                else if (ShimmerDevice.IsDefaultEMGConfigurationEnabled())
                                {
                                    try
                                    {
                                        index2[0] = objectCluster.GetIndex(Shimmer3Configuration.SignalNames.EXG2_CH1, ShimmerConfiguration.SignalFormats.CAL);
                                        double[] filteredData1 = BSF_Exg2Ch1.filterData(new double[] { data[index2[0]] });
                                        data[index2[0]] = filteredData1[0];
                                        index2[1] = objectCluster.GetIndex(Shimmer3Configuration.SignalNames.EXG2_CH2, ShimmerConfiguration.SignalFormats.CAL);
                                        double[] filteredData2 = BSF_Exg2Ch2.filterData(new double[] { data[index2[1]] });
                                        data[index2[1]] = filteredData2[0];
                                    }
                                    catch
                                    {
                                    }
                                }
                                else
                                {
                                    try
                                    {
                                        index2[0] = objectCluster.GetIndex(Shimmer3Configuration.SignalNames.EXG2_CH1, ShimmerConfiguration.SignalFormats.CAL);
                                        double[] filteredData1 = BSF_Exg2Ch1.filterData(new double[] { data[index2[0]] });
                                        data[index2[0]] = filteredData1[0];
                                        index2[1] = objectCluster.GetIndex(Shimmer3Configuration.SignalNames.EXG2_CH2, ShimmerConfiguration.SignalFormats.CAL);
                                        double[] filteredData2 = BSF_Exg2Ch2.filterData(new double[] { data[index2[1]] });
                                        data[index2[1]] = filteredData2[0];
                                    }
                                    catch
                                    {
                                    }
                                }
                            }

                            if ((ShimmerDevice.GetEnabledSensors() & (int)ShimmerBluetooth.SensorBitmapShimmer3.SENSOR_EXG1_16BIT) > 0)
                            {
                                if (ShimmerDevice.IsDefaultECGConfigurationEnabled())
                                {
                                    try
                                    {
                                        index1[0] = objectCluster.GetIndex(Shimmer3Configuration.SignalNames.ECG_LL_RA, ShimmerConfiguration.SignalFormats.CAL);
                                        double[] filteredData1 = BSF_Exg1Ch1.filterData(new double[] { data[index1[0]] });
                                        data[index1[0]] = filteredData1[0];
                                        index1[1] = objectCluster.GetIndex(Shimmer3Configuration.SignalNames.ECG_LA_RA, ShimmerConfiguration.SignalFormats.CAL);
                                        double[] filteredData2 = BSF_Exg1Ch2.filterData(new double[] { data[index1[1]] });
                                        data[index1[1]] = filteredData2[0];
                                    }
                                    catch
                                    {
                                    }
                                }
                                else if (ShimmerDevice.IsDefaultEMGConfigurationEnabled())
                                {
                                    try
                                    {
                                        index1[0] = objectCluster.GetIndex(Shimmer3Configuration.SignalNames.EMG_CH1, ShimmerConfiguration.SignalFormats.CAL);
                                        double[] filteredData1 = BSF_Exg1Ch1.filterData(new double[] { data[index1[0]] });
                                        data[index1[0]] = filteredData1[0];
                                        index1[1] = objectCluster.GetIndex(Shimmer3Configuration.SignalNames.EMG_CH2, ShimmerConfiguration.SignalFormats.CAL);
                                        double[] filteredData2 = BSF_Exg1Ch2.filterData(new double[] { data[index1[1]] });
                                        data[index1[1]] = filteredData2[0];
                                    }
                                    catch
                                    {
                                    }
                                }
                                else
                                {
                                    try
                                    {
                                        index1[0] = objectCluster.GetIndex(Shimmer3Configuration.SignalNames.EXG1_CH1_16BIT, ShimmerConfiguration.SignalFormats.CAL);
                                        double[] filteredData1 = BSF_Exg1Ch1.filterData(new double[] { data[index1[0]] });
                                        data[index1[0]] = filteredData1[0];
                                        index1[1] = objectCluster.GetIndex(Shimmer3Configuration.SignalNames.EXG1_CH2_16BIT, ShimmerConfiguration.SignalFormats.CAL);
                                        double[] filteredData2 = BSF_Exg1Ch2.filterData(new double[] { data[index1[1]] });
                                        data[index1[1]] = filteredData2[0];
                                    }
                                    catch
                                    {
                                    }
                                }
                            }
                            if ((ShimmerDevice.GetEnabledSensors() & (int)ShimmerBluetooth.SensorBitmapShimmer3.SENSOR_EXG2_16BIT) > 0)
                            {
                                if (ShimmerDevice.IsDefaultECGConfigurationEnabled())
                                {
                                    try
                                    {
                                        index2[0] = objectCluster.GetIndex(Shimmer3Configuration.SignalNames.EXG2_CH1, ShimmerConfiguration.SignalFormats.CAL);
                                        double[] filteredData1 = BSF_Exg2Ch1.filterData(new double[] { data[index2[0]] });
                                        data[index2[0]] = filteredData1[0];
                                        index2[1] = objectCluster.GetIndex(Shimmer3Configuration.SignalNames.ECG_VX_RL, ShimmerConfiguration.SignalFormats.CAL);
                                        double[] filteredData2 = BSF_Exg2Ch2.filterData(new double[] { data[index2[1]] });
                                        data[index2[1]] = filteredData2[0];
                                    }
                                    catch
                                    {
                                    }
                                }
                                else if (ShimmerDevice.IsDefaultEMGConfigurationEnabled())
                                {
                                    try
                                    {
                                        index2[0] = objectCluster.GetIndex(Shimmer3Configuration.SignalNames.EXG2_CH1, ShimmerConfiguration.SignalFormats.CAL);
                                        double[] filteredData1 = BSF_Exg2Ch1.filterData(new double[] { data[index2[0]] });
                                        data[index2[0]] = filteredData1[0];
                                        index2[1] = objectCluster.GetIndex(Shimmer3Configuration.SignalNames.EXG2_CH2, ShimmerConfiguration.SignalFormats.CAL);
                                        double[] filteredData2 = BSF_Exg2Ch2.filterData(new double[] { data[index2[1]] });
                                        data[index2[1]] = filteredData2[0];
                                    }
                                    catch
                                    {
                                    }
                                }
                                else
                                {
                                    try
                                    {
                                        index2[0] = objectCluster.GetIndex(Shimmer3Configuration.SignalNames.EXG2_CH1_16BIT, ShimmerConfiguration.SignalFormats.CAL);
                                        double[] filteredData1 = BSF_Exg2Ch1.filterData(new double[] { data[index2[0]] });
                                        data[index2[0]] = filteredData1[0];
                                        index2[1] = objectCluster.GetIndex(Shimmer3Configuration.SignalNames.EXG2_CH2_16BIT, ShimmerConfiguration.SignalFormats.CAL);
                                        double[] filteredData2 = BSF_Exg2Ch2.filterData(new double[] { data[index2[1]] });
                                        data[index2[1]] = filteredData2[0];
                                    }
                                    catch
                                    {
                                    }
                                }
                            }
                        }
                        if (EnableNQF)
                        {
                            int[] index1 = new int[4];
                            int[] index2 = new int[4];
                            if ((ShimmerDevice.GetEnabledSensors() & (int)ShimmerBluetooth.SensorBitmapShimmer3.SENSOR_EXG1_24BIT) > 0)
                            {
                                if (ShimmerDevice.IsDefaultECGConfigurationEnabled())
                                {
                                    try
                                    {
                                        index1[0] = objectCluster.GetIndex(Shimmer3Configuration.SignalNames.ECG_LL_RA, ShimmerConfiguration.SignalFormats.CAL);
                                        double[] filteredData1 = NQF_Exg1Ch1.filterData(new double[] { data[index1[0]] });
                                        data[index1[0]] = filteredData1[0];
                                        index1[1] = objectCluster.GetIndex(Shimmer3Configuration.SignalNames.ECG_LA_RA, ShimmerConfiguration.SignalFormats.CAL);
                                        double[] filteredData2 = NQF_Exg1Ch2.filterData(new double[] { data[index1[1]] });
                                        data[index1[1]] = filteredData2[0];
                                    }
                                    catch
                                    {
                                    }
                                }
                                else if (ShimmerDevice.IsDefaultEMGConfigurationEnabled())
                                {
                                    try
                                    {
                                        index1[0] = objectCluster.GetIndex(Shimmer3Configuration.SignalNames.EMG_CH1, ShimmerConfiguration.SignalFormats.CAL);
                                        double[] filteredData1 = NQF_Exg1Ch1.filterData(new double[] { data[index1[0]] });
                                        data[index1[0]] = filteredData1[0];
                                        index1[1] = objectCluster.GetIndex(Shimmer3Configuration.SignalNames.EMG_CH2, ShimmerConfiguration.SignalFormats.CAL);
                                        double[] filteredData2 = NQF_Exg1Ch2.filterData(new double[] { data[index1[1]] });
                                        data[index1[1]] = filteredData2[0];
                                    }
                                    catch
                                    {
                                    }
                                }
                                else
                                {
                                    try
                                    {
                                        index1[0] = objectCluster.GetIndex(Shimmer3Configuration.SignalNames.EXG1_CH1, ShimmerConfiguration.SignalFormats.CAL);
                                        double[] filteredData1 = NQF_Exg1Ch1.filterData(new double[] { data[index1[0]] });
                                        data[index1[0]] = filteredData1[0];
                                        index1[1] = objectCluster.GetIndex(Shimmer3Configuration.SignalNames.EXG1_CH2, ShimmerConfiguration.SignalFormats.CAL);
                                        double[] filteredData2 = NQF_Exg1Ch2.filterData(new double[] { data[index1[1]] });
                                        data[index1[1]] = filteredData2[0];
                                    }
                                    catch
                                    {
                                    }
                                }
                            }
                            if ((ShimmerDevice.GetEnabledSensors() & (int)ShimmerBluetooth.SensorBitmapShimmer3.SENSOR_EXG2_24BIT) > 0)
                            {
                                if (ShimmerDevice.IsDefaultECGConfigurationEnabled())
                                {
                                    try
                                    {
                                        index2[0] = objectCluster.GetIndex(Shimmer3Configuration.SignalNames.EXG2_CH1, ShimmerConfiguration.SignalFormats.CAL);
                                        double[] filteredData1 = NQF_Exg2Ch1.filterData(new double[] { data[index2[0]] });
                                        data[index2[0]] = filteredData1[0];
                                        index2[1] = objectCluster.GetIndex(Shimmer3Configuration.SignalNames.ECG_VX_RL, ShimmerConfiguration.SignalFormats.CAL);
                                        double[] filteredData2 = NQF_Exg2Ch2.filterData(new double[] { data[index2[1]] });
                                        data[index2[1]] = filteredData2[0];
                                    }
                                    catch
                                    {
                                    }
                                }
                                else if (ShimmerDevice.IsDefaultEMGConfigurationEnabled())
                                {
                                    try
                                    {
                                        index2[0] = objectCluster.GetIndex(Shimmer3Configuration.SignalNames.EXG2_CH1, ShimmerConfiguration.SignalFormats.CAL);
                                        double[] filteredData1 = NQF_Exg2Ch1.filterData(new double[] { data[index2[0]] });
                                        data[index2[0]] = filteredData1[0];
                                        index2[1] = objectCluster.GetIndex(Shimmer3Configuration.SignalNames.EXG2_CH2, ShimmerConfiguration.SignalFormats.CAL);
                                        double[] filteredData2 = NQF_Exg2Ch2.filterData(new double[] { data[index2[1]] });
                                        data[index2[1]] = filteredData2[0];
                                    }
                                    catch
                                    {
                                    }
                                }
                                else
                                {
                                    try
                                    {
                                        index2[0] = objectCluster.GetIndex(Shimmer3Configuration.SignalNames.EXG2_CH1, ShimmerConfiguration.SignalFormats.CAL);
                                        double[] filteredData1 = NQF_Exg2Ch1.filterData(new double[] { data[index2[0]] });
                                        data[index2[0]] = filteredData1[0];
                                        index2[1] = objectCluster.GetIndex(Shimmer3Configuration.SignalNames.EXG2_CH2, ShimmerConfiguration.SignalFormats.CAL);
                                        double[] filteredData2 = NQF_Exg2Ch2.filterData(new double[] { data[index2[1]] });
                                        data[index2[1]] = filteredData2[0];
                                    }
                                    catch
                                    {
                                    }
                                }
                            }

                            if ((ShimmerDevice.GetEnabledSensors() & (int)ShimmerBluetooth.SensorBitmapShimmer3.SENSOR_EXG1_16BIT) > 0)
                            {
                                if (ShimmerDevice.IsDefaultECGConfigurationEnabled())
                                {
                                    try
                                    {
                                        index1[0] = objectCluster.GetIndex(Shimmer3Configuration.SignalNames.ECG_LL_RA, ShimmerConfiguration.SignalFormats.CAL);
                                        double[] filteredData1 = NQF_Exg1Ch1.filterData(new double[] { data[index1[0]] });
                                        data[index1[0]] = filteredData1[0];
                                        index1[1] = objectCluster.GetIndex(Shimmer3Configuration.SignalNames.ECG_LA_RA, ShimmerConfiguration.SignalFormats.CAL);
                                        double[] filteredData2 = NQF_Exg1Ch2.filterData(new double[] { data[index1[1]] });
                                        data[index1[1]] = filteredData2[0];
                                    }
                                    catch
                                    {
                                    }
                                }
                                else if (ShimmerDevice.IsDefaultEMGConfigurationEnabled())
                                {
                                    try
                                    {
                                        index1[0] = objectCluster.GetIndex(Shimmer3Configuration.SignalNames.EMG_CH1, ShimmerConfiguration.SignalFormats.CAL);
                                        double[] filteredData1 = NQF_Exg1Ch1.filterData(new double[] { data[index1[0]] });
                                        data[index1[0]] = filteredData1[0];
                                        index1[1] = objectCluster.GetIndex(Shimmer3Configuration.SignalNames.EMG_CH2, ShimmerConfiguration.SignalFormats.CAL);
                                        double[] filteredData2 = NQF_Exg1Ch2.filterData(new double[] { data[index1[1]] });
                                        data[index1[1]] = filteredData2[0];
                                    }
                                    catch
                                    {
                                    }
                                }
                                else
                                {
                                    try
                                    {
                                        index1[0] = objectCluster.GetIndex(Shimmer3Configuration.SignalNames.EXG1_CH1_16BIT, ShimmerConfiguration.SignalFormats.CAL);
                                        double[] filteredData1 = NQF_Exg1Ch1.filterData(new double[] { data[index1[0]] });
                                        data[index1[0]] = filteredData1[0];
                                        index1[1] = objectCluster.GetIndex(Shimmer3Configuration.SignalNames.EXG1_CH2_16BIT, ShimmerConfiguration.SignalFormats.CAL);
                                        double[] filteredData2 = NQF_Exg1Ch2.filterData(new double[] { data[index1[1]] });
                                        data[index1[1]] = filteredData2[0];
                                    }
                                    catch
                                    {
                                    }
                                }
                            }
                            if ((ShimmerDevice.GetEnabledSensors() & (int)ShimmerBluetooth.SensorBitmapShimmer3.SENSOR_EXG2_16BIT) > 0)
                            {
                                if (ShimmerDevice.IsDefaultECGConfigurationEnabled())
                                {
                                    try
                                    {
                                        index2[0] = objectCluster.GetIndex(Shimmer3Configuration.SignalNames.EXG2_CH1, ShimmerConfiguration.SignalFormats.CAL);
                                        double[] filteredData1 = NQF_Exg2Ch1.filterData(new double[] { data[index2[0]] });
                                        data[index2[0]] = filteredData1[0];
                                        index2[1] = objectCluster.GetIndex(Shimmer3Configuration.SignalNames.ECG_VX_RL, ShimmerConfiguration.SignalFormats.CAL);
                                        double[] filteredData2 = NQF_Exg2Ch2.filterData(new double[] { data[index2[1]] });
                                        data[index2[1]] = filteredData2[0];
                                    }
                                    catch
                                    {
                                    }
                                }
                                else if (ShimmerDevice.IsDefaultEMGConfigurationEnabled())
                                {
                                    try
                                    {
                                        index2[0] = objectCluster.GetIndex(Shimmer3Configuration.SignalNames.EXG2_CH1, ShimmerConfiguration.SignalFormats.CAL);
                                        double[] filteredData1 = NQF_Exg2Ch1.filterData(new double[] { data[index2[0]] });
                                        data[index2[0]] = filteredData1[0];
                                        index2[1] = objectCluster.GetIndex(Shimmer3Configuration.SignalNames.EXG2_CH2, ShimmerConfiguration.SignalFormats.CAL);
                                        double[] filteredData2 = NQF_Exg2Ch2.filterData(new double[] { data[index2[1]] });
                                        data[index2[1]] = filteredData2[0];
                                    }
                                    catch
                                    {
                                    }
                                }
                                else
                                {
                                    try
                                    {
                                        index2[0] = objectCluster.GetIndex(Shimmer3Configuration.SignalNames.EXG2_CH1_16BIT, ShimmerConfiguration.SignalFormats.CAL);
                                        double[] filteredData1 = NQF_Exg2Ch1.filterData(new double[] { data[index2[0]] });
                                        data[index2[0]] = filteredData1[0];
                                        index2[1] = objectCluster.GetIndex(Shimmer3Configuration.SignalNames.EXG2_CH2_16BIT, ShimmerConfiguration.SignalFormats.CAL);
                                        double[] filteredData2 = NQF_Exg2Ch2.filterData(new double[] { data[index2[1]] });
                                        data[index2[1]] = filteredData2[0];
                                    }
                                    catch
                                    {
                                    }
                                }
                            }
                        }
                    }


                    if (EnableECGtoHRConversion)
                    {
                        //ECG-HR Conversion
                        int index = -1;
                        if (ShimmerDevice.GetShimmerVersion() == (int)ShimmerBluetooth.ShimmerVersion.SHIMMER3 ||
                            ShimmerDevice.GetShimmerVersion() == (int)ShimmerBluetooth.ShimmerVersion.SHIMMER3R)
                        {
                            index = objectCluster.GetIndex(ECGSignalName, ShimmerConfiguration.SignalFormats.RAW);
                        }
                        else
                        {
                            index = objectCluster.GetIndex(ECGSignalName, ShimmerConfiguration.SignalFormats.RAW);
                        }
                        if (index != -1)
                        {
                            double ecgData = -1;
                            ecgData = data[index];
                            double calTimestamp = objectCluster.GetData(ShimmerConfiguration.SignalNames.TIMESTAMP, ShimmerConfiguration.SignalFormats.CAL).GetData();
                            ECGToHRAdaptive.DataECGToHROutput hrdata = ECGtoHRAdaptive.ecgToHrConversion(ecgData, calTimestamp);
                            names.Add("Heart Rate ECG");
                            formats.Add("");
                            units.Add("Beats/min");
                            data.Add(hrdata.getHeartRate());
                            names.Add("IBI ECG");
                            formats.Add("");
                            units.Add("ms");
                            data.Add(hrdata.getInterBeatInterval());
                        }
                    }
                    /*
                    if (UserControlExgConfig.comboBoxLeadOffDetection != null) // method to extract ExG status channels if ExG lead off detection is enabled
                    {
                    }
                    */
                    if (((ShimmerDevice.GetEnabledSensors() & (int)ShimmerBluetooth.SensorBitmapShimmer3.SENSOR_EXG1_16BIT) > 0
                        && (ShimmerDevice.GetEnabledSensors() & (int)ShimmerBluetooth.SensorBitmapShimmer3.SENSOR_EXG2_16BIT) > 0) ||
                        (((ShimmerDevice.GetEnabledSensors() & (int)ShimmerBluetooth.SensorBitmapShimmer3.SENSOR_EXG1_24BIT) > 0
                        && (ShimmerDevice.GetEnabledSensors() & (int)ShimmerBluetooth.SensorBitmapShimmer3.SENSOR_EXG2_24BIT) > 0)))
                    {

                        if ((ShimmerDevice.GetShimmerVersion() == (int)ShimmerBluetooth.ShimmerVersion.SHIMMER3 || ShimmerDevice.GetShimmerVersion() == (int)ShimmerBluetooth.ShimmerVersion.SHIMMER3R) && (ShimmerDevice.IsDefaultECGConfigurationEnabled()) && (((ShimmerDevice.GetEXG1RegisterByte(1) & 0x40) != 0) && ((ShimmerDevice.GetEXG2RegisterByte(1) & 0x40) != 0)))
                        {
                            try
                            {
                                int ExG1statusIndex = objectCluster.GetIndex(Shimmer3Configuration.SignalNames.EXG1_STATUS, ShimmerConfiguration.SignalFormats.RAW);
                                int ExG2statusIndex = objectCluster.GetIndex(Shimmer3Configuration.SignalNames.EXG2_STATUS, ShimmerConfiguration.SignalFormats.RAW);
                                double ExG1status = objectCluster.GetData(Shimmer3Configuration.SignalNames.EXG1_STATUS, ShimmerConfiguration.SignalFormats.RAW).GetData();
                                double ExG2status = objectCluster.GetData(Shimmer3Configuration.SignalNames.EXG2_STATUS, ShimmerConfiguration.SignalFormats.RAW).GetData();
                                if (ExGLeadOffCounter < ExGLeadOffCounterSize) // call updateExGLeadOff code once every second
                                {
                                    ExGLeadOffCounter++;
                                }
                                else
                                {
                                    ExGLeadOffCounter = 0;
                                    updateExGLeadOffTextBoxes(ExG1status, ExG2status);
                                }
                            }
                            catch
                            {
                            }
                        }
                    }

                    //Set up checkboxes and graph first time
                    /*
                    if (!ShimmerIdSetup.Contains(objectCluster.GetShimmerID()))
                    {
                        List<String> signalNamesandFormats = new List<String>();
                        List<String> signalNamesandFormatsRaw = new List<String>();
                        List<String> signalNamesandFormatsCal = new List<String>();
                        for (int i = 0; i < names.Count; i++)
                        {
                            //RAW and CAL signals separated to allow for checkbox set up - RAW on LHS, CAL on RHS.
                            if (formats[i].Equals("RAW"))
                            {
                                signalNamesandFormatsRaw.Add(names[i] + " " + formats[i]);
                                StreamingSignalNamesRaw.Add(names[i] + " " + formats[i]);
                            }
                            else
                            {
                                signalNamesandFormatsCal.Add(names[i] + " " + formats[i]);
                                StreamingSignalNamesCal.Add(names[i] + " " + formats[i]);
                            }
                            signalNamesandFormats.Add(names[i] + " " + formats[i]);
                        }
                        SetupCheckboxesGroup1(objectCluster.GetShimmerID(), signalNamesandFormatsRaw.ToArray(), signalNamesandFormatsCal.ToArray());
                        SetupCheckboxesGroup2(objectCluster.GetShimmerID(), signalNamesandFormatsRaw.ToArray(), signalNamesandFormatsCal.ToArray());
                        SetupCheckboxesGroup3(objectCluster.GetShimmerID(), signalNamesandFormatsRaw.ToArray(), signalNamesandFormatsCal.ToArray());
                        SetupGraph1(data.Count, signalNamesandFormats.ToArray());
                        SetupGraph2(data.Count, signalNamesandFormats.ToArray());
                        SetupGraph3(data.Count, signalNamesandFormats.ToArray());
                        ShimmerIdSetup.Add(objectCluster.GetShimmerID());
                    }
                    */

                    if (FirstTime)
                    {
                        List<String> fnames = new List<String>();
                        int icount = 0;
                        foreach (String s in names)
                        {
                            fnames.Add(s + " " + formats[icount] + " (" + units[icount] + ")");
                            icount++;
                        }
                        ShowChannelLabels(fnames);
                        ShowChannelTextBoxes(fnames.Count);

                        //checkboxes

                        List<String> signalNamesandFormats = new List<String>();
                        List<String> signalNamesandFormatsRaw = new List<String>();
                        List<String> signalNamesandFormatsCal = new List<String>();
                        for (int i = 0; i < names.Count; i++)
                        {
                            //RAW and CAL signals separated to allow for checkbox set up - RAW on LHS, CAL on RHS.
                            if (formats[i].Equals(ShimmerConfiguration.SignalFormats.RAW))
                            {
                                signalNamesandFormatsRaw.Add(names[i] + " " + formats[i]);
                                StreamingSignalNamesRaw.Add(names[i] + " " + formats[i]);
                            }
                            else
                            {
                                signalNamesandFormatsCal.Add(names[i] + " " + formats[i]);
                                StreamingSignalNamesCal.Add(names[i] + " " + formats[i]);
                            }
                            signalNamesandFormats.Add(names[i] + " " + formats[i]);
                        }
                        SetupCheckboxesGroup1(objectCluster.GetShimmerID(), signalNamesandFormatsRaw.ToArray(), signalNamesandFormatsCal.ToArray());
                        SetupCheckboxesGroup2(objectCluster.GetShimmerID(), signalNamesandFormatsRaw.ToArray(), signalNamesandFormatsCal.ToArray());
                        SetupCheckboxesGroup3(objectCluster.GetShimmerID(), signalNamesandFormatsRaw.ToArray(), signalNamesandFormatsCal.ToArray());
                        SetupGraph1(data.Count, signalNamesandFormats.ToArray());
                        SetupGraph2(data.Count, signalNamesandFormats.ToArray());
                        SetupGraph3(data.Count, signalNamesandFormats.ToArray());
                        ShimmerIdSetup.Add(objectCluster.GetShimmerID());

                        FirstTime = false;
                    }


                    //Plot
                    DrawingData(names.ToArray(), formats.ToArray(), data.ToArray());
                    ShowTBcount++;
                    if (ShowTBcount % Math.Truncate(ShimmerDevice.GetSamplingRate() / 5) == 0)
                    {
                        UpdateChannelTextBoxes(data);
                    }

                    //Write to file
                    if (WriteToFile != null && ToolStripMenuItemSaveToCSV.Checked != false)
                    {
                        WriteToFile.WriteData(objectCluster);
                        //ToolStripMenuItemSaveToCSV.Checked = false
                    }
                    break;
                case (int)ShimmerBluetooth.ShimmerIdentifier.MSG_IDENTIFIER_PACKET_RECEPTION_RATE:
                    double prr = (double)eventArgs.getObject();
                    count++;
                    if (count % Math.Truncate(ShimmerDevice.GetSamplingRate()) == 0)
                    {
                        SetLabelText("Packet Reception Rate: " + Math.Truncate(prr).ToString() + "%");
                    }
                    break;
            }
        }

        delegate void SetTextCallback(string text);

        private void SetButtonText(string text)
        {
            // InvokeRequired required compares the thread ID of the
            // calling thread to the thread ID of the creating thread.
            // If these threads are different, it returns true.
            if (this.buttonStreamandLog.InvokeRequired)
            {
                SetTextCallback d = new SetTextCallback(SetButtonText);
                this.Invoke(d, new object[] { text });
            }
            else
            {
                this.buttonStreamandLog.Text = text;
            }
        }

        delegate void SetLabelCallback(string text);

        private void SetLabelText(string text)
        {
            // InvokeRequired required compares the thread ID of the
            // calling thread to the thread ID of the creating thread.
            // If these threads are different, it returns true.
            if (this.labelPRR.InvokeRequired)
            {
                SetLabelCallback d = new SetLabelCallback(SetLabelText);
                this.Invoke(d, new object[] { text });
            }
            else
            {
                this.labelPRR.Text = text;
            }
        }

        public void setTheOrientation()
        {
            SetOrientation = true;
        }

        internal void resetTheOrientation()
        {
            SetOriMatrix = new double[,] { { 1, 0, 0 }, { 0, -1, 0 }, { 0, 0, -1 } };
        }

        private double[,] matrixmultiplication(double[,] a, double[,] b)
        {
            int aRows = a.GetLength(0),
                aColumns = a.GetLength(1),
                bRows = b.GetLength(0),
                bColumns = b.GetLength(1);
            double[,] resultant = new double[aRows, bColumns];

            for (int i = 0; i < aRows; i++)
            { // aRow
                for (int j = 0; j < bColumns; j++)
                { // bColumn
                    for (int k = 0; k < aColumns; k++)
                    { // aColumn
                        resultant[i, j] += a[i, k] * b[k, j];
                    }
                }
            }

            return resultant;
        }

        private double[,] matrixinverse3x3(double[,] data)
        {
            double a, b, c, d, e, f, g, h, i;
            a = data[0, 0];
            b = data[0, 1];
            c = data[0, 2];
            d = data[1, 0];
            e = data[1, 1];
            f = data[1, 2];
            g = data[2, 0];
            h = data[2, 1];
            i = data[2, 2];
            //
            double deter = a * e * i + b * f * g + c * d * h - c * e * g - b * d * i - a * f * h;
            double[,] answer = new double[3, 3];
            answer[0, 0] = (1 / deter) * (e * i - f * h);
            answer[0, 1] = (1 / deter) * (c * h - b * i);
            answer[0, 2] = (1 / deter) * (b * f - c * e);
            answer[1, 0] = (1 / deter) * (f * g - d * i);
            answer[1, 1] = (1 / deter) * (a * i - c * g);
            answer[1, 2] = (1 / deter) * (c * d - a * f);
            answer[2, 0] = (1 / deter) * (d * h - e * g);
            answer[2, 1] = (1 / deter) * (g * b - a * h);
            answer[2, 2] = (1 / deter) * (a * e - b * d);
            return answer;
        }

        private void SetupFilters()
        {
            //Create NQ Filters
            if ((((ShimmerDevice.GetEnabledSensors() & (int)ShimmerBluetooth.SensorBitmapShimmer3.SENSOR_EXG1_24BIT) > 0) || ((ShimmerDevice.GetEnabledSensors() & (int)ShimmerBluetooth.SensorBitmapShimmer3.SENSOR_EXG1_16BIT) > 0)) && EnableNQF)
            {
                double cutoff = ShimmerDevice.GetSamplingRate() / 2;
                NQF_Exg1Ch1 = new Filter(Filter.LOW_PASS, ShimmerDevice.GetSamplingRate(), new double[] { cutoff });
                NQF_Exg1Ch2 = new Filter(Filter.LOW_PASS, ShimmerDevice.GetSamplingRate(), new double[] { cutoff });

            }

            if ((((ShimmerDevice.GetEnabledSensors() & (int)ShimmerBluetooth.SensorBitmapShimmer3.SENSOR_EXG2_24BIT) > 0) || ((ShimmerDevice.GetEnabledSensors() & (int)ShimmerBluetooth.SensorBitmapShimmer3.SENSOR_EXG2_16BIT) > 0)) && EnableNQF)
            {
                double cutoff = ShimmerDevice.GetSamplingRate() / 2;
                NQF_Exg2Ch1 = new Filter(Filter.LOW_PASS, ShimmerDevice.GetSamplingRate(), new double[] { cutoff });
                NQF_Exg2Ch2 = new Filter(Filter.LOW_PASS, ShimmerDevice.GetSamplingRate(), new double[] { cutoff });

            }

            //Create High Pass Filters for EXG

            if ((((ShimmerDevice.GetEnabledSensors() & (int)ShimmerBluetooth.SensorBitmapShimmer3.SENSOR_EXG1_24BIT) > 0) || ((ShimmerDevice.GetEnabledSensors() & (int)ShimmerBluetooth.SensorBitmapShimmer3.SENSOR_EXG1_16BIT) > 0)) && EnableHPF_0_05HZ)
            {
                HPF_Exg1Ch1 = new Filter(Filter.HIGH_PASS, ShimmerDevice.GetSamplingRate(), new double[] { 0.05 });
                HPF_Exg1Ch2 = new Filter(Filter.HIGH_PASS, ShimmerDevice.GetSamplingRate(), new double[] { 0.05 });
            }

            if ((((ShimmerDevice.GetEnabledSensors() & (int)ShimmerBluetooth.SensorBitmapShimmer3.SENSOR_EXG2_24BIT) > 0) || ((ShimmerDevice.GetEnabledSensors() & (int)ShimmerBluetooth.SensorBitmapShimmer3.SENSOR_EXG2_16BIT) > 0)) && EnableHPF_0_05HZ)
            {
                HPF_Exg2Ch1 = new Filter(Filter.HIGH_PASS, ShimmerDevice.GetSamplingRate(), new double[] { 0.05 });
                HPF_Exg2Ch2 = new Filter(Filter.HIGH_PASS, ShimmerDevice.GetSamplingRate(), new double[] { 0.05 });
            }
            if ((((ShimmerDevice.GetEnabledSensors() & (int)ShimmerBluetooth.SensorBitmapShimmer3.SENSOR_EXG1_24BIT) > 0) || ((ShimmerDevice.GetEnabledSensors() & (int)ShimmerBluetooth.SensorBitmapShimmer3.SENSOR_EXG1_16BIT) > 0)) && EnableHPF_0_5HZ)
            {
                HPF_Exg1Ch1 = new Filter(Filter.HIGH_PASS, ShimmerDevice.GetSamplingRate(), new double[] { 0.5 });
                HPF_Exg1Ch2 = new Filter(Filter.HIGH_PASS, ShimmerDevice.GetSamplingRate(), new double[] { 0.5 });
            }
            if ((((ShimmerDevice.GetEnabledSensors() & (int)ShimmerBluetooth.SensorBitmapShimmer3.SENSOR_EXG2_24BIT) > 0) || ((ShimmerDevice.GetEnabledSensors() & (int)ShimmerBluetooth.SensorBitmapShimmer3.SENSOR_EXG2_16BIT) > 0)) && EnableHPF_0_5HZ)
            {
                HPF_Exg2Ch1 = new Filter(Filter.HIGH_PASS, ShimmerDevice.GetSamplingRate(), new double[] { 0.5 });
                HPF_Exg2Ch2 = new Filter(Filter.HIGH_PASS, ShimmerDevice.GetSamplingRate(), new double[] { 0.5 });
            }
            if ((((ShimmerDevice.GetEnabledSensors() & (int)ShimmerBluetooth.SensorBitmapShimmer3.SENSOR_EXG1_24BIT) > 0) || ((ShimmerDevice.GetEnabledSensors() & (int)ShimmerBluetooth.SensorBitmapShimmer3.SENSOR_EXG1_16BIT) > 0)) && EnableHPF_5HZ)
            {
                HPF_Exg1Ch1 = new Filter(Filter.HIGH_PASS, ShimmerDevice.GetSamplingRate(), new double[] { 5 });
                HPF_Exg1Ch2 = new Filter(Filter.HIGH_PASS, ShimmerDevice.GetSamplingRate(), new double[] { 5 });
            }
            if ((((ShimmerDevice.GetEnabledSensors() & (int)ShimmerBluetooth.SensorBitmapShimmer3.SENSOR_EXG2_24BIT) > 0) || ((ShimmerDevice.GetEnabledSensors() & (int)ShimmerBluetooth.SensorBitmapShimmer3.SENSOR_EXG2_16BIT) > 0)) && EnableHPF_5HZ)
            {
                HPF_Exg2Ch1 = new Filter(Filter.HIGH_PASS, ShimmerDevice.GetSamplingRate(), new double[] { 5 });
                HPF_Exg2Ch2 = new Filter(Filter.HIGH_PASS, ShimmerDevice.GetSamplingRate(), new double[] { 5 });
            }

            //Create Band Stop Filters for EXG
            if ((((ShimmerDevice.GetEnabledSensors() & (int)ShimmerBluetooth.SensorBitmapShimmer3.SENSOR_EXG1_24BIT) > 0) || ((ShimmerDevice.GetEnabledSensors() & (int)ShimmerBluetooth.SensorBitmapShimmer3.SENSOR_EXG1_16BIT) > 0)) && EnableBSF_49_51HZ)
            {
                BSF_Exg1Ch1 = new Filter(Filter.BAND_STOP, ShimmerDevice.GetSamplingRate(), new double[] { 49, 51 });
                BSF_Exg1Ch2 = new Filter(Filter.BAND_STOP, ShimmerDevice.GetSamplingRate(), new double[] { 49, 51 });
            }
            if ((((ShimmerDevice.GetEnabledSensors() & (int)ShimmerBluetooth.SensorBitmapShimmer3.SENSOR_EXG2_24BIT) > 0) || ((ShimmerDevice.GetEnabledSensors() & (int)ShimmerBluetooth.SensorBitmapShimmer3.SENSOR_EXG2_16BIT) > 0)) && EnableBSF_49_51HZ)
            {
                BSF_Exg2Ch1 = new Filter(Filter.BAND_STOP, ShimmerDevice.GetSamplingRate(), new double[] { 49, 51 });
                BSF_Exg2Ch2 = new Filter(Filter.BAND_STOP, ShimmerDevice.GetSamplingRate(), new double[] { 49, 51 });
            }
            if ((((ShimmerDevice.GetEnabledSensors() & (int)ShimmerBluetooth.SensorBitmapShimmer3.SENSOR_EXG1_24BIT) > 0) || ((ShimmerDevice.GetEnabledSensors() & (int)ShimmerBluetooth.SensorBitmapShimmer3.SENSOR_EXG1_16BIT) > 0)) && EnableBSF_59_61HZ)
            {
                BSF_Exg1Ch1 = new Filter(Filter.BAND_STOP, ShimmerDevice.GetSamplingRate(), new double[] { 59, 61 });
                BSF_Exg1Ch2 = new Filter(Filter.BAND_STOP, ShimmerDevice.GetSamplingRate(), new double[] { 59, 61 });
            }
            if ((((ShimmerDevice.GetEnabledSensors() & (int)ShimmerBluetooth.SensorBitmapShimmer3.SENSOR_EXG2_24BIT) > 0) || ((ShimmerDevice.GetEnabledSensors() & (int)ShimmerBluetooth.SensorBitmapShimmer3.SENSOR_EXG2_16BIT) > 0)) && EnableBSF_59_61HZ)
            {
                BSF_Exg2Ch1 = new Filter(Filter.BAND_STOP, ShimmerDevice.GetSamplingRate(), new double[] { 59, 61 });
                BSF_Exg2Ch2 = new Filter(Filter.BAND_STOP, ShimmerDevice.GetSamplingRate(), new double[] { 59, 61 });
            }

            //PPG-HR Conversion
            if (EnablePPGtoHRConversion)
            {
                PPGtoHeartRateCalculation = new PPGToHRAlgorithm(ShimmerDevice.GetSamplingRate(), NumberOfHeartBeatsToAverage, TrainingPeriodPPG);
                LPF_PPG = new Filter(Filter.LOW_PASS, ShimmerDevice.GetSamplingRate(), new double[] { 5 });
                HPF_PPG = new Filter(Filter.HIGH_PASS, ShimmerDevice.GetSamplingRate(), new double[] { 0.5 });
            }

        }

        private void buttonStream_Click(object sender, EventArgs e)
        {
            //int[] a = new int [1];
            //a[2] = 3;
            FirstTime = true;
            labelPRR.Visible = true;

            SetupFilters();

            //ECG-HR Conversion
            if (EnableECGtoHRConversion)
            {
                ECGtoHRAdaptive = new ECGToHRAdaptive(ShimmerDevice.GetSamplingRate());
            }
            ExGLeadOffCounter = 0;
            ExGLeadOffCounterSize = (int)ShimmerDevice.GetSamplingRate();
            ShimmerIdSetup.Clear();
            StreamingSignalNamesRaw.Clear();
            StreamingSignalNamesCal.Clear();
            NumberOfTracesCountGraph1 = 0;
            NumberOfTracesCountGraph2 = 0;
            NumberOfTracesCountGraph3 = 0;
            CountXAxisDataPoints = 0;
            CountXAxisDataPoints++;
            ShimmerDevice.StartStreaming();

        }

        private void buttonReload_Click(object sender, EventArgs e)
        {
            comboBoxComPorts.Items.Clear();
            String[] names = SerialPort.GetPortNames();
            foreach (String s in names)
            {
                comboBoxComPorts.Items.Add(s);
            }
            /*
            //Left in just for testing
            byte[] infomwracc = { 0x80,0x02,0x01,0x00,0x10,0x00,0x41,0xFF,0x01,0x08,0x00,0x80,0x10,0x00,0x00,0x00,0x00,0x00,0x02,0x01,0x00,0x80,0x10,0x00,0x00,0x00,0x00,0x00,0x02,0x01,0x09,0x00,0x00,0x00,0x08,0xCD,0x08,0xCD,0x08,0xCD,0x00,0x5C,0x00,0x5C,0x00,0x5C,0x00,0x9C,0x00,0x9C,0x00,0x00,0x00,0x00,0x9C,0x00,0x00,0x00,0x00,0x00,0x00,0x19,0x96,0x19,0x96,0x19,0x96,0x00,0x9C,0x00,0x9C,0x00,0x00,0x00,0x00,0x9C,0x00,0x00,0x00,0x00,0x00,0x00,0x02,0x9B,0x02,0x9B,0x02,0x9B,0x00,0x9C,0x00,0x64,0x00,0x00,0x00,0x00,0x9C,0x00,0x00,0x00,0x00,0x00,0x00,0x06,0x87,0x06,0x87,0x06,0x87,0x00,0x9C,0x00,0x64,0x00,0x00,0x00,0x00,0x9C,0x00,0x00,0x00,0x00,0x00,0x00,0x00,0x00,0x00,0x00,
            0x00,0x00,0x00,0x00,0x00,0x00,0x00,0x00,0x00,0x00,0x00,0x00,0x00,0x00,0x00,0x00,0x00,0x00,0x00,0x00,0x00,0x00,0x00,0x00,0x00,0x00,0x00,0x00,0x00,0x00,0x00,0x00,0x00,0x00,0x00,0x00,0x00,0x00,0x00,0x00,0x00,0x00,0x00,0x00,0x00,0x00,0x00,0x00,0x00,0x00,0x00,0x00,0x00,0x00,0x00,0x00,0x00,0x00,0x00,0x53,0x68,0x69,0x6D,0x6D,0x65,0x72,0x5F,0x36,0x38,0x44,0x44,0x44,0x65,0x66,0x61,0x75,0x6C,0x74,0x54,0x72,0x69,0x61,0x6C,0x65,0x55,0x8C,0x9F,0x00,0x00,0x31,0x00,0x00,0x00,0x00,0x00,0x00,0xFF,0xFF,0xFF,0xFF,0xFF,0xFF,0x01,0x00,0x00,0x00,0x00,0x00,0x00,0x00,0x00,0x00,0x00,0x00,0x00,0x00,0x00,0x00,0x00,0x00,0x00,0x00,0x00,0x00,0x00,0x00,0x00,0x00,
            0x00,0x00,0x00,0x00,0x00,0x00,0x00,0x00,0x00,0x00,0x00,0x00,0x00,0x00,0x00,0x00,0x00,0x00,0x00,0x00,0x00,0x00,0x00,0x00,0x00,0x00,0x00,0x00,0x00,0x00,0x00,0x00,0x00,0x00,0x00,0x00,0x00,0x00,0x00,0x00,0x00,0x00,0x00,0x00,0x00,0x00,0x00,0x00,0x00,0x00,0x00,0x00,0x00,0x00,0x00,0x00,0x00,0x00,0x00,0x00,0x00,0x00,0x00,0x00,0x00,0x00,0x00,0x00,0x00,0x00,0x00,0x00,0x00,0x00,0x00,0x00,0x00,0x00,0x00,0x00,0x00,0x00,0x00,0x00,0x00,0x00,0x00,0x00,0x00,0x00,0x00,0x00,0x00,0x00,0x00,0x00,0x00,0x00,0x00,0x00,0x00,0x00,0x00,0x00,0x00,0x00,0x00,0x00,0x00,0x00,0x00,0x00,0x00,0x00,0x00,0x00,0x00,0x00,0x00,0x00,0x00,0x00,0x00,0x00,0x00,0x00,0x00,0x00};
            ShimmerDevice.WriteShimmer3Infomem(infomwracc);
            */
        }

        private void labelPRR_Click(object sender, EventArgs e)
        {

        }

        private void ZedGraphControl1_Load(object sender, EventArgs e)
        {

        }

        private void checkForUpdatesToolStripMenuItem_Click(object sender, EventArgs e)
        {
            FormUpdateCheck ImportForm = new FormUpdateCheck();
            ImportForm.ShowDialog(this);
        }

        private void aboutToolStripMenuItem_Click(object sender, EventArgs e)
        {
            FormAbout AboutForm = new FormAbout();
            AboutForm.ShowDialog(this);
        }

        private void toolStripButton1_Click(object sender, EventArgs e)
        {
            FormUpdateCheck ImportForm = new FormUpdateCheck();
            ImportForm.ShowDialog(this);
        }

        private void buttonReadDirectory_Click(object sender, EventArgs e)
        {
            ShimmerDevice.ReadDirectory();
            /*
            //Left in just for testing
            ShimmerDevice.ReadShimmer3InfoMem();
            byte[] ifmaccgyromag = {0x80,0x02,0x01,0xE0,0x20,0x00,0x01,0x9B,0x0D,0x08,0x00,0x80,0x10,0x00,0x00,0x00,0x00,0x00,0x02,0x01,0x00,0x80,0x10,0x00,0x00,0x00,0x00,0x00,0x02,0x01,0x09,0x00,0x00,0x00,0x08,0xCD,0x08,0xCD,0x08,0xCD,0x00,0x5C,0x00,0x5C,0x00,0x5C,0x00,0x9C,0x00,0x9C,0x00,0x00,0x00,0x00,0x9C,0x00,0x00,0x00,0x00,0x00,0x00,0x19,0x96,0x19,0x96,0x19,0x96,0x00,0x9C,0x00,0x9C,0x00,0x00,0x00,0x00,0x9C,0x00,0x00,0x00,0x00,0x00,0x00,0x02,0x9B,0x02,0x9B,0x02,0x9B,0x00,0x9C,0x00,0x64,0x00,0x00,0x00,0x00,0x9C,0x00,0x00,0x00,0x00,0x00,0x00,0x06,0x87,0x06,0x87,0x06,0x87,0x00,0x9C,0x00,0x64,0x00,0x00,0x00,0x00,0x9C,0x00,0x00,0x00,0x00,0x00,0x00,0x00,0x00,0x00,0x00,
            0x00,0x00,0x00,0x00,0x00,0x00,0x00,0x00,0x00,0x00,0x00,0x00,0x00,0x00,0x00,0x00,0x00,0x00,0x00,0x00,0x00,0x00,0x00,0x00,0x00,0x00,0x00,0x00,0x00,0x00,0x00,0x00,0x00,0x00,0x00,0x00,0x00,0x00,0x00,0x00,0x00,0x00,0x00,0x00,0x00,0x00,0x00,0x00,0x00,0x00,0x00,0x00,0x00,0x00,0x00,0x00,0x00,0x00,0x00,0x53,0x68,0x69,0x6D,0x6D,0x65,0x72,0x5F,0x36,0x38,0x44,0x44,0x44,0x65,0x66,0x61,0x75,0x6C,0x74,0x54,0x72,0x69,0x61,0x6C,0x65,0x54,0x7E,0x40,0x00,0x00,0x31,0x00,0x00,0x00,0x00,0x00,0x00,0xFF,0xFF,0xFF,0xFF,0xFF,0xFF,0x01,0x00,0x00,0x00,0x00,0x00,0x00,0x00,0x00,0x00,0x00,0x00,0x00,0x00,0x00,0x00,0x00,0x00,0x00,0x00,0x00,0x00,0x00,0x00,0x00,0x00,
            0x00,0x00,0x00,0x00,0x00,0x00,0x00,0x00,0x00,0x00,0x00,0x00,0x00,0x00,0x00,0x00,0x00,0x00,0x00,0x00,0x00,0x00,0x00,0x00,0x00,0x00,0x00,0x00,0x00,0x00,0x00,0x00,0x00,0x00,0x00,0x00,0x00,0x00,0x00,0x00,0x00,0x00,0x00,0x00,0x00,0x00,0x00,0x00,0x00,0x00,0x00,0x00,0x00,0x00,0x00,0x00,0x00,0x00,0x00,0x00,0x00,0x00,0x00,0x00,0x00,0x00,0x00,0x00,0x00,0x00,0x00,0x00,0x00,0x00,0x00,0x00,0x00,0x00,0x00,0x00,0x00,0x00,0x00,0x00,0x00,0x00,0x00,0x00,0x00,0x00,0x00,0x00,0x00,0x00,0x00,0x00,0x00,0x00,0x00,0x00,0x00,0x00,0x00,0x00,0x00,0x00,0x00,0x00,0x00,0x00,0x00,0x00,0x00,0x00,0x00,0x00,0x00,0x00,0x00,0x00,0x00,0x00,0x00,0x00,0x00,0x00,0x00,0x00 };
            ShimmerDevice.WriteShimmer3Infomem(ifmaccgyromag);
            */
        }

        private void comboBoxComPorts_SelectedIndexChanged(object sender, EventArgs e)
        {

        }

        private void toolStrip1_ItemClicked(object sender, ToolStripItemClickedEventArgs e)
        {

        }

        private void button1_Click(object sender, EventArgs e)
        {
            System.Console.WriteLine("Set Blink LED");
            if (ShimmerDevice.GetBlinkLED() == 0)
            {
                ShimmerDevice.WriteBlinkLED(2);
            }
            else if (ShimmerDevice.GetBlinkLED() == 2)
            {
                ShimmerDevice.WriteBlinkLED(0);
            }
        }

        private void checkBoxTSACheck_CheckedChanged(object sender, EventArgs e)
        {
            ShimmerDevice.mEnableTimeStampAlignmentCheck = checkBoxTSACheck.Checked;

        }

        private void button1_Click_1(object sender, EventArgs e)
        {
            PPGtoHeartRateCalculation.resetParameters();
        }

        private void button2_Click(object sender, EventArgs e)
        {
            int SawtoothValue = Convert.ToInt32(textBoxSawtoothValue.Text.ToString());
            ShimmerDevice.StartStreamingEXGSawtoothTestSignal(SawtoothValue);
        }

        private void textBox1_TextChanged(object sender, EventArgs e)
        {

        }

        private void checkBox91_CheckedChanged(object sender, EventArgs e)
        {
            if (ShimmerDevice.IsConnected())
            {
                if (checkBoxCRC.Checked)
                {
                    ShimmerDevice.WriteCRCMode(ShimmerBluetooth.BTCRCMode.ONE_BYTE);
                } else
                {
                    ShimmerDevice.WriteCRCMode(ShimmerBluetooth.BTCRCMode.OFF);
                }
            }
        }

        private void button3_Click(object sender, EventArgs e)
        {
            ShimmerDevice.StopStreamingandLog();
        }

        private void buttonClearGraphs_Click_1(object sender, EventArgs e)
        {
            ClearGraphData(ZedGraphControl1, DataListGraph1);
            if (IsGraph2Visible)
                ClearGraphData(ZedGraphControl2, DataListGraph2);
            if (IsGraph3Visible)
                ClearGraphData(ZedGraphControl3, DataListGraph3);
        }
    }


    // ExceptionReporter Class
    internal class ExceptionEventHandler
    {
        private static readonly string ApplicationName = Application.ProductName.ToString().Replace("_", " ");
        //    private string versionNumber = Application.ProductVersion.ToString().Substring(0, Application.ProductVersion.ToString().LastIndexOf(".")).ToLower();
        private string versionNumber = Application.ProductVersion.ToString();

        public void CurrentDomainUnhandledException(object sender, UnhandledExceptionEventArgs e)
        {
            //ReportCrash(e.ExceptionObject as Exception);
            Environment.Exit(0);
        }

        public void ApplicationThreadException(object sender, ThreadExceptionEventArgs e)
        {
            //ReportCrash(e.Exception);
            Environment.Exit(0);
        }

        private static void ReportCrash(Exception exception)
        {

        }
    }


}
