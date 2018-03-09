using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using Babble.Core.Crypto;
using Babble.Core.Crypto.Asn;
using Babble.Core.Util;
using Xunit;

namespace Dotnatter.Test.Crypto
{
    public class CryptoTests
    {
        internal static byte[] ReadFile(string fileName)
        {
            FileStream f = new FileStream(fileName, FileMode.Open, FileAccess.Read);
            int size = (int) f.Length;
            byte[] data = new byte[size];
            size = f.Read(data, 0, size);
            f.Close();
            return data;
        }

        private byte[] GetBytesFromPEM(string pemString, string section)
        {
            var header = string.Format("-----BEGIN {0}-----", section);
            var footer = string.Format("-----END {0}-----", section);

            var start = pemString.IndexOf(header, StringComparison.Ordinal);
            if (start < 0)
                return null;

            start += header.Length;
            var end = pemString.IndexOf(footer, start, StringComparison.Ordinal) - start;

            if (end < 0)
                return null;

            return Convert.FromBase64String(pemString.Substring(start, end));
        }

        [Fact]
        public void TryOutput()
        {
            var pem = File.ReadAllText("Crypto\\text.pem");
            byte[] certBuffer = GetBytesFromPEM(pem, "EC PRIVATE KEY");

            Console.WriteLine(certBuffer.BytesToString());
            var t = new AsnEncodedData(new Oid("1.2.840.10045.3.1.7"), certBuffer);

            Console.WriteLine(t.Format(true));

            var p = new AsnEncodedData(new byte[] {05, 00});
            var x = new PublicKey(t.Oid, p, t);
            Console.WriteLine(x.EncodedKeyValue.Format(true));

            //var certificate = new X509Certificate2(t.Oid.Value);

            //Console.WriteLine(Directory.GetCurrentDirectory());

            //  var data = ReadFile("Crypto\\text.cer");

            //  var vdc=Convert.FromBase64String(data.BytesToString());

            //  Console.WriteLine(data.BytesToString());

            //  Console.WriteLine(vdc.BytesToString());

            //  var cert = new X509Certificate2(data);

            //var priv =cert.GetECDsaPrivateKey();

            //var p2 = priv as ECDsaCng;
            //var key = p2.Key;
        }

        [Fact]
        public void TryWithRaw()
        {
            var pubHex = "046A347F0488ABC7D92E2208794E327ECA15B0C2B27018B2B5B89DD8CB736FD7CC38F37D2D10822530AD97359ACBD837A65C2CA62D44B0CE569BD222C2DABF268F";
            var privBytes = "48 119 2 1 1 4 32 36 32 85 234 114 73 227 18 64 63 130 39 155 80 70 109 242 211 48 21 9 238 238 96 191 178 8 11 9 221 183 246 160 10 6 8 42 134 72 206 61 3 1 7 161 68 3 66 0 4 106 52 127 4 136 171 199 217 46 34 8 121 78 50 126 202 21 176 194 178 112 24 178 181 184 157 216 203 115 111 215 204 56 243 125 45 16 130 37 48 173 151 53 154 203 216 55 166 92 44 166 45 68 176 206 86 155 210 34 194 218 191 38 143".FromIntList();
            var pubBytes = pubHex.FromHex();

            var msgBytes = "time for beer".StringToBytes();

            var d = Asn1Node.ReadNode(privBytes);
            //Console.WriteLine(privBytes.ToHex());

            var pk = d.Nodes.First(n => n.NodeType == Asn1UniversalNodeType.OctetString).GetBytes().Skip(2).ToArray();
            // var oid = d.Nodes.First(n => n.NodeType == Asn1UniversalNodeType.ObjectId);
            //Console.WriteLine(pk.Length);

            //var npb = new List<byte>();
            //npb.AddRange("45435332".FromHex());
            //npb.AddRange("20000000".FromHex());

            //var keyType = new byte[] {0x45, 0x43, 0x53, 0x31};
            //var keyLength = new byte[] {0x20, 0x00, 0x00, 0x00};

            //var key = pubBytes.Skip(1);

            //var keyImport = keyType.Concat(keyLength).Concat(key).ToArray();

            //var cngKey = CngKey.Import(keyImport, CngKeyBlobFormat.EccPublicBlob);

            var keyType = new byte[] {0x45, 0x43, 0x53, 0x32};
            var keyLength = new byte[] {0x20, 0x00, 0x00, 0x00};

            var key = pubBytes.Skip(1);

            var keyImport = keyType.Concat(keyLength).Concat(key).Concat(pk.Take(32)).ToArray();

            var cngKey = CngKey.Import(keyImport, CngKeyBlobFormat.EccPrivateBlob);

           // Console.WriteLine(msgBytes.ToIntList());

           // Console.WriteLine(cngKey.Algorithm);

            using (var ecdsa = new ECDsaCng(cngKey))
            {
                ;

                var sig = ecdsa.SignHash(msgBytes);

                var r = sig.Take(32).ToArray().ToIntList();
                var s = sig.Skip(32).ToArray().ToIntList();

               // Console.WriteLine($"r={r}");
                //Console.WriteLine($"s={s}");
            }

            var sm = new List<byte>();

            var rb = "4 125 215 32 233 142 70 85 201 154 76 249 192 224 47 110 137 143 196 200 134 41 40 215 145 53 16 48 70 137 141 220".FromIntList();
            var sb = "13 204 63 209 196 150 249 28 161 192 197 238 187 28 49 93 64 81 111 132 87 13 150 77 41 62 144 197 244 173 110 176".FromIntList();


            var ri = new BigInteger(rb.Reverse().ToArray());
            




            Console.WriteLine(ri);

            var bi = BigInteger.Parse("2031592040209509309444738411503462520448943330036365867913793138397723332060");
            var bib = bi.ToByteArray();

            Console.WriteLine(bib.ToIntList());

            sm.AddRange(rb);
            sm.AddRange(sb);

            //Console.WriteLine(sm.Count);

            using (var ecdsa = new ECDsaCng(cngKey))
            {
                Assert.True(ecdsa.VerifyHash(msgBytes, sm.ToArray()));
            }

            //npb.AddRange(pubBytes.Skip(1));
            //npb.AddRange(pk);

            //var cngKey = CngKey.Import(npb.ToArray(), CngKeyBlobFormat.EccPrivateBlob);
        }

        [Fact]
        public void TestOut()
        {
        }

        [Fact]
        public void TestCryptoConsistencySigning()
        {
            var key = CryptoUtils.GenerateEcdsaKey();

            //var key= CngKey.Import();

            var bytes = "You grot boy!".StringToBytes();

            var s = CryptoUtils.Sign(key, bytes);
            var s1 = s.ToHex();
            Console.WriteLine(s1.Length);

            Console.WriteLine($"Sig1={s1}");

            var s2 = CryptoUtils.Sign(key, bytes).ToHex();

            Console.WriteLine($"Sig2={s2}");

            Assert.NotEqual(s1, s2);
        }

        [Fact]
        public void TestCryptoConsistencyHashing()
        {
            var key = CryptoUtils.GenerateEcdsaKey();

            var bytes = "You grot boy!".StringToBytes();

            var hash1 = CryptoUtils.Sha256(bytes).ToHex();

            Console.WriteLine(hash1);

            var hash2 = CryptoUtils.Sha256(bytes).ToHex();

            Console.WriteLine(hash2);

            Assert.Equal(hash1, hash2);
        }
    }
}