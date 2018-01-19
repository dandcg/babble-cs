namespace Dotnatter.NetImpl
{
    public interface ILoopbackTransport
    {
        ITransport Transport { get; set; } // Embedded transport reference
        IWithPeers WithPeers { get; set; } // Embedded peer management
    }
}