using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Akka.Actor;

namespace ChartApp.Actors
{
    /// <summary>
    /// Actor responsible for monitoring a specific <see cref="PerformanceCounter"/>
    /// </summary>
    public class PerformanceCounterActor : UntypedActor
    {
        private readonly string _seriesName;
        private readonly Func<PerformanceCounter> _performanceCounterGenerator;
        private PerformanceCounter _counter;

        private readonly HashSet<IActorRef> _subscriptions;
        private readonly ICancelable _cancelPublishing;

        public PerformanceCounterActor(string seriesName, Func<PerformanceCounter> performanceCounterGenerator)
        {
            _seriesName = seriesName;
            _performanceCounterGenerator = performanceCounterGenerator;
            _subscriptions = new HashSet<IActorRef>();
            _cancelPublishing = new Cancelable(Context.System.Scheduler);
        }

        #region Actor lifecycle methods
        protected override void PreStart()
        {
            // Create a new instance of the performance counter
            _counter = _performanceCounterGenerator();
            Context.System.Scheduler.ScheduleTellRepeatedly(
                TimeSpan.FromMilliseconds(250),
                TimeSpan.FromMilliseconds(250),
                Self,
                new GatherMetrics(),
                Self,
                _cancelPublishing);
        }

        // prevent resource leaks by disposing of our current PeformanceCounter
        protected override void PostStop()
        {
            try
            {
                // Terminate the scheduled task
                _cancelPublishing.Cancel(false);
                _counter.Dispose();
            }
            catch
            {
                // Don't care about additional "ObjectDisposed" exceptions
            }
            finally
            {
                base.PostStop();
            }
        }
        #endregion

        protected override void OnReceive(object message)
        {
            if (message is GatherMetrics)
            {
                // Publish latest counter value to all subscribers
                var metric = new Metric(_seriesName, _counter.NextValue());
                foreach (var sub in _subscriptions)
                {
                    sub.Tell(metric);
                }
            }
            else if (message is SubscribeCounter)
            {
                // Add a subscription for this counter
                // (it's parent's job to filter by counter types)
                var sc = message as SubscribeCounter;
                _subscriptions.Add(sc.Subscriber);
            }
            else if (message is UnsubscribeCounter)
            {
                // Remove a subscription from this counter
                var uc = message as UnsubscribeCounter;
                _subscriptions.Remove(uc.Subscriber);
            }
        }
    }
}
