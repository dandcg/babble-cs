using System;
using System.IO;
using Serilog.Core;
using Serilog.Events;
using Serilog.Formatting;
using Xunit.Abstractions;

namespace Dotnatter.Test.Helpers
{
    public class XUnitTestOutputSink: ILogEventSink
    {
        readonly ITestOutputHelper output;
        readonly ITextFormatter textFormatter;

        public XUnitTestOutputSink(ITestOutputHelper testOutputHelper, ITextFormatter textFormatter)
        {
            if (testOutputHelper == null) throw new ArgumentNullException("testOutputHelper");
            if (textFormatter == null) throw new ArgumentNullException("textFormatter");

            output = testOutputHelper;
            this.textFormatter = textFormatter;
        }

        public void Emit(LogEvent logEvent)
        {
            if (logEvent == null) throw new ArgumentNullException("logEvent");

            var renderSpace = new StringWriter();
            textFormatter.Format(logEvent, renderSpace);
            output.WriteLine(renderSpace.ToString());
        }
    }
}
