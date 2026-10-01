using System;
using System.IO;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using ShimmerAPI;
using ShimmerAPI.Utilities;

namespace ShimmerBluetoothTests
{
    [TestClass]
    public class ShimmerTest : ShimmerBluetooth
    {

        String comPort = "COM29";
        String deviceName = "testName";
        ShimmerLogAndStreamSystemSerialPort shimmerDevice;

        public ShimmerTest()
        {

        }

        [TestMethod]
        public void TestCRCTrue()
        {
            byte[] testPacket = new byte[] {
                (byte) 0x00,
                (byte) 0xb6, (byte) 0xf8, (byte) 0xbb,
                (byte) 0xff, (byte) 0x80, (byte) 0x00, (byte) 0x01, (byte) 0x80, (byte) 0x00, (byte) 0x01,
                (byte) 0xff, (byte) 0x80, (byte) 0x00, (byte) 0x01, (byte) 0x80, (byte) 0x00, (byte) 0x01,
                (byte) 0x8a, (byte) 0x93
            };
            byte[] crc = ShimmerUartCrcCalc(testPacket, testPacket.Length - 2);
            Assert.IsTrue(ShimmerBluetooth.ShimmerUartCrcCheck(testPacket));

            testPacket = new byte[] {
                (byte) 0x00,
                (byte) 0x00, (byte) 0x00, (byte) 0x00,
                (byte) 0x00, (byte) 0x00, (byte) 0x00, (byte) 0x00, (byte) 0x00, (byte) 0x00, (byte) 0x00,
                (byte) 0x00, (byte) 0x00, (byte) 0x00, (byte) 0x00, (byte) 0x00, (byte) 0x00, (byte) 0x00,
                 231,  206
            };
            crc = ShimmerUartCrcCalc(testPacket, testPacket.Length - 2);
            Assert.IsTrue(ShimmerBluetooth.ShimmerUartCrcCheck(testPacket));
        }

        [TestMethod]
        public void TestCRCFalse()
        {
            byte[] testPacket = new byte[] {
                (byte) 0x00,
                (byte) 0xb6, (byte) 0xf8, (byte) 0xbb,
                (byte) 0xff, (byte) 0x80, (byte) 0x00, (byte) 0x01, (byte) 0x80, (byte) 0x00, (byte) 0x01,
                (byte) 0xff, (byte) 0x80, (byte) 0x00, (byte) 0x01, (byte) 0x80, (byte) 0x00, (byte) 0x01,
                (byte) 0x8a, (byte) 0x94
            };
            Assert.IsFalse(ShimmerBluetooth.ShimmerUartCrcCheck(testPacket));
        }

        [TestMethod]
        public void TestMethodDeviceName()
        {
            shimmerDevice = new ShimmerLogAndStreamSystemSerialPort(deviceName, comPort);
            Assert.AreEqual(deviceName, shimmerDevice.GetDeviceName());
        }

        [TestMethod]
        public void CopyAndRemoveBytes_ShouldCopyCorrectNumberOfBytesAndRemoveFromSourceArray()
        {
            // Arrange
            byte[] sourceArray = { 0x01, 0x02, 0x03, 0x04, 0x05, 0x06, 0x07, 0x08 };
            byte[] expectedCopiedArray = { 0x01, 0x02, 0x03, 0x04 };
            byte[] expectedSourceArray = { 0x05, 0x06, 0x07, 0x08 };

            int bytesToCopy = 4;

            // Act
            byte[] copiedArray = ProgrammerUtilities.CopyAndRemoveBytes(ref sourceArray, bytesToCopy);

            // Assert
            CollectionAssert.AreEqual(expectedCopiedArray, copiedArray, "Copied array does not match expected.");
            CollectionAssert.AreEqual(expectedSourceArray, sourceArray, "Source array after removal does not match expected.");
        }

        [TestMethod]
        public void AppendByteArrays_SuccessfullyAppendsArrays()
        {
            // Arrange
            byte[] array1 = new byte[] { 0x01, 0x02, 0x03 };
            byte[] array2 = new byte[] { 0x04, 0x05, 0x06 };
            byte[] expectedArray = new byte[] { 0x01, 0x02, 0x03, 0x04, 0x05, 0x06 };

            // Act
            byte[] combinedArray = ProgrammerUtilities.AppendByteArrays(array1, array2);

            // Assert
            CollectionAssert.AreEqual(expectedArray, combinedArray, "Arrays should be equal after appending.");
        }
        [TestMethod]
        public void AppendByteArrays_WithEmptyArray1_ReturnsArray2()
        {
            // Arrange
            byte[] array1 = new byte[0];
            byte[] array2 = new byte[] { 0x01, 0x02, 0x03 };

            // Act
            byte[] combinedArray = ProgrammerUtilities.AppendByteArrays(array1, array2);

            // Assert
            CollectionAssert.AreEqual(array2, combinedArray, "Combined array should be equal to array2.");
        }

        [TestMethod]
        public void AppendByteArrays_WithEmptyArray2_ReturnsArray1()
        {
            // Arrange
            byte[] array1 = new byte[] { 0x01, 0x02, 0x03 };
            byte[] array2 = new byte[0];

            // Act
            byte[] combinedArray = ProgrammerUtilities.AppendByteArrays(array1, array2);

            // Assert
            CollectionAssert.AreEqual(array1, combinedArray, "Combined array should be equal to array1.");
        }

        [TestMethod]
        public void AppendByteArrays_WithBothEmptyArrays_ReturnsEmptyArray()
        {
            // Arrange
            byte[] array1 = new byte[0];
            byte[] array2 = new byte[0];

            // Act
            byte[] combinedArray = ProgrammerUtilities.AppendByteArrays(array1, array2);

            // Assert
            CollectionAssert.AreEqual(array1, combinedArray, "Combined array should be an empty array.");
        }

        [TestMethod]
        public void RemoveLastBytes_RemovesCorrectNumberOfBytes()
        {
            // Arrange
            byte[] originalArray = new byte[] { 0x01, 0x02, 0x03, 0x04, 0x05 };
            int numberOfBytesToRemove = 2;
            byte[] expectedArray = new byte[] { 0x01, 0x02, 0x03 };

            // Act
            byte[] modifiedArray = ProgrammerUtilities.RemoveLastBytes(originalArray, numberOfBytesToRemove);

            // Assert
            CollectionAssert.AreEqual(expectedArray, modifiedArray, "Arrays should be equal after removing bytes.");
        }

