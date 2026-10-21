using System;
using System.IO;
using InTheHand.Bluetooth;
using System.Collections.Concurrent;
using System.Threading;
using System.Diagnostics;
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
            // Wakes a ReadByte that is waiting, so the read thread stops rather than wait for a byte
            // that cannot come
            received.Close();
            bluetoothDevice.GattServerDisconnected -= Device_GattServerDisconnected;
            bluetoothDevice.Gatt.Disconnect();
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
                    bluetoothDevice.Gatt.Disconnect();
                }
                Console.WriteLine("Radio Plugin 32Feet Exception " + ex.Message);
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

        protected override async void WriteBytes(byte[] b, int index, int length)
        {
            if (GetState() != SHIMMER_STATE_NONE)
            {
                await UartTX.WriteValueWithoutResponseAsync(b);
            }
        }
    }
}
