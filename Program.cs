using System;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Hosting;
using Serilog;
using Serilog.Events;
using Serilog.Sinks.GoogleCloudLogging;
using Utf8Json;
using Utf8Json.Resolvers;

namespace VSMarketplaceBadges
{
    public class Program
    {
        private static GoogleCloudLoggingSinkOptions gcLoggingConf;
        public static int Main(string[] args)
        {

            var projectId = Environment.GetEnvironmentVariable("GCP_PROJECT_ID");
            if (!string.IsNullOrEmpty(projectId))
                gcLoggingConf = new GoogleCloudLoggingSinkOptions { ProjectId = projectId, UseJsonOutput = true };


            var tmp = new LoggerConfiguration()
                .MinimumLevel.Debug()
                .MinimumLevel.Override("Microsoft", LogEventLevel.Information)
                .Enrich.FromLogContext();

            Log.Logger = (gcLoggingConf == null ? tmp.WriteTo.Console() : tmp.WriteTo.GoogleCloudLogging(gcLoggingConf))
                            .CreateLogger();
            JsonSerializer.SetDefaultResolver(StandardResolver.CamelCase);

            try
            {
                CreateHostBuilder(args).Build().Run();
                return 0;
            }
            catch (Exception ex)
            {
                Log.Fatal(ex, "Host terminated unexpectedly");
                return 1;
            }
            finally
            {
                Log.CloseAndFlush();
            }
        }

        public static IHostBuilder CreateHostBuilder(string[] args) =>
            Host.CreateDefaultBuilder(args)
                .UseSerilog()
                .ConfigureWebHostDefaults(webBuilder =>
                {
                    webBuilder.UseStartup<Startup>();
                });
    }
}
