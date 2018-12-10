using System.Collections.Generic;
using Babble.Core.HashgraphImpl.Model;

namespace Babble.Core.HashgraphImpl
{
    public class Frame
    {
        public Root[] Roots { get; set; }
        public Event[] Events { get; set; }
        public int Round { get; set; }
    }
}