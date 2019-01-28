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

        public static Frame Unmarshal(byte[] data)
        {
            return data.DeserializeFromByteArray<Frame>();
        }

        public byte[] Hash()
        {
            return CryptoUtils.Sha256(Marshal());
        }
    }
}