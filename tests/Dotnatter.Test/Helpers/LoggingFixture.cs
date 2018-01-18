using System;
using Serilog;
using Serilog.Events;
using Xunit.Abstractions;

namespace Dotnatter.Test.Helpers
{
    public class LoggingFixture : IDisposable
    {
        public LoggingFixture(ITestOutputHelper output)
        {
            Log.Logger = new LoggerConfiguration()
                .MinimumLevel.Verbose()
                .MinimumLevel.Override("Microsoft", LogEventLevel.Information)
                .Enrich.FromLogContext()
                .WriteTo.XunitTestOutput(output)
                .CreateLogger();
        }

        public void Dispose()
        {
           Log.CloseAndFlush();
        }

   
    }
}
