using System.Text.RegularExpressions;
using RT.Util;
using RT.Util.ExtensionMethods;

namespace UniKey;

public static class Conversions
{
    public class ScriptInfo
    {
        public string Key;
        public string Name;
        public string Letters;
        public bool CaseSensitive;
        public bool AutoCaps;

        public IDictionary<string, string> Pairs { get { if (_pairs == null) generate(); return _pairs; } }
        public IList<string> Keys { get { if (_keys == null) generate(); return _keys; } }

        private IDictionary<string, string> _pairs;
        private IList<string> _keys;

        [System.Diagnostics.CodeAnalysis.SuppressMessage("Performance", "CA1862:Use the 'StringComparison' method overloads to perform case-insensitive string comparisons", Justification = "inapplicable in this case")]
        private void generate()
        {
            _pairs = Regex.Split(Letters.Replace(" ", "").Trim(), @"\s*\r?\n\s*|\s*,\s*", RegexOptions.Singleline)
                .Select(s => s.Split('→'))
                .SelectMany(s => s[0].Split('|').Select(s2 => new { Key = s2, Value = s[1] }))
                .ToDictionary(a => a.Key, a => a.Value);
            if (AutoCaps)
                foreach (var pair in _pairs.ToArray())
                {
                    if (pair.Value.ToUpperInvariant() == pair.Value)
                        continue;
                    if (pair.Key.ToUpperInvariant() != pair.Key)
                        _pairs.Add(pair.Key.ToUpper(), pair.Value.ToUpper());
                    if (pair.Key.Length > 1)
                    {
                        var keyUpper = char.ToUpper(pair.Key[0]) + pair.Key.Substring(1);
                        if (keyUpper != pair.Key && keyUpper != pair.Key.ToUpper())
                            _pairs.Add(keyUpper, pair.Value.ToUpper());
                    }
                }
            _pairs = _pairs.AsReadOnly();
            _keys = _pairs.Keys.OrderByDescending(k => k.Length).ToList().AsReadOnly();
        }
    }

    public static readonly ScriptInfo Cyrillic = new()
    {
        Key = "c",
        Name = "Cyrillic",
        Letters = @"
                a→а, b→б, v→в, g→г, d→д, e→е, yo→ё, zh→ж, z→з, i→и, j→й, k→к, l→л, m→м, n→н, o→о, p→п, r→р, s→с, t→т, u→у, f→ф, x→х, c→ц, ch→ч, sh→ш, shch→щ, `→ъ, y→ы, '→ь, eh→э, yu→ю, ya→я
                A→А, B→Б, V→В, G→Г, D→Д, E→Е, Yo|YO→Ё, Zh|ZH→Ж, Z→З, I→И, J→Й, K→К, L→Л, M→М, N→Н, O→О, P→П, R→Р, S→С, T→Т, U→У, F→Ф, X→Х, C→Ц, Ch|CH→Ч, Sh|SH→Ш, Shch|SHCH→Щ, ~→Ъ, Y→Ы, ""→Ь, Eh|EH→Э, Yu|YU→Ю, Ya|YA→Я
                Jh|JH→Ј, jh→ј, ye→є, Ye|YE→Є, ih→і, yi→ї, Ih|IH→І, Yi|YI→Ї
            ",
        CaseSensitive = true
    };

    public static readonly ScriptInfo RussianNative = new()
    {
        Letters = @"
                a→а, b→б, v→в, g→г, d→д, e→е, yo→ё, zh→ж, z→з, i→и, j→й, k→к, l→л, m→м, n→н, o→о, p→п, r→р, s→с, t→т, u→у, f→ф, x→х, h→х, c→ц, ch→ч, sh→ш, shch→щ, `→ъ, y→ы, '→ь, eh→э, yu→ю, ju→ю, ya→я, ja→я
                ~→Ъ, ""→Ь, ¬→Ъ, @→Ь
            ",
        CaseSensitive = true,
        AutoCaps = true
    };

