using System;
using Dotnatter.Crypto;
using Dotnatter.Util;

namespace Dotnatter.HashgraphImpl
{
    public class EventBody
    {

        public byte[][] Transactions { get; set; } //the payload
        public string[] Parents { get; set; } //hashes of the event's parents, self-parent first
        public byte[] Creator { get; set; } //creator's public key
        public DateTime Timestamp { get; set; } //creator's claimed timestamp of the event's creation
        public int Index { get; set; } //index in the sequence of events created by Creator

        //wire
        //It is cheaper to send ints then hashes over the wire
        internal int SelfParentIndex { get; set; }
        internal int OtherParentCreatorId { get; set; }
        internal int OtherParentIndex { get; set; }
        internal int CreatorId { get; set; }


        public byte[] Marshal()
        {
            return this.SerializeToByteArray();
        }

        public static EventBody Unmarshal(byte[] data)
        {
            return data.DeserializeFromByteArray<EventBody>();
        }

 
        public byte[] Hash() =>  CryptoUtils.Sha256(Marshal());

    }
}