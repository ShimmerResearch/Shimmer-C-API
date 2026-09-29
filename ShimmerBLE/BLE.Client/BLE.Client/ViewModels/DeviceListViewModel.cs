using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Acr.UserDialogs;
using BLE.Client.Extensions;
using MvvmCross.ViewModels;
using Plugin.BLE.Abstractions;
using Plugin.BLE.Abstractions.Contracts;
using Plugin.BLE.Abstractions.EventArgs;
using Plugin.Permissions.Abstractions;
using Plugin.Settings.Abstractions;
using MvvmCross.Commands;
using MvvmCross.Navigation;
using MvvmCross;
using Xamarin.Forms;
using shimmer.Services;
using shimmer.Models;
using ShimmerAPI;
using shimmer.Sensors;
using static shimmer.Models.ShimmerBLEEventData;
using shimmer.Communications;
using ShimmerBLEAPI.Devices;
using System.IO;
using SkiaSharp;
using Microcharts;
using Entry = Microcharts.ChartEntry;
using BLE.Client.Pages;
using System.Collections.Concurrent;
using shimmer.Helpers;
using ShimmerBLEAPI;
using ShimmerBLEAPI.Models;
using System.Diagnostics;
using Trace = System.Diagnostics.Trace;
using OxyPlot;
using static ShimmerBLEAPI.AbstractPlotManager;
using ShimmerBLEAPI.UWP.Communications;
using Newtonsoft.Json;
using ShimmerAdvanceBLEAPI;
using ShimmerBLEAPI.Communications;
using shimmer.DTO;

namespace BLE.Client.ViewModels
{
    public class DeviceListViewModel : BaseViewModel, IObserver<String>//, INotifyPropertyChanged
    {
        private readonly IBluetoothLE _bluetoothLe;
        private readonly IUserDialogs _userDialogs;
        private readonly ISettings _settings;
        private Guid _previousGuid;

        PlotManager PlotManager = new PlotManager("Data", "Data Point", "Timestamp", true);

        VerisenseBLEScannedDevice selectedDevice = null;
        VerisenseSerialDevice selectedSerialDevice = null;
        private CancellationTokenSource _cancellationTokenSource;
        VerisenseBLEDevice VerisenseBLEDevice;
        IVerisenseBLEManager bleManager = DependencyService.Get<IVerisenseBLEManager>();
        IVerisenseSerialPortManager serialPortManager = DependencyService.Get<IVerisenseSerialPortManager>();
        ShimmerDeviceBluetoothState CurrentState;
        ShimmerDeviceBluetoothState StateToContinue;
        VerisenseAPIDemoSettings verisenseApiDemoSettings = new VerisenseAPIDemoSettings();
        S3CloudManager cloudManager;
        public static NavigationPage CurrentPage { get; set; }
        public static TimeSpan StartTimeSpan { get; set; }
        public static TimeSpan EndTimeSpan { get; set; }
        public static IBLEPairingKeyGenerator PairingKeyGenerator;

        public Guid PreviousGuid
        {
            get => _previousGuid;
            set
            {
                _previousGuid = value;
                _settings.AddOrUpdateValue("lastguid", _previousGuid.ToString());
                RaisePropertyChanged();
                RaisePropertyChanged(() => ConnectToPreviousCommand);
            }
        }
        String _pairingStatus;
        public String PairingStatus
        {
            get => _pairingStatus;
            set
            {
                if (_pairingStatus == value)
                    return;
                _pairingStatus = value;
                RaisePropertyChanged();
            }
        }

        DeviceManagerPluginBLE BLEManager;
        public MvxCommand RefreshCommand => new MvxCommand(() => TryStartScanning(true));
        public MvxCommand RefreshSerialPort => new MvxCommand(async () => {
            SerialDevices.Clear();
            await serialPortManager.StartScanForSerialPorts();
            foreach (var device in serialPortManager.GetListOfSerialDevices())
            {
                SerialDevices.Add(new VerisenseSerialDevice(device.Id));
            }
        });
        public MvxCommand<DeviceListItemViewModel> DisconnectCommand => new MvxCommand<DeviceListItemViewModel>(DisconnectDevice);

        public MvxCommand<DeviceListItemViewModel> ConnectDisposeCommand => new MvxCommand<DeviceListItemViewModel>(ConnectAndDisposeDevice);
        public MvxCommand TestSpeedCommand => new MvxCommand(() => TestSpeed());
        public MvxCommand TestSpeedStopCommand => new MvxCommand(() => TestSpeedStop());
        public MvxCommand UploadCommand => new MvxCommand(() => Upload());
        public MvxCommand ConnectCommand => new MvxCommand(() => Connect());
        public MvxCommand DisconnectVRECommand => new MvxCommand(() => Disconnect());
        public MvxCommand ReadStatusCommand => new MvxCommand(() => ReadStatus());
        public MvxCommand ReadProdConfCommand => new MvxCommand(() => ReadProdConf());
        public MvxCommand ReadOpConfCommand => new MvxCommand(() => ReadOpConf());
        public MvxCommand WriteTimeCommand => new MvxCommand(() => WriteTime());
        public MvxCommand ReadTimeCommand => new MvxCommand(() => ReadTime());
        public MvxCommand DownloadDataCommand => new MvxCommand(() => DownloadData());
        public MvxCommand StreamDataCommand => new MvxCommand(() => StreamData());
        public MvxCommand StopStreamCommand => new MvxCommand(() => StopStream());
        public MvxCommand EraseDataCommand => new MvxCommand(() => EraseData());
        public MvxCommand PairCommand => new MvxCommand(() => PairDev());
        public MvxCommand EnableAcc2Gyro => new MvxCommand(() => EnableAccGyro());
        public MvxCommand DisableAcc2Gyro => new MvxCommand(() => DisableAccGyro());
        public MvxCommand EnableAcc => new MvxCommand(() => EnableAccel());
        public MvxCommand DisableAcc => new MvxCommand(() => DisableAccel());
        public MvxCommand ConfigureVerisenseDevice => new MvxCommand(() => ConfigureDevice());
        public MvxCommand ConfigureVerisenseSensor => new MvxCommand(() => ConfigureSensor());
        public MvxCommand StopScanCommand => new MvxCommand(() => StopScan());
        public MvxCommand SoftResetCommand => new MvxCommand(() => ResetSensor());
        public MvxCommand ConnectWithoutInitializeCommand => new MvxCommand(() => Connect(false));

        public ObservableCollection<DeviceListItemViewModel> Devices { get; set; } = new ObservableCollection<DeviceListItemViewModel>();
        public ObservableCollection<VerisenseSerialDevice> SerialDevices { get; set; } = new ObservableCollection<VerisenseSerialDevice>();
        public bool IsRefreshing => (Adapter != null) ? Adapter.IsScanning : false;
        public bool IsStateOn => _bluetoothLe.IsOn;
        public string StateText => GetStateText();
        public DeviceListItemViewModel SelectedDevice
        {
            get => null;
            set
            {
                if (value != null)
                {
                    HandleSelectedDevice(value);
                }

                RaisePropertyChanged();
            }
        }
        public VerisenseSerialDevice SelectedSerialDevice
        {
            get => null;
            set
            {
                if (value != null)
                {
                    selectedSerialDevice = value;
                }

                RaisePropertyChanged();
            }
        }

        string _trialName = "UnknownTrialName";
        public String TrialName
        {
            get
            {
                return _trialName;
            }
            set
            {
                _trialName = value;
                RaisePropertyChanged();
            }
        }


        string _participantID = "UnknownParticipantID";
        public String ParticipantID
        {
            get
            {
                return _participantID;
            }
            set
            {
                _participantID = value;
                RaisePropertyChanged();
            }
        }

        bool _useAutoConnect;
        public bool UseAutoConnect
        {
            get => _useAutoConnect;

            set
            {
                if (_useAutoConnect == value)
                    return;

                _useAutoConnect = value;
                RaisePropertyChanged();
            }
        }

        bool _deviceLogging;
        public bool DeviceLogging
        {
            get => _deviceLogging;

            set
            {
                if (_deviceLogging == value)
                    return;

                _deviceLogging = value;
                RaisePropertyChanged();
            }
        }

        bool _deviceEnabled;
        public bool DeviceEnabled
        {
            get => _deviceEnabled;

            set
            {
                if (_deviceEnabled == value)
                    return;

                _deviceEnabled = value;
                RaisePropertyChanged();
            }
        }

        bool _bluetoothEnabled;
        public bool BluetoothEnabled
        {
            get => _bluetoothEnabled;

            set
            {
                if (_bluetoothEnabled == value)
                    return;

                _bluetoothEnabled = value;
                RaisePropertyChanged();
            }
        }

        bool _usbEnabled;
        public bool USBEnabled
        {
            get => _usbEnabled;

            set
            {
                if (_usbEnabled == value)
                    return;

                _usbEnabled = value;
                RaisePropertyChanged();
            }
        }

        bool _AutoReconnect=false;
        public bool AutoReconnect
        {
            get => _AutoReconnect;

            set
            {
                if (_AutoReconnect == value)
                    return;

                _AutoReconnect = value;
                RaisePropertyChanged();
            }
        }

        bool _KeepDeviceSettings = false;
        public bool KeepDeviceSettings
        {
            get => _KeepDeviceSettings;

            set
            {
                if (_KeepDeviceSettings == value)
                    return;

                _KeepDeviceSettings = value;
                RaisePropertyChanged();
            }
        }
        bool _AccelEnabled;
        public bool SensorAccel
        {
            get => _AccelEnabled;

            set
            {
                if (_AccelEnabled == value)
                    return;

                _AccelEnabled = value;
                RaisePropertyChanged();
            }
        }

        bool _AccelLowNoiseEnabled;
        public bool SensorAccelLowNoise
        {
            get => _AccelLowNoiseEnabled;

            set
            {
                if (_AccelLowNoiseEnabled == value)
                    return;

                _AccelLowNoiseEnabled = value;
                RaisePropertyChanged();
            }
        }

        bool _AccelHighPassFilterEnabled;
        public bool SensorAccelHighPassFilter
        {
            get => _AccelHighPassFilterEnabled;

            set
            {
                if (_AccelHighPassFilterEnabled == value)
                    return;

                _AccelHighPassFilterEnabled = value;
                RaisePropertyChanged();
            }
        }

        bool _AccelHighPassFilterRefModeEnabled;
        public bool SensorAccelHighPassFilterRefMode
        {
            get => _AccelHighPassFilterRefModeEnabled;

            set
            {
                if (_AccelHighPassFilterRefModeEnabled == value)
                    return;

                _AccelHighPassFilterRefModeEnabled = value;
                RaisePropertyChanged();
            }
        }

        bool _Accel2Enabled;
        public bool SensorAccel2
        {
            get => _Accel2Enabled;

            set
            {
                if (_Accel2Enabled == value)
                    return;

                _Accel2Enabled = value;
                RaisePropertyChanged();
            }
        }

        bool _GyroEnabled;
        public bool SensorGyro
        {
            get => _GyroEnabled;

            set
            {
                if (_GyroEnabled == value)
                    return;

                _GyroEnabled = value;
                RaisePropertyChanged();
            }
        }

        //bool _GyroHighPerformanceEnabled;
        //public bool SensorGyroHighPerformance
        //{
        //    get => _GyroHighPerformanceEnabled;

        //    set
        //    {
        //        if (_GyroHighPerformanceEnabled == value)
        //            return;

        //        _GyroHighPerformanceEnabled = value;
        //        RaisePropertyChanged();
        //    }
        //}

        bool _GyroHighPassFilterEnabled;
        public bool SensorGyroHighPassFilter
        {
            get => _GyroHighPassFilterEnabled;

            set
            {
                if (_GyroHighPassFilterEnabled == value)
                    return;

                _GyroHighPassFilterEnabled = value;
                RaisePropertyChanged();
            }
        }

        bool _GyroDigitalHPResetEnabled;
        public bool SensorGyroDigitalHPReset
        {
            get => _GyroDigitalHPResetEnabled;

            set
            {
                if (_GyroDigitalHPResetEnabled == value)
                    return;

                _GyroDigitalHPResetEnabled = value;
                RaisePropertyChanged();
            }
        }

        bool _SourceRegRoundingStatusEnabled;
        public bool SourceRegisterRoundingStatus
        {
            get => _SourceRegRoundingStatusEnabled;

            set
            {
                if (_SourceRegRoundingStatusEnabled == value)
                    return;

                _SourceRegRoundingStatusEnabled = value;
                RaisePropertyChanged();
            }
        }

        bool _StepCounterAndTimestampEnabled;
        public bool StepCounterAndTimestamp
        {
            get => _StepCounterAndTimestampEnabled;

            set
            {
                if (_StepCounterAndTimestampEnabled == value)
                    return;

                _StepCounterAndTimestampEnabled = value;
                RaisePropertyChanged();
            }
        }

        bool _WriteInFIFOAtEveryStepDetectedEnabled;
        public bool WriteInFIFOAtEveryStepDetected
        {
            get => _WriteInFIFOAtEveryStepDetectedEnabled;

            set
            {
                if (_WriteInFIFOAtEveryStepDetectedEnabled == value)
                    return;

                _WriteInFIFOAtEveryStepDetectedEnabled = value;
                RaisePropertyChanged();
            }
        }

        bool _Accel2LowPassFilterEnabled;
        public bool Accel2LowPassFilter
        {
            get => _Accel2LowPassFilterEnabled;

            set
            {
                if (_Accel2LowPassFilterEnabled == value)
                    return;

                _Accel2LowPassFilterEnabled = value;
                RaisePropertyChanged();
            }
        }

        bool _Accel2SlopeOrHighPassFilterEnabled;
        public bool Accel2SlopeOrHighPassFilter
        {
            get => _Accel2SlopeOrHighPassFilterEnabled;

            set
            {
                if (_Accel2SlopeOrHighPassFilterEnabled == value)
                    return;

                _Accel2SlopeOrHighPassFilterEnabled = value;
                RaisePropertyChanged();
            }
        }

        bool _Accel2LowPassFilterOn6DEnabled;
        public bool Accel2LowPassFilterOn6D
        {
            get => _Accel2LowPassFilterOn6DEnabled;

            set
            {
                if (_Accel2LowPassFilterOn6DEnabled == value)
                    return;

                _Accel2LowPassFilterOn6DEnabled = value;
                RaisePropertyChanged();
            }
        }

        bool _GSREnabled;
        public bool SensorGSR
        {
            get => _GSREnabled;

            set
            {
                if (_GSREnabled == value)
                    return;

                if (_BattEnabled && gsroversamplingrate != shimmer.Sensors.SensorGSR.ADCOversamplingRate.ADC_Oversampling_Disabled)
                {
                    RaisePropertyChanged();
                    return;
                }

                _GSREnabled = value;
                RaisePropertyChanged();
            }
        }

        bool _BattEnabled;
        public bool SensorBatt
        {
            get => _BattEnabled;

            set
            {
                if (_BattEnabled == value)
                    return;

                if (_GSREnabled && gsroversamplingrate != shimmer.Sensors.SensorGSR.ADCOversamplingRate.ADC_Oversampling_Disabled)
                {
                    RaisePropertyChanged();
                    return;
                }

                _BattEnabled = value;
                RaisePropertyChanged();
            }
        }

        bool _SensorPPGGreen;
        public bool SensorPPGGreen
        {
            get => _SensorPPGGreen;

            set
            {
                if (_SensorPPGGreen == value)
                    return;

                _SensorPPGGreen = value;
                RaisePropertyChanged();
            }
        }
        bool _SensorPPGRed;
        public bool SensorPPGRed
        {
            get => _SensorPPGRed;

            set
            {
                if (_SensorPPGRed == value)
                    return;

                _SensorPPGRed = value;
                RaisePropertyChanged();
            }
        }
        bool _SensorPPGIR;
        public bool SensorPPGIR
        {
            get => _SensorPPGIR;

            set
            {
                if (_SensorPPGIR == value)
                    return;

                _SensorPPGIR = value;
                RaisePropertyChanged();
            }
        }
        bool _SensorPPGBlue;
        public bool SensorPPGBlue
        {
            get => _SensorPPGBlue;

            set
            {
                if (_SensorPPGBlue == value)
                    return;

                _SensorPPGBlue = value;
                RaisePropertyChanged();
            }
        }

        bool _LogToFile = false;
        public bool LogToFile
        {
            get => _LogToFile;

            set
            {
                if (_LogToFile == value)
                    return;

                _LogToFile = value;
                RaisePropertyChanged();
            }
        }
        public class VerisenseAPIDemoSettings
        {
            public S3CloudInfo S3CloudInfo { get; set; }
            public string FWVersion { get; set; }
        }


        bool _enableDownsample = false;
        public bool EnableDownsample
        {
            get => _enableDownsample;

            set
            {
                if (_enableDownsample == value)
                    return;

                _enableDownsample = value;
                PlotManager.SetEnableDownsampling(_enableDownsample);
                RaisePropertyChanged();
            }
        }

