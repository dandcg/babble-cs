namespace Dotnatter.NetImpl.PeerImpl
{
    public class Peer
    {
        public string NetAddr { get; }
        public string PubKeyHex { get; }


        public Peer(string netAddr, string pubKeyHex)
        {
            this.NetAddr = netAddr;
            this.PubKeyHex = pubKeyHex;
     
        }


    }
}