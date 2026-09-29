using shimmer.Communications;
using shimmer.Helpers;
using shimmer.Models;
using shimmer.Sensors;
using shimmer.Services;
using ShimmerAPI;
using ShimmerBLEAPI.Communications;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Xamarin.Forms;
using static shimmer.Models.CommunicationState;
using static shimmer.Models.ShimmerBLEEventData;
using System.Threading;
using System.Diagnostics;

namespace ShimmerBLEAPI.Devices
{
    /// <summary>
    /// Each instance of this class represent a single Verisense Device. It is used to communicate with said device via Bluetooth Low Energy
    /// </summary>
    public class VerisenseBLEDevice : VerisenseDevice, IDisposable, IVerisenseBLE
    {
        #region Comm Props
        public event EventHandler<ShimmerBLEEventData> ShimmerBLEEvent;

        public byte[] ResponseBuffer { get; set; }

        public CommunicationMode SyncMode { get; set; }

        protected IVerisenseByteCommunication BLERadio;
        private string FinalChunkLogMsgForNack = null;

        int TimerCount;
        int TaskCount;
        int MaximumNumberOfBytesPerBinFile = 100000000; // 100MB limit
        public int GallCallBackErrorCount = 0;

        protected const string LogObject = "VerisenseBLEDevice";
        protected const string BadCRC = "BadCRC";
        #endregion

        #region Data Props
        protected string TrialName = "UnknownTrialName";
        protected string ParticipantID = "UnknownParticipantID";
        bool NewPayload { get; set; }
        bool NewStreamPayload { get; set; }
        bool NewCommandPayload { get; set; }

        public bool ReceivingData { get; set; }
        public DataChunkNew DataBuffer { get; set; }
        public DataChunkNew DataBufferToBeSaved { get; set; }
        public DataChunkNew DataStreamingBuffer { get; set; }
        public DataChunkNew DataCommandBuffer { get; set; }

        byte dataEndHeader = 0x42;
        byte[] dataACK = new byte[] { 0x82, 0x00, 0x00 };
        byte[] dataNACK = new byte[] { 0x72, 0x00, 0x00 };
        public enum LastDataTransferReplySentFromBS
        {
            NONE,
            ACK,
            NACK
        }

        ShimmerDeviceBluetoothState CurrentBluetoothState = ShimmerDeviceBluetoothState.None;

        LastDataTransferReplySentFromBS LastDataTransferReplySent = LastDataTransferReplySentFromBS.NONE;

        int NACKcounter { get; set; }
        int NACKCRCcounter { get; set; }

        public string dataFileName { get; set; }
        public string dataFilePath { get; set; }
        public ushort PayloadIndex { get; set; }
        public int? PreviouslyWrittenPayloadIndex { get; set; } = -1;
        public string binFileFolderDir { get; set; }
        public string sensorLogFileName { get; set; }
        public string sensorLogFilePath { get; set; }
        public string sensorLogFolderDir { get; set; }

        #endregion

        #region Read Request props

        protected byte[] ReadStatusRequest = new byte[] { 0x11, 0x00, 0x00 };
        protected byte[] ReadStatusRequest2 = new byte[] { 0x1C, 0x00, 0x00 };
        protected byte[] ReadDataRequest = new byte[] { 0x12, 0x00, 0x00 };
        protected byte[] StreamDataRequest = new byte[] { 0x2A, 0x01, 0x00, 0x01 };
        protected byte[] StopStreamRequest = new byte[] { 0x2A, 0x01, 0x00, 0x02 };
        protected byte[] ReadProdConfigRequest = new byte[] { 0x13, 0x00, 0x00 };
        protected byte[] ReadOpConfigRequest = new byte[] { 0x14, 0x00, 0x00 };
        protected byte[] ReadTimeRequest = new byte[] { 0x15, 0x00, 0x00 };
        protected byte[] ReadPendingEventsRequest = new byte[] { 0x17, 0x00, 0x00 };
        protected byte[] DFUCommand = new byte[] { 0x26, 0x00, 0x00 };
        protected byte[] DisconnectRequest = new byte[] { 0x2B, 0x00, 0x00 };
        protected byte[] EraseSensorData = new byte[] { 0x29, 0x01, 0x00, 0x0A };
        protected byte[] ReadEventLog = new byte[] { 0x29, 0x01, 0x00, 0x10 };
        protected byte[] ResetSensor = new byte[] { 0x29, 0x01, 0x00, 0x13 };


        #endregion

        #region Response Props
        TaskCompletionSource<bool> RequestTCS { get; set; }
        TaskCompletionSource<bool> DataTCS { get; set; }

        #endregion
        /// <summary>
        /// Created a verisese ble device of which to connect/configure/stream/sync
        /// </summary>
        /// <param name="id">the uuid for the address of which is used to connect to via BLE e.g. "00000000-0000-0000-0000-e7452c6d6f14" note that the uuid across OS (android vs iOS) can differ </param>
        /// <param name="name">and arbitrary/custom name e.g. "190226016F14"</param>
        public VerisenseBLEDevice(string id, string name)
        {
            ASMName = name;
            //payloadIndex = -1; //there is no need to initialized ASM-493, considering create chunk is surely called before createbinfile, there shouldnt be a case where this value is used without being initialized
            Asm_uuid = Guid.Parse(id);
            
        }

        /// <summary>
        /// Create a clone of the verisese ble device. The idea will be to create a 'clone' of the verisense device, update the sensor/setting within the clone. Generate the operation config bytes and transmit said bytes to the physical Verisense device.
        /// </summary>
        /// <param name="verisenseBLEDevice">an existing verisense ble device</param>
        public VerisenseBLEDevice(VerisenseBLEDevice verisenseBLEDevice) : base(verisenseBLEDevice)
        {

        }

        #region Device Connection

        /// <summary>
        /// This is to give an option to log advance data if required
        /// </summary>
        /// <param name="ObjectName"></param>
        /// <param name="Action"></param>
        /// <param name="Data"></param>
        /// <param name="asmid"></param>
        public virtual void AdvanceLog(string ObjectName, string Action, object Data, string asmid)
        {
            //Just print to console
            System.Console.WriteLine(ObjectName + " " + Action + " " + Data + " " + asmid);
            Debug.WriteLine(ObjectName + " " + Action + " " + Data + " " + asmid);
        }

        public virtual void AdvanceSyncLog(string ObjectName, string Action, object Data, string asmid)
        {
            System.Console.WriteLine(ObjectName + " " + Action + " " + Data + " " + asmid);
            Debug.WriteLine(ObjectName + " " + Action + " " + Data + " " + asmid);
        }

        /// <summary>
        /// This is to give an option to log in normal mode
        /// </summary>
        /// <param name="ObjectName"></param>
        /// <param name="Action"></param>
        /// <param name="Data"></param>
        /// <param name="asmid"></param>
        public virtual void NormalLog(string ObjectName, string Action, object Data, string asmid)
        {
            //Just print to console
            System.Console.WriteLine(ObjectName + " " + Action + " " + Data + " " + asmid);
            Debug.WriteLine(ObjectName + " " + Action + " " + Data + " " + asmid);
        }

        /// <summary>
        /// The duration in milliseconds since the last time the instance has received data successfully from a Verisense Device
        /// </summary>
        /// <returns>Duration in milliseconds</returns>
        public long DurationSinceLastRX()
        {
            return (DateHelper.GetTimestamp(DateTime.Now) - DateHelper.GetTimestamp(LastRX));
        }

        protected async Task<bool> GetKnownDevice()
        {
            LastRX = DateTime.Now;
            BLERadio.Asm_uuid = Asm_uuid;
            BLERadio.CommunicationEvent += UartRX_ValueUpdated;

            var result = await BLERadio.Connect();
            if (result.Equals(ConnectivityState.Connected))
            {
                return true;
            } else
            {
                return false;
            }
        }

        protected void StateChange(ShimmerDeviceBluetoothState state)
        {
            if (!CurrentBluetoothState.Equals(state))
            {
                CurrentBluetoothState = state;
                if (ShimmerBLEEvent != null)
                    ShimmerBLEEvent.Invoke(null, new ShimmerBLEEventData { ASMID = Asm_uuid.ToString(), CurrentEvent = VerisenseBLEEvent.StateChange });
            }
        }

        /// <summary>
        /// Returns the current bluetooth state
        /// </summary>
        /// <returns></returns>
        public ShimmerDeviceBluetoothState GetVerisenseBLEState()
        {
            return CurrentBluetoothState;
        }

        #endregion

        #region Device Notifications

        TimeSpan DataTimeout = TimeSpan.FromSeconds(5);
        DateTime LastDataPayload { get; set; }
        DateTime LastRX { get; set; }

        readonly int DefaultExecuteRequestTimerDuration = 5000;

        private int expectedResponseLength = -1;
        private List<byte> partialData = new List<byte>();

