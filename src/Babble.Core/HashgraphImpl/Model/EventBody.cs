using System;
using Babble.Core.Crypto;
using Babble.Core.Util;

namespace Babble.Core.HashgraphImpl.Model
{
    public class EventBody
    {
        private int selfParentIndex;
        private int otherParentCreatorId;
        private int otherParentIndex;
        private int creatorId;

        public byte[][] Transactions { get; set; } //the payload
        public string[] Parents { get; set; }  //hashes of the event's parents, self-parent first
        public byte[] Creator { get; set; } //creator's public key


        public int Index { get; set; } //index in the sequence of events created by Creator

        public BlockSignature[] BlockSignatures { get; set; }

        //wire
        //It is cheaper to send ints then hashes over the wire

        public void SetSelfParentIndex(int value)
        {
            selfParentIndex = value;
        }

        public int GetSelfParentIndex()
        {
            return selfParentIndex;
        }

        public void SetOtherParentCreatorId(int value)
        {
            otherParentCreatorId = value;
        }

        public int GetOtherParentCreatorId()
        {
            return otherParentCreatorId;
        }

        public void SetOtherParentIndex(int value)
        {
            otherParentIndex = value;
        }

        public int GetOtherParentIndex()
        {
            return otherParentIndex;
        }

        public void SetCreatorId(int value)
        {
            creatorId = value;
        }

        public int GetCreatorId()
        {
            return creatorId;
        }

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