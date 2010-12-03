using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace UniKey
{
    public static class Conversions
    {
        public class ScriptInfo
        {
            public string Letters;
            public bool CaseSensitive;
        }

        public static ScriptInfo Cyrillic = new ScriptInfo
        {
            Letters = @"
                a→а, b→б, v→в, g→г, d→д, e→е, yo→ё, zh→ж, z→з, i→и, j→й, k→к, l→л, m→м, n→н, o→о, p→п, r→р, s→с, t→т, u→у, f→ф, x→х, c→ц, ch→ч, sh→ш, shch→щ, `→ъ, y→ы, '→ь, eh→э, yu→ю, ya→я
                A→А, B→Б, V→В, G→Г, D→Д, E→Е, Yo|YO→Ё, Zh|ZH→Ж, Z→З, I→И, J→Й, K→К, L→Л, M→М, N→Н, O→О, P→П, R→Р, S→С, T→Т, U→У, F→Ф, X→Х, C→Ц, Ch|CH→Ч, Sh|SH→Ш, Shch|SHCH→Щ, ~→Ъ, Y→Ы, ""→Ь, Eh|EH→Э, Yu|YU→Ю, Ya|YA→Я
                Jh|JH→Ј, jh→ј
            ",
            CaseSensitive = true
        };

        public static ScriptInfo Greek = new ScriptInfo
        {
            Letters = @"
                a→α, 'a→ά, b→β, g→γ, d→δ, e→ε, 'e→έ, z→ζ, eh→η, 'eh→ή, th→θ, i→ι, 'i→ί, k→κ, l→λ, m→μ, n→ν, x→ξ, o→ο, 'o→ό, p→π, r→ρ, s→σ, '→ς, t→τ, u→υ, 'u→ύ, ph→φ, kh|ch→χ, ps→ψ, oh→ω, 'oh→ώ
                A→Α, 'A→Ά, B→Β, G→Γ, D→Δ, E→Ε, 'E→Έ, Z→Ζ, EH|Eh→Η, 'Eh|'EH→Ή, TH|Th→Θ, I→Ι, 'I→Ί, K→Κ, L→Λ, M→Μ, N→Ν, X→Ξ, O→Ο, 'O→Ό, P→Π, R→Ρ, S→Σ, T→Τ, U→Υ, 'U→Ύ, PH|Ph→Φ, KH|CH|Kh|Ch→Χ, PS|Ps→Ψ, OH|Oh→Ω, 'Oh|'OH→Ώ
            ",
            CaseSensitive = true
        };

        public static ScriptInfo Hiragana = new ScriptInfo
        {
            Letters = @"
                a→あ, i→い, u→う, e→え, o→お, n→ん, ka→か, ga→が, ki→き, gi→ぎ, ku→く, gu→ぐ, ke→け, ge→げ, ko→こ, go→ご, sa→さ, za→ざ, shi→し, ji→じ, su→す, zu→ず, se→せ, ze→ぜ, so→そ, zo→ぞ
                ta→た, da→だ, chi→ち, di→ぢ, tsu→つ, dzu→づ, te→て, de→で, to→と, do→ど, na→な, ni→に, nu→ぬ, ne→ね, no→の, ha→は, ba→ば, pa→ぱ, hi→ひ, bi→び, pi→ぴ, fu→ふ, bu→ぶ, pu→ぷ
                he→へ, be→べ, pe→ぺ, ho→ほ, bo→ぼ, po→ぽ, ma→ま, mi→み, mu→む, me→め, mo→も, ya→や, yu→ゆ, yo→よ, ra→ら, ri→り, ru→る, re→れ, ro→ろ, wa→わ, wi→ゐ, we→ゑ, wo→を, vu→ゔ
                'a→ぁ, 'i→ぃ, 'u→ぅ, 'e→ぇ, 'o→ぉ, 'tsu→っ, 'ya→ゃ, 'yu→ゅ, 'yo→ょ, 'wa→ゎ, 'ka→ゕ, 'ke→ゖ, -→ー
            ",
            CaseSensitive = false
        };

        public static ScriptInfo Katakana = new ScriptInfo
        {
            Letters = @"
                a→ア, i→イ, u→ウ, e→エ, o→オ, n→ン, ka→カ, ga→ガ, ki→キ, gi→ギ, ku→ク, gu→グ, ke→ケ, ge→ゲ, ko→コ, go→ゴ, sa→サ, za→ザ, shi→シ, ji→ジ, su→ス, zu→ズ, se→セ, ze→ゼ, so→ソ, zo→ゾ
                ta→タ, da→ダ, chi→チ, di→ヂ, tsu→ツ, dzu→ヅ, te→テ, de→デ, to→ト, do→ド, na→ナ, ni→ニ, nu→ヌ, ne→ネ, no→ノ, ha→ハ, ba→バ, pa→パ, hi→ヒ, bi→ビ, pi→ピ, fu→フ, bu→ブ, pu→プ
                he→ヘ, be→ベ, pe→ペ, ho→ホ, bo→ボ, po→ポ, ma→マ, mi→ミ, mu→ム, me→メ, mo→モ, ya→ヤ, yu→ユ, yo→ヨ, ra→ラ, ri→リ, ru→ル, re→レ, ro→ロ, wa→ワ, wi→ヰ, we→ヱ, wo→ヲ, vu→ヴ
                va→ヷ, vi→ヸ, ve→ヹ, vo→ヺ, 'a→ァ, 'i→ィ, 'u→ゥ, 'e→ェ, 'o→ォ, 'tsu→ッ, 'ya→ャ, 'yu→ュ, 'yo→ョ, 'wa→ヮ, 'ka→ヵ, 'ke→ヶ, 'ku→ㇰ, 'shi→ㇱ, 'su→ㇲ, 'to→ㇳ, 'nu→ㇴ, 'ha→ㇵ, 'hi→ㇶ, 'fu→ㇷ,
                'he→ㇸ, 'ho→ㇹ, 'mu→ㇺ, 'ra→ㇻ, 'ri→ㇼ, 'ru→ㇽ, 're→ㇾ, 'ro→ㇿ, -→ー
            ",
            CaseSensitive = false
        };

        public static string Convert(ScriptInfo script, string input)
        {
            var pairs = Regex.Split(script.Letters.Replace(" ", "").Trim(), @"\s*\r?\n\s*|\s*,\s*", RegexOptions.Singleline)
                .Select(s => s.Split('→'))
                .SelectMany(s => s[0].Split('|').Select(s2 => new { Key = s2, Value = s[1] }))
                .ToDictionary(a => a.Key, a => a.Value);
            var keys = pairs.Keys.OrderByDescending(k => k.Length).ToList();
            var output = "";
            var comparison = script.CaseSensitive ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase;

            while (input.Length > 0)
            {
                int? len = null;
                foreach (var key in keys)
                {
                    if (input.StartsWith(key, comparison))
                    {
                        len = key.Length;
                        output += pairs[key];
                        break;
                    }
                }
                if (len == null)
                {
                    len = 1;
                    output += input[0];
                }
                input = input.Substring(len.Value);
            }
            return output;
        }
    }
}