using System;
using System.Linq;
using System.Numerics;
using System.Security.Cryptography;
using Dotnatter.Util;

namespace Dotnatter.Crypto
{
    public static class CryptoUtils
    {
        public static byte[] SHA256(byte[] hashBytes)
        {
            using (var hasher = System.Security.Cryptography.SHA256.Create())
            {
                var hash = hasher.ComputeHash(hashBytes);

                return hash;
            }
        }

        public static CngKey GenerateECDSAKey()
        {
            var key = CngKey.Create(CngAlgorithm.ECDsaP256);
            return key;
        }

        public static CngKey ToECDSAPub(byte[] pub)
        {
            if (pub.Length == 0)
            {
                return null;
            }
            // Todo: Will need to align blob formats for interpolority
            var key = CngKey.Import(pub, CngKeyBlobFormat.GenericPublicBlob);
            return key;
        }

        public static byte[] FromECDSAPub(CngKey pub)
        {
            // Todo: Will need to align blob formats for interpolority
            var bytes = pub?.Export(CngKeyBlobFormat.GenericPublicBlob);
            return bytes;
        }

        public static (BigInteger r, BigInteger s) SignToBigInt(CngKey priv, byte[] hash)
        {
            var sig = Sign(priv, hash);

            // Todo: Check endian

            var r = new BigInteger(sig.Take(32).ToArray());
            var s = new BigInteger(sig.Skip(32).ToArray());

            return (r, s);
        }

        public static byte[] Sign(CngKey priv, byte[] hash)
        {
            Console.WriteLine("len={0},hash={1}", hash.Length, hash.ToHex());

            using (var ecdsa = new ECDsaCng(priv))
            {
                ecdsa.HashAlgorithm = CngAlgorithm.ECDsaP256;
                var sig = ecdsa.SignHash(hash);

                return sig;
            }
        }

        public static bool Verify(CngKey pub, byte[] hash, BigInteger r, BigInteger s)
        {
            var sig = new byte[64];

            // Todo: Check endian

            Array.Copy(r.ToByteArray(), 0, sig, 0, 32);
            Array.Copy(s.ToByteArray(), 0, sig, 32, 32);

            return Verify(pub, hash, sig);
        }

        public static bool Verify(CngKey pub, byte[] hash, byte[] sig)
        {
            using (var ecdsa = new ECDsaCng(pub))
            {
                ecdsa.HashAlgorithm = CngAlgorithm.ECDsaP256;

                Console.WriteLine("len={0},sig={1}", sig.Length, sig.ToHex());

                return ecdsa.VerifyHash(hash, sig);
            }
        }
    }
}