using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using NUnit.Framework.Constraints;
using StackExchange.Profiling.Helpers;

namespace StackExchange.Profiling.Tests
{
    [TestFixture]
    public class CallContextProfilerTest
    {
        private IProfilerProvider _previousProvider;
        private Func<IStopwatch> _previousStopwatchProvider;

        [TestFixtureSetUp]
        public void SetupOnce()
        {
            _previousProvider = MiniProfiler.Settings.ProfilerProvider;
            _previousStopwatchProvider = MiniProfiler.Settings.StopwatchProvider;

            MiniProfiler.Settings.ProfilerProvider = new CallContextProfilerProvider();
        }

        [TestFixtureTearDown]
        public void TearDownOnce()
        {
            // Restore provider to avoid polluting other tests
            MiniProfiler.Settings.ProfilerProvider = _previousProvider;
            MiniProfiler.Settings.StopwatchProvider = _previousStopwatchProvider;
        }

        [Test]
        public async Task Step_WithParallelTasksAndRealTime()
        {
            MiniProfiler.Settings.StopwatchProvider = StopwatchWrapper.StartNew;
            var profiler = MiniProfiler.Start("root");

            Timing timing10 = null, timing11 = null, timing20 = null, timing21 = null, timing30 = null, timing31 = null;

            // Act

            // Add 100ms to root
            await Task.Delay(100);

            // Start tasks in parallel
            var whenAllTask = Task.WhenAll(
                Task.Run(async () =>
                {
                    using (timing10 = (Timing) profiler.Step("step1.0 (Task.Run)"))
                    {
                        await Task.Delay(100);
                        await Task.Run(async () =>
                        {
                            using (timing11 = (Timing) profiler.Step("step1.1 (Task.Run)"))
                            {
                                await Task.Delay(100);
                            }
                        });
                    }
                }),
                Task.Factory.StartNew(async () =>
                {
                    using (timing20 = (Timing) profiler.Step("step2.0 (Task.Factory.StartNew)"))
                    {
                        await Task.Delay(200);
                        await Task.Run(async () =>
                        {
                            using (timing21 = (Timing) profiler.Step("step2.1 (Task.Run)"))
                            {
                                await Task.Delay(100);
                            }
                        });
                    }
                    // Important to Unwrap() when using the not-for-mortals StartNew()
                }).Unwrap(),
                Task.Factory.StartNew(async () =>
                {
                    using (timing30 = (Timing) profiler.Step("step3.0 (Task.Factory.StartNew:LongRunning)"))
                    {
                        await Task.Delay(300);
                        await Task.Run(async () =>
                        {
                            using (timing31 = (Timing) profiler.Step("step3.1 (Task.Run)"))
                            {
                                await Task.Delay(100);
                            }
                        });
                    }
                    // Important to Unwrap() when using the not-for-mortals StartNew()
                }, TaskCreationOptions.LongRunning).Unwrap()
            );

            await whenAllTask;

            MiniProfiler.Stop();

            // Assert
            Console.WriteLine(profiler.RenderPlainText());

            // 100ms + longest running task (step3.0) = 500ms
            Assert.That(profiler.DurationMilliseconds, IsNear(500, 50), "Total duration (ms)");

            // Parent durations are sum of itself and children
            Assert.That(timing10.DurationMilliseconds, IsNear(200, 50), "Step1.0");
            Assert.That(timing11.DurationMilliseconds, IsNear(100, 50), "Step1.1");

            Assert.That(timing20.DurationMilliseconds, IsNear(300, 50), "Step2.0");
            Assert.That(timing21.DurationMilliseconds, IsNear(100, 50), "Step2.1");

            Assert.That(timing30.DurationMilliseconds, IsNear(400, 50), "Step3.0");
            Assert.That(timing31.DurationMilliseconds, IsNear(100, 50), "Step3.1");
        }

        [Test]
        public void Step_WithParallelThreadsAndRealTime()
        {
            MiniProfiler.Settings.StopwatchProvider = StopwatchWrapper.StartNew;
            var profiler = MiniProfiler.Start("root");

            // Act

            // Add 100ms to root just to offset the starting point
            Task.Delay(100).Wait();

            Parallel.For(0, 10, i =>
            {
                using (profiler.Step($"thread[{i}]"))
                {
                    foreach (int j in Enumerable.Range(0, 10))
//                    Parallel.For(0, 10, j =>
                    {
                        using (profiler.Step($"work[{i}/{j}]"))
                        {
                            Thread.Sleep(50);
                        }
                    }
                }
            });

            MiniProfiler.Stop();

            // Assert
            Console.WriteLine(profiler.RenderPlainText());

            Assert.That(profiler.DurationMilliseconds, IsNear(1000, 200), "Total duration (ms)");
            foreach (var timing in profiler.GetTimingHierarchy())
            {

                if (timing.Name.StartsWith("thread"))
                {
                    // 10 work items, 50 ms each
                    Assert.That(timing.DurationMilliseconds, IsNear(500, 20));
                }
                else if (timing.Name.StartsWith("work"))
                {
                    // 50 ms each work item
                    Assert.That(timing.DurationMilliseconds, IsNear(50, 20));
                }
            }
        }

