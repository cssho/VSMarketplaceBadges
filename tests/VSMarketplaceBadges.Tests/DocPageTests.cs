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
    /// wwwroot/index.html のバッジ一覧は、カードの data-type からバッジ URL を組み立てる。
    /// 一覧はページ内にしか無いので、バッジタイプを足したときに書き漏らしても他のテストでは
    /// 気づけない。ここで BadgeType の EnumMember と突き合わせる。
    /// </summary>
    public class DocPageTests
    {
        [Fact]
        public void BadgeCards_MatchEnumMembers()
        {
            var html = File.ReadAllText(Path.Combine(RepoPaths.Root, "wwwroot", "index.html"));
            var cards = Regex.Matches(html, @"<li\s+class=""badge-card""\s+data-type=""([^""]*)""")
                .Select(m => m.Groups[1].Value)
                .ToArray();

            var expected = Enum.GetValues(typeof(BadgeType)).Cast<BadgeType>()
                .Where(t => t != BadgeType.Unknown)
                .Select(t => t.ToEnumMember())
                .OrderBy(s => s, StringComparer.Ordinal);

            Assert.Equal(expected, cards.OrderBy(s => s, StringComparer.Ordinal));
        }
    }
}
