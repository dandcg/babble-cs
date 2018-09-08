using Babble.Core.Crypto;
using Babble.Core.HashgraphImpl.Model;
using Babble.Core.Util;
using Babble.Test.Helpers;
using Serilog;
using Xunit;
using Xunit.Abstractions;

namespace Babble.Test.HashgraphImpl
{
   public class BlockTests
    {
        private ITestOutputHelper output;
        private ILogger logger;

        public BlockTests(ITestOutputHelper output)
        {
            this.output = output;
            logger = output.SetupLogging().ForContext("SourceContext", "HashGraphTests");
        }


        [Fact]
   public void TestSignBlock()
        {
            var privateKey = CryptoUtils.GenerateEcdsaKey();

            var block = new Block(0, 1,
                "framehash".StringToBytes(),
                new[]
                {
                    "abc".StringToBytes(),
                    "def".StringToBytes(),
                    "ghi".StringToBytes(),
                });

            var (sig, err) = block.Sign(privateKey);
           Assert.Null(err);

            bool res;
            (res, err) = block.Verify(sig);
            Assert.Null(err);
            Assert.True(res);
        }

    [Fact]
    public void TestAppendSignature()
    {
        var privateKey = CryptoUtils.GenerateEcdsaKey();
        var pubKeyBytes = CryptoUtils.FromEcdsaPub(privateKey);

      
        var block = new Block(0, 1,
            "framehash".StringToBytes(),
            new[]
            {
                "abc".StringToBytes(),
                "def".StringToBytes(),
                "ghi".StringToBytes(),
            });

        var (sig, err) = block.Sign(privateKey);
        Assert.Null(err);

        err = block.SetSignature(sig);
        Assert.Null(err);

        BlockSignature blockSignature;
        (blockSignature, err) = block.GetSignature(pubKeyBytes.ToHex());
        Assert.Null(err);

        bool res;
            (res, err) = block.Verify(blockSignature);
        Assert.Null(err);
        Assert.True(res);

        }



    }
}