        [TestMethod]
        public void RemoveLastBytes_RemovesAllBytes()
        {
            // Arrange
            byte[] originalArray = new byte[] { 0x01, 0x02, 0x03, 0x04, 0x05 };
            int numberOfBytesToRemove = 5;

            // Act
            byte[] modifiedArray = ProgrammerUtilities.RemoveLastBytes(originalArray, numberOfBytesToRemove);
            byte[] testExpectation = new byte[0];
            // Assert
            Assert.AreEqual(modifiedArray.Length, 0, "Modified array should be empty after removing all bytes.");
        }

        [TestMethod]
        public void RemoveLastBytes_TriesToRemoveMoreBytesThanArrayLength()
        {
            // Arrange
            byte[] originalArray = new byte[] { 0x01, 0x02, 0x03, 0x04, 0x05 };
            int numberOfBytesToRemove = 6;

            // Act
            byte[] modifiedArray = ProgrammerUtilities.RemoveLastBytes(originalArray, numberOfBytesToRemove);

            // Assert
            Assert.AreEqual(modifiedArray,null, "Modified array should be empty when trying to remove more bytes than the array length.");
        }

        [TestMethod]
        public void TestNudgeDouble()
        {
            Assert.AreEqual(UtilCalibration.NudgeDouble(1000, 0, 500), 500);
            System.Console.WriteLine(UtilCalibration.NudgeDouble(1000, 0, 500));
            Assert.AreEqual(UtilCalibration.NudgeDouble(-10, 10, 500), 10);
            System.Console.WriteLine(UtilCalibration.NudgeDouble(-10, 10, 500));
            Assert.AreEqual(UtilCalibration.NudgeDouble(50, 10, 500), 50);
            System.Console.WriteLine(UtilCalibration.NudgeDouble(50, 10, 500));
        }

        [TestMethod]
        public void TestNudgeGsrResistance()
        {
            /*
              {8.0, 63.0}, 		//Range 0
			{63.0, 220.0}, 		//Range 1
			{220.0, 680.0}, 	//Range 2
			{680.0, 4700.0}}; 	//Range 3
            */
            Assert.AreEqual(NudgeGsrResistance(7,0),8);
            Assert.AreEqual(NudgeGsrResistance(70, 0),63);
            Assert.AreEqual(NudgeGsrResistance(59, 1), 63);
            Assert.AreEqual(NudgeGsrResistance(90, 1), 90);
            Assert.AreEqual(NudgeGsrResistance(230, 1), 220);
            Assert.AreEqual(NudgeGsrResistance(230, 2), 230);
            Assert.AreEqual(NudgeGsrResistance(200, 2), 220);
            Assert.AreEqual(NudgeGsrResistance(690, 3), 690);
            Assert.AreEqual(NudgeGsrResistance(5000, 3), 4700);
        }

        [TestMethod]
        public void RemoveBytesFromArray_RemovesCorrectBytes()
        {
            // Arrange
            byte[] originalBytes = { 0x01, 0x02, 0x03, 0x04, 0x05 };
            int bytesToRemove = 2;

            // Act
            byte[] modifiedBytes = ProgrammerUtilities.RemoveBytesFromArray(originalBytes, bytesToRemove);

            // Assert
            byte[] expectedBytes = { 0x03, 0x04, 0x05 };
            Assert.AreEqual(expectedBytes.Length, modifiedBytes.Length, "Lengths should match after removing bytes");

            for (int i = 0; i < expectedBytes.Length; i++)
            {
                Assert.AreEqual(expectedBytes[i], modifiedBytes[i], $"Byte at index {i} does not match");
            }
        }

        [TestMethod]
        public void TestBMP390_PressureTemperature()
        {
            byte[] pressureResoResTest = { 0xE7, 0x6B, 0xF0, 0x4A, 0xF9, 0xAB, 0x1C, 0x9B, 0x15, 0x06, 0x01, 0xD2, 0x49, 0x18, 0x5F, 0x03, 0xFA, 0x3A, 0x0F, 0x07, 0xF5 };

            CalculateBMP390PressureCalibrationCoefficientsResponse(pressureResoResTest);

            Assert.AreEqual(Bmp3QuantizedCalibData_ParT1, 7071488);
            Assert.AreEqual(Bmp3QuantizedCalibData_ParT2, 0.00001786649227142334);
            Assert.AreEqual(Bmp3QuantizedCalibData_ParT3, -0.000000000000024868995751603507);
            Assert.AreEqual(Bmp3QuantizedCalibData_ParP1, -0.0086259841918945312);
            Assert.AreEqual(Bmp3QuantizedCalibData_ParP2, -0.000020215287804603577);
            Assert.AreEqual(Bmp3QuantizedCalibData_ParP3, 0.0000000013969838619232178);
            Assert.AreEqual(Bmp3QuantizedCalibData_ParP4, 0.0000000000072759576141834259);
            Assert.AreEqual(Bmp3QuantizedCalibData_ParP5, 151184);
            Assert.AreEqual(Bmp3QuantizedCalibData_ParP6, 380.375);
            Assert.AreEqual(Bmp3QuantizedCalibData_ParP7, 0.01171875);
            Assert.AreEqual(Bmp3QuantizedCalibData_ParP8, -0.00018310546875);
            Assert.AreEqual(Bmp3QuantizedCalibData_ParP9, 0.000000000013848477919964353);
            Assert.AreEqual(Bmp3QuantizedCalibData_ParP10, 0.000000000000024868995751603507);
            Assert.AreEqual(Bmp3QuantizedCalibData_ParP11, -0.00000000000000000029815559743351372);

            Bmp3QuantizedCalibData_TLin = 23.17016986780799;
            
            //byte[] sensorData = { 0x00, 0x0D, 0x64, 0x00, 0xBA, 0x7F };
            byte[] sensorDataP = { 0x00, 0x0D, 0x64};
            byte[] sensorDataT = { 0x00, 0xBA, 0x7F };

            string[] sensorDataType = { "u24"};
            long[] uncalibResultP = ProgrammerUtilities.ParseData(sensorDataP, sensorDataType); 
            long[] uncalibResultT = ProgrammerUtilities.ParseData(sensorDataT, sensorDataType);

            Assert.AreEqual(uncalibResultP[0], 6556928);
            Assert.AreEqual(uncalibResultT[0], 8370688);
            double[] bmpX80caldata = new double[2];
            bmpX80caldata = CalibratePressure390SensorData(uncalibResultP[0], uncalibResultT[0]);
            Bmp3QuantizedCalibData_TLin = bmpX80caldata[1];

            //Assert.AreEqual(resultP, 100911.8245324826);
            //Assert.AreEqual(resultT, 23.170169867807999);
            Assert.AreEqual(Math.Round(bmpX80caldata[0], 4), 100911.8245);
            Assert.AreEqual(Math.Round(bmpX80caldata[1], 4), 23.1702);

            //byte[] sensorData2 = { 0x00, 0x17, 0x64, 0x00, 0xCF, 0x7F };
            byte[] sensorDataP2 = { 0x00, 0x17, 0x64 };
            byte[] sensorDataT2 = { 0x00, 0xCF, 0x7F };

            long[] uncalibResultP2 = ProgrammerUtilities.ParseData(sensorDataP2, sensorDataType);
            long[] uncalibResultT2 = ProgrammerUtilities.ParseData(sensorDataT2, sensorDataType);
            double[] bmpX80caldata2 = new double[2];
            bmpX80caldata2 = CalibratePressure390SensorData(uncalibResultP2[0], uncalibResultT2[0]);
            Bmp3QuantizedCalibData_TLin = bmpX80caldata2[1];

            //Assert.AreEqual(resultP2, 100912.81758676282);
            //Assert.AreEqual(resultT2, 23.26587201654911);
            Assert.AreEqual(Math.Round(bmpX80caldata2[0], 4), 100912.8176);
            Assert.AreEqual(Math.Round(bmpX80caldata2[1], 4), 23.2659);
        }

