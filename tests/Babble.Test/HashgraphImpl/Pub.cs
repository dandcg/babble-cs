using System.Security.Cryptography;

namespace Dotnatter.Test.HashgraphImpl
{
    public class Pub
    {
        public int Id { get; set; }
        public CngKey PrivKey { get; set; }
        public byte[] PubKey { get; set; }
        public string Hex { get; set; }
    }
}