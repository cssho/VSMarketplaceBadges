using System;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Hosting;
using Serilog;
using Serilog.Events;
using Serilog.Formatting.Compact;
using Amazon;
using Serilog.Configuration;
using Serilog.Core;
using System.Text;
using Serilog.Sinks.AmazonS3;
using RollingInterval = Serilog.Sinks.AmazonS3.RollingInterval;
using Serilog.Sinks.PeriodicBatching;

namespace VSMarketplaceBadges
{
    public class Program
    {
        public static int Main(string[] args)
        {
            var logConf = new LoggerConfiguration()
                .MinimumLevel.Information()
                .Enrich.FromLogContext();
            string env = Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT");
            // if (env == Microsoft.Extensions.Hosting.Environments.Development)
            //     logConf.WriteTo.Console(new RenderedCompactJsonFormatter());
            // else if (env == Microsoft.Extensions.Hosting.Environments.Production)
            // {
            //     logConf.WriteTo.Console(new RenderedCompactJsonFormatter());
            //     AmazonS3(logConf.WriteTo, "logs", "vsmarketplcae-badges", RegionEndpoint.APNortheast1);
            // }
            logConf.WriteTo.Console(new RenderedCompactJsonFormatter());
            Log.Logger = logConf.CreateLogger();
            Log.Information($"env:{env}");
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

        public static LoggerConfiguration AmazonS3(LoggerSinkConfiguration sinkConfiguration, string path, string bucketName, RegionEndpoint endpoint)
        {


            var options = new AmazonS3Options
            {
                Path = path,
                BucketName = bucketName,
                Endpoint = endpoint,
                OutputTemplate = "{Timestamp:yyyy-MM-dd HH:mm:ss.fff zzz} [{Level:u3}] {Message:lj}{NewLine}{Exception}",
                FormatProvider = null,
                RollingInterval = RollingInterval.Day,
                Encoding = Encoding.UTF8,
                FailureCallback = null,
                BucketPath = null
            };

            var amazonS3Sink = new AmazonS3Sink(options);

            var batchingOptions = new PeriodicBatchingSinkOptions
            {
                BatchSizeLimit = 100,
                Period = TimeSpan.FromSeconds(2),
                EagerlyEmitFirstEvent = true,
                QueueLimit = 10000
            };

            var batchingSink = new PeriodicBatchingSink(amazonS3Sink, batchingOptions);
            return sinkConfiguration.Sink(batchingSink, LevelAlias.Minimum, null);
        }
    }
}
