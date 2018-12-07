namespace Babble.Core.HashgraphImpl.Model
{
    public class ParentRoundInfo
    {
        public int Round { get; set; }
        public bool IsRoot { get; set; }
        public int RootStronglySeenWitnesses { get; set; }

        // remove constructor init
        public ParentRoundInfo()
        {
            Round = -1;
            IsRoot = false;
        }

        public static ParentRoundInfo NewBaseParentRoundInfo()
        {
            return new ParentRoundInfo
            {
                Round = -1,
                IsRoot = false
            };
        }
    }
}