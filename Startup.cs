using System;
using System.Net;
using System.Net.Http;
using Amazon.Lambda.AspNetCoreServer.Hosting;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Polly;
using Polly.Extensions.Http;
using Polly.Timeout;
using Serilog;
using VSMarketplaceBadges.Formatters;
using VSMarketplaceBadges.Services;
using VSMarketplaceBadges.Middlewares;

namespace VSMarketplaceBadges
{
    public class Startup
    {
        private readonly IWebHostEnvironment environment;

        public Startup(IConfiguration configuration, IWebHostEnvironment env)
        {
            Configuration = configuration;
            this.environment = env;
        }

        public IConfiguration Configuration { get; }

        // このメソッドはランタイムから呼ばれる。DI コンテナへのサービス登録はここで行う。
        public void ConfigureServices(IServiceCollection services)
        {
            services.AddControllers()
            .ConfigureApiBehaviorOptions(options => { options.SuppressInferBindingSourcesForParameters = true; });

            // Lambda 上では Kestrel の代わりに Lambda ランタイム API を待ち受けるサーバーに差し替わる。
            // 判定は環境変数 AWS_LAMBDA_FUNCTION_NAME の有無なので、ローカル実行や App Runner では
            // 何も起きない。Function URL のペイロードは v2 形式なので HttpApi を指定する。
            services.AddAWSLambdaHosting(LambdaEventSource.HttpApi);
            RegisterSnapshotWarmup(services);

            services.AddHttpClient<IVSMarketplaceService, VSMarketplaceService>(x =>
            {
                x.BaseAddress = new Uri("https://marketplace.visualstudio.com");
                x.DefaultRequestHeaders.Add("UserAgent", "VSMarketplaceBadges/2.0");
                x.DefaultRequestHeaders.Add("Accept", "application/json;api-version=3.0-preview.1");
            }).ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler
            {
                AutomaticDecompression = DecompressionMethods.Deflate | DecompressionMethods.GZip
            }).AddPolicyHandler(TotalTimeoutPolicy())
                .AddPolicyHandler(RetryPolicy())
                .AddPolicyHandler(PerAttemptTimeoutPolicy());

            services.AddHttpClient<IShiledsIoService, ShiledsIoService>(x =>
                {
                    x.BaseAddress = new Uri("https://img.shields.io");
                    x.DefaultRequestHeaders.Add("UserAgent", "VSMarketplaceBadges/2.0");
                }).ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler
                {
                    AutomaticDecompression = DecompressionMethods.Deflate | DecompressionMethods.GZip
                }).AddPolicyHandler(TotalTimeoutPolicy())
                .AddPolicyHandler(RetryPolicy())
                .AddPolicyHandler(PerAttemptTimeoutPolicy());
            // 上流障害時の代替バッジ。wwwroot から一度だけ読むので Singleton。
            services.AddSingleton<IFallbackBadgeService>(sp => new FallbackBadgeService(
                sp.GetRequiredService<IWebHostEnvironment>().WebRootFileProvider,
                sp.GetRequiredService<ILogger<FallbackBadgeService>>()));

            services.AddResponseCaching();