        int _downsamplingFactor = 0;
        public string DownsamplingFactor
        {
            get
            {
                return _downsamplingFactor.ToString();
            }
            set
            {
                if (value.Equals("") || value == null) 
                {
                    _downsamplingFactor = 0;
                    PlotManager.SetDownsamplingFactor(_downsamplingFactor);
                    Debug.WriteLine(PlotManager.GetDownsamplingFactor());
                }else if (!value.Equals("") || value != null) 
                {
                    _downsamplingFactor = int.Parse(value);
                    PlotManager.SetDownsamplingFactor(_downsamplingFactor);
                }
                RaisePropertyChanged();
            }
        }


        readonly IPermissions _permissions;

        public List<ScanMode> ScanModes => Enum.GetValues(typeof(ScanMode)).Cast<ScanMode>().ToList();
        public List<String> AccelRanges => SensorLIS2DW12.AccelRange.Settings.Select(setting => setting.GetDisplayName()).ToList();
        public List<String> AccelModes => SensorLIS2DW12.Mode.Settings.Select(setting => setting.GetDisplayName()).ToList();
        public List<String> AccelLPModes => SensorLIS2DW12.LowPowerMode.Settings.Select(setting => setting.GetDisplayName()).ToList();
        public List<String> accelRates = SensorLIS2DW12.LowPerformanceAccelSamplingRate.Settings.Select(setting => setting.GetDisplayName()).ToList();
        public List<String> AccelBandwidthFilter => SensorLIS2DW12.LowPerformanceBandwidthFilter.Settings.Select(setting => setting.GetDisplayName()).ToList();
        public List<String> AccelFMode => SensorLIS2DW12.FMode.Settings.Select(setting => setting.GetDisplayName()).ToList();
        public List<String> BandwidthFilter => SensorLIS2DW12.LowPerformanceBandwidthFilter.Settings.Select(setting => setting.GetDisplayName()).ToList();
        public List<String> Accel2Ranges => SensorLSM6DS3.AccelRange.Settings.Select(setting => setting.GetDisplayName()).ToList();
        public List<String> Accel2GyroRates => SensorLSM6DS3.SamplingRate.Settings.Select(setting => setting.GetDisplayName()).ToList();
        public List<String> FIFOThresholds => SensorLSM6DS3.FIFOThreshold.Settings.Select(setting => setting.GetDisplayName()).ToList();
        public List<String> GyroRanges => SensorLSM6DS3.GyroRange.Settings.Select(setting => setting.GetDisplayName()).ToList();
        public List<String> GyroFIFODecimation => SensorLSM6DS3.GyroFIFODecimation.Settings.Select(setting => setting.GetDisplayName()).ToList();
        public List<String> AccelFIFODecimation => SensorLSM6DS3.AccelFIFODecimation.Settings.Select(setting => setting.GetDisplayName()).ToList();
        public List<String> FIFOOutputDataRate => SensorLSM6DS3.FIFOOutputDataRate.Settings.Select(setting => setting.GetDisplayName()).ToList();
        public List<String> FIFOMode => SensorLSM6DS3.FIFOMode.Settings.Select(setting => setting.GetDisplayName()).ToList();
        public List<String> AccelAntiAliasingFilterBW => SensorLSM6DS3.AccelAntiAliasingFilterBW.Settings.Select(setting => setting.GetDisplayName()).ToList();
        public List<String> HPFilterCutOffFreq => SensorLSM6DS3.HPFilterCutOffFrequency.Settings.Select(setting => setting.GetDisplayName()).ToList();
        //public List<String> GyroRates => SensorLSM6DS3.GyroSamplingRate.Settings.Select(setting => setting.GetDisplayName()).ToList();
        public List<String> GSRRanges => shimmer.Sensors.SensorGSR.GSRRange.Settings.Select(setting => setting.GetDisplayName()).ToList();
        public List<String> GSRRates => shimmer.Sensors.SensorGSR.GSRRate.Settings.Select(setting => setting.GetDisplayName()).ToList();
        public List<String> GSROversamplingRates => shimmer.Sensors.SensorGSR.ADCOversamplingRate.Settings.Select(setting => setting.GetDisplayName()).ToList();
        public List<String> PPGRates => shimmer.Sensors.SensorPPG.SamplingRate.Settings.Select(setting => setting.GetDisplayName()).ToList();
        public List<String> PPGRanges => shimmer.Sensors.SensorPPG.ADCRange.Settings.Select(setting => setting.GetDisplayName()).ToList();
        public List<String> PPGSamplingAverages => shimmer.Sensors.SensorPPG.SampleAverage.Settings.Select(setting => setting.GetDisplayName()).ToList();
        public List<String> PPGWidths => shimmer.Sensors.SensorPPG.LEDPulseWidth.Settings.Select(setting => setting.GetDisplayName()).ToList();
        public List<String> PPGProxAGCMode => shimmer.Sensors.SensorPPG.ProxAGCMode.Settings.Select(setting => setting.GetDisplayName()).ToList();
        public List<String> DeviceConfigurations => VerisenseDevice.DefaultVerisenseConfiguration.Settings.Select(setting => setting.GetDisplayName()).ToList();
        public List<String> RadioOutputPower => VerisenseDevice.BT5RadioOutputPower.Settings.Select(setting => setting.GetDisplayName()).ToList();

        public List<String> AccelRates
        {
            get => accelRates;
            set
            {
                accelRates = value;
                //RaisePropertyChanged();
            }
        }

        public DateTime MinimumDate
        {
            get
            {
                return DateHelper.Start;
            }
        }

        TimeSpan startTime = DateTime.Now.TimeOfDay;
        public TimeSpan SelectedStartTime
        {
            get
            {
                return startTime;
            }
            set
            {
                startTime = value;
                deviceSettingDescription = "The date and time (in minutes) the ASM sensor will start collecting data.";
                RaisePropertyChanged();
            }
        }

        DateTime startDate = DateTime.Today;
        public DateTime SelectedStartDate
        {
            get
            {
                return startDate;
            }
            set
            {
                startDate = value;
                deviceSettingDescription = "The date and time (in minutes) the ASM sensor will start collecting data.";
                RaisePropertyChanged();
            }
        }

        TimeSpan endTime = DateTime.Now.TimeOfDay;
        public TimeSpan SelectedEndTime
        {
            get
            {
                return endTime;
            }
            set
            {
                endTime = value;
                deviceSettingDescription = "The date and time (in minutes) the ASM sensor will stop collecting data.";
                RaisePropertyChanged();
            }
        }


        public static DateTime endDate = DateTime.Today;
        public DateTime SelectedEndDate
        {
            get
            {
                return endDate;
            }
            set
            {
                endDate = value;
                deviceSettingDescription = "The date and time (in minutes) the ASM sensor will start collecting data.";
                RaisePropertyChanged();
            }
        }

        public ScanMode SelectedScanMode
        {
            get => Adapter.ScanMode;
            set => Adapter.ScanMode = value;
        }

        Sensor.SensorSetting a2bandwidth = SensorLIS2DW12.LowPerformanceBandwidthFilter.Bandwidth_Unknown;
        public String SelectedBandwidthFilter
        {
            get
            {
                return a2bandwidth.GetDisplayName();
            }
            set
            {
                a2bandwidth = Sensor.GetSensorSettingFromDisplayName(SensorLIS2DW12.LowPerformanceBandwidthFilter.Settings, value);
                SensorDescription = a2bandwidth.GetDescription();
                RaisePropertyChanged();
            }
        }

        Sensor.SensorSetting a2fmode = SensorLIS2DW12.FMode.FMode_Unknown;
        public String SelectedFMode
        {
            get
            {
                return a2fmode.GetDisplayName();
            }
            set
            {
                a2fmode = Sensor.GetSensorSettingFromDisplayName(SensorLIS2DW12.FMode.Settings, value);
                SensorDescription = a2fmode.GetDescription();
                RaisePropertyChanged();
            }
        }

        int a2FIFOthreshold = -1;
        public String FIFOthreshold
        {
            get
            {
                return a2FIFOthreshold.ToString();
            }
            set
            {
                a2FIFOthreshold = int.Parse(value);
                RaisePropertyChanged();
            }
        }

        Sensor.SensorSetting a2range = SensorLSM6DS3.AccelRange.Range_Unknown;
        public String SelectedAccel2Range
        {
            get
            {
                return a2range.GetDisplayName();
            }
            set
            {
                a2range = Sensor.GetSensorSettingFromDisplayName(SensorLSM6DS3.AccelRange.Settings, value);
                SensorDescription = a2range.GetDescription();
                RaisePropertyChanged();
            }
        }

        Sensor.SensorSetting a2gyrorate = SensorLSM6DS3.SamplingRate.Rate_Unknown;
        public String SelectedAccel2GyroRate
        {
            get
            {
                return a2gyrorate.GetDisplayName();
            }
            set
            {
                a2gyrorate = Sensor.GetSensorSettingFromDisplayName(SensorLSM6DS3.SamplingRate.Settings, value);
                SensorDescription = a2gyrorate.GetDescription();
                RaisePropertyChanged();
            }
        }

        Sensor.SensorSetting fthreshold = SensorLSM6DS3.FIFOThreshold.Threshold_Unknown;
        public String SelectedFIFOThreshold
        {
            get
            {
                return fthreshold.GetDisplayName();
            }
            set
            {
                fthreshold = Sensor.GetSensorSettingFromDisplayName(SensorLSM6DS3.FIFOThreshold.Settings, value);
                SensorDescription = fthreshold.GetDescription();
                RaisePropertyChanged();
            }
        }

        Sensor.SensorSetting grange = SensorLSM6DS3.GyroRange.Range_Unknown;
        public String SelectedGyroRange
        {
            get
            {
                return grange.GetDisplayName();
            }
            set
            {
                grange = Sensor.GetSensorSettingFromDisplayName(SensorLSM6DS3.GyroRange.Settings, value);
                SensorDescription = grange.GetDescription();
                RaisePropertyChanged();
            }
        }

        bool __GyroFullScaleAt125Enabled;
        public bool SensorGyroFullScaleAt125
        {
            get => __GyroFullScaleAt125Enabled;

            set
            {
                if (__GyroFullScaleAt125Enabled == value)
                    return;

                __GyroFullScaleAt125Enabled = value;
                RaisePropertyChanged();
            }
        }

        Sensor.SensorSetting gFIFODecimation = SensorLSM6DS3.GyroFIFODecimation.Decimation_Unknown;
        public String SelectedGyroFIFODecimation
        {
            get
            {
                return gFIFODecimation.GetDisplayName();
            }
            set
            {
                gFIFODecimation = Sensor.GetSensorSettingFromDisplayName(SensorLSM6DS3.GyroFIFODecimation.Settings, value);
                SensorDescription = gFIFODecimation.GetDescription();
                RaisePropertyChanged();
            }
        }

        Sensor.SensorSetting aFIFODecimation = SensorLSM6DS3.AccelFIFODecimation.Decimation_Unknown;
        public String SelectedAccel2FIFODecimation
        {
            get
            {
                return aFIFODecimation.GetDisplayName();
            }
            set
            {
                aFIFODecimation = Sensor.GetSensorSettingFromDisplayName(SensorLSM6DS3.GyroFIFODecimation.Settings, value);
                SensorDescription = aFIFODecimation.GetDescription();
                RaisePropertyChanged();
            }
        }

        Sensor.SensorSetting fifoODR = SensorLSM6DS3.FIFOOutputDataRate.ODR_Unknown;
        public String SelectedFIFOOutputDataRate
        {
            get
            {
                return fifoODR.GetDisplayName();
            }
            set
            {
                fifoODR = Sensor.GetSensorSettingFromDisplayName(SensorLSM6DS3.FIFOOutputDataRate.Settings, value);
                SensorDescription = fifoODR.GetDescription();
                RaisePropertyChanged();
            }
        }

        Sensor.SensorSetting fifoMode = SensorLSM6DS3.FIFOMode.FIFOMode_Unknown;
        public String SelectedFIFOMode
        {
            get
            {
                return fifoMode.GetDisplayName();
            }
            set
            {
                fifoMode = Sensor.GetSensorSettingFromDisplayName(SensorLSM6DS3.FIFOMode.Settings, value);
                SensorDescription = fifoMode.GetDescription();
                RaisePropertyChanged();
            }
        }

        Sensor.SensorSetting accelAntiAliasingBW = SensorLSM6DS3.AccelAntiAliasingFilterBW.Filter_BW_Unknown;
        public String SelectedAccel2AntiAliasingFilterBW
        {
            get
            {
                return accelAntiAliasingBW.GetDisplayName();
            }
            set
            {
                accelAntiAliasingBW = Sensor.GetSensorSettingFromDisplayName(SensorLSM6DS3.AccelAntiAliasingFilterBW.Settings, value);
                SensorDescription = accelAntiAliasingBW.GetDescription();
                RaisePropertyChanged();
            }
        }

        Sensor.SensorSetting gcutoffFreq = SensorLSM6DS3.HPFilterCutOffFrequency.CutOff_Freq_Unknown;
        public String SelectedHPFilterCutOffFreq
        {
            get
            {
                return gcutoffFreq.GetDisplayName();
            }
            set
            {
                gcutoffFreq = Sensor.GetSensorSettingFromDisplayName(SensorLSM6DS3.HPFilterCutOffFrequency.Settings, value);
                SensorDescription = gcutoffFreq.GetDescription();
                RaisePropertyChanged();
            }
        }
        /*
        Sensor.SensorSetting grate = SensorLSM6DS3.GyroSamplingRate.Rate_Unknown;
        public String SelectedGyroRate
        {
            get
            {
                return grate.GetDisplayName();
            }
            set
            {
                grate = Sensor.GetSensorSettingFromDisplayName(SensorLSM6DS3.GyroSamplingRate.Settings, value);
                SensorDescription = grate.GetDescription();
                RaisePropertyChanged();
            }
        }
        */

        Sensor.SensorSetting gsrrange = SensorLSM6DS3.GyroRange.Range_Unknown;
        public String SelectedGSRRange
        {
            get
            {
                return gsrrange.GetDisplayName();
            }
            set
            {
                gsrrange = Sensor.GetSensorSettingFromDisplayName(shimmer.Sensors.SensorGSR.GSRRange.Settings, value);
                SensorDescription = gsrrange.GetDescription();
                RaisePropertyChanged();
            }
        }

        Sensor.SensorSetting gsrrate = shimmer.Sensors.SensorGSR.GSRRate.Rate_Unknown;
        public String SelectedGSRRate
        {
            get
            {
                return gsrrate.GetDisplayName();
            }
            set
            {
                gsrrate = Sensor.GetSensorSettingFromDisplayName(shimmer.Sensors.SensorGSR.GSRRate.Settings, value);
                SensorDescription = gsrrate.GetDescription();
                RaisePropertyChanged();
            }
        }

        Sensor.SensorSetting gsroversamplingrate = shimmer.Sensors.SensorGSR.GSRRate.Rate_Unknown;
        public String SelectedGSROversamplingRate
        {
            get
            {
                return gsroversamplingrate.GetDisplayName();
            }
            set
            {
                if (_GSREnabled && _BattEnabled)
                {
                    RaisePropertyChanged();
                    return;
                }

                gsroversamplingrate = Sensor.GetSensorSettingFromDisplayName(shimmer.Sensors.SensorGSR.ADCOversamplingRate.Settings, value);
                SensorDescription = gsroversamplingrate.GetDescription();
                RaisePropertyChanged();
            }
        }

        int bleretrycount = -1;
        public String BLEWakeupRetryCount
        {
            get
            {
                return bleretrycount.ToString();
            }
            set
            {
                bleretrycount = int.Parse(value);
                RaisePropertyChanged();
            }
        }

        VerisenseDevice.DeviceByteSetting outputpower = VerisenseDevice.BT5RadioOutputPower.Power_Unknown;
        public String SelectedRadioOutputPower
        {
            get
            {
                return outputpower.GetDisplayName();
            }
            set
            {
                outputpower = VerisenseDevice.GetDeviceOpSettingFromDisplayName(VerisenseDevice.BT5RadioOutputPower.Settings, value);
                DeviceSettingDescription = outputpower.GetDescription();
                RaisePropertyChanged();
            }
        }

        int datatransferinterval = -1;
        public String DataTransferInterval
        {
            get
            {
                return datatransferinterval.ToString();
            }
            set
            {
                datatransferinterval = int.Parse(value);
                RaisePropertyChanged();
            }
        }

        int datatransfertime = -1;
        public String DataTransferTime
        {
            get
            {
                return datatransfertime.ToString();
            }
            set
            {
                datatransfertime = int.Parse(value);
                RaisePropertyChanged();
            }
        }

        int datatransferduration = -1;
        public String DataTransferDuration
        {
            get
            {
                return datatransferduration.ToString();
            }
            set
            {
                datatransferduration = int.Parse(value);
                RaisePropertyChanged();
            }
        }

        int datatransferretryinterval= -1;
        public String DataTransferRetryInterval
        {
            get
            {
                return datatransferretryinterval.ToString();
            }
            set
            {
                datatransferretryinterval = int.Parse(value);
                RaisePropertyChanged();
            }
        }

