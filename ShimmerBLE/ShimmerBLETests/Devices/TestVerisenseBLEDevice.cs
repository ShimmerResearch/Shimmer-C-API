using shimmer.Communications;
using shimmer.Models;
using shimmer.Sensors;
using ShimmerBLEAPI.Devices;
using ShimmerBLETests.Sensors;
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading.Tasks;

namespace ShimmerBLETests.Communications
{
    public class TestVerisenseBLEDevice : VerisenseBLEDevice
    {
        public TestVerisenseBLEDevice(string id, string name) : base(id, name)
        {

        }

        public TestVerisenseBLEDevice(string id, string name, byte[] opconfigbytes) : base(id, name)
        {
            OpConfig = new OpConfigPayload();
            OpConfig.ConfigurationBytes = new byte[opconfigbytes.Length];
            Array.Copy(opconfigbytes, OpConfig.ConfigurationBytes, opconfigbytes.Length); //deep copy
            UpdateDeviceAndSensorConfiguration();
        }

        public TestVerisenseBLEDevice(string id, string name, byte[] opconfigbytes, byte[] prodconfigbytes) : base(id, name)
        {
            OpConfig = new OpConfigPayload();
            OpConfig.ConfigurationBytes = new byte[opconfigbytes.Length];
            ProdConfig = new ProdConfigPayload();
            ProdConfig.ProcessPayload(prodconfigbytes);
            Array.Copy(opconfigbytes, OpConfig.ConfigurationBytes, opconfigbytes.Length); //deep copy
            UpdateDeviceAndSensorConfiguration();
        }

        protected override void InitializeRadio()
        {
            BLERadio = new TestByteRadio();
        }

        /// <summary>The fake radio, once Connect() has made it</summary>
        public TestByteRadio Radio => (TestByteRadio)BLERadio;

        public void InjectDataSyncEndBytes()
        {
            ((TestByteRadio)BLERadio).InjectEndBytes();
        }

        protected override void createBinFile(bool crcError)
        {
            try
            {
                //var asm = RealmService.GetSensorbyID(Asm_uuid.ToString());
                //var trialSettings = RealmService.LoadTrialSettings();

                //var participantID = asm.ParticipantID;
                binFileFolderDir = string.Format("{0}/{1}/{2}/BinaryFiles", GetTrialName(), GetParticipantID(), Asm_uuid.ToString());
                var folder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), binFileFolderDir);

                if (!Directory.Exists(folder))
                {
                    Directory.CreateDirectory(folder);
                }
                if (crcError)
                {
                    dataFileName = string.Format("{0}_{1}_{2}.bin", DateTime.Now.ToString("yyMMdd_HHmmss"), PayloadIndex.ToString("00000"), BadCRC);
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

        public void InjectRawPacketAccel1()
        {
            ((TestByteRadio)BLERadio).InjectRawPacketAccel1();
        }

        public void InjectRawPacketAccel2Gyro()
        {
            ((TestByteRadio)BLERadio).InjectRawPacketAccel2Gyro();
        }

        public void InjectRawPacketGSR()
        {
            ((TestByteRadio)BLERadio).InjectRawPacketGSR();
        }

        public void InjectRawPacketGSRVerisensePulsePlus()
        {
            ((TestByteRadio)BLERadio).InjectRawPacketGSRVerisensePulsePlus();
        }

        public void InjectRawPacketPPG()
        {
            ((TestByteRadio)BLERadio).InjectRawPacketPPG();
        }

        public void InjectRawPacket(int mtuSize)
        {
            ((TestByteRadio)BLERadio).InjectRawPacket(mtuSize);
        }
    }
}
