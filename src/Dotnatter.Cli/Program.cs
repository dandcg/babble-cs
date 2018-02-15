using System;
using System.IO;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Serilog;

namespace Dotnatter.Cli
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
            // Add logging
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

            // Add access to generic IConfigurationRoot
            serviceCollection.AddSingleton<IConfigurationRoot>(configuration);

            // Add services
            //serviceCollection.AddTransient<IBackupService, BackupService>();

            // Add app
            serviceCollection.AddTransient<App>();
        }


    }
}
