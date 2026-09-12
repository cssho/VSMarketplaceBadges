using VSMarketplaceBadges.Entity;
using Xunit;

namespace VSMarketplaceBadges.Tests
{
    /// <summary>
    /// Subject / Color は shields.io のパスに文字列連結されるため、素通しするとパスを
    /// 書き換えられる。実際 ?subject=../../badge/PWNED で任意の shields.io パスに到達できていた。
    /// </summary>
    public class BadgeRequestSanitizeTests
    {
        [Theory]
        [InlineData("../../badge/PWNED", "....badgePWNED")]
        [InlineData("a/b/c", "abc")]
        [InlineData("..\\..\\evil", "....evil")]
        [InlineData("x?y=1", "xy=1")]
        [InlineData("x#frag", "xfrag")]
        public void Subject_はパス構造を変える文字を落とす(string input, string expected)
        {
            var sut = new BadgeRequest { Subject = input };

            Assert.Equal(expected, sut.Subject);
        }

        [Theory]
        [InlineData("../../x", "....x")]
        [InlineData("green", "green")]
        public void Color_も同じ規則でサニタイズされる(string input, string expected)
        {
            var sut = new BadgeRequest { Color = input };

            Assert.Equal(expected, sut.Color);
        }

        [Fact]
        public void 制御文字は落とす()
        {
            var sut = new BadgeRequest { Subject = "a\r\nb\tc\0d" };

            Assert.Equal("abcd", sut.Subject);
        }

        [Fact]
        public void 長すぎる値は上限で切る()
        {
            var sut = new BadgeRequest { Subject = new string('A', 500) };

            Assert.Equal(128, sut.Subject.Length);
        }

        [Fact]
        public void 正常な値はそのまま通す()
        {
            // shields.io のエスケープ規則 (-- は -、_ は空白) を壊さないこと。
            var sut = new BadgeRequest { Subject = "trending--daily" };

            Assert.Equal("trending--daily", sut.Subject);
        }

        [Fact]
        public void BadgeType_が入れる既定値は二重エンコードされない()
        {
            // 既定値は URL エンコード済みのリテラルなので、サニタイズで壊れてはいけない。
            var sut = new BadgeRequest { BadgeType = BadgeType.Version };

            Assert.Equal("Visual%20Studio%20Marketplace", sut.Subject);
        }

        [Fact]
        public void Color_の既定値は_brightgreen()
        {
            Assert.Equal("brightgreen", new BadgeRequest().Color);
        }

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        public void 空値はそのまま返す(string input)
        {
            var sut = new BadgeRequest { Subject = input };

            Assert.Equal(input, sut.Subject);
        }
    }
}
