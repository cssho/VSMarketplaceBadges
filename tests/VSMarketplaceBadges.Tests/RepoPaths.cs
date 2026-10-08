using System;
using System.IO;

namespace VSMarketplaceBadges.Tests
{
    /// <summary>リポジトリ内のファイルを直接読むテスト向けに、リポジトリのルートを探す。</summary>
    internal static class RepoPaths
    {
        public static readonly string Root = FindRepoRoot();

        private static string FindRepoRoot()
        {
            var dir = new DirectoryInfo(AppContext.BaseDirectory);
            while (dir != null && !File.Exists(Path.Combine(dir.FullName, "VSMarketplaceBadges.sln")))
                dir = dir.Parent;
            return dir?.FullName ?? throw new InvalidOperationException("VSMarketplaceBadges.sln が見つからない。");
        }
    }
}
