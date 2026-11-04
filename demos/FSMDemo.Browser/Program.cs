using System.Runtime.InteropServices.JavaScript;
using System.Runtime.Versioning;
using FSMDemo.Runtime;

return;

namespace FSMDemo.Browser
{
    [SupportedOSPlatform("browser")]
    public static partial class BrowserExports
    {
        private static RuntimeSession? _session;

        [JSImport("emit", "runtime-events")]
        private static partial void Emit(string json);

        [JSExport]
        public static void Initialize()
        {
            if (_session != null) return;
            _session = new RuntimeSession(new BrowserNodeFactory(), Emit);
        }

        [JSExport]
        public static Task<string> Invoke(string operation, string payload)
            => _session?.InvokeAsync(operation, payload)
                ?? Task.FromResult("{\"error\":\"Browser runtime is not initialized\"}");

        [JSExport]
        public static async Task Dispose()
        {
            var previous = _session;
            _session = null;
            if (previous != null) await previous.DisposeAsync();
        }
    }
}