        [TestMethod]
        public void TestBMP581_PressureTemperature()
        {
            // Pressure: unsigned 24-bit / 64 = Pa, reported in kPa by the caller (/1000)
            Assert.AreEqual(100.0, CalibratePressure581SensorData(6400000, 0)[0] / 1000, 1e-12);
            Assert.AreEqual(262.143984375, CalibratePressure581SensorData(0xFFFFFF, 0)[0] / 1000, 1e-12);

            // Temperature: signed 24-bit / 65536 = degC
            Assert.AreEqual(25.0, CalibratePressure581SensorData(0, 1638400)[1], 1e-12);
            Assert.AreEqual(24.4140625, CalibratePressure581SensorData(0, 1600000)[1], 1e-12);
            Assert.AreEqual(127.9999847, CalibratePressure581SensorData(0, 0x7FFFFF)[1], 1e-7);
            Assert.AreEqual(-1.0 / 65536, CalibratePressure581SensorData(0, 0xFFFFFF)[1], 1e-12);
            Assert.AreEqual(-128.0, CalibratePressure581SensorData(0, 0x800000)[1], 1e-12);

            // DEV-1102 regression: a sub-zero temperature must not decode as ~254 degC. Wire bytes 56 55 FE = 0xFE5556
            string[] sensorDataType = { "u24" };
            long[] uncalibResultT = ProgrammerUtilities.ParseData(new byte[] { 0x56, 0x55, 0xFE }, sensorDataType);
            Assert.AreEqual(0xFE5556, uncalibResultT[0]);
            Assert.AreEqual(-1.6666565, CalibratePressure581SensorData(0, uncalibResultT[0])[1], 1e-7);

            // SignExtend24 masks to 24 bits before extending, so an already signed value passes through unchanged
            Assert.AreEqual(-109226.0, SignExtend24(0xFE5556));
            Assert.AreEqual(-109226.0, SignExtend24(-109226));
            Assert.AreEqual(0x7FFFFF, SignExtend24(0x7FFFFF));

            // Frame 00 A8 61 00 00 19 with channel order [0x1B pressure, 0x1A temperature]
            long[] uncalibFrame = ProgrammerUtilities.ParseData(new byte[] { 0x00, 0xA8, 0x61, 0x00, 0x00, 0x19 }, new string[] { "u24", "u24" });
            double[] bmp581caldata = CalibratePressure581SensorData(uncalibFrame[0], uncalibFrame[1]);
            Assert.AreEqual(100.0, bmp581caldata[0] / 1000, 1e-12);
            Assert.AreEqual(25.0, bmp581caldata[1], 1e-12);
        }

        [TestMethod]
        public void TestBMP581_PressureCalibrationCoefficientsResponse()
        {
            // [A6 01 03] = BMP581, no calibration bytes
            Assert.AreEqual(PRESSURE_SENSOR_UNKNOWN, PressureSensorId);
            Assert.IsTrue(InterpretPressureCalibrationCoefficientsResponse(new byte[] { 0x01, 0x03 }));
            Assert.AreEqual(PRESSURE_SENSOR_BMP581, PressureSensorId);

            // A length that does not match the sensor ID is rejected and the ID is left unchanged
            PressureSensorId = PRESSURE_SENSOR_UNKNOWN;
            Assert.IsFalse(InterpretPressureCalibrationCoefficientsResponse(new byte[] { 0x04, 0x03, 0x11, 0x22, 0x33 })); // [A6 04 03 xx xx xx]
            Assert.IsFalse(InterpretPressureCalibrationCoefficientsResponse(new byte[] { 0x01, 0x02 })); // BMP390 without its 21 bytes
            Assert.IsFalse(InterpretPressureCalibrationCoefficientsResponse(new byte[] { 0x01, 0x04 })); // unknown sensor ID
            Assert.IsFalse(InterpretPressureCalibrationCoefficientsResponse(new byte[] { 0x05, 0x03 })); // length byte disagrees with payload
            Assert.IsFalse(InterpretPressureCalibrationCoefficientsResponse(new byte[] { 0x00 }));
            Assert.AreEqual(PRESSURE_SENSOR_UNKNOWN, PressureSensorId);

            // [A6 16 02 <21 bytes>] = BMP390, decoded as before
            byte[] bmp390Response = { 0x16, 0x02, 0xE7, 0x6B, 0xF0, 0x4A, 0xF9, 0xAB, 0x1C, 0x9B, 0x15, 0x06, 0x01, 0xD2, 0x49, 0x18, 0x5F, 0x03, 0xFA, 0x3A, 0x0F, 0x07, 0xF5 };
            Assert.IsTrue(InterpretPressureCalibrationCoefficientsResponse(bmp390Response));
            Assert.AreEqual(PRESSURE_SENSOR_BMP390, PressureSensorId);
            Assert.AreEqual(7071488, Bmp3QuantizedCalibData_ParT1);
            Assert.AreEqual(151184, Bmp3QuantizedCalibData_ParP5);

            // The in-band sensor ID overrides the SR number rule
            SetBmp581TestBoard((int)ShimmerVersion.SHIMMER3R, 1, 1, 6, (int)ExpansionBoardDetectShimmer3.EXP_BRD_GSR_UNIFIED, 8, 2);
            Assert.IsTrue(isShimmer3RwithBmp581());
            Assert.IsFalse(isBmp581InUse());
            SetBmp581TestBoard((int)ShimmerVersion.SHIMMER3R, 1, 1, 6, (int)ExpansionBoardDetectShimmer3.EXP_BRD_GSR_UNIFIED, 8, 1);
            Assert.IsTrue(InterpretPressureCalibrationCoefficientsResponse(new byte[] { 0x01, 0x03 }));
            Assert.IsFalse(isShimmer3RwithBmp581());
            Assert.IsTrue(isBmp581InUse());

            // With no sensor ID received, the SR number rule decides
            PressureSensorId = PRESSURE_SENSOR_UNKNOWN;
            Assert.IsFalse(isBmp581InUse());
            SetBmp581TestBoard((int)ShimmerVersion.SHIMMER3R, 1, 1, 6, (int)ExpansionBoardDetectShimmer3.EXPANSION_PROTO3_DELUXE, 4, 2);
            Assert.IsTrue(isBmp581InUse());
        }