        int statustransferinterval = -1;
        public String StatusTransferInterval
        {
            get
            {
                return statustransferinterval.ToString();
            }
            set
            {
                statustransferinterval = int.Parse(value);
                RaisePropertyChanged();
            }
        }

        int statustransfertime = -1;
        public String StatusTransferTime
        {
            get
            {
                return statustransfertime.ToString();
            }
            set
            {
                statustransfertime = int.Parse(value);
                RaisePropertyChanged();
            }
        }

        int statustransferduration = -1;
        public String StatusTransferDuration
        {
            get
            {
                return statustransferduration.ToString();
            }
            set
            {
                statustransferduration = int.Parse(value);
                RaisePropertyChanged();
            }
        }

        int statustransferretryinterval = -1;
        public String StatusTransferRetryInterval
        {
            get
            {
                return statustransferretryinterval.ToString();
            }
            set
            {
                statustransferretryinterval = int.Parse(value);
                RaisePropertyChanged();
            }
        }

        int rtcsyncinterval = -1;
        public String RTCSyncInterval
        {
            get
            {
                return rtcsyncinterval.ToString();
            }
            set
            {
                rtcsyncinterval = int.Parse(value);
                RaisePropertyChanged();
            }
        }

        int rtcsynctime = -1;
        public String RTCSyncTime
        {
            get
            {
                return rtcsynctime.ToString();
            }
            set
            {
                rtcsynctime = int.Parse(value);
                RaisePropertyChanged();
            }
        }

        int rtcsyncduration = -1;
        public String RTCSyncDuration
        {
            get
            {
                return rtcsyncduration.ToString();
            }
            set
            {
                rtcsyncduration = int.Parse(value);
                RaisePropertyChanged();
            }
        }

        int rtcsyncretryinterval = -1;
        public String RTCSyncRetryInterval
        {
            get
            {
                return rtcsyncretryinterval.ToString();
            }
            set
            {
                rtcsyncretryinterval = int.Parse(value);
                RaisePropertyChanged();
            }
        }

        int ppgrecduration = -1;
        public String PPGRecDuration
        {
            get
            {
                return ppgrecduration.ToString();
            }
            set
            {
                ppgrecduration = int.Parse(value);
                RaisePropertyChanged();
            }
        }

        int ppgrecinterval = -1;
        public String PPGRecInterval
        {
            get
            {
                return ppgrecinterval.ToString();
            }
            set
            {
                ppgrecinterval = int.Parse(value);
                RaisePropertyChanged();
            }
        }

        int ppgmadefault = -1;
        public String PPG_MA_DEFAULT
        {
            get
            {
                return ppgmadefault.ToString();
            }
            set
            {
                ppgmadefault = int.Parse(value);
                RaisePropertyChanged();
            }
        }

        int ppgmamaxredir = -1;
        public String PPG_MA_MAX_RED_IR
        {
            get
            {
                return ppgmamaxredir.ToString();
            }
            set
            {
                ppgmamaxredir = int.Parse(value);
                RaisePropertyChanged();
            }
        }

        int ppgmamaxgreenblue = -1;
        public String PPG_MA_MAX_GREEN_BLUE
        {
            get
            {
                return ppgmamaxgreenblue.ToString();
            }
            set
            {
                ppgmamaxgreenblue = int.Parse(value);
                RaisePropertyChanged();
            }
        }

        int ppgagctargetpercentofrange = -1;
        public String PPG_AGC_TARGET_PERCENT_OF_RANGE
        {
            get
            {
                return ppgagctargetpercentofrange.ToString();
            }
            set
            {
                ppgagctargetpercentofrange = int.Parse(value);
                RaisePropertyChanged();
            }
        }

        int ppgmaledpilot = -1;
        public String PPG_MA_LED_PILOT
        {
            get
            {
                return ppgmaledpilot.ToString();
            }
            set
            {
                ppgmaledpilot = int.Parse(value);
                RaisePropertyChanged();
            }
        }

        int ppgdaccrosstalk1 = -1;
        public String PPG_XTALK_DAC1
        {
            get
            {
                return ppgdaccrosstalk1.ToString();
            }
            set
            {
                ppgdaccrosstalk1 = int.Parse(value);
                RaisePropertyChanged();
            }
        }

        int ppgdaccrosstalk2 = -1;
        public String PPG_XTALK_DAC2
        {
            get
            {
                return ppgdaccrosstalk2.ToString();
            }
            set
            {
                ppgdaccrosstalk2 = int.Parse(value);
                RaisePropertyChanged();
            }
        }

        int ppgdaccrosstalk3 = -1;
        public String PPG_XTALK_DAC3
        {
            get
            {
                return ppgdaccrosstalk3.ToString();
            }
            set
            {
                ppgdaccrosstalk3 = int.Parse(value);
                RaisePropertyChanged();
            }
        }

        int ppgdaccrosstalk4 = -1;
        public String PPG_XTALK_DAC4
        {
            get
            {
                return ppgdaccrosstalk4.ToString();
            }
            set
            {
                ppgdaccrosstalk4 = int.Parse(value);
                RaisePropertyChanged();
            }
        }

        Sensor.SensorSetting agcmode = SensorPPG.ProxAGCMode.Mode_Unknown;
        public String SelectedProxAGCMode
        {
            get
            {
                return agcmode.GetDisplayName();
            }
            set
            {
                agcmode = Sensor.GetSensorSettingFromDisplayName(SensorPPG.ProxAGCMode.Settings, value);
                SensorDescription = agcmode.GetDescription();
                RaisePropertyChanged();
            }
        }

        Sensor.SensorSetting arange = SensorLIS2DW12.AccelRange.Range_Unknown;
        public String SelectedAccelRange
        {
            get
            {
                return arange.GetDisplayName();
            }
            set
            {
                arange = Sensor.GetSensorSettingFromDisplayName(SensorLIS2DW12.AccelRange.Settings, value);
                SensorDescription = arange.GetDescription();
                RaisePropertyChanged();
            }
        }

        Sensor.SensorSetting arate = SensorLIS2DW12.LowPerformanceAccelSamplingRate.Rate_Unknown;
        public String SelectedAccelRate
        {
            get
            {
                return arate.GetDisplayName();
            }
            set
            {
                arate = Sensor.GetSensorSettingFromDisplayName(SensorLIS2DW12.LowPerformanceAccelSamplingRate.Settings, value);
                SensorDescription = arate.GetDescription();
                RaisePropertyChanged();
            }
        }

        Sensor.SensorSetting amode = SensorLIS2DW12.Mode.Mode_Unknown;
        public String SelectedAccelMode
        {
            get
            {
                return amode.GetDisplayName();
            }
            set
            {
                amode = Sensor.GetSensorSettingFromDisplayName(SensorLIS2DW12.Mode.Settings, value);
                SensorDescription = amode.GetDescription();
                RaisePropertyChanged();
            }
        }

        Sensor.SensorSetting lpmode = SensorLIS2DW12.LowPowerMode.Mode_Unknown;
        public String SelectedAccelLPMode
        {
            get
            {
                return lpmode.GetDisplayName();
            }
            set
            {
                lpmode = Sensor.GetSensorSettingFromDisplayName(SensorLIS2DW12.LowPowerMode.Settings, value);
                SensorDescription = lpmode.GetDescription();
                RaisePropertyChanged();
            }
        }

        Sensor.SensorSetting ppgrate = SensorPPG.SamplingRate.Rate_Unknown;
        public String SelectedPPGRate
        {
            get
            {
                return ppgrate.GetDisplayName();
            }
            set
            {
                ppgrate = Sensor.GetSensorSettingFromDisplayName(shimmer.Sensors.SensorPPG.SamplingRate.Settings, value);
                //SensorDescription = ppgrate.GetDescription();
                RaisePropertyChanged();
            }
        }

        Sensor.SensorSetting ppgwidth = SensorPPG.LEDPulseWidth.Pulse_Width_Unknown;
        public String SelectedPPGWidth
        {
            get
            {
                return ppgwidth.GetDisplayName();
            }
            set
            {
                ppgwidth = Sensor.GetSensorSettingFromDisplayName(shimmer.Sensors.SensorPPG.LEDPulseWidth.Settings, value);
                SensorDescription = ppgwidth.GetDescription();
                RaisePropertyChanged();
            }
        }

        Sensor.SensorSetting ppgsavg = SensorPPG.SampleAverage.Sample_Average_Unknown;
        public String SelectedPPGSamplingAverage
        {
            get
            {
                return ppgsavg.GetDisplayName();
            }
            set
            {
                ppgsavg = Sensor.GetSensorSettingFromDisplayName(shimmer.Sensors.SensorPPG.SampleAverage.Settings, value);
                SensorDescription = ppgsavg.GetDescription();
                RaisePropertyChanged();
            }
        }

        Sensor.SensorSetting ppgrange = SensorPPG.ADCRange.Range_Unknown;
        public String SelectedPPGRange
        {
            get
            {
                return ppgrange.GetDisplayName();
            }
            set
            {
                ppgrange = Sensor.GetSensorSettingFromDisplayName(shimmer.Sensors.SensorPPG.ADCRange.Settings, value);
                SensorDescription = ppgrange.GetDescription();
                RaisePropertyChanged();
            }
        }

        VerisenseDevice.DeviceByteArraySettings _selectedDeviceConfiguration = VerisenseDevice.DefaultVerisenseConfiguration.Unknown_Device_OpConfig_Setting;
        public String SelectedDeviceConfiguration
        {
            get
            {
                return _selectedDeviceConfiguration.GetDisplayName();
            }
            set
            {
                _selectedDeviceConfiguration = VerisenseDevice.GetDeviceOpSettingFromDisplayName(VerisenseDevice.DefaultVerisenseConfiguration.Settings, value);
                DeviceMessage = _selectedDeviceConfiguration.GetDescription();
                RaisePropertyChanged();
            }
        }

        Logging LoggingAccel1;
        Logging LoggingAccel2;
        Logging LoggingGSR;
        Logging LoggingPPG;

        public Chart Chart { get; private set; }
        public Chart ChartGyro { get; private set; }
        public PlotModel PlotModel { get; set; }

        public List<string> PlotSignalsAvailable { get; set; } = new List<string>();
        public string SelectedPlotSignal { get; set; }
        private List<string[]> PlotSignalsArray = new List<string[]>();
        public ObservableCollection<CheckBoxModel> PopulateSignalsListView { get; set; } = new ObservableCollection<CheckBoxModel>();
        ObservableCollection<CheckBoxModel> populateSignalsListView = new ObservableCollection<CheckBoxModel>();
        List<CheckBoxModel> populateSignalsListViewTemp = new List<CheckBoxModel>();
        List<string> plotSignalsAvailableTemp = new List<string>();

        private bool IsFirstOjc = true;
        private bool IsUpdateSignalsToPlot = false;

        private void OxyPlotDataProcess(ObjectCluster ojc)
        {
            try
            {
                List<string[]> signalsToPlotList = PlotManager.GetAllSignalPropertiesFromOjc(ojc);
                foreach (string[] signalsToPlotArray in signalsToPlotList)
                {
                    string signal = GetSignalToPlotStringFromArray(signalsToPlotArray);

                    if (IsFirstOjc || !plotSignalsAvailableTemp.Contains(signal))
                    {
                        IsUpdateSignalsToPlot = true;
                        plotSignalsAvailableTemp.Add(signal);
                        PlotSignalsArray.Add(signalsToPlotArray);
                        populateSignalsListView = new ObservableCollection<CheckBoxModel>(populateSignalsListViewTemp);
                        CheckBoxModel checkBoxModel = new CheckBoxModel { Title = signal, Signals = PlotSignalsArray, CBMPlotManager = PlotManager };
                        populateSignalsListViewTemp.Add(checkBoxModel);
                        populateSignalsListView.Add(checkBoxModel);

                        if (signalsToPlotArray[(int)SignalArrayIndex.Name].Equals(ShimmerConfiguration.SignalNames.SYSTEM_TIMESTAMP))
                        {
                            PlotManager.AddXAxis(signalsToPlotArray);
                        }
                    }
                }
                if (IsUpdateSignalsToPlot)
                {
                    PopulateSignalsListView = populateSignalsListView;
                    RaisePropertyChanged(nameof(this.PopulateSignalsListView));
                    IsFirstOjc = false;
                    IsUpdateSignalsToPlot = false;
                }
                PlotManager.FilterDataAndPlot(ojc);
                RaisePropertyChanged(nameof(this.PlotModel));
            }
            catch (Exception ex)
            {
                Debug.WriteLine("OxyPlot Exception: " + ex);
            }
        }

        public static string GetSignalToPlotStringFromArray(string[] array)
        {
            return array[(int)SignalArrayIndex.Name] + " " + array[(int)SignalArrayIndex.Format] + " " + array[(int)SignalArrayIndex.Unit];
        }

        public DeviceListViewModel(IBluetoothLE bluetoothLe, IAdapter adapter, IUserDialogs userDialogs, ISettings settings, IPermissions permissions) : base(adapter)
        {
            
            CurrentPage = new NavigationPage(new DeviceListPage());
            Application.Current.MainPage = CurrentPage;
            _permissions = permissions;
            _bluetoothLe = bluetoothLe;
            _userDialogs = userDialogs;
            _settings = settings;

            // quick and dirty :>
            BLEManager = new DeviceManagerPluginBLE();
            
            _bluetoothLe.StateChanged += OnStateChanged;
            Adapter.DeviceDiscovered += OnDeviceDiscovered;
            Adapter.DeviceAdvertised += OnDeviceDiscovered;
            Adapter.DeviceDisconnected += OnDeviceDisconnected;
            Adapter.DeviceConnectionLost += OnDeviceConnectionLost;
            //Adapter.DeviceConnected += (sender, e) => Adapter.DisconnectDeviceAsync(e.Device);

            Adapter.ScanMode = ScanMode.LowLatency;
            bleManager.BLEManagerEvent += BLEManager_BLEEvent;

            PlotModel = PlotManager.BuildPlotModel(); //Init the plot properties
            RaisePropertyChanged(nameof(this.PlotModel)); //Notify the UI element that its data model has changed
            
        }
        ConcurrentQueue<Entry> entriesX = new ConcurrentQueue<Entry>();
        ConcurrentQueue<Entry> entriesY = new ConcurrentQueue<Entry>();
        ConcurrentQueue<Entry> entriesZ = new ConcurrentQueue<Entry>();

        DateTime LastDraw = DateTime.Now;
        void PlotData(double x, double y, double z)
        {
            try
            {
                if (entriesX.Count > 250 && Chart.IsAnimated)
                {
                    Entry e;
                    entriesX.TryDequeue(out e);
                    entriesY.TryDequeue(out e);
                    entriesZ.TryDequeue(out e);
                }
                Entry entryX = new Entry((float)x);
                Entry entryY = new Entry((float)y);
                Entry entryZ = new Entry((float)z);
                entriesX.Enqueue(entryX);
                entriesY.Enqueue(entryY);
                entriesZ.Enqueue(entryZ);
                SKPaint skp = new SKPaint();
                skp.Color = SKColor.Parse("#2c3e50");
                Chart = new LineChart
                {
                    LegendOption = SeriesLegendOption.Top,
                    LineMode = LineMode.Straight,
                    LabelOrientation = Orientation.Horizontal,
                    ValueLabelOrientation = Orientation.Horizontal,
                    LabelTextSize = 42,
                    ValueLabelTextSize = 18,
                    SerieLabelTextSize = 42,
                    ShowYAxisLines = true,
                    ShowYAxisText = true,
                    YAxisPosition = Position.Left,
                    Series = new List<ChartSerie>()
                    {
                        new ChartSerie()
                        {
                            Name = "X AXIS",
                            Color = SKColor.Parse("#2c3e50"),
                            Entries = entriesX,
                        },
                        new ChartSerie()
                        {
                            Name = "Y AXIS",
                            Color = SKColor.Parse("#77d065"),
                            Entries = entriesY,
                        },
                        new ChartSerie()
                        {
                            Name = "Z AXIS",
                            Color = SKColor.Parse("#b455b6"),
                            Entries = entriesZ,
                        },
                    }
                };
                ((LineChart)Chart).PointSize = 0;
                Chart.MaxValue = 20;
                Chart.MinValue = -20;
                Chart.AnimationDuration = new TimeSpan(0);
                if ((DateHelper.GetTimestamp(DateTime.Now) - DateHelper.GetTimestamp(LastDraw)) > 1000)
                {
                    LastDraw = DateTime.Now;
                    RaisePropertyChanged(nameof(this.Chart));
                }
            }
            catch (Exception ex)
            {
                System.Console.WriteLine();
            }
        }

        ConcurrentQueue<Entry> entriesGX = new ConcurrentQueue<Entry>();
        ConcurrentQueue<Entry> entriesGY = new ConcurrentQueue<Entry>();
        ConcurrentQueue<Entry> entriesGZ = new ConcurrentQueue<Entry>();