            services.AddMvc(options =>
            {
                options.OutputFormatters.Insert(0, new ImageOutputFormatter());
            });
        }

        /// <summary>
        /// SnapStart のスナップショットを取る前に流しておくリクエスト。
        /// </summary>
        /// <remarks>
        /// SnapStart は「初期化のやり直し」を省くだけで、.NET のティアード コンパイルまでは
        /// 肩代わりしない。実測でも復元自体は速い (Restore 752ms) のに復元直後の実行が
        /// 1177ms かかっており、JIT が残っていた。ここでパイプラインを一度通しておくと、
        /// JIT 済みのコードごとスナップショットに焼き込める。
        ///
        /// 流すのは**外部通信を伴わない経路だけ**にしている。実在するバッジの URL を
        /// 温めると、
        ///   1. スナップショット作成が Marketplace / shields.io の生存に依存し、
        ///      上流障害がそのままデプロイ失敗 (バージョン発行の失敗) になる
        ///   2. プロセス内キャッシュに載った応答がスナップショットに焼き込まれ、
        ///      復元された全環境が同じ古いバッジを持って起動する
        /// という二つの副作用がある。割に合わないので温めない。
        ///
        /// 反復回数はランタイムに最適化を促すため。ただし INIT フェーズには 10 秒の上限が
        /// あるので、外部通信のない軽い経路に限る前提で増やしすぎないこと。
        /// https://aws.amazon.com/blogs/dotnet/blog-improving-snapstart-performance-in-net-lambdas/
        /// </remarks>
        private static void RegisterSnapshotWarmup(IServiceCollection services)
        {
            for (var i = 0; i < WarmupIterations; i++)
            {
                // 静的ファイル配信の経路 (wwwroot/index.html)。
                services.AddAWSLambdaBeforeSnapshotRequest(new HttpRequestMessage(HttpMethod.Get, "/"));

                // ルーティング → CustomEnumConverter によるモデルバインド → BadgeController の
                // 入り口までを通す。未知のバッジタイプなので 400 で折り返し、外部には出ない。
                services.AddAWSLambdaBeforeSnapshotRequest(
                    new HttpRequestMessage(HttpMethod.Get, "/warmup/warmup.svg"));
            }
        }

        private const int WarmupIterations = 8;

        // 外部呼び出しの時間予算。App Runner 時代は 4 回 × 3^n 秒 (最大 120 秒) 待ち、
        // タイムアウトも 5 分だったが、Lambda では待機時間がそのまま課金される。
        // ポリシーは登録順に外側から内側へ重なるので、3 層でこう効かせている:
        //
        //   TotalTimeoutPolicy (10 秒)   ← 何があってもここで打ち切るハードキャップ
        //     RetryPolicy (1 回, 1 秒待機)
        //       PerAttemptTimeoutPolicy (5 秒)
        //
        // 1 回の外部呼び出しは最悪 10 秒。VSMarketplaceService.LoadVsmItemDataFromApi は
        // 失敗時に CoreRequest をもう一度呼ぶため Marketplace 側は最悪 20 秒、shields.io の
        // 10 秒と合わせて 1 リクエスト最悪 30 秒。これが Lambda のタイムアウト (35 秒) と
        // CloudFront のオリジン応答タイムアウト (40 秒) の内側に収まるよう terraform 側と
        // 揃えてある。1 か所だけ変えないこと。
        //
        // 試行ごとを 5 秒にしているのは、Marketplace の extensionquery が flags=914 で
        // versions/files/statistics を丸ごと返す重いクエリで、コールドな拡張機能では
        // 数秒かかることが珍しくないため。短くしすぎると「上流が遅いだけ」で 500 になる。

        /// <summary>
        /// リトライ方針。<see cref="PerAttemptTimeoutPolicy"/> が投げる
        /// <see cref="TimeoutRejectedException"/> は <c>HttpRequestException</c> ではないため、
        /// <c>HandleTransientHttpError</c> だけでは捕まらずリトライされずに 500 まで抜ける。
        /// タイムアウトこそリトライしたいケースなので明示的に足している。
        /// </summary>
        private static IAsyncPolicy<HttpResponseMessage> RetryPolicy() =>
            HttpPolicyExtensions.HandleTransientHttpError()
                .OrResult(y => y.StatusCode == HttpStatusCode.NotFound)
                .Or<TimeoutRejectedException>()
                .WaitAndRetryAsync(1, y => TimeSpan.FromSeconds(y));

        /// <summary>
        /// 試行ごとのタイムアウト。<see cref="RetryPolicy"/> より後に登録することで内側に入り、
        /// リトライ 1 回ごとに適用される。
        /// </summary>
        private static IAsyncPolicy<HttpResponseMessage> PerAttemptTimeoutPolicy() =>
            Policy.TimeoutAsync<HttpResponseMessage>(TimeSpan.FromSeconds(5));

        /// <summary>
        /// 呼び出し 1 回あたりのハードキャップ。最も外側に登録し、リトライと待機を含めた
        /// 合計時間を打ち切る。試行ごとの秒数×回数を足し算で見積もるのをやめ、
        /// 上限をここ 1 か所で保証するためのもの。
        /// </summary>
        private static IAsyncPolicy<HttpResponseMessage> TotalTimeoutPolicy() =>
            Policy.TimeoutAsync<HttpResponseMessage>(TimeSpan.FromSeconds(10));

        // このメソッドはランタイムから呼ばれる。HTTP リクエストパイプラインの構成はここで行う。
        public void Configure(IApplicationBuilder app, IWebHostEnvironment env)
        {
            app.UseErrorHandling();
            if (env.IsDevelopment())
            {
                app.UseDeveloperExceptionPage();
            }

            // TLS 終端は CloudFront (および App Runner) が行い、オリジンへは常に HTTPS で届く。
            // Lambda Function URL には HTTP のリスナーが存在しないため UseHttpsRedirection は
            // 何も守らず、転送ヘッダーの解釈次第でリダイレクトループの種になるだけなので外した。

            app.UseRouting();

            app.UseEndpoints(endpoints =>
            {
                endpoints.MapControllers();
            });
            app.UseDefaultFiles();
            app.UseResponseCaching();
            app.UseStaticFiles();

            //app.UseSerilogRequestLogging();
        }
    }
}
