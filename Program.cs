using System;
using System.IO;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Serilog;
using Serilog.Events;
using Serilog.Formatting.Compact;
using Serilog.Core;

namespace VSMarketplaceBadges
{
    public class Program
    {
        public static int Main(string[] args)
        {
            string env = Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT");

            // シンクの障害を stderr に出す。stdout シンクのみになった現在も、Serilog 内部の
            // 設定ミス (フォーマッタ例外など) を黙って握りつぶさないために残している。
            Serilog.Debugging.SelfLog.Enable(Console.Error);

            // シンクは環境によらず stdout。Lambda / App Runner とも stdout をそのまま
            // CloudWatch Logs に転送するため、アプリ側が AWS SDK を持つ必要はない。
            // 保持期間とコストは CloudWatch のロググループ側 (terraform/main.tf) で制御する。
            Log.Logger = new LoggerConfiguration()
                .MinimumLevel.ControlledBy(new LoggingLevelSwitch(ResolveMinimumLevel(env)))
                .Enrich.FromLogContext()
                .WriteTo.Console(new RenderedCompactJsonFormatter())
                .CreateLogger();
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
    }
}