        protected void UartRX_ValueUpdated(object sender, ByteLevelCommunicationEvent comEvent)
        {
            if (comEvent.Event == ByteLevelCommunicationEvent.CommEvent.NewBytes)
            {
                byte[] bytes = comEvent.Bytes;
                LastRX = DateTime.Now;
                if (bytes == null)
                {
                    AdvanceLog(LogObject, "Response Buffer is null", string.Empty, ASMName);
                    return;
                }

                if (ReceivingData)
                {
                    LastDataPayload = DateTime.Now;

                    if (NewPayload)
                    {
                        CreateNewPayload(bytes);
                    }
                    else
                    {
                        HandleDataChunk(bytes);
                    }

                    return;
                }
                else if (CurrentBluetoothState == ShimmerDeviceBluetoothState.Streaming)
                {
                    //System.Console.WriteLine("STREAMING DATA (" + bytes.Length + ") :" + String.Join(" ", bytes));
                    if (bytes.Length == 3 && bytes[0] == 74)//4A 00 00
                    {
                        if (WaitingForStopStreamingCommand)
                        {
                            HandleCommonResponse();
                            StateChange(ShimmerDeviceBluetoothState.Connected);
                            WaitingForStopStreamingCommand = false;
                            return;
                        }
                        else
                        {
                            StateChange(ShimmerDeviceBluetoothState.Streaming);
                            return;
                        }
                    }

                    if (NewStreamPayload)
                    {
                        CreateNewStreamPayload(bytes);
                    }
                    else
                    {
                        HandleStreamDataChunk(bytes);
                    }

                    return;
                }
                else
                {
                    if (partialData.Count > 0)
                    {
                        partialData.AddRange(comEvent.Bytes);
                        bytes = partialData.ToArray();
                        partialData.Clear();
                    }

                    if (expectedResponseLength == -1)
                    {
                        if (bytes.Length >= 3)
                        {
                            byte lengthByte1 = bytes[1];
                            byte lengthByte2 = bytes[2];
                            expectedResponseLength = (lengthByte2 << 8) | lengthByte1;
                            expectedResponseLength += 3;
                        }
                        else
                        {
                            partialData.AddRange(bytes);
                            AdvanceLog(LogObject, "Current Length: " + bytes.Length, string.Empty, ASMName);
                            return;
                        }
                    }

                    if (bytes.Length >= expectedResponseLength)
                    {
                        expectedResponseLength = -1;
                        partialData.Clear();
                    }
                    else
                    {
                        partialData.AddRange(bytes);
                        AdvanceLog(LogObject, "Current Length: " + bytes.Length, string.Empty, ASMName);
                        return;
                    }
                    ResponseBuffer = bytes;

                    if (bytes.Length == 3 && bytes[0] >> 4 == 4)//if it is an ack
                    {
                        HandleCommonResponse();
                    }
                    else
                    {
                        if (NewCommandPayload)
                        {
                            if (bytes.Length < 3)
                            {
                                WaitForCompletePayload(bytes);
                            }
                            else
                            {
                                CreateNewCommandPayload(bytes);
                            }
                        }
                        else
                        {
                            HandleCommandDataChunk(bytes);
                        }
                    }
                }
            } else if (comEvent.Event == ByteLevelCommunicationEvent.CommEvent.Disconnected)
            {
                if (DataRequestTimer != null)
                {
                    if (CurrentBluetoothState.Equals(ShimmerDeviceBluetoothState.StreamingLoggedData))
                    {
                        DataTCS.TrySetResult(false);
                        //DataTCS.SetResult(false);
                    }
                    DataRequestTimer.Dispose(); //there is no point having the timer, if the connection is disconnected
                }
                StateChange(ShimmerDeviceBluetoothState.Disconnected);
            }
        }

        void ProcessDataTimeout(Object obj)
        {
            try
            {
                Debug.WriteLine("ProcessDataTimeOut");
                AdvanceLog(LogObject, "Process Data Timeout", "Receiving Data" + ReceivingData, ASMName);
                if (!ReceivingData)
                {
                    DataRequestTimer.Dispose();
                    return;
                }

                if ((DateTime.Now - LastDataPayload) > DataTimeout)
                {
                    //current design means that you need send a command when timeout, because the exception is what sets trysetresult to false
                    if (LastDataTransferReplySent == LastDataTransferReplySentFromBS.NONE) //if none means was the data request command
                    {
                        //SendDataACK();
                        AdvanceLog(LogObject, "Process Data Timeout", "Sending Read Data Request Again", ASMName);
                        SendReadDataRequestCommandOnMainThread();
                    }
                    else
                    {
                        AdvanceLog(LogObject, "Process Data Timeout", "Sending NACK, Last Reply: " + LastDataTransferReplySent.ToString(), ASMName);
                        if (PayloadIndex == 65535 && DataBuffer.ExpectedLength == 16) //fix for ASM-815
                        {
                            SendDataNACK(true);
                        }
                        else
                        {
                            SendDataNACK(false);
                        }
                    }
                    //return true;
                }
                else
                {
                    //return true;
                }
            }
            catch (Exception ex)
            {
                NormalLog(LogObject, "ProcessDataTimeout", ex, ASMName);
                ReceivingData = false;
                DataTCS.TrySetResult(false);
                DataRequestTimer.Dispose();
            }
        }

        void HandleCommonResponse()
        {
            try
            {
                switch (ResponseBuffer[0])
                {
                    case 0x31:
                        var statusData = new StatusPayload();
                        var statusResult = statusData.ProcessPayload(ResponseBuffer, SyncMode);
                        if (statusResult)
                        {
                            Status = statusData;

                            if (ShimmerBLEEvent != null) {
                                ShimmerBLEEvent.Invoke(null, new ShimmerBLEEventData { ASMID = Asm_uuid.ToString(), CurrentEvent = VerisenseBLEEvent.RequestResponse, ObjMsg = RequestType.ReadStatus });
                            }
                            RequestTCS.TrySetResult(true);
                        }
                        else
                        {
                            if (ShimmerBLEEvent != null)
                            {
                                ShimmerBLEEvent.Invoke(null, new ShimmerBLEEventData { ASMID = Asm_uuid.ToString(), CurrentEvent = VerisenseBLEEvent.RequestResponseFail, ObjMsg = RequestType.ReadStatus });
                            }
                            RequestTCS.TrySetResult(false);
                        }

                        break;
                    case 0x3C:
                        var statusData2 = new StatusPayload();
                        var statusResult2 = statusData2.ProcessPayload(ResponseBuffer, SyncMode);
                        if (statusResult2)
                        {
                            Status = statusData2;

                            if (ShimmerBLEEvent != null)
                            {
                                ShimmerBLEEvent.Invoke(null, new ShimmerBLEEventData { ASMID = Asm_uuid.ToString(), CurrentEvent = VerisenseBLEEvent.RequestResponse, ObjMsg = RequestType.ReadStatus2 });
                            }
                            RequestTCS.TrySetResult(true);
                        }
                        else
                        {
                            if (ShimmerBLEEvent != null)
                            {
                                ShimmerBLEEvent.Invoke(null, new ShimmerBLEEventData { ASMID = Asm_uuid.ToString(), CurrentEvent = VerisenseBLEEvent.RequestResponseFail, ObjMsg = RequestType.ReadStatus2 });
                            }
                            RequestTCS.TrySetResult(false);
                        }

                        break;
                    case 0x33:
                        var prodData = new ProdConfigPayload();
                        var prodResult = prodData.ProcessPayload(ResponseBuffer);
                        if (prodResult)
                        {
                            ProdConfig = prodData;
                            UpdateDeviceAndSensorConfiguration();
                            if (ShimmerBLEEvent != null)
                            {
                                ShimmerBLEEvent.Invoke(null, new ShimmerBLEEventData { ASMID = Asm_uuid.ToString(), CurrentEvent = VerisenseBLEEvent.RequestResponse, ObjMsg = RequestType.ReadProductionConfig });
                            }
                            RequestTCS.TrySetResult(true);
                        }
                        else
                        {
                            if (ShimmerBLEEvent != null)
                            {
                                ShimmerBLEEvent.Invoke(null, new ShimmerBLEEventData { ASMID = Asm_uuid.ToString(), CurrentEvent = VerisenseBLEEvent.RequestResponseFail, ObjMsg = RequestType.ReadProductionConfig });
                            }
                            RequestTCS.TrySetResult(false);
                        }
                        break;
                    case 0x34:
                        var opData = new OpConfigPayload();
                        var opResult = opData.ProcessPayload(ResponseBuffer);
                        if (opResult)
                        {
                            OpConfig = opData;
                            UpdateDeviceAndSensorConfiguration();
                            if (ShimmerBLEEvent != null)
                            {
                                ShimmerBLEEvent.Invoke(null, new ShimmerBLEEventData { ASMID = Asm_uuid.ToString(), CurrentEvent = VerisenseBLEEvent.RequestResponse, ObjMsg = RequestType.ReadOperationalConfig });
                            }
                            RequestTCS.TrySetResult(true);
                        }
                        else
                        {
                            if (ShimmerBLEEvent != null)
                            {
                                ShimmerBLEEvent.Invoke(null, new ShimmerBLEEventData { ASMID = Asm_uuid.ToString(), CurrentEvent = VerisenseBLEEvent.RequestResponseFail, ObjMsg = RequestType.ReadOperationalConfig });
                            }
                            RequestTCS.TrySetResult(false);
                        }
                        break;
                    case 0x35:
                        var timeData = new TimePayload();
                        var timeResult = timeData.ProcessPayload(ResponseBuffer);
                        if (timeResult)
                        {
                            Time = timeData;
                            if (ShimmerBLEEvent != null)
                            {
                                ShimmerBLEEvent.Invoke(null, new ShimmerBLEEventData { ASMID = Asm_uuid.ToString(), CurrentEvent = VerisenseBLEEvent.RequestResponse, ObjMsg = RequestType.ReadRTC });
                            }
                            RequestTCS.TrySetResult(true);
                        }
                        else
                        {
                            if (ShimmerBLEEvent != null)
                            {
                                ShimmerBLEEvent.Invoke(null, new ShimmerBLEEventData { ASMID = Asm_uuid.ToString(), CurrentEvent = VerisenseBLEEvent.RequestResponseFail, ObjMsg = RequestType.ReadRTC });
                            }
                            RequestTCS.TrySetResult(false);
                        }
                        break;
                    case 0x37:
                        var pendingData = new PendingEventsPayload();
                        var pendingResult = pendingData.ProcessPayload(ResponseBuffer);
                        if (pendingResult)
                        {
                            PendingEvents = pendingData;
                            if (ShimmerBLEEvent != null)
                            {
                                ShimmerBLEEvent.Invoke(null, new ShimmerBLEEventData { ASMID = Asm_uuid.ToString(), CurrentEvent = VerisenseBLEEvent.RequestResponse, ObjMsg = RequestType.ReadPendingEvents });
                            }
                            RequestTCS.TrySetResult(true);
                        }
                        else
                        {
                            if (ShimmerBLEEvent != null)
                            {
                                ShimmerBLEEvent.Invoke(null, new ShimmerBLEEventData { ASMID = Asm_uuid.ToString(), CurrentEvent = VerisenseBLEEvent.RequestResponseFail, ObjMsg = RequestType.ReadPendingEvents });
                            }
                            RequestTCS.TrySetResult(false);
                        }
                        break;
                    case 0x43:
                    case 0x44:
                    case 0x45: //writertc
                        var baseData = new BasePayload();
                        var baseResult = baseData.ProcessPayload(ResponseBuffer);
                        if (baseResult)
                        {
                            WriteResponse = baseData;
                            RequestTCS.TrySetResult(true);
                        }
                        else
                        {
                            RequestTCS.TrySetResult(false);
                        }

                        break;
                    case 0x46: //dfu
                        var baseDataDFU = new BasePayload();
                        var baseResultDFU = baseDataDFU.ProcessPayload(ResponseBuffer);
                        if (baseResultDFU)
                        {
                            WriteResponse = baseDataDFU;
                            RequestTCS.TrySetResult(true);
                        }
                        else
                        {
                            RequestTCS.TrySetResult(false);
                        }

                        break;
                    case 0x4A:
                        var baseDataSS = new BasePayload();
                        var baseResultSS = baseDataSS.ProcessPayload(ResponseBuffer);
                        if (CurrentBluetoothState == ShimmerDeviceBluetoothState.Streaming)
                        {
                            StateChange(ShimmerDeviceBluetoothState.Connected);
                        }
                        if (baseResultSS)
                        {
                            WriteResponse = baseDataSS;
                            RequestTCS.TrySetResult(true);
                        }
                        else
                        {
                            RequestTCS.TrySetResult(false);
                        }
                        break;
                    case 0x4B:
                        var baseDataD = new BasePayload();
                        var baseResultD = baseDataD.ProcessPayload(ResponseBuffer);
                        if (baseResultD)
                        {
                            WriteResponse = baseDataD;
                            RequestTCS.TrySetResult(true);
                        }
                        else
                        {
                            RequestTCS.TrySetResult(false);
                        }

                        break;
                    case 0x49:
                        var baseDataErase = new BasePayload();
                        var baseResultErase = baseDataErase.ProcessPayload(ResponseBuffer);
                        if (baseResultErase)
                        {
                            WriteResponse = baseDataErase;
                            if (ShimmerBLEEvent != null)
                            {
                                ShimmerBLEEvent.Invoke(null, new ShimmerBLEEventData { ASMID = Asm_uuid.ToString(), CurrentEvent = VerisenseBLEEvent.RequestResponse, ObjMsg = RequestType.EraseData });
                            }
                            RequestTCS.TrySetResult(true);
                        }
                        else
                        {
                            RequestTCS.TrySetResult(false);
                        }

                        break;
                    case 0x39: // read event log
                        var logEventsData = new LogEventsPayload();
                        var logEventsResult = logEventsData.ProcessPayload(ResponseBuffer);
                        CreateSensorLogFile(false);
                        SaveVlogFileToDB();
                        if (logEventsResult)
                        {
                            LogEvents = logEventsData;
                            WriteSensorLogToFile(((LogEventsPayload)logEventsData).LogEventsBytes);
                            //logEventsData.WriteLogEventsToFile(sensorLogFilePath);
                            if (ShimmerBLEEvent != null)
                            {
                                ShimmerBLEEvent.Invoke(null, new ShimmerBLEEventData { ASMID = Asm_uuid.ToString(), CurrentEvent = VerisenseBLEEvent.RequestResponse, ObjMsg = RequestType.ReadEventLog });
                            }
                            RequestTCS.TrySetResult(true);
                        }
                        else
                        {
                            if (ShimmerBLEEvent != null)
                            {
                                ShimmerBLEEvent.Invoke(null, new ShimmerBLEEventData { ASMID = Asm_uuid.ToString(), CurrentEvent = VerisenseBLEEvent.RequestResponseFail, ObjMsg = RequestType.ReadEventLog });
                            }
                            RequestTCS.TrySetResult(false);
                        }
                        break;
                    default:
                        AdvanceLog(LogObject, "NonDataResponse", BitConverter.ToString(ResponseBuffer), ASMName);
                        throw new Exception();
                };
            }
            catch (Exception ex)
            {
                NormalLog(LogObject, "NonDataResponse Exception", ex, ASMName);
                if (RequestTCS != null)
                    RequestTCS.TrySetResult(false);
                if (DataTCS != null)
                    DataTCS.TrySetResult(false);
                if (BLERadio != null)
                    BLERadio.CommunicationEvent -= UartRX_ValueUpdated;
            }
        }

