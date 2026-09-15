using VSMarketplaceBadges.Utility;
using Xunit;

namespace VSMarketplaceBadges.Tests
{
    public class LogSanitizerTests
    {
        [Fact]
        public void 制御文字を落とす()
        {
            // 改行を混ぜて偽のログ行を捏造される経路を塞ぐ。
            var actual = LogSanitizer.ForLog("publisher.ext\r\n[INF] fake log line");

            Assert.Equal("publisher.ext[INF] fake log line", actual);
        }

        [Fact]
        public void タブや_NUL_も落とす()
        {
            Assert.Equal("abc", LogSanitizer.ForLog("a\tb\0c"));
        }

        [Fact]
        public void 上限を超えたら切り詰めて省略記号を付ける()
        {
            var actual = LogSanitizer.ForLog(new string('A', 500));

            Assert.Equal(LogSanitizer.MaxLength + 1, actual.Length);
            Assert.EndsWith("…", actual);
        }

        [Fact]
        public void 上限ちょうどは切り詰めない()
        {
            var input = new string('A', LogSanitizer.MaxLength);

            var actual = LogSanitizer.ForLog(input);

            Assert.Equal(input, actual);
            Assert.DoesNotContain("…", actual);
        }

        [Fact]
        public void 通常の_itemName_はそのまま通す()
        {
            Assert.Equal("ms-dotnettools.csharp", LogSanitizer.ForLog("ms-dotnettools.csharp"));
        }

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        public void 空値はそのまま返す(string input)
        {
            Assert.Equal(input, LogSanitizer.ForLog(input));
        }
    }
}
