using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace UniKey
{
    internal class CyrCycle
    {
        public char BaseChar;
        public string Sequence;
    }

    internal class CyrMode : KeyboardMode
    {
        public CyrMode()
            : base()
        {
            var cycles = new List<CyrCycle>();
            cycles.Add(new CyrCycle { BaseChar = 'a', Sequence = @"аяӑӓӕ" });
            cycles.Add(new CyrCycle { BaseChar = 'b', Sequence = @"б" });
            cycles.Add(new CyrCycle { BaseChar = 'c', Sequence = @"цџ" });
            cycles.Add(new CyrCycle { BaseChar = 'd', Sequence = @"дђћ" });
            cycles.Add(new CyrCycle { BaseChar = 'e', Sequence = @"еёэєҽҿӗәӛѥ" });
            cycles.Add(new CyrCycle { BaseChar = 'f', Sequence = @"фѳ" });
            cycles.Add(new CyrCycle { BaseChar = 'g', Sequence = @"гѓґғҕ" });
            cycles.Add(new CyrCycle { BaseChar = 'h', Sequence = @"чҷҹӌӵһҩ" });
            cycles.Add(new CyrCycle { BaseChar = 'i', Sequence = @"иӣӥӀ" });
            cycles.Add(new CyrCycle { BaseChar = 'j', Sequence = @"жҗӂӝѧѩѫѭ" });
            cycles.Add(new CyrCycle { BaseChar = 'k', Sequence = @"кќқҝҟҡӄҁ" });
            cycles.Add(new CyrCycle { BaseChar = 'l', Sequence = @"лљ" });
            cycles.Add(new CyrCycle { BaseChar = 'm', Sequence = @"м" });
            cycles.Add(new CyrCycle { BaseChar = 'n', Sequence = @"нњңҥӈ" });
            cycles.Add(new CyrCycle { BaseChar = 'o', Sequence = @"оѻӧөӫѡѽѿ" });
            cycles.Add(new CyrCycle { BaseChar = 'p', Sequence = @"пҧѱ" });
            cycles.Add(new CyrCycle { BaseChar = 'q', Sequence = @"йіїј" });
            cycles.Add(new CyrCycle { BaseChar = 'r', Sequence = @"р" });
            cycles.Add(new CyrCycle { BaseChar = 's', Sequence = @"сҫѕ" });
            cycles.Add(new CyrCycle { BaseChar = 't', Sequence = @"тҭҵ" });
            cycles.Add(new CyrCycle { BaseChar = 'u', Sequence = @"уюўӯӱӳүұѹ" });
            cycles.Add(new CyrCycle { BaseChar = 'v', Sequence = @"вѵѷ" });
            cycles.Add(new CyrCycle { BaseChar = 'w', Sequence = @"шщ" });
            cycles.Add(new CyrCycle { BaseChar = 'x', Sequence = @"хҳ" });
            cycles.Add(new CyrCycle { BaseChar = 'y', Sequence = @"ьыъѣӹ" });
            cycles.Add(new CyrCycle { BaseChar = 'z', Sequence = @"зҙӟѯӡ" });

            cycles.Add(new CyrCycle { BaseChar = 'A', Sequence = @"АЯӐӒӔ" });
            cycles.Add(new CyrCycle { BaseChar = 'B', Sequence = @"Б" });
            cycles.Add(new CyrCycle { BaseChar = 'C', Sequence = @"ЦЏ" });
            cycles.Add(new CyrCycle { BaseChar = 'D', Sequence = @"ДЂЋ" });
            cycles.Add(new CyrCycle { BaseChar = 'E', Sequence = @"ЕЁЭЄҼҾӖӘӚѤ" });
            cycles.Add(new CyrCycle { BaseChar = 'F', Sequence = @"ФѲ" });
            cycles.Add(new CyrCycle { BaseChar = 'G', Sequence = @"ГЃҐҒҔ" });
            cycles.Add(new CyrCycle { BaseChar = 'H', Sequence = @"ЧҶҸӋӴҺҨ" });
            cycles.Add(new CyrCycle { BaseChar = 'I', Sequence = @"ИӢӤӀ" });
            cycles.Add(new CyrCycle { BaseChar = 'J', Sequence = @"ЖҖӁӜѦѨѪѬ" });
            cycles.Add(new CyrCycle { BaseChar = 'K', Sequence = @"КЌҚҜҞҠӃҀ" });
            cycles.Add(new CyrCycle { BaseChar = 'L', Sequence = @"ЛЉ" });
            cycles.Add(new CyrCycle { BaseChar = 'M', Sequence = @"М" });
            cycles.Add(new CyrCycle { BaseChar = 'N', Sequence = @"НЊҢҤӇ" });
            cycles.Add(new CyrCycle { BaseChar = 'O', Sequence = @"ОѺӦӨӪѠѼѾ" });
            cycles.Add(new CyrCycle { BaseChar = 'P', Sequence = @"ПҦѰ" });
            cycles.Add(new CyrCycle { BaseChar = 'Q', Sequence = @"ЙІЇЈ" });
            cycles.Add(new CyrCycle { BaseChar = 'R', Sequence = @"Р" });
            cycles.Add(new CyrCycle { BaseChar = 'S', Sequence = @"СҪЅ" });
            cycles.Add(new CyrCycle { BaseChar = 'T', Sequence = @"ТҬҴ" });
            cycles.Add(new CyrCycle { BaseChar = 'U', Sequence = @"УЮЎӮӰӲҮҰѸ" });
            cycles.Add(new CyrCycle { BaseChar = 'V', Sequence = @"ВѴѶ" });
            cycles.Add(new CyrCycle { BaseChar = 'W', Sequence = @"ШЩ" });
            cycles.Add(new CyrCycle { BaseChar = 'X', Sequence = @"ХҲ" });
            cycles.Add(new CyrCycle { BaseChar = 'Y', Sequence = @"ЬЫЪѢӸ" });
            cycles.Add(new CyrCycle { BaseChar = 'Z', Sequence = @"ЗҘӞѮӠ" });

            Shortcuts = new List<Shortcut>();
            foreach (var c in cycles)
                Shortcuts.Add(new Shortcut { Key = c.BaseChar.ToString(), Value = c.Sequence[0].ToString() });

            foreach (var c in cycles)
            {
                for (int i = 0; i < c.Sequence.Length; i++)
                {
                    Shortcuts.Add(new Shortcut { Key = c.Sequence[i] + "¬", Value = c.Sequence[(i + 1) % c.Sequence.Length].ToString() });
                    Shortcuts.Add(new Shortcut { Value = c.Sequence[i].ToString(), Key = c.Sequence[(i + 1) % c.Sequence.Length] + "¦" });
                }
            }
        }
    }
}
