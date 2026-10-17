using System;
using System.IO;

namespace HoverExplorer
{
    internal static class TestHarness
    {
        [STAThread]
        static int Main(string[] args)
        {
            if (args.Length < 1) { Console.WriteLine("用法: TestHarness <文件路径>..."); return 1; }
            int fail = 0;
            foreach (var path in args)
            {
                if (!File.Exists(path)) { Console.WriteLine("[跳过] 不存在: " + path); continue; }
                Console.Write("[测试] " + Path.GetFileName(path) + " ... ");
                try
                {
                    var r = PreviewProvider.Generate(path);
                    if (r == null) { Console.WriteLine("null"); fail++; continue; }
                    if (r.Kind == PreviewResult.KindEnum.Image)
                        Console.WriteLine("图片 " + r.Image.Width + "x" + r.Image.Height);
                    else
                    {
                        var lines = r.Text.Split(new[] { "\r\n" }, StringSplitOptions.None);
                        Console.WriteLine("文本 " + lines.Length + " 行");
                        foreach (var ln in lines)
                        {
                            var s = ln.Length > 90 ? ln.Substring(0, 90) + "..." : ln;
                            Console.WriteLine("  | " + s);
                        }
                    }
                }
                catch (Exception ex) { Console.WriteLine("异常: " + ex.Message); fail++; }
            }
            Console.WriteLine(fail == 0 ? "全部通过" : ("失败 " + fail));
            return fail;
        }
    }
}