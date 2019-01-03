using System;

namespace Babble.Core.HashgraphImpl.Model
{
    public class Index:ICloneable
    {
        public Index()
        {
            
        }

        public Index(int participantId, EventCoordinates ev)
        {
            ParticipantId = participantId;
            Event = ev;
        }

        public int ParticipantId { get; set; }
        public EventCoordinates Event { get; set; }
        public object Clone()
        {
            return new Index(ParticipantId,(EventCoordinates) Event.Clone());
        }
    }
}