        #endregion

        #region Execute Requests


        /// <summary>
        /// Write bytes to the BLE device based on the request type
        /// </summary>
        /// <param name="reqObjects">1st parameter must be a request type, second parameter is optional and can either be a byte array or an integer. An integer value sets a custom execute request timeout.</param>
        /// <exception>Thrown if 1st parameter is not a request type or second parameter is not a byte array or integer</exception>
        /// <returns>return payload that varies based on request type</returns>
        public async Task<IBasePayload> ExecuteRequest(params Object[] reqObjects)
        {
            // DEV-1096: the Bluetooth-off guard in the write below needs the firmware version, and
            // Connect() writes an operational config before it reads the production config. So
            // read it first, before this request's own state is set up - only for a write that
            // turns Bluetooth off while no version is known.
            if (reqObjects.Length == 2 && reqObjects[0] is RequestType firstRequestType
                && firstRequestType == RequestType.WriteOperationalConfig
                && reqObjects[1] is byte[] opConfigToWrite && ProdConfig == null
                && EnforceBluetoothOffFirmwareGuard((byte[])opConfigToWrite.Clone()))
            {
                await ExecuteRequest(RequestType.ReadProductionConfig);
            }

            int setCustomExecuteRequestTimeout = -1;
            NewCommandPayload = true;
            DataCommandBuffer = new DataChunkNew();
            byte[] additionalBytesToWrite = null;
            partialData.Clear();
            if (!(reqObjects[0] is RequestType))
            {
                throw new Exception("1st Parameter needs to be the Request Type");
            }
            RequestType requestType = (RequestType)reqObjects[0];
            if (reqObjects.Length == 2)
            {
                if (reqObjects[1] is byte[])
                {
                    additionalBytesToWrite = (byte[])reqObjects[1];
                } else if(reqObjects[1] is int){
                    setCustomExecuteRequestTimeout = (int)reqObjects[1];
                } else
                {
                    throw new Exception("2nd Parameter needs to be a byte [] or integer");
                }
            }
            if (ReceivingData)
            {
                return null;
            }

            RequestTCS = new TaskCompletionSource<bool>();
            byte[] request = null;

            switch (requestType)
            {
                case RequestType.TransferLoggedData:
                    var drResult = await ExecuteDataRequest();
                    if (drResult)
                    {
                        return new BasePayload();
                    } else
                    {
                        return null;
                    }
                case RequestType.StartStreaming:
                    request = StreamDataRequest;
                    break;
                case RequestType.StopStreaming:
                    WaitingForStopStreamingCommand = true;
                    request = StopStreamRequest;
                    break;
                case RequestType.ReadStatus:
                    request = ReadStatusRequest;
                    break;
                case RequestType.ReadStatus2:
                    request = ReadStatusRequest2;
                    break;
                case RequestType.ReadProductionConfig:
                    request = ReadProdConfigRequest;
                    break;
                case RequestType.ReadOperationalConfig:
                    request = ReadOpConfigRequest;
                    break;
                case RequestType.WriteOperationalConfig:
                    if (additionalBytesToWrite != null)
                    {
                        //needs a header
                        request = new byte[additionalBytesToWrite.Length + 3];
                        Array.Copy(additionalBytesToWrite, 0, request, 3, additionalBytesToWrite.Length);
                        var lengthb = BitHelper.LSBByteArray(additionalBytesToWrite.Length.ToString("X4"));
                        request[0] = 0x24;
                        request[1] = lengthb[0];
                        request[2] = lengthb[1];
                    }
                    else
                    {
                        request = await CreateWriteOpConfigRequest();
                    }
                    KeepBluetoothOnIfFirmwareNeedsIt(request);
                    break;
                case RequestType.WriteProductionConfig:
                    if (additionalBytesToWrite != null)
                    {
                        if (additionalBytesToWrite.Length >= 55)
                        {
                            byte[] passkeyBytes = new byte[ProdConfig.PasskeyLength];
                            Array.Copy(additionalBytesToWrite, (int)ProdConfigPayload.ConfigurationBytesIndexName.PASSKEY, passkeyBytes, 0, passkeyBytes.Length);
                            for (int i = 0; i < passkeyBytes.Length; i++)
                            {
                                if (passkeyBytes[i] != 0xFF && !int.TryParse(Encoding.UTF8.GetString(passkeyBytes, i, 1), out _))
                                {
                                    throw new Exception("Passkey Must Be Numeric Values");
                                }
                            }
                        }
                        
                        //needs a header
                        request = new byte[additionalBytesToWrite.Length + 3];
                        Array.Copy(additionalBytesToWrite, 0, request, 3, additionalBytesToWrite.Length);
                        var lengthb = BitHelper.LSBByteArray(additionalBytesToWrite.Length.ToString("X4"));
                        request[0] = 0x23;
                        request[1] = lengthb[0];
                        request[2] = lengthb[1];
                    }
                    break;
                case RequestType.ReadRTC:
                    request = ReadTimeRequest;
                    break;
                case RequestType.WriteRTC:
                    if (additionalBytesToWrite != null)
                    {
                        //needs a header
                        request = additionalBytesToWrite;
                    }
                    else
                    {
                        try
                        {
                            request = CreateWriteTimeRequest();
                            AdvanceLog(LogObject, "CreateWriteTimeRequest", "RTC Bytes: " + BitConverter.ToString(request).Replace("-", ""), "");
                        }
                        catch (Exception ex)
                        {
                            AdvanceLog(LogObject, "CreateWriteTimeRequestException", ex, "");
                        }
                    }
                    break;
                case RequestType.ReadPendingEvents:
                    request = ReadPendingEventsRequest;
                    break;
                case RequestType.Disconnect:
                    request = DisconnectRequest;
                    break;
                case RequestType.OperationalConfigWriteOnUnpairing:
                    request = await CreateWriteOpConfigRequestOnUnpairing();
                    KeepBluetoothOnIfFirmwareNeedsIt(request);
                    break;
                case RequestType.DFU:
                    request = DFUCommand;
                    break;
                case RequestType.EraseData:
                    request = EraseSensorData;
                    break;
                case RequestType.ReadEventLog:
                    request = ReadEventLog;
                    break;
                case RequestType.Reset:
                    request = ResetSensor;
                    break;
            }

            if (request == null)
            {
                return null;
            }

            var writeResult = await WriteRequest(request);

            if (!writeResult)
            {
                return null;
            }

            StartExecuteRequestTimer(setCustomExecuteRequestTimeout);

            var result = await RequestTCS.Task;
            TaskCount++;

            if (!result)
            {
                return null;
            }

            switch (requestType)
            {
                case RequestType.ReadStatus:
                    return Status;
                case RequestType.ReadStatus2:
                    return Status;
                case RequestType.ReadProductionConfig:
                    return ProdConfig;
                case RequestType.ReadOperationalConfig:
                    return OpConfig;
                case RequestType.WriteOperationalConfig:
                    //success write the value written to the realmDB
                    var opData = new OpConfigPayload();
                    var opResult = opData.ProcessPayload(request);
                    if (opResult)
                    {
                        OpConfig = opData;
                        UpdateDeviceAndSensorConfiguration();
                        if (ShimmerBLEEvent != null)
                        {
                            ShimmerBLEEvent.Invoke(null, new ShimmerBLEEventData { ASMID = Asm_uuid.ToString(), CurrentEvent = VerisenseBLEEvent.RequestResponse, ObjMsg = RequestType.WriteOperationalConfig });
                        }
                        RequestTCS.TrySetResult(true);
                    }
                    else
                    {
                        if (ShimmerBLEEvent != null)
                        {
                            ShimmerBLEEvent.Invoke(null, new ShimmerBLEEventData { ASMID = Asm_uuid.ToString(), CurrentEvent = VerisenseBLEEvent.RequestResponseFail, ObjMsg = RequestType.WriteOperationalConfig });
                        }
                        RequestTCS.TrySetResult(false);
                    }
                    return WriteResponse;
                case RequestType.WriteProductionConfig:
                    var prodData = new ProdConfigPayload();
                    var prodResult = prodData.ProcessPayload(request);
                    if (prodResult)
                    {
                        ProdConfig = prodData;
                        UpdateDeviceAndSensorConfiguration();
                        if (ShimmerBLEEvent != null)
                        {
                            ShimmerBLEEvent.Invoke(null, new ShimmerBLEEventData { ASMID = Asm_uuid.ToString(), CurrentEvent = VerisenseBLEEvent.RequestResponse, ObjMsg = RequestType.WriteProductionConfig });
                        }
                        RequestTCS.TrySetResult(true);
                    }
                    else
                    {
                        if (ShimmerBLEEvent != null)
                        {
                            ShimmerBLEEvent.Invoke(null, new ShimmerBLEEventData { ASMID = Asm_uuid.ToString(), CurrentEvent = VerisenseBLEEvent.RequestResponseFail, ObjMsg = RequestType.WriteOperationalConfig });
                        }
                        RequestTCS.TrySetResult(false);
                    }
                    return WriteResponse;
                case RequestType.ReadRTC:
                    return Time;
                case RequestType.WriteRTC:
                    return WriteResponse;
                case RequestType.ReadPendingEvents:
                    return PendingEvents;
                case RequestType.Disconnect:
                    return WriteResponse;
                case RequestType.OperationalConfigWriteOnUnpairing:
                    return WriteResponse;
                case RequestType.DFU:
                    return WriteResponse;
                case RequestType.StartStreaming:
                    ResetSensorTimestampOffsets();
                    DataTCS = new TaskCompletionSource<bool>();
                    StateChange(ShimmerDeviceBluetoothState.Streaming);
                    AdvanceLog(LogObject, "Execute Stream Request", "Streaming Data True", ASMName);
                    //Device.StartTimer(DataTimeout, ProcessDataTimeout);
                    NewStreamPayload = true;
                    DataStreamingBuffer = new DataChunkNew();
                    //DataBufferToBeSaved = null;
                    return WriteResponse;
                case RequestType.StopStreaming:
                    return WriteResponse;
                case RequestType.EraseData:
                    return WriteResponse;
                case RequestType.ReadEventLog:
                    return LogEvents;
                case RequestType.Reset:
                    return WriteResponse;
            }

            return null;
        }

