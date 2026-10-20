using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using VSMarketplaceBadges.Services;
using Xunit;

namespace VSMarketplaceBadges.Tests
{
    /// <summary>
    /// <see cref="Startup.ConfigureServices"/> の登録だけで上流クライアントが組み立てられることを担保する。
    /// </summary>
    /// <remarks>
    /// <see cref="UpstreamCache"/> が解決できないとコントローラーの生成で失敗し、フォールバックより
    /// 手前で全バッジが 500 になる。以前は <c>AddMvc</c> が副作用で登録した <c>IDistributedCache</c> に
    /// 頼っていて、登録が目に見えなかった。
    /// </remarks>
    public class StartupServicesTests
    {
        [Fact]
        public void キャッシュが登録される()
        {
            using var provider = BuildServices();

            Assert.NotNull(provider.GetRequiredService<UpstreamCache>());
        }

        [Fact]
        public void 上流クライアントを解決できる()
        {
            using var provider = BuildServices();

            Assert.NotNull(provider.GetRequiredService<IVSMarketplaceService>());
            Assert.NotNull(provider.GetRequiredService<IShiledsIoService>());
        }

        private static ServiceProvider BuildServices()
        {
            var services = new ServiceCollection();
            services.AddLogging();
            new Startup(new ConfigurationBuilder().Build(), null).ConfigureServices(services);
            return services.BuildServiceProvider();
        }
    }
}
