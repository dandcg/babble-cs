using System;

namespace Dotnatter.HashgraphImpl.Model
{
    public class WireBody
    {
        public byte[][] Transactions { get; set; }

        public int SelfParentIndex { get; set; }
        public int OtherParentCreatorId { get; set; }
        public int OtherParentIndex { get; set; }
        public int CreatorId { get; set; }

        public DateTime Timestamp { get; set; }
        public int Index { get; set; }
    }
}