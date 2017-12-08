using System;
using System.IO;
using System.Text;
using Newtonsoft.Json;

namespace Dotnatter.Util
{
    public static class ByteUtils
    {
        /// Decode a hex string into bytes.
        public static byte[] FromHex(this string hex)
        {
            var numberChars = hex.Length;
            var bytes = new byte[numberChars / 2];
            for (var i = 0; i < numberChars; i += 2)
                bytes[i / 2] = Convert.ToByte(hex.Substring(i, 2), 16);
            return bytes;
        }

        public static string ToHex(this byte[] bytes)
        {
            var hex = new StringBuilder(bytes.Length * 2);

            foreach (var b in bytes)
                hex.AppendFormat("{0:x2}", b);

            return hex.ToString();
        }


        // string encoding 
        public static byte[] StringToBytes(this string str)
        {
            var bytes = Encoding.ASCII.GetBytes(str);
            return bytes;
        }

        public static string BytesToString(this byte[] bytes)
        {
            var str = Encoding.ASCII.GetString(bytes);
            return str;
        }
        
        // Serialization helpers
        public static T DeserializeFromByteArray<T>(this byte[] data) where T : class
        {
            using (var stream = new MemoryStream(data))
            using (var reader = new StreamReader(stream, Encoding.UTF8))
                return JsonSerializer.Create().Deserialize(reader, typeof(T)) as T;
        }
        
        public static byte[] SerializeToByteArray<T>(this T obj) 
        {
            using (var stream = new MemoryStream())
            {
                using (var writer = new StreamWriter(stream, Encoding.UTF8))
                {
                    JsonSerializer.Create().Serialize(writer, obj);

                }
                return stream.ToArray();
            }

        }



    }
}