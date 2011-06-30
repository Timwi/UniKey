using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Windows.Forms;
using RT.Util;
using RT.Util.Dialogs;
using RT.Util.ExtensionMethods;
using RT.Util.Xml;
using UniKey.Properties;

namespace UniKey
{
    internal class ReplaceResult
    {
        private int _replaceLength;
        private string _replaceWith;
        public ReplaceResult(int replaceLength, string replaceWith)
        {
            _replaceLength = replaceLength;
            _replaceWith = replaceWith;
        }
        public int ReplaceLength { get { return _replaceLength; } }
        public string ReplaceWith { get { return _replaceWith; } }
    }

    internal class Replacer
    {
        public string Input;
        public string ReplaceWith;
        public override string ToString()
        {
            return Input + " ⇒ " + ReplaceWith;
        }
    }

    internal class SearchItem
    {
        public int CodePoint;
        public string Name;
        public int Score;
        public string GetReplacer(List<Replacer> replacers)
        {
            string str = char.ConvertFromUtf32(CodePoint);
            return replacers.Where(s => s.ReplaceWith == str).Select(s => s.Input).JoinString("; ");
        }
        public override string ToString()
        {
            return "({0}) U+{1:X4} {2}".Fmt(Score, CodePoint, Name);
        }
    }

    static class Program
    {
        const bool DebugLog = false;

        static Settings Settings;
        static GlobalKeyboardListener KeyboardListener;
        static List<Keys> Pressed = new List<Keys>();
        static bool Processing = false;
        static string Buffer = string.Empty;
        static int LastBufferCheck = 0;
        static string UndoBufferFrom = null;
        static string UndoBufferTo = null;

        private static Dictionary<int, string> _unicodeData = null;
        static Dictionary<int, string> UnicodeData
        {
            get
            {
                if (_unicodeData == null)
                    initUnicodeDataCache();
                return _unicodeData;
            }
        }

        static void parse(string input, List<Replacer> addTo)
        {
            string[] items = input.Replace("\r", "").Replace("\n", "").Split(';');
            foreach (string item in items)
            {
                int i = item.IndexOf('>');
                if (i > 0)
                    addTo.Add(new Replacer { Input = item.Substring(0, i), ReplaceWith = item.Substring(i + 1) });
            }
            addTo.Sort((a, b) => b.Input.Length.CompareTo(a.Input.Length));
        }

        /// <summary>
        /// The main entry point for the application.
        /// </summary>
        [STAThread]
        static void Main()
        {
            foreach (var p in Process.GetProcessesByName("UniKey"))
            {
                if (p.Id != Process.GetCurrentProcess().Id)
                    p.Kill();
            }

            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);

            try
            {
                Settings = XmlClassify.LoadObjectFromXmlFile<Settings>(PathUtil.AppPathCombine(@"UniKey.settings.xml"));
            }
            catch (Exception e)
            {
                var result = DlgMessage.Show("Could not load UniKey.settings.xml: " + e.Message + "\n\nCreate new file?", "Error", DlgType.Question, "&Yes", "&No");
                if (result == 1)
                    return;
                Settings = new Settings();
                parse("ae>ä;oe>ö;ue>ü;AE>Ä;OE>Ö;UE>Ü;Ae>Ä;Oe>Ö;Ue>Ü;" +
                    "aue>aue;AUE>AUE;Aue>Aue;eue>eue;EUE>EUE;Eue>Eue;" +
                    "äue>äue;ÄUE>ÄUE;Äue>Äue;" +
                    "söben>soeben;SÖBEN>SOEBEN;Söben>Soeben;" +
                    "zürst>zuerst;ZÜRST>ZUERST;Zürst>Zuerst;" +
                    "michäl>michael;Michäl>Michael;MICHÄL>MICHAEL;" +
                    "raffäl>raffael;Raffäl>Raffael;RAFFÄL>RAFFAEL;" +
                    "raphäl>raphael;Raphäl>Raphael;RAPHÄL>RAPHAEL;" +
                    "samül>samuel;Samül>Samuel;SAMÜL>SAMUEL;" +
                    "Getue>Getue;GETUE>GETUE;" +
                    "Getuet>Getüt;GETUET>GETÜT;" +
                    "statue>statue;Statue>Statue;STATUE>STATUE;" +
                    "que>que;QUE>QUE;Que>Que;" +
                    "sexue>sexue;SEXUE>SEXUE;Sexue>Sexue;" +
                    "köx>koex;KÖX>KOEX;Köx>Koex;" +
                    "pöt>poet;PÖT>POET;Pöt>Poet;" +
                    "pösie>poesie;Pösie>Poesie;PÖSIE>POESIE;" +
                    "kongrue>kongrue;KONGRUE>KONGRUE;Kongrue>Kongrue;" +
                    "äro>aero;ÄRO>AERO;Äro>Aero;" +
                    "düll>duell;DÜLL>DUELL;Düll>Duell;" +  // this also takes care of "individuell" coincidentally
                    "dütt>duett;DÜTT>DUETT;Dütt>Duett;" +
                    "aktue>aktue;AKTUE>AKTUE;Aktue>Aktue;" +
                    "eventue>eventue;EVENTUE>EVENTUE;Eventue>Eventue;" +
                    "manue>manue;MANUE>MANUE;Manue>Manue;", Settings.Replacers);
                parse("cx>ĉ;gx>ĝ;hx>ĥ;jx>ĵ;sx>ŝ;ux>ŭ;" +
                    "CX>Ĉ;GX>Ĝ;HX>Ĥ;JX>Ĵ;SX>Ŝ;UX>Ŭ;" +
                    "Cx>Ĉ;Gx>Ĝ;Hx>Ĥ;Jx>Ĵ;Sx>Ŝ;Ux>Ŭ", Settings.Replacers);
                if (!save())
                    return;
            }

