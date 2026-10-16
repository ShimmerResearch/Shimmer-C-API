using System;
using System.IO;
using InTheHand.Bluetooth;
using System.Collections.Concurrent;
using System.Threading;
using System.Diagnostics;

namespace ShimmerAPI
{
    public class ShimmerLogAndStream32FeetBLE : ShimmerLogAndStream
    {
        private BluetoothDevice bluetoothDevice { get; set; }
        private GattService ServiceTXRX { get; set; }
        private GattCharacteristic UartTX { get; set; }
        private GattCharacteristic UartRX { get; set; }
        protected String macAddress { get; set; }
        ConcurrentQueue<byte> cq = new ConcurrentQueue<byte>();
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
            for (int i = 0; i < args.Value.Length; i++)
            {
                cq.Enqueue(args.Value[i]);
            }
        }

        protected override int ReadByte()
        {
            if (GetState() != SHIMMER_STATE_NONE)
            {
                byte b = 0xFF;
                //Timer timer = new Timer((obj) => throw new TimeoutException(), null, 30000, Timeout.Infinite);
                while (!cq.TryDequeue(out b))
                {

                }
                //timer.Dispose();
                return b;
            }
            throw new InvalidOperationException();
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
