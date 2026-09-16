using NUnit.Framework;
using ShimmerAPI.Utilities;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;

namespace ShimmerAPI
{
    

    public class ShimmerSDLog : ShimmerLogAndStream
    {

        public static class SDLogHeader
        {
            // 0..31 fit in int, but note: anything at bit 31 is safer as long.
            public const int ACCEL_LN = 1 << 7;
            public const int GYRO = 1 << 6;
            public const int MAG = 1 << 5;
            public const int EXG1_24BIT = 1 << 4;
            public const int EXG2_24BIT = 1 << 3;
            public const int GSR = 1 << 2;
            public const int EXT_EXP_A7 = 1 << 1;
            public const int EXT_EXP_A6 = 1 << 0;

            public const int BRIDGE_AMP = 1 << 15;
            public const int ECG_TO_HR_FW = 1 << 14;
            public const int BATTERY = 1 << 13;
            public const int ACCEL_WR = 1 << 12;
            public const int EXT_EXP_A15 = 1 << 11;
            public const int INT_EXP_A1 = 1 << 10;
            public const int INT_EXP_A12 = 1 << 9;
            public const int INT_EXP_A13 = 1 << 8;

            // These are still within 0..31, so int is OK
            public const int INT_EXP_A14 = 1 << 23;
            public const int ACCEL_MPU = 1 << 22;
            public const int MAG_MPU = 1 << 21;
            public const int EXG1_16BIT = 1 << 20;
            public const int EXG2_16BIT = 1 << 19;
            public const int BMPX80 = 1 << 18;
            public const int MPL_TEMPERATURE = 1 << 17;
            // bit 16 unused in your list

            // Use long for anything that could overflow an int or that you may OR into a long mask.
            // In Java you cast for 1<<31 because int would overflow; same idea in C#.
            public const long MPL_QUAT_6DOF = 1L << 31;

            public const int MPL_QUAT_9DOF = 1 << 30;
            public const int MPL_EULER_6DOF = 1 << 29;
            public const int MPL_EULER_9DOF = 1 << 28;
            public const int MPL_HEADING = 1 << 27;
            public const int MPL_PEDOMETER = 1 << 26;
            public const int MPL_TAP = 1 << 25;
            public const int MPL_MOTION_ORIENT = 1 << 24;

            public const long GYRO_MPU_MPL = 1L << 39;
            public const long ACCEL_MPU_MPL = 1L << 38;
            public const long MAG_MPU_MPL = 1L << 37;
            public const long SD_SENSOR_MPL_QUAT_6DOF_RAW = 1L << 36;

            // 1<<35, 1<<34, 1<<33, 1<<32 unused
        }

        private string _absoluteFilePath;
        private string _sdFileNumber;
        private long FileSize;
        private string _fileName;
        private BufferedStream _bin; // like Java BufferedInputStream
        private long mTrackBytesRead = 0;//256; // the header will always be read
        private byte[] mSdHeaderBytes;
        private long mTrialId = 0;
        private long mTrialNumberOfShimmers = 0;
        private Boolean mBluetoothDisabled = false;
        private string MacAddress = "";
        private long mSampleCount = 0;
        public Boolean EndOfFile = false;
        protected override bool ShouldAddSystemTimestamp => false;
        public ShimmerSDLog(string filePath)
        {
            _absoluteFilePath = filePath;
            var fileInfo = new FileInfo(_absoluteFilePath); // will throw if path is invalid/null
            if (!fileInfo.Exists)
                throw new FileNotFoundException($"{_absoluteFilePath} does not exist", _absoluteFilePath);

            ParseInfoFromDirStructure(_absoluteFilePath);
            ReadSDVersionFromHeader();
            OpenLog();
            byte[] fileContent = ReadSdConfigHeader();
            ProcessSDLogHeader(fileContent);
        }

        private void ParseInfoFromDirStructure(string filePath)
        {
            // Java would throw if length < 3. Decide if you want same behavior:
            if (filePath == null) throw new ArgumentNullException(nameof(filePath));
            if (filePath.Length < 3) throw new ArgumentException("filePath must be at least 3 characters long", nameof(filePath));

            _sdFileNumber = filePath.Substring(filePath.Length - 3, 3);
        }

