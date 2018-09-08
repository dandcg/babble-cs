using System;
using System.Linq;
using System.Numerics;
using System.Security.Cryptography;
using Babble.Core.Common;
using Babble.Core.Util;

namespace Babble.Core.Crypto
{
    public static class CryptoUtils
    {
        public static byte[] Sha256(byte[] hashBytes)
        {
            using (var hasher = System.Security.Cryptography.SHA256.Create())
            {
                var hash = hasher.ComputeHash(hashBytes);
                return hash;
            }
        }

        public static CngKey GenerateEcdsaKey()
        {
            var key = CngKey.Create(CngAlgorithm.ECDsaP256);
            return key;
        }

        public static CngKey ToEcdsaPub(byte[] pub)
        {
            if (pub.Length == 0)
            {
                return null;
            }



            // Todo: Will need to align blob formats for interop
            var key = CngKey.Import(pub, CngKeyBlobFormat.GenericPublicBlob);
            return key;
        }

     

        public static byte[] FromEcdsaPub(CngKey pub)
        {
            // Todo: Will need to align blob formats for interop


            var bytes = pub?.Export(CngKeyBlobFormat.GenericPublicBlob);

            return bytes;
        }

        public static (BigInteger r, BigInteger s) SignToBigInt(CngKey priv, byte[] hash)
        {
            var sig = Sign(priv, hash);

            var r = new BigInteger(sig.Take(32).ToArray());
            var s = new BigInteger(sig.Skip(32).ToArray());

            return (r, s);
        }

        public static byte[] Sign(CngKey priv, byte[] hash)
        {
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
                return ecdsa.VerifyHash(hash, sig);
            }
        }

        public static string EncodeSignature(BigInteger r, BigInteger s)
        {
            return $"{r.ToBase36String()}|{r.ToBase36String()}"; //"%s|%s", r.Text(36), s.Text(36))
        }

        public static (BigInteger r, BigInteger s) DecodeSignature(string sig)
        {
            var values = sig.Split('|');
            if (values.Length != 2)
            {
               throw new Exception($"wrong number of values in signature: got {values.Length}, want 2");
            }

            var r = values[0].FromBase36String();

            var s= values[1].FromBase36String();
            return (r, s);
        }







    }
}