        [TestMethod]
        public void TestBMP581_ReadPressureCalibrationCoefficientsFallback()
        {
            // SR38-4-2 is a BMP581 board per the SR number rule; SR48-8-1 is a BMP390 board
            // v1.01.006 NACKs 0xA7 on a BMP581 (no reply as far as the read thread is concerned): fall back to the SR number rule
            ShimmerPressureTestDevice device = new ShimmerPressureTestDevice((int)ExpansionBoardDetectShimmer3.EXPANSION_PROTO3_DELUXE, 4, 2, null);
            device.ReadPressureCalibrationCoefficients();
            Assert.AreEqual(1, device.NumPressureCalibrationRequests);
            Assert.AreEqual(PRESSURE_SENSOR_BMP581, device.PressureSensorId);

            device = new ShimmerPressureTestDevice((int)ExpansionBoardDetectShimmer3.EXP_BRD_GSR_UNIFIED, 8, 1, null);
            device.ReadPressureCalibrationCoefficients();
            Assert.AreEqual(PRESSURE_SENSOR_BMP390, device.PressureSensorId);

            // An in-band reply is used as is, even where the SR number rule disagrees
            device = new ShimmerPressureTestDevice((int)ExpansionBoardDetectShimmer3.EXP_BRD_GSR_UNIFIED, 8, 1, new byte[] { 0x01, 0x03 });
            device.ReadPressureCalibrationCoefficients();
            Assert.AreEqual(PRESSURE_SENSOR_BMP581, device.PressureSensorId);
        }

        [TestMethod]
        public void TestBMP581_SrNumberRule()
        {
            // Cases copied from log-and-stream-common Test/host/test_boards.c test_bmp581_gate()
            int[,] cases = {
                // srId, rev, revSpecial, expected
                /* IMU, SR31: from 11.2 */
                { (int)ExpansionBoardDetectShimmer3.SHIMMER3, 11, 1, 0 }, // one minor below the line
                { (int)ExpansionBoardDetectShimmer3.SHIMMER3, 11, 2, 1 }, // the first board with it
                { (int)ExpansionBoardDetectShimmer3.SHIMMER3, 11, 3, 1 }, // a later minor keeps it
                { (int)ExpansionBoardDetectShimmer3.SHIMMER3, 12, 0, 1 }, // a later major keeps it
                { (int)ExpansionBoardDetectShimmer3.SHIMMER3, 10, 9, 0 }, // an earlier major never has it
                /* Proto3 Deluxe, SR38: from 4.2 */
                { (int)ExpansionBoardDetectShimmer3.EXPANSION_PROTO3_DELUXE, 4, 1, 0 },
                { (int)ExpansionBoardDetectShimmer3.EXPANSION_PROTO3_DELUXE, 4, 2, 1 },
                { (int)ExpansionBoardDetectShimmer3.EXPANSION_PROTO3_DELUXE, 5, 0, 1 },
                /* ExG, SR47: from 8.2 - 7.x never has it, unlike SR48 */
                { (int)ExpansionBoardDetectShimmer3.EXP_BRD_EXG_UNIFIED, 7, 2, 0 },
                { (int)ExpansionBoardDetectShimmer3.EXP_BRD_EXG_UNIFIED, 8, 1, 0 },
                { (int)ExpansionBoardDetectShimmer3.EXP_BRD_EXG_UNIFIED, 8, 2, 1 },
                { (int)ExpansionBoardDetectShimmer3.EXP_BRD_EXG_UNIFIED, 9, 0, 1 },
                /* Bridge Amplifier, SR49: from 4.2 */
                { (int)ExpansionBoardDetectShimmer3.EXP_BRD_BR_AMP_UNIFIED, 4, 1, 0 },
                { (int)ExpansionBoardDetectShimmer3.EXP_BRD_BR_AMP_UNIFIED, 4, 2, 1 },
                /* GSR+, SR48: the two-window case */
                { (int)ExpansionBoardDetectShimmer3.EXP_BRD_GSR_UNIFIED, 6, 0, 0 },
                { (int)ExpansionBoardDetectShimmer3.EXP_BRD_GSR_UNIFIED, 7, 0, 0 },
                { (int)ExpansionBoardDetectShimmer3.EXP_BRD_GSR_UNIFIED, 7, 1, 0 },
                { (int)ExpansionBoardDetectShimmer3.EXP_BRD_GSR_UNIFIED, 7, 2, 1 },
                { (int)ExpansionBoardDetectShimmer3.EXP_BRD_GSR_UNIFIED, 7, 3, 1 },
                { (int)ExpansionBoardDetectShimmer3.EXP_BRD_GSR_UNIFIED, 8, 0, 0 }, // later board, but back to the BMP390
                { (int)ExpansionBoardDetectShimmer3.EXP_BRD_GSR_UNIFIED, 8, 1, 0 }, // still the BMP390
                { (int)ExpansionBoardDetectShimmer3.EXP_BRD_GSR_UNIFIED, 8, 2, 1 },
                { (int)ExpansionBoardDetectShimmer3.EXP_BRD_GSR_UNIFIED, 8, 3, 1 },
                { (int)ExpansionBoardDetectShimmer3.EXP_BRD_GSR_UNIFIED, 9, 0, 1 },
                /* A board ID with no BMP581 rule at all */
                { (int)ExpansionBoardDetectShimmer3.EXPANSION_PROTO3_MINI, 9, 9, 0 },
                /* An unprogrammed card */
                { 0xFF, 0xFF, 0xFF, 0 },
            };

            for (int i = 0; i < cases.GetLength(0); i++)
            {
                SetBmp581TestBoard((int)ShimmerVersion.SHIMMER3R, 1, 1, 6, cases[i, 0], cases[i, 1], cases[i, 2]);
                Assert.AreEqual(cases[i, 3] == 1, isShimmer3RwithBmp581(), "SR" + cases[i, 0] + "-" + cases[i, 1] + "-" + cases[i, 2]);
            }

            // The rule is Shimmer3R only: a daughter card can be moved onto a Shimmer3 host
            SetBmp581TestBoard((int)ShimmerVersion.SHIMMER3, 1, 1, 6, (int)ExpansionBoardDetectShimmer3.EXP_BRD_GSR_UNIFIED, 8, 2);
            Assert.IsFalse(isShimmer3RwithBmp581());
            Assert.IsFalse(isBmp581InUse());

            // BMP581 support starts at LogAndStream v1.01.006
            SetBmp581TestBoard((int)ShimmerVersion.SHIMMER3R, 1, 1, 5, (int)ExpansionBoardDetectShimmer3.EXPANSION_PROTO3_DELUXE, 4, 2);
            Assert.IsFalse(isShimmer3RwithBmp581());
            SetBmp581TestBoard((int)ShimmerVersion.SHIMMER3R, 1, 2, 0, (int)ExpansionBoardDetectShimmer3.EXPANSION_PROTO3_DELUXE, 4, 2);
            Assert.IsTrue(isShimmer3RwithBmp581());
            SetBmp581TestBoard((int)ShimmerVersion.SHIMMER3R, 1, 1, 6, (int)ExpansionBoardDetectShimmer3.EXPANSION_PROTO3_DELUXE, 4, 2);
            FirmwareIdentifier = FW_IDENTIFIER_BTSTREAM;
            Assert.IsFalse(isShimmer3RwithBmp581());
        }