    public static readonly ScriptInfo Greek = new()
    {
        Key = "g",
        Name = "Greek",
        Letters = @"
                a→α, 'a→ά, b→β, g→γ, d→δ, e→ε, 'e→έ, z→ζ, eh→η, 'eh→ή, th→θ, i→ι, 'i→ί, k→κ, l→λ, m→μ, n→ν, x→ξ, o→ο, 'o→ό, p→π, r→ρ, s→σ, '→ς, t→τ, u→υ, 'u→ύ, ph→φ, kh|ch→χ, ps→ψ, oh→ω, 'oh→ώ
                A→Α, 'A→Ά, B→Β, G→Γ, D→Δ, E→Ε, 'E→Έ, Z→Ζ, EH|Eh→Η, 'Eh|'EH→Ή, TH|Th→Θ, I→Ι, 'I→Ί, K→Κ, L→Λ, M→Μ, N→Ν, X→Ξ, O→Ο, 'O→Ό, P→Π, R→Ρ, S→Σ, T→Τ, U→Υ, 'U→Ύ, PH|Ph→Φ, KH|CH|Kh|Ch→Χ, PS|Ps→Ψ, OH|Oh→Ω, 'Oh|'OH→Ώ
            ",
        CaseSensitive = true
    };

    public static readonly ScriptInfo Hiragana = new()
    {
        Key = "hi",
        Name = "Hiragana",
        Letters = @"
                a→あ, i→い, u→う, e→え, o→お, n→ん, ka→か, ga→が, ki→き, gi→ぎ, ku→く, gu→ぐ, ke→け, ge→げ, ko→こ, go→ご, sa→さ, za→ざ, shi→し, ji→じ, su→す, zu→ず, se→せ, ze→ぜ, so→そ, zo→ぞ
                ta→た, da→だ, chi→ち, di→ぢ, tsu→つ, dzu→づ, te→て, de→で, to→と, do→ど, na→な, ni→に, nu→ぬ, ne→ね, no→の, ha→は, ba→ば, pa→ぱ, hi→ひ, bi→び, pi→ぴ, fu→ふ, bu→ぶ, pu→ぷ
                he→へ, be→べ, pe→ぺ, ho→ほ, bo→ぼ, po→ぽ, ma→ま, mi→み, mu→む, me→め, mo→も, ya→や, yu→ゆ, yo→よ, ra→ら, ri→り, ru→る, re→れ, ro→ろ, wa→わ, wi→ゐ, we→ゑ, wo→を, vu→ゔ
                'a→ぁ, 'i→ぃ, 'u→ぅ, 'e→ぇ, 'o→ぉ, 'tsu→っ, 'ya→ゃ, 'yu→ゅ, 'yo→ょ, 'wa→ゎ, 'ka→ゕ, 'ke→ゖ, -→ー
            ",
        CaseSensitive = false
    };

    public static readonly ScriptInfo Katakana = new()
    {
        Key = "ka",
        Name = "Katakana",
        Letters = @"
                a→ア, i→イ, u→ウ, e→エ, o→オ, n→ン, ka→カ, ga→ガ, ki→キ, gi→ギ, ku→ク, gu→グ, ke→ケ, ge→ゲ, ko→コ, go→ゴ, sa→サ, za→ザ, shi→シ, ji→ジ, su→ス, zu→ズ, se→セ, ze→ゼ, so→ソ, zo→ゾ
                ta→タ, da→ダ, chi→チ, di→ヂ, tsu→ツ, dzu→ヅ, te→テ, de→デ, to→ト, do→ド, na→ナ, ni→ニ, nu→ヌ, ne→ネ, no→ノ, ha→ハ, ba→バ, pa→パ, hi→ヒ, bi→ビ, pi→ピ, fu→フ, bu→ブ, pu→プ
                he→ヘ, be→ベ, pe→ペ, ho→ホ, bo→ボ, po→ポ, ma→マ, mi→ミ, mu→ム, me→メ, mo→モ, ya→ヤ, yu→ユ, yo→ヨ, ra→ラ, ri→リ, ru→ル, re→レ, ro→ロ, wa→ワ, wi→ヰ, we→ヱ, wo→ヲ, vu→ヴ
                va→ヷ, vi→ヸ, ve→ヹ, vo→ヺ, 'a→ァ, 'i→ィ, 'u→ゥ, 'e→ェ, 'o→ォ, 'tsu→ッ, 'ya→ャ, 'yu→ュ, 'yo→ョ, 'wa→ヮ, 'ka→ヵ, 'ke→ヶ, 'ku→ㇰ, 'shi→ㇱ, 'su→ㇲ, 'to→ㇳ, 'nu→ㇴ, 'ha→ㇵ, 'hi→ㇶ, 'fu→ㇷ,
                'he→ㇸ, 'ho→ㇹ, 'mu→ㇺ, 'ra→ㇻ, 'ri→ㇼ, 'ru→ㇽ, 're→ㇾ, 'ro→ㇿ, -→ー
            ",
        CaseSensitive = false
    };

