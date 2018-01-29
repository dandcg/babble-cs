namespace Dotnatter.NetImpl.TransportImpl
{
    public interface ILoopbackTransport
    {
        ITransport Transport { get; set; } // Embedded transport reference
        IWithPeers WithPeers { get; set; } // Embedded peer management
    }
}