        public bool OpenLog()
        {
            var fileInfo = new FileInfo(_absoluteFilePath); // will throw if path is invalid/null
            if (!fileInfo.Exists)
                throw new FileNotFoundException($"{_absoluteFilePath} does not exist", _absoluteFilePath);

            FileSize = fileInfo.Length;
            _fileName = fileInfo.Name;

            // FileSize = 178 = SDLog v0.5.0
            if (FileSize < 178)
                return false;

            // Equivalent to: FileInputStream + BufferedInputStream
            // (leave open if you manage lifetime elsewhere)
            var fs = new FileStream(_absoluteFilePath, FileMode.Open, FileAccess.Read, FileShare.Read);
            _bin = new BufferedStream(fs); // optionally: new BufferedStream(fs, 8192 * 10);

            return true;
        }
        protected void ReadSDVersionFromHeader()
        {
           try
            {
                byte[] fileContent = new byte[256];

                using (var fs = new FileStream(_absoluteFilePath, FileMode.Open, FileAccess.Read, FileShare.Read))
                {
                    int totalRead = 0;
                    while (totalRead < fileContent.Length)
                    {
                        int read = fs.Read(fileContent, totalRead, fileContent.Length - totalRead);
                        if (read == 0) break; // EOF
                        totalRead += read;
                    }

                    // If you want exact Java behavior (it doesn't check), you can omit this.
                    // If you want to be safer:
                    // if (totalRead < 256) throw new EndOfStreamException($"Expected 256 bytes, got {totalRead}.");
                }

                // Java used (byte & 0xFF) to treat bytes as unsigned.
                // In C#, byte is already unsigned (0..255).
                HardwareVersion = (fileContent[30] << 8) | fileContent[31];
                FirmwareIdentifier = (fileContent[34] << 8) | fileContent[35];
                FirmwareMajor = (fileContent[36] << 8) | fileContent[37];
                FirmwareMinor = fileContent[38];
                FirmwareInternal = fileContent[39];

            }
            catch (FileNotFoundException ex)
            {
                Console.WriteLine(ex);
            }
            catch (IOException ex)
            {
                Console.WriteLine(ex);
            }

        }
        private byte[] ReadSdConfigHeader()
        {
            byte[] sdHeaderBytes = new byte[256];

            if (HardwareVersion == (int)ShimmerVersion.SHIMMER3R)
            {
                sdHeaderBytes = new byte[384];
            }

            try
            {
                ReadFromLog(sdHeaderBytes); // should fill entire buffer
            }
            catch (IOException ex)
            {
                // Java prints stack trace; C# equivalent:
                Console.Error.WriteLine(ex);
            }

            return sdHeaderBytes;
        }

