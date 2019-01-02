namespace Babble.Core.HashgraphImpl.Model
{
    public class Index
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
    }
}