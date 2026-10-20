using ShimmerAPI;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ShimmerConsoleAppExample
{
    class Program
    {
        ShimmerLogAndStreamSystemSerialPort shimmer;

        static void Main(string[] args)
        {
            /* Example of using 32 feet to scan for devices
            var client = new BluetoothClient();
            BluetoothDeviceInfo[] availableDevices = client.DiscoverDevices(); // I've found this to be SLOW!
            */

            System.Console.WriteLine("Hello");
            Program p = new Program();
            p.start();
        }

        public void start()
        {
            //There are two main uses of the constructors, first one just connects to the device without setting and specific configurations
            //shimmer = new ShimmerSDBT("ShimmerID1", "COM15");
            int enabledSensors = ((int)ShimmerBluetooth.SensorBitmapShimmer3.SENSOR_A_ACCEL); // this is to enable Analog Accel also known as low noise accelerometer
            double samplingRate = 51.2;
            //byte[] defaultECGReg1 = new byte[10] { 0x00, 0xA0, 0x10, 0x40, 0x40, 0x2D, 0x00, 0x00, 0x02, 0x03 }; //see ShimmerBluetooth.SHIMMER3_DEFAULT_ECG_REG1
            //byte[] defaultECGReg2 = new byte[10] { 0x00, 0xA0, 0x10, 0x40, 0x47, 0x00, 0x00, 0x00, 0x02, 0x01 }; //see ShimmerBluetooth.SHIMMER3_DEFAULT_ECG_REG2
            byte[] defaultECGReg1 = ShimmerBluetooth.SHIMMER3_DEFAULT_TEST_REG1; //also see ShimmerBluetooth.SHIMMER3_DEFAULT_ECG_REG1
            byte[] defaultECGReg2 = ShimmerBluetooth.SHIMMER3_DEFAULT_TEST_REG2; //also see ShimmerBluetooth.SHIMMER3_DEFAULT_ECG_REG2
            //The constructor below allows the user to specify the shimmer configurations which is set upon connection to the device
            shimmer = new ShimmerLogAndStreamSystemSerialPort("ShimmerID1", "COM14", 1, 0, 4, 0, false, false, false, 0, 0, defaultECGReg1, defaultECGReg2, false,ShimmerBluetooth.BTCRCMode.TWO_BYTE);
            shimmer.UICallback += this.HandleEvent;
            shimmer.Connect();
            if (shimmer.GetState() == ShimmerBluetooth.SHIMMER_STATE_CONNECTED)
            {
                shimmer.WriteSamplingRate(samplingRate);
                shimmer.WriteSensors(enabledSensors);
                shimmer.StartStreaming();
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
                        System.Diagnostics.Debug.Write("Connected");
                    }
                    else if (state == (int)ShimmerBluetooth.SHIMMER_STATE_CONNECTING)
                    {
                        System.Diagnostics.Debug.Write("Connecting");
                    }
                    else if (state == (int)ShimmerBluetooth.SHIMMER_STATE_NONE)
                    {
                        System.Diagnostics.Debug.Write("Disconnected");
                    }
                    else if (state == (int)ShimmerBluetooth.SHIMMER_STATE_STREAMING)
                    {
                        System.Diagnostics.Debug.Write("Streaming");
                    }
                    break;
                case (int)ShimmerBluetooth.ShimmerIdentifier.MSG_IDENTIFIER_NOTIFICATION_MESSAGE:
                    System.Console.WriteLine(eventArgs.getObject());
                    break;
                case (int)ShimmerBluetooth.ShimmerIdentifier.MSG_IDENTIFIER_DATA_PACKET:
                    ObjectCluster objectCluster = (ObjectCluster)eventArgs.getObject();
                    SensorData data = objectCluster.GetData(Shimmer3Configuration.SignalNames.LOW_NOISE_ACCELEROMETER_X, "CAL");
                    System.Console.WriteLine("AccelX: " + data.Data);
                    
                    break;
            }
        }
    }
}