        protected int ReadFromLog(byte[] newPacket)
        {
            // Equivalent of Java's newPacket.length
            mTrackBytesRead += newPacket.Length;

            // Equivalent of Java's bin.read(newPacket)
            // (assumes `bin` is a Stream, e.g., FileStream, BufferedStream, etc.)
            return _bin.Read(newPacket, 0, newPacket.Length);
        }


public void ProcessSDLogHeader(byte[] byteArrayInfo)
    {
        mSdHeaderBytes = byteArrayInfo;

        // In C#, byte is already unsigned (0..255), so no need for & 0xFF
        mTrialId = byteArrayInfo[32];
        mTrialNumberOfShimmers = byteArrayInfo[33];

        byte[] signalIdArray = null;

        
        // 0-1 Byte = Sampling Rate (little-endian per original Java expression)
        long rawSamplingRate = byteArrayInfo[0] | (byteArrayInfo[1] << 8);
        

        SamplingRate = (32768 / rawSamplingRate);

        ParseEnabledDerivedSensorsForMaps(byteArrayInfo);

        ExpansionBoardId = byteArrayInfo[214];
        ExpansionBoardRev = byteArrayInfo[215];
        ExpansionBoardRevSpecial = byteArrayInfo[216];


        if (HardwareVersion == (int) ShimmerVersion.SHIMMER3R)
        {
            SetAccelSamplingRate((byteArrayInfo[8] >> 4) & 0x0F);
            SetAccelRange((byteArrayInfo[8] >> 2) & 0x03);
            LowPowerAccelEnabled = (((byteArrayInfo[8] >> 1) & 0x01) == 1);

            GyroSamplingRate = byteArrayInfo[9];

            AltMagRange = ((byteArrayInfo[10] >> 5) & 0x07);
            AltMagSamplingRate = ((byteArrayInfo[10] >> 2) & 0x07);

            int gyroRange = (byteArrayInfo[10]) & 0x03;
            int msbGyroRange = (byteArrayInfo[12] >> 2) & 0x01;
            SetGyroRange(gyroRange + (msbGyroRange << 2));

            int pressureResolution = (byteArrayInfo[11] >> 4) & 0x03;
            int msbPressureResolution = (byteArrayInfo[12]) & 0x01;
            SetPressureResolution(pressureResolution + (msbPressureResolution << 2));

            SetGSRRange((byteArrayInfo[11] >> 1) & 0x07);
            SetInternalExpPower((byteArrayInfo[11] >> 0) & 0x01);

            SetUserButton(((byteArrayInfo[16] >> 5) & 0x01)==1);
            mBluetoothDisabled = (((byteArrayInfo[16] >> 3) & 0x01)==1);
            /*
            setRTCSetByBT((byteArrayInfo[16] >> 7) & 0x01);
            setSyncWhenLogging((byteArrayInfo[16] >> 2) & 0x01);
            setMasterShimmer((byteArrayInfo[16] >> 1) & 0x01);

            setSingleTouch((byteArrayInfo[17] >> 7) & 0x01);
            */
            int i = (byteArrayInfo[17] >> 4) & 0x01;
            if (i > 0)
            {
                Console.Error.WriteLine("here");
            }
            /*
            setTCXO((byteArrayInfo[17] >> 4) & 0x01);

            mBroadcastInterval = byteArrayInfo[18];

            setTrialDurationEstimatedInSecs((byteArrayInfo[20] << 8) | byteArrayInfo[21]);
            setTrialDurationMaximumInSecs((byteArrayInfo[22] << 8) | byteArrayInfo[23]);
            */
            // Bluetooth address (same formatting as Java version: concatenated hex, no colons)
            string b1 = byteArrayInfo[24].ToString("x2");
            string b2 = byteArrayInfo[25].ToString("x2");
            string b3 = byteArrayInfo[26].ToString("x2");
            string b4 = byteArrayInfo[27].ToString("x2");
            string b5 = byteArrayInfo[28].ToString("x2");
            string b6 = byteArrayInfo[29].ToString("x2");
            SetShimmerAddress( b1 + b2 + b3 + b4 + b5 + b6 );
            /*    
            // RTC Difference (8 bytes big-endian)
            long rtcDiff =
                ((long)byteArrayInfo[44] << 56) |
                ((long)byteArrayInfo[45] << 48) |
                ((long)byteArrayInfo[46] << 40) |
                ((long)byteArrayInfo[47] << 32) |
                ((long)byteArrayInfo[48] << 24) |
                ((long)byteArrayInfo[49] << 16) |
                ((long)byteArrayInfo[50] << 8) |
                ((long)byteArrayInfo[51]);
            setRTCDifferenceInTicks(rtcDiff);
            */
            // EXG Configuration
            Buffer.BlockCopy(byteArrayInfo, 56, Exg1RegArray, 0, 10);
            Buffer.BlockCopy(byteArrayInfo, 66, Exg2RegArray, 0, 10);

            // get channel id
            NumberofChannels = byteArrayInfo[314];
            signalIdArray = new byte[NumberofChannels];
            Array.Copy(byteArrayInfo, 315, signalIdArray, 0, NumberofChannels);
        }
        else
        {
                // The Configuration byte index 8 - 19
            SetAccelSamplingRate((byteArrayInfo[8] >> 4) & 0x0F);
            SetAccelRange((byteArrayInfo[8] >> 2) & 0x03);
            LowPowerAccelEnabled = (((byteArrayInfo[8] >> 1) & 0x01) == 1);

            GyroSamplingRate = byteArrayInfo[9];

            AltMagRange = ((byteArrayInfo[10] >> 5) & 0x07);
            AltMagSamplingRate = ((byteArrayInfo[10] >> 2) & 0x07);

            int gyroRange = (byteArrayInfo[10]) & 0x03;
            int msbGyroRange = (byteArrayInfo[12] >> 2) & 0x01;
            SetGyroRange(gyroRange + (msbGyroRange << 2));

            int pressureResolution = (byteArrayInfo[11] >> 4) & 0x03;
            int msbPressureResolution = (byteArrayInfo[12]) & 0x01;
            SetPressureResolution(pressureResolution + (msbPressureResolution << 2));

            SetGSRRange((byteArrayInfo[11] >> 1) & 0x07);
            SetInternalExpPower((byteArrayInfo[11] >> 0) & 0x01);
            /*
            setMPU9150DMP((byteArrayInfo[12] >> 7) & 0x01);
            setMPU9150LPF((byteArrayInfo[12] >> 3) & 0x07);
            setMPU9150MotCalCfg((byteArrayInfo[12] >> 0) & 0x07);
            setMPU9150MPLSamplingRate((byteArrayInfo[13] >> 5) & 0x07);
            setMPU9150MagSamplingRate((byteArrayInfo[13] >> 2) & 0x07);

            setMPLSensorFusion((byteArrayInfo[14] >> 7) & 0x01);
            setMPLGyroCalTC((byteArrayInfo[14] >> 6) & 0x01);
            setMPLVectCompCal((byteArrayInfo[14] >> 5) & 0x01);
            setMPLMagDistCal((byteArrayInfo[14] >> 4) & 0x01);
            setMPLEnabled((byteArrayInfo[14] >> 3) & 0x01);
            */
            SetUserButton(((byteArrayInfo[16] >> 5) & 0x01) == 1);
            mBluetoothDisabled = (((byteArrayInfo[16] >> 3) & 0x01) == 1);
            /*
            setRTCSetByBT((byteArrayInfo[16] >> 7) & 0x01);
            setSyncWhenLogging((byteArrayInfo[16] >> 2) & 0x01);
            setMasterShimmer((byteArrayInfo[16] >> 1) & 0x01);
            setSingleTouch((byteArrayInfo[17] >> 7) & 0x01);
            */
            int i = (byteArrayInfo[17] >> 4) & 0x01;
            if (i > 0)
            {
                Console.Error.WriteLine("here");
            }
            /*
            setTCXO((byteArrayInfo[17] >> 4) & 0x01);

            mBroadcastInterval = byteArrayInfo[18];

            setTrialDurationEstimatedInSecs((byteArrayInfo[20] << 8) | byteArrayInfo[21]);
            setTrialDurationMaximumInSecs((byteArrayInfo[22] << 8) | byteArrayInfo[23]);
            */

            string b1 = byteArrayInfo[24].ToString("x2");
            string b2 = byteArrayInfo[25].ToString("x2");
            string b3 = byteArrayInfo[26].ToString("x2");
            string b4 = byteArrayInfo[27].ToString("x2");
            string b5 = byteArrayInfo[28].ToString("x2");
            string b6 = byteArrayInfo[29].ToString("x2");
            SetShimmerAddress( b1 + b2 + b3 + b4 + b5 + b6 );
            /*
            long rtcDiff =
                ((long)byteArrayInfo[44] << 56) |
                ((long)byteArrayInfo[45] << 48) |
                ((long)byteArrayInfo[46] << 40) |
                ((long)byteArrayInfo[47] << 32) |
                ((long)byteArrayInfo[48] << 24) |
                ((long)byteArrayInfo[49] << 16) |
                ((long)byteArrayInfo[50] << 8) |
                ((long)byteArrayInfo[51]);
            setRTCDifferenceInTicks(rtcDiff);
            */
            Buffer.BlockCopy(byteArrayInfo, 56, Exg1RegArray, 0, 10);
            Buffer.BlockCopy(byteArrayInfo, 66, Exg2RegArray, 0, 10);
        }

            // Configuration Time (4 bytes big-endian)
            configtime =
            ((long)byteArrayInfo[52] << 24) |
            ((long)byteArrayInfo[53] << 16) |
            ((long)byteArrayInfo[54] << 8) |
            ((long)byteArrayInfo[55]);

        int indexWRaccelparam = 76;
        int indexLNaccelparam = 139;
        int indexGyroparam = 97;
        int indexMagparam = 118;
        int indexTempPres = 160;
        int indexAltAccel = 256;
        int indexAltMag = 285;
        
        
        // Digital Accel Calibration Configuration
        byte[] mDigiAccelCalRawParams = new byte[21];
        Buffer.BlockCopy(byteArrayInfo, indexWRaccelparam, mDigiAccelCalRawParams, 0, 21);
        RetrieveKinematicCalibrationParametersFromPacket(mDigiAccelCalRawParams, (byte)PacketTypeShimmer3.WR_ACCEL_CALIBRATION_RESPONSE);
        
        // Gyroscope Calibration Configuration
        byte[] mGyroCalRawParams = new byte[21];
        Buffer.BlockCopy(byteArrayInfo, indexGyroparam, mGyroCalRawParams, 0, 21);
        RetrieveKinematicCalibrationParametersFromPacket(mGyroCalRawParams, (byte)PacketTypeShimmer3.GYRO_CALIBRATION_RESPONSE);

        // Magnetometer Calibration Configuration
        byte[] mMagCalRawParams = new byte[21];
        Buffer.BlockCopy(byteArrayInfo, indexMagparam, mMagCalRawParams, 0, 21);
        RetrieveKinematicCalibrationParametersFromPacket(mMagCalRawParams, (byte)PacketTypeShimmer3.MAG_CALIBRATION_RESPONSE);

        // Analog Accel Calibration Configuration
        byte[] mAccelCalRawParams = new byte[21];
        Buffer.BlockCopy(byteArrayInfo, indexLNaccelparam, mAccelCalRawParams, 0, 21);
        RetrieveKinematicCalibrationParametersFromPacket(mAccelCalRawParams, (byte)PacketTypeShimmer3.LNACCEL_CALIBRATION_RESPONSE);
        
        byte[] pressureCalRawParams = new byte[24];
        Buffer.BlockCopy(byteArrayInfo, indexTempPres, pressureCalRawParams, 0, 22);
        if (isShimmer3withUpdatedSensors())
        {
            Buffer.BlockCopy(byteArrayInfo, 222, pressureCalRawParams, 22, 2);
        }

        if (HardwareVersion == (int)ShimmerVersion.SHIMMER3R)
        {
                CalculateBMP390PressureCalibrationCoefficientsResponse(pressureCalRawParams);
        }
        else
        {
            if (isShimmer3withUpdatedSensors())
            {
                CalculateBMP280PressureCalibrationCoefficientsResponse(pressureCalRawParams);
            }
            else
            {
                CalculateBMP180PressureCalibrationCoefficientsResponse(pressureCalRawParams);
            }
        }

        if (HardwareVersion == (int)ShimmerVersion.SHIMMER3R)
        {   
            //alt accel, alt mag
            byte[] mAltAccelCalRawParams = new byte[21];
            Buffer.BlockCopy(byteArrayInfo, indexAltAccel, mAltAccelCalRawParams, 0, 21);
            RetrieveKinematicCalibrationParametersFromPacket(mAltAccelCalRawParams, (byte)PacketTypeShimmer3.ALT_ACCEL_CALIBRATION_RESPONSE);

            byte[] mAltMagRawParams = new byte[21];
            Buffer.BlockCopy(byteArrayInfo, indexAltMag, mAltMagRawParams, 0, 21);
            RetrieveKinematicCalibrationParametersFromPacket(mAltMagRawParams, (byte)PacketTypeShimmer3.ALT_MAG_CALIBRATION_RESPONSE);

        }

                /*
                if (isLegacySdLog())
                {
                    long temp =
                        ((long)byteArrayInfo[177] << 24) |
                        ((long)byteArrayInfo[176] << 16) |
                        ((long)byteArrayInfo[175] << 8) |
                        ((long)byteArrayInfo[174]);
                    // TODO review this, should initial timestamp be temp instead of 0?
                    setInitialTimeStampTicksSd(0);
                }
                else
                {
                    if (mShimmerVerObject.isSupportedMpl() &&
                        (getFirmwareIdentifier() == FW_ID.SDLOG &&
                         getFirmwareVersionMajor() == 0 &&
                         getFirmwareVersionMinor() <= 12 &&
                         getFirmwareVersionInternal() <= 3))
                    {
                        if (mSensorMpu9x50 != null)
                        {
                            byte[] bufferCalibrationParameters = new byte[21];

                            Buffer.BlockCopy(byteArrayInfo, 182, bufferCalibrationParameters, 0, 21);
                            mSensorMpu9x50.setCalibParamMPLAccel(bufferCalibrationParameters);

                            bufferCalibrationParameters = new byte[21];
                            Buffer.BlockCopy(byteArrayInfo, 203, bufferCalibrationParameters, 0, 21);
                            mSensorMpu9x50.setCalibParamMPLMag(bufferCalibrationParameters);

                            bufferCalibrationParameters = new byte[21];
                            Buffer.BlockCopy(byteArrayInfo, 224, bufferCalibrationParameters, 0, 21);
                            mSensorMpu9x50.setCalibParamMPLGyro(bufferCalibrationParameters);
                        }
                    }
                    else
                    {
                        if (isThisVerCompatibleWith(FW_ID.SDLOG, 0, 12, 4) ||
                            isThisVerCompatibleWith(FW_ID.LOGANDSTREAM, 0, 6, 13))
                        {
                            getCurrentCalibDetailsAccelWr().setCalibTimeMs(parseCalibTimeKinematic(byteArrayInfo, 182));
                            getCurrentCalibDetailsGyro().setCalibTimeMs(parseCalibTimeKinematic(byteArrayInfo, 190));
                            getCurrentCalibDetailsMag().setCalibTimeMs(parseCalibTimeKinematic(byteArrayInfo, 198));
                            getCurrentCalibDetailsAccelLn().setCalibTimeMs(parseCalibTimeKinematic(byteArrayInfo, 206));
                        }
                    }

                    // Initial timestamp (5 bytes mixed order: 251 then 255..252)
                    long tempTs =
                        ((long)byteArrayInfo[251] << 32) |
                        ((long)byteArrayInfo[255] << 24) |
                        ((long)byteArrayInfo[254] << 16) |
                        ((long)byteArrayInfo[253] << 8) |
                        ((long)byteArrayInfo[252]);
                    setInitialTimeStampTicksSd(tempTs);

                    // start of 3R code
                    if (getHardwareVersion() == HW_ID.SHIMMER_3R)
                    {
                        byte[] mAltAccelCalRawParams = new byte[21];
                        Buffer.BlockCopy(byteArrayInfo, indexAltAccel, mAltAccelCalRawParams, 0, 21);
                        getCurrentCalibDetailsAccelAlt(mAltAccelCalRawParams, CALIB_READ_SOURCE.SD_HEADER);

                        byte[] mAltMagCalRawParams = new byte[21];
                        Buffer.BlockCopy(byteArrayInfo, indexAltMag, mAltMagCalRawParams, 0, 21);
                        getCurrentCalibDetailsMagWr(mAltMagCalRawParams, CALIB_READ_SOURCE.SD_HEADER);
                    }
                }

                if (getFirmwareIdentifier() == FW_ID.GQ_802154)
                {
                    // Session number from SD header
                    mDbSessionNumber = (byteArrayInfo[249] << 8) | byteArrayInfo[250];
                }
                else if (getFirmwareIdentifier() == FW_ID.STROKARE)
                {
                    // Session number based on SD Session Number
                    mDbSessionNumber = int.Parse(mSdSessionNumber, CultureInfo.InvariantCulture);
                }

                // NOTE: same memory caveat as Java
                initializeAlgorithms();
                */
                if (signalIdArray != null && HardwareVersion == (int)ShimmerVersion.SHIMMER3R)
        {
            CompatibilityCode = 9;
            TimeStampPacketByteSize = 3;
            InterpretDataPacketFormat(NumberofChannels, signalIdArray);
        }
        else
        {
            InterpretDataPacketFormat();
        }

        
    }

