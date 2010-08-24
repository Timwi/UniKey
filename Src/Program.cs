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
            return replacers.Where(s => s.ReplaceWith == str).Select(s => s.Input).FirstOrDefault();
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
                var candidate = UnicodeData
                    .Where(kvp => words.All(w => kvp.Value.Contains(w)))
                    .Select(kvp => new SearchItem { CodePoint = kvp.Key, Name = kvp.Value, Score = words.Sum(w => Regex.IsMatch(kvp.Value, "\\b" + Regex.Escape(w) + "\\b") ? 20 : 10) - (kvp.Value.Length / 3) })
                    .OrderByDescending(item => item.Score)
                    .Take(1).ToArray();
                if (candidate.Any())
                    return new ReplaceResult(m.Length, char.ConvertFromUtf32(candidate.First().CodePoint));
                else
                    return new ReplaceResult(m.Length, "Character not found.");
            }
            else if ((m = Regex.Match(buffer, @"\{findall\s+([^\{\}]+?)\s*\}$")).Success && m.Groups[1].Length > 0)
            {
                var input = m.Groups[1].Value;
                string[] words = input.Length == 0 ? null : input.Split(' ').Where(s => s.Length > 0).Select(s => s.ToUpperInvariant()).ToArray();
                if (words == null || words.Length < 1)
                    return new ReplaceResult(m.Length, "No search terms given.");
                var candidates = UnicodeData
                    .Where(kvp => words.All(w => kvp.Value.Contains(w)))
                    .Select(kvp => new SearchItem { CodePoint = kvp.Key, Name = kvp.Value, Score = words.Sum(w => Regex.IsMatch(kvp.Value, "\\b" + Regex.Escape(w) + "\\b") ? 20 : 10) - (kvp.Value.Length / 3) })
                    .OrderByDescending(item => item.Score).AsEnumerable();
                var candidatesStr = candidates
                    .Select(si => char.ConvertFromUtf32(si.CodePoint) + "    " + si.GetReplacer(Settings.Replacers) + "    0x" + si.CodePoint.ToString("X") + "    " + si.Name + Environment.NewLine)
                    .JoinString();
                if (candidatesStr.Length > 0)
                    Clipboard.SetText(candidatesStr);
                else
                    Clipboard.Clear();
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
            else if ((m = Regex.Match(buffer, @"\{c ([^\{\}]+)\}$")).Success)
            {
                var input = m.Groups[1].Value;
                var output = string.Empty;
                while (input.Length > 0)
                {
                    int i = 4;
                    if (input.StartsWith("shch"))
                        output += "щ";
                    else if (input.StartsWith("Shch") || input.StartsWith("SHCH"))
                        output += "Щ";
                    else
                    {
                        i = 2;
                        if (input.StartsWith("yo"))
                            output += "ё";
                        else if (input.StartsWith("zh"))
                            output += "ж";
                        else if (input.StartsWith("ch"))
                            output += "ч";
                        else if (input.StartsWith("sh"))
                            output += "ш";
                        else if (input.StartsWith("eh"))
                            output += "э";
                        else if (input.StartsWith("yu"))
                            output += "ю";
                        else if (input.StartsWith("ya"))
                            output += "я";
                        else if (input.StartsWith("Yo") || input.StartsWith("YO"))
                            output += "Ё";
                        else if (input.StartsWith("Zh") || input.StartsWith("ZH"))
                            output += "Ж";
                        else if (input.StartsWith("Ch") || input.StartsWith("CH"))
                            output += "Ч";
                        else if (input.StartsWith("Sh") || input.StartsWith("SH"))
                            output += "Ш";
                        else if (input.StartsWith("Eh") || input.StartsWith("EH"))
                            output += "Э";
                        else if (input.StartsWith("Yu") || input.StartsWith("YU"))
                            output += "Ю";
                        else if (input.StartsWith("Ya") || input.StartsWith("YA"))
                            output += "Я";
                        else
                        {
                            i = 1;
                            switch (input[0])
                            {
                                case 'a': output += "а"; break;
                                case 'b': output += "б"; break;
                                case 'v': output += "в"; break;
                                case 'g': output += "г"; break;
                                case 'd': output += "д"; break;
                                case 'e': output += "е"; break;
                                case 'z': output += "з"; break;
                                case 'i': output += "и"; break;
                                case 'j': output += "й"; break;
                                case 'k': output += "к"; break;
                                case 'l': output += "л"; break;
                                case 'm': output += "м"; break;
                                case 'n': output += "н"; break;
                                case 'o': output += "о"; break;
                                case 'p': output += "п"; break;
                                case 'r': output += "р"; break;
                                case 's': output += "с"; break;
                                case 't': output += "т"; break;
                                case 'u': output += "у"; break;
                                case 'f': output += "ф"; break;
                                case 'x': output += "х"; break;
                                case 'c': output += "ц"; break;
                                case '`': output += "ъ"; break;
                                case 'y': output += "ы"; break;
                                case '\'': output += "ь"; break;
                                case 'A': output += "А"; break;
                                case 'B': output += "Б"; break;
                                case 'V': output += "В"; break;
                                case 'G': output += "Г"; break;
                                case 'D': output += "Д"; break;
                                case 'E': output += "Е"; break;
                                case 'Z': output += "З"; break;
                                case 'I': output += "И"; break;
                                case 'J': output += "Й"; break;
                                case 'K': output += "К"; break;
                                case 'L': output += "Л"; break;
                                case 'M': output += "М"; break;
                                case 'N': output += "Н"; break;
                                case 'O': output += "О"; break;
                                case 'P': output += "П"; break;
                                case 'R': output += "Р"; break;
                                case 'S': output += "С"; break;
                                case 'T': output += "Т"; break;
                                case 'U': output += "У"; break;
                                case 'F': output += "Ф"; break;
                                case 'X': output += "Х"; break;
                                case 'C': output += "Ц"; break;
                                case '~': output += "Ъ"; break;
                                case 'Y': output += "Ы"; break;
                                case '"': output += "Ь"; break;
                                default: output += input[0]; break;
                            }
                        }
                    }
                    input = input.Substring(i);
                }
                return new ReplaceResult(m.Length, output);
            }

            foreach (var repl in Settings.Replacers.OrderByDescending(kvp => kvp.Input.Length))
                if (buffer.EndsWith(repl.Input, StringComparison.Ordinal))
                    return new ReplaceResult(repl.Input.Length, repl.ReplaceWith);
            return null;
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
