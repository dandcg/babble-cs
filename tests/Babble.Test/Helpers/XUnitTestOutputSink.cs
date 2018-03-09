using System;
using System.IO;
using Serilog.Core;
using Serilog.Events;
using Serilog.Formatting;
using Xunit.Abstractions;

namespace Babble.Test.Helpers
{
    public class XUnitTestOutputSink: ILogEventSink
    {
        private readonly ITestOutputHelper output;
        private readonly ITextFormatter textFormatter;

        public XUnitTestOutputSink(ITestOutputHelper testOutputHelper, ITextFormatter textFormatter)
        {
            output = testOutputHelper ?? throw new ArgumentNullException(nameof(testOutputHelper));
            this.textFormatter = textFormatter ?? throw new ArgumentNullException(nameof(textFormatter));
        }

        public void Emit(LogEvent logEvent)
        {
            if (logEvent == null) throw new ArgumentNullException(nameof(logEvent));

            var renderSpace = new StringWriter();
            textFormatter.Format(logEvent, renderSpace);
            output.WriteLine(renderSpace.ToString());
           
         

        }
    }
}
