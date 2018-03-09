using System;
using Dotnatter.Core.Crypto;
using Dotnatter.Core.HashgraphImpl.Model;
using Dotnatter.Core.Util;
using Dotnatter.Test.Helpers;
using Xunit;
using Xunit.Abstractions;

namespace Dotnatter.Test.HashgraphImpl
{
    public class EventTests
    {
        public EventTests(ITestOutputHelper output)
        {
            output.SetupLogging();
        }

        private static EventBody CreateDummyEventBody()
        {
            var body = new EventBody
            {
                Transactions = new[] {"abc".StringToBytes(), "def".StringToBytes()},
                Parents = new[] {"self", "other"},
                Creator = "public key".StringToBytes(),
                Timestamp = DateTimeOffset.UtcNow,
                
            };

            body.BlockSignatures = new[] {new BlockSignature() {Validator = body.Creator, Index = 0, Signature = "r|s".StringToBytes()}};
            return body;
        }

        [Fact]
        public void TestMarshallBody()
        {
            var body = CreateDummyEventBody();

            var raw = body.Marshal();

            var newBody = EventBody.Unmarshal(raw);

            newBody.ShouldCompareTo(body);

            newBody.Transactions.ShouldCompareTo(body.Transactions);

            newBody.BlockSignatures.ShouldCompareTo(body.BlockSignatures);

            newBody.Parents.ShouldCompareTo(body.Parents);

            newBody.Creator.ShouldCompareTo(body.Creator);

            Assert.Equal(body.Timestamp,newBody.Timestamp);

        }

        [Fact]
        public void TestSignEvent()
        {
            var privateKey = CryptoUtils.GenerateEcdsaKey();

            var publicKeyBytes = CryptoUtils.FromEcdsaPub(privateKey);

            var body = CreateDummyEventBody();

            body.Creator = publicKeyBytes;

            var ev = new Event {Body = body};

            ev.Sign(privateKey);

            var (ok, err) = ev.Verify();
            Assert.True(ok);
        }

        [Fact]
        public void TestBigIntegerSignitureEncoding()
        {
            var privateKey = CryptoUtils.GenerateEcdsaKey();

            var body = CreateDummyEventBody();

            var ev = new Event {Body = body};

      var hash = ev.Hash();

            var sig = CryptoUtils.SignToBigInt(privateKey, hash);

            Assert.True(CryptoUtils.Verify(privateKey, hash, sig.r, sig.s));
        }

        [Fact]
        public void TestMarshallEvent()
        {
            var privateKey = CryptoUtils.GenerateEcdsaKey();

            var publicKeyBytes = CryptoUtils.FromEcdsaPub(privateKey);

            var body = CreateDummyEventBody();

            body.Creator = publicKeyBytes;

            var ev = new Event {Body = body};
           
            ev.Sign(privateKey);

            var raw = ev.Marhsal();

            var newEv = Event.Unmarshal(raw);

            newEv.ShouldCompareTo(ev);
        }


        [Fact]
        public void TestWireEvent()
        {
            var privateKey = CryptoUtils.GenerateEcdsaKey();

            var publicKeyBytes = CryptoUtils.FromEcdsaPub(privateKey);
            
            var body = CreateDummyEventBody();

            body.Creator = publicKeyBytes;
            
            var ev = new Event {Body = body};

            ev.Sign(privateKey);

            ev.SetWireInfo(1, 66, 2, 67);
            
            var expectedWireEvent = new WireEvent
            {
                Body = new WireBody
                {
                    Transactions = ev.Body.Transactions,
                    SelfParentIndex = 1,
                    OtherParentCreatorId = 66,
                    OtherParentIndex = 2,
                    CreatorId = 67,
                    Timestamp = ev.Body.Timestamp,
                    Index = ev.Body.Index,
                    BlockSignatures = ev.WireBlockSignatures()
                },
                Signiture = ev.Signiture            };

            var wireEvent = ev.ToWire();
            
            wireEvent.ShouldCompareTo(expectedWireEvent);
        }


        [Fact]
        public void TestIsLoaded()
        {
            //nil payload
            var ev = new Event(null, null,new[] {"p1", "p2"}, "creator".StringToBytes(), 1);
            Assert.False(ev.IsLoaded(), "IsLoaded() should return false for nil Body.Transactions");
            
            //empty payload
            ev.Body.Transactions = new byte[0][];
            Assert.False(ev.IsLoaded(), "IsLoaded() should return false for empty Body.Transactions");


            ev.Body.BlockSignatures = new BlockSignature[]{};
            Assert.False(ev.IsLoaded(), "IsLoaded() should return false for empty Body.BlockSignatures");

            //initial event
            ev.Body.Index = 0;
            Assert.True(ev.IsLoaded(), "IsLoaded() should return true for initial event");
            
            //non-empty tx payload
            ev.Body.Transactions = new[] {"abc".StringToBytes()};
            Assert.True(ev.IsLoaded(), "IsLoaded() should return true for non-empty transaction payload");

            //non-empty signiture payload
            ev.Body.Transactions = null;
            ev.Body.BlockSignatures = new[] {new BlockSignature() {Validator = "validator".StringToBytes(), Index = 0, Signature = "r|s".StringToBytes()}};
            Assert.True(ev.IsLoaded(), "IsLoaded() should return true for non-empty transaction payload");

        }
    }
}