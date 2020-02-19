using System;
using log4net.Core;

namespace Log4net.Appender.NewRelicLogs
{
    /// <summary>
    /// An evaluator that always triggers above a certain logging Level or after a specified number of seconds.
    /// </summary>
    public class TimeAndLevelEvaluator : ITriggeringEventEvaluator
    {
        /// <summary>
        /// One of the valid Levels which would trigger an immediate flush of the logging buffer
        /// </summary>
        public Level Threshold { get; set; } = Level.Error;

        /// <summary>
        /// Interval in seconds after which a buffer is flushed regardless of the number of events in it.
        /// Set to 0 to disable this portion of the evaluator.
        /// </summary>
        public int Interval { get; set; } = 60;

        private DateTime LastTimeUtc { get; set; } = DateTime.UtcNow;

        private readonly object _lockObject = new object();

        public bool IsTriggeringEvent(LoggingEvent loggingEvent)
        {
            if (loggingEvent?.Level == null)
            {
                throw new ArgumentNullException(nameof(loggingEvent));
            }

            //Threshold is evaluated first
            if (loggingEvent.Level >= Threshold)
            {
                return true;
            }

            // disable the time portion of evaluator if threshold is zero
            if (Interval == 0) return false;

            lock (_lockObject) // avoid triggering multiple times
            {
                var passed = DateTime.UtcNow.Subtract(LastTimeUtc);

                if (passed.TotalSeconds > Interval)
                {
                    LastTimeUtc = DateTime.UtcNow;
                    return true;
                }

                return false;
            }
        }
    }
}
