using System;
using System.Text;
using Multiplayer.Common.Networking.Exception;

namespace Multiplayer.Common.Networking
{
    public class ByteReader
    {
        private readonly byte[] array;
        private int index;
        public object context;

        public int Length => array.Length;
        public int Position => index;
        public int Left => Length - Position;

        public ByteReader(byte[] array)
        {
            this.array = array;
        }

        public byte PeekByte() => array[index];

        public byte ReadByte() => array[IncrementIndex(1)];

        public sbyte ReadSByte() => (sbyte) array[IncrementIndex(1)];

        public short ReadShort() => BitConverter.ToInt16(array, IncrementIndex(2));

        public ushort ReadUShort() => BitConverter.ToUInt16(array, IncrementIndex(2));

        public int ReadInt32() => BitConverter.ToInt32(array, IncrementIndex(4));

        public uint ReadUInt32() => BitConverter.ToUInt32(array, IncrementIndex(4));

        public long ReadLong() => BitConverter.ToInt64(array, IncrementIndex(8));

        public ulong ReadULong() => BitConverter.ToUInt64(array, IncrementIndex(8));

        public float ReadFloat() => BitConverter.ToSingle(array, IncrementIndex(4));

        public double ReadDouble() => BitConverter.ToDouble(array, IncrementIndex(8));

        public bool ReadBool() => BitConverter.ToBoolean(array, IncrementIndex(1));

        public string ReadString(int maxLen = 32767)
        {
            int bytes = ReadInt32();

            if (bytes < 0)
                throw new ReaderException($"String byte length ({bytes}<0)");
            if (bytes > maxLen)
                throw new ReaderException($"String too long ({bytes}>{maxLen})");

            string result = Encoding.UTF8.GetString(array, index, bytes);
            index += bytes;
            return result;
        }

        public byte[] ReadRaw(int len)
        {
            return array.SubArray(IncrementIndex(len), len);
        }

        public byte[] ReadPrefixedBytes(int maxLen = int.MaxValue)
        {
            int len = ReadInt32();

            if (len < 0)
                throw new ReaderException($"Byte array length ({len}<0)");
            if (len >= maxLen)
                throw new ReaderException($"Byte array too long ({len}>{maxLen})");

            return ReadRaw(len);
        }

        public int[] ReadPrefixedInts(int maxLen = int.MaxValue)
        {
            int len = ReadInt32();

            if (len < 0)
                throw new ReaderException($"Int array length ({len}<0)");
            if (len >= maxLen)
                throw new ReaderException($"Int array too long ({len}>{maxLen})");

            int[] result = new int[len];
            for (int i = 0; i < len; i++)
                result[i] = ReadInt32();

            return result;
        }

        public uint[] ReadPrefixedUInts()
        {
            int len = ReadInt32();
            uint[] result = new uint[len];
            for (int i = 0; i < len; i++)
                result[i] = ReadUInt32();
            return result;
        }

        public string[] ReadPrefixedStrings()
        {
            int len = ReadInt32();
            string[] result = new string[len];
            for (int i = 0; i < len; i++)
                result[i] = ReadString();
            return result;
        }

        private int IncrementIndex(int size)
        {
            int i = index;
            index += size;
            return i;
        }
    }
}