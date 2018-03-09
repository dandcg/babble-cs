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

        public void SetConsensusTimestamp(DateTimeOffset value)
        {
            consensusTimestamp = value;
        }

        public DateTimeOffset GetConsensusTimestamp()
        {
            return consensusTimestamp;
        }

        public void SetLastAncestors(EventCoordinates[] value)
        {
            lastAncestors = value;
        }

        public EventCoordinates[] GetLastAncestors()
        {
            return lastAncestors;
        }

        public void SetFirstDescendants(EventCoordinates[] value)
        {
            firstDescendants = value;
        }

        public EventCoordinates[] GetFirstDescendants()
        {
            return firstDescendants;
        }

        //sha256 hash of body and signature

        private string creator;
        private int topologicalIndex;
        private int? roundReceived;
        private DateTimeOffset consensusTimestamp;
        private EventCoordinates[] lastAncestors;
        private EventCoordinates[] firstDescendants;
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
                BlockSignatures = blockSignatures ?? new BlockSignature[]{},
                Parents = parents,
                Creator = creator,
                Timestamp = DateTime.UtcNow, //strip monotonic time
                Index = index
            };

            Body = body;
        }

        public string SelfParent => Body.Parents[0];

        public string OtherParent => Body.Parents.Length > 1 ? Body.Parents[1] : "";

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

            var hasBlockSignatures = Body.BlockSignatures != null && Body.BlockSignatures.Length > 0;

            return hasTransactions || hasBlockSignatures;
        }

        //ecdsa sig
        public HashgraphError Sign(CngKey privKey)
        {
            var signBytes = Hash();
            Signiture = CryptoUtils.Sign(privKey, signBytes);
            return null;
        }

        public (bool res, Exception err) Verify()
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
                    Timestamp = Body.Timestamp,
                    Index = Body.Index,
                    BlockSignatures=WireBlockSignatures()
                },
                Signiture = Signiture 
            };
        }

        //Sorting
        public class EventByTimeStamp : IComparer<Event>
        {
            public int Compare(Event x, Event y)
            {
                if (x == null) throw new ArgumentNullException(nameof(x));
                if (y == null) throw new ArgumentNullException(nameof(y));
                return DateTimeOffset.Compare(x.Body.Timestamp, y.Body.Timestamp);
            }
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

        public class EventByConsensus : IComparer<Event>
        {
            private readonly Dictionary<int, RoundInfo> r = new Dictionary<int, RoundInfo>();
            private readonly Dictionary<int, BigInteger> cache = new Dictionary<int, BigInteger>();

            public int Compare(Event i, Event j)
            {
                if (i == null) throw new ArgumentNullException(nameof(i));
                if (j == null) throw new ArgumentNullException(nameof(j));

                var irr = i.GetRoundReceived() ?? -1;
                var jrr = j.GetRoundReceived() ?? -1;

                if (irr != jrr)
                {
                    return irr.CompareTo(jrr);
                }

                if (!i.GetConsensusTimestamp().Equals(j.GetConsensusTimestamp()))
                {
                    return DateTimeOffset.Compare(i.GetConsensusTimestamp(), j.GetConsensusTimestamp());
                }

                Debug.Assert(i.GetRoundReceived() != null, "i.RoundReceived != null");

                var w = GetPseudoRandomNumber(i.GetRoundReceived() ?? -1);

                var wsi = i.SignatureRs().S ^ w;

                var wsj = j.SignatureRs().S ^ w;

                return wsi.CompareTo(wsj);
            }

            public BigInteger GetPseudoRandomNumber(int round)
            {
                if (cache.TryGetValue(round, out var ps))
                {
                    return ps;
                }

                if (!r.TryGetValue(round, out var rd))
                {
                    rd = new RoundInfo();
                }

                ps = rd.PseudoRandomNumber();

                cache[round] = ps;

                return ps;
            }
        }
    }

    //++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++
    // WireEvent
}