        protected virtual void StartExecuteRequestTimer(int duration)
        {
            if (duration == -1) //no change to timeout duration
            {
                Timer timer = new Timer(ExecuteRequestTimerCallback, null, DefaultExecuteRequestTimerDuration, Timeout.Infinite);
            } else
            {
                Timer timer = new Timer(ExecuteRequestTimerCallback, null, duration, Timeout.Infinite);
            }

            /*
            Device.StartTimer(TimeSpan.FromSeconds(5), () =>
            {
                if (TimerCount >= TaskCount)
                {
                    RequestTCS.TrySetResult(false);
                }
                TimerCount++;
                return false;
            });
            */
        }

        void ExecuteRequestTimerCallback(object state)
        {
            if (TimerCount >= TaskCount)
            {
                RequestTCS.TrySetResult(false);
            }
            TimerCount++;
        }

        Timer DataRequestTimer = null;

        protected virtual void StartDataRequestTimer()
        {
            //Device.StartTimer(DataTimeout, ProcessDataTimeout);
            if (DataRequestTimer != null)
            {
                DataRequestTimer.Dispose();
            }
            DataRequestTimer = new Timer(ProcessDataTimeout,null,5000,5000);
        }

        protected async virtual Task<bool> WriteRequest(byte[] request)
        {
            AdvanceLog(LogObject, "WriteRequest", BitConverter.ToString(request), ASMName);
            bool success = false;

            try
            {
                success = await BLERadio.WriteBytes(request);
            }
            catch (Exception ex)
            {
                AdvanceLog(LogObject, "WriteRequestException", ex.Message, ASMName);
            }

            return success;
        }

        protected async Task<bool> ExecuteDataRequest()
        {
            DataTCS = new TaskCompletionSource<bool>();
            ReceivingData = true;
            AdvanceLog(LogObject, "Execute Data Request", "Receiving Data True",ASMName);
            StartDataRequestTimer();
            NewPayload = true;
            DataBuffer = new DataChunkNew();
            DataBufferToBeSaved = null;

            SendReadDataRequestCommandOnMainThread();

            var result = await DataTCS.Task;

            ReceivingData = false;
            AdvanceLog(LogObject, "Execute Data Request", "Receiving Data False", ASMName);
            return result;
        }

        private async void SendReadDataRequestCommandOnMainThread()
        {

            try
            {
                LastDataTransferReplySent = LastDataTransferReplySentFromBS.NONE;
                var res = await BLERadio.WriteBytes(ReadDataRequest);
            }
            catch (Exception ex)
            {
                DataTCS.TrySetResult(false);
                NormalLog(LogObject, "ReadDataException", ex, ASMName);
            }

        }

        private async void SendStreamDataRequestCommandOnMainThread()
        {
            try
            {
                //LastDataTransferReplySent = LastDataTransferReplySentFromBS.NONE;
                var res = await BLERadio.WriteBytes(StreamDataRequest);
            }
            catch (Exception ex)
            {
                DataTCS.TrySetResult(false);
                NormalLog(LogObject, "ReadDataException", ex, ASMName);
            }
        }

        bool WaitingForStopStreamingCommand = false;

        /// <summary>
        /// Stop streaming
        /// </summary>
        public async void SendStopStreamRequestCommandOnMainThread()
        {
            try
            {
                //LastDataTransferReplySent = LastDataTransferReplySentFromBS.NONE;
                WaitingForStopStreamingCommand = true;
                var res = await BLERadio.WriteBytes(StopStreamRequest);
            }
            catch (Exception ex)
            {
                WaitingForStopStreamingCommand = false;
                DataTCS.TrySetResult(false);
                NormalLog(LogObject, "ReadDataException", ex, ASMName);
            }
        }

        #endregion

        #region Data Requests

        async void SendDataACK()
        {
            DataBuffer = new DataChunkNew();

            //AutoSyncLogger.AddLog(LogObject, "DataACKRequest", BitConverter.ToString(dataACK), ASMName);


            try
            {
                LastDataTransferReplySent = LastDataTransferReplySentFromBS.ACK;
                /*//ASM-931 only used for testing
                Random rnd = new Random();
                int test = rnd.Next(0, 10);
                if (test <= 2)
                {
                    throw new Exception("Testing delete last payload exception");
                }
                */
                await BLERadio.WriteBytes(dataACK);
            }
            catch (Exception ex)
            {
                //Delete the last payload written to the bin file, if it isnt crc error
                if (!DataBufferToBeSaved.CRCErrorPayload)
                {
                    DeleteLastPayloadFromBinFile();
                }

                NormalLog(LogObject, "SendDataACKException", ex.Message, ASMName);
                DataTCS.TrySetResult(false);
            }

        }

        /// <summary>
        /// For advance applications, to have the option to keep the location of the bin file in the DB
        /// </summary>
        protected virtual void SaveBinFileToDB()
        {
            //
        }


