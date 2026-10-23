using System;
using System.IO;
using InTheHand.Bluetooth;
using System.Collections.Concurrent;
using System.Threading;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using ShimmerAPI.Utilities;

namespace ShimmerAPI
{
    public class ShimmerLogAndStream32FeetBLE : ShimmerLogAndStream
    {
        private BluetoothDevice bluetoothDevice { get; set; }
        private GattService ServiceTXRX { get; set; }
        private GattCharacteristic UartTX { get; set; }
        private GattCharacteristic UartRX { get; set; }
        protected String macAddress { get; set; }
        /// <summary>What the notifications delivered, for ReadByte. A new one for each link: see OpenConnection.</summary>
        LinkByteBuffer received = new LinkByteBuffer();
        /// <summary>
        /// Holds Windows' short connection interval for this link: once released, the link goes back to
        /// Windows' default. Kept as an IDisposable, so a Windows without the request never loads its type.
        /// </summary>
        IDisposable connectionParametersRequest;
        private static bool Debug = false;
        /// <summary>
        /// 
        /// </summary>
        /// <param name="devID"></param>
        /// <param name="bMacAddress"> for example 114f439f84aa</param>
        /// <param name="hwVersion"></param>
        public ShimmerLogAndStream32FeetBLE(String devID, String bMacAddress, ShimmerVersion hwVersion)
          : base(devID)
        {
            HardwareVersion = (int)hwVersion;
            macAddress = bMacAddress;
        }

        public override string GetShimmerAddress()
        {
            return macAddress;
        }

        public override void SetShimmerAddress(string address)
        {
            macAddress = address;
        }

        protected override void CloseConnection()
        {
            try
            {
                bluetoothDevice.GattServerDisconnected -= Device_GattServerDisconnected;
                ReleaseConnectionParameters();
                bluetoothDevice.Gatt.Disconnect();
            }
            finally
            {
                // Wakes a ReadByte that is waiting, so the read thread stops rather than wait for a
                // byte that cannot come. Last, so Disconnect stops the thread straight after
                received.Close();
            }
        }

        protected override void FlushConnection()
        {

        }

        protected override void FlushInputConnection()
        {

        }

        protected override bool IsConnectionOpen()
        {
            if(bluetoothDevice == null)
            {
                return false;
            }
            return bluetoothDevice.Gatt.IsConnected;
        }

        private void ConnectFail()
        {
            
            CloseConnection();
            SetState(SHIMMER_STATE_NONE);
        }

        protected override void OpenConnection()
        {
            try
            {
                SetState(SHIMMER_STATE_CONNECTING);
                // A closed buffer stays closed, and a previous link's bytes are not this link's
                received = new LinkByteBuffer();
                bluetoothDevice = BluetoothDevice.FromIdAsync(macAddress).GetAwaiter().GetResult();
                bluetoothDevice.Gatt.ConnectAsync().GetAwaiter().GetResult();
                if (HardwareVersion == (int)ShimmerVersion.SHIMMER3)
                {
                    RequestShortConnectionInterval();
                }
                Console.WriteLine("current mtu value " + bluetoothDevice.Gatt.Mtu);
                BluetoothUuid TxID = BluetoothUuid.FromGuid(new Guid("49535343-8841-43f4-a8d4-ecbe34729bb3"));
                BluetoothUuid RxID = BluetoothUuid.FromGuid(new Guid("49535343-1e4d-4bd9-ba61-23c647249616"));
                BluetoothUuid ServiceID = BluetoothUuid.FromGuid(new Guid("49535343-fe7d-4ae5-8fa9-9fafd205e455"));

                if (HardwareVersion == (int)ShimmerVersion.SHIMMER3R)
                {
                    TxID = BluetoothUuid.FromGuid(new Guid("65333333-A115-11E2-9E9A-0800200CA102"));
                    RxID = BluetoothUuid.FromGuid(new Guid("65333333-A115-11E2-9E9A-0800200CA101"));
                    ServiceID = BluetoothUuid.FromGuid(new Guid("65333333-A115-11E2-9E9A-0800200CA100"));
                }
                else if (HardwareVersion == (int)ShimmerVersion.SHIMMER3)
                {
                    TxID = BluetoothUuid.FromGuid(new Guid("49535343-8841-43f4-a8d4-ecbe34729bb3"));
                    RxID = BluetoothUuid.FromGuid(new Guid("49535343-1e4d-4bd9-ba61-23c647249616"));
                    ServiceID = BluetoothUuid.FromGuid(new Guid("49535343-fe7d-4ae5-8fa9-9fafd205e455"));
                }


                ServiceTXRX = bluetoothDevice.Gatt.GetPrimaryServiceAsync(ServiceID).GetAwaiter().GetResult();
                if (ServiceTXRX != null)
                {
                    UartTX = ServiceTXRX.GetCharacteristicAsync(TxID).GetAwaiter().GetResult();
                    if (UartTX == null)
                    {
                        Console.WriteLine("UARTTX null");
                        ConnectFail();
                        return;
                    }
                    UartRX = ServiceTXRX.GetCharacteristicAsync(RxID).GetAwaiter().GetResult();
                    if (UartRX == null)
                    {
                        Console.WriteLine("UARTRX null");
                        ConnectFail();
                        return;
                    }
                    UartRX.CharacteristicValueChanged += Gc_ValueChanged;
                    UartRX.StartNotificationsAsync().GetAwaiter().GetResult();
                    bluetoothDevice.GattServerDisconnected += Device_GattServerDisconnected;
                    Console.WriteLine("current mtu value" + bluetoothDevice.Gatt.Mtu);
                } else
                {
                    Console.WriteLine("Service TXRX null");
                    ConnectFail();
                    return;
                }
            }
            catch (Exception ex)
            {
                if (bluetoothDevice != null)
                {
                    bluetoothDevice.GattServerDisconnected -= Device_GattServerDisconnected;
                    ReleaseConnectionParameters();
                    bluetoothDevice.Gatt.Disconnect();
                }
                Console.WriteLine("Radio Plugin 32Feet Exception " + ex.Message);
            }
        }

