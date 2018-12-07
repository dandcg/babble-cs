using System.Collections.Generic;
using System.Linq;

namespace Babble.Core.HashgraphImpl.Model
{
    public class OrderedEventCoordinates : List<Index>
    {
        public OrderedEventCoordinates():base()
        {
            
        }

        public OrderedEventCoordinates(int capacity):base(capacity)
        {
            
        }
        public int GetIdIndex(int id)
        {
            return FindIndex(a => a.ParticipantId == id);
        }

        public (Index, bool) GetById(int id)
        {
            var idx = this.FirstOrDefault(w => w.ParticipantId == id);
            if (idx != null)
            {
                return (idx, true);
            }

            return (new Index(), false);
        }

        public void Add(int id, EventCoordinates evt)
        {
            this.Add(new Index {ParticipantId = id, Event = evt});
        }
    }

    public class Index
    {
        public int ParticipantId { get; set; }
        public EventCoordinates Event { get; set; }
    }
}