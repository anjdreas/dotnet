using System;
using System.Linq;
using System.Runtime.Remoting.Messaging;

namespace StackExchange.Profiling
{
    /// <summary>
    ///     Async/await and thread-safe <see cref="LogicalCallContext" /> based singleton profiler
    ///     instance of a <see cref="MiniProfiler" /> to be the <see cref="MiniProfiler.Current" /> one.
    /// </summary>
    public class CallContextProfilerProvider : BaseProfilerProvider
    {
        private const string CallContextNameForHead = "miniprofiler_head";
        private const string CallContextNameForCurrentProfiler = "miniprofiler_current";

        /// <summary>
        ///     Gets the currently running MiniProfiler for the current <see cref="LogicalCallContext" />; null if no MiniProfiler
        ///     was <see cref="Start(string)" />ed.
        /// </summary>
        private MiniProfiler Current
        {
            get { return GetCurrentProfilerFromCallContext(); }
            set { SetCurrentProfilerToCallContext(value); }
        }

        /// <inheritdoc />
        public override MiniProfiler GetCurrentProfiler() => Current;

        /// <inheritdoc />
        public override Timing GetHead()
        {
            return (Timing)CallContext.LogicalGetData(CallContextNameForHead);
        }

        /// <inheritdoc />
        public override void SetHead(Timing t)
        {
            CallContext.LogicalSetData(CallContextNameForHead, t);
        }

        /// <inheritdoc />
        [Obsolete("Please use the Start(string sessionName) overload instead of this one. ProfileLevel is going away.")]
        public override MiniProfiler Start(ProfileLevel level, string sessionName = null)
        {
            var profiler = new MiniProfiler(sessionName ?? AppDomain.CurrentDomain.FriendlyName) {IsActive = true};
            SetCurrentProfilerToCallContext(profiler);
            return profiler;
        }

        /// <inheritdoc />
        public override MiniProfiler Start(string sessionName = null)
        {
#pragma warning disable CS0618 // Type or member is obsolete
            return Start(ProfileLevel.Info, sessionName);
#pragma warning restore CS0618 // Type or member is obsolete
        }

        /// <inheritdoc />
        public override void Stop(bool discardResults)
        {
            var current = Current;
            if (current == null)
                return;

            // stop our timings - when this is false, we've already called .Stop before on this session
            if (!StopProfiler(current))
                return;

            if (discardResults)
            {
                Current = null;
                return;
            }

            // save the profiler
            SaveProfiler(current);

            try
            {
                var arrayOfIds = MiniProfiler.Settings.Storage.GetUnviewedIds(current.User);

                if ((arrayOfIds != null) && (arrayOfIds.Count > MiniProfiler.Settings.MaxUnviewedProfiles))
                    foreach (var id in arrayOfIds.Take(arrayOfIds.Count - MiniProfiler.Settings.MaxUnviewedProfiles))
                        MiniProfiler.Settings.Storage.SetViewed(current.User, id);
            }
            catch
            {
                // ignored
            }
        }

        private static void SetCurrentProfilerToCallContext(MiniProfiler profiler)
        {
            CallContext.LogicalSetData(CallContextNameForCurrentProfiler, profiler);
        }

        private static MiniProfiler GetCurrentProfilerFromCallContext()
        {
            return CallContext.LogicalGetData(CallContextNameForCurrentProfiler) as MiniProfiler;
        }
    }
}