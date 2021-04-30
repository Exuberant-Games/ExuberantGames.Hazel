﻿using System;
using System.IO;
using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Hazel.UnitTests
{
    [TestClass]
    public class MessageReaderTests
    {
        [TestMethod]
        public void ReadProperInt()
        {
            const int Test1 = int.MaxValue;
            const int Test2 = int.MinValue;

            var msg = new MessageWriter(128);
            msg.StartMessage(1);
            msg.Write(Test1);
            msg.Write(Test2);
            msg.EndMessage();

            Assert.AreEqual(11, msg.Length);
            Assert.AreEqual(msg.Length, msg.Position);

            MessageReader reader = MessageReader.Get(TestHelper.ReaderPool, msg.Buffer, 0);
            Assert.AreEqual(Test1, reader.ReadInt32());
            Assert.AreEqual(Test2, reader.ReadInt32());
        }

        [TestMethod]
        public void ReadProperBool()
        {
            const bool Test1 = true;
            const bool Test2 = false;

            var msg = new MessageWriter(128);
            msg.StartMessage(1);
            msg.Write(Test1);
            msg.Write(Test2);
            msg.EndMessage();

            Assert.AreEqual(5, msg.Length);
            Assert.AreEqual(msg.Length, msg.Position);

            MessageReader reader = MessageReader.Get(TestHelper.ReaderPool, msg.Buffer, 0);

            Assert.AreEqual(Test1, reader.ReadBoolean());
            Assert.AreEqual(Test2, reader.ReadBoolean());

        }

        [TestMethod]
        public void ReadProperString()
        {
            const string Test1 = "Hello";
            string Test2 = new string(' ', 1024);
            var msg = new MessageWriter(2048);
            msg.StartMessage(1);
            msg.Write(Test1);
            msg.Write(Test2);
            msg.Write(string.Empty);
            msg.EndMessage();

            Assert.AreEqual(msg.Length, msg.Position);

            MessageReader reader = MessageReader.Get(TestHelper.ReaderPool, msg.Buffer, 0);

            Assert.AreEqual(Test1, reader.ReadString());
            Assert.AreEqual(Test2, reader.ReadString());
            Assert.AreEqual(string.Empty, reader.ReadString());

        }

        [TestMethod]
        public void ReadProperFloat()
        {
            const float Test1 = 12.34f;

            var msg = new MessageWriter(2048);
            msg.StartMessage(1);
            msg.Write(Test1);
            msg.EndMessage();

            Assert.AreEqual(7, msg.Length);
            Assert.AreEqual(msg.Length, msg.Position);

            MessageReader reader = MessageReader.Get(TestHelper.ReaderPool, msg.Buffer, 0);

            Assert.AreEqual(Test1, reader.ReadSingle());
        }

        [TestMethod]
        public void RemoveMessageWorks()
        {
            const byte Test0 = 11;
            const byte Test3 = 33;
            const byte Test4 = 44;
            const byte Test5 = 55;

            var msg = new MessageWriter(2048);
            msg.StartMessage(0);
            msg.Write(Test0);
            msg.EndMessage();

            msg.StartMessage(12);
            msg.StartMessage(23);

            msg.StartMessage(34);
            msg.Write(Test3);
            msg.EndMessage();

            msg.StartMessage(45);
            msg.Write(Test4);
            msg.EndMessage();

            msg.EndMessage();
            msg.EndMessage();

            msg.StartMessage(56);
            msg.Write(Test5);
            msg.EndMessage();

            MessageReader reader = MessageReader.Get(TestHelper.ReaderPool, msg.Buffer);
            reader.Length = msg.Length;

            var zero = reader.ReadMessage();

            var one = reader.ReadMessage();
            var two = one.ReadMessage();
            var three = two.ReadMessage();
            two.RemoveMessage(three);

            // Reader becomes invalid
            Assert.AreNotEqual(Test3, three.ReadByte()); 

            // Unrealistic, but nice. Earlier data is not affected
            Assert.AreEqual(Test0, zero.ReadByte()); 

            // Continuing to read depth-first works
            var four = two.ReadMessage();
            Assert.AreEqual(Test4, four.ReadByte());

            var five = reader.ReadMessage();
            Assert.AreEqual(Test5, five.ReadByte());
        }

        [TestMethod]
        public void CopySubMessage()
        {
            const byte Test1 = 12;
            const byte Test2 = 146;

            var msg = new MessageWriter(2048);
            msg.StartMessage(1);

            msg.StartMessage(2);
            msg.Write(Test1);
            msg.Write(Test2);
            msg.EndMessage();

            msg.EndMessage();

            MessageReader handleMessage = MessageReader.Get(TestHelper.ReaderPool, msg.Buffer, 0);
            Assert.AreEqual(1, handleMessage.Tag);

            var parentReader = MessageReader.Get(TestHelper.ReaderPool, handleMessage);

            handleMessage.Recycle();
            SetZero(handleMessage);

            Assert.AreEqual(1, parentReader.Tag);

            for (int i = 0; i < 5; ++i)
            {

                var reader = parentReader.ReadMessage();
                Assert.AreEqual(2, reader.Tag);
                Assert.AreEqual(Test1, reader.ReadByte());
                Assert.AreEqual(Test2, reader.ReadByte());

                var temp = parentReader;
                parentReader = MessageReader.CopyMessageIntoParent(TestHelper.ReaderPool, reader);

                temp.Recycle();
                SetZero(temp);
                SetZero(reader);
            }
        }

        [TestMethod]
        public void ReadMessageLength()
        {
            var msg = new MessageWriter(2048);
            msg.StartMessage(1);
            msg.Write(65534);
            msg.StartMessage(2);
            msg.Write("HO");
            msg.EndMessage();
            msg.StartMessage(2);
            msg.EndMessage();
            msg.EndMessage();

            Assert.AreEqual(msg.Length, msg.Position);

            MessageReader reader = MessageReader.Get(TestHelper.ReaderPool, msg.Buffer, 0);
            Assert.AreEqual(1, reader.Tag);
            Assert.AreEqual(65534, reader.ReadInt32()); // Content

            var sub = reader.ReadMessage();
            Assert.AreEqual(3, sub.Length);
            Assert.AreEqual(2, sub.Tag);
            Assert.AreEqual("HO", sub.ReadString());

            sub = reader.ReadMessage();
            Assert.AreEqual(0, sub.Length);
            Assert.AreEqual(2, sub.Tag);
        }

        [TestMethod]
        public void ReadMessageAsNewBufferLength()
        {
            var msg = new MessageWriter(2048);
            msg.StartMessage(1);
            msg.Write(65534);
            msg.StartMessage(2);
            msg.Write("HO");
            msg.EndMessage();
            msg.StartMessage(232);
            msg.EndMessage();
            msg.EndMessage();

            Assert.AreEqual(msg.Length, msg.Position);

            MessageReader reader = MessageReader.Get(TestHelper.ReaderPool, msg.Buffer, 0);
            Assert.AreEqual(1, reader.Tag);
            Assert.AreEqual(65534, reader.ReadInt32()); // Content

            var sub = reader.ReadMessageAsNewBuffer();
            Assert.AreEqual(0, sub.Position);
            Assert.AreEqual(0, sub.Offset);

            Assert.AreEqual(3, sub.Length);
            Assert.AreEqual(2, sub.Tag);
            Assert.AreEqual("HO", sub.ReadString());

            sub.Recycle();

            sub = reader.ReadMessageAsNewBuffer();
            Assert.AreEqual(0, sub.Position);
            Assert.AreEqual(0, sub.Offset);

            Assert.AreEqual(0, sub.Length);
            Assert.AreEqual(232, sub.Tag);
            sub.Recycle();
        }

        [TestMethod]
        public void ReadStringProtectsAgainstOverrun()
        {
            const string TestDataFromAPreviousPacket = "You shouldn't be able to see this data";

            // An extra byte from the length of TestData when written via MessageWriter
            int DataLength = TestDataFromAPreviousPacket.Length + 1;

            // THE BUG
            //
            // No bound checks. When the server wants to read a string from
            // an offset, it reads the packed int at that offset, treats it
            // as a length and then proceeds to read the data that comes after
            // it without any bound checks. This can be chained with something
            // else to create an infoleak.

            MessageWriter writer = MessageWriter.Get(SendOption.None);

            // This will be our malicious "string length"
            writer.WritePacked(DataLength);

            // This is data from a "previous packet"
            writer.Write(TestDataFromAPreviousPacket);

            byte[] testData = writer.ToByteArray(includeHeader: false);

            // One extra byte for the MessageWriter header, one more for the malicious data
            Assert.AreEqual(DataLength + 1, testData.Length);

            var dut = MessageReader.Get(TestHelper.ReaderPool, testData);

            // If Length is short by even a byte, ReadString should obey that.
            dut.Length--;

            try
            {
                dut.ReadString();
                Assert.Fail("ReadString is expected to throw");
            }
            catch (InvalidDataException) { }
        }

        [TestMethod]
        public void ReadMessageProtectsAgainstOverrun()
        {
            const string TestDataFromAPreviousPacket = "You shouldn't be able to see this data";
            
            // An extra byte from the length of TestData when written via MessageWriter
            // Extra 3 bytes for the length + tag header for ReadMessage.
            int DataLength = TestDataFromAPreviousPacket.Length + 1 + 3;

            // THE BUG
            //
            // No bound checks. When the server wants to read a message, it
            // reads the uint16 at that offset, treats it as a length without any bound checks.
            // This can be allow a later ReadString or ReadBytes to create an infoleak.

            MessageWriter writer = MessageWriter.Get(SendOption.None);

            // This is the malicious length. No data in this message, so it should be zero.
            writer.Write((ushort)1); 
            writer.Write((byte)0); // Tag

            // This is data from a "previous packet"
            writer.Write(TestDataFromAPreviousPacket);

            byte[] testData = writer.ToByteArray(includeHeader: false);

            Assert.AreEqual(DataLength, testData.Length);

            var outer = MessageReader.Get(TestHelper.ReaderPool, testData);

            // Length is just the malicious message header.
            outer.Length = 3;

            try
            {
                outer.ReadMessage();
                Assert.Fail("ReadMessage is expected to throw");
            }
            catch (InvalidDataException) { }
        }

        [TestMethod]
        public void GetLittleEndian()
        {
            Assert.IsTrue(MessageWriter.IsLittleEndian());
        }

        private void SetZero(MessageReader reader)
        {
            for (int i = 0; i < reader.Buffer.Length; ++i)
                reader.Buffer[i] = 0;
        }
    }

}