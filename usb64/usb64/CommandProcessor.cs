﻿using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace ed64usb
{
    public static class CommandProcessor
    {

        public const uint ROM_BASE_ADDRESS = 0x10000000; //X-Series only
        public const uint RAM_BASE_ADDRESS = 0x80000000; //X-Series only
        public const uint EMULATOR_ROM_ADDRESS = ROM_BASE_ADDRESS + 0x200000; //Emulators expect the ROM to start at this offset
        
        public const string MINIMUM_SUPPORTED_OS_VERSION = "3.05";

        const int SIZE_4_KILOBYTE = 0x1000;
        const int SIZE_512_KILOBYTE = 0x80000;
        const int SIZE_1_MEGABYTE = 0x100000;
        const int SIZE_8_MEGABYTE = 0x800000;
        const int SIZE_32_MEGABYTE = 0x2000000;

        private enum SystemRegion : byte
        {
            PAL = (byte)'p',
            NTSC = (byte)'n',
            MPAL = (byte)'m',
            Unknown = (byte)'u'
        }

        private enum CartId : byte
        {
            V2_5 = 2,
            V3_0 = 3,
            X5 = 5,
            X7 = 7
        }

        private enum TransmitCommand : byte
        {
            RomFillCartridgeSpace = (byte)'c', //char ROM fill 'c' artridge space
            RomRead = (byte)'R', //char ROM 'R' ead
            RomWrite = (byte)'W', // char ROM 'W' rite
            RomStart = (byte)'s', //char ROM 's' tart
            TestConnection = (byte)'t', //char 't' est

            RamRead = (byte)'r', //char RAM 'r' ead
            //RamWrite = (byte)'w', //char RAM 'w' rite
            FpgaWrite = (byte)'f', //char 'f' pga write

            UnofficialRtcGet = (byte)'d', //char get RTC 'd' atetime
            UnofficialRtcSet = (byte)'D', //char set RTC 'D' atetime
            UnofficialResolutionGet = (byte)'p' //char get Resolution in 'p' ixels

        }

        public enum ReceiveCommand : byte
        {
            CommsReply = (byte)'r',
            CommsReplyLegacy = (byte)'k',
            UnofficialRtcDate = (byte)'d',
            UnofficialResolutionPixels = (byte)'p'

        }

        /// <summary>
        /// Dumps the current framebuffer to a file in bitmap format
        /// </summary>
        /// <param name="filename">The file to be written to</param>
        public static void DumpScreenBuffer(string filename)
        {
            //TODO: the official OS only currently supports 320x240 resolution, but should be read from the appropriate RAM register for forward compatibility! 
            // See https://n64brew.dev/wiki/Video_Interface for how this possibily could be improved.
            short width = 320; //TODO: the OS menu only currently supports 320x240 resolution, but should be read from the appropriate RAM register for forward compatibility! 
            short height = 240;

            

            //try
            //{ //The unofficial OS supports getting the resolution as a command packet type. This is run in a try statement incase we are using the official OS.
            //    CommandPacketTransmit(TransmitCommand.UnofficialResolutionGet);
            //    var responseBytes = CommandPacketReceive();
            //    if (responseBytes[3] == (byte)ReceiveCommand.UnofficialResolutionPixels)
            //    {
            //        if (BitConverter.IsLittleEndian)
            //        {

            //            Array.Reverse(responseBytes, 4, 2); //convert endian for width short
            //            Array.Reverse(responseBytes, 6, 2); //convert endian for height short
            //        }
            //        width = BitConverter.ToInt16(responseBytes, 4);
            //        height = BitConverter.ToInt16(responseBytes, 6);
            //        Console.WriteLine($"width = {width}, height = {height}");
            //    }
            //}
            //catch (Exception)
            //{

            //    //the packet is not supported on this OS. Just ignore and use the default.
            //}

            var length = width * height * 2;
            Console.WriteLine($"len = {length}");

            var data = RamRead(0xA4400004, 512); // get the framebuffer address from its pointer in cartridge RAM (requires reading the whole 512 byte buffer, otherwise USB comms will fail)
            if (BitConverter.IsLittleEndian)
            {
                Array.Reverse(data, 0, 4); //convert endian (we only need the first 4 bytes)
            }
            var framebufferAddress = BitConverter.ToUInt32(data, 0);

            data = RamRead(RAM_BASE_ADDRESS | framebufferAddress, length); // Get the framebuffer data from cartridge RAM
            File.WriteAllBytes(filename, ImageUtilities.ConvertToBitmap(width, height, data));       
        }

        /// <summary>
        /// Dumps the current ROM to a file
        /// </summary>
        /// <param name="filename">The filename</param>
        public static void DumpRom(string filename)
        {
            //TODO: what is the maximum ROM size, and should trailing Zeros be cut?
            var data = RomRead(ROM_BASE_ADDRESS, SIZE_1_MEGABYTE + SIZE_4_KILOBYTE); //TODO: only covers 1MB (+4KB CRC) ROMs and smaller, what about larger ROMs?
            File.WriteAllBytes(filename, data);

        }

        /// <summary>
        /// Check that the ROM can be wriien and read.
        /// </summary>
        public static void RunDiagnostics() //TODO: only covers the first 8MB of ROM?!
        {
            byte[] writeBuffer = new byte[SIZE_1_MEGABYTE]; //create a 1MB array
            byte[] readBuffer;

            for (int i = 0; i < SIZE_8_MEGABYTE; i += writeBuffer.Length) //for each 1MB in an 8MB range
            {
                new Random().NextBytes(writeBuffer); //randomly fill the 1MB array
                RomWrite(writeBuffer, ROM_BASE_ADDRESS);
                readBuffer = RomRead(ROM_BASE_ADDRESS, writeBuffer.Length);

                for (int u = 0; u < writeBuffer.Length; u++) //ensure that the bytes set match the bytes received.
                {
                    if (writeBuffer[u] != readBuffer[u]) throw new Exception("USB diagnostics error: " + (i + u));
                }
            }
        }

        /// <summary>
        /// Loads an FPGA RBF file
        /// </summary>
        /// <param name="filename">The filename to load</param>
        /// <returns></returns>
        public static bool LoadFpga(string filename)
        {
            var data = File.ReadAllBytes(filename);

            data = FixDataSize(data);
            CommandPacketTransmit(TransmitCommand.FpgaWrite, 0, data.Length, 0);

            UsbInterface.Write(data);
            var responseBytes = CommandPacketReceive();
            if (responseBytes[4] != 0)
            {
                throw new Exception($"FPGA configuration error: 0x{BitConverter.ToString(new byte[] { responseBytes[4] })}");
            }
            return true;
        }

        /// <summary>
        /// Loads a ROM
        /// </summary>
        /// <param name="filename">The filename to load</param>
        public static void LoadRom(string filename)
        {
            if (File.Exists(filename))
            {
                using (FileStream fs = new FileStream(filename, FileMode.Open, FileAccess.Read))
                {
                    using (BinaryReader br = new BinaryReader(fs))
                    {
                        var romBytes = new List<byte>();
                        var baseAddress = ROM_BASE_ADDRESS;

                        // We cannot rely on the filename for the format to be correct, so it is best to check the first 4 bytes of the ROM
                        //TODO: check this works on linux!
                        var header = br.ReadUInt32(); // Reading the the bytes as a UInt32 simplifies the code below, but at the expense of changing the byte format.
                        br.BaseStream.Position = 0; // Reset the stream position for when we need to read the full ROM.

                        switch (header)
                        {
                            case 0x40123780: // BigEndian - Native (if reading the bytes in order, it would be 0x80371240)
                                Console.Write("Rom format (BigEndian - Native).");
                                // No Conversion necessary, just load the file.
                                romBytes.AddRange(br.ReadBytes((int)fs.Length));
                                break;
                            case 0x12408037: //Byte Swapped (if reading the bytes in order, it would be 0x37804012)
                                Console.WriteLine("Rom format (Byte Swapped).");
                                // Swap each 2 bytes to make it Big Endian
                                {
                                    var chunk = br.ReadBytes(2).Reverse().ToArray();

                                    while (chunk.Length > 0)
                                    {
                                        romBytes.AddRange(chunk);
                                        chunk = br.ReadBytes(2).Reverse().ToArray();
                                    }

                                    romBytes.AddRange(chunk);
                                }
                                break;

                            case 0x80371240: // Little Endian (if reading the bytes in order, it would be 0x40123780)
                                Console.WriteLine("Rom format (Little Endian).");
                                // Reverse each 4 bytes to make it Big Endian
                                {
                                    var chunk = br.ReadBytes(4).Reverse().ToArray();

                                    while (chunk.Length > 0)
                                    {
                                        romBytes.AddRange(chunk);
                                        chunk = br.ReadBytes(4).Reverse().ToArray();
                                    }

                                    romBytes.AddRange(chunk);
                                }
                                break;

                            default:
                                Console.WriteLine("Unrecognised Rom Format: {0:X}, presuming emulator ROM.", header);
                                baseAddress = EMULATOR_ROM_ADDRESS;
                                break;
                        }

                        var fillValue = IsBootLoader(romBytes.ToArray()) ? 0xffffffff : 0; //TODO: or should it be made clear that it is filling 4 bytes (i.e. 0x00000000)

                        FillCartridgeRomSpace(romBytes.ToArray().Length, fillValue);
                        RomWrite(romBytes.ToArray(), baseAddress);
                    }
                }
            }
        }

        /// <summary>
        /// Reads the Cartridge ROM
        /// </summary>
        /// <param name="startAddress">The start address</param>
        /// <param name="length">The length to read</param>
        /// <returns></returns>
        private static byte[] RomRead(uint startAddress, int length)
        {

            CommandPacketTransmit(TransmitCommand.RomRead, startAddress, length, 0);

            UsbInterface.ProgressBarTimerInterval = length > SIZE_32_MEGABYTE ? SIZE_1_MEGABYTE : SIZE_512_KILOBYTE;
            var time = DateTime.Now.Ticks;
            var data = UsbInterface.Read(length);
            time = DateTime.Now.Ticks - time;
            Console.WriteLine($"OK. speed: {GetSpeedString(data.Length, time)}"); //TODO: this should be in the main program! or at least return the time!
            return data;
        }

        /// <summary>
        /// Reads the Cartridge RAM
        /// </summary>
        /// <param name="startAddress">The start address</param>
        /// <param name="length">The length to read</param>
        /// <returns></returns>
        private static byte[] RamRead(uint startAddress, int length)
        {

            CommandPacketTransmit(TransmitCommand.RamRead, startAddress, length, 0);

            Console.Write("Reading RAM...");
            UsbInterface.ProgressBarTimerInterval = length > SIZE_32_MEGABYTE ? SIZE_1_MEGABYTE : SIZE_512_KILOBYTE;
            var time = DateTime.Now.Ticks;
            var data = UsbInterface.Read(length);
            time = DateTime.Now.Ticks - time;
            Console.WriteLine($"OK. speed: {GetSpeedString(data.Length, time)}"); //TODO: this should be in the main program! or at least return the time!
            return data;
        }

        /// <summary>
        /// Writes to the cartridge ROM
        /// </summary>
        /// <param name="data">The data to write</param>
        /// <param name="startAddress">The start address</param>
        /// <returns></returns>
        private static byte[] RomWrite(byte[] data, uint startAddress)
        {

            var length = data.Length;

            CommandPacketTransmit(TransmitCommand.RomWrite, startAddress, length, 0);

            UsbInterface.ProgressBarTimerInterval = length > SIZE_32_MEGABYTE ? SIZE_1_MEGABYTE : SIZE_512_KILOBYTE;
            var time = DateTime.Now.Ticks;
            UsbInterface.Write(data);
            time = DateTime.Now.Ticks - time;

            Console.WriteLine($"OK. speed: {GetSpeedString(data.Length, time)}"); //TODO: this should be in the main program! or at least return the time!

            return data;
        }

        /// <summary>
        /// Starts a ROM on the cartridge
        /// </summary>
        /// <param name="fileName">The filename (optional)</param>
        /// <remarks> The filename (optional) is used for creating a save file on the SD card</remarks>
        public static void StartRom(string fileName = "")
        {

            if (fileName.Length < 256)
            {
                var filenameBytes = Encoding.ASCII.GetBytes(fileName);
                Array.Resize(ref filenameBytes, 256); //The packet must be 256 bytes in length, so resize it.

                CommandPacketTransmit(TransmitCommand.RomStart, 0, 0, 1);
                UsbInterface.Write(filenameBytes);
            }
            else
            { 
                throw new Exception("Filename exceeds the 256 character limit.");
            }

        }

        /// <summary>
        /// Reads the time from the Real Time Clock
        /// </summary>
        /// <returns></returns>
        public static DateTime ReadRtc()
        {
            CommandPacketTransmit(TransmitCommand.UnofficialRtcGet);
            var responseBytes = CommandPacketReceive();
            if (responseBytes[3] != (byte)ReceiveCommand.UnofficialRtcDate)
            {
                throw new Exception("Unexpected RTC Response");
            }

            //The received bytes are used as a "hex string" interpretation of the number, so it must be read as hex and then converted to an integer.
            var seconds = int.Parse(BitConverter.ToString(new byte[] { responseBytes[4] }));
            var minutes = int.Parse(BitConverter.ToString(new byte[] { responseBytes[5] }));
            var hours = int.Parse(BitConverter.ToString(new byte[] { responseBytes[6] }));
            //var dayOfWeek = int.Parse(BitConverter.ToString(new byte[] { responseBytes[7] }));
            var dayOfMonth = int.Parse(BitConverter.ToString(new byte[] { responseBytes[8] }));
            var month = int.Parse(BitConverter.ToString(new byte[] { responseBytes[9] }));
            var year = int.Parse(BitConverter.ToString(new byte[] { responseBytes[10] })) + 2000; // the year has a base offset of 2000
            return new DateTime(year, month, dayOfMonth, hours, minutes, seconds);
        }

        /// <summary>
        /// Writes the time to the Real Time Clock
        /// </summary>
        /// <param name="dateTime">The DateTime to send</param>
        public static void WriteRtc(DateTime dateTime)
        { //This does not conform to the CommandPacket Schema, so we will just create the expected packet and send it directly.

            List<byte> RtcPacket = new List<byte>
            {
                (byte)'c',
                (byte)'m',
                (byte)'d',
                (byte)TransmitCommand.UnofficialRtcSet,
                //we need to convert the values to be literal values as in the reverse of ReadRtc
                Convert.ToByte(dateTime.Second.ToString(), 16),
                Convert.ToByte(dateTime.Minute.ToString(), 16),
                Convert.ToByte(dateTime.Hour.ToString(), 16),
                Convert.ToByte(dateTime.DayOfWeek.ToString("x"), 16),
                Convert.ToByte(dateTime.Day.ToString(), 16),
                Convert.ToByte(dateTime.Month.ToString(), 16),
                Convert.ToByte(dateTime.ToString("yy"), 16) //todo, need to minus 2000
            };
            RtcPacket.AddRange(new byte[512 - 11]); //need to send as 512 bytes!

            //Console.WriteLine($"RTC Send: {BitConverter.ToString(RtcPacket.ToArray())}");

            UsbInterface.Write(RtcPacket.ToArray());
        }


        private static void FillCartridgeRomSpace(int romLength, uint value)
        {
            var crcArea = SIZE_1_MEGABYTE + SIZE_4_KILOBYTE; // The N64 only requires a minimum of the first 1MB (and CRC area) to be filled.
            if (romLength < crcArea)
            {

                Console.Write("Filling memory...");
                CommandPacketTransmit(TransmitCommand.RomFillCartridgeSpace, ROM_BASE_ADDRESS, crcArea, value);
                TestCommunication();
                Console.WriteLine("ok");
            }

        }

        /// <summary>
        /// Test that USB port is able to transmit and receive
        /// </summary>
        public static void TestCommunication()
        {
            CommandPacketTransmit(TransmitCommand.TestConnection);
            var responseBytes = CommandPacketReceive(); //the unofficial OS returns the system info as a response...
            if (responseBytes[4] != 0) //check for packet version (would be zero on the official OS)
            {
                if (responseBytes[4] == 1) // packet version 1
                {
                    var cartType = (char)responseBytes[5];
                    var systemRegion = Convert.ToChar(responseBytes[6]);
                    Console.WriteLine($"ED64 Version = {cartType}, N64 region = {(SystemRegion)systemRegion}.");
                }
            }
        }

        private static bool IsBootLoader(byte[] data)
        {
            var bootloader = true;
            const string BOOT_MESSAGE = "EverDrive bootloader";
            for (int i = 0; i < BOOT_MESSAGE.ToCharArray().Length; i++)
            {
                if (BOOT_MESSAGE.ToCharArray()[i] != data[0x20 + i]) bootloader = false;
            }

            return bootloader;
        }


        /// <summary>
        /// Transmits a command to the USB port
        /// </summary>
        /// <param name="commandType">the command to send</param>
        /// <param name="address">Optional</param>
        /// <param name="length">Optional </param>
        /// <param name="argument">Optional</param>
        private static void CommandPacketTransmit(TransmitCommand commandType, uint address = 0, int length = 0, uint argument = 0)
        {
            length /= 512; //Must take into account buffer size.

            var commandPacket = new List<byte>();

            commandPacket.AddRange(Encoding.ASCII.GetBytes("cmd"));
            commandPacket.Add((byte)commandType);
            if (BitConverter.IsLittleEndian)
            { //Convert to Big Endian
                commandPacket.AddRange(BitConverter.GetBytes(address).Reverse());
                commandPacket.AddRange(BitConverter.GetBytes(length).Reverse());
                commandPacket.AddRange(BitConverter.GetBytes(argument).Reverse());
            }
            else
            {
                commandPacket.AddRange(BitConverter.GetBytes(address));
                commandPacket.AddRange(BitConverter.GetBytes(length));
                commandPacket.AddRange(BitConverter.GetBytes(argument));
            }
            //It is uncertain whether the unofficial OS can support commands with a buffer size != to 512 bytes, but the official OS can.
            //Until we can do it with Unofficial OS, lets just send the whole 512 bytes!
            commandPacket.AddRange(new byte[512 - 16]); //TODO: find a way to support 16 byte commands in the unofficial OS!
            UsbInterface.Write(commandPacket.ToArray());

        }

        /// <summary>
        /// Receives a command response from the USB port
        /// </summary>
        /// <returns>the full response in bytes</returns>
        private static byte[] CommandPacketReceive()
        {

            var cmd = UsbInterface.Read(512); //should be 16, but this is needed for the unofficial OS?!
            if (Encoding.ASCII.GetString(cmd).ToLower().StartsWith("cmd") || Encoding.ASCII.GetString(cmd).ToLower().StartsWith("RSP"))
            {
                switch ((ReceiveCommand)cmd[3])
                {
                    case ReceiveCommand.UnofficialResolutionPixels:
                    case ReceiveCommand.UnofficialRtcDate:
                    case ReceiveCommand.CommsReply:
                        return cmd;
                    case ReceiveCommand.CommsReplyLegacy: //Certain ROM's may reply that used the old OSes without case sensitivity on the test commnad, this ensures they are handled.
                        throw new Exception($"Outdated OS, please update to {MINIMUM_SUPPORTED_OS_VERSION} or above!");
                    default:
                        throw new Exception("Unexpected response received from USB port.");
                }
            }
            else
            {
                throw new Exception($"Corrupted response received from USB port: {BitConverter.ToString(cmd)}.");
            }
        }



        private static string GetSpeedString(long length, long time)
        {
            time /= 10000;
            if (time == 0) time = 1;
            var speed = ((length / 1024) * 1000) / time;

            return ($"{speed} KB/s");

        }

        private static byte[] FixDataSize(byte[] data)
        {
            if (data.Length % 512 != 0)
            {
                var buff = new byte[data.Length / 512 * 512 + 512];
                for (int i = buff.Length - 512; i < buff.Length; i++)
                {
                    buff[i] = 0xff;
                }
                Array.Copy(data, 0, buff, 0, data.Length);

                return buff;
            }
            else
            {
                return data;
            }
        }
    }
}