        [TestMethod]
        public void TestBMP581_SdLog()
        {
            // SR38-4-2 on LogAndStream v1.01.006: BMP581, no calibration coefficients in the header
            byte[] packet = { 0x00, 0x00, 0x00, 0x00, 0xA8, 0x61, 0x00, 0x00, 0x19 }; // timestamp, pressure (0x1B), temperature (0x1A)
            string filePath = WriteShimmer3RSdLogTestFile((int)ExpansionBoardDetectShimmer3.EXPANSION_PROTO3_DELUXE, 4, 2, null, packet);
            try
            {
                AssertBmp581SdLog(filePath);
            }
            finally
            {
                DeleteSdLogTestFile(filePath);
            }

            // SR48-8-1 on the same firmware: BMP390, coefficients decoded from the header as before
            byte[] bmp390Calib = { 0xE7, 0x6B, 0xF0, 0x4A, 0xF9, 0xAB, 0x1C, 0x9B, 0x15, 0x06, 0x01, 0xD2, 0x49, 0x18, 0x5F, 0x03, 0xFA, 0x3A, 0x0F, 0x07, 0xF5 };
            filePath = WriteShimmer3RSdLogTestFile((int)ExpansionBoardDetectShimmer3.EXP_BRD_GSR_UNIFIED, 8, 1, bmp390Calib, packet);
            try
            {
                AssertBmp390SdLog(filePath);
            }
            finally
            {
                DeleteSdLogTestFile(filePath);
            }
        }

        // The ShimmerSDLog is created in a separate method so it is unreachable, and its file can be released, once the method returns
        private static void AssertBmp581SdLog(string filePath)
        {
            ShimmerSDLog sdLog = new ShimmerSDLog(filePath);
            Assert.AreEqual(PRESSURE_SENSOR_BMP581, sdLog.PressureSensorId);
            Assert.IsTrue(sdLog.isBmp581InUse());

            ObjectCluster ojc = sdLog.ReadPacketMsg();
            Assert.AreEqual(100.0, ojc.GetData(Shimmer3Configuration.SignalNames.PRESSURE, ShimmerConfiguration.SignalFormats.CAL).Data, 1e-12);
            Assert.AreEqual(25.0, ojc.GetData(Shimmer3Configuration.SignalNames.TEMPERATURE, ShimmerConfiguration.SignalFormats.CAL).Data, 1e-12);
        }

        private static void AssertBmp390SdLog(string filePath)
        {
            ShimmerSDLog sdLog = new ShimmerSDLog(filePath);
            Assert.AreEqual(PRESSURE_SENSOR_BMP390, sdLog.PressureSensorId);
            Assert.IsFalse(sdLog.isBmp581InUse());
            Assert.AreEqual(7071488, sdLog.Bmp3QuantizedCalibData_ParT1);
        }

        // DEV-1123: the pressure sensor ID at SD header offset 224
        private static readonly byte[] Bmp390SdHeaderCalib = { 0xE7, 0x6B, 0xF0, 0x4A, 0xF9, 0xAB, 0x1C, 0x9B, 0x15, 0x06, 0x01, 0xD2, 0x49, 0x18, 0x5F, 0x03, 0xFA, 0x3A, 0x0F, 0x07, 0xF5 };
        // BST-BMP180-DS000 example coefficients (big-endian): AC1 408, AC2 -72, AC3 -14383, AC4 32741, AC5 32757, AC6 23153, B1 6190, B2 4, MB -32768, MC -8711, MD 2868
        private static readonly byte[] Bmp180SdHeaderCalib = { 0x01, 0x98, 0xFF, 0xB8, 0xC7, 0xD1, 0x7F, 0xE5, 0x7F, 0xF5, 0x5A, 0x71, 0x18, 0x2E, 0x00, 0x04, 0x80, 0x00, 0xDD, 0xF9, 0x0B, 0x34 };
        // BST-BMP280-DS001 example coefficients (little-endian): T1 27504, T2 26435, T3 -1000, P1 36477, P2 -10685, P3 3024, P4 2855, P5 140, P6 -7, P7 15500, P8 -14600, P9 6000
        private static readonly byte[] Bmp280SdHeaderCalib = { 0x70, 0x6B, 0x43, 0x67, 0x18, 0xFC, 0x7D, 0x8E, 0x43, 0xD6, 0xD0, 0x0B, 0x27, 0x0B, 0x8C, 0x00, 0xF9, 0xFF, 0x8C, 0x3C, 0xF8, 0xC6, 0x70, 0x17 };

