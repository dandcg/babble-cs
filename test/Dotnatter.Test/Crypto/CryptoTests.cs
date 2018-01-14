using System;
using Dotnatter.Crypto;
using Dotnatter.Util;
using Xunit;

namespace Dotnatter.Test.Crypto
{
    public class CryptoTests
    {
        [Fact]
        public void TestCryptoConsistencySigning()
        {
            var key = CryptoUtils.GenerateEcdsaKey();

            var bytes = "You grot boy!".StringToBytes();

            var s1 = CryptoUtils.Sign(key, bytes).ToHex();

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