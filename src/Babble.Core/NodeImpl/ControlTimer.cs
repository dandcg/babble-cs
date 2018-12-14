using System;
using System.Threading;
using System.Threading.Tasks;
using System.Threading.Tasks.Dataflow;
using Nito.AsyncEx;

namespace Babble.Core.NodeImpl
{
    public class ControlTimer
    {
        private readonly Func<TimeSpan> timerFactory;

        public ControlTimer(Func<TimeSpan> timerFactory)
        {
            this.timerFactory = timerFactory;
            TickCh = new BufferBlock<bool>();
            ResetCh = new BufferBlock<bool>();
            StopCh = new BufferBlock<bool>();
        }

        public BufferBlock<bool> TickCh { get; }

        public BufferBlock<bool> ResetCh { get; }

        public BufferBlock<bool> StopCh { get; }

        public bool Set { get; private set; }

        public static ControlTimer NewRandomControlTimer(TimeSpan baseDuration)
        {
            TimeSpan RandomTimeout()
            {
                //Todo: For consistency change to System.Cryptography
                var r = new Random();
                var value = (long) ((r.NextDouble() * 2.0 - 1.0) * long.MaxValue);

                var minVal = baseDuration;
                if (minVal.Ticks == 0)
                {
                    return TimeSpan.Zero;
                }

                var extra = value % minVal.Ticks;
                return TimeSpan.FromTicks(minVal.Ticks + extra);
            }

            return new ControlTimer(RandomTimeout);
        }

        public async Task RunAsync(CancellationToken ct)
        {
            async Task TimerTask  () 
            {
                while (!ct.IsCancellationRequested)
                {
                    var dur = timerFactory();
                    if (dur.Ticks == 0)
                    {
                        break;
                    }
                    await Task.Delay(100, ct);
                    //await Task.Delay(dur, ct);
                    await TickCh.ReceiveAsync( ct);
                    Set = false;
                }
            }

            async Task ResetTask()
            {
                while (!ct.IsCancellationRequested)
                {
                    await ResetCh.ReceiveAsync(ct);
                    Set = true;
                }
            }

            
            async Task StopTask()
            {
                while (!ct.IsCancellationRequested)
                {
                    await StopCh.ReceiveAsync(ct);
                    Set = false;
                }
            };

            await Task.WhenAny(Task.WhenAll(TimerTask(), ResetTask(), StopTask()), Task.Delay(Timeout.Infinite, ct));
        }
    }
}