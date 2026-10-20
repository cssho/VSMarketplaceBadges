using System;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using VSMarketplaceBadges.Entity;
using VSMarketplaceBadges.Services;
using Xunit;

namespace VSMarketplaceBadges.Tests
{
    /// <summary>
    /// 上流が応答ヘッダーだけ返して本文の途中で止まっても、外部呼び出しが時間予算
    /// (1 回 10 秒) で打ち切られることを担保する。
    /// </summary>
    /// <remarks>
    /// 本文の読み込みは Polly のポリシーの外で行われるため、HttpClient.Timeout を設定しないと
    /// 既定の 100 秒まで待ち続け、Lambda のタイムアウト (35 秒) に先に当たる。
    /// 2026-10-20 にこれで Lambda がタイムアウトし、エラーのアラームが発報した。
    /// </remarks>
    public class HttpClientTimeoutTests
    {
        // 予算 10 秒に対する許容幅。CI の揺らぎで落ちないよう余裕を持たせつつ、
        // 既定の 100 秒や Lambda の 35 秒とは明確に区別できる値にしている。
        private static readonly TimeSpan Budget = TimeSpan.FromSeconds(15);

        [Fact]
        public async Task shields_io_の本文が止まっても時間予算内に打ち切られる()
        {
            var shieldsIo = BuildServices().GetRequiredService<IShiledsIoService>();
            var request = new BadgeRequest { BadgeType = BadgeType.VersionShort, ImageExt = ImageExt.Svg };

            var sw = Stopwatch.StartNew();
            await Assert.ThrowsAnyAsync<Exception>(() => shieldsIo.LoadImage(request, "1.0.0", ""));

            Assert.True(sw.Elapsed < Budget, $"打ち切りまで {sw.Elapsed.TotalSeconds:F1} 秒かかった");
        }

        [Fact]
        public async Task Marketplace_の本文が止まっても時間予算内に打ち切られる()
        {
            var marketplace = BuildServices().GetRequiredService<IVSMarketplaceService>();

            // LoadVsmItemDataFromApi は失敗時にもう一度呼ぶので、予算はその 2 倍。
            var sw = Stopwatch.StartNew();
            await Assert.ThrowsAnyAsync<Exception>(() => marketplace.LoadVsmItemDataFromApi("publisher.extension"));

            Assert.True(sw.Elapsed < Budget * 2, $"打ち切りまで {sw.Elapsed.TotalSeconds:F1} 秒かかった");
        }

        /// <summary>
        /// 本番と同じ登録 (ポリシー・HttpClient の設定) のまま、通信部分だけを
        /// 本文が止まるハンドラーに差し替えたサービスを組み立てる。
        /// </summary>
        private static ServiceProvider BuildServices()
        {
            var services = new ServiceCollection();
            services.AddLogging();
            new Startup(new ConfigurationBuilder().Build(), null).ConfigureServices(services);

            services.AddHttpClient<IShiledsIoService, ShiledsIoService>()
                .ConfigurePrimaryHttpMessageHandler(() => new StalledBodyHandler());
            services.AddHttpClient<IVSMarketplaceService, VSMarketplaceService>()
                .ConfigurePrimaryHttpMessageHandler(() => new StalledBodyHandler());

            return services.BuildServiceProvider();
        }

        /// <summary>
        /// 200 と応答ヘッダーは即座に返すが、本文は 1 バイトも届かないハンドラー。
        /// </summary>
        private class StalledBodyHandler : HttpMessageHandler
        {
            protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
                Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StreamContent(new StalledStream())
                });
        }

        private class StalledStream : Stream
        {
            public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
            {
                await Task.Delay(Timeout.Infinite, cancellationToken);
                return 0;
            }

            public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken) =>
                ReadAsync(buffer.AsMemory(offset, count), cancellationToken).AsTask();

            public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
            public override bool CanRead => true;
            public override bool CanSeek => false;
            public override bool CanWrite => false;
            public override long Length => throw new NotSupportedException();
            public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
            public override void Flush() { }
            public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
            public override void SetLength(long value) => throw new NotSupportedException();
            public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        }
    }
}