        DateTime LastDrawGyro = DateTime.Now;
        void PlotGyroData(double x, double y, double z)
        {
            try
            {
                if (entriesGX.Count > 250 && ChartGyro.IsAnimated)
                {
                    Entry e;
                    entriesGX.TryDequeue(out e);
                    entriesGY.TryDequeue(out e);
                    entriesGZ.TryDequeue(out e);
                }
                Entry entryX = new Entry((float)x);
                Entry entryY = new Entry((float)y);
                Entry entryZ = new Entry((float)z);
                entriesGX.Enqueue(entryX);
                entriesGY.Enqueue(entryY);
                entriesGZ.Enqueue(entryZ);
                SKPaint skp = new SKPaint();
                skp.Color = SKColor.Parse("#2c3e50");
                ChartGyro = new LineChart
                {
                    LegendOption = SeriesLegendOption.Top,
                    LineMode = LineMode.Straight,
                    LabelOrientation = Orientation.Horizontal,
                    ValueLabelOrientation = Orientation.Horizontal,
                    LabelTextSize = 42,
                    ValueLabelTextSize = 18,
                    SerieLabelTextSize = 42,
                    ShowYAxisLines = true,
                    ShowYAxisText = true,
                    YAxisPosition = Position.Left,
                    Series = new List<ChartSerie>()
                    {
                        new ChartSerie()
                        {
                            Name = "X AXIS",
                            Color = SKColor.Parse("#2c3e50"),
                            Entries = entriesGX,
                        },
                        new ChartSerie()
                        {
                            Name = "Y AXIS",
                            Color = SKColor.Parse("#77d065"),
                            Entries = entriesGY,
                        },
                        new ChartSerie()
                        {
                            Name = "Z AXIS",
                            Color = SKColor.Parse("#b455b6"),
                            Entries = entriesGZ,
                        },
                    }
                };
                ((LineChart)ChartGyro).PointSize = 0;
                ChartGyro.MaxValue = 250;
                ChartGyro.MinValue = -250;
                ChartGyro.AnimationDuration = new TimeSpan(0);
                if ((DateHelper.GetTimestamp(DateTime.Now) - DateHelper.GetTimestamp(LastDrawGyro)) > 1000)
                {
                    LastDrawGyro = DateTime.Now;
                    RaisePropertyChanged(nameof(this.ChartGyro));
                }
            }
            catch (Exception ex)
            {
                System.Console.WriteLine();
            }
        }

        void OnCheckBoxCheckedChanged(object sender, CheckedChangedEventArgs e)
        {
            // Perform required operation after examining e.Value
            System.Console.WriteLine();
        }
        private Task GetPreviousGuidAsync()
        {
            return Task.Run(() =>
            {
                var guidString = _settings.GetValueOrDefault("lastguid", string.Empty);
                PreviousGuid = !string.IsNullOrEmpty(guidString) ? Guid.Parse(guidString) : Guid.Empty;
            });
        }
        private Task GetPairingStatusAsync()
        {
            return Task.Run(() =>
            {
                PairingStatus = "";
            });
        }

        private void OnDeviceConnectionLost(object sender, DeviceErrorEventArgs e)
        {
            Devices.FirstOrDefault(d => d.Id == e.Device.Id)?.Update();

            _userDialogs.HideLoading();
            _userDialogs.ErrorToast("Error", $"Connection LOST {e.Device.Name}", TimeSpan.FromMilliseconds(6000));
        }

        private void OnStateChanged(object sender, BluetoothStateChangedArgs e)
        {
            RaisePropertyChanged(nameof(IsStateOn));
            RaisePropertyChanged(nameof(StateText));
            //TryStartScanning();
        }

        private string GetStateText()
        {
            switch (_bluetoothLe.State)
            {
                case BluetoothState.Unknown:
                    return "Unknown BLE state.";
                case BluetoothState.Unavailable:
                    return "BLE is not available on this device.";
                case BluetoothState.Unauthorized:
                    return "You are not allowed to use BLE.";
                case BluetoothState.TurningOn:
                    return "BLE is warming up, please wait.";
                case BluetoothState.On:
                    return "BLE is on.";
                case BluetoothState.TurningOff:
                    return "BLE is turning off. That's sad!";
                case BluetoothState.Off:
                    return "BLE is off. Turn it on!";
                default:
                    return "Unknown BLE state.";
            }
        }

        private void OnDeviceDiscovered(object sender, DeviceEventArgs args)
        {
            //AddOrUpdateDevice(args.Device);
        }

        private void AddOrUpdateDevice(VerisenseBLEScannedDevice device)
        {
            InvokeOnMainThread(() =>
            {
                var vm = Devices.FirstOrDefault(d => d.Device.ID == device.ID);
                if (vm != null)
                {
                    vm.Update();
                }
                else
                {
                    Devices.Add(new DeviceListItemViewModel(device));
                }
            });
        }
        
        public override async void ViewAppeared()
        {
            base.ViewAppeared();

            CheckTime = true;
            await GetPreviousGuidAsync();
            await GetPairingStatusAsync();
            //TryStartScanning();

            if (Device.RuntimePlatform == Device.UWP || Device.RuntimePlatform == Device.Android)
            {
                await serialPortManager.StartScanForSerialPorts();
                foreach (var device in serialPortManager.GetListOfSerialDevices())
                {
                    SerialDevices.Add(new VerisenseSerialDevice(device.Id));
                }
            }
            GetSystemConnectedOrPairedDevices();
        }

        private void GetSystemConnectedOrPairedDevices()
        {
            /*
            try
            {
                //heart rate
                var guid = Guid.Parse("0000180d-0000-1000-8000-00805f9b34fb");

                // SystemDevices = Adapter.GetSystemConnectedOrPairedDevices(new[] { guid }).Select(d => new DeviceListItemViewModel(d)).ToList();
                // remove the GUID filter for test
                // Avoid to loose already IDevice with a connection, otherwise you can't close it
                // Keep the reference of already known devices and drop all not in returned list.
                var pairedOrConnectedDeviceWithNullGatt = Adapter.GetSystemConnectedOrPairedDevices();
                SystemDevices.RemoveAll(sd => !pairedOrConnectedDeviceWithNullGatt.Any(p => p.Id == sd.Id));
                SystemDevices.AddRange(pairedOrConnectedDeviceWithNullGatt.Where(d => !SystemDevices.Any(sd => sd.Id == d.Id)).Select(d => new DeviceListItemViewModel(d)));
                RaisePropertyChanged(() => SystemDevices);
            }
            catch (Exception ex)
            {
                Trace.Message("Failed to retreive system connected devices. {0}", ex.Message);
            }
            */
        }

        public List<DeviceListItemViewModel> SystemDevices { get; private set; } = new List<DeviceListItemViewModel>();

        public override void ViewDisappeared()
        {
            base.ViewDisappeared();

            Adapter.StopScanningForDevicesAsync();
            RaisePropertyChanged(() => IsRefreshing);
        }

        private async void TryStartScanning(bool refresh = false)
        {
            /*
            if (Xamarin.Forms.Device.RuntimePlatform == Device.Android)
            {
                var status = await _permissions.CheckPermissionStatusAsync(Permission.Location);
                if (status != PermissionStatus.Granted)
                {
                    var permissionResult = await _permissions.RequestPermissionsAsync(Permission.Location);

                    if (permissionResult.First().Value != PermissionStatus.Granted)
                    {
                        await _userDialogs.AlertAsync("Permission denied. Not scanning.");
                        _permissions.OpenAppSettings();
                        return;
                    }
                }
            }

            if (IsStateOn && (refresh || !Devices.Any()) && !IsRefreshing)
            {
                ScanForDevices();
            }
            */
            if (Device.RuntimePlatform == Device.UWP || Device.RuntimePlatform == Device.Android)
            {
                SerialDevices.Clear();
                await serialPortManager.StartScanForSerialPorts();
                foreach (var device in serialPortManager.GetListOfSerialDevices())
                {
                    SerialDevices.Add(new VerisenseSerialDevice(device.Id));
                }
            }
            bleManager.StartScanForDevices();
        }

        private async void ScanForDevices()
        {
            /*
            Devices.Clear();

            foreach (var connectedDevice in Adapter.ConnectedDevices)
            {
                //update rssi for already connected evices (so tha 0 is not shown in the list)
                try
                {
                    await connectedDevice.UpdateRssiAsync();
                }
                catch (Exception ex)
                {
                    Trace.Message(ex.Message);
                    await _userDialogs.AlertAsync($"Failed to update RSSI for {connectedDevice.Name}");
                }

                AddOrUpdateDevice(connectedDevice);
            }

            _cancellationTokenSource = new CancellationTokenSource();
            await RaisePropertyChanged(() => StopScanCommand);

            await RaisePropertyChanged(() => IsRefreshing);
            Adapter.ScanMode = ScanMode.LowLatency;
            //await Adapter.StartScanningForDevicesAsync(_cancellationTokenSource.Token);
            BLEManager.StartScanForDevices();
            */
        }

        private async void DisconnectDevice(DeviceListItemViewModel device)
        {
            /*
            try
            {
                if (!device.IsConnected)
                    return;

                _userDialogs.ShowLoading($"Disconnecting {device.Name}...");

                await Adapter.DisconnectDeviceAsync(device.Device);
            }
            catch (Exception ex)
            {
                await _userDialogs.AlertAsync(ex.Message, "Disconnect error");
            }
            finally
            {
                device.Update();
                _userDialogs.HideLoading();
            }
            */
        }

        private void HandleSelectedDevice(DeviceListItemViewModel device)
        {
            
            var config = new ActionSheetConfig();
            PreviousGuid = device.Id;
            if (Device.RuntimePlatform != Device.iOS)
            {
                if (device.Device.IsPaired)
                {
                    PairingStatus = "Is Paired";
                }
                else
                {
                    PairingStatus = "Not Paired";
                }
            }
            else
            {
                PairingKeyGenerator = new VerisenseBLEPairingKeyGenerator();

                PairingStatus = "Pairing Key: " + PairingKeyGenerator.CalculatePairingPin(device.Name);
            }
               
            selectedDevice = device.Device;
        }

        private async Task<bool> ConnectDeviceAsync(DeviceListItemViewModel device, bool showPrompt = true)
        {
            return true;
            /*
            if (showPrompt && !await _userDialogs.ConfirmAsync($"Connect to device '{device.Name}'?"))
            {
                return false;
            }

            CancellationTokenSource tokenSource = new CancellationTokenSource();
            try
            {
                var config = new ProgressDialogConfig()
                {
                    Title = $"Connecting to '{device.Id}'",
                    CancelText = "Cancel",
                    IsDeterministic = false,
                    OnCancel = tokenSource.Cancel
                };

                using (var progress = _userDialogs.Progress(config))
                {
                    progress.Show();

                    await Adapter.ConnectToDeviceAsync(device.Device, new ConnectParameters(autoConnect: UseAutoConnect, forceBleTransport: true), tokenSource.Token);
                }

                await _userDialogs.AlertAsync($"Connected to {device.Device.Name}.");

                PreviousGuid = device.Device.Id;
                return true;

            }
            catch (Exception ex)
            {
                await _userDialogs.AlertAsync(ex.Message, "Connection error");
                Trace.Message(ex.Message);
                return false;
            }
            finally
            {
                _userDialogs.HideLoading();
                device.Update();
                tokenSource.Dispose();
                tokenSource = null;
            }
            */
        }


        public MvxCommand ConnectToPreviousCommand => new MvxCommand(ConnectToPreviousDeviceAsync, CanConnectToPrevious);

        private async void ConnectToPreviousDeviceAsync()
        {
            /*
            IDevice device;
            CancellationTokenSource tokenSource = new CancellationTokenSource();
            try
            {
                var config = new ProgressDialogConfig()
                {
                    Title = $"Searching for '{PreviousGuid}'",
                    CancelText = "Cancel",
                    IsDeterministic = false,
                    OnCancel = tokenSource.Cancel
                };

                using (var progress = _userDialogs.Progress(config))
                {
                    progress.Show();

                    device = await Adapter.ConnectToKnownDeviceAsync(PreviousGuid, new ConnectParameters(autoConnect: UseAutoConnect, forceBleTransport: false), tokenSource.Token);

                }

                await _userDialogs.AlertAsync($"Connected to {device.Name}.");

                var deviceItem = Devices.FirstOrDefault(d => d.Device.Id == device.Id);
                if (deviceItem == null)
                {
                    deviceItem = new DeviceListItemViewModel(device);
                    Devices.Add(deviceItem);
                }
                else
                {
                    deviceItem.Update(device);
                }
            }
            catch (Exception ex)
            {
                _userDialogs.ErrorToast(string.Empty, ex.Message, TimeSpan.FromSeconds(5));
                return;
            }
            finally
            {
                tokenSource.Dispose();
                tokenSource = null;
            }
            */
        }

        private bool CanConnectToPrevious()
        {
            return PreviousGuid != default;
        }

        private async void ConnectAndDisposeDevice(DeviceListItemViewModel item)
        {
            /*
            try
            {
                using (item.Device)
                {
                    _userDialogs.ShowLoading($"Connecting to {item.Name} ...");
                    await Adapter.ConnectToDeviceAsync(item.Device);

                    // TODO make this configurable
                    var resultMTU = await item.Device.RequestMtuAsync(60);
                    System.Diagnostics.Debug.WriteLine($"Requested MTU. Result is {resultMTU}");

                    // TODO make this configurable
                    var resultInterval = item.Device.UpdateConnectionInterval(ConnectionInterval.High);
                    System.Diagnostics.Debug.WriteLine($"Set Connection Interval. Result is {resultInterval}");

                    item.Update();
                    await _userDialogs.AlertAsync($"Connected {item.Device.Name}");

                    _userDialogs.HideLoading();
                    for (var i = 5; i >= 1; i--)
                    {
                        _userDialogs.ShowLoading($"Disconnect in {i}s...");

                        await Task.Delay(1000);

                        _userDialogs.HideLoading();
                    }
                }
            }
            catch (Exception ex)
            {
                await _userDialogs.AlertAsync(ex.Message, "Failed to connect and dispose.");
            }
            finally
            {
                _userDialogs.HideLoading();
            }
            */

        }

        private void OnDeviceDisconnected(object sender, DeviceEventArgs e)
        {
            Devices.FirstOrDefault(d => d.Id == e.Device.Id)?.Update();
            _userDialogs.HideLoading();
            _userDialogs.Toast($"Disconnected {e.Device.Name}", TimeSpan.FromSeconds(3));

            Console.WriteLine($"Disconnected {e.Device.Name}");
        }

        public MvxCommand<DeviceListItemViewModel> CopyGuidCommand => new MvxCommand<DeviceListItemViewModel>(device =>
        {
            PreviousGuid = device.Id;
        });

        bool __checkTime;
        public bool CheckTime
        {
            get => __checkTime;

            set
            {
                if (__checkTime == value)
                    return;

                __checkTime = value;
                RaisePropertyChanged();
            }
        }

