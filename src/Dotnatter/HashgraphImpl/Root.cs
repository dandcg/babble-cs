using System.Collections.Generic;
using Dotnatter.Util;

namespace Dotnatter.HashgraphImpl
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
    - Round= 0      -		 - Round= 0      -       - Round= 0      -
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
    - Round= r0     -		 - Round= r1     -       - Round= r2     -
    - Others= {     - 		 - Others= empty -       - Others= empty -
    -  E02: E_OLD   -        -----------------       -----------------
    - }             -
    -----------------
    */

    public class Root
    {
        public string X { get; set; }
        public string Y { get; set; }
        public int Index { get; set; }
        public int Round { get; set; }
        public Dictionary<string, string> Others { get; set; }


        public static Root NewBaseRoot()
        {
            return new Root
            {
                X = "",
                Y = "",
                Index = -1,
                Round = -1,
                Others = new Dictionary<string, string>()
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