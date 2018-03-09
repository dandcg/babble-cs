namespace Babble.Core.HashgraphImpl.Model
{
    public class ParentRoundInfo
    {
        public int Round { get; set; }
        public bool IsRoot { get; set; }
        
        public ParentRoundInfo()
        {

            Round = -1;
            IsRoot = false;

        }


    }
}