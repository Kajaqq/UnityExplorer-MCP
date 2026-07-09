using System;
using System.Collections.Concurrent;
using System.Threading;
#if INTEROP
using System.Threading.Tasks;
#endif

#nullable enable

namespace UnityExplorer.Mcp
{
#if INTEROP
    internal static class MainThread
    {
        private const int MaxPumpActionsPerFrame = 64;
        private static readonly ConcurrentQueue<Action> Queue = new();
        private static int _mainThreadId;

        public static void Capture()
        {
            _mainThreadId = Thread.CurrentThread.ManagedThreadId;
        }

        public static bool IsCaptured => _mainThreadId != 0;

        public static void Pump()
        {
            _mainThreadId = Thread.CurrentThread.ManagedThreadId;

            for (var i = 0; i < MaxPumpActionsPerFrame && Queue.TryDequeue(out var action); i++)
                action();
        }

        public static Task Run(Action action)
        {
            if (IsMainThread)
            {
                action();
                return Task.CompletedTask;
            }

            var tcs = CreateCompletionSource();
            Queue.Enqueue(() => Execute(action, tcs));
            return tcs.Task;
        }

        public static Task<T> Run<T>(Func<T> func)
        {
            if (IsMainThread)
            {
                return Task.FromResult(func());
            }

            var tcs = CreateCompletionSource<T>();
            Queue.Enqueue(() => Execute(func, tcs));
            return tcs.Task;
        }

        public static Task<T> RunAsync<T>(Func<Task<T>> func)
        {
            if (IsMainThread)
            {
                return func();
            }

            var tcs = CreateCompletionSource<T>();
            Queue.Enqueue(() => _ = ExecuteAsync(func, tcs));
            return tcs.Task;
        }

        public static Task RunAsync(Func<Task> func)
        {
            if (IsMainThread)
            {
                return func();
            }

            var tcs = CreateCompletionSource();
            Queue.Enqueue(() => _ = ExecuteAsync(func, tcs));
            return tcs.Task;
        }

        private static bool IsMainThread
            => _mainThreadId != 0 && Thread.CurrentThread.ManagedThreadId == _mainThreadId;

        private static TaskCompletionSource<object?> CreateCompletionSource()
            => new(TaskCreationOptions.RunContinuationsAsynchronously);

        private static TaskCompletionSource<T> CreateCompletionSource<T>()
            => new(TaskCreationOptions.RunContinuationsAsynchronously);

        private static void Execute(Action action, TaskCompletionSource<object?> tcs)
        {
            try
            {
                action();
                tcs.TrySetResult(null);
            }
            catch (Exception ex)
            {
                tcs.TrySetException(ex);
            }
        }

        private static void Execute<T>(Func<T> func, TaskCompletionSource<T> tcs)
        {
            try
            {
                tcs.TrySetResult(func());
            }
            catch (Exception ex)
            {
                tcs.TrySetException(ex);
            }
        }

        private static async Task ExecuteAsync(Func<Task> func, TaskCompletionSource<object?> tcs)
        {
            try
            {
                await func().ConfigureAwait(false);
                tcs.TrySetResult(null);
            }
            catch (Exception ex)
            {
                tcs.TrySetException(ex);
            }
        }

        private static async Task ExecuteAsync<T>(Func<Task<T>> func, TaskCompletionSource<T> tcs)
        {
            try
            {
                var result = await func().ConfigureAwait(false);
                tcs.TrySetResult(result);
            }
            catch (Exception ex)
            {
                tcs.TrySetException(ex);
            }
        }

    }
#elif MONO
    internal static class MainThread
    {
        private static SynchronizationContext? _context;

        public static void Capture()
        {
            _context = SynchronizationContext.Current;
        }

        public static bool IsCaptured => _context != null;

        public static void Run(Action action)
        {
            if (action == null) return;
            if (_context == null)
            {
                action();
                return;
            }
            if (SynchronizationContext.Current == _context)
            {
                action();
                return;
            }

            Exception? ex = null;
            using (var done = new ManualResetEvent(false))
            {
                _context.Post(_ =>
                {
                    try { action(); }
                    catch (Exception e) { ex = e; }
                    finally { done.Set(); }
                }, null);
                done.WaitOne();
            }
            if (ex != null) throw ex;
        }

        public static T Run<T>(Func<T> func)
        {
            if (func == null) return default!;
            if (_context == null)
            {
                return func();
            }
            if (SynchronizationContext.Current == _context)
            {
                return func();
            }

            T result = default!;
            Exception? ex = null;
            using (var done = new ManualResetEvent(false))
            {
                _context.Post(_ =>
                {
                    try { result = func(); }
                    catch (Exception e) { ex = e; }
                    finally { done.Set(); }
                }, null);
                done.WaitOne();
            }
            if (ex != null) throw ex;
            return result;
        }

        public static void RunAsync(Action action)
        {
            ThreadPool.QueueUserWorkItem(_ => Run(action));
        }
    }
#else
    internal static class MainThread
    {
        public static void Capture() { }
        public static bool IsCaptured => false;
        public static void Run(Action action) { action(); }
        public static T Run<T>(Func<T> func) => func();
        public static void RunAsync(Action action) { action(); }
    }
#endif
}