        [Test]
        public async Task Step_WithParallelTasksAndSimulatedTime()
        {
            MiniProfiler.Settings.StopwatchProvider = () => new UnitTestStopwatch();
            var profiler = MiniProfiler.Start("root");

            var waiters = new ConcurrentBag<CountdownEvent>();
            Timing timing10 = null, timing11 = null, timing20 = null, timing21 = null, timing30 = null, timing31 = null;

            // Act

            // Add 1ms to root
            BaseTest.IncrementStopwatch();

            // Start tasks in parallel
            var whenAllTask = Task.WhenAll(
                Task.Run(async () =>
                {
                    using (timing10 = (Timing) profiler.Step("step1.0 (Task.Run)"))
                    {
                        var ce = new CountdownEvent(1);
                        waiters.Add(ce);
                        ce.Wait();

                        await Task.Run(() =>
                        {
                            using (timing11 = (Timing) profiler.Step("step1.1 (Task.Run)"))
                            {
                                var ce2 = new CountdownEvent(1);
                                waiters.Add(ce2);
                                ce2.Wait();
                            }
                        });
                    }
                }),
                Task.Factory.StartNew(async () =>
                {
                    using (timing20 = (Timing) profiler.Step("step2.0 (Task.Factory.StartNew)"))
                    {
                        var ce = new CountdownEvent(2);
                        waiters.Add(ce);
                        ce.Wait();

                        await Task.Run(() =>
                        {
                            using (timing21 = (Timing) profiler.Step("step2.1 (Task.Run)"))
                            {
                                var ce2 = new CountdownEvent(1);
                                waiters.Add(ce2);
                                ce2.Wait();
                            }
                        });
                    }
                }),
                Task.Factory.StartNew(async () =>
                {
                    using (timing30 = (Timing) profiler.Step("step3.0 (Task.Factory.StartNew:LongRunning)"))
                    {
                        var ce = new CountdownEvent(3);
                        waiters.Add(ce);
                        ce.Wait();

                        await Task.Run(() =>
                        {
                            using (timing31 = (Timing) profiler.Step("step3.1 (Task.Run)"))
                            {
                                var ce2 = new CountdownEvent(1);
                                waiters.Add(ce2);
                                ce2.Wait();
                            }
                        });
                    }
                }, TaskCreationOptions.LongRunning)
            );

            Func<List<CountdownEvent>, bool> hasPendingTasks =
                handlers2 => (handlers2.Count == 0) || handlers2.Any(y => !y.IsSet);

            // TODO Make this a thread safe signaling lock step to avoid sleeping
            // Wait for tasks to run and call their Step() methods
            Thread.Sleep(50);

            List<CountdownEvent> handlers;
            while (hasPendingTasks(handlers = waiters.ToList()))
            {
                BaseTest.IncrementStopwatch();
                handlers.ForEach(x =>
                {
                    if (!x.IsSet) x.Signal();
                });

                // TODO Make this a thread safe signaling lock step to avoid sleeping
                // Wait for sub-tasks to run and call their Step() methods
                Thread.Sleep(50);
            }

            await whenAllTask;

            MiniProfiler.Stop();

            // Assert
            Console.WriteLine(profiler.RenderPlainText());

            // 1ms added to root
            Assert.AreEqual(5, profiler.DurationMilliseconds, "Total duration (ms)");

            // Parent durations are sum of itself and children
            Assert.AreEqual(2, timing10.DurationMilliseconds, "Step1.0");
            Assert.AreEqual(1, timing11.DurationMilliseconds, "Step1.1");

            Assert.AreEqual(3, timing20.DurationMilliseconds, "Step2.0");
            Assert.AreEqual(1, timing21.DurationMilliseconds, "Step2.1");

            Assert.AreEqual(4, timing30.DurationMilliseconds, "Step3.0");
            Assert.AreEqual(1, timing31.DurationMilliseconds, "Step3.1");
        }

        private static IResolveConstraint IsNear(int actual, int maxError)
        {
            return Is.InRange(actual - maxError, actual + maxError);
        }
    }
}