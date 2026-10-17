using System;
using System.Collections.Generic;

namespace HoverExplorer
{
    internal static class TestI18n
    {
        [STAThread]
        static int Main()
        {
            var keys = new[]
            {
                "pin","preview","search","items","delete_n","copy_path_n","cancel_sel","open","new_window",
                "rename","delete","copy_path","copy_name","sort_asc","sort_desc","delete_confirm","delete_title",
                "explorer_hover","file_preview","exit","language","sort_name","sort_size","sort_date",
                "font_label","slide","office_no_text","total_items","no_bandizip","bz_failed","empty_archive",
                "file_label","desc_label","product_label","company_label","version_label","origfile_label",
                "type_label","size_label","managed_asm","native_dll","managed_exe","target_label","args_label",
                "workdir_label","icon_label","exists_label","yes","no","parse_failed","title_label",
                "author_label","lang_label","toc_header"
            };
            int fail = 0;
            foreach (var lang in I18n.LangCodes)
            {
                I18n.Lang = lang;
                Console.WriteLine("=== " + lang + " ===");
                foreach (var k in keys)
                {
                    string v = I18n.T(k, 5);
                    if (string.IsNullOrEmpty(v) || v == k) { Console.WriteLine("  缺失: " + k); fail++; }
                }
                Console.WriteLine("  " + keys.Length + " 键 OK");
            }
            Console.WriteLine(fail == 0 ? "全部通过" : ("失败 " + fail));
            return fail;
        }
    }
}