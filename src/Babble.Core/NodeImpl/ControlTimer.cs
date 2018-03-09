using System;
using System.Threading;
using System.Threading.Tasks;
using Nito.AsyncEx;

namespace Babble.Core.NodeImpl
{
    public class ControlTimer
    {
        private readonly Func<TimeSpan> timerFactory;

        public ControlTimer(Func<TimeSpan> timerFactory)
        {
            this.timerFactory = timerFactory;
            TickCh = new AsyncProducerConsumerQueue<bool>();
            ResetCh = new AsyncProducerConsumerQueue<bool>();
            StopCh = new AsyncProducerConsumerQueue<bool>();
        }

        public AsyncProducerConsumerQueue<bool> TickCh { get; }

        public AsyncProducerConsumerQueue<bool> ResetCh { get; }

        public AsyncProducerConsumerQueue<bool> StopCh { get; }

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
            var timerTask = new Task(async () =>
            {
                while (!ct.IsCancellationRequested)
                {
                    var dur = timerFactory();
                    if (dur.Ticks == 0)
                    {
                        break;
                    }

                    await Task.Delay(dur, ct);
                    await TickCh.EnqueueAsync(true, ct);
                    Set = false;
                }
            });
            var resetTask = new Task(async () =>
            {
                while (!ct.IsCancellationRequested)
                {
                    await ResetCh.DequeueAsync(ct);
                    Set = true;
                }
            });
            var stopTask = new Task(async () =>
            {
                while (!ct.IsCancellationRequested)
                {
                    await StopCh.DequeueAsync(ct);
                    Set = false;
                }
            });

            await Task.WhenAny(Task.WhenAll(timerTask, resetTask, stopTask), Task.Delay(Timeout.Infinite, ct));
        }
    }
}