        [TestMethod]
        public void TestSdHeaderPressureSensorId_Shimmer3R()
        {
            const int SR38 = (int)ExpansionBoardDetectShimmer3.EXPANSION_PROTO3_DELUXE; // SR38-4-2: BMP581 per the SR number rule
            const int SR48 = (int)ExpansionBoardDetectShimmer3.EXP_BRD_GSR_UNIFIED; // SR48-8-1: BMP390 per the SR number rule
            byte[] packet = { 0x00, 0x00, 0x00, 0x00, 0xA8, 0x61, 0x00, 0x00, 0x19 }; // timestamp, pressure (0x1B), temperature (0x1A)

            // v1.01.018 records the ID and it overrides the SR number rule: 0x03 on a BMP390 board is a BMP581
            AssertSdLog(WriteShimmer3RSdLogTestFile(SR48, 8, 1, Bmp390SdHeaderCalib, packet, 1, 1, 18, 0x03), sdLog =>
            {
                AssertBmp581SdLogCalibrated(sdLog);
                Assert.IsFalse(sdLog.PressureSensorInferred);
                Assert.AreEqual(0, sdLog.PressureSensorWarnings.Count);
            });

            // ... and 0x02 on a BMP581 board is a BMP390, with its coefficients decoded from the header
            AssertSdLog(WriteShimmer3RSdLogTestFile(SR38, 4, 2, Bmp390SdHeaderCalib, packet, 1, 1, 18, 0x02), sdLog =>
            {
                Assert.AreEqual(PRESSURE_SENSOR_BMP390, sdLog.PressureSensorId);
                Assert.IsFalse(sdLog.isBmp581InUse());
                Assert.AreEqual(7071488, sdLog.Bmp3QuantizedCalibData_ParT1);
                Assert.AreEqual(0, sdLog.PressureSensorWarnings.Count);
            });

            // Below v1.01.018 the byte is ignored and the SR number rule decides, including v1.01.006-v1.01.017, which pass the Shimmer3 gate
            foreach (int fwInternal in new int[] { 6, 17 })
            {
                AssertSdLog(WriteShimmer3RSdLogTestFile(SR48, 8, 1, Bmp390SdHeaderCalib, packet, 1, 1, fwInternal, 0x03), sdLog =>
                {
                    Assert.AreEqual(PRESSURE_SENSOR_BMP390, sdLog.PressureSensorId, "v1.01." + fwInternal);
                    Assert.AreEqual(7071488, sdLog.Bmp3QuantizedCalibData_ParT1);
                });
                AssertSdLog(WriteShimmer3RSdLogTestFile(SR38, 4, 2, null, packet, 1, 1, fwInternal, 0x02), sdLog =>
                {
                    AssertBmp581SdLogCalibrated(sdLog);
                });
            }

            // 0xFF (not recorded) on v1.01.018: the SR number rule decides
            AssertSdLog(WriteShimmer3RSdLogTestFile(SR38, 4, 2, null, packet, 1, 1, 18, 0xFF), sdLog =>
            {
                AssertBmp581SdLogCalibrated(sdLog);
                Assert.AreEqual(0, sdLog.PressureSensorWarnings.Count);
            });
            AssertSdLog(WriteShimmer3RSdLogTestFile(SR48, 8, 1, Bmp390SdHeaderCalib, packet, 1, 1, 18, 0xFF), sdLog =>
            {
                Assert.AreEqual(PRESSURE_SENSOR_BMP390, sdLog.PressureSensorId);
                Assert.AreEqual(7071488, sdLog.Bmp3QuantizedCalibData_ParT1);
            });

            // 0x83: a BMP581 inferred from the SR number, not confirmed by chip ID
            AssertSdLog(WriteShimmer3RSdLogTestFile(SR48, 8, 1, Bmp390SdHeaderCalib, packet, 1, 1, 18, 0x83), sdLog =>
            {
                AssertBmp581SdLogCalibrated(sdLog);
                Assert.IsTrue(sdLog.PressureSensorInferred);
                Assert.AreEqual(1, sdLog.PressureSensorWarnings.Count);
            });

            // An unrecognised ID: uncalibrated with a warning, never the SR number rule's BMP581 (SR38-4-2). 0x7E-0x7F are never allocated.
            foreach (byte sensorIdByte in new byte[] { 0x04, 0x7D, 0x7E, 0x7F })
            {
                AssertSdLog(WriteShimmer3RSdLogTestFile(SR38, 4, 2, null, packet, 1, 1, 18, sensorIdByte), sdLog =>
                {
                    Assert.AreEqual((int)sensorIdByte, sdLog.PressureSensorId);
                    Assert.IsFalse(sdLog.isBmp581InUse());
                    Assert.IsFalse(sdLog.PressureSensorInferred);
                    Assert.AreEqual(1, sdLog.PressureSensorWarnings.Count);
                    AssertPressureUncalibrated(sdLog.ReadPacketMsg(), 0x61A800, 0x190000);
                });
            }
            AssertSdLog(WriteShimmer3RSdLogTestFile(SR38, 4, 2, null, packet, 1, 1, 18, 0x84), sdLog =>
            {
                Assert.AreEqual(0x04, sdLog.PressureSensorId);
                Assert.IsTrue(sdLog.PressureSensorInferred);
                Assert.AreEqual(2, sdLog.PressureSensorWarnings.Count);
                AssertPressureUncalibrated(sdLog.ReadPacketMsg(), 0x61A800, 0x190000);
            });
        }

