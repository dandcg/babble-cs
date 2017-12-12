using System;
using System.Security.Cryptography;
using Dotnatter.Crypto;
using Dotnatter.Util;

namespace Dotnatter.HashgraphImpl
{
    public class Event
    {
        public EventBody Body { get; set; }
        //public (ulong R, ulong S) Signiture { get; set; } //creator's digital signature of body
        public byte[] Signiture { get; set; }
        public int TopologicalIndex { get; set; }
        public int RoundReceived { get; set; }
        public DateTime ConsensusTimestamp { get; set; }
        public EventCoordinates[] LastAncestors { get; set; } //[participant fake id] => last ancestor
        public EventCoordinates[] FirstDescendants { get; set; } //[participant fake id] => first descendant


        //sha256 hash of body and signature

        private string creator;
        private byte[] hash;
        private string hex;
        public string Creator => creator ?? (creator = Body.Creator.ToHex());
        public byte[] Hash() => hash ?? (hash = CryptoUtils.Sha256(Marhsal()));
        public string Hex() => hex ?? (hex = Hash().ToHex());

        public Event()
        { }

        public Event(byte[][] transactions, string[] parents, byte[] creator, int index)
        {
            var body = new EventBody
            {
                Transactions = transactions,
                Parents = parents,
                Creator = creator,
                Timestamp = DateTime.UtcNow, //strip monotonic time
                Index = index
            };
            
            Body = body;
        }

        public string SelfParent()
        {
            return Body.Parents[0];
        }

        public string OtherParent()
        {
            return Body.Parents[1];
        }

        public byte[][] Transactions()
        {
            return Body.Transactions;
        }

        public int Index()
        {
            return Body.Index;
        }

        //True if Event contains a payload or is the initial Event of its creator
        public bool IsLoaded()
        {
            if (Body.Index == 0)
            {
                return true;
            }

            return Body.Transactions?.Length > 0;
        }

        //ecdsa sig
        public void Sign(CngKey privKey)
        {
            var signBytes = Body.Hash();
            Signiture = CryptoUtils.Sign(privKey, signBytes);
        }

        public bool Verify()
        {
            var pubBytes = Body.Creator;
            var pubKey = CryptoUtils.ToEcdsaPub(pubBytes);
            var signBytes = Body.Hash();

            return CryptoUtils.Verify(pubKey, signBytes, Signiture);
        }


        //json encoding of body and signature
        public byte[] Marhsal()
        {
            return this.SerializeToByteArray();
        }

        public static Event Unmarshal(byte[] data)
        {
            return data.DeserializeFromByteArray<Event>();
        }

        public void SetRoundReceived(int rr)
        {
            RoundReceived = rr;
        }

        public void SetWireInfo(int selfParentIndex, int otherParentCreatorId, int otherParentIndex, int creatorId)
        {
            Body.SelfParentIndex = selfParentIndex;

            Body.OtherParentCreatorId = otherParentCreatorId;

            Body.OtherParentIndex = otherParentIndex;

            Body.CreatorId = creatorId;
        }

        public WireEvent ToWire()
        {
            return new WireEvent
            {
                Body = new WireBody
                {
                    Transactions = Body.Transactions,
                    SelfParentIndex = Body.SelfParentIndex,
                    OtherParentCreatorId = Body.OtherParentCreatorId,
                    OtherParentIndex = Body.OtherParentIndex,
                    CreatorId = Body.CreatorId,
                    Timestamp = Body.Timestamp,
                    Index = Body.Index
                },
                Signiture = Signiture
                //R = Signiture.R,
               // S = Signiture.S
            };
        }
    }
    
    //Sorting
    //Todo: Sorting extensions

    //++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++
    // WireEvent
}