        /// <summary>
        /// For advance applications, to have the option to keep the location of the vlog file in the DB
        /// </summary>
        protected virtual void SaveVlogFileToDB()
        {
            //
        }

        /// <summary>
        /// Get Sensor ID
        /// </summary>
        /// <returns></returns>
        public virtual string GetSensorID()
        {
            if (ProdConfig != null)
            {
                if (!String.IsNullOrEmpty(ProdConfig.ASMID))
                {
                    return ProdConfig.ASMID;
                }
            }
            throw new Exception("ID unknown, please read production config first");
        }

        /// <summary>
        /// For more advance API/App which associate sensors to participants
        /// </summary>
        /// <returns></returns>
        public virtual string GetParticipantID()
        {
            return ParticipantID;
        }

        /// <summary>
        /// For more advance API/App which associate sensors to trials
        /// </summary>
        /// <returns></returns>
        public virtual string GetTrialName()
        {
            return TrialName;
        }

        /// <summary>
        /// For more advance API/App which associate sensors to trials
        /// </summary>
        public virtual void SetTrialName(string trialName)
        {
            TrialName = trialName;
        }

        /// <summary>
        /// For more advance API/App which associate sensors to participants
        /// </summary>
        public virtual void SetParticipantID(string participantID)
        {
            ParticipantID = participantID;
        }

        void SendDataNACK(bool crcError)
        {
            if (FinalChunkLogMsgForNack != null)
            {
                AdvanceLog(LogObject, "SendDataNack", FinalChunkLogMsgForNack, ASMName);
                FinalChunkLogMsgForNack = null;
            }
            if (NACKcounter >= 5)
            {
                DataTCS.TrySetResult(false);

                return;
            }

            if (NACKCRCcounter >= 5)
            {
                //var asm = RealmService.GetSensorbyID(Asm_uuid.ToString());
                //var trialSettings = RealmService.LoadTrialSettings();

                var participantID = GetParticipantID();
                //DataTCS.TrySetResult(false); //removed as part of ASM-709
                AdvanceLog(LogObject, "DataNACKRequest", "NACK count = " + NACKcounter + "; Participant ID =" + participantID + " ; Attempting to create bad crc file", ASMName);

                if (string.IsNullOrWhiteSpace(dataFileName))
                {
                    AdvanceLog(LogObject, "DataNACKRequest", "dataFileName is null Creating Bad CRC File", ASMName);
                    //if this is the first file create and write to the bad crc file
                    createBinFile(true);
                    FinishPayload(true);
                } else if (dataFilePath.Contains(BadCRC))
                {
                    //if the previous file was a bad crc file, then just create a new bad crc file and write the data to it
                    AdvanceLog(LogObject, "DataNACKRequest", "previous datFileName was a bad crc file, Creating New Bad CRC File", ASMName);
                    createBinFile(true);
                    FinishPayload(true);
                } else
                {
                    //if there is a previous file being written to we want to register it with the realm database, 
                    //we do this by comparing the current payload index with the file payload index, 
                    //if they are matching means this is the first payload having the problem
                    int? filePayloadIndex = FileHelper.GetPayloadFromBinFile(dataFileName);
                    if (filePayloadIndex != null)
                    {
                        if (filePayloadIndex == PayloadIndex)
                        {
                            AdvanceLog(LogObject, "DataNACKRequest", "same payload, Creating New Bad CRC File", ASMName);
                            createBinFile(true); //update the bin file name to one with crcerror
                            FinishPayload(true);
                        } else
                        {
                            AdvanceLog(LogObject, "DataNACKRequest", "different payload, Save old file to realm, and Create New Bad CRC File", ASMName);
                            SaveBinFileToDB();
                            createBinFile(true);
                            FinishPayload(true);
                        }
                    } else
                    {
                        AdvanceLog(LogObject, "DataNACKRequest", "payload is null, Creating New Bad CRC File", ASMName);
                        createBinFile(true);
                        FinishPayload(true);
                    }
                }
                
                return;
            }

            if (crcError)
            {
                NACKCRCcounter++;
            } else
            {
                NACKcounter++;
            }
            NormalLog(LogObject, "DataNACKRequest", "NACK count = " + NACKcounter + ";  NACK CRC count = " + NACKCRCcounter, ASMName);
            DataBuffer = new DataChunkNew();
            NewPayload = true;


            try
            {
                LastDataTransferReplySent = LastDataTransferReplySentFromBS.NACK;
                BLERadio.WriteBytes(dataNACK);
            }
            catch (Exception ex)
            {
                NormalLog(LogObject, "SendDataNACKException", ex.Message, ASMName);
                DataTCS.TrySetResult(false);
            }

        }

        void HandleCommandDataChunk(byte[] payload)
        {
            try
            {
                Buffer.BlockCopy(payload, 0, DataCommandBuffer.Packets, DataCommandBuffer.CurrentLength, payload.Count());
                DataCommandBuffer.CurrentLength += payload.Length;

                //JC: This causes too many msgs in the logs we need a better implementation of this, maybe just logs last 10 msgs if a failure occurs
                //AutoSyncLogger.AddLog(LogObject, "PayloadChunk", string.Format("Chunk length = {0}; Current Length = {1}; Expected Length={2}",
                //    payload.Length, DataBuffer.CurrentLength, DataBuffer.ExpectedLength), ASMName);
                //InternalWriteConsoleAndLog("Payload Chunk", string.Format("Chunk length = {0}; Current Length = {1}; Expected Length={2}", payload.Length, DataBuffer.CurrentLength, DataBuffer.ExpectedLength));
                FinalChunkLogMsgForNack = string.Format("Chunk length = {0}; Current Length = {1}; Expected Length={2}", payload.Length, DataCommandBuffer.CurrentLength, DataCommandBuffer.ExpectedLength);
                if (DataCommandBuffer.CurrentLength >= DataCommandBuffer.ExpectedLength) //Note: for readability it would be better for this to be == because > will cause the crc to fail anyway via a Buffer.BlockCopy exception e.g. "System.ArgumentException","Message":"Offset and length were out of bounds for the array or count is greater than the number of elements from index to the end of the source collection."
                {
                    HandleCompleteCommandPayload();
                }
            }
            catch (Exception ex)
            {
                SendDataNACK(false);
                AdvanceLog(LogObject, "HandleDataChunk", ex, ASMName);
            }
        }

        void HandleStreamDataChunk(byte[] payload)
        {
            try
            {
                Buffer.BlockCopy(payload, 0, DataStreamingBuffer.Packets, DataStreamingBuffer.CurrentLength, payload.Count());
                DataStreamingBuffer.CurrentLength += payload.Length;

                //JC: This causes too many msgs in the logs we need a better implementation of this, maybe just logs last 10 msgs if a failure occurs
                //AutoSyncLogger.AddLog(LogObject, "PayloadChunk", string.Format("Chunk length = {0}; Current Length = {1}; Expected Length={2}",
                //    payload.Length, DataBuffer.CurrentLength, DataBuffer.ExpectedLength), ASMName);
                //InternalWriteConsoleAndLog("Payload Chunk", string.Format("Chunk length = {0}; Current Length = {1}; Expected Length={2}", payload.Length, DataBuffer.CurrentLength, DataBuffer.ExpectedLength));
                FinalChunkLogMsgForNack = string.Format("Chunk length = {0}; Current Length = {1}; Expected Length={2}", payload.Length, DataStreamingBuffer.CurrentLength, DataStreamingBuffer.ExpectedLength);
                if (DataStreamingBuffer.CurrentLength >= DataStreamingBuffer.ExpectedLength) //Note: for readability it would be better for this to be == because > will cause the crc to fail anyway via a Buffer.BlockCopy exception e.g. "System.ArgumentException","Message":"Offset and length were out of bounds for the array or count is greater than the number of elements from index to the end of the source collection."
                {
                    HandleCompleteStreamingPayload();
                }
            }
            catch (Exception ex)
            {
                SendDataNACK(false);
                AdvanceLog(LogObject, "HandleDataChunk", ex, ASMName);
            }
        }


        void HandleDataChunk(byte[] payload)
        {
            try
            {
                Buffer.BlockCopy(payload, 0, DataBuffer.Packets, DataBuffer.CurrentLength, payload.Count());
                DataBuffer.CurrentLength += payload.Length;

                //JC: This causes too many msgs in the logs we need a better implementation of this, maybe just logs last 10 msgs if a failure occurs
                //AutoSyncLogger.AddLog(LogObject, "PayloadChunk", string.Format("Chunk length = {0}; Current Length = {1}; Expected Length={2}",
                //    payload.Length, DataBuffer.CurrentLength, DataBuffer.ExpectedLength), ASMName);
                //InternalWriteConsoleAndLog("Payload Chunk", string.Format("Chunk length = {0}; Current Length = {1}; Expected Length={2}", payload.Length, DataBuffer.CurrentLength, DataBuffer.ExpectedLength));
                FinalChunkLogMsgForNack = string.Format("Chunk length = {0}; Current Length = {1}; Expected Length={2}", payload.Length, DataBuffer.CurrentLength, DataBuffer.ExpectedLength);
                if (DataBuffer.CurrentLength >= DataBuffer.ExpectedLength) //Note: for readability it would be better for this to be == because > will cause the crc to fail anyway via a Buffer.BlockCopy exception e.g. "System.ArgumentException","Message":"Offset and length were out of bounds for the array or count is greater than the number of elements from index to the end of the source collection."
                {
                    HandleCompletePayload();
                }
            }
            catch (Exception ex)
            {
                SendDataNACK(false);
                AdvanceLog(LogObject, "HandleDataChunk", ex, ASMName);
            }
        }

        //to be overridden
        public virtual void InvokeSyncEvent(string asmID, SyncEventData data)
        {
            //can be used for any UI updates
        }

        void HandleCompleteCommandPayload()
        {
            NewCommandPayload = true;
            ResponseBuffer = DataCommandBuffer.Packets;
            HandleCommonResponse();
            DataCommandBuffer = new DataChunkNew();
        }

        void HandleCompleteStreamingPayload()
        {
            if (!CRCCheck(DataStreamingBuffer))
            {
                if (ShimmerBLEEvent != null)
                {
                    ShimmerBLEEvent.Invoke(null, new ShimmerBLEEventData { ASMID = Asm_uuid.ToString(), CurrentEvent = VerisenseBLEEvent.DataStreamCRCFail });
                }
                return;
            }
            NewStreamPayload = true;
            parseStreamingPayload();
            DataStreamingBuffer = new DataChunkNew();
        }

        void parseStreamingPayload()
        {
            byte[] data = DataStreamingBuffer.Packets;
            var sensorid = data[0];
            var sensor = SensorList[sensorid];
            var tickBytes = new byte[4];
            Array.Copy(data,1,tickBytes,0,3);
            //var tickBytes = reader.ReadBytes(3);
            //var bArray = addByteToArray(tickBytes, 0);
            var tick = BitConverter.ToUInt32(tickBytes, 0);
            Debug.WriteLine(" Time stamp ticks: " + tick);
            int offset = 4;
            byte[] sensorDataPayload = new byte[data.Length - offset];
            Array.Copy(data, offset, sensorDataPayload, 0, data.Length - offset);
            var ojcs = sensor.ParsePayloadData(sensorDataPayload, Asm_uuid.ToString());

            //Calculate timestamps (extrapolate backwards for every sample in the payload as tick is only recorded for latest sample)
            double systemTsLastSampleMillis = DateHelper.GetUnixTimestampMillis();
            double tsLastSampleMillis = sensor.GetShimmerTimestampUnwrapped(tick, systemTsLastSampleMillis);
            var samplingRate = Convert.ToDouble(sensor.GetSamplingRate().GetSettingsValue());
            var numOfSamples = ojcs.Count();
            int i = 0;
            foreach(ObjectCluster ojc in ojcs)
            {
                sensor.ExtrapolateTimestampsAndAddToOjc(ojc, tick, tsLastSampleMillis, systemTsLastSampleMillis, numOfSamples, i, samplingRate);
                i++;
            }
            
            foreach (ObjectCluster ojc in ojcs)
            {
               
                if (ShimmerBLEEvent != null)
                    ShimmerBLEEvent.Invoke(null, new ShimmerBLEEventData { ASMID = Asm_uuid.ToString(), CurrentEvent = VerisenseBLEEvent.NewDataPacket, ObjMsg = ojc });

            }
        }

        void HandleCompletePayload()
        {
            try
            {
                DataBuffer.Finish = DateTime.Now;
                DataBuffer.Transfer = DataBuffer.CurrentLength / (DataBuffer.Finish - DataBuffer.Start).TotalSeconds;
                string syncProgress = string.Format("{0:.##} KB/s", DataBuffer.Transfer / 1024.0) + "(" + ShimmerBLEAPI.Resources.AppResources.PayloadIndex + ": " + PayloadIndex + ")";
                AdvanceSyncLog(LogObject, "Payload transfer rate", syncProgress, ASMName);
                //InvokeSyncEvent(Asm_uuid.ToString(), new SyncEventData { ASMID = Asm_uuid.ToString(), CurrentEvent = SyncEvent.DataSync, SyncProgress = syncProgress });
                
                if (!CRCCheck(DataBuffer))
                {
                    SendDataNACK(true);
                    return;
                }

                if (string.IsNullOrWhiteSpace(dataFileName) || dataFileName.Contains(BadCRC)) //if the previous file name has a bad crc, create a new file, this has passed the crc check to reach here
                {
                    createBinFile(false);
                }
                else //if there is an existing file check the file size
                {
                    //check size of file and create new bin file if required
                    long length = new System.IO.FileInfo(dataFilePath).Length;
                    if (length > MaximumNumberOfBytesPerBinFile)
                    {
                        SaveBinFileToDB();
                        AdvanceLog(LogObject, "BinFileCheckNewFileRequired", dataFilePath + " size " + length, ASMName);
                        createBinFile(false);
                    }
                }

                FinishPayload(false);

                if (ShimmerBLEEvent != null)
                    ShimmerBLEEvent.Invoke(null, new ShimmerBLEEventData { ASMID = Asm_uuid.ToString(), CurrentEvent = VerisenseBLEEvent.SyncLoggedDataNewPayload, Message = syncProgress });

            }
            catch (Exception ex)
            {
                AdvanceLog(LogObject, "ProcessingDataPayloadException", ex, ASMName);
            }

        }

        void HandleEOS(byte[] payload)
        {
            try
            {
                NormalLog(LogObject, "HandleEOS", BitConverter.ToString(payload), ASMName);
                DataRequestTimer.Dispose(); //can stop the timer if the EOS is reached
                SaveBinFileToDB();
                DataTCS.TrySetResult(true);
                return;
            }
            catch (Exception ex)
            {
                AdvanceLog(LogObject, "HandleEOS Exception", ex, ASMName);
            }

        }

        void CreateNewStreamPayload(byte[] payload)
        {
            try
            {
                //AutoSyncLogger.AddLog(LogObject, "NewPayloadHead", BitConverter.ToString(payload), ASMName);

                var stream = new MemoryStream(payload);
                var reader = new BinaryReader(stream);

                var header = reader.ReadByte();

                var lengthBytes = reader.ReadBytes(2);
                var length = BitConverter.ToUInt16(lengthBytes, 0);
                DataStreamingBuffer = new DataChunkNew(length);
                DataStreamingBuffer.ExpectedLength = length;
                //var sensorid = reader.ReadByte();
                //var tickBytes = reader.ReadBytes(3);
                //var bArray = addByteToArray(tickBytes, 0);
                //var tick = BitConverter.ToUInt32(bArray, 0);
                int offset = 3;
                var remainingBytes = reader.ReadBytes(payload.Length - offset);

                Buffer.BlockCopy(remainingBytes, 0, DataStreamingBuffer.Packets, DataStreamingBuffer.CurrentLength, remainingBytes.Count());

                DataStreamingBuffer.CurrentLength += remainingBytes.Count();
                NewStreamPayload = false;

                AdvanceSyncLog(LogObject, "PayloadIndex", string.Format("Payload Index = {0}; Expected Length = {1}", PayloadIndex, DataStreamingBuffer.ExpectedLength), ASMName);

                reader.Close();
                stream = null;
                if (DataStreamingBuffer.CurrentLength > DataStreamingBuffer.ExpectedLength)
                {
                    AdvanceLog(LogObject, "CreateNewPayload", "Error Current Length: " + DataStreamingBuffer.CurrentLength + " bigger than Expected Length: " + DataStreamingBuffer.ExpectedLength, ASMName);
                    SendDataNACK(true);
                }
                else if (DataStreamingBuffer.CurrentLength == DataStreamingBuffer.ExpectedLength) //this might occur if the payload length is very small
                {
                    AdvanceLog(LogObject, "CreateNewPayload", "HandleCompleteStreamingPayload", ASMName);
                    HandleCompleteStreamingPayload();
                }
            }
            catch (Exception ex)
            {
                AdvanceLog(LogObject, "CreateChunk Exception", ex, ASMName);

                SendDataNACK(false);
                return;
            }

        }

        byte[] currentPayload;
        int currentLength = 0;
        void WaitForCompletePayload(byte[] payload)
        {
            if (currentPayload == null)
            {
                currentPayload = new byte[3];
            }
            Buffer.BlockCopy(payload, 0, currentPayload, currentLength, payload.Length);
            currentLength += payload.Length;

            if (currentLength == 3)
            {
                CreateNewCommandPayload(currentPayload);
                currentLength = 0;
            }
        }

        void CreateNewCommandPayload(byte[] payload)
        {
            try
            {
                //AutoSyncLogger.AddLog(LogObject, "NewPayloadHead", BitConverter.ToString(payload), ASMName);

                var stream = new MemoryStream(payload);
                var reader = new BinaryReader(stream);

                var header = reader.ReadByte();

                var lengthBytes = reader.ReadBytes(2);
                var length = BitConverter.ToUInt16(lengthBytes, 0);
                int offset = 3;
                DataCommandBuffer = new DataChunkNew(length+offset);
                DataCommandBuffer.ExpectedLength = length+offset;
                //var sensorid = reader.ReadByte();
                //var tickBytes = reader.ReadBytes(3);
                //var bArray = addByteToArray(tickBytes, 0);
                //var tick = BitConverter.ToUInt32(bArray, 0);

                var remainingBytes = reader.ReadBytes(payload.Length - offset);
                byte[] offsetBytes = new byte[] { header, lengthBytes[0], lengthBytes[1]};
                var remainingBytesWStartingOffset = new byte[remainingBytes.Count()+offset];
                remainingBytesWStartingOffset[0] = header;
                remainingBytesWStartingOffset[1] = lengthBytes[0];
                remainingBytesWStartingOffset[2] = lengthBytes[1];

                Buffer.BlockCopy(remainingBytes, 0, remainingBytesWStartingOffset, 3, remainingBytes.Count());
                Buffer.BlockCopy(remainingBytesWStartingOffset, 0, DataCommandBuffer.Packets, DataCommandBuffer.CurrentLength, remainingBytesWStartingOffset.Count());

                DataCommandBuffer.CurrentLength += remainingBytesWStartingOffset.Count();
                NewCommandPayload = false;

                AdvanceSyncLog(LogObject, "PayloadIndex", string.Format("Payload Index = {0}; Expected Length = {1}", PayloadIndex, DataCommandBuffer.ExpectedLength), ASMName);

                reader.Close();
                stream = null;
                if (DataCommandBuffer.CurrentLength > DataCommandBuffer.ExpectedLength)
                {
                    AdvanceLog(LogObject, "CreateNewPayload", "Error Current Length: " + DataCommandBuffer.CurrentLength + " bigger than Expected Length: " + DataCommandBuffer.ExpectedLength, ASMName);
                    SendDataNACK(true);
                }
                else if (DataCommandBuffer.CurrentLength == DataCommandBuffer.ExpectedLength) //this might occur if the payload length is very small
                {
                    AdvanceLog(LogObject, "CreateNewPayload", "HandleCompleteCommandPayload", ASMName);
                    HandleCompleteCommandPayload();
                }
            }
            catch (Exception ex)
            {
                AdvanceLog(LogObject, "CreateChunk Exception", ex, ASMName);

                SendDataNACK(false);
                return;
            }

        }

        /// <summary>
        /// Append a new byte to a byte array
        /// </summary>
        /// <param name="bArray"></param>
        /// <param name="newByte"></param>
        /// <returns></returns>
        public byte[] addByteToArray(byte[] bArray, byte newByte)
        {
            byte[] newArray = new byte[bArray.Length + 1];
            bArray.CopyTo(newArray, 0);
            newArray[newArray.Length-1] = newByte;
            return newArray;
        }

        void CreateNewPayload(byte[] payload)
        {
            if (payload.Length == 3 && payload[0] == dataEndHeader)
            {
                HandleEOS(payload);
                if (ShimmerBLEEvent != null)
                    ShimmerBLEEvent.Invoke(null, new ShimmerBLEEventData { ASMID = Asm_uuid.ToString(), CurrentEvent = VerisenseBLEEvent.SyncLoggedDataComplete });
                StateChange(ShimmerDeviceBluetoothState.Connected);
                return;
            }

            try
            {
                //AutoSyncLogger.AddLog(LogObject, "NewPayloadHead", BitConverter.ToString(payload), ASMName);

                var stream = new MemoryStream(payload);
                var reader = new BinaryReader(stream);

                var header = reader.ReadByte();
                StateChange(ShimmerDeviceBluetoothState.StreamingLoggedData);
                var lengthBytes = reader.ReadBytes(2);
                var length = BitConverter.ToUInt16(lengthBytes, 0);

                DataBuffer.ExpectedLength = length;

                int offset = 3;

                var indexBytes = reader.ReadBytes(2);
                PayloadIndex = BitConverter.ToUInt16(indexBytes, 0);

                offset = 5;

                Buffer.BlockCopy(indexBytes, 0, DataBuffer.Packets, 0, indexBytes.Count());
                DataBuffer.CurrentLength += 2; //this is the index?

                var remainingBytes = reader.ReadBytes(payload.Length - offset);

                Buffer.BlockCopy(remainingBytes, 0, DataBuffer.Packets, DataBuffer.CurrentLength, remainingBytes.Count());

                DataBuffer.CurrentLength += remainingBytes.Count();
                NewPayload = false;
                if (String.IsNullOrEmpty(ASMName))
                {
                    AdvanceSyncLog(LogObject, "PayloadIndex", string.Format("Payload Index = {0}; Expected Length = {1}", PayloadIndex, DataBuffer.ExpectedLength), Asm_uuid.ToString());
                }
                else
                {
                    AdvanceSyncLog(LogObject, "PayloadIndex", string.Format("Payload Index = {0}; Expected Length = {1}", PayloadIndex, DataBuffer.ExpectedLength), ASMName);
                }
                reader.Close();
                stream = null;
                if (DataBuffer.CurrentLength > DataBuffer.ExpectedLength)
                {
                    AdvanceLog(LogObject, "CreateNewPayload", "Error Current Length: " + DataBuffer.CurrentLength + " bigger than Expected Length: " + DataBuffer.ExpectedLength, ASMName);
                    SendDataNACK(true);
                }
                else if (DataBuffer.CurrentLength == DataBuffer.ExpectedLength) //this might occur if the payload length is very small
                {
                    AdvanceLog(LogObject, "CreateNewPayload", "HandleCompletePayload", ASMName);
                    HandleCompletePayload();
                }
            }
            catch (Exception ex)
            {
                AdvanceLog(LogObject, "CreateChunk Exception", ex, ASMName);

                SendDataNACK(false);
                return;
            }

        }

        bool CRCCheck(DataChunkNew DataBuffer)
        {
            try
            {
                var completeChunk = new byte[DataBuffer.ExpectedLength];

                Buffer.BlockCopy(DataBuffer.Packets, 0, completeChunk, 0, DataBuffer.CurrentLength);

                var result = BitHelper.CheckCRC(completeChunk);

                if (!result.result)
                {
                    AdvanceLog(LogObject, "CRCCheck", result, ASMName);
                    //see ASM-1142, ASM-1131
                    //AutoSyncLogger.AddLog(LogObject, "Failed CRC Payload", BitConverter.ToString(DataBuffer.Packets), ASMName);
                }

                return result.result;
            }
            catch (Exception ex)
            {
                AdvanceLog(LogObject, "CRCCheck Exception", ex, ASMName);
                return false;
            }

        }

        #endregion

        #region Data File

        //need to override
        protected virtual void createBinFile(bool crcError)
        {
            try
            {
                //var asm = RealmService.GetSensorbyID(Asm_uuid.ToString());
                //var trialSettings = RealmService.LoadTrialSettings();

                //var participantID = asm.ParticipantID;
                String sensorID = Asm_uuid.ToString();
                try
                {
                    sensorID = GetSensorID();
                } catch (Exception ex) // if production config wasnt read default to the UUID
                {
                    sensorID = Asm_uuid.ToString();
                    AdvanceLog(ex.Message, "Defaulting to UUID", dataFileName, ASMName);
                }
                binFileFolderDir = string.Format("{0}/{1}/{2}/BinaryFiles", GetTrialName(), GetParticipantID(), sensorID);
                var folder = Path.Combine(DependencyService.Get<ILocalFolderService>().GetAppLocalFolder(), binFileFolderDir);

                if (!Directory.Exists(folder))
                {
                    Directory.CreateDirectory(folder);
                }
                if (crcError)
                {
                    dataFileName = string.Format("{0}_{1}_{2}.bin", DateTime.Now.ToString("yyMMdd_HHmmss"), PayloadIndex.ToString("00000"),BadCRC);
                }
                else
                {
                    dataFileName = string.Format("{0}_{1}.bin", DateTime.Now.ToString("yyMMdd_HHmmss"), PayloadIndex.ToString("00000"));
                }
                
                AdvanceLog(LogObject, "BinFileNameCreated", dataFileName, ASMName);
                dataFilePath = Path.Combine(folder, dataFileName);

                AdvanceLog(LogObject, "BinFileCreated", dataFilePath, ASMName);
            }
            catch (Exception ex)
            {
                AdvanceLog(LogObject, "BinFileCreatedException", ex, ASMName);
            }
        }

        protected virtual void CreateSensorLogFile(bool crcError)
        {
            try
            {
                String sensorID = Asm_uuid.ToString();
                try
                {
                    sensorID = GetSensorID();
                }
                catch (Exception ex) // if production config wasnt read default to the UUID
                {
                    sensorID = Asm_uuid.ToString();
                    AdvanceLog(ex.Message, "Defaulting to UUID", sensorLogFileName, ASMName);
                }

                sensorLogFolderDir = string.Format("{0}/{1}/{2}/SensorLogs", GetTrialName(), GetParticipantID(), sensorID);
                //sensorLogFolderDir = String.Format("SensorLog");

                //need to check how data bin file is being uploaded (so that no all bin file is being uploaded to the BinaryFiles folder)
                //or use a different path
                var folder = Path.Combine(DependencyService.Get<ILocalFolderService>().GetAppLocalFolder(), sensorLogFolderDir);

                if (!Directory.Exists(folder))
                {
                    Directory.CreateDirectory(folder);
                }
                if (crcError)
                {
                    sensorLogFileName = string.Format("{0}_{1}_{2}.vlog", DateTime.Now.ToString("yyMMdd_HHmmss"), "DebugLog", BadCRC);
                }
                else
                {
                    sensorLogFileName = string.Format("{0}_{1}.vlog", DateTime.Now.ToString("yyMMdd_HHmmss"), "DebugLog");
                }

                AdvanceLog(LogObject, "SensorLogFileNameCreated", sensorLogFileName, ASMName);
                sensorLogFilePath = Path.Combine(folder, sensorLogFileName);

                AdvanceLog(LogObject, "SensorLogFileCreated", sensorLogFilePath, ASMName);
            }
            catch (Exception ex)
            {
                AdvanceLog(LogObject, "SensorLogFileCreatedException", ex, ASMName);
            }
        }

        void DeleteLastPayloadFromBinFile()
        {
            AdvanceLog(LogObject, "DeleteLastPayloadFromBinFile", FinalChunkLogMsgForNack, ASMName);
            var fi = new FileInfo(dataFilePath);
            FileStream fs = fi.Open(FileMode.Open);
            long bytesToDelete = DataBufferToBeSaved.CurrentLength;
            fs.SetLength(Math.Max(0, fi.Length - bytesToDelete));
            fs.Close();
            PreviouslyWrittenPayloadIndex = -1; //since the last payload was deleted reset this value, so the next transmission of the 'duplicate payload' will be written to the file
        }

        /// <summary>
        /// This is called by WritePayloadToBinFile to allow custom action in terms of keeping track of the sync date/time
        /// </summary>
        protected virtual void UpdateSensorDataSyncDate()
        {

        }

        void WritePayloadToBinFile()
        {
            if (PreviouslyWrittenPayloadIndex != PayloadIndex)
            {
                try
                {
                    System.Console.WriteLine("Write Payload To Bin File!");
                    using (var stream = new FileStream(dataFilePath, FileMode.Append))
                    {
                        stream.Write(DataBufferToBeSaved.Packets, 0, DataBufferToBeSaved.CurrentLength);
                    }
                    IsFileLocked(dataFilePath);


                    if (DataBufferToBeSaved.CRCErrorPayload)
                    {
                        SaveBinFileToDB();
                    } else
                    {
                        //only assume non crc error payload index is valid
                        PreviouslyWrittenPayloadIndex = PayloadIndex;
                    }
                    //DataBufferToBeSaved = null;
                    //RealmService.UpdateSensorDataSyncDate(Asm_uuid.ToString());
                    UpdateSensorDataSyncDate();
                }
                catch (Exception ex)
                {
                    NormalLog(LogObject, "FileAppendException", ex, ASMName);
                    throw ex;
                }
            }
            else
            {
                AdvanceLog(LogObject, "WritePayloadToBinFile", "Same Payload Index = " + PayloadIndex.ToString(), ASMName);
            }
        }

        void WriteSensorLogToFile(byte[] payload)
        {            
            try
            {
                System.Console.WriteLine("Write Sensor Log To File!");
                using (var stream = new FileStream(sensorLogFilePath, FileMode.Append))
                {
                    stream.Write(payload, 0, payload.Length);
                }
                IsFileLocked(sensorLogFilePath);
            }
            catch (Exception ex)
            {
                AdvanceLog(LogObject, "SensorLogFileAppendException", ex, ASMName);
                throw ex;
            }            
        }

        protected virtual bool IsFileLocked(string filepath)
        {
            try
            {
                FileInfo file = new FileInfo(filepath);
                using (FileStream stream = file.Open(FileMode.Open, FileAccess.Read, FileShare.None))
                {
                    stream.Close();
                }
            }
            catch (IOException ex)
            {
                //the file is unavailable because it is:
                //still being written to
                //or being processed by another thread
                //or does not exist (has already been processed)
                return true;
            }

            //file is not locked
            return false;
        }
        void FinishPayload(bool CRCError)
        {
            DataBufferToBeSaved = DataBuffer;

            if (CRCError)
            {
                DataBufferToBeSaved.CRCErrorPayload = true;
            }
            try
            {
                WritePayloadToBinFile();

            } catch (Exception ex)
            {
                DataTCS.TrySetResult(false);
                return;
            }
            SendDataACK();
            NewPayload = true;
            NACKcounter = 0;
            NACKCRCcounter = 0;
        }
        #endregion

        #region Creating Write Requests

        //to be overide 
        protected async virtual Task<byte[]> CreateWriteOpConfigRequest()
        {
            return null;
        }

        //to override
        protected async virtual Task<byte[]> CreateWriteOpConfigRequestOnUnpairing()
        {
            return null;
        }

        /// <summary>
        /// DEV-1096: keep Bluetooth on in a write operational config request, header and all, if the
        /// sensor's firmware cannot run without it (<see cref="VerisenseDevice.EnforceBluetoothOffFirmwareGuard"/>),
        /// and log that it did. Every operational config write passes through here, the ones an
        /// override of <see cref="CreateWriteOpConfigRequest"/> builds included.
        /// </summary>
        /// <param name="request">0x24, the 2-byte length, then the operational config; changed in place</param>
        void KeepBluetoothOnIfFirmwareNeedsIt(byte[] request)
        {
            const int headerLength = 3;
            if (request == null || request.Length <= headerLength)
            {
                return;
            }
            var opConfig = new byte[request.Length - headerLength];
            Array.Copy(request, headerLength, opConfig, 0, opConfig.Length);
            if (EnforceBluetoothOffFirmwareGuard(opConfig))
            {
                Array.Copy(opConfig, 0, request, headerLength, opConfig.Length);
                var firmware = ProdConfig == null
                    ? "of unknown version"
                    : $"{ProdConfig.REV_FW_MAJOR}.{ProdConfig.REV_FW_MINOR}.{ProdConfig.REV_FW_INTERNAL}";
                AdvanceLog(LogObject, "WriteOperationalConfig", $"Bluetooth kept on: firmware {firmware} loses USB with it off (DEV-1096)", ASMName);
            }
        }
        #endregion

        #region IDisposable Support

        /// <summary>
        /// Only Kept For Backward Compatibility
        /// </summary>
        /// <param name="disposing"></param>
        protected virtual void Dispose(bool disposing)
        {
            if (BLERadio is RadioPluginBLE)
            {
                ((RadioPluginBLE)BLERadio).Dispose(disposing);
            }
            ResponseBuffer = null;
        }

        /// <summary>
        /// Only Kept For Backward Compatibility
        /// </summary>
        /// <param name="disposing"></param>
        public void Dispose()
        {
            Dispose(true);
        }

        #endregion

        /// <summary>
        /// Create write time request using current time
        /// </summary>
        /// <returns></returns>
        public static byte[] CreateWriteTimeRequest()
        {
            //see ASM-DES04 section: Real-World Clock Synchronisation Format
            try
            {
                var timestamp = DateHelper.GetTimestamp(DateTime.Now);

                var minutes = timestamp / 1000 / 60.0;
                var minuteswithoutseconds = Math.Floor(minutes);
                var minutesHex = Convert.ToInt32(minuteswithoutseconds).ToString("X8");
                var ticks = (minutes % 1) * 60 * 32768;
                var tickHex = Convert.ToInt32(ticks).ToString("X6");
                var minuteBytes = BitHelper.LSBByteArray(minutesHex);
                var tickBytes = BitHelper.LSBByteArray(tickHex);

                var headerList = new List<byte> { 0x25 };

                var payloadLength = minuteBytes.Count + tickBytes.Count;

                headerList = headerList.Concat(BitHelper.LSBByteArray(payloadLength.ToString("X4"))).ToList();
                headerList = headerList.Concat(minuteBytes).ToList();
                headerList = headerList.Concat(tickBytes).ToList();

                var pendingEventsPacket = headerList.ToArray();
                return pendingEventsPacket;
            }
            catch (Exception ex)
            {
                throw ex;
            }

        }

        protected virtual void InitializeRadio()
        {
            if (BLERadio != null)
                BLERadio.CommunicationEvent -= UartRX_ValueUpdated;

            BLERadio = new RadioPluginBLE();

        }

        /// <summary>
        /// To intitialize a ble connection with the verisense device
        /// </summary>
        /// <param name="initialize">this will read the status, production configuration, operation configuration and set the time</param>
        /// <param name="configuration">if this is set, the operational configuration will be written before it is read back</param>
        /// <returns></returns>
        public async Task<bool> Connect(bool initialize, DeviceByteArraySettings configuration, bool keepDeviceSettings)
        {
            InitializeRadio();
            StateChange(ShimmerDeviceBluetoothState.Connecting);
            var result = await GetKnownDevice();
            if (!result)
            {
                StateChange(ShimmerDeviceBluetoothState.Disconnected);
                return false;
            }
            else
            {
                if (initialize)
                {
                    var sresult = await ExecuteRequest(RequestType.ReadStatus);
                    if (configuration.GetOperationalConfigurationBytes() != null)
                    {
                        var opConfig = configuration.GetOperationalConfigurationBytes();
                        if (keepDeviceSettings)
                        {
                            OpConfigPayload deviceOpConfig = (OpConfigPayload)await ExecuteRequest(RequestType.ReadOperationalConfig);
                            if (deviceOpConfig == null)
                            {
                                return false;
                            }
                            opConfig = UpdateDefaultDeviceConfigBytes(deviceOpConfig, configuration.GetOperationalConfigurationBytes());
                        }

                        var wopcresult = await ExecuteRequest(RequestType.WriteOperationalConfig, opConfig);
                        if (wopcresult == null)
                        {
                            return false;
                        }
                    }
                    var opcresult = await ExecuteRequest(RequestType.ReadOperationalConfig);
                    var pcresult = await ExecuteRequest(RequestType.ReadProductionConfig);
                    var rtcresult = await ExecuteRequest(RequestType.WriteRTC);
                    if (sresult != null && opcresult != null && pcresult != null && rtcresult != null)
                    {
                        StateChange(ShimmerDeviceBluetoothState.Connected);
                        return true;
                    }
                    else
                    {
                        await Disconnect();
                    }
                }
                else
                {
                    StateChange(ShimmerDeviceBluetoothState.Connected);
                    return true;
                }
            }
            return false;
        }

        /// <summary>
        /// To attempt a ble connection with the verisense device
        /// </summary>
        /// <param name="initialize">if a BLE connection is successful setting this to true will read the status, production configuration, operation configuration and set the time</param>
        /// <returns></returns>
        public async Task<bool> Connect(bool initialize)
        {
            return await Connect(initialize, DefaultVerisenseConfiguration.Unknown_Device_OpConfig_Setting, false);
        }

        /// <summary>
        /// To disconnect the verisense device
        /// </summary>
        /// <returns></returns>
        public async Task<bool> Disconnect()
        {   
            var result = await BLERadio.Disconnect();
            ResponseBuffer = null;
            if (result.Equals(ConnectivityState.Disconnected))
            {
                BLERadio.CommunicationEvent -= UartRX_ValueUpdated;
                StateChange(ShimmerDeviceBluetoothState.Disconnected);
                return true;
            } else if(result.Equals(ConnectivityState.Limited))
            {
                StateChange(ShimmerDeviceBluetoothState.Limited);
                return true;
            }
            return true;
        }

        /// <summary>
        /// Write and read the operation configuration. The read can be used to verify what was written.
        /// </summary>
        /// <returns>operational config payload</returns>
        public async Task<IBasePayload> WriteAndReadOperationalConfiguration(byte[] operationalConfiguration)
        {
            await ExecuteRequest(RequestType.WriteOperationalConfig, operationalConfiguration);
            var result = await ExecuteRequest(RequestType.ReadOperationalConfig);
            return result;
        }

        /// <summary>
        /// Convert mac address to ulong
        /// </summary>
        /// <returns></returns>
        public static ulong ConvertMACAddress(string macAddress)
        {
            string hex = macAddress.Replace(":", "");
            return Convert.ToUInt64(hex, 16);
        }

        private void ResetSensorTimestampOffsets()
        {
            foreach(var sensor in SensorList.Values)
            {
                sensor.ResetTimestamps();
            }
        }
    }
}
