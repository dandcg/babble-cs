namespace Babble.Core.NodeImpl
{

    public class NodeState
    {
        private NodeStateEnum state;

        private int starting;

        public void SetRecording(bool shoudRecord)
        {
        }

        public NodeStateEnum GetState()
        {
            return state;
        }

        public void SetState(NodeStateEnum s)
        {
            state = s;
        }

        public bool IsStarting()

        {
            return starting > 0;
        }

        public void SetStarting(bool b)
        {
            starting = 1;
        }

//func (b *nodeState) setStarting(starting bool) {
//if starting {
//atomic.CompareAndSwapInt32(&b.starting, 0, 1)
//} else {
//atomic.CompareAndSwapInt32(&b.starting, 1, 0)
//}
//}

//// Start a goroutine and add it to waitgroup
//func (b *nodeState) goFunc(f func()) {
//b.wg.Set(1)
//go func() {
//defer b.wg.Done()
//f()
//}()
//}

//public void waitRoutines() {
//b.wg.Wait()
//}
 
    }
}