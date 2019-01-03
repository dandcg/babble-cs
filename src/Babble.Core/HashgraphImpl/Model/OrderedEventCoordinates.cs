using System;
using System.Linq;

namespace Babble.Core.HashgraphImpl.Model
{
    public class OrderedEventCoordinates
    {
        public Index[] Values { get; }

        public Index[] CloneValues()
        {
           return Values.Select(s => (Index)s.Clone()).ToArray();
        }
        
        public OrderedEventCoordinates(Index[] values)
        {
            Values = values;
        }

        public OrderedEventCoordinates(int capacity)
        {
            Values = new Index[capacity];
        }

        public int GetIdIndex(int id)
        {
            var i = 0;
            foreach (var idx in Values)
            {
                if (idx.ParticipantId == id)
                {
                    return i;
                }

                i++;
            }

            return -1;
        }

        public (Index, bool) GetById(int id)
        {
            var idx = Values.FirstOrDefault(w => w.ParticipantId == id);
            if (idx != null)
            {
                return (idx, true);
            }

            return (new Index(), false);
        }

        public void Add(int id, EventCoordinates evt)
        {
            Values.Append(new Index {ParticipantId = id, Event = evt});
        }

 
    }
}