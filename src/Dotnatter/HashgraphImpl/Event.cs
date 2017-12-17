using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Dynamic;
using System.Linq;
using System.Numerics;
using System.Security.Cryptography;
using Dotnatter.Crypto;
using Dotnatter.Util;

namespace Dotnatter.HashgraphImpl
{
    public class Event
    {
        public EventBody Body { get; set; }



        //creator's digital signature of body
        public byte[] Signiture { get; set; }
        public (BigInteger R, BigInteger S) GetSignitureTuple()
        {
            var r = new BigInteger(Signiture.Take(32).ToArray());
            var s = new BigInteger(Signiture.Skip(32).ToArray());
            return (r, s);
        }

        public int TopologicalIndex { get; set; }
        public int? RoundReceived { get; set; }
        public DateTime ConsensusTimestamp { get; set; }
        public EventCoordinates[] LastAncestors { get; set; } //[participant fake id] => last ancestor
        public EventCoordinates[] FirstDescendants  { get; set; } //[participant fake id] => first descendant


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

    





    //Sorting
    public class EventByTimeStamp : IComparer<Event>
    {
        public int Compare(Event x, Event y)
        {
            Debug.Assert(x != null, nameof(x) + " != null");
            Debug.Assert(y != null, nameof(y) + " != null");
            return DateTime.Compare(x.Body.Timestamp, y.Body.Timestamp);
        }
    }

    public class EventByTopologicalOrder : IComparer<Event>
    {
        public int Compare(Event x, Event y)
        {
            Debug.Assert(x != null, nameof(x) + " != null");
            Debug.Assert(y != null, nameof(y) + " != null");
            return x.TopologicalIndex.CompareTo(y.TopologicalIndex);
        }
    }



    public class EventByConsensus : IComparer<Event>
    {

        private readonly Dictionary<int, RoundInfo> r = new Dictionary<int, RoundInfo>();
        private readonly Dictionary<int, BigInteger> cache = new Dictionary<int, BigInteger>();

        
        public int Compare(Event i, Event j)
        {

            Debug.Assert(i != null, nameof(i) + " != null");
            Debug.Assert(j != null, nameof(j) + " != null");


            var (irr, jrr) = (-1, -1);

            if (i.RoundReceived != null)
            {
                irr = (int) i.RoundReceived;
            }
 
            if (j.RoundReceived != null)
            {
                jrr = (int) j.RoundReceived;
            }

            if (irr != jrr)
            {
                return irr.CompareTo( jrr);
            }

            if (!i.ConsensusTimestamp.Equals(j.ConsensusTimestamp))
            {
                return DateTime.Compare(i.ConsensusTimestamp, i.ConsensusTimestamp);
            }

            Debug.Assert(i.RoundReceived != null, "i.RoundReceived != null");

            var w = GetPseudoRandomNumber((int)i.RoundReceived);
            
            var wsi =i.GetSignitureTuple().S ^ w;
            
           var  wsj = j.GetSignitureTuple().S ^ w;

            return wsi.CompareTo(wsj);
        }



        public BigInteger GetPseudoRandomNumber(int round)
        {

            if ( cache.TryGetValue(round, out var ps))
            {
                return ps;

            }
            var rd = r[round];

            ps = rd.PseudoRandomNumber();

            cache[round] = ps;

            return ps;
        }

    }



    }




    //++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++
    // WireEvent
}