        /// <summary>
        /// Reads the next usable sample from the file.
        /// <para>
        /// Records whose timestamp field is zero are dropped rather than returned. The
        /// firmware stamps a packet when its sample tick starts it and does not publish
        /// one it never stamped, so such a record is invalid - its sensor data is fine
        /// but there is nothing to place it on the timeline with. LogAndStream
        /// v1.00.x-v1.01.003 could write one under SD write back-pressure, and reading
        /// it as a 24-bit roll-over adds 512 seconds to everything after it.
        /// </para>
        /// <para><see cref="RejectedTimestampRowCount"/> reports how many were dropped.</para>
        /// </summary>
        /// <returns>the next sample, or null at end of file</returns>
        public ObjectCluster ReadPacketMsg()
        {
            ObjectCluster ojc = ReadOnePacketMsg();

            while (ojc != null && LastTimestampRejected)
            {
                RejectedTimestampRowCount++;
                if (EndOfFile)
                {
                    return null;
                }
                ojc = ReadOnePacketMsg();
            }

            return ojc;
        }

        /// <summary>
        /// How many records this pass dropped because they carried no timestamp. Zero
        /// for a file written by firmware without the defect described on
        /// <see cref="ReadPacketMsg"/>.
        /// </summary>
        public int RejectedTimestampRowCount { get; private set; }