        double PrintScreenTime = 0;
        double PlotTime = 0;
        double UIUpdateInterval = 100;
        double PlotUpdateInterval = 100;
        private void ShimmerDevice_BLEEvent(object sender, ShimmerBLEEventData e)
        {
            if (e.CurrentEvent == VerisenseBLEEvent.NewDataPacket)
            {
                DateTime UnixEpoch = new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc);
                double time = (DateTime.UtcNow - UnixEpoch).TotalMilliseconds;
                ObjectCluster ojc = ((ObjectCluster)e.ObjMsg);
                if (__checkTime)
                {
                    if ((time - PlotTime) > PlotUpdateInterval)
                    {
                        PlotTime = time;
                        OxyPlotDataProcess(ojc);
                    }
                }
                else
                {
                    OxyPlotDataProcess(ojc);
                }
                if (ojc.GetNames().Contains(SensorLIS2DW12.ObjectClusterSensorName.LIS2DW12_ACC_X))
                {
                    if (LoggingAccel1 == null && _LogToFile)
                    {
                        var folder = Path.Combine(DependencyService.Get<ILocalFolderService>().GetAppLocalFolder());
                        if (!Directory.Exists(folder))
                        {
                            Directory.CreateDirectory(folder);
                        }
                        LoggingAccel1 = new Logging(Path.Combine(folder, time.ToString() + "SensorLIS2DW12.csv"), ",");
                    }
                    if (_LogToFile)
                    {
                        LoggingAccel1.WriteData(ojc);
                    }
                    var a2x = ojc.GetData(SensorLIS2DW12.ObjectClusterSensorName.LIS2DW12_ACC_X, ShimmerConfiguration.SignalFormats.CAL, ShimmerConfiguration.SignalUnits.MeterPerSecondSquared_DefaultCal);
                    var a2y = ojc.GetData(SensorLIS2DW12.ObjectClusterSensorName.LIS2DW12_ACC_Y, ShimmerConfiguration.SignalFormats.CAL, ShimmerConfiguration.SignalUnits.MeterPerSecondSquared_DefaultCal);
                    var a2z = ojc.GetData(SensorLIS2DW12.ObjectClusterSensorName.LIS2DW12_ACC_Z, ShimmerConfiguration.SignalFormats.CAL, ShimmerConfiguration.SignalUnits.MeterPerSecondSquared_DefaultCal);
                    //PlotData(a2x.Data, a2y.Data, a2z.Data);
                    System.Console.WriteLine("New Data Packet: " + "  X : " + a2x.Data + "  Y : " + a2y.Data + "  Z : " + a2z.Data);
                    if ((time - PrintScreenTime) > UIUpdateInterval)
                    {
                        PrintScreenTime = time;
                        DeviceMessage = SensorLIS2DW12.SensorName + " New Data Packet: " + "  X : " + Math.Round(a2x.Data, 2) + "  Y : " + Math.Round(a2y.Data, 2) + "  Z : " + Math.Round(a2z.Data, 2);
                    }
                }
                if (ojc.GetNames().Contains(SensorLSM6DS3.ObjectClusterSensorName.LSM6DS3_ACC_X)
                    || ojc.GetNames().Contains(SensorLSM6DS3.ObjectClusterSensorName.LSM6DS3_GYRO_X))
                {
                    if (LoggingAccel2 == null && _LogToFile)
                    {
                        var folder = Path.Combine(DependencyService.Get<ILocalFolderService>().GetAppLocalFolder());
                        if (!Directory.Exists(folder))
                        {
                            Directory.CreateDirectory(folder);
                        }
                        LoggingAccel2 = new Logging(Path.Combine(folder, time.ToString() + "SensorLSM6DS3.csv"), ",");

                    }

                    if (_LogToFile)
                    {
                        LoggingAccel2.WriteData(ojc);
                    }
                    string datamsg=SensorLSM6DS3.SensorName + " New Data Packet: ";
                    if (ojc.GetNames().Contains(SensorLSM6DS3.ObjectClusterSensorName.LSM6DS3_ACC_X))
                    {
                        var a2x = ojc.GetData(SensorLSM6DS3.ObjectClusterSensorName.LSM6DS3_ACC_X, ShimmerConfiguration.SignalFormats.CAL);
                        var a2y = ojc.GetData(SensorLSM6DS3.ObjectClusterSensorName.LSM6DS3_ACC_Y, ShimmerConfiguration.SignalFormats.CAL);
                        var a2z = ojc.GetData(SensorLSM6DS3.ObjectClusterSensorName.LSM6DS3_ACC_Z, ShimmerConfiguration.SignalFormats.CAL);
                        //PlotData(a2x.Data, a2y.Data, a2z.Data);
                        System.Console.WriteLine("New Data Packet: " + "  X : " + a2x.Data + "  Y : " + a2y.Data + "  Z : " + a2z.Data);
                        datamsg = "ACCEL =  X : " + Math.Round(a2x.Data, 2) + "  Y : " + Math.Round(a2y.Data, 2) + "  Z : " + Math.Round(a2z.Data, 2) + " ; ";
                    }
                    if (ojc.GetNames().Contains(SensorLSM6DS3.ObjectClusterSensorName.LSM6DS3_GYRO_X))
                    {
                        var g2x = ojc.GetData(SensorLSM6DS3.ObjectClusterSensorName.LSM6DS3_GYRO_X, ShimmerConfiguration.SignalFormats.CAL);
                        var g2y = ojc.GetData(SensorLSM6DS3.ObjectClusterSensorName.LSM6DS3_GYRO_Y, ShimmerConfiguration.SignalFormats.CAL);
                        var g2z = ojc.GetData(SensorLSM6DS3.ObjectClusterSensorName.LSM6DS3_GYRO_Z, ShimmerConfiguration.SignalFormats.CAL);
                        //PlotGyroData(g2x.Data, g2y.Data, g2z.Data);
                        System.Console.WriteLine("New Data Packet: " + "  X : " + g2x.Data + "  Y : " + g2y.Data + "  Z : " + g2z.Data);
                        datamsg = datamsg + "Gyro = X : " + Math.Round(g2x.Data, 2) + "  Y : " + Math.Round(g2y.Data, 2) + "  Z : " + Math.Round(g2z.Data, 2);
                    }
                    if ((time - PrintScreenTime) > UIUpdateInterval)
                    {
                        PrintScreenTime = time;
                        DeviceMessage = datamsg;
                    }
                }
                if (ojc.GetNames().Contains(shimmer.Sensors.SensorGSR.ObjectClusterSensorName.GSR) || ojc.GetNames().Contains(shimmer.Sensors.SensorGSR.ObjectClusterSensorName.Batt))
                {
                    if (LoggingGSR == null && _LogToFile)
                    {
                        var folder = Path.Combine(DependencyService.Get<ILocalFolderService>().GetAppLocalFolder());
                        if (!Directory.Exists(folder))
                        {
                            Directory.CreateDirectory(folder);
                        }
                        LoggingGSR = new Logging(Path.Combine(folder, time.ToString() + "SensorGSR.csv"), ",");
                    }
                    if (_LogToFile)
                    {
                        LoggingGSR.WriteData(ojc);
                    }
                    string datamsg = shimmer.Sensors.SensorGSR.SensorName + " New Data Packet: ";
                    var gsrR = ojc.GetData(shimmer.Sensors.SensorGSR.ObjectClusterSensorName.GSR, ShimmerConfiguration.SignalFormats.CAL, ShimmerConfiguration.SignalUnits.KiloOhms);
                    var gsrC = ojc.GetData(shimmer.Sensors.SensorGSR.ObjectClusterSensorName.GSR, ShimmerConfiguration.SignalFormats.CAL, ShimmerConfiguration.SignalUnits.MicroSiemens);
                    var batt = ojc.GetData(shimmer.Sensors.SensorGSR.ObjectClusterSensorName.Batt, ShimmerConfiguration.SignalFormats.CAL);
                    if ((time - PrintScreenTime) > UIUpdateInterval)
                    {
                        PrintScreenTime = time;
                        DeviceMessage = datamsg + (gsrR!=null? " GSR CAL (kOhms): " + gsrR.Data : "") + (gsrC!=null? " GSR CAL (uS): " + gsrC.Data : "") + (batt!=null? " BATT CAL (mV): " + batt.Data : "");
                    }
                }

                if (ojc.GetNames().Contains(shimmer.Sensors.SensorPPG.ObjectClusterSensorName.PPG_BLUE)
                    || ojc.GetNames().Contains(shimmer.Sensors.SensorPPG.ObjectClusterSensorName.PPG_RED)
                    || ojc.GetNames().Contains(shimmer.Sensors.SensorPPG.ObjectClusterSensorName.PPG_GREEN)
                    || ojc.GetNames().Contains(shimmer.Sensors.SensorPPG.ObjectClusterSensorName.PPG_IR))
                {
                    if (LoggingPPG == null && _LogToFile)
                    {
                        var folder = Path.Combine(DependencyService.Get<ILocalFolderService>().GetAppLocalFolder());
                        if (!Directory.Exists(folder))
                        {
                            Directory.CreateDirectory(folder);
                        }
                        LoggingPPG = new Logging(Path.Combine(folder, time.ToString() + "SensorPPG.csv"), ",");
                    }
                    if (_LogToFile)
                    {
                        LoggingPPG.WriteData(ojc);
                    }
                    string datamsg = SensorPPG.SensorName + " New Data Packet: ";
                    var ppgRed = ojc.GetData(SensorPPG.ObjectClusterSensorName.PPG_RED, ShimmerConfiguration.SignalFormats.CAL);
                    var ppgIR = ojc.GetData(SensorPPG.ObjectClusterSensorName.PPG_IR, ShimmerConfiguration.SignalFormats.CAL);
                    var ppgGreen = ojc.GetData(SensorPPG.ObjectClusterSensorName.PPG_GREEN, ShimmerConfiguration.SignalFormats.CAL);
                    var ppgBlue = ojc.GetData(SensorPPG.ObjectClusterSensorName.PPG_BLUE, ShimmerConfiguration.SignalFormats.CAL);
                    if ((time - PrintScreenTime) > UIUpdateInterval)
                    {
                        PrintScreenTime = time; 
                        DeviceMessage = (ppgRed!=null?"PPG Red (nA):"+ppgRed.Data:"") + (ppgIR!=null? " PPG IR (nA): " + ppgIR.Data:"") + (ppgGreen != null ? " PPG Green (nA): " + ppgGreen.Data:"") + (ppgBlue != null ? " PPG Blue (nA): " + ppgBlue.Data:"");
                    }
                }


            }
            else if (e.CurrentEvent == VerisenseBLEEvent.StateChange)
            {
                Debug.WriteLine("SHIMMER DEVICE BLE EVENT: " + VerisenseBLEDevice.GetVerisenseBLEState().ToString());
                DeviceState = "Device State: " + VerisenseBLEDevice.GetVerisenseBLEState().ToString();
                if (VerisenseBLEDevice.GetVerisenseBLEState().Equals(ShimmerDeviceBluetoothState.Connected) && ReconnectTimer == null) //null means its not trying to reconnect
                {
                    StateToContinue = ShimmerDeviceBluetoothState.Connected;
                }

                ShimmerDeviceBluetoothState previousState = CurrentState;
                CurrentState = VerisenseBLEDevice.GetVerisenseBLEState();
                if ((previousState.Equals(ShimmerDeviceBluetoothState.StreamingLoggedData) || previousState.Equals(ShimmerDeviceBluetoothState.Streaming))
                && CurrentState.Equals(ShimmerDeviceBluetoothState.Disconnected)) //if it went from a streaming state to disconnect, we want to remember what the previous state was so can execute it upon reconnect
                {
                    StateToContinue = previousState;
                }

                if ((previousState.Equals(ShimmerDeviceBluetoothState.Connected) || previousState.Equals(ShimmerDeviceBluetoothState.Streaming) || previousState.Equals(ShimmerDeviceBluetoothState.StreamingLoggedData) || previousState.Equals(ShimmerDeviceBluetoothState.Connecting)) &&
                    VerisenseBLEDevice.GetVerisenseBLEState().Equals(ShimmerDeviceBluetoothState.Disconnected) && !DisconnectPressed && AutoReconnect)
                {
                    ReconnectTimer = new System.Threading.Timer(Reconnect, null, 5000, Timeout.Infinite);
                }

                DisconnectPressed = false;
            }
            else if (e.CurrentEvent == VerisenseBLEEvent.SyncLoggedDataNewPayload)
            {
                DeviceMessage = VerisenseBLEDevice.dataFilePath + " : " + e.Message;
            }
            else if (e.CurrentEvent == VerisenseBLEEvent.SyncLoggedDataComplete)
            {
                DeviceMessage = VerisenseBLEDevice.dataFilePath + " : " + e.CurrentEvent.ToString();
                InvokeOnMainThread(() =>
                {
                    CurrentPage.DisplayAlert("Sync Complete", "You can refer to https://github.com/ShimmerEngineering/Shimmer-C-API/blob/master/ShimmerBLE/FileParser/README.md regarding how to parse the bin file", "OK");
                });
            }
            else if (e.CurrentEvent == VerisenseBLEEvent.RequestResponse)
            {
                if ((RequestType)e.ObjMsg == RequestType.ReadStatus)
                {
                    DeviceMessage = "Battery % =" + VerisenseBLEDevice.GetStatus().BatteryPercent + " UsbPowered =" + VerisenseBLEDevice.GetStatus().UsbPowered;
                }
                else if ((RequestType)e.ObjMsg == RequestType.EraseData)
                {
                    DeviceMessage = "Onboard Sensor Data Erased";
                }
                else if ((RequestType)e.ObjMsg == RequestType.ReadRTC)
                {
                    var minutes = VerisenseBLEDevice.GetLastReceivedRTC().Minutes;
                    double time = (double)(VerisenseBLEDevice.GetLastReceivedRTC().Minutes * 60) + VerisenseBLEDevice.GetLastReceivedRTC().RemainingSeconds;
                    DeviceMessage = "Current Sensor Time : " + DateHelper.GetDateTimeFromSeconds(time).ToString();
                }
                else if ((RequestType)e.ObjMsg == RequestType.ReadProductionConfig)
                {
                    DeviceMessage = "Hardware Version =v" + VerisenseBLEDevice.GetProductionConfig().REV_HW_MAJOR + "." + VerisenseBLEDevice.GetProductionConfig().REV_HW_MINOR + " Firmware Version =v" + VerisenseBLEDevice.GetProductionConfig().REV_FW_MAJOR + "." + VerisenseBLEDevice.GetProductionConfig().REV_FW_MINOR + "." + VerisenseBLEDevice.GetProductionConfig().REV_FW_INTERNAL;
                }
                else if ((RequestType)e.ObjMsg == RequestType.EraseData)
                {
                    DeviceMessage = "Successfully delete data from the sensor.";
                }
                else if ((RequestType)e.ObjMsg == RequestType.ReadOperationalConfig)
                {
                    DeviceMessage = "Operational Config Received";

                    DeviceLogging = VerisenseBLEDevice.IsLoggingEnabled();
                    DeviceEnabled = VerisenseBLEDevice.IsDeviceEnabled();
                    BluetoothEnabled = VerisenseBLEDevice.IsBluetoothEnabled();
                    USBEnabled = VerisenseBLEDevice.IsUSBEnabled();
                    SensorAccel = ((SensorLIS2DW12)VerisenseBLEDevice.GetSensor(SensorLIS2DW12.SensorName)).IsAccelEnabled();
                    SensorAccel2 = ((SensorLSM6DS3)VerisenseBLEDevice.GetSensor(SensorLSM6DS3.SensorName)).IsAccelEnabled();
                    SensorGyro = ((SensorLSM6DS3)VerisenseBLEDevice.GetSensor(SensorLSM6DS3.SensorName)).IsGyroEnabled();
                    if (((shimmer.Sensors.SensorGSR)VerisenseBLEDevice.GetSensor(shimmer.Sensors.SensorGSR.SensorName)).GetOversamplingRate() != null)
                    {
                        SelectedGSROversamplingRate = ((shimmer.Sensors.SensorGSR)VerisenseBLEDevice.GetSensor(shimmer.Sensors.SensorGSR.SensorName)).GetOversamplingRate().GetDisplayName();
                    }
                    SensorGSR = ((shimmer.Sensors.SensorGSR)VerisenseBLEDevice.GetSensor(shimmer.Sensors.SensorGSR.SensorName)).IsGSREnabled();
                    SensorBatt = ((shimmer.Sensors.SensorGSR)VerisenseBLEDevice.GetSensor(shimmer.Sensors.SensorGSR.SensorName)).IsBattEnabled();
                    SensorPPGBlue = ((SensorPPG)VerisenseBLEDevice.GetSensor(SensorPPG.SensorName)).IsPPGBlueEnabled();
                    SensorPPGGreen = ((SensorPPG)VerisenseBLEDevice.GetSensor(SensorPPG.SensorName)).IsPPGGreenEnabled();
                    SensorPPGIR = ((SensorPPG)VerisenseBLEDevice.GetSensor(SensorPPG.SensorName)).IsPPGIREnabled();
                    SensorPPGRed = ((SensorPPG)VerisenseBLEDevice.GetSensor(SensorPPG.SensorName)).IsPPGRedEnabled();
                    SelectedAccelRange = ((SensorLIS2DW12)VerisenseBLEDevice.GetSensor(SensorLIS2DW12.SensorName)).GetAccelRange().GetDisplayName();
                    if (((SensorLIS2DW12)VerisenseBLEDevice.GetSensor(SensorLIS2DW12.SensorName)).GetMode().GetConfigurationValue() == SensorLIS2DW12.Mode.Low_Power_Mode.GetConfigurationValue())
                    {
                        //NOTE THIS UI UPDATE DOESNT WORK
                        AccelRates = SensorLIS2DW12.LowPerformanceAccelSamplingRate.Settings.Select(setting => setting.GetDisplayName()).ToList();
                    }
                    else
                    {
                        //NOTE THIS UI UPDATE DOESNT WORK
                        AccelRates = SensorLIS2DW12.HighPerformanceAccelSamplingRate.Settings.Select(setting => setting.GetDisplayName()).ToList();
                    }
                    SelectedAccelRate = ((SensorLIS2DW12)VerisenseBLEDevice.GetSensor(SensorLIS2DW12.SensorName)).GetSamplingRate().GetDisplayName();
                    SelectedAccelMode = ((SensorLIS2DW12)VerisenseBLEDevice.GetSensor(SensorLIS2DW12.SensorName)).GetMode().GetDisplayName();
                    SelectedAccelLPMode = ((SensorLIS2DW12)VerisenseBLEDevice.GetSensor(SensorLIS2DW12.SensorName)).GetLowPowerMode().GetDisplayName();
                    SelectedBandwidthFilter = ((SensorLIS2DW12)VerisenseBLEDevice.GetSensor(SensorLIS2DW12.SensorName)).GetAccelBandwidthFilter().GetDisplayName();
                    FIFOthreshold = ((SensorLIS2DW12)VerisenseBLEDevice.GetSensor(SensorLIS2DW12.SensorName)).GetAccelFIFOThreshold().ToString();
                    SelectedFMode = ((SensorLIS2DW12)VerisenseBLEDevice.GetSensor(SensorLIS2DW12.SensorName)).GetAccelFMode().GetDisplayName();

                    SensorAccelLowNoise = ((SensorLIS2DW12)VerisenseBLEDevice.GetSensor(SensorLIS2DW12.SensorName)).IsLowNoiseEnabled();
                    SensorAccelHighPassFilter = ((SensorLIS2DW12)VerisenseBLEDevice.GetSensor(SensorLIS2DW12.SensorName)).IsHighPassFilterEnabled();
                    SensorAccelHighPassFilterRefMode = ((SensorLIS2DW12)VerisenseBLEDevice.GetSensor(SensorLIS2DW12.SensorName)).IsHighPassFilterRefModeEnabled();

                    SelectedAccel2Range = ((SensorLSM6DS3)VerisenseBLEDevice.GetSensor(SensorLSM6DS3.SensorName)).GetAccelRange().GetDisplayName();
                    SelectedAccel2GyroRate = ((SensorLSM6DS3)VerisenseBLEDevice.GetSensor(SensorLSM6DS3.SensorName)).GetSamplingRate().GetDisplayName();
                    
                    SelectedGyroFIFODecimation = ((SensorLSM6DS3)VerisenseBLEDevice.GetSensor(SensorLSM6DS3.SensorName)).GetGyroFIFODecimationSetting().GetDisplayName();
                    SelectedAccel2FIFODecimation = ((SensorLSM6DS3)VerisenseBLEDevice.GetSensor(SensorLSM6DS3.SensorName)).GetAccelFIFODecimationSetting().GetDisplayName();
                    SelectedFIFOOutputDataRate = ((SensorLSM6DS3)VerisenseBLEDevice.GetSensor(SensorLSM6DS3.SensorName)).GetFIFOOutputDataRateSetting().GetDisplayName();
                    SelectedFIFOMode = ((SensorLSM6DS3)VerisenseBLEDevice.GetSensor(SensorLSM6DS3.SensorName)).GetFIFOModeSetting().GetDisplayName();
                    SelectedAccel2AntiAliasingFilterBW = ((SensorLSM6DS3)VerisenseBLEDevice.GetSensor(SensorLSM6DS3.SensorName)).GetAccelAntiAliasingFilterBWSetting().GetDisplayName();
                    SelectedHPFilterCutOffFreq = ((SensorLSM6DS3)VerisenseBLEDevice.GetSensor(SensorLSM6DS3.SensorName)).GetHPFilterCutOffFreqSetting().GetDisplayName();
                    SelectedGyroRange = ((SensorLSM6DS3)VerisenseBLEDevice.GetSensor(SensorLSM6DS3.SensorName)).GetGyroRange().GetDisplayName();
                    SensorGyroFullScaleAt125 = ((SensorLSM6DS3)VerisenseBLEDevice.GetSensor(SensorLSM6DS3.SensorName)).IsGyroFullScaleAt125Enabled();
                    //SensorGyroHighPerformance = ((SensorLSM6DS3)VerisenseBLEDevice.GetSensor(SensorLSM6DS3.SensorName)).IsHighPerformanceOpModeEnabled();
                    SensorGyroHighPassFilter = ((SensorLSM6DS3)VerisenseBLEDevice.GetSensor(SensorLSM6DS3.SensorName)).IsHighPassFilterEnabled();
                    SensorGyroDigitalHPReset = ((SensorLSM6DS3)VerisenseBLEDevice.GetSensor(SensorLSM6DS3.SensorName)).IsDigitalHPFilterResetEnabled();
                    SourceRegisterRoundingStatus = ((SensorLSM6DS3)VerisenseBLEDevice.GetSensor(SensorLSM6DS3.SensorName)).IsSourceRegRoundingStatusEnabled();
                    StepCounterAndTimestamp = ((SensorLSM6DS3)VerisenseBLEDevice.GetSensor(SensorLSM6DS3.SensorName)).IsStepCounterAndTSDataEnabled();
                    WriteInFIFOAtEveryStepDetected = ((SensorLSM6DS3)VerisenseBLEDevice.GetSensor(SensorLSM6DS3.SensorName)).IsWriteInFIFOAtEveryStepDetectedEnabled();
                    Accel2LowPassFilter = ((SensorLSM6DS3)VerisenseBLEDevice.GetSensor(SensorLSM6DS3.SensorName)).IsAccelLowPassFilterEnabled();
                    Accel2SlopeOrHighPassFilter = ((SensorLSM6DS3)VerisenseBLEDevice.GetSensor(SensorLSM6DS3.SensorName)).IsAccelSlopeOrHighPassFilterEnabled();
                    Accel2LowPassFilterOn6D = ((SensorLSM6DS3)VerisenseBLEDevice.GetSensor(SensorLSM6DS3.SensorName)).IsAccelLowPassFilterOn6DEnabled();

                    /*
                    if (((SensorLSM6DS3)VerisenseBLEDevice.GetSensor(SensorLSM6DS3.SensorName)).GetGyroRate() != null)
                    {
                        SelectedGyroRate = ((SensorLSM6DS3)VerisenseBLEDevice.GetSensor(SensorLSM6DS3.SensorName)).GetGyroRate().GetDisplayName();
                    }
                    */
                    if (((SensorLSM6DS3)VerisenseBLEDevice.GetSensor(SensorLSM6DS3.SensorName)).GetFIFOThreshold() != null)
                    {
                        SelectedFIFOThreshold = ((SensorLSM6DS3)VerisenseBLEDevice.GetSensor(SensorLSM6DS3.SensorName)).GetFIFOThreshold().GetDisplayName();
                    }
                    if (((shimmer.Sensors.SensorGSR)VerisenseBLEDevice.GetSensor(shimmer.Sensors.SensorGSR.SensorName)).GetGSRRange() != null)
                    {
                        SelectedGSRRange = ((shimmer.Sensors.SensorGSR)VerisenseBLEDevice.GetSensor(shimmer.Sensors.SensorGSR.SensorName)).GetGSRRange().GetDisplayName();
                    }
                    if (((shimmer.Sensors.SensorGSR)VerisenseBLEDevice.GetSensor(shimmer.Sensors.SensorGSR.SensorName)).GetSamplingRate() != null)
                    {
                        SelectedGSRRate = ((shimmer.Sensors.SensorGSR)VerisenseBLEDevice.GetSensor(shimmer.Sensors.SensorGSR.SensorName)).GetSamplingRate().GetDisplayName();
                    }

                    //PPG
                    if (((SensorPPG)VerisenseBLEDevice.GetSensor(SensorPPG.SensorName)).GetPPGRecordingDurationinSeconds() != -1)
                    {
                        PPGRecDuration = ((SensorPPG)VerisenseBLEDevice.GetSensor(SensorPPG.SensorName)).GetPPGRecordingDurationinSeconds().ToString();
                    }

                    if (((SensorPPG)VerisenseBLEDevice.GetSensor(SensorPPG.SensorName)).GetPPGRecordingIntervalinMinutes() != -1)
                    {
                        PPGRecInterval = ((SensorPPG)VerisenseBLEDevice.GetSensor(SensorPPG.SensorName)).GetPPGRecordingIntervalinMinutes().ToString();
                    }

                    if (((SensorPPG)VerisenseBLEDevice.GetSensor(SensorPPG.SensorName)).GetPGGDefaultLEDPulseAmplitude() != -1)
                    {
                        PPG_MA_DEFAULT = ((SensorPPG)VerisenseBLEDevice.GetSensor(SensorPPG.SensorName)).GetPGGDefaultLEDPulseAmplitude().ToString();
                    }
                    if (((SensorPPG)VerisenseBLEDevice.GetSensor(SensorPPG.SensorName)).GetMaxLEDPulseAmplitudeRedIR() != -1)
                    {
                        PPG_MA_MAX_RED_IR = ((SensorPPG)VerisenseBLEDevice.GetSensor(SensorPPG.SensorName)).GetMaxLEDPulseAmplitudeRedIR().ToString();
                    }
                    if (((SensorPPG)VerisenseBLEDevice.GetSensor(SensorPPG.SensorName)).GetMaxLEDPulseAmplitudeGreenBlue() != -1)
                    {
                        PPG_MA_MAX_GREEN_BLUE = ((SensorPPG)VerisenseBLEDevice.GetSensor(SensorPPG.SensorName)).GetMaxLEDPulseAmplitudeGreenBlue().ToString();
                    }
                    if (((SensorPPG)VerisenseBLEDevice.GetSensor(SensorPPG.SensorName)).GetAGCTargetRange() != -1)
                    {
                        PPG_AGC_TARGET_PERCENT_OF_RANGE = ((SensorPPG)VerisenseBLEDevice.GetSensor(SensorPPG.SensorName)).GetAGCTargetRange().ToString();
                    }
                    if (((SensorPPG)VerisenseBLEDevice.GetSensor(SensorPPG.SensorName)).GetLEDPilotPulseAmplitude() != -1)
                    {
                        PPG_MA_LED_PILOT = ((SensorPPG)VerisenseBLEDevice.GetSensor(SensorPPG.SensorName)).GetLEDPilotPulseAmplitude().ToString();
                    }
                    if (((SensorPPG)VerisenseBLEDevice.GetSensor(SensorPPG.SensorName)).GetDAC1CROSSTALK() != -1)
                    {
                        PPG_XTALK_DAC1 = ((SensorPPG)VerisenseBLEDevice.GetSensor(SensorPPG.SensorName)).GetDAC1CROSSTALK().ToString();
                    }
                    if (((SensorPPG)VerisenseBLEDevice.GetSensor(SensorPPG.SensorName)).GetDAC2CROSSTALK() != -1)
                    {
                        PPG_XTALK_DAC2 = ((SensorPPG)VerisenseBLEDevice.GetSensor(SensorPPG.SensorName)).GetDAC2CROSSTALK().ToString();
                    }
                    if (((SensorPPG)VerisenseBLEDevice.GetSensor(SensorPPG.SensorName)).GetDAC3CROSSTALK() != -1)
                    {
                        PPG_XTALK_DAC3 = ((SensorPPG)VerisenseBLEDevice.GetSensor(SensorPPG.SensorName)).GetDAC3CROSSTALK().ToString();
                    }
                    if (((SensorPPG)VerisenseBLEDevice.GetSensor(SensorPPG.SensorName)).GetDAC4CROSSTALK() != -1)
                    {
                        PPG_XTALK_DAC4 = ((SensorPPG)VerisenseBLEDevice.GetSensor(SensorPPG.SensorName)).GetDAC4CROSSTALK().ToString();
                    }
                    if (((SensorPPG)VerisenseBLEDevice.GetSensor(SensorPPG.SensorName)).GetProxAGCMode() != null)
                    {
                        SelectedProxAGCMode = ((SensorPPG)VerisenseBLEDevice.GetSensor(SensorPPG.SensorName)).GetProxAGCMode().GetDisplayName();
                    }
                    if (((SensorPPG)VerisenseBLEDevice.GetSensor(SensorPPG.SensorName)).GetPPGSampleAverage() != null)
                    {
                        SelectedPPGSamplingAverage = ((SensorPPG)VerisenseBLEDevice.GetSensor(SensorPPG.SensorName)).GetPPGSampleAverage().GetDisplayName();
                    }
                    if (((SensorPPG)VerisenseBLEDevice.GetSensor(SensorPPG.SensorName)).GetPPGPulseWidth() != null)
                    {
                        SelectedPPGWidth = ((SensorPPG)VerisenseBLEDevice.GetSensor(SensorPPG.SensorName)).GetPPGPulseWidth().GetDisplayName();
                    }
                    if (((SensorPPG)VerisenseBLEDevice.GetSensor(SensorPPG.SensorName)).GetSamplingRate() != null)
                    {
                        SelectedPPGRate = ((SensorPPG)VerisenseBLEDevice.GetSensor(SensorPPG.SensorName)).GetSamplingRate().GetDisplayName();
                    }
                    if (((SensorPPG)VerisenseBLEDevice.GetSensor(SensorPPG.SensorName)).GetPPGRange() != null)
                    {
                        SelectedPPGRange = ((SensorPPG)VerisenseBLEDevice.GetSensor(SensorPPG.SensorName)).GetPPGRange().GetDisplayName();
                    }

                    //Device Settings

                    DateTime StartDateAndTime = VerisenseBLEDevice.ConvertUnixTSInMinuteIntoDateTime(VerisenseBLEDevice.GetStartTimeinMinutes());
                    //StartDateAndTime = StartDateAndTime.ToLocalTime();
                    SelectedStartTime = StartDateAndTime.TimeOfDay;
                    SelectedStartDate = StartDateAndTime.Date;

                    DateTime EndDateAndTime = VerisenseBLEDevice.ConvertUnixTSInMinuteIntoDateTime(VerisenseBLEDevice.GetEndTimeinMinutes());
                    //EndDateAndTime = EndDateAndTime.ToLocalTime();
                    SelectedEndTime = EndDateAndTime.TimeOfDay;
                    SelectedEndDate = EndDateAndTime.Date;

                    if (VerisenseBLEDevice.GetBLETXPower() != null)
                    {
                        SelectedRadioOutputPower = VerisenseBLEDevice.GetBLETXPower().GetDisplayName();
                    }

                    if (VerisenseBLEDevice.GetBLERetryCount() != -1)
                    {
                        BLEWakeupRetryCount = VerisenseBLEDevice.GetBLERetryCount().ToString();
                    }
                    if (VerisenseBLEDevice.GetDataTransferInterval() != -1)
                    {
                        DataTransferInterval = VerisenseBLEDevice.GetDataTransferInterval().ToString();
                    }
                    if (VerisenseBLEDevice.GetDataTransferStartTime() != -1)
                    {
                        DataTransferTime = VerisenseBLEDevice.GetDataTransferStartTime().ToString();
                    }
                    if (VerisenseBLEDevice.GetDataTransferDuration() != -1)
                    {
                        DataTransferDuration = VerisenseBLEDevice.GetDataTransferDuration().ToString();
                    }
                    if (VerisenseBLEDevice.GetDataTransferRetryInterval() != -1)
                    {
                        DataTransferRetryInterval = VerisenseBLEDevice.GetDataTransferRetryInterval().ToString();
                    }
                    if (VerisenseBLEDevice.GetStatusInterval() != -1)
                    {
                        StatusTransferInterval = VerisenseBLEDevice.GetStatusInterval().ToString();
                    }
                    if (VerisenseBLEDevice.GetStatusStartTime() != -1)
                    {
                        StatusTransferTime = VerisenseBLEDevice.GetStatusStartTime().ToString();
                    }
                    if (VerisenseBLEDevice.GetStatusDuration() != -1)
                    {
                        StatusTransferDuration = VerisenseBLEDevice.GetStatusDuration().ToString();
                    }
                    if (VerisenseBLEDevice.GetStatusRetryInterval() != -1)
                    {
                        StatusTransferRetryInterval = VerisenseBLEDevice.GetStatusRetryInterval().ToString();
                    }
                    if (VerisenseBLEDevice.GetRTCSyncInterval() != -1)
                    {
                        RTCSyncInterval = VerisenseBLEDevice.GetRTCSyncInterval().ToString();
                    }
                    if (VerisenseBLEDevice.GetRTCSyncTime() != -1)
                    {
                        RTCSyncTime = VerisenseBLEDevice.GetRTCSyncTime().ToString();
                    }
                    if (VerisenseBLEDevice.GetRTCSyncDuration() != -1)
                    {
                        RTCSyncDuration = VerisenseBLEDevice.GetRTCSyncDuration().ToString();
                    }
                    if (VerisenseBLEDevice.GetRTCSyncRetryInterval() != -1)
                    {
                        RTCSyncRetryInterval = VerisenseBLEDevice.GetRTCSyncRetryInterval().ToString();
                    }
                }
            }
        }