    public static readonly ScriptInfo SmallCaps = new()
    {
        Key = "sc",
        Name = "Small caps",
        Letters = @"a→ᴀ, b→ʙ, c→ᴄ, d→ᴅ, e→ᴇ, g→ɢ, h→ʜ, i→ɪ, j→ᴊ, k→ᴋ, l→ʟ, m→ᴍ, n→ɴ, o→ᴏ, p→ᴘ, r→ʀ, t→ᴛ, u→ᴜ, v→ᴠ, w→ᴡ, y→ʏ, z→ᴢ",
        CaseSensitive = true
    };

    public static readonly ScriptInfo MathBold = new()
    {
        Key = "mb",
        Name = "Math bold",
        Letters = @"A→𝐀, B→𝐁, C→𝐂, D→𝐃, E→𝐄, F→𝐅, G→𝐆, H→𝐇, I→𝐈, J→𝐉, K→𝐊, L→𝐋, M→𝐌, N→𝐍, O→𝐎, P→𝐏, Q→𝐐, R→𝐑, S→𝐒, T→𝐓, U→𝐔, V→𝐕, W→𝐖, X→𝐗, Y→𝐘, Z→𝐙, a→𝐚, b→𝐛, c→𝐜, d→𝐝, e→𝐞, f→𝐟, g→𝐠, h→𝐡, i→𝐢, j→𝐣, k→𝐤, l→𝐥, m→𝐦, n→𝐧, o→𝐨, p→𝐩, q→𝐪, r→𝐫, s→𝐬, t→𝐭, u→𝐮, v→𝐯, w→𝐰, x→𝐱, y→𝐲, z→𝐳, 0→𝟎, 1→𝟏, 2→𝟐, 3→𝟑, 4→𝟒, 5→𝟓, 6→𝟔, 7→𝟕, 8→𝟖, 9→𝟗",
        CaseSensitive = true
    };

    public static readonly ScriptInfo MathItalic = new()
    {
        Key = "mi",
        Name = "Math italic",
        Letters = @"A→𝐴, B→𝐵, C→𝐶, D→𝐷, E→𝐸, F→𝐹, G→𝐺, H→𝐻, I→𝐼, J→𝐽, K→𝐾, L→𝐿, M→𝑀, N→𝑁, O→𝑂, P→𝑃, Q→𝑄, R→𝑅, S→𝑆, T→𝑇, U→𝑈, V→𝑉, W→𝑊, X→𝑋, Y→𝑌, Z→𝑍, a→𝑎, b→𝑏, c→𝑐, d→𝑑, e→𝑒, f→𝑓, g→𝑔, h→ℎ, i→𝑖, j→𝑗, k→𝑘, l→𝑙, m→𝑚, n→𝑛, o→𝑜, p→𝑝, q→𝑞, r→𝑟, s→𝑠, t→𝑡, u→𝑢, v→𝑣, w→𝑤, x→𝑥, y→𝑦, z→𝑧, 'i→𝚤, 'j→𝚥",
        CaseSensitive = true
    };

    public static readonly ScriptInfo MathBoldItalic = new()
    {
        Key = "mbi",
        Name = "Math bold italic",
        Letters = @"A→𝑨, B→𝑩, C→𝑪, D→𝑫, E→𝑬, F→𝑭, G→𝑮, H→𝑯, I→𝑰, J→𝑱, K→𝑲, L→𝑳, M→𝑴, N→𝑵, O→𝑶, P→𝑷, Q→𝑸, R→𝑹, S→𝑺, T→𝑻, U→𝑼, V→𝑽, W→𝑾, X→𝑿, Y→𝒀, Z→𝒁, a→𝒂, b→𝒃, c→𝒄, d→𝒅, e→𝒆, f→𝒇, g→𝒈, h→𝒉, i→𝒊, j→𝒋, k→𝒌, l→𝒍, m→𝒎, n→𝒏, o→𝒐, p→𝒑, q→𝒒, r→𝒓, s→𝒔, t→𝒕, u→𝒖, v→𝒗, w→𝒘, x→𝒙, y→𝒚, z→𝒛",
        CaseSensitive = true
    };

