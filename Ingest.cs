using System;
using System.IO;

namespace HoverExplorer
{
    internal class IngestResult
    {
        public string Kind;    // file / url / text
        public string Target;  // 路径 / URL / 文本内容
        public string Title;   // 可选显示标题
    }

    // 统一入口：把任意来源（剪贴板/拖放/选中）的字符串分类成卡片可消费的结构
    internal static class Ingest
    {
        public static IngestResult Classify(string raw)
        {
            if (string.IsNullOrWhiteSpace(raw)) return null;
            raw = raw.Trim().Trim('"', '\'');
            if (raw.Length == 0) return null;
            // 多行文本：若含换行，整体作为 text 卡片（保留原文）
            if (raw.IndexOfAny(new[] { '\r', '\n' }) >= 0)
                return new IngestResult { Kind = "text", Target = raw, Title = FirstLine(raw) };

            if (File.Exists(raw) || Directory.Exists(raw))
                return new IngestResult { Kind = "file", Target = raw, Title = "" };

            if (IsUrl(raw))
                return new IngestResult { Kind = "url", Target = raw, Title = "" };

            return new IngestResult { Kind = "text", Target = raw, Title = "" };
        }

        private static bool IsUrl(string s)
        {
            if (string.IsNullOrEmpty(s)) return false;
            string low = s.ToLowerInvariant();
            if (low.StartsWith("http://") || low.StartsWith("https://") || low.StartsWith("ftp://")) return true;
            if (low.StartsWith("www.") && s.IndexOf('.') > 3) return true;
            return false;
        }

        // 提取域名用于 url 卡片副标题
        public static string DomainOf(string url)
        {
            try
            {
                var u = url.Trim();
                int scheme = u.IndexOf("://");
                if (scheme >= 0) u = u.Substring(scheme + 3);
                int slash = u.IndexOf('/');
                if (slash >= 0) u = u.Substring(0, slash);
                int at = u.IndexOf('@');
                if (at >= 0) u = u.Substring(at + 1);
                return u;
            }
            catch { return url; }
        }

        public static string FirstLine(string s)
        {
            if (string.IsNullOrEmpty(s)) return "";
            int i = s.IndexOfAny(new[] { '\r', '\n' });
            return i >= 0 ? s.Substring(0, i) : s;
        }

        public static string Ellipsis(string s, int max)
        {
            if (string.IsNullOrEmpty(s)) return "";
            return s.Length <= max ? s : s.Substring(0, max) + "…";
        }
    }
}