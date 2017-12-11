namespace Dotnatter.HashgraphImpl
{
    public class Key
    {
        public string X { get; set; }
        public string Y { get; set; }

        public override string ToString()
        {
            return $"{{{X}, {Y}}}";
        }
    }
}