            KeyboardListener = new GlobalKeyboardListener();
            KeyboardListener.HookAllKeys = true;
            KeyboardListener.KeyDown += new KeyEventHandler(keyDown);
            KeyboardListener.KeyUp += new KeyEventHandler(keyUp);
            Application.Run();
        }

        static ReplaceResult GetReplace(string buffer)
        {
            Match m;
            int i;

            if ((m = Regex.Match(buffer, @"\{del(c?) ([^\{\}]+)\}$")).Success)
            {
                var input = (m.Groups[1].Length > 0 ? "{{{0}}}" : "{0}").Fmt(m.Groups[2].Value);
                if (!Settings.Replacers.Any(r => r.Input == input))
                    return new ReplaceResult(m.Length, "not found");
                Settings.Replacers = Settings.Replacers.Where(r => r.Input != input).ToList();
                save();
                return new ReplaceResult(m.Length, "done");
            }
            else if ((m = Regex.Match(buffer, @"\{add(c?) ([^\{\}]+)\}$")).Success)
            {
                var input = (m.Groups[1].Length > 0 ? "{{{0}}}" : "{0}").Fmt(m.Groups[2].Value);
                var newRepl = Clipboard.GetText();
                var existing = Settings.Replacers.FirstOrDefault(r => r.Input == input);
                if (existing != null)
                {
                    var tmp = existing.ReplaceWith;
                    existing.ReplaceWith = newRepl;
                    save();
                    return new ReplaceResult(m.Length, "updated: {0} ⇒ {1}".Fmt(tmp, newRepl));
                }
                Settings.Replacers.Add(new Replacer { Input = input, ReplaceWith = newRepl });
                save();
                return new ReplaceResult(m.Length, "added: " + newRepl);
            }
            else if ((m = Regex.Match(buffer, @"\{find\s+([^\{\}]+?)\s*\}$")).Success && m.Groups[1].Length > 0)
            {
                var input = m.Groups[1].Value;
                string[] words = input.Length == 0 ? null : input.Split(' ').Where(s => s.Length > 0).Select(s => s.ToUpperInvariant()).ToArray();
                if (words == null || words.Length < 1)
                    return new ReplaceResult(m.Length, "No search terms given.");
                var candidate = FindCharacters(words).MaxElementOrDefault(item => item.Score);
                if (candidate != null)
                    return new ReplaceResult(m.Length, char.ConvertFromUtf32(candidate.CodePoint));
                else
                    return new ReplaceResult(m.Length, "Character not found.");
            }
            else if ((m = Regex.Match(buffer, @"\{findall\s+([^\{\}]+?)\s*\}$")).Success && m.Groups[1].Length > 0)
            {
                var input = m.Groups[1].Value;
                string[] words = input.Length == 0 ? null : input.Split(' ').Where(s => s.Length > 0).Select(s => s.ToUpperInvariant()).ToArray();
                if (words == null || words.Length < 1)
                    return new ReplaceResult(m.Length, "No search terms given.");
                var candidatesStr = FindCharacters(words)
                    .Select(si => char.ConvertFromUtf32(si.CodePoint) + "    " + si.GetReplacer(Settings.Replacers) + "    0x" + si.CodePoint.ToString("X") + "    " + si.Name + Environment.NewLine)
                    .JoinString();
                if (candidatesStr.Length > 0)
                    Clipboard.SetText(candidatesStr);
                else
                    Clipboard.SetText("No character found matching: " + words.JoinString(" "));
                return new ReplaceResult(m.Length, "");
            }
            else if ((m = Regex.Match(buffer, @"\{html\}$")).Success)
            {
                var input = Clipboard.GetText();
                return new ReplaceResult(m.Length, input.HtmlEscape());
            }
            else if ((m = Regex.Match(buffer, @"\{url\}$")).Success)
            {
                var input = Clipboard.GetText();
                return new ReplaceResult(m.Length, input.UrlEscape());
            }
            else if ((m = Regex.Match(buffer, @"\{unurl\}$")).Success)
            {
                var input = Clipboard.GetText();
                return new ReplaceResult(m.Length, input.UrlUnescape());
            }
            else if ((m = Regex.Match(buffer, @"\{u ([0-9a-f]+)\}$")).Success && int.TryParse(m.Groups[1].Value, NumberStyles.HexNumber, null, out i))
                return new ReplaceResult(m.Length, char.ConvertFromUtf32(i));
            else if ((m = Regex.Match(buffer, @"\{c ([^\{\}]+)\}$")).Success)
                return new ReplaceResult(m.Length, Conversions.Convert(Conversions.Cyrillic, m.Groups[1].Value));
            else if ((m = Regex.Match(buffer, @"\{el ([^\{\}]+)\}$")).Success)
                return new ReplaceResult(m.Length, Conversions.Convert(Conversions.Greek, m.Groups[1].Value));
            else if ((m = Regex.Match(buffer, @"\{hi ([^\{\}]+)\}$")).Success)
                return new ReplaceResult(m.Length, Conversions.Convert(Conversions.Hiragana, m.Groups[1].Value));
            else if ((m = Regex.Match(buffer, @"\{ka ([^\{\}]+)\}$")).Success)
                return new ReplaceResult(m.Length, Conversions.Convert(Conversions.Katakana, m.Groups[1].Value));

            foreach (var repl in Settings.Replacers.OrderByDescending(kvp => kvp.Input.Length))
                if (buffer.EndsWith(repl.Input, StringComparison.Ordinal))
                    return new ReplaceResult(repl.Input.Length, repl.ReplaceWith);
            return null;
        }