        protected async void Upload()
        {
            //string localFolder = ApplicationData.Current.LocalFolder.Path + "\\testfolderupload";

            try
            {
                var res = GetDemoSettingsJsonFile();
                if (res)
                {
                    InitializeCloudManager();
                    var pathToUpload = Path.Combine(DependencyService.Get<ILocalFolderService>().GetAppLocalFolder(), VerisenseBLEDevice.binFileFolderDir);
                    var result = await cloudManager.UploadFile(pathToUpload);
                }
            }
            catch (Exception e)
            {
                await CurrentPage.DisplayAlert("Error", e.Message, "OK");
            }
        }

        public bool GetDemoSettingsJsonFile()
        {
            try
            {
                var demoSettingsFilePath = Path.Combine(DependencyService.Get<ILocalFolderService>().GetAppLocalFolder(), "VerisenseAPIDemoSettings.json");
                if (!File.Exists(demoSettingsFilePath))
                {
                    CurrentPage.DisplayAlert("Error", "No VerisenseAPIDemoSettings.json file found. For further details on the json file please refer to ReadMe. Please put json file in " + DependencyService.Get<ILocalFolderService>().GetAppLocalFolder() + " and retry.", "OK");
                    return false;
                }
                StreamReader r = new StreamReader(demoSettingsFilePath);
                string jsonString = r.ReadToEnd();
                verisenseApiDemoSettings = JsonConvert.DeserializeObject<VerisenseAPIDemoSettings>(jsonString);
                return true;
            }
            catch(Exception e)
            {
                return false;
            }
          
        }

