using Dotnatter.Core.Crypto;
using Dotnatter.Core.Util;

namespace Dotnatter.Core.HashgraphImpl.Model
{
    public class BlockBody
    {
        public int Index { get; set; }
        public int RoundReceived { get; set; }
        public byte[] StateHash { get; set; }
        public byte[][] Transactions { get; set; }

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