    public static readonly ScriptInfo MathSansSerif = new()
    {
        Key = "mss",
        Name = "Math sans-serif",
        Letters = @"A→𝖠, B→𝖡, C→𝖢, D→𝖣, E→𝖤, F→𝖥, G→𝖦, H→𝖧, I→𝖨, J→𝖩, K→𝖪, L→𝖫, M→𝖬, N→𝖭, O→𝖮, P→𝖯, Q→𝖰, R→𝖱, S→𝖲, T→𝖳, U→𝖴, V→𝖵, W→𝖶, X→𝖷, Y→𝖸, Z→𝖹, a→𝖺, b→𝖻, c→𝖼, d→𝖽, e→𝖾, f→𝖿, g→𝗀, h→𝗁, i→𝗂, j→𝗃, k→𝗄, l→𝗅, m→𝗆, n→𝗇, o→𝗈, p→𝗉, q→𝗊, r→𝗋, s→𝗌, t→𝗍, u→𝗎, v→𝗏, w→𝗐, x→𝗑, y→𝗒, z→𝗓, 0→𝟢, 1→𝟣, 2→𝟤, 3→𝟥, 4→𝟦, 5→𝟧, 6→𝟨, 7→𝟩, 8→𝟪, 9→𝟫",
        CaseSensitive = true
    };

    public static readonly ScriptInfo MathSansSerifBold = new()
    {
        Key = "mssb",
        Name = "Math sans-serif bold",
        Letters = @"A→𝗔, B→𝗕, C→𝗖, D→𝗗, E→𝗘, F→𝗙, G→𝗚, H→𝗛, I→𝗜, J→𝗝, K→𝗞, L→𝗟, M→𝗠, N→𝗡, O→𝗢, P→𝗣, Q→𝗤, R→𝗥, S→𝗦, T→𝗧, U→𝗨, V→𝗩, W→𝗪, X→𝗫, Y→𝗬, Z→𝗭, a→𝗮, b→𝗯, c→𝗰, d→𝗱, e→𝗲, f→𝗳, g→𝗴, h→𝗵, i→𝗶, j→𝗷, k→𝗸, l→𝗹, m→𝗺, n→𝗻, o→𝗼, p→𝗽, q→𝗾, r→𝗿, s→𝘀, t→𝘁, u→𝘂, v→𝘃, w→𝘄, x→𝘅, y→𝘆, z→𝘇, 0→𝟬, 1→𝟭, 2→𝟮, 3→𝟯, 4→𝟰, 5→𝟱, 6→𝟲, 7→𝟳, 8→𝟴, 9→𝟵",
        CaseSensitive = true
    };

    public static readonly ScriptInfo MathSansSerifItalic = new()
    {
        Key = "mssi",
        Name = "Math sans-serif italic",
        Letters = @"A→𝘈, B→𝘉, C→𝘊, D→𝘋, E→𝘌, F→𝘍, G→𝘎, H→𝘏, I→𝘐, J→𝘑, K→𝘒, L→𝘓, M→𝘔, N→𝘕, O→𝘖, P→𝘗, Q→𝘘, R→𝘙, S→𝘚, T→𝘛, U→𝘜, V→𝘝, W→𝘞, X→𝘟, Y→𝘠, Z→𝘡, a→𝘢, b→𝘣, c→𝘤, d→𝘥, e→𝘦, f→𝘧, g→𝘨, h→𝘩, i→𝘪, j→𝘫, k→𝘬, l→𝘭, m→𝘮, n→𝘯, o→𝘰, p→𝘱, q→𝘲, r→𝘳, s→𝘴, t→𝘵, u→𝘶, v→𝘷, w→𝘸, x→𝘹, y→𝘺, z→𝘻",
        CaseSensitive = true
    };

