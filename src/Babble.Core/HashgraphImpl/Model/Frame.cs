using Babble.Core.Crypto;
using Babble.Core.Util;

namespace Babble.Core.HashgraphImpl.Model
{
    public class Frame
    {
        public int Round { get; set; } //RoundReceived
        public Root[] Roots { get; set; } // [participant ID] => Root
        public Event[] Events { get; set; } //Event with RoundReceived = Round

        //json encoding of body only
        public byte[] Marshal()
        {
            return this.SerializeToByteArray();
        }

        public static BlockBody Unmarshal(byte[] data)
        {
            return data.DeserializeFromByteArray<BlockBody>();
        }

        public byte[] Hash()
        {
            return CryptoUtils.Sha256(Marshal());
        }
    }
}