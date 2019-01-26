﻿using System;
using Serilog;
using Serilog.Configuration;
using Serilog.Events;
using Serilog.Formatting.Display;
using Xunit.Abstractions;
using Xunit.Sdk;

namespace Babble.Test.Utils
{
    /// <summary>
    /// Adds the WriteTo.XunitTestOutput(output) extension method to <see cref="LoggerConfiguration"/>.
    /// </summary>
    public static class LoggerConfigurationXunitTestOutputExtensions
    {
        const string DefaultOutputTemplate = "[{Timestamp:HH:mm:ss.ffff} {Level:u3}] {SourceContext}({InstanceName}) {Message:lj}{Exception}";

        /// <summary>
        /// Adds a sink that writes log events to the output of an xUnit test.
        /// </summary>
        /// <param name="loggerConfiguration">The logger configuration.</param>
        /// <param name="testOutputHelper">Xunit <see cref="TestOutputHelper"/> that writes to test output</param>
        /// <param name="restrictedToMinimumLevel">The minimum log event level required in order to write an event to the sink.</param>
        /// <param name="outputTemplate">Message template describing the output format.</param>
        /// <param name="formatProvider">Supplies culture-specific formatting information, or null.</param>
        /// <returns>Logger configuration, allowing configuration to continue.</returns>
        /// <exception cref="ArgumentNullException">A required parameter is null.</exception>
        public static LoggerConfiguration XunitTestOutput(
            this LoggerSinkConfiguration loggerConfiguration,
            ITestOutputHelper testOutputHelper,
            LogEventLevel restrictedToMinimumLevel = LevelAlias.Minimum,
            string outputTemplate = DefaultOutputTemplate,
            IFormatProvider formatProvider = null)
        {
            if (loggerConfiguration == null) throw new ArgumentNullException(nameof(loggerConfiguration));
            if (testOutputHelper == null) throw new ArgumentNullException(nameof(testOutputHelper));
            if (outputTemplate == null) throw new ArgumentNullException(nameof(outputTemplate));

            var formatter = new MessageTemplateTextFormatter(outputTemplate, formatProvider);
            return loggerConfiguration.Sink(new XUnitTestOutputSink(testOutputHelper, formatter), restrictedToMinimumLevel);
        }
    }
}