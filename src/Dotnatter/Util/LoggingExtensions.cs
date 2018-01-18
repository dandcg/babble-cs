using System;
using System.Collections.Generic;
using System.Text;
using Serilog;

namespace Dotnatter.Util
{
    public static class LoggingExtensions
    {

        public static ILogger AddNamedContext(this ILogger logger, string name, string instanceName=null)
        {
            return logger.ForContext("SourceContext",instanceName==null ? name: $"{name} ({instanceName})");

        }
    }
}