        public void InitializeCloudManager()
        {
            S3CloudInfo s3CloudInfo = new S3CloudInfo
            {
                S3AccessKey = verisenseApiDemoSettings.S3CloudInfo.S3AccessKey,
                S3SecretKey = verisenseApiDemoSettings.S3CloudInfo.S3SecretKey,
                S3RegionName = verisenseApiDemoSettings.S3CloudInfo.S3RegionName,
                S3BucketName = verisenseApiDemoSettings.S3CloudInfo.S3BucketName,
                S3SubFolder = VerisenseBLEDevice.binFileFolderDir
            };

            cloudManager = new S3CloudManager(s3CloudInfo);
            cloudManager.CloudManagerEvent += CloudManager_Event;
            cloudManager.DeleteAfterUpload = true;
        }
        
        protected async void Connect(bool initialize = true)
        {
            if (VerisenseBLEDevice != null)
            {
                if (!VerisenseBLEDevice.GetVerisenseBLEState().Equals(ShimmerDeviceBluetoothState.Disconnected) ||
                    VerisenseBLEDevice.GetVerisenseBLEState().Equals(ShimmerDeviceBluetoothState.None) ||
                    VerisenseBLEDevice.GetVerisenseBLEState().Equals(ShimmerDeviceBluetoothState.Limited))
                {
                    _userDialogs.Toast($"Device is already connected", TimeSpan.FromSeconds(3));
                    return;
                }
            }
            if (ReconnectTimer != null)
            {
                ReconnectTimer.Dispose();
            }

            if (selectedSerialDevice != null && (Device.RuntimePlatform == Device.UWP || Device.RuntimePlatform == Device.Android))
            {
                VerisenseBLEDevice = serialPortManager.CreateVerisenseSerialDevice(PreviousGuid.ToString(), "SensorName", selectedSerialDevice.Id);
            }
            else if (VerisenseBLEDevice != null)
            {
                VerisenseBLEDevice.ShimmerBLEEvent -= ShimmerDevice_BLEEvent;
                if (!VerisenseBLEDevice.Asm_uuid.Equals(PreviousGuid))
                {
                    VerisenseBLEDevice = new VerisenseBLEDevice(PreviousGuid.ToString(), "SensorName");
                }
            }
            else
            {
                VerisenseBLEDevice = new VerisenseBLEDevice(PreviousGuid.ToString(), "SensorName");
            }

            VerisenseBLEDevice.ShimmerBLEEvent += ShimmerDevice_BLEEvent;
            VerisenseBLEDevice.SetParticipantID(ParticipantID);
            VerisenseBLEDevice.SetTrialName(TrialName);
            if (initialize)
            {
                VerisenseBLEDevice.Connect(true, VerisenseDevice.GetDeviceOpSettingFromDisplayName(VerisenseDevice.DefaultVerisenseConfiguration.Settings, SelectedDeviceConfiguration), KeepDeviceSettings);
            }
            else
            {
                VerisenseBLEDevice.Connect(false);
            }

        }
        bool DisconnectPressed = false;
        protected async void Disconnect()
        {
            DisconnectPressed = true;
            ResetPlotVars();
            if (ReconnectTimer != null)
            {
                ReconnectTimer.Dispose();
            }
            await VerisenseBLEDevice.Disconnect();
        }
        protected async void ReadStatus()
        {
            var result = await VerisenseBLEDevice.ExecuteRequest(RequestType.ReadStatus);
            Debug.WriteLine(BitConverter.ToString(((StatusPayload)result).GetPayloadWithHeader()).Replace("-", ",0x"));
        }

        protected async void ReadProdConf()
        {
            var result = await VerisenseBLEDevice.ExecuteRequest(RequestType.ReadProductionConfig);
            Debug.WriteLine(BitConverter.ToString(((ProdConfigPayload)result).GetPayloadWithHeader()).Replace("-", ",0x"));
        }

        protected async void ReadOpConf()
        {
            var result = await VerisenseBLEDevice.ExecuteRequest(RequestType.ReadOperationalConfig);
            Debug.WriteLine(BitConverter.ToString(((OpConfigPayload)result).GetPayloadWithHeader()).Replace("-", ",0x"));
        }

        protected Boolean TryingToReconnect= false;
        System.Threading.Timer ReconnectTimer = null;

        protected async void Reconnect(Object obj)
        {
            if (VerisenseBLEDevice.GetVerisenseBLEState().Equals(ShimmerDeviceBluetoothState.Disconnected))
            {
                try
                {
                    /*VerisenseBLEDevice.ShimmerBLEEvent -= ShimmerDevice_BLEEvent;
                    VerisenseBLEDevice = null;
                    VerisenseBLEDevice = new VerisenseBLEDevice(PreviousGuid.ToString(), "DemoSensor");
                    VerisenseBLEDevice.ShimmerBLEEvent += ShimmerDevice_BLEEvent;
                    */
                    var result = await VerisenseBLEDevice.Connect(true);
                    if (result)
                    {
                        if (StateToContinue.Equals(ShimmerDeviceBluetoothState.Streaming))
                        {
                            VerisenseBLEDevice.ExecuteRequest(RequestType.StartStreaming);
                        }
                        else if (StateToContinue.Equals(ShimmerDeviceBluetoothState.StreamingLoggedData))
                        {
                            VerisenseBLEDevice.ExecuteRequest(RequestType.TransferLoggedData);
                        }
                        ReconnectTimer = null;
                    }

                }
                catch (Exception ex)
                {
                    Debug.WriteLine(ex.ToString());
                }
            }
        }
        protected async void WriteTime()
        {
            VerisenseBLEDevice.ExecuteRequest(RequestType.WriteRTC);
        }

        protected async void ReadTime()
        {
            VerisenseBLEDevice.ExecuteRequest(RequestType.ReadRTC);
        }

        protected async void EraseData()
        {
            DeviceMessage = "Please wait, erasing data";
            VerisenseBLEDevice.ExecuteRequest(RequestType.EraseData);
        }

        protected async void StopScan()
        {
            bleManager.StopScanForDevices();
        }

        protected async void ResetSensor()
        {
            VerisenseBLEDevice.ExecuteRequest(RequestType.Reset);
        }

        protected async void ConfigureSensor()
        {
            var clone = new VerisenseBLEDevice(VerisenseBLEDevice);
            if (SensorGSR)
            {
                ((shimmer.Sensors.SensorGSR)clone.GetSensor(shimmer.Sensors.SensorGSR.SensorName)).SetGSREnabled(true);    
            }
            else
            {
                ((shimmer.Sensors.SensorGSR)clone.GetSensor(shimmer.Sensors.SensorGSR.SensorName)).SetGSREnabled(false);
            }
            ((shimmer.Sensors.SensorGSR)clone.GetSensor(shimmer.Sensors.SensorGSR.SensorName)).SetOversamplingRate(gsroversamplingrate);
            if (SensorBatt)
            {
                ((shimmer.Sensors.SensorGSR)clone.GetSensor(shimmer.Sensors.SensorGSR.SensorName)).SetBattEnabled(true);
            }
            else
            {
                ((shimmer.Sensors.SensorGSR)clone.GetSensor(shimmer.Sensors.SensorGSR.SensorName)).SetBattEnabled(false);
            }
            ((shimmer.Sensors.SensorGSR)clone.GetSensor(shimmer.Sensors.SensorGSR.SensorName)).SetSamplingRate(gsrrate);
            ((shimmer.Sensors.SensorGSR)clone.GetSensor(shimmer.Sensors.SensorGSR.SensorName)).SetGSRRange(gsrrange);

            if (SensorAccel)
            {
                ((SensorLIS2DW12)clone.GetSensor(SensorLIS2DW12.SensorName)).SetAccelEnabled(true);
            }
            else
            {
                ((SensorLIS2DW12)clone.GetSensor(SensorLIS2DW12.SensorName)).SetAccelEnabled(false);
            }
            ((SensorLIS2DW12)clone.GetSensor(SensorLIS2DW12.SensorName)).SetMode(amode);
            ((SensorLIS2DW12)clone.GetSensor(SensorLIS2DW12.SensorName)).SetAccelRange(arange);
            ((SensorLIS2DW12)clone.GetSensor(SensorLIS2DW12.SensorName)).SetSamplingRate(arate);
            ((SensorLIS2DW12)clone.GetSensor(SensorLIS2DW12.SensorName)).SetLPMode(lpmode);
            ((SensorLIS2DW12)clone.GetSensor(SensorLIS2DW12.SensorName)).SetAccelFMode(a2fmode);
            ((SensorLIS2DW12)clone.GetSensor(SensorLIS2DW12.SensorName)).SetBandwidthFilter(a2bandwidth);

            if (SensorAccel2)
            {
                ((SensorLSM6DS3)clone.GetSensor(SensorLSM6DS3.SensorName)).SetAccelEnabled(true);
            }
            else
            {
                ((SensorLSM6DS3)clone.GetSensor(SensorLSM6DS3.SensorName)).SetAccelEnabled(false);
            }
            if (SensorGyro)
            {
                ((SensorLSM6DS3)clone.GetSensor(SensorLSM6DS3.SensorName)).SetGyroEnabled(true);
            }
            else
            {
                ((SensorLSM6DS3)clone.GetSensor(SensorLSM6DS3.SensorName)).SetGyroEnabled(false);
            }
            ((SensorLSM6DS3)clone.GetSensor(SensorLSM6DS3.SensorName)).SetAccelRange(a2range);
            ((SensorLSM6DS3)clone.GetSensor(SensorLSM6DS3.SensorName)).SetSamplingRate(a2gyrorate);
            ((SensorLSM6DS3)clone.GetSensor(SensorLSM6DS3.SensorName)).SetGyroRange(grange);
            ((SensorLSM6DS3)clone.GetSensor(SensorLSM6DS3.SensorName)).SetAccelAntiAliasingFilterBWSetting(accelAntiAliasingBW);
            ((SensorLSM6DS3)clone.GetSensor(SensorLSM6DS3.SensorName)).SetFIFOModeSetting(fifoMode);
            ((SensorLSM6DS3)clone.GetSensor(SensorLSM6DS3.SensorName)).SetHPFilterCutOffFreqSetting(gcutoffFreq);
            ((SensorLSM6DS3)clone.GetSensor(SensorLSM6DS3.SensorName)).SetFIFOOutputDataRateSetting(fifoODR);
            ((SensorLSM6DS3)clone.GetSensor(SensorLSM6DS3.SensorName)).SetAccelFIFODecimationSetting(aFIFODecimation);
            ((SensorLSM6DS3)clone.GetSensor(SensorLSM6DS3.SensorName)).SetGyroFIFODecimationSetting(gFIFODecimation);
            //((SensorLSM6DS3)clone.GetSensor(SensorLSM6DS3.SensorName)).SetGyroRate(grate);
            ((SensorLSM6DS3)clone.GetSensor(SensorLSM6DS3.SensorName)).SetFIFOThreshold(fthreshold);

            if (SensorAccelHighPassFilter)
            {
                ((SensorLIS2DW12)clone.GetSensor(SensorLIS2DW12.SensorName)).SetHighPassFilterEnabled(true);
            }
            else
            {
                ((SensorLIS2DW12)clone.GetSensor(SensorLIS2DW12.SensorName)).SetHighPassFilterEnabled(false);
            }
            if (SensorAccelLowNoise)
            {
                ((SensorLIS2DW12)clone.GetSensor(SensorLIS2DW12.SensorName)).SetLowNoiseEnabled(true);
            }
            else
            {
                ((SensorLIS2DW12)clone.GetSensor(SensorLIS2DW12.SensorName)).SetLowNoiseEnabled(false);
            }
            if (SensorAccelHighPassFilterRefMode)
            {
                ((SensorLIS2DW12)clone.GetSensor(SensorLIS2DW12.SensorName)).SetHighPassFilterRefModeEnabled(true);
            }
            else
            {
                ((SensorLIS2DW12)clone.GetSensor(SensorLIS2DW12.SensorName)).SetHighPassFilterRefModeEnabled(false);
            }
            ((SensorLIS2DW12)clone.GetSensor(SensorLIS2DW12.SensorName)).SetAccelFIFOThreshold(a2FIFOthreshold);
            //if (SensorGyroHighPerformance)
            //{
            //    ((SensorLSM6DS3)clone.GetSensor(SensorLSM6DS3.SensorName)).SetHighPerformanceOpModeEnabled(true);
            //}
            //else
            //{
            //    ((SensorLSM6DS3)clone.GetSensor(SensorLSM6DS3.SensorName)).SetHighPerformanceOpModeEnabled(false);
            //}
            if (SensorGyroHighPassFilter)
            {
                ((SensorLSM6DS3)clone.GetSensor(SensorLSM6DS3.SensorName)).SetHighPassFilterEnabled(true);
            }
            else
            {
                ((SensorLSM6DS3)clone.GetSensor(SensorLSM6DS3.SensorName)).SetHighPassFilterEnabled(false);
            }
            if (SensorGyroDigitalHPReset)
            {
                ((SensorLSM6DS3)clone.GetSensor(SensorLSM6DS3.SensorName)).SetDigitalHPFilterResetEnabled(true);
            }
            else
            {
                ((SensorLSM6DS3)clone.GetSensor(SensorLSM6DS3.SensorName)).SetDigitalHPFilterResetEnabled(false);
            }
            if (SourceRegisterRoundingStatus)
            {
                ((SensorLSM6DS3)clone.GetSensor(SensorLSM6DS3.SensorName)).SetRoundingStatusEnabled(true);
            }
            else
            {
                ((SensorLSM6DS3)clone.GetSensor(SensorLSM6DS3.SensorName)).SetRoundingStatusEnabled(false);
            }
            if (StepCounterAndTimestamp)
            {
                ((SensorLSM6DS3)clone.GetSensor(SensorLSM6DS3.SensorName)).SetStepCounterAndTSDataEnabled(true);
            }
            else
            {
                ((SensorLSM6DS3)clone.GetSensor(SensorLSM6DS3.SensorName)).SetStepCounterAndTSDataEnabled(false);
            }
            if (WriteInFIFOAtEveryStepDetected)
            {
                ((SensorLSM6DS3)clone.GetSensor(SensorLSM6DS3.SensorName)).SetWriteInFIFOAtEveryStepDetectedEnabled(true);
            }
            else
            {
                ((SensorLSM6DS3)clone.GetSensor(SensorLSM6DS3.SensorName)).SetWriteInFIFOAtEveryStepDetectedEnabled(false);
            }
            if (Accel2LowPassFilter)
            {
                ((SensorLSM6DS3)clone.GetSensor(SensorLSM6DS3.SensorName)).SetAccelLowPassFilterEnabled(true);
            }
            else
            {
                ((SensorLSM6DS3)clone.GetSensor(SensorLSM6DS3.SensorName)).SetAccelLowPassFilterEnabled(false);
            }
            if (Accel2SlopeOrHighPassFilter)
            {
                ((SensorLSM6DS3)clone.GetSensor(SensorLSM6DS3.SensorName)).SetAccelSlopeOrHighPassFilterEnabled(true);
            }
            else
            {
                ((SensorLSM6DS3)clone.GetSensor(SensorLSM6DS3.SensorName)).SetAccelSlopeOrHighPassFilterEnabled(false);
            }
            if (Accel2LowPassFilterOn6D)
            {
                ((SensorLSM6DS3)clone.GetSensor(SensorLSM6DS3.SensorName)).SetAccelLowPassFilterOn6DEnabled(true);
            }
            else
            {
                ((SensorLSM6DS3)clone.GetSensor(SensorLSM6DS3.SensorName)).SetAccelLowPassFilterOn6DEnabled(false);
            }
            if (SensorGyroFullScaleAt125)
            {
                ((SensorLSM6DS3)clone.GetSensor(SensorLSM6DS3.SensorName)).SetGyroFullScaleAt125Enabled(true);
            }
            else
            {
                ((SensorLSM6DS3)clone.GetSensor(SensorLSM6DS3.SensorName)).SetGyroFullScaleAt125Enabled(false);
            }
            if (SensorPPGGreen)
            {
                ((SensorPPG)clone.GetSensor(SensorPPG.SensorName)).SetPPGGreenEnabled(true);
            }
            else
            {
                ((SensorPPG)clone.GetSensor(SensorPPG.SensorName)).SetPPGGreenEnabled(false);
            }
            if (SensorPPGRed)
            {
                ((SensorPPG)clone.GetSensor(SensorPPG.SensorName)).SetPPGRedEnabled(true);
            }
            else
            {
                ((SensorPPG)clone.GetSensor(SensorPPG.SensorName)).SetPPGRedEnabled(false);
            }
            if (SensorPPGIR)
            {
                ((SensorPPG)clone.GetSensor(SensorPPG.SensorName)).SetPPGIREnabled(true);
            }
            else
            {
                ((SensorPPG)clone.GetSensor(SensorPPG.SensorName)).SetPPGIREnabled(false);
            }
            if (SensorPPGBlue)
            {
                ((SensorPPG)clone.GetSensor(SensorPPG.SensorName)).SetPPGBlueEnabled(true);
            }
            else
            {
                ((SensorPPG)clone.GetSensor(SensorPPG.SensorName)).SetPPGBlueEnabled(false);
            }

            ((SensorPPG)clone.GetSensor(SensorPPG.SensorName)).SetPPGRecordingDurationinSeconds(ppgrecduration);
            ((SensorPPG)clone.GetSensor(SensorPPG.SensorName)).SetPPGRecordingIntervalinMinutes(ppgrecinterval);
            ((SensorPPG)clone.GetSensor(SensorPPG.SensorName)).SetPGGDefaultLEDPulseAmplitude(ppgmadefault);
            ((SensorPPG)clone.GetSensor(SensorPPG.SensorName)).SetMaxLEDPulseAmplitudeRedIR(ppgmamaxredir);
            ((SensorPPG)clone.GetSensor(SensorPPG.SensorName)).SetMaxLEDPulseAmplitudeGreenBlue(ppgmamaxgreenblue);
            ((SensorPPG)clone.GetSensor(SensorPPG.SensorName)).SetAGCTargetRange(ppgagctargetpercentofrange);
            ((SensorPPG)clone.GetSensor(SensorPPG.SensorName)).SetLEDPilotPulseAmplitude(ppgmaledpilot);
            ((SensorPPG)clone.GetSensor(SensorPPG.SensorName)).SetDAC1CROSSTALK(ppgdaccrosstalk1);
            ((SensorPPG)clone.GetSensor(SensorPPG.SensorName)).SetDAC2CROSSTALK(ppgdaccrosstalk2);
            ((SensorPPG)clone.GetSensor(SensorPPG.SensorName)).SetDAC3CROSSTALK(ppgdaccrosstalk3);
            ((SensorPPG)clone.GetSensor(SensorPPG.SensorName)).SetDAC4CROSSTALK(ppgdaccrosstalk4);
            ((SensorPPG)clone.GetSensor(SensorPPG.SensorName)).SetProxAGCMode(agcmode);
            ((SensorPPG)clone.GetSensor(SensorPPG.SensorName)).SetPPGSampleAverage(ppgsavg);
            ((SensorPPG)clone.GetSensor(SensorPPG.SensorName)).SetSamplingRate(ppgrate);
            ((SensorPPG)clone.GetSensor(SensorPPG.SensorName)).SetPPGPulseWidth(ppgwidth);
            ((SensorPPG)clone.GetSensor(SensorPPG.SensorName)).SetPPGRange(ppgrange);

            var opconfigbytes = clone.GenerateConfigurationBytes();
            var compare = VerisenseBLEDevice.GetOperationalConfigByteArray(); //make sure the byte values havent changed
            Debug.WriteLine(BitConverter.ToString(opconfigbytes).Replace("-", ",0x"));
            VerisenseBLEDevice.WriteAndReadOperationalConfiguration(opconfigbytes);
            ResetPlotVars();
        }
        protected async void ConfigureDevice()
        {
            // DEV-1096: firmware before V2.01.003 stops USB as well when Bluetooth is off, and the
            // sensor is then unreachable. The API keeps Bluetooth on for that firmware; say why here.
            if (!_bluetoothEnabled && !VerisenseBLEDevice.SupportsBluetoothOff())
            {
                BluetoothEnabled = true;
                await CurrentPage.DisplayAlert("Bluetooth kept enabled",
                    "This sensor's firmware cannot run with Bluetooth disabled: it would stop USB as well, and the sensor would be unreachable. " +
                    "Update the sensor to firmware 2.1.3 or later to run it with Bluetooth disabled.", "OK");
            }

            var clone = new VerisenseBLEDevice(VerisenseBLEDevice);
            clone.setLoggingEnabled(_deviceLogging);
            clone.setDeviceEnabled(_deviceEnabled);
            clone.setBluetoothEnabled(_bluetoothEnabled);
            clone.setUSBEnabled(_usbEnabled);
            var startDateTime = startDate.AddMinutes(StartTimeSpan.TotalMinutes);
            DateTime utcStartDateTime = startDateTime.ToUniversalTime();
            var localStartDateTime = utcStartDateTime.ToLocalTime();
            long startDateTimeInMinute = DateHelper.GetTimestamp(localStartDateTime) / 60000;   //convert ms to minute
            clone.SetStartTimeInMinutes(startDateTimeInMinute);

            var endDateTime = endDate.AddMinutes(EndTimeSpan.TotalMinutes);
            DateTime utcEndDateTime = endDateTime.ToUniversalTime();
            var localEndDateTime = utcEndDateTime.ToLocalTime();
            long endDateTimeInMinute = DateHelper.GetTimestamp(localEndDateTime) / 60000;   //convert ms to minute
            clone.SetEndTimeInMinutes(endDateTimeInMinute);

            clone.SetBLETXPower(outputpower.GetConfigurationByte());
            clone.SetDataTransferInterval(datatransferinterval);
            clone.SetDataTransferStartTime(datatransfertime);
            clone.SetDataTransferDuration(datatransferduration);
            clone.SetDataTransferRetryInterval(datatransferretryinterval);

            clone.SetStatusInterval(statustransferinterval);
            clone.SetStatusStartTime(statustransfertime);
            clone.SetStatusDuration(statustransferduration);
            clone.SetStatusRetryInterval(statustransferretryinterval);

            clone.SetRTCSyncInterval(rtcsyncinterval);
            clone.SetRTCSyncTime(rtcsynctime);
            clone.SetRTCSyncDuration(rtcsyncduration);
            clone.SetRTCSyncRetryInterval(rtcsyncretryinterval);

            var opconfigbytes = clone.GenerateConfigurationBytes();
            var compare = VerisenseBLEDevice.GetOperationalConfigByteArray(); //make sure the byte values havent changed
            Debug.WriteLine(BitConverter.ToString(opconfigbytes).Replace("-", ",0x"));
            VerisenseBLEDevice.WriteAndReadOperationalConfiguration(opconfigbytes);
        }
            protected async void EnableAccGyro()
        {
            var clone = new VerisenseBLEDevice(VerisenseBLEDevice);
            var sensor = clone.GetSensor("Accel2");
            ((SensorLSM6DS3)sensor).SetAccelEnabled(true);
            ((SensorLSM6DS3)sensor).SetGyroEnabled(true);
            //((SensorLIS2DW12)sensor).Enabled = false;
            var opconfigbytes = clone.GenerateConfigurationBytes();
            var compare = VerisenseBLEDevice.GetOperationalConfigByteArray(); //make sure the byte values havent changed
            VerisenseBLEDevice.ExecuteRequest(RequestType.WriteOperationalConfig, opconfigbytes);
        }

