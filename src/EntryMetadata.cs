using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace AgitCon {
    // Only public pack titles/filenames and aliases explicitly entered by the user.
    // No per-image emotion table or generated human caption is loaded here.
    static class EntryMetadata {
        internal const string CacheSuffix="-metadata-v2";
        internal static bool Meaningful(string word){
            return !String.IsNullOrWhiteSpace(word)&&word.Length<=120&&
                !Regex.IsMatch(word.Trim(),@"^(?:콘\s*)?\d+$|^(?:아카콘|디시콘|콘)$")&&
                !Regex.IsMatch(word.Trim(),@"^[0-9a-f-]{24,}$",RegexOptions.IgnoreCase);
        }
        internal static string Caption(Entry entry){
            var parts=new List<string>();
            if(Meaningful(entry.DisplayName))parts.Add(entry.DisplayName.Trim());
            parts.AddRange((entry.Aliases??"").Split(',').Where(Meaningful).Select(word=>word.Trim()));
            string text=String.Join("; ",parts.Distinct(StringComparer.OrdinalIgnoreCase));
            return text.Substring(0,Math.Min(1000,text.Length));
        }
    }
}