        /// <summary>One physical record, whether or not it is usable.</summary>
        private ObjectCluster ReadOnePacketMsg()
        {
            int fullPacketSize;

            // indicates when there will be an offset value
            bool timeSync = false;

            fullPacketSize = PacketSize;

            byte[] newPacket = new byte[fullPacketSize];

            if (ReadFromLog(newPacket) != 0)
            {
                mSampleCount++;

                // Java: super.buildMsg(newPacket, COMMUNICATION_TYPE.SD, timeSync, -1);
                ObjectCluster ojc = base.BuildMsg(newPacket.ToList());

                if (mTrackBytesRead == FileSize)
                {
                    EndOfFile = true;
                }

                return ojc;
            }
            else
            {
                EndOfFile = true;

                if (getBytesAvailable() == 0)
                {
                    Console.Write("EOF");
                    return null;
                }

                // TODO: Should this really throw an exception?
                throw new Exception("Not enough bytes to form a new packet");
            }
        }

        private void ParseEnabledDerivedSensorsForMaps(byte[] byteArrayInfo)
        {
            byte[] enabledSensorsByteArray = new byte[5];
                // 3-7 Byte = Sensors
            Array.Copy(byteArrayInfo, 3, enabledSensorsByteArray, 0, 5);

            long a = (long)enabledSensorsByteArray[0] & 0xFF;
            long b = ((long)enabledSensorsByteArray[1] & 0xFF) << 8;
            long c = ((long)enabledSensorsByteArray[2] & 0xFF) << 16;
            long d = ((long)enabledSensorsByteArray[3] & 0xFF) << 24;
            long e = ((long)enabledSensorsByteArray[4] & 0xFF) << 32;

            EnabledSensors = a + b + c + d + e;

            // Derived sensors (3 bytes)
            DerivedSensors =
                ((long)(byteArrayInfo[40] & 0xFF)) +
                (((long)(byteArrayInfo[41] & 0xFF)) << 8) +
                (((long)(byteArrayInfo[42] & 0xFF)) << 16);

            // Optional extra derived sensors (8 bytes total)
            //if (mShimmerVerObject.IsSupportedEightByteDerivedSensors())
            //{
            DerivedSensors |= ((long)(byteArrayInfo[217] & 0xFF)) << (8 * 3);
            DerivedSensors |= ((long)(byteArrayInfo[218] & 0xFF)) << (8 * 4);
            DerivedSensors |= ((long)(byteArrayInfo[219] & 0xFF)) << (8 * 5);
            DerivedSensors |= ((long)(byteArrayInfo[220] & 0xFF)) << (8 * 6);
            DerivedSensors |= ((long)(byteArrayInfo[221] & 0xFF)) << (8 * 7);
            //}

        }

