using System.IO;
using System.Linq;
using System.Security.Cryptography;

namespace Dotnatter.Crypto
{
    public static class Hash
    {
        public static byte[] Sha256(byte[] hashBytes)
        {
            using (var hasher = SHA256.Create())
            {
                var hash = hasher.ComputeHash(hashBytes);
                return hash;
            }
        }

        public static byte[] SimpleHashFromTwoHashes(byte[] left, byte[] right)
        {
            using (var hasher = SHA256.Create())
            {
                using (var ms = new MemoryStream())
                {
                    ms.Write(left, 0, left.Length);
                    ms.Write(right, 0, right.Length);

                    var hash = hasher.ComputeHash(ms);
                    return hash;
                }
            }
        }

        public static byte[] SimpleHashFromHashes(byte[][] hashes)
        {
            switch (hashes.Length)
            {
                case 0:
                    return null;
                case 1:
                    return hashes[0];
                default:
                    var left = SimpleHashFromHashes(hashes.Take((hashes.Length + 1) / 2).ToArray());
                    var right = SimpleHashFromHashes(hashes.Skip((hashes.Length + 1) / 2).ToArray());
                    return SimpleHashFromTwoHashes(left, right);
            }
        }
    }
}