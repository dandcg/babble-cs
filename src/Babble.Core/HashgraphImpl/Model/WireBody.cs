using System;

namespace Babble.Core.HashgraphImpl.Model
{
    public class WireBody
    {
        public byte[][] Transactions { get; set; }

        public int SelfParentIndex { get; set; }
        public int OtherParentCreatorId { get; set; }
        public int OtherParentIndex { get; set; }
        public int CreatorId { get; set; }

        public DateTimeOffset Timestamp { get; set; }
        public int Index { get; set; }
        public WireBlockSignature[] BlockSignatures { get; set; }
    }
}