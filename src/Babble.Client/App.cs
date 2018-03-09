using Microsoft.Extensions.CommandLineUtils;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace Babble.Cli
{
    public class App
    {

        private readonly ILogger logger;
        private readonly IConfigurationRoot config;

        public App(IConfigurationRoot config, ILogger logger)
        {

            this.logger = logger;
            this.config = config;
        }

        public void Run()
        {
            logger.LogInformation("Running application.");
            System.Console.ReadKey();

            var app = new CommandLineApplication();
            app.Name = "ninja";
            app.HelpOption("-?|-h|--help");



        }

    }
}