        private static IEnumerable<SearchItem> FindCharacters(string[] words)
        {
            var candidates = UnicodeData
                .Where(kvp => words.All(w => kvp.Value.Contains(w)))
                .Select(kvp => new SearchItem
                {
                    CodePoint = kvp.Key,
                    Name = kvp.Value,
                    Score = kvp.Value.Split(' ').Where(s => s.Length > 0).Select(s => s.Trim()).Sum(w => words.Contains(w) ? 20 : words.Any(w2 => w.Contains(w2)) ? 0 : -2)
                })
                .OrderByDescending(item => item.Score).AsEnumerable();
            return candidates;
        }

        static bool save()
        {
            try
            {
                XmlClassify.SaveObjectToXmlFile(Settings, PathUtil.AppPathCombine("UniKey.settings.xml"));
            }
            catch (Exception e)
            {
                DlgMessage.Show("Error saving UniKey.settings.xml: " + e.Message, "Error", DlgType.Error, "OK");
                return false;
            }
            return true;
        }

        static void keyUp(object sender, KeyEventArgs e)
        {
            if (Processing)
                return;
            Processing = true;
            try
            {
                if (DebugLog)
                {
                    var buf = Encoding.UTF8.GetBytes("Up: " + e.KeyCode.ToString() + "\r\n");
                    using (var f = File.Open(@"C:\temp\log", FileMode.Append, FileAccess.Write, FileShare.Write))
                    {
                        f.Write(buf, 0, buf.Length);
                        f.Close();
                    }
                }

                Pressed.Remove(e.KeyCode);

                if (LastBufferCheck > Buffer.Length)
                    LastBufferCheck = Buffer.Length;

                bool alt = Pressed.Contains(Keys.LMenu) || Pressed.Contains(Keys.RMenu);
                bool ctrl = Pressed.Contains(Keys.LControlKey) || Pressed.Contains(Keys.RControlKey);
                bool shift = Pressed.Contains(Keys.LShiftKey) || Pressed.Contains(Keys.RShiftKey);

                if (LastBufferCheck < Buffer.Length && !alt && !ctrl && !shift)
                {
                    string oldBuffer = Buffer;
                    while (LastBufferCheck < Buffer.Length)
                    {
                        LastBufferCheck++;
                        if (Buffer.Substring(0, LastBufferCheck).EndsWith("{exit}"))
                        {
                            Ut.SendKeystrokes(Enumerable.Repeat<object>(Keys.Back, 6));
                            Application.Exit();
                            return;
                        }

                        var replace = GetReplace(Buffer.Substring(0, LastBufferCheck));
                        if (replace != null)
                        {
                            UndoBufferFrom = replace.ReplaceWith + Buffer.Substring(LastBufferCheck);
                            UndoBufferTo = Buffer.Substring(LastBufferCheck - replace.ReplaceLength, replace.ReplaceLength) + Buffer.Substring(LastBufferCheck);
                            Buffer = Buffer.Substring(0, LastBufferCheck - replace.ReplaceLength) + replace.ReplaceWith + Buffer.Substring(LastBufferCheck);
                            LastBufferCheck += replace.ReplaceWith.Length - replace.ReplaceLength;
                        }
                        else if (UndoBufferFrom != null && LastBufferCheck == Buffer.Length && Buffer[LastBufferCheck - 1] == '\\')
                        {
                            UndoBufferFrom = UndoBufferFrom.Substring(0, UndoBufferFrom.Length - 1);
                            UndoBufferTo = UndoBufferTo.Substring(0, UndoBufferTo.Length - 1);
                            Buffer = Buffer.Substring(0, Buffer.Length - UndoBufferFrom.Length - 1) + UndoBufferTo;
                            var t = UndoBufferTo;
                            UndoBufferTo = UndoBufferFrom;
                            UndoBufferFrom = t;
                            LastBufferCheck = Buffer.Length;
                        }
                    }

                    if (Buffer != oldBuffer)
                    {
                        int i = 0;
                        while (i < Buffer.Length && i < oldBuffer.Length && Buffer[i] == oldBuffer[i])
                            i++;
                        Ut.SendKeystrokes(Enumerable.Repeat<object>(Keys.Back, oldBuffer.Length - i).Concat(Buffer.Substring(i).Cast<object>()));
                    }
                }
            }
            finally
            {
                Processing = false;
            }
        }

