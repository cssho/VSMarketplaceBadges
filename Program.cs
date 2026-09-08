using System;
using System.IO;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
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
            string env = Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT");

            // これがないと、シンクの障害 (IAM ロールの不備、バケットポリシー変更、リージョン障害) は
            // PeriodicBatchingSink に握りつぶされ、ログ転送が死んだままバッジ配信だけが続いてしまう。
            // SelfLog は 1.7.0 で削除された AmazonS3Options.FailureCallback の代替。
            Serilog.Debugging.SelfLog.Enable(Console.Error);

            var logConf = new LoggerConfiguration()
                .MinimumLevel.ControlledBy(new LoggingLevelSwitch(ResolveMinimumLevel(env)))
                .Enrich.FromLogContext();
            if (env == Microsoft.Extensions.Hosting.Environments.Development)
                logConf.WriteTo.Console(new RenderedCompactJsonFormatter());
            else if (env == Microsoft.Extensions.Hosting.Environments.Production)
                AmazonS3(logConf.WriteTo, "logs/app.log", "vsmarketplace-badges/logs", RegionEndpoint.APNortheast1);
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

        /// <summary>
        /// Serilog は ASP.NET Core が使う <c>Logging:LogLevel</c> セクションを無視するため、最小ログレベルは
        /// ここで <c>Serilog:MinimumLevel</c> (appsettings、または環境変数 <c>Serilog__MinimumLevel</c>) から
        /// 解決し、未設定なら環境ごとの既定値にフォールバックする。
        /// </summary>
        private static LogEventLevel ResolveMinimumLevel(string env)
        {
            var configuration = new ConfigurationBuilder()
                .SetBasePath(Directory.GetCurrentDirectory())
                .AddJsonFile("appsettings.json", optional: true, reloadOnChange: false)
                .AddJsonFile($"appsettings.{env}.json", optional: true, reloadOnChange: false)
                .AddEnvironmentVariables()
                .Build();

            if (Enum.TryParse<LogEventLevel>(configuration["Serilog:MinimumLevel"], ignoreCase: true, out var level))
                return level;

            return env == Microsoft.Extensions.Hosting.Environments.Development
                ? LogEventLevel.Debug
                : LogEventLevel.Information;
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
                RollingInterval = RollingInterval.Hour,
                Encoding = Encoding.UTF8,
                BucketPath = null
            };

            var amazonS3Sink = new AmazonS3Sink(options);

            var batchingOptions = new PeriodicBatchingSinkOptions
            {
                BatchSizeLimit = 5000,
                Period = TimeSpan.FromSeconds(5),
                EagerlyEmitFirstEvent = true,
                QueueLimit = 10000
            };

            var batchingSink = new PeriodicBatchingSink(amazonS3Sink, batchingOptions);
            return sinkConfiguration.Sink(batchingSink, LevelAlias.Minimum, null);
        }
    }
}
