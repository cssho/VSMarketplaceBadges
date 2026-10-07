using System;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using VSMarketplaceBadges.Entity;
using VSMarketplaceBadges.Utility;
using Xunit;

namespace VSMarketplaceBadges.Tests
{
    /// <summary>
    /// terraform/functions/path-filter.js (CloudFront Functions) は許可リストに無いパスをエッジで 404 にする。
    /// リストはアプリ側の定義の写しなので、バッジタイプや wwwroot のファイルを足したときに更新を
    /// 忘れると、本番だけで静かに 404 になる。ここでその乖離をテスト時の失敗に変える。
    /// </summary>
    public class CloudFrontPathFilterTests
    {
        private static readonly string RepoRoot = FindRepoRoot();

        private static string FindRepoRoot()
        {
            var dir = new DirectoryInfo(AppContext.BaseDirectory);
            while (dir != null && !File.Exists(Path.Combine(dir.FullName, "VSMarketplaceBadges.sln")))
                dir = dir.Parent;
            return dir?.FullName ?? throw new InvalidOperationException("VSMarketplaceBadges.sln が見つからない。");
        }

        /// <summary>path-filter.js の `var NAME = [ '...', ... ];` から文字列リテラルを取り出す。</summary>
        private static string[] ReadJsArray(string name)
        {
            var js = File.ReadAllText(Path.Combine(RepoRoot, "terraform", "functions", "path-filter.js"));
            var block = Regex.Match(js, $@"var\s+{name}\s*=\s*\[(.*?)\];", RegexOptions.Singleline);
            Assert.True(block.Success, $"path-filter.js に {name} が見つからない。");
            return Regex.Matches(block.Groups[1].Value, @"'([^']*)'").Select(m => m.Groups[1].Value).ToArray();
        }

        [Fact]
        public void BadgeTypes_MatchEnumMembers()
        {
            var expected = Enum.GetValues(typeof(BadgeType)).Cast<BadgeType>()
                .Where(t => t != BadgeType.Unknown)
                .Select(t => t.ToEnumMember())
                .OrderBy(s => s, StringComparer.Ordinal);

            Assert.Equal(expected, ReadJsArray("BADGE_TYPES").OrderBy(s => s, StringComparer.Ordinal));
        }

        [Fact]
        public void StaticPaths_CoverEveryWwwrootFile()
        {
            var wwwroot = Path.Combine(RepoRoot, "wwwroot");
            var files = Directory.GetFiles(wwwroot, "*", SearchOption.AllDirectories)
                .Select(f => "/" + Path.GetRelativePath(wwwroot, f).Replace('\\', '/'));

            var allowed = ReadJsArray("STATIC_PATHS");
            foreach (var file in files)
                Assert.Contains(file, allowed);
        }

        [Fact]
        public void StaticPaths_PointAtExistingFiles()
        {
            var wwwroot = Path.Combine(RepoRoot, "wwwroot");
            foreach (var path in ReadJsArray("STATIC_PATHS").Where(p => p != "/"))
                Assert.True(File.Exists(Path.Combine(wwwroot, path.TrimStart('/'))),
                    $"STATIC_PATHS の {path} が wwwroot に存在しない。");
        }
    }
}
