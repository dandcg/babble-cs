using Serilog;

namespace Babble.Core.Util
{
    public static class LoggingExtensions
    {
        public static ILogger AddNamedContext(this ILogger logger, string name, string instanceName=null)
        {
            var l= logger.ForContext("SourceContext", name);

            if (instanceName != null)
            {
                l=l.ForContext("InstanceName", instanceName);
            }

            return l;
        }
    }
}
