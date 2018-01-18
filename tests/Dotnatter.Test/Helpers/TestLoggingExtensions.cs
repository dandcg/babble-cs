using Serilog;
using Serilog.Events;
using Xunit.Abstractions;

namespace Dotnatter.Test.Helpers
{
    public static class TestLoggingExtensions
    {
        public static ILogger SetupLogging(this ITestOutputHelper output)
        {
            return Log.Logger = new LoggerConfiguration()
                .MinimumLevel.Verbose()
                .MinimumLevel.Override("Microsoft", LogEventLevel.Information)
                .Enrich.FromLogContext()
                .WriteTo.XunitTestOutput(output)
                .CreateLogger();
        }
    }
}