        [TestMethod]
        public void TestSdHeaderPressureSensorId_Shimmer3()
        {
            const int SR48 = (int)ExpansionBoardDetectShimmer3.EXP_BRD_GSR_UNIFIED; // SR48-3-0: BMP280 per the board rule, SR48-2-0: BMP180
            byte[] bmp180Packet = { 0x00, 0x00, 0x00, 0x6C, 0xFA, 0x5D, 0x23, 0x00 }; // timestamp, temperature UT 27898 (u16r), pressure UP 23843 << 8 (u24r)
            byte[] bmp280Packet = { 0x00, 0x00, 0x00, 0x7E, 0xED, 0x65, 0x5A, 0xC0 }; // timestamp, temperature 519888 >> 4 (u16r), pressure 415148 << 4 (u24r)

            // The datasheet examples, calibrated here with the same coefficients
            CalculateBMP180PressureCalibrationCoefficientsResponse(Bmp180SdHeaderCalib);
            double[] bmp180Expected = CalibratePressure180SensorData(23843, 27898);
            Assert.AreEqual(15.0, bmp180Expected[1], 0.1);
            Assert.AreEqual(69964, bmp180Expected[0], 5);
            CalculateBMP280PressureCalibrationCoefficientsResponse(Bmp280SdHeaderCalib);
            double[] bmp280Expected = CalibratePressure280SensorData(415148, 519888);
            Assert.AreEqual(25.08, bmp280Expected[1], 0.01);
            Assert.AreEqual(100653.27, bmp280Expected[0], 0.01);

            // 0x00 is a BMP180 on a board the board rule calls BMP280, on v1.01.006 and on v1.01.018 (the Shimmer3 gate, not the Shimmer3R one)
            foreach (int fwInternal in new int[] { 6, 18 })
            {
                AssertSdLog(WriteShimmer3SdLogTestFile(SR48, 3, 0, Bmp180SdHeaderCalib, bmp180Packet, 1, 1, fwInternal, 0x00), sdLog =>
                {
                    Assert.AreEqual(PRESSURE_SENSOR_BMP180, sdLog.PressureSensorId, "v1.01." + fwInternal);
                    Assert.AreEqual(0, sdLog.PressureSensorWarnings.Count);
                    AssertPressureCalibrated(sdLog.ReadPacketMsg(), bmp180Expected);
                });
            }

            // 0x01 is a BMP280 on a board the board rule calls BMP180, with the last two coefficient bytes read from offset 222
            AssertSdLog(WriteShimmer3SdLogTestFile(SR48, 2, 0, Bmp280SdHeaderCalib, bmp280Packet, 1, 1, 6, 0x01), sdLog =>
            {
                Assert.AreEqual(PRESSURE_SENSOR_BMP280, sdLog.PressureSensorId);
                Assert.AreEqual(6000, sdLog.dig_P9);
                AssertPressureCalibrated(sdLog.ReadPacketMsg(), bmp280Expected);
            });

            // 0xFE: no pressure sensor fitted, but pressure is enabled, so it is output uncalibrated with a warning
            AssertSdLog(WriteShimmer3SdLogTestFile(SR48, 3, 0, Bmp280SdHeaderCalib, bmp280Packet, 1, 1, 6, 0xFE), sdLog =>
            {
                Assert.AreEqual(PRESSURE_SENSOR_NONE, sdLog.PressureSensorId);
                Assert.IsFalse(sdLog.PressureSensorInferred);
                Assert.AreEqual(1, sdLog.PressureSensorWarnings.Count);
                AssertPressureUncalibrated(sdLog.ReadPacketMsg(), 0x655AC0, 0x7EED);
            });

            // 0xFF (not recorded), or firmware below v1.01.006: the board rule decides
            AssertSdLog(WriteShimmer3SdLogTestFile(SR48, 3, 0, Bmp280SdHeaderCalib, bmp280Packet, 1, 1, 6, 0xFF), sdLog =>
            {
                Assert.AreEqual(PRESSURE_SENSOR_BMP280, sdLog.PressureSensorId);
                AssertPressureCalibrated(sdLog.ReadPacketMsg(), bmp280Expected);
            });
            AssertSdLog(WriteShimmer3SdLogTestFile(SR48, 3, 0, Bmp280SdHeaderCalib, bmp280Packet, 1, 1, 5, 0x00), sdLog =>
            {
                Assert.AreEqual(PRESSURE_SENSOR_BMP280, sdLog.PressureSensorId);
                AssertPressureCalibrated(sdLog.ReadPacketMsg(), bmp280Expected);
            });
            AssertSdLog(WriteShimmer3SdLogTestFile(SR48, 2, 0, Bmp180SdHeaderCalib, bmp180Packet, 1, 1, 6, 0xFF), sdLog =>
            {
                Assert.AreEqual(PRESSURE_SENSOR_BMP180, sdLog.PressureSensorId);
                AssertPressureCalibrated(sdLog.ReadPacketMsg(), bmp180Expected);
            });
        }

        /// <summary>
        /// Parses the SD log, runs the assertions on it and deletes the file. The ShimmerSDLog is only referenced from the assertions,
        /// so it is unreachable, and its file can be released, once they return.
        /// </summary>
        private static void AssertSdLog(string filePath, Action<ShimmerSDLog> assertions)
        {
            try
            {
                assertions(new ShimmerSDLog(filePath));
            }
            finally
            {
                DeleteSdLogTestFile(filePath);
            }
        }

        private static void AssertBmp581SdLogCalibrated(ShimmerSDLog sdLog)
        {
            Assert.AreEqual(PRESSURE_SENSOR_BMP581, sdLog.PressureSensorId);
            Assert.IsTrue(sdLog.isBmp581InUse());
            AssertPressureCalibrated(sdLog.ReadPacketMsg(), new double[] { 100000.0, 25.0 });
        }

        private static void AssertPressureCalibrated(ObjectCluster ojc, double[] expected)
        {
            Assert.AreEqual(expected[0] / 1000, ojc.GetData(Shimmer3Configuration.SignalNames.PRESSURE, ShimmerConfiguration.SignalFormats.CAL).Data, 1e-9);
            Assert.AreEqual(expected[1], ojc.GetData(Shimmer3Configuration.SignalNames.TEMPERATURE, ShimmerConfiguration.SignalFormats.CAL).Data, 1e-9);
        }

        private static void AssertPressureUncalibrated(ObjectCluster ojc, double rawPressure, double rawTemperature)
        {
            Assert.AreEqual(rawPressure, ojc.GetData(Shimmer3Configuration.SignalNames.PRESSURE, ShimmerConfiguration.SignalFormats.RAW).Data);
            Assert.AreEqual(rawTemperature, ojc.GetData(Shimmer3Configuration.SignalNames.TEMPERATURE, ShimmerConfiguration.SignalFormats.RAW).Data);
            Assert.IsNull(ojc.GetData(Shimmer3Configuration.SignalNames.PRESSURE, ShimmerConfiguration.SignalFormats.CAL));
            Assert.IsNull(ojc.GetData(Shimmer3Configuration.SignalNames.TEMPERATURE, ShimmerConfiguration.SignalFormats.CAL));
        }

        /// <summary>
        /// ShimmerSDLog has no method to close its file, so let the finalizer release it before deleting
        /// </summary>
        private static void DeleteSdLogTestFile(string filePath)
        {
            GC.Collect();
            GC.WaitForPendingFinalizers();
            try
            {
                File.Delete(filePath);
            }
            catch (IOException)
            {
            }
        }

