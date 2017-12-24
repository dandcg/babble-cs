using System;
using Dotnatter.Crypto;
using Dotnatter.HashgraphImpl;
using Dotnatter.Test.Helpers;
using Dotnatter.Util;
using Xunit;

namespace Dotnatter.Test.HashgraphImpl
{
    public class EventTests
    {
        private static EventBody CreateDummyEventBody()
        {
            var body = new EventBody
            {
                Transactions = new[] {"abc".StringToBytes(), "def".StringToBytes()},
                Parents = new[] {"self", "other"},
                Creator = "public key".StringToBytes(),
                Timestamp = DateTime.MinValue
            };
            return body;
        }

        [Fact]
        public void TestMarshallBody()
        {
            var body = CreateDummyEventBody();

            var raw = body.Marshal();

            var newBody = EventBody.Unmarshal(raw);

            newBody.ShouldCompareTo(body);
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

            Assert.True(ev.Verify());
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
                    Index = ev.Body.Index
                },
                Signiture = ev.Signiture
            };

            var wireEvent = ev.ToWire();
            
            wireEvent.ShouldCompareTo(expectedWireEvent);
        }


        [Fact]
        public void TestIsLoaded()
        {
            //nil payload
            var ev = new Event(null, new[] {"p1", "p2"}, "creator".StringToBytes(), 1);
            Assert.False(ev.IsLoaded(), "IsLoaded() should return false for nil Body.Transactions");
            
            //empty payload
            ev.Body.Transactions = new byte[0][];
            Assert.False(ev.IsLoaded(), "IsLoaded() should return false for empty Body.Transactions");

            //initial event
            ev.Body.Index = 0;
            Assert.True(ev.IsLoaded(), "IsLoaded() should return true for initial event");
            
            //non-empty payload
            ev.Body.Transactions = new[] {"abc".StringToBytes()};
            Assert.True(ev.IsLoaded(), "IsLoaded() should return true for non-empty payload");
        }
    }
}