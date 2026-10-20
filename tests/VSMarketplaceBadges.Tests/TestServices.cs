using System.Net.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using VSMarketplaceBadges.Services;

namespace VSMarketplaceBadges.Tests
{
    /// <summary>
    /// 本番と同じ <see cref="Startup.ConfigureServices"/> の登録でサービスを組み立てる。
    /// </summary>
    internal static class TestServices
    {
        /// <summary>
        /// ポリシーや HttpClient の設定は本番のまま、指定した上流の通信部分だけを差し替える。
        /// 返したプロバイダーは呼び出し側で破棄すること。
        /// </summary>
        public static ServiceProvider Build(HttpMessageHandler marketplace = null, HttpMessageHandler shieldsIo = null)
        {
            var services = new ServiceCollection();
            services.AddLogging();
            new Startup(new ConfigurationBuilder().Build(), null).ConfigureServices(services);

            if (marketplace != null)
                services.AddHttpClient<IVSMarketplaceService, VSMarketplaceService>()
                    .ConfigurePrimaryHttpMessageHandler(() => marketplace);
            if (shieldsIo != null)
                services.AddHttpClient<IShiledsIoService, ShiledsIoService>()
                    .ConfigurePrimaryHttpMessageHandler(() => shieldsIo);

            return services.BuildServiceProvider();
        }
    }
}