    public static readonly ScriptInfo MathSansSerifBoldItalic = new()
    {
        Key = "mssbi",
        Name = "Math sans-serif bold italic",
        Letters = @"A→𝘼, B→𝘽, C→𝘾, D→𝘿, E→𝙀, F→𝙁, G→𝙂, H→𝙃, I→𝙄, J→𝙅, K→𝙆, L→𝙇, M→𝙈, N→𝙉, O→𝙊, P→𝙋, Q→𝙌, R→𝙍, S→𝙎, T→𝙏, U→𝙐, V→𝙑, W→𝙒, X→𝙓, Y→𝙔, Z→𝙕, a→𝙖, b→𝙗, c→𝙘, d→𝙙, e→𝙚, f→𝙛, g→𝙜, h→𝙝, i→𝙞, j→𝙟, k→𝙠, l→𝙡, m→𝙢, n→𝙣, o→𝙤, p→𝙥, q→𝙦, r→𝙧, s→𝙨, t→𝙩, u→𝙪, v→𝙫, w→𝙬, x→𝙭, y→𝙮, z→𝙯",
        CaseSensitive = true
    };

    public static readonly ScriptInfo MathScript = new()
    {
        Key = "ms",
        Name = "Math script",
        Letters = @"A→𝒜, B→ℬ, C→𝒞, D→𝒟, E→ℰ, F→ℱ, G→𝒢, H→ℋ, I→ℐ, J→𝒥, K→𝒦, L→ℒ, M→ℳ, N→𝒩, O→𝒪, P→𝒫, Q→𝒬, R→ℛ, S→𝒮, T→𝒯, U→𝒰, V→𝒱, W→𝒲, X→𝒳, Y→𝒴, Z→𝒵, a→𝒶, b→𝒷, c→𝒸, d→𝒹, e→ℯ, f→𝒻, g→ℊ, h→𝒽, i→𝒾, j→𝒿, k→𝓀, l→𝓁, m→𝓂, n→𝓃, o→ℴ, p→𝓅, q→𝓆, r→𝓇, s→𝓈, t→𝓉, u→𝓊, v→𝓋, w→𝓌, x→𝓍, y→𝓎, z→𝓏",
        CaseSensitive = true
    };

    public static readonly ScriptInfo MathScriptBold = new()
    {
        Key = "msb",
        Name = "Math script bold",
        Letters = @"A→𝓐, B→𝓑, C→𝓒, D→𝓓, E→𝓔, F→𝓕, G→𝓖, H→𝓗, I→𝓘, J→𝓙, K→𝓚, L→𝓛, M→𝓜, N→𝓝, O→𝓞, P→𝓟, Q→𝓠, R→𝓡, S→𝓢, T→𝓣, U→𝓤, V→𝓥, W→𝓦, X→𝓧, Y→𝓨, Z→𝓩, a→𝓪, b→𝓫, c→𝓬, d→𝓭, e→𝓮, f→𝓯, g→𝓰, h→𝓱, i→𝓲, j→𝓳, k→𝓴, l→𝓵, m→𝓶, n→𝓷, o→𝓸, p→𝓹, q→𝓺, r→𝓻, s→𝓼, t→𝓽, u→𝓾, v→𝓿, w→𝔀, x→𝔁, y→𝔂, z→𝔃",
        CaseSensitive = true
    };

    public static readonly ScriptInfo MathFraktur = new()
    {
        Key = "mf",
        Name = "Math fraktur",
        Letters = @"A→𝔄, B→𝔅, C→ℭ, D→𝔇, E→𝔈, F→𝔉, G→𝔊, H→ℌ, I→ℑ, J→𝔍, K→𝔎, L→𝔏, M→𝔐, N→𝔑, O→𝔒, P→𝔓, Q→𝔔, R→ℜ, S→𝔖, T→𝔗, U→𝔘, V→𝔙, W→𝔚, X→𝔛, Y→𝔜, Z→ℨ, a→𝔞, b→𝔟, c→𝔠, d→𝔡, e→𝔢, f→𝔣, g→𝔤, h→𝔥, i→𝔦, j→𝔧, k→𝔨, l→𝔩, m→𝔪, n→𝔫, o→𝔬, p→𝔭, q→𝔮, r→𝔯, s→𝔰, t→𝔱, u→𝔲, v→𝔳, w→𝔴, x→𝔵, y→𝔶, z→𝔷",
        CaseSensitive = true
    };