        protected long getBytesAvailable() 
        {
                if (!_bin.CanSeek) return 0; // or throw
                return _bin.Length - _bin.Position;
        }

        private void InterpretDataPacketFormat()
        {
            NumberofChannels = 1;
            PacketSize = 3;

            string[] signalNameArray = new string[MAX_NUMBER_OF_SIGNALS];
            string[] signalDataTypeArray = new string[MAX_NUMBER_OF_SIGNALS];

            int i = 0;


            // Timestamp
            signalNameArray[i] = ShimmerConfiguration.SignalNames.TIMESTAMP;
            signalDataTypeArray[i] = "u24";

            // Shimmer3R branch
           
            if (HardwareVersion == (int)ShimmerVersion.SHIMMER3)
            {
                // Shimmer3 / others branch

                if ((EnabledSensors & SDLogHeader.ACCEL_LN) > 0)
                {
                    NumberofChannels += 3;
                    PacketSize += 6;

                    signalNameArray[i + 1] = Shimmer3Configuration.SignalNames.LOW_NOISE_ACCELEROMETER_X;
                    signalDataTypeArray[i + 1] = "u12";
                    i++;

                    signalNameArray[i + 1] = Shimmer3Configuration.SignalNames.LOW_NOISE_ACCELEROMETER_Y;
                    signalDataTypeArray[i + 1] = "u12";
                    i++;

                    signalNameArray[i + 1] = Shimmer3Configuration.SignalNames.LOW_NOISE_ACCELEROMETER_Z;
                    signalDataTypeArray[i + 1] = "u12";
                    i++;
                }

                if ((EnabledSensors & SDLogHeader.BATTERY) > 0)
                {
                    NumberofChannels += 1;
                    PacketSize += 2;

                    signalNameArray[i + 1] = Shimmer3Configuration.SignalNames.V_SENSE_BATT;
                    signalDataTypeArray[i + 1] = "u12";
                    i++;
                }

                // ADC channels
                if ((EnabledSensors & SDLogHeader.EXT_EXP_A7) > 0)
                {
                    NumberofChannels += 1;
                    PacketSize += 2;

                    signalNameArray[i + 1] = Shimmer3Configuration.SignalNames.EXTERNAL_ADC_A7;
                    signalDataTypeArray[i + 1] = "u12";
                    i++;
                }

                if ((EnabledSensors & SDLogHeader.EXT_EXP_A6) > 0)
                {
                    NumberofChannels += 1;
                    PacketSize += 2;

                    signalNameArray[i + 1] = Shimmer3Configuration.SignalNames.EXTERNAL_ADC_A6;
                    signalDataTypeArray[i + 1] = "u12";
                    i++;
                }

                if ((EnabledSensors & SDLogHeader.EXT_EXP_A15) > 0)
                {
                    NumberofChannels += 1;
                    PacketSize += 2;

                    signalNameArray[i + 1] = Shimmer3Configuration.SignalNames.EXTERNAL_ADC_A15;
                    signalDataTypeArray[i + 1] = "u12";
                    i++;
                }

                if ((EnabledSensors & SDLogHeader.INT_EXP_A12) > 0)
                {
                    NumberofChannels += 1;
                    PacketSize += 2;

                    signalNameArray[i + 1] = Shimmer3Configuration.SignalNames.INTERNAL_ADC_A12;
                    signalDataTypeArray[i + 1] = "u12";
                    i++;
                }

                if ((EnabledSensors & SDLogHeader.INT_EXP_A13) > 0)
                {
                    NumberofChannels += 1;
                    PacketSize += 2;

                    signalNameArray[i + 1] = Shimmer3Configuration.SignalNames.INTERNAL_ADC_A13;
                    signalDataTypeArray[i + 1] = "u12";
                    i++;
                }

                if ((EnabledSensors & SDLogHeader.INT_EXP_A14) > 0)
                {
                    NumberofChannels += 1;
                    PacketSize += 2;

                    signalNameArray[i + 1] = Shimmer3Configuration.SignalNames.INTERNAL_ADC_A14;
                    signalDataTypeArray[i + 1] = "u12";
                    i++;
                }

                // Bridge amp high + low
                if ((EnabledSensors & SDLogHeader.BRIDGE_AMP) > 0)
                {
                    NumberofChannels += 1;
                    PacketSize += 2;

                    signalNameArray[i + 1] = Shimmer3Configuration.SignalNames.BRIGE_AMPLIFIER_HIGH;
                    signalDataTypeArray[i + 1] = "u12";
                    i++;
                }

                if ((EnabledSensors & SDLogHeader.BRIDGE_AMP) > 0)
                {
                    NumberofChannels += 1;
                    PacketSize += 2;

                    signalNameArray[i + 1] = Shimmer3Configuration.SignalNames.BRIGE_AMPLIFIER_LOW;
                    signalDataTypeArray[i + 1] = "u12";
                    i++;
                }

                // GSR
                if ((EnabledSensors & SDLogHeader.GSR) > 0)
                {
                    NumberofChannels += 1;
                    PacketSize += 2;

                    signalNameArray[i + 1] = Shimmer3Configuration.SignalNames.GSR;

                    signalDataTypeArray[i + 1] = "u16";
                    i++;
                }

                // A1
                if ((EnabledSensors & SDLogHeader.INT_EXP_A1) > 0)
                {
                    NumberofChannels += 1;
                    PacketSize += 2;

                    signalNameArray[i + 1] = Shimmer3Configuration.SignalNames.INTERNAL_ADC_A1;
                    signalDataTypeArray[i + 1] = "u12";
                    i++;
                }

                // Gyro
                if ((EnabledSensors & SDLogHeader.GYRO) > 0)
                {
                    NumberofChannels += 3;
                    PacketSize += 6;

                    signalNameArray[i + 1] = Shimmer3Configuration.SignalNames.GYROSCOPE_X;
                    signalDataTypeArray[i + 1] = "i16r";
                    i++;

                    signalNameArray[i + 1] = Shimmer3Configuration.SignalNames.GYROSCOPE_Y;
                    signalDataTypeArray[i + 1] = "i16r";
                    i++;

                    signalNameArray[i + 1] = Shimmer3Configuration.SignalNames.GYROSCOPE_Z;
                    signalDataTypeArray[i + 1] = "i16r";
                    i++;
                }

                // ACCEL_WR
                if ((EnabledSensors & SDLogHeader.ACCEL_WR) > 0)
                {
                    NumberofChannels += 3;
                    PacketSize += 6;

                    signalNameArray[i + 1] = Shimmer3Configuration.SignalNames.WIDE_RANGE_ACCELEROMETER_X;
                    signalDataTypeArray[i + 1] = "i16";
                    i++;

                    signalNameArray[i + 1] = Shimmer3Configuration.SignalNames.WIDE_RANGE_ACCELEROMETER_Y;
                    signalDataTypeArray[i + 1] = "i16";
                    i++;

                    signalNameArray[i + 1] = Shimmer3Configuration.SignalNames.WIDE_RANGE_ACCELEROMETER_Z;
                    signalDataTypeArray[i + 1] = "i16";
                    i++;
                }

                // MAG
                if ((EnabledSensors & SDLogHeader.MAG) > 0)
                {
                    NumberofChannels += 3;
                    PacketSize += 6;

                    if (!isShimmer3withUpdatedSensors())
                    {
                        signalNameArray[i + 1] = Shimmer3Configuration.SignalNames.MAGNETOMETER_X;
                        signalDataTypeArray[i + 1] = "i16*";
                        i++;

                        signalNameArray[i + 1] = Shimmer3Configuration.SignalNames.MAGNETOMETER_Y;
                        signalDataTypeArray[i + 1] = "i16*";
                        i++;

                        signalNameArray[i + 1] = Shimmer3Configuration.SignalNames.MAGNETOMETER_Z;
                        signalDataTypeArray[i + 1] = "i16*";
                        i++;
                    }
                    else
                    {
                        signalNameArray[i + 1] = Shimmer3Configuration.SignalNames.MAGNETOMETER_X;
                        signalDataTypeArray[i + 1] = "i16";
                        i++;

                        signalNameArray[i + 1] = Shimmer3Configuration.SignalNames.MAGNETOMETER_Y;
                        signalDataTypeArray[i + 1] = "i16";
                        i++;

                        signalNameArray[i + 1] = Shimmer3Configuration.SignalNames.MAGNETOMETER_Z;
                        signalDataTypeArray[i + 1] = "i16";
                        i++;
                    }
                }

                // BMP180/280 temperature
                if ((EnabledSensors & SDLogHeader.BMPX80) > 0)
                {
                    NumberofChannels += 1;
                    PacketSize += 2;

                    string signalName = Shimmer3Configuration.SignalNames.TEMPERATURE;
                    signalNameArray[i + 1] = signalName;
                    signalDataTypeArray[i + 1] = "u16r";
                    i++;
                }

                // BMP180/280 pressure
                if ((EnabledSensors & SDLogHeader.BMPX80) > 0)
                {
                    NumberofChannels += 1;
                    PacketSize += 3;

                    string signalName = Shimmer3Configuration.SignalNames.PRESSURE;
                    signalNameArray[i + 1] = signalName;
                    signalDataTypeArray[i + 1] = "u24r";
                    i++;
                }

                // EXG 24-bit / 16-bit blocks (same as Java)
                if ((EnabledSensors & SDLogHeader.EXG1_24BIT) > 0)
                {
                    NumberofChannels += 3;
                    PacketSize += 7;

                    signalNameArray[i + 1] = Shimmer3Configuration.SignalNames.EXG1_STATUS;
                    signalDataTypeArray[i + 1] = "u8";
                    i++;

                    signalNameArray[i + 1] = Shimmer3Configuration.SignalNames.EXG1_CH1;
                    signalDataTypeArray[i + 1] = "i24r";
                    i++;

                    signalNameArray[i + 1] = Shimmer3Configuration.SignalNames.EXG1_CH2;
                    signalDataTypeArray[i + 1] = "i24r";
                    i++;
                }

                if ((EnabledSensors & SDLogHeader.EXG2_24BIT) > 0)
                {
                    NumberofChannels += 3;
                    PacketSize += 7;

                    signalNameArray[i + 1] = Shimmer3Configuration.SignalNames.EXG2_STATUS;
                    signalDataTypeArray[i + 1] = "u8";
                    i++;

                    signalNameArray[i + 1] = Shimmer3Configuration.SignalNames.EXG2_CH1;
                    signalDataTypeArray[i + 1] = "i24r";
                    i++;

                    signalNameArray[i + 1] = Shimmer3Configuration.SignalNames.EXG2_CH2;
                    signalDataTypeArray[i + 1] = "i24r";
                    i++;
                }

                if ((EnabledSensors & SDLogHeader.EXG1_16BIT) > 0)
                {
                    NumberofChannels += 3;
                    PacketSize += 5;

                    signalNameArray[i + 1] = Shimmer3Configuration.SignalNames.EXG1_STATUS;
                    signalDataTypeArray[i + 1] = "u8";
                    i++;

                    signalNameArray[i + 1] = Shimmer3Configuration.SignalNames.EXG1_CH1_16BIT;
                    signalDataTypeArray[i + 1] = "i16r";
                    i++;

                    signalNameArray[i + 1] = Shimmer3Configuration.SignalNames.EXG1_CH2_16BIT;
                    signalDataTypeArray[i + 1] = "i16r";
                    i++;
                }

                if ((EnabledSensors & SDLogHeader.EXG2_16BIT) > 0)
                {
                    NumberofChannels += 3;
                    PacketSize += 5;

                    signalNameArray[i + 1] = Shimmer3Configuration.SignalNames.EXG2_STATUS;
                    signalDataTypeArray[i + 1] = "u8";
                    i++;

                    signalNameArray[i + 1] = Shimmer3Configuration.SignalNames.EXG2_CH1_16BIT;
                    signalDataTypeArray[i + 1] = "i16r";
                    i++;

                    signalNameArray[i + 1] = Shimmer3Configuration.SignalNames.EXG2_CH2_16BIT;
                    signalDataTypeArray[i + 1] = "i16r";
                    i++;
                }

                if ((EnabledSensors & SDLogHeader.ECG_TO_HR_FW) > 0)
                {
                    NumberofChannels += 1;
                    PacketSize += 1;

                    signalNameArray[i + 1] = "Heart Rate_FW";
                    signalDataTypeArray[i + 1] = "u8";
                    i++;
                }
            }

            SignalNameArray = signalNameArray;
            SignalDataTypeArray = signalDataTypeArray;

        }


        protected override bool IsConnectionOpen()
        {
            throw new NotImplementedException();
        }

        protected override void OpenConnection()
        {
            throw new NotImplementedException();
        }

        protected override void CloseConnection()
        {
            throw new NotImplementedException();
        }

        protected override void FlushConnection()
        {
            throw new NotImplementedException();
        }

        protected override void FlushInputConnection()
        {
            throw new NotImplementedException();
        }

        protected override void WriteBytes(byte[] b, int index, int length)
        {
            //This logic is not required, but is called due to the battery check part of buildmsg
        }

        protected override int ReadByte()
        {
            throw new NotImplementedException();
        }

        public override string GetShimmerAddress()
        {
            return MacAddress;
        }

        public override void SetShimmerAddress(string address)
        {
            MacAddress = address;
        }
    }
}
