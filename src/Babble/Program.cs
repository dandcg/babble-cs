using System;
using System.IO;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Serilog;

namespace Babble
{

    class Program
    {
        public static IConfigurationRoot configuration;

        static void Main(string[] args)
        {

            // Create service collection
            IServiceCollection serviceCollection = new ServiceCollection();
            ConfigureServices(serviceCollection);

            // Create service provider
            IServiceProvider serviceProvider = serviceCollection.BuildServiceProvider();

            // Run app
            serviceProvider.GetService<App>().Run();

        }

        private static void ConfigureServices(IServiceCollection serviceCollection)
        {
            // Set logging
            serviceCollection.AddSingleton(new LoggerFactory()
                .AddConsole()
                .AddSerilog()
                .AddDebug());
            serviceCollection.AddLogging();

            // Build configuration
            configuration = new ConfigurationBuilder()
                .SetBasePath(Directory.GetParent(AppContext.BaseDirectory).FullName)
                .AddJsonFile("appsettings.json", false)
                .Build();


        

            // Initialize serilog logger
            Log.Logger = new LoggerConfiguration()
                 .WriteTo.Console()
                 .MinimumLevel.Verbose()
                 .Enrich.FromLogContext()
                 .CreateLogger();

            // Set access to generic IConfigurationRoot
            serviceCollection.AddSingleton<IConfigurationRoot>(configuration);

            // Set services
            //serviceCollection.AddTransient<IBackupService, BackupService>();

            // Set app
            serviceCollection.AddTransient<App>();
        }


    }
}