        static void keyDown(object sender, KeyEventArgs e)
        {
            if (Processing)
                return;
            Processing = true;
            try
            {
                if (DebugLog)
                {
                    var buf = Encoding.UTF8.GetBytes("Down: " + e.KeyCode.ToString() + "\r\n");
                    using (var f = File.Open(@"C:\temp\log", FileMode.Append, FileAccess.Write, FileShare.Write))
                    {
                        f.Write(buf, 0, buf.Length);
                        f.Close();
                    }
                }

                if (!Pressed.Contains(e.KeyCode))
                    Pressed.Add(e.KeyCode);

                if (e.KeyCode == Keys.LControlKey || e.KeyCode == Keys.RControlKey || e.KeyCode == Keys.Escape || e.KeyCode == Keys.Enter || e.KeyCode == Keys.Return || e.KeyCode == Keys.Tab)
                {
                    Buffer = string.Empty;
                    UndoBufferFrom = null;
                }
                else if (e.KeyCode == Keys.Back && Buffer.Length > 0)
                {
                    Buffer = Buffer.Substring(0, Buffer.Length - 1);
                    if (UndoBufferFrom != null)
                    {
                        if (UndoBufferFrom.Length == 1 || UndoBufferTo.Length == 1)
                            UndoBufferFrom = null;
                        else
                        {
                            UndoBufferFrom = UndoBufferFrom.Substring(0, UndoBufferFrom.Length - 1);
                            UndoBufferTo = UndoBufferTo.Substring(0, UndoBufferTo.Length - 1);
                        }
                    }
                    if (LastBufferCheck > 0) LastBufferCheck--;
                }

                bool alt = Pressed.Contains(Keys.LMenu) || Pressed.Contains(Keys.RMenu);
                bool ctrl = Pressed.Contains(Keys.LControlKey) || Pressed.Contains(Keys.RControlKey);
                bool shift = Pressed.Contains(Keys.LShiftKey) || Pressed.Contains(Keys.RShiftKey);

                if (!alt && !ctrl)
                {
                    char? ch = null;
                    if (shift && KeyToChar.Shift.ContainsKey(e.KeyCode))
                        ch = KeyToChar.Shift[e.KeyCode];
                    else if (!shift && KeyToChar.NoShift.ContainsKey(e.KeyCode))
                        ch = KeyToChar.NoShift[e.KeyCode];
                    if (ch != null)
                    {
                        Buffer += ch.Value;
                        if (UndoBufferFrom != null)
                        {
                            UndoBufferFrom += ch.Value;
                            UndoBufferTo += ch.Value;
                        }
                    }
                }
            }
            finally
            {
                Processing = false;
            }
        }

        private static void initUnicodeDataCache()
        {
            _unicodeData = new Dictionary<int, string>();
            foreach (var line in Resources.UnicodeData.Replace("\r", "").Split('\n'))
            {
                if (line.Length == 0)
                    continue;
                var fields = line.Split(';');
                if (fields.Length < 2)
                    continue;
                if (fields[1].Length == 0 || fields[1][0] == '<')
                    continue;
                _unicodeData[int.Parse(fields[0], NumberStyles.HexNumber)] = fields[1].ToUpperInvariant();
            }
        }
    }
}
