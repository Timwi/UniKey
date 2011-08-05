using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using RT.Util.ExtensionMethods;

namespace UniKey
{
    sealed class SearchItem
    {
        public int CodePoint;
        public string Name;
        public int Score;
        public string GetReplacer(Dictionary<string, string> replacers)
        {
            return replacers.Where(kvp => kvp.Value == char.ConvertFromUtf32(CodePoint)).Select(kvp => kvp.Key).JoinString("; ");
        }
        public override string ToString()
        {
            return "({0}) U+{1:X4} {2}".Fmt(Score, CodePoint, Name);
        }
    }
}