    public static readonly ScriptInfo MathFrakturBold = new()
    {
        Key = "mfb",
        Name = "Math fraktur bold",
        Letters = @"A→𝕬, B→𝕭, C→𝕮, D→𝕯, E→𝕰, F→𝕱, G→𝕲, H→𝕳, I→𝕴, J→𝕵, K→𝕶, L→𝕷, M→𝕸, N→𝕹, O→𝕺, P→𝕻, Q→𝕼, R→𝕽, S→𝕾, T→𝕿, U→𝖀, V→𝖁, W→𝖂, X→𝖃, Y→𝖄, Z→𝖅, a→𝖆, b→𝖇, c→𝖈, d→𝖉, e→𝖊, f→𝖋, g→𝖌, h→𝖍, i→𝖎, j→𝖏, k→𝖐, l→𝖑, m→𝖒, n→𝖓, o→𝖔, p→𝖕, q→𝖖, r→𝖗, s→𝖘, t→𝖙, u→𝖚, v→𝖛, w→𝖜, x→𝖝, y→𝖞, z→𝖟",
        CaseSensitive = true
    };

    public static readonly ScriptInfo MathMonospace = new()
    {
        Key = "mm",
        Name = "Math monospace",
        Letters = @"A→𝙰, B→𝙱, C→𝙲, D→𝙳, E→𝙴, F→𝙵, G→𝙶, H→𝙷, I→𝙸, J→𝙹, K→𝙺, L→𝙻, M→𝙼, N→𝙽, O→𝙾, P→𝙿, Q→𝚀, R→𝚁, S→𝚂, T→𝚃, U→𝚄, V→𝚅, W→𝚆, X→𝚇, Y→𝚈, Z→𝚉, a→𝚊, b→𝚋, c→𝚌, d→𝚍, e→𝚎, f→𝚏, g→𝚐, h→𝚑, i→𝚒, j→𝚓, k→𝚔, l→𝚕, m→𝚖, n→𝚗, o→𝚘, p→𝚙, q→𝚚, r→𝚛, s→𝚜, t→𝚝, u→𝚞, v→𝚟, w→𝚠, x→𝚡, y→𝚢, z→𝚣, 0→𝟶, 1→𝟷, 2→𝟸, 3→𝟹, 4→𝟺, 5→𝟻, 6→𝟼, 7→𝟽, 8→𝟾, 9→𝟿",
        CaseSensitive = true
    };

    public static readonly ScriptInfo MathDoubleStruck = new()
    {
        Key = "md",
        Name = "Math double-struck",
        Letters = @"A→𝔸, B→𝔹, C→ℂ, D→𝔻, E→𝔼, F→𝔽, G→𝔾, H→ℍ, I→𝕀, J→𝕁, K→𝕂, L→𝕃, M→𝕄, N→ℕ, O→𝕆, P→ℙ, Q→ℚ, R→ℝ, S→𝕊, T→𝕋, U→𝕌, V→𝕍, W→𝕎, X→𝕏, Y→𝕐, Z→ℤ, a→𝕒, b→𝕓, c→𝕔, d→𝕕, e→𝕖, f→𝕗, g→𝕘, h→𝕙, i→𝕚, j→𝕛, k→𝕜, l→𝕝, m→𝕞, n→𝕟, o→𝕠, p→𝕡, q→𝕢, r→𝕣, s→𝕤, t→𝕥, u→𝕦, v→𝕧, w→𝕨, x→𝕩, y→𝕪, z→𝕫, 0→𝟘, 1→𝟙, 2→𝟚, 3→𝟛, 4→𝟜, 5→𝟝, 6→𝟞, 7→𝟟, 8→𝟠, 9→𝟡",
        CaseSensitive = true
    };

    public static string Convert(ScriptInfo script, string input)
    {
        var output = "";
        var comparison = script.CaseSensitive ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase;

        while (input.Length > 0)
        {
            int? len = null;
            foreach (var key in script.Keys)
            {
                if (input.StartsWith(key, comparison))
                {
                    len = key.Length;
                    output += script.Pairs[key];
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

    // Make sure this is at the end of this file so that all the other static fields are initialized first.
    public static readonly ScriptInfo[] AllConversions = Ut.NewArray(
        Cyrillic,
        Greek,
        Hiragana,
        Katakana,
        SmallCaps,
        MathBold,
        MathItalic,
        MathBoldItalic,
        MathSansSerif,
        MathSansSerifBold,
        MathSansSerifItalic,
        MathSansSerifBoldItalic,
        MathScript,
        MathScriptBold,
        MathFraktur,
        MathFrakturBold,
        MathMonospace,
        MathDoubleStruck
    );
}