        /// <summary>
        /// Asks Windows for its short connection interval (ThroughputOptimized), which Windows 11 allows.
        /// On Windows' default interval a Shimmer3's RN4678 carries less than a 256 Hz stream needs, so it
        /// drops samples in bursts (DEV-1272). Where Windows lacks the request or refuses it, the link keeps
        /// the default interval. A Shimmer3R is not asked: it gets a better link on its own, and the
        /// request raised its longest silence from about 23 ms to 45-50 ms.
        /// </summary>
        private void RequestShortConnectionInterval()
        {
            try
            {
                if (!Windows.Foundation.Metadata.ApiInformation.IsMethodPresent("Windows.Devices.Bluetooth.BluetoothLEDevice", "RequestPreferredConnectionParameters"))
                {
                    Console.WriteLine("Radio Plugin 32Feet: this Windows cannot request a connection interval");
                    return;
                }
                RequestThroughputOptimizedConnectionParameters();
            }
            catch (Exception ex)
            {
                Console.WriteLine("Radio Plugin 32Feet connection interval request failed: " + ex.Message);
            }
        }

        /// <summary>Only called where Windows has the request, so its types load only there.</summary>
        [MethodImpl(MethodImplOptions.NoInlining)]
        private void RequestThroughputOptimizedConnectionParameters()
        {
            Windows.Devices.Bluetooth.BluetoothLEDevice device = bluetoothDevice;
            var request = device.RequestPreferredConnectionParameters(Windows.Devices.Bluetooth.BluetoothLEPreferredConnectionParameters.ThroughputOptimized);
            connectionParametersRequest = request;
            Console.WriteLine("Radio Plugin 32Feet connection interval request: " + request.Status);
        }

        private void ReleaseConnectionParameters()
        {
            IDisposable request = connectionParametersRequest;
            connectionParametersRequest = null;
            if (request != null)
            {
                try
                {
                    request.Dispose();
                }
                catch (Exception ex)
                {
                    Console.WriteLine("Radio Plugin 32Feet connection interval release failed: " + ex.Message);
                }
            }
        }

        private void Device_GattServerDisconnected(object sender, EventArgs e)
        {
            //connection lost
            /*
            Console.WriteLine("Connection Lost. Please reconnect.");
            bluetoothDevice.GattServerDisconnected -= Device_GattServerDisconnected;
            bluetoothDevice.Gatt.Disconnect();
            */
            bluetoothDevice.GattServerDisconnected -= Device_GattServerDisconnected;
            bluetoothDevice.Gatt.Disconnect();
            CustomEventArgs newEventArgs = new CustomEventArgs((int)ShimmerIdentifier.MSG_IDENTIFIER_NOTIFICATION_MESSAGE, "Connection lost");
            OnNewEvent(newEventArgs);
            Disconnect();
            
        }

        private void Gc_ValueChanged(object sender, GattCharacteristicValueChangedEventArgs args)
        {
            
            if (Debug)
            {
                Console.WriteLine("RXB:" + BitConverter.ToString(args.Value).Replace("-", ""));
            }
            received.Add(args.Value);
        }

        /// <summary>
        /// The next byte the device sent, waiting up to ReadTimeout for one and then throwing
        /// TimeoutException, as the serial port's read does. It used to spin until a byte arrived, with no
        /// timeout and no check of the link: the read thread took a whole CPU core while connected, and
        /// never stopped after Disconnect, so each link left a core spinning until the process ended
        /// (DEV-1270). ReadData's timeout handling, the idle status poll and "Connection lost", now
        /// works over BLE as it does over a serial port.
        /// </summary>
        protected override int ReadByte()
        {
            return received.Read(ReadTimeout);
        }

        /// <summary>
        /// Writes without waiting for the write to finish. This is async void, so an exception from the
        /// write would reach no caller and end the process instead: a write to a link that has gone, such
        /// as ReadData's status poll in the moment Disconnect tears the link down, is logged and dropped.
        /// </summary>
        protected override async void WriteBytes(byte[] b, int index, int length)
        {
            if (GetState() != SHIMMER_STATE_NONE)
            {
                try
                {
                    await UartTX.WriteValueWithoutResponseAsync(b);
                }
                catch (Exception ex)
                {
                    Console.WriteLine("Radio Plugin 32Feet write failed: " + ex.Message);
                }
            }
        }
    }
}
