using System.Collections.Generic;
using Babble.Core.Util;

namespace Babble.Core.HashgraphImpl.Model
{
    /*
    Roots constitute the base of a Hashgraph. Each Participant is assigned a Root on
    top of which Events will be added. The first Event of a participant must have a
    Self-Parent and an Other-Parent that match its Root X and Y respectively.

    This construction allows us to initialize Hashgraphs where the first Events are
    taken from the middle of another Hashgraph

    ex 1:

    -----------------        -----------------       -----------------
    - Event E0      -        - Event E1      -       - Event E2      -
    - SP = ""       -        - SP = ""       -       - SP = ""       -
    - OP = ""       -        - OP = ""       -       - OP = ""       -
    -----------------        -----------------       -----------------
            |                        |                       |
    -----------------		 -----------------		 -----------------
    - Root 0        - 		 - Root 1        - 		 - Root 2        -
    - X = Y = ""    - 		 - X = Y = ""    -		 - X = Y = ""    -
    - Index= -1     -		 - Index= -1     -       - Index= -1     -
    - Others= empty - 		 - Others= empty -       - Others= empty -
    -----------------		 -----------------       -----------------

    ex 2:

    -----------------
    - Event E02     -
    - SP = E01      -
    - OP = E_OLD    -
    -----------------
           |
    -----------------
    - Event E01     -
    - SP = E00      -
    - OP = E10      -  \
    -----------------    \
           |               \
    -----------------        -----------------       -----------------
    - Event E00     -        - Event E10     -       - Event E20     -
    - SP = x0       -        - SP = x1       -       - SP = x2       -
    - OP = y0       -        - OP = y1       -       - OP = y2       -
    -----------------        -----------------       -----------------
            |                        |                       |
    -----------------		 -----------------		 -----------------
    - Root 0        - 		 - Root 1        - 		 - Root 2        -
    - X: x0, Y: y0  - 		 - X: x1, Y: y1  - 		 - X: x2, Y: y2  -
    - Index= i0     -		 - Index= i1     -       - Index= i2     -
    - Others= {     - 		 - Others= empty -       - Others= empty -
    -  E02: E_OLD   -        -----------------       -----------------
    - }             -
    -----------------
    */

    //RootEvent contains enough information about an Event and its direct descendant
//to allow inserting Events on top of it.
    public class RootEvent
    {

        public RootEvent()
        {
            
        }

        public string Hash { get; set; }
        public int CreatorId { get; set; }
        public int Index { get; set; }
        public int LamportTimestamp { get; set; }
        public int Round { get; set; }

//NewBaseRootEvent creates a RootEvent corresponding to the the very beginning
//of a Hashgraph.
       public static RootEvent NewBaseRootEvent(int creatorId )
       {
           var res = new RootEvent
           {
               Hash = $"Root{creatorId}",
               CreatorId = creatorId,
               Index = -1,
               LamportTimestamp = -1,
               Round = -1
           };
           return res;
       }
    }
//Root forms a base on top of which a participant's Events can be inserted. In
//contains the SelfParent of the first descendant of the Root, as well as other
//Events, belonging to a past before the Root, which might be referenced
//in future Events. NextRound corresponds to a proposed value for the child's
//Round; it is only used if the child's OtherParent is empty or NOT in the
//Root's Others.




    public class Root
    {
        public int NextRound { get; set; }
        public RootEvent SelfParent { get; set; }
        public Dictionary<string, RootEvent> Others { get; set; }

        public Root()
        {
            
        }

        public static Root NewBaseRoot(int creatorId )
        {
            return new Root
            {
               NextRound = 0,
                SelfParent = RootEvent.NewBaseRootEvent(creatorId ),
                Others = new Dictionary<string, RootEvent>()
            };
        }

        //json encoding of body and signature
        public byte[] Marhsal()
        {
            return this.SerializeToByteArray();
        }

        public static Root Unmarshal(byte[] data)
        {
            return data.DeserializeFromByteArray<Root>();
        }
    }
}