        /// <summary>
        /// Writes a minimal Shimmer3R LogAndStream SD log (384 byte header, v1.01.006 unless given) with pressure (0x1B) and temperature (0x1A) enabled.
        /// The pressure sensor ID at offset 224 defaults to 0xFF (not recorded), the value the firmware's 0xFF header fill leaves; a zero-filled
        /// header would otherwise read as 0x00, BMP180.
        /// </summary>
        private static string WriteShimmer3RSdLogTestFile(int srId, int rev, int revSpecial, byte[] pressureCalib, byte[] packet,
            int fwMajor = 1, int fwMinor = 1, int fwInternal = 6, byte pressureSensorIdByte = 0xFF)
        {
            byte[] header = new byte[384];
            header[0] = 0x80; // sampling rate 32768/128 = 256Hz
            header[31] = (byte)ShimmerVersion.SHIMMER3R;
            header[35] = (byte)FW_IDENTIFIER_LOGANDSTREAM;
            header[36] = (byte)(fwMajor >> 8);
            header[37] = (byte)fwMajor;
            header[38] = (byte)fwMinor;
            header[39] = (byte)fwInternal;
            header[214] = (byte)srId;
            header[215] = (byte)rev;
            header[216] = (byte)revSpecial;
            if (pressureCalib != null)
            {
                Array.Copy(pressureCalib, 0, header, 160, pressureCalib.Length);
            }
            header[224] = pressureSensorIdByte;
            header[314] = 2;
            header[315] = 0x1B;
            header[316] = 0x1A;

            return WriteSdLogTestFile(header, packet);
        }

        /// <summary>
        /// Writes a minimal Shimmer3 LogAndStream SD log (256 byte header) with the pressure sensor enabled: temperature (u16r) then pressure (u24r).
        /// A 24 byte (BMP280) calibration is split as the firmware writes it, the first 22 bytes at offset 160 and the last 2 at 222.
        /// The pressure sensor ID at offset 224 defaults to 0xFF (not recorded), as in WriteShimmer3RSdLogTestFile().
        /// </summary>
        private static string WriteShimmer3SdLogTestFile(int srId, int rev, int revSpecial, byte[] pressureCalib, byte[] packet,
            int fwMajor, int fwMinor, int fwInternal, byte pressureSensorIdByte = 0xFF)
        {
            byte[] header = new byte[256];
            header[0] = 0x80; // sampling rate 32768/128 = 256Hz
            header[5] = 0x04; // enabled sensors (bytes 3-7): BMPX80, bit 18
            header[31] = (byte)ShimmerVersion.SHIMMER3;
            header[35] = (byte)FW_IDENTIFIER_LOGANDSTREAM;
            header[36] = (byte)(fwMajor >> 8);
            header[37] = (byte)fwMajor;
            header[38] = (byte)fwMinor;
            header[39] = (byte)fwInternal;
            header[214] = (byte)srId;
            header[215] = (byte)rev;
            header[216] = (byte)revSpecial;
            if (pressureCalib != null)
            {
                Array.Copy(pressureCalib, 0, header, 160, Math.Min(pressureCalib.Length, 22));
                if (pressureCalib.Length > 22)
                {
                    Array.Copy(pressureCalib, 22, header, 222, pressureCalib.Length - 22);
                }
            }
            header[224] = pressureSensorIdByte;

            return WriteSdLogTestFile(header, packet);
        }

        private static string WriteSdLogTestFile(byte[] header, byte[] packet)
        {
            string filePath = Path.Combine(Path.GetTempPath(), "BMP581SdLogTest_" + Guid.NewGuid().ToString("N") + ".000");
            using (FileStream fs = new FileStream(filePath, FileMode.CreateNew))
            {
                fs.Write(header, 0, header.Length);
                fs.Write(packet, 0, packet.Length);
            }
            return filePath;
        }

        private void SetBmp581TestBoard(int hardwareVersion, int fwMajor, int fwMinor, int fwInternal, int srId, int rev, int revSpecial)
        {
            HardwareVersion = hardwareVersion;
            FirmwareIdentifier = FW_IDENTIFIER_LOGANDSTREAM;
            FirmwareMajor = fwMajor;
            FirmwareMinor = fwMinor;
            FirmwareInternal = fwInternal;
            ExpansionBoardId = srId;
            ExpansionBoardRev = rev;
            ExpansionBoardRevSpecial = revSpecial;
        }

        /// <summary>
        /// Shimmer3R LogAndStream v1.01.006 that answers 0xA7 with the given 0xA6 payload, or not at all (NACK) if it is null
        /// </summary>
        class ShimmerPressureTestDevice : ShimmerBluetooth
        {
            private readonly byte[] PressureCalibrationResponse;
            public int NumPressureCalibrationRequests = 0;

            public ShimmerPressureTestDevice(int srId, int rev, int revSpecial, byte[] pressureCalibrationResponse)
            {
                HardwareVersion = (int)ShimmerVersion.SHIMMER3R;
                FirmwareIdentifier = FW_IDENTIFIER_LOGANDSTREAM;
                FirmwareMajor = 1;
                FirmwareMinor = 1;
                FirmwareInternal = 6;
                ExpansionBoardId = srId;
                ExpansionBoardRev = rev;
                ExpansionBoardRevSpecial = revSpecial;
                PressureCalibrationResponse = pressureCalibrationResponse;
            }

            protected override void WriteBytes(byte[] b, int index, int length)
            {
                if (b[0] == (byte)PacketTypeShimmer3RSDBT.GET_PRESSURE_CALIBRATION_COEFFICIENTS_COMMAND)
                {
                    NumPressureCalibrationRequests++;
                    if (PressureCalibrationResponse != null)
                    {
                        InterpretPressureCalibrationCoefficientsResponse(PressureCalibrationResponse);
                    }
                }
            }

            public override string GetShimmerAddress() { throw new NotImplementedException(); }
            public override void SetShimmerAddress(string address) { throw new NotImplementedException(); }
            protected override void CloseConnection() { throw new NotImplementedException(); }
            protected override void FlushConnection() { throw new NotImplementedException(); }
            protected override void FlushInputConnection() { throw new NotImplementedException(); }
            protected override bool IsConnectionOpen() { throw new NotImplementedException(); }
            protected override void OpenConnection() { throw new NotImplementedException(); }
            protected override int ReadByte() { throw new NotImplementedException(); }
        }

        public override string GetShimmerAddress()
        {
            throw new NotImplementedException();
        }

        public override void SetShimmerAddress(string address)
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

        protected override bool IsConnectionOpen()
        {
            throw new NotImplementedException();
        }

        protected override void OpenConnection()
        {
            throw new NotImplementedException();
        }

        protected override int ReadByte()
        {
            throw new NotImplementedException();
        }

        protected override void WriteBytes(byte[] b, int index, int length)
        {
            throw new NotImplementedException();
        }
    }
}