        protected async void DisableAccGyro()
        {
            var clone = new VerisenseBLEDevice(VerisenseBLEDevice);
            var sensor = clone.GetSensor("Accel2");
            ((SensorLSM6DS3)sensor).SetAccelEnabled(false);
            ((SensorLSM6DS3)sensor).SetGyroEnabled(false);
            //((SensorLIS2DW12)sensor).Enabled = false;
            var opconfigbytes = clone.GenerateConfigurationBytes();
            var compare = VerisenseBLEDevice.GetOperationalConfigByteArray(); //make sure the byte values havent changed
            VerisenseBLEDevice.ExecuteRequest(RequestType.WriteOperationalConfig, opconfigbytes);
        }

        protected async void EnableAccel()
        {
            var clone = new VerisenseBLEDevice(VerisenseBLEDevice);
            var sensor = clone.GetSensor("Accel1");
            //((SensorLSM6DS3)sensor).Gyro_Enabled = true;
            //((SensorLSM6DS3)sensor).Accel2_Enabled = true;
            ((SensorLIS2DW12)sensor).SetAccelEnabled(true);
            var opconfigbytes = clone.GenerateConfigurationBytes();
            var compare = VerisenseBLEDevice.GetOperationalConfigByteArray(); //make sure the byte values havent changed
            VerisenseBLEDevice.ExecuteRequest(RequestType.WriteOperationalConfig, opconfigbytes);
        }

        protected async void DisableAccel()
        {
            var clone = new VerisenseBLEDevice(VerisenseBLEDevice);
            var sensor = clone.GetSensor("Accel1");
            //(SensorLSM6DS3)sensor).Gyro_Enabled = false;
            //((SensorLSM6DS3)sensor).Accel2_Enabled = false;
            ((SensorLIS2DW12)sensor).SetAccelEnabled(false);
            var opconfigbytes = clone.GenerateConfigurationBytes();
            var compare = VerisenseBLEDevice.GetOperationalConfigByteArray(); //make sure the byte values havent changed
            VerisenseBLEDevice.ExecuteRequest(RequestType.WriteOperationalConfig, opconfigbytes);
        }

        protected async void DownloadData()
        {
            /*
            ForegroundSyncService serv = new ForegroundSyncService(PreviousGuid.ToString(), "SensorName", shimmer.Models.CommunicationState.CommunicationMode.ForceDataTransferSync);
            serv.ShimmerBLEEvent += ShimmerDevice_BLEEvent;
            bool success = await serv.GetKnownDevice();
            if (success)
            {
                var data = await serv.ExecuteDataRequest();
            }
            */
            var data = await VerisenseBLEDevice.ExecuteRequest(RequestType.TransferLoggedData);

        }
        protected async void StreamData()
        {
            ResetPlotVars();
            //var data = await SyncService.ExecuteStreamRequest();
            var data = VerisenseBLEDevice.ExecuteRequest(RequestType.StartStreaming);

        }

        protected async void PairDev()
        {
            //var service = DependencyService.Get<IVerisenseBLEPairing>();
            var service = DependencyService.Get<IVerisenseBLEManager>();
            var pairing = await service.PairVerisenseDevice(selectedDevice, new VerisenseBLEPairingKeyGenerator());
            selectedDevice.IsPaired = pairing;

           
            if (selectedDevice.IsPaired)
            {
                PairingStatus = "Is Paired";
            }
            else
            {
                PairingStatus = "Not Paired";
            }


        }
        protected async void StopStream()
        {
            //SyncService.SendStopStreamRequestCommandOnMainThread();
            var data = VerisenseBLEDevice.ExecuteRequest(RequestType.StopStreaming);
            if (LoggingAccel1 != null)
            {
                LoggingAccel1.CloseFile();
            }
            LoggingAccel1 = null;
            if (LoggingAccel2 != null)
            {
                LoggingAccel2.CloseFile();
            }
            LoggingAccel2 = null;
            if (LoggingGSR != null)
            {
                LoggingGSR.CloseFile();
            }
            LoggingGSR = null;
            if (LoggingPPG != null)
            {
                LoggingPPG.CloseFile();
            }
            LoggingPPG = null;
        }
        SpeedTestService serv;
        protected async void TestSpeedStop()
        {
            if (serv != null)
            {
                serv.Disconnect();
            }
        }
        protected async void TestSpeed()
        {
            serv = new SpeedTestService(PreviousGuid.ToString());
            serv.Subscribe(this);
            await serv.GetKnownDevice();
            if (serv.ConnectedASM != null)
            {
                System.Console.WriteLine("Memory Lookup Execution");
                await serv.ExecuteMemoryLookupTableCommand();
            }
            else
            {
                System.Console.WriteLine("Connect Fail");
            }
        }
        
        string displayPath = "Log File Path : " + DependencyService.Get<ILocalFolderService>().GetAppLocalFolder();
        public string LogPath
        {
            protected set
            {
                if (displayPath != value)
                {
                    displayPath = value;
                    RaisePropertyChanged();
                }
            }
            get { return displayPath; }
        }

        string displayText = "Device Messages";
        public string DeviceMessage
        {
            protected set
            {
                if (displayText != value)
                {
                    displayText = value;
                    RaisePropertyChanged();
                }
            }
            get { return displayText; }
        }

        string sensorDescription = "Sensor Description";
        public string SensorDescription
        {
            protected set
            {
                if (sensorDescription != value)
                {
                    sensorDescription = value;
                    RaisePropertyChanged();
                }
            }
            get { return sensorDescription; }
        }

        string deviceSettingDescription = "Device Setting Description";
        public string DeviceSettingDescription
        {
            protected set
            {
                if (deviceSettingDescription != value)
                {
                    deviceSettingDescription = value;
                    RaisePropertyChanged();
                }
            }
            get { return deviceSettingDescription; }
        }

        string deviceState = "Device State: Unknown";
        public string DeviceState
        {
            protected set
            {
                if (deviceState != value)
                {
                    deviceState = value;
                    RaisePropertyChanged();
                }
            }
            get { return deviceState; }
        }
        public void OnCompleted()
        {
            throw new NotImplementedException();
        }

        public void OnError(Exception error)
        {
            throw new NotImplementedException();
        }

        public void OnNext(string value)
        {
            Trace.WriteLine("Works" + value);
            if (value.Contains("Transfer rate")) {
                serv.ExecuteMemoryLookupTableCommand();
                DeviceMessage = value;
            }
        }
        VerisenseBLEScannedDevice DeviceToBePaired;

        private void CloudManager_Event(object sender, CloudManagerEvent e)
        {
            if (e.CurrentEvent == CloudManagerEvent.CloudEvent.UploadSuccessful)
            {
                DeviceMessage = "File Uploaded: " + e.message;
            }
            else if (e.CurrentEvent == CloudManagerEvent.CloudEvent.UploadProgressUpdate)
            {
                DeviceMessage = "File Upload Progress (%): " + e.message;
            }
            else if (e.CurrentEvent == CloudManagerEvent.CloudEvent.UploadFail)
            {
                DeviceMessage = "File Failed to Upload: " + e.message;
            }
            else if (e.CurrentEvent == CloudManagerEvent.CloudEvent.UploadedFileDeleteSuccessful)
            { 
                DeviceMessage = "Uploaded Bin File Deleted From: " + e.message;
            }
            else if (e.CurrentEvent == CloudManagerEvent.CloudEvent.UploadedFileDeleteFail)
            {
                DeviceMessage = "Uploaded Bin File Delete Failed: " + e.message;
            }

        }
        private void BLEManager_BLEEvent(object sender, BLEManagerEvent e)
        {

            if (e.CurrentEvent == BLEManagerEvent.BLEAdapterEvent.ScanCompleted)
            {
                List<VerisenseBLEScannedDevice> devices = new List<VerisenseBLEScannedDevice>();

                foreach (VerisenseBLEScannedDevice device in bleManager.GetListOfScannedDevices())
                {
                    if (device.Name.Contains("Verisense") && device.IsConnectable)
                    {
                        devices.Add(device);
                        AddOrUpdateDevice(device);
                    }
                }

                if (devices.Count > 1)
                {
                    int maxRSSI = -100;
                    foreach (VerisenseBLEScannedDevice item in devices)
                    {
                        if (item.RSSI > maxRSSI)
                        {
                            maxRSSI = item.RSSI;
                            DeviceToBePaired = item;
                        }
                    }
                }
                else
                {
                    if (devices.Count > 0)
                    {
                        DeviceToBePaired = devices[0];
                    }
                }

            }
            else if (e.CurrentEvent == BLEManagerEvent.BLEAdapterEvent.DevicePaired)
            {

            }
            else if (e.CurrentEvent == BLEManagerEvent.BLEAdapterEvent.DeviceDiscovered) {
                VerisenseBLEScannedDevice dev = (VerisenseBLEScannedDevice)e.objMsg;
                AddOrUpdateDevice(dev);
            }
        }

        private void ResetPlotVars()
        {
            IsFirstOjc = true;
            populateSignalsListView.Clear();
            populateSignalsListViewTemp.Clear();
            plotSignalsAvailableTemp.Clear();

            PlotSignalsAvailable.Clear();
            PlotSignalsArray.Clear();
            SelectedPlotSignal = "";
            PlotManager.RemoveAllSignalsFromPlot();
        }
    }

    public class CheckBoxModel
    {
        bool _isChecked = false;
        public bool IsChecked
        {
            get => _isChecked;
            set
            {
                _isChecked = value;
                try
                {
                    if (_isChecked)
                    {
                        AddSignalToPlot();
                    }
                    else if (!_isChecked)
                    {
                        RemoveSignalFromPlot();
                    }
                }
                catch (Exception e)
                {
                    Debug.WriteLine("------Exception Here------> " + e);
                }
            }
        }

        public string Title { get; set; }

        public List<string[]> Signals { get; set; }

        public PlotManager CBMPlotManager { get; set; }

        private void AddSignalToPlot()
        {
            if (Title != null && Title.Length > 0)
            {
                string[] signal = GetPlotSignalArray(Title);
                if(signal != null)
                {
                    if (CBMPlotManager.CheckSignalExists(signal) == false)
                    {
                        CBMPlotManager.AddSignalToPlotDefaultColors(signal);
                    }
                }
            }
        }

        private void RemoveSignalFromPlot()
        {
            if (Title != null && Title.Length > 0)
            {
                string[] signal = GetPlotSignalArray(Title);
                if (signal != null)
                {
                    CBMPlotManager.RemoveSignalFromPlot(signal);
                }
            }
        }

        private string[] GetPlotSignalArray(string signal)
        {
            foreach(string[] signalArray in Signals)
            {
                string signalArrayCombined = DeviceListViewModel.GetSignalToPlotStringFromArray(signalArray);
                if (signalArrayCombined.Equals(signal))
                {
                    return signalArray;
                }
            }
            return null;
        }
    }
}