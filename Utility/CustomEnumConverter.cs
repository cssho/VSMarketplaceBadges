using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Globalization;
using System.Linq;
using VSMarketplaceBadges.Entity;

namespace VSMarketplaceBadges.Utility
{
    public class CustomEnumConverter<T> : TypeConverter where T : struct
    {
        static CustomEnumConverter()
        {
            cache.Add(typeof(BadgeType), CreateEnumDictionary<BadgeType>());
            cache.Add(typeof(ImageExt), CreateEnumDictionary<ImageExt>());
        }
        private static readonly Dictionary<Type, Dictionary<string, Enum>> cache
            = new Dictionary<Type, Dictionary<string, Enum>>();
        public override bool CanConvertFrom(ITypeDescriptorContext context, Type sourceType)
            => sourceType == typeof(string);

        public override object ConvertFrom(ITypeDescriptorContext context, CultureInfo culture, object value)
            => cache[typeof(T)].GetValueOrDefault((value as string) ?? "");

        private static Dictionary<string, Enum> CreateEnumDictionary<EnumType>()
        {
            return System.Enum.GetValues(typeof(EnumType)).Cast<Enum>().ToDictionary(t => t.ToEnumMember() ?? "", t => t);
        }
    }
}