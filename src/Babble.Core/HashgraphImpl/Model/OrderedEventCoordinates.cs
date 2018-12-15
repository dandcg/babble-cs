using System;
using System.Collections.Generic;
using System.Linq;

namespace Babble.Core.HashgraphImpl.Model
{
    public class OrderedEventCoordinates 
    {
        public  Index[] Values { get; private set; }

   
    
        public OrderedEventCoordinates(int capacity)
        {
            Values=new Index[capacity];
        }
        public int GetIdIndex(int id)
        {
            return Array.FindIndex(Values, a => a.ParticipantId == id);
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

    public class Index
    {
        public int ParticipantId { get; set; }
        public EventCoordinates Event { get; set; }
    }
}