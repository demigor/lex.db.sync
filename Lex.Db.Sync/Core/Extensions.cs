using System;
using System.Xml;

namespace Lex.Db
{
  static class Extensions
  {
    public static DateTime? Max(this DateTime? a, DateTime? b)
    {
      if (a != null && (b == null || a > b))
        return a;

      return b;
    }

    public static DateTime? GetLastTs(this DbTable table) 
    {
      return table[DbSync.TsMetadata].StringToTs();
    }

    public static string TsToString(this DateTime? lastTs)
    {
      return lastTs != null ? XmlConvert.ToString(new DateTimeOffset(lastTs.Value)) : null;
    }

    public static DateTime? StringToTs(this string value)
    {
      if (!string.IsNullOrEmpty(value))
        return XmlConvert.ToDateTimeOffset(value).LocalDateTime;

      return null;
    }
  }
}
