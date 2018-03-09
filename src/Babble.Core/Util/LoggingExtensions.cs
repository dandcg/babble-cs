using Serilog;

namespace Babble.Core.Util
{
    public static class LoggingExtensions
    {

        public static ILogger AddNamedContext(this ILogger logger, string name, string instanceName=null)
        {
            return logger.ForContext("SourceContext",instanceName==null ? name: $"{name} ({instanceName})");

        }
    }
}
