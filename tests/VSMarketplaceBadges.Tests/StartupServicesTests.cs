using Microsoft.Extensions.Caching.Distributed;
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
    /// <c>IDistributedCache</c> が解決できないとコントローラーの生成で失敗し、フォールバックより
    /// 手前で全バッジが 500 になる。<c>AddMvc</c> の副作用に頼らず明示登録していることを
    /// 外から確かめる手段がないので、解決できること自体を検査する。
    /// </remarks>
    public class StartupServicesTests
    {
        [Fact]
        public void キャッシュはメモリ上の実装で登録される()
        {
            using var provider = BuildServices();

            Assert.IsType<MemoryDistributedCache>(provider.GetRequiredService<IDistributedCache>());
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
