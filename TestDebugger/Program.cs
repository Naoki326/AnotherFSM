using System.Diagnostics;

namespace TestDebugger
{
    class SycnContext : SynchronizationContext
    {
        /// <inheritdoc />
        public override void Post(SendOrPostCallback d, object state)
        {
            var sc = SynchronizationContext.Current;
            Run = () => 
            {
                var sc = SynchronizationContext.Current;
                d(state);
            };
            Event.Set();
        }

        /// <inheritdoc />
        public override void Send(SendOrPostCallback d, object state)
        {
            // 用于了解执行完成
            AutoResetEvent autoResetEvent = new AutoResetEvent(false);
            Run = () =>
            {
                d(state);
                autoResetEvent.Set();
            };
            Event.Set();
            autoResetEvent.WaitOne();
        }

        public Action Run { private set; get; }

        public AutoResetEvent Event { get; } = new AutoResetEvent(false);
    }


    class Program
    {
        static void Main(string[] args)
        {
            var synchronizationContext = new SycnContext();

            SynchronizationContext.SetSynchronizationContext(synchronizationContext);

            Console.WriteLine($"{Thread.CurrentThread.ManagedThreadId}");
            Foo();
            Task.Factory.StartNew(() =>
            {
                SynchronizationContext.SetSynchronizationContext(synchronizationContext);
                while (true)
                {
                    synchronizationContext.Event.WaitOne();
                    synchronizationContext.Run();
                }
            });
            Console.ReadLine();
        }

        private static async void Foo()
        {
            var task = Task.Run(async () =>
            {
                await Task.Delay(100);
                Console.WriteLine($"{Thread.CurrentThread.ManagedThreadId}");
            });
            await task;
            var sc = SynchronizationContext.Current;
            Console.WriteLine($"{Thread.CurrentThread.ManagedThreadId}");
            await Task.Delay(100);
            var sc2 = SynchronizationContext.Current;
            Console.WriteLine($"{Thread.CurrentThread.ManagedThreadId}");
            await Task.Delay(100);
            var sc3 = SynchronizationContext.Current;
            Console.WriteLine($"{Thread.CurrentThread.ManagedThreadId}");
        }
    }
}
