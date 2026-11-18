using System;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using InTheHand.Bluetooth;

namespace ShimmerAPI
{
    /// <summary>
    /// Finds a BLE device by its address. Windows looks a BLE device up only among those it has seen
    /// advertise, and it may not have seen this one: after the device is power-cycled with nothing on the
    /// PC scanning since, BluetoothDevice.FromIdAsync returns null at once and the connect fails before
    /// it starts (DEV-1394). So when it does, this listens for the device's advertising and asks again.
    /// </summary>
    public static class BleDeviceLookup
    {
        /// <summary>
        /// How long to listen for the device's advertising before giving up. A Shimmer that is on and in
        /// range advertises several times a second, so it is heard well within this.
        /// </summary>
        public const int AdvertisingWaitMs = 10000;

        /// <summary>The device with this address, for example "d8478f04be69", or null if it was not heard.</summary>
        public static async Task<BluetoothDevice> FindAsync(string address)
        {
            BluetoothDevice device = await BluetoothDevice.FromIdAsync(address);
            if (device != null)
            {
                return device;
            }
            Console.WriteLine("Radio Plugin 32Feet: Windows knows no BLE device " + address + " yet; listening for its advertising");
            if (!await HearAdvertisingAsync(address, AdvertisingWaitMs))
            {
                Console.WriteLine("Radio Plugin 32Feet: no advertising from " + address + " within " + AdvertisingWaitMs / 1000 + " s");
                return null;
            }
            return await BluetoothDevice.FromIdAsync(address);
        }

        /// <summary>
        /// Whether the device advertised within the time. Once Windows has heard it, FromIdAsync finds it.
        /// Uses Windows' advertisement watcher directly, as InTheHand's .NET Framework build has no scan that
        /// reports each advertisement, so the wait ends as soon as this device is heard.
        /// </summary>
        [MethodImpl(MethodImplOptions.NoInlining)]
        static async Task<bool> HearAdvertisingAsync(string address, int timeoutMs)
        {
            ulong wanted;
            try
            {
                wanted = Convert.ToUInt64(address.Replace(":", "").Replace("-", ""), 16);
            }
            catch (FormatException)
            {
                return false;
            }
            var heard = new TaskCompletionSource<bool>();
            var watcher = new Windows.Devices.Bluetooth.Advertisement.BluetoothLEAdvertisementWatcher
            {
                ScanningMode = Windows.Devices.Bluetooth.Advertisement.BluetoothLEScanningMode.Active
            };
            Windows.Foundation.TypedEventHandler<Windows.Devices.Bluetooth.Advertisement.BluetoothLEAdvertisementWatcher, Windows.Devices.Bluetooth.Advertisement.BluetoothLEAdvertisementReceivedEventArgs> received =
                (sender, args) =>
                {
                    if (args.BluetoothAddress == wanted)
                    {
                        heard.TrySetResult(true);
                    }
                };
            watcher.Received += received;
            try
            {
                watcher.Start();
                return await Task.WhenAny(heard.Task, Task.Delay(timeoutMs)) == heard.Task;
            }
            catch (Exception ex)
            {
                Console.WriteLine("Radio Plugin 32Feet: could not listen for BLE advertising: " + ex.Message);
                return false;
            }
            finally
            {
                watcher.Stop();
                watcher.Received -= received;
            }
        }
    }
}
