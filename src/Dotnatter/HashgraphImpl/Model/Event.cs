using System;
using System.Collections.Generic;
using System.ComponentModel.Design;
using System.Diagnostics;
using System.Linq;
using System.Numerics;
using System.Security.Cryptography;
using Dotnatter.Crypto;
using Dotnatter.NodeImpl;
using Dotnatter.Util;

namespace Dotnatter.HashgraphImpl.Model
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

        public void SetConsensusTimestamp(DateTime value)
        {
            consensusTimestamp = value;
        }

        public DateTime GetConsensusTimestamp()
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
        private DateTime consensusTimestamp;
        private EventCoordinates[] lastAncestors;
        private EventCoordinates[] firstDescendants;
        private byte[] hash;

        public string Creator() => creator ?? (creator = Body.Creator.ToHex());



        public byte[] Hash() => hash ?? (hash = Body.Hash());

        public string Hex() => Hash().ToHex();

        public Event()
        {
        }

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
        public HashgraphError Sign(CngKey privKey)
        {
            var signBytes = Hash();
            Signiture=CryptoUtils.Sign(privKey, signBytes);
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
                    Index = Body.Index
                },
                Signiture = Signiture                //R = Signiture.R,
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
                return x.GetTopologicalIndex().CompareTo(y.GetTopologicalIndex());
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

                if (i.GetRoundReceived() != null)
                {
                    irr = (int) i.GetRoundReceived();
                }

                if (j.GetRoundReceived() != null)
                {
                    jrr = (int) j.GetRoundReceived();
                }

                if (irr != jrr)
                {
                    return irr.CompareTo(jrr);
                }

                if (!i.GetConsensusTimestamp().Equals(j.GetConsensusTimestamp()))
                {
                    return DateTime.Compare(i.GetConsensusTimestamp(), j.GetConsensusTimestamp());
                }

                Debug.Assert(i.GetRoundReceived() != null, "i.RoundReceived != null");

                var w = GetPseudoRandomNumber((int) i.GetRoundReceived());

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