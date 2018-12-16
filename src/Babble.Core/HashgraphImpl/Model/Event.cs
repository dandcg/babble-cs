using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Numerics;
using System.Security.Cryptography;
using Babble.Core.Crypto;
using Babble.Core.Util;

namespace Babble.Core.HashgraphImpl.Model
{
    public class Event
    {
        public EventBody Body { get; set; }

        //creator's digital signature of body
        public byte[] Signiture { get; set; }

        public (BigInteger R, BigInteger S) SignatureRs()
        {
            var r = new BigInteger(Signiture.Take(32).ToArray());
            var s = new BigInteger(Signiture.Skip(32).ToArray());
            return (r, s);
        }

        public void SetTopologicalIndex(int value)
        {
            topologicalIndex = value;
        }

        public int GetTopologicalIndex()
        {
            return topologicalIndex;
        }

        public void SetRoundReceived(int? value)
        {
            roundReceived = value;
        }

        public int? GetRoundReceived()
        {
            return roundReceived;
        }

        public void SetLastAncestors(OrderedEventCoordinates value)
        {
            lastAncestors = value;
        }

        public OrderedEventCoordinates LastAncestors
        {
            get { return lastAncestors; }
        }

        public void SetFirstDescendants(OrderedEventCoordinates value)
        {
            firstDescendants = value;
        }

        public OrderedEventCoordinates FirstDescendants
        {
            get { return firstDescendants; }
        }

        //sha256 hash of body and signature

       
        private int topologicalIndex;
        //private int? roundReceived;
        //private DateTimeOffset consensusTimestamp;
        
        private int? round;
        private int? lamportTimestamp;
        private int? roundReceived;
        
        
        private OrderedEventCoordinates lastAncestors;
        private OrderedEventCoordinates firstDescendants;
        
        private string creator;
        private byte[] hash;

        public string Creator()
        {
            return creator ?? (creator = Body.Creator.ToHex());
        }

        public byte[] Hash()
        {
            return hash ?? (hash = Body.Hash());
        }

        public string Hex()
        {
            return Hash().ToHex();
        }

        public Event()
        {
       

        }

        public Event(byte[][] transactions, BlockSignature[] blockSignatures, string[] parents, byte[] creator, int index)
        {
            var body = new EventBody
            {
                Transactions = transactions?? new byte[][]{},
                Parents = parents,
                Creator = creator,
                Index = index,
                BlockSignatures = blockSignatures ?? new BlockSignature[]{},
            };

            Body = body;
        }

        public string SelfParent => Body.Parents[0] ?? "";

        public string OtherParent =>  Body.Parents[1] ?? "";

        public byte[][] Transactions()
        {
            return Body.Transactions;
        }

        public int Index()
        {
            return Body.Index;
        }

        public BlockSignature[] BlockSignatures()
        {
            return Body.BlockSignatures;
        }


        //True if Event contains a payload or is the initial Event of its creator
        public bool IsLoaded()
        {
            if (Body.Index == 0)
            {
                return true;
            }

            var hasTransactions = Body.Transactions != null && Body.Transactions.Length > 0;

            return hasTransactions;
        }

        //ecdsa sig
        public HashgraphError Sign(CngKey privKey)
        {
            var signBytes = Hash();
            Signiture = CryptoUtils.Sign(privKey, signBytes);
            return null;
        }

        public (bool res, HashgraphError err) Verify()
        {
            var pubBytes = Body.Creator;
            var pubKey = CryptoUtils.ToEcdsaPub(pubBytes);
            var signBytes = Hash();

            return (CryptoUtils.Verify(pubKey, signBytes, Signiture), null);
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


        public void  SetRound(int r) 
        {
        
             
                round = r;
            

        }

        public int? Round => round;




       public void SetLamportTimestamp( int t)
       {
           lamportTimestamp = t;
       }

        public int? LamportTimestamp => lamportTimestamp;


        public void SetWireInfo(int selfParentIndex, int otherParentCreatorId, int otherParentIndex, int creatorId)
        {
            Body.SetSelfParentIndex(selfParentIndex);

            Body.SetOtherParentCreatorId(otherParentCreatorId);

            Body.SetOtherParentIndex(otherParentIndex);

            Body.SetCreatorId(creatorId);
        }


       public WireBlockSignature[] WireBlockSignatures() 
       {
            if (Body.BlockSignatures != null)
            {
                var wireSignatures = new List<WireBlockSignature>();
                
                foreach (var bs in Body.BlockSignatures)
                {
                    wireSignatures.Add(bs.ToWire());
                }

                return wireSignatures.ToArray();
            }

           return null;
       }


        public WireEvent ToWire()
        {
            return new WireEvent
            {
                Body = new WireBody
                {
                    Transactions = Body.Transactions,
                    SelfParentIndex = Body.GetSelfParentIndex(),
                    OtherParentCreatorId = Body.GetOtherParentCreatorId(),
                    OtherParentIndex = Body.GetOtherParentIndex(),
                    CreatorId = Body.GetCreatorId(),

                    Index = Body.Index,
                    BlockSignatures=WireBlockSignatures()
                },
                Signiture = Signiture 
            };
        }

       public static string RootSelfParent(int participantId ) {
            return $"Root{participantId}";
       }

        public class EventByTopologicalOrder : IComparer<Event>
        {
            public int Compare(Event x, Event y)
            {
                if (x == null) throw new ArgumentNullException(nameof(x));
                if (y == null) throw new ArgumentNullException(nameof(y));
                return x.GetTopologicalIndex().CompareTo(y.GetTopologicalIndex());
            }
        }


        public class EventByLamportTimeStamp : IComparer<Event>
        {
            public int Compare(Event i, Event j)
            {
                if (i == null) throw new ArgumentNullException(nameof(i));
                if (j == null) throw new ArgumentNullException(nameof(j));

                var (it, jt) = (-1, -1);

                if (i.lamportTimestamp != null)
                {
                    it = (int)i.lamportTimestamp;
                }
                if (j.lamportTimestamp != null)
                {
                    jt = (int) j.lamportTimestamp;
                }
                if (it != jt)
                {
                    return (it.CompareTo(jt));
                }

                var wsi = i.SignatureRs().S;
                var wsj = j.SignatureRs().S;
        
                return wsi.CompareTo(wsj);
            }
        }



        //public class EventByConsensus : IComparer<Event>
        //{
        //    private readonly Dictionary<int, RoundInfo> r = new Dictionary<int, RoundInfo>();
        //    private readonly Dictionary<int, BigInteger> cache = new Dictionary<int, BigInteger>();

        //    public int Compare(Event i, Event j)
        //    {
        //        if (i == null) throw new ArgumentNullException(nameof(i));
        //        if (j == null) throw new ArgumentNullException(nameof(j));

        //        var irr = i.GetRoundReceived() ?? -1;
        //        var jrr = j.GetRoundReceived() ?? -1;

        //        if (irr != jrr)
        //        {
        //            return irr.CompareTo(jrr);
        //        }

        //        if (!i.GetConsensusTimestamp().Equals(j.GetConsensusTimestamp()))
        //        {
        //            return DateTimeOffset.Compare(i.GetConsensusTimestamp(), j.GetConsensusTimestamp());
        //        }

        //        Debug.Assert(i.GetRoundReceived() != null, "i.RoundReceived != null");

        //        var w = GetPseudoRandomNumber(i.GetRoundReceived() ?? -1);

        //        var wsi = i.SignatureRs().S ^ w;

        //        var wsj = j.SignatureRs().S ^ w;

        //        return wsi.CompareTo(wsj);
        //    }

            //public BigInteger GetPseudoRandomNumber(int round)
            //{
            //    if (cache.TryGetValue(round, out var ps))
            //    {
            //        return ps;
            //    }

            //    if (!r.TryGetValue(round, out var rd))
            //    {
            //        rd = new RoundInfo();
            //    }

            //    ps = rd.PseudoRandomNumber();

            //    cache[round] = ps;

            //    return ps;
            //}
        }
    }

