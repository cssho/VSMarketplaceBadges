using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace VSMarketplaceBadges.Utility
{
    /// <summary>
    /// バージョン文字列をセマンティックバージョニング (semver.org) の優先順位規則で比較する。
    /// 数値部はセグメントごとに数値として比較するため、1.10.0 &gt; 1.9.0 になる。
    /// プレリリース付きは同じ数値部の正式版より小さく、ビルドメタデータ (+ 以降) は無視する。
    /// 数値部が semver として解釈できないバージョン (日付形式やラベル付きなど) が混ざった場合は、
    /// 従来どおり序数 (ordinal) の文字列比較にフォールバックする。
    /// </summary>
    public sealed class SemanticVersionComparer : IComparer<string>
    {
        public static readonly SemanticVersionComparer Instance = new SemanticVersionComparer();

        public int Compare(string x, string y)
        {
            if (ReferenceEquals(x, y)) return 0;
            if (x is null) return -1;
            if (y is null) return 1;

            var left = Parse(x);
            var right = Parse(y);
            // どちらか一方でも解釈できなければ比較基準を揃えられないため序数比較に落とす。
            if (left is null || right is null) return string.CompareOrdinal(x, y);

            var coreResult = CompareCore(left.Value.Core, right.Value.Core);
            if (coreResult != 0) return coreResult;

            return ComparePreRelease(left.Value.PreRelease, right.Value.PreRelease);
        }

        private static (long[] Core, string[] PreRelease)? Parse(string version)
        {
            if (string.IsNullOrWhiteSpace(version)) return null;

            var value = version.Trim();
            // Marketplace は付けないが、"v1.2.3" 形式で渡されても解釈できるようにしておく。
            if (value[0] == 'v' || value[0] == 'V') value = value.Substring(1);

            // ビルドメタデータは優先順位に影響しない。
            var plus = value.IndexOf('+');
            if (plus >= 0) value = value.Substring(0, plus);

            var preRelease = Array.Empty<string>();
            var hyphen = value.IndexOf('-');
            if (hyphen >= 0)
            {
                preRelease = value.Substring(hyphen + 1).Split('.');
                value = value.Substring(0, hyphen);
                if (preRelease.Any(string.IsNullOrEmpty)) return null;
            }

            var parts = value.Split('.');
            var core = new long[parts.Length];
            for (var i = 0; i < parts.Length; i++)
            {
                // NumberStyles.None なので符号・空白・桁区切りを含むセグメントは解釈失敗になる。
                if (!long.TryParse(parts[i], NumberStyles.None, CultureInfo.InvariantCulture, out core[i]))
                    return null;
            }
            return (core, preRelease);
        }

        // Marketplace には "1.2.3.4" のような 4 桁もあるため、桁数の違いは 0 埋めして扱う。
        private static int CompareCore(long[] x, long[] y)
        {
            var length = Math.Max(x.Length, y.Length);
            for (var i = 0; i < length; i++)
            {
                var left = i < x.Length ? x[i] : 0;
                var right = i < y.Length ? y[i] : 0;
                if (left != right) return left.CompareTo(right);
            }
            return 0;
        }

        private static int ComparePreRelease(string[] x, string[] y)
        {
            if (x.Length == 0 && y.Length == 0) return 0;
            // プレリリースを持たない正式版のほうが優先順位が高い。
            if (x.Length == 0) return 1;
            if (y.Length == 0) return -1;

            var length = Math.Min(x.Length, y.Length);
            for (var i = 0; i < length; i++)
            {
                var result = CompareIdentifier(x[i], y[i]);
                if (result != 0) return result;
            }
            // ここまで等しければ、識別子の数が多いほうが優先順位が高い (1.0.0-alpha < 1.0.0-alpha.1)。
            return x.Length.CompareTo(y.Length);
        }

        private static int CompareIdentifier(string x, string y)
        {
            var xIsNumeric = long.TryParse(x, NumberStyles.None, CultureInfo.InvariantCulture, out var xValue);
            var yIsNumeric = long.TryParse(y, NumberStyles.None, CultureInfo.InvariantCulture, out var yValue);

            if (xIsNumeric && yIsNumeric) return xValue.CompareTo(yValue);
            // 数値の識別子は英数字の識別子より優先順位が低い。
            if (xIsNumeric) return -1;
            if (yIsNumeric) return 1;
            return string.CompareOrdinal(x, y);
        }
    }
}
