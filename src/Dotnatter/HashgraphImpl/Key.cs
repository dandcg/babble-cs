namespace Dotnatter.HashgraphImpl
{
    public class Key
    {

        public Key(string x, string y)
        {
            X = x;
            Y = y;
        }
        public string X { get; }
        public string Y { get;  }

        public override string ToString()
        {
            return $"{{{X}, {Y}}}";
        }
    }
}