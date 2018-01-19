using System;
using System.Threading.Tasks;
using Dotnatter.Util;

namespace Dotnatter.NodeImpl
{
    public class ControlTimer
    {

        //private Channel<> timerFactory 


        protected ControlTimer()
        {
     
        }


     //public static ControlTimer NewControlTimer(timerFactory timerFactory) 
     //{
     //       return &ControlTimer{
     //           timerFactory: timerFactory,
     //           tickCh:       make(chan struct{}),
     //           resetCh:      make(chan struct{}),
     //           stopCh:       make(chan struct{}),
     //           shutdownCh:   make(chan struct{}),
     //       }
     //   }


     public static ControlTimer NewRandomControlTimer(TimeSpan baseDuration) 
        {
            throw new NotImplementedException();
     //       randomTimeout := func() <-chan time.Time {
     //           minVal := base
     //           if minVal == 0 {
     //               return nil
     //           }
     //           extra := (time.Duration(rand.Int63()) % minVal)
     //           return time.After(minVal + extra)
     //       }
     //       return NewControlTimer(randomTimeout)
        }



        public async Task RunAsync()
        {
            throw new NotImplementedException();
        }
    }
}