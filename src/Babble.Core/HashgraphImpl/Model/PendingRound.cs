namespace Babble.Core.HashgraphImpl.Model
{
    public class PendingRound
    {
        public PendingRound()
        {
        }

        public PendingRound(int index, bool decided)
        {
            Index = index;
            Decided = decided;
        }

        public int Index { get; set; }
        public bool Decided { get; set; }
    }
}