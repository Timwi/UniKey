using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Windows.Forms;
using System.Xml.Linq;
using RT.Util;
using RT.Util.Dialogs;
using RT.Util.ExtensionMethods;
using RT.Util.Xml;
using UniKey.Properties;

namespace UniKey
{
    static class Program
    {
        static Settings Settings;
        static GlobalKeyboardListener KeyboardListener;
        static List<Keys> Pressed = new List<Keys>();
        static bool Processing = false;
        static string Buffer = string.Empty;
        static int LastBufferCheck = 0;
        static string UndoBufferFrom = null;
        static string UndoBufferTo = null;
        static string Password = null;

        static Control GuiThreadInvoker;

        static CommandInfo[] Commands = Ut.NewArray
        (
            new CommandInfo(@"\{exit\}$", "{exit}", "Exits UniKey.",
                m =>
                {
                    GuiThreadInvoker.BeginInvoke(new Action(() => { Application.Exit(); }));
                    return new ReplaceResult(m.Length, "Exiting.");
                }),

            new CommandInfo(@"\{del ([^\{\}]+|\{[^\{\}]+\})\}$", "{del <key>}",
                @"Deletes the specified key from the replacements dictionary. The key may be surrounded by curly braces, but may otherwise not contain any '{' or '}'.",
                m => del(m.Groups[1].Value, m.Length)),

            new CommandInfo(@"\{add ([^\{\}]+|\{[^\{\}]+\})\}$", "{add <key>}",
                @"Adds a new entry to the replacements dictionary. The key is specified, the replacement value is taken from the clipboard. The key may be surrounded by curly braces, but may otherwise not contain any '{' or '}'.",
                m => add(m.Groups[1].Value, m.Length)),

            new CommandInfo(@"\{ren ([^ \{\}]+) ([^ \{\}]+)\}$", "{ren <oldkey> <newkey>}",
                @"Changes the key for an existing replacement rule. Each key may be surrounded by curly braces, but may otherwise not contain any '{' or '}'.",
                m => ren(m.Groups[1].Value, m.Groups[2].Value, m.Length)),

            new CommandInfo(@"\{setpassword ([^\{\}]+)\}$", "{setpassword <newpassword>}",
                @"Encrypts the UniKey data file using the specified password. You will be prompted for the password every time UniKey starts. If you forget the password, you will not be able to retrieve your UniKey data.",
                m => { Password = m.Groups[1].Value; save(); return new ReplaceResult(m.Length, "done"); }),

            new CommandInfo(@"\{removepassword\}$", "{removepassword}",
                @"Saves the UniKey data file unencrypted. You will no longer be prompted for a password when UniKey starts.",
                m => { Password = null; save(); return new ReplaceResult(m.Length, "done"); }),

            new CommandInfo(@"\{help\}$", "{help}", @"Displays this help screen.", m => help(m.Length)),

            new CommandInfo(@"\{find\s+([^\{\}]+?)\s*\}$", @"{find <words>}",
                @"Searches for a Unicode character using the specified keywords and outputs the best match.",
                m => find(m.Groups[1].Value, m.Length)),

            new CommandInfo(@"\{findall\s+([^\{\}]+?)\s*\}$", @"{findall <words>}",
                @"FInds all Unicode characters whose names contain the specified words, and places a tabular list of those characters in the clipboard.",
                m => findAll(m.Groups[1].Value, m.Length)),

            new CommandInfo(@"\{html\}$", @"{html}", @"HTML-escapes the current contents of the clipboard and outputs the result as keystrokes.",
                m => new ReplaceResult(m.Length, Clipboard.GetText().HtmlEscape())),

            new CommandInfo(@"\{url\}$", @"{url}", @"URL-escapes the current contents of the clipboard and outputs the result as keystrokes.",
                m => new ReplaceResult(m.Length, Clipboard.GetText().UrlEscape())),

            new CommandInfo(@"\{unurl\}$", @"{unurl}", @"Reverses URL escaping in the current contents of the clipboard and outputs the result as keystrokes.",
                m => new ReplaceResult(m.Length, Clipboard.GetText().UrlUnescape())),

            new CommandInfo(@"\{u ([0-9a-f]+)\}$", @"{u <hexadecimal codepoint>}", @"Outputs the specified Unicode character as a keystroke.",
                m =>
                {
                    int i;
                    return int.TryParse(m.Groups[1].Value, NumberStyles.HexNumber, null, out i)
                        ? new ReplaceResult(m.Length, char.ConvertFromUtf32(i))
                        : new ReplaceResult(m.Length, "Invalid codepoint.");
                }),

            new CommandInfo(@"\{c ([^\{\}]+)\}$", "{c <text>}", @"Converts the specified text to Cyrillic.",
                m => new ReplaceResult(m.Length, Conversions.Convert(Conversions.Cyrillic, m.Groups[1].Value))),
            new CommandInfo(@"\{el ([^\{\}]+)\}$", "{el <text>}", @"Converts the specified text to Greek.",
                m => new ReplaceResult(m.Length, Conversions.Convert(Conversions.Greek, m.Groups[1].Value))),
            new CommandInfo(@"\{hi ([^\{\}]+)\}$", "{hi <text>}", @"Converts the specified text to Hiragana.",
                m => new ReplaceResult(m.Length, Conversions.Convert(Conversions.Hiragana, m.Groups[1].Value))),
            new CommandInfo(@"\{ka ([^\{\}]+)\}$", "{ka <text>}", @"Converts the specified text to Katakana.",
                m => new ReplaceResult(m.Length, Conversions.Convert(Conversions.Katakana, m.Groups[1].Value)))
        );

        private static ReplaceResult del(string key, int length)
        {
            if (!Settings.Replacers.ContainsKey(key))
                return new ReplaceResult(length, "not found");
            Settings.Replacers.Remove(key);
            save();
            return new ReplaceResult(length, "done");
        }

        private static ReplaceResult add(string key, int length)
        {
            var newRepl = Clipboard.GetText();
            string existing;
            if (Settings.Replacers.TryGetValue(key, out existing))
            {
                Settings.Replacers[key] = newRepl;
                save();
                return new ReplaceResult(length, "updated: {0} ⇒ {1}".Fmt(existing, newRepl));
            }
            Settings.Replacers[key] = newRepl;
            save();
            return new ReplaceResult(length, "added: " + newRepl);
        }

        private static ReplaceResult ren(string oldInput, string newInput, int length)
        {
            if (!Settings.Replacers.ContainsKey(oldInput))
                return new ReplaceResult(length, "not found");
            if (Settings.Replacers.ContainsKey(newInput))
                return new ReplaceResult(length, "failed: new key already exists");
            var replaceWith = Settings.Replacers[oldInput];
            Settings.Replacers.Remove(oldInput);
            Settings.Replacers[newInput] = replaceWith;
            save();
            return new ReplaceResult(length, "done");
        }

        private static ReplaceResult find(string input, int length)
        {
            string[] words = input.Length == 0 ? null : input.Split(' ').Where(s => s.Length > 0).Select(s => s.ToUpperInvariant()).ToArray();
            if (words == null || words.Length < 1)
                return new ReplaceResult(length, "No search terms given.");
            var candidate = FindCharacters(words).MaxElementOrDefault(item => item.Score);
            if (candidate != null)
                return new ReplaceResult(length, char.ConvertFromUtf32(candidate.CodePoint));
            else
                return new ReplaceResult(length, "Character not found.");
        }

        private static ReplaceResult findAll(string input, int length)
        {
            string[] words = input.Length == 0 ? null : input.Split(' ').Where(s => s.Length > 0).Select(s => s.ToUpperInvariant()).ToArray();
            if (words == null || words.Length < 1)
                return new ReplaceResult(length, "No search terms given.");
            var candidatesStr = FindCharacters(words)
                .Select(si => char.ConvertFromUtf32(si.CodePoint) + "    " + si.GetReplacer(Settings.Replacers) + "    0x" + si.CodePoint.ToString("X") + "    " + si.Name + Environment.NewLine)
                .JoinString();
            if (candidatesStr.Length > 0)
                Clipboard.SetText(candidatesStr);
            else
                Clipboard.SetText("No character found matching: " + words.JoinString(" "));
            return new ReplaceResult(length, "");
        }

        private static ReplaceResult help(int length)
        {
            GuiThreadInvoker.BeginInvoke(new Action(() =>
            {
                var str = new StringBuilder();
                foreach (var info in Commands.OrderBy(cmd => cmd.CommandName))
                {
                    str.AppendLine(info.CommandName);
                    str.AppendLine("    " + info.HelpString);
                    str.AppendLine();
                }
                DlgMessage.Show(str.ToString(), "UniKey Commands", DlgType.Info);
            }));
            return new ReplaceResult(length, "");
        }

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

        static void parse(string input, Dictionary<string, string> addTo)
        {
            string[] items = input.Replace("\r", "").Replace("\n", "").Split(';');
            foreach (string item in items)
            {
                int i = item.IndexOf('>');
                if (i > 0)
                    addTo.Add(item.Substring(0, i), item.Substring(i + 1));
            }
        }

        private static byte[] _iv = "A,EW9%9Enp{1!oiN".ToUtf8();
        private static byte[] _salt = "kdSkeuDkj3%k".ToUtf8();

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

            _isNumLockOn = Control.IsKeyLocked(Keys.NumLock);

            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);

            try
            {
                bool usePw = true;
                using (var f = File.Open(PathUtil.AppPathCombine(@"UniKey.settings.xml"), FileMode.Open))
                {
                    var start = f.Read(5).FromUtf8();
                    if (start == "passw")
                    {
                        Password = InputBox.GetLine("Enter password:", caption: "Password");
                        if (Password == null) return;
                        var passwordDeriveBytes = new PasswordDeriveBytes(Password, _salt);
                        var key = passwordDeriveBytes.GetBytes(16);
                        var rij = Rijndael.Create();
                        var rijDec = rij.CreateDecryptor(key, _iv);
                        try
                        {
                            using (var cStream = new CryptoStream(f, rijDec, CryptoStreamMode.Read))
                                Settings = XmlClassify.ObjectFromXElement<Settings>(XElement.Parse(cStream.ReadAllBytes().FromUtf8()));
                        }
                        catch (Exception e)
                        {
                            DlgMessage.Show("Could not decrypt UniKey.settings.xml:\n{0}\nPassword may be wrong. Exiting.".Fmt(e.Message), "Error", DlgType.Error);
                            return;
                        }
                    }
                    else
                        usePw = false;
                }
                if (!usePw)
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

            GuiThreadInvoker = new Form();
            var _ = GuiThreadInvoker.Handle;
            KeyboardListener = new GlobalKeyboardListener();
            KeyboardListener.HookAllKeys = true;
            KeyboardListener.KeyDown += keyDown;
            KeyboardListener.KeyUp += keyUp;
            Application.Run();
        }

        static ReplaceResult GetReplace(string buffer)
        {
            Match m;

            foreach (var command in Commands)
                if ((m = Regex.Match(buffer, command.Regex)).Success)
                    return command.Function(m);

            foreach (var repl in Settings.Replacers.Keys.OrderByDescending(key => key.Length))
                if (buffer.EndsWith(repl, StringComparison.Ordinal))
                    return new ReplaceResult(repl.Length, Settings.Replacers[repl]);
            return null;
        }

        private static IEnumerable<SearchItem> FindCharacters(string[] words)
        {
            var candidates = UnicodeData
                .Where(kvp => words.All(w => kvp.Value.Contains(w)))
                .Select(kvp =>
                {
                    var split = kvp.Value.Split(' ');
                    return new SearchItem
                    {
                        CodePoint = kvp.Key,
                        Name = kvp.Value,
                        Score =
                            split.All(words.Contains) && words.All(split.Contains) ? 1000 :
                            split.Where(s => s.Length > 0).Select(s => s.Trim()).Sum(w => words.Contains(w) ? 20 : words.Any(w2 => w.Contains(w2)) ? 0 : -2)
                    };
                })
                .OrderByDescending(item => item.Score).AsEnumerable();
            return candidates;
        }

        static bool save()
        {
            try
            {
                var fileName = PathUtil.AppPathCombine("UniKey.settings.xml");
                if (Password != null)
                {
                    var passwordDeriveBytes = new PasswordDeriveBytes(Password, _salt);
                    var key = passwordDeriveBytes.GetBytes(16);
                    var rij = Rijndael.Create();

                    var rijEnc = rij.CreateEncryptor(key, _iv);
                    using (var outputStream = File.Open(fileName, FileMode.Create))
                    {
                        outputStream.Write("passw".ToUtf8());
                        using (var cStream = new CryptoStream(outputStream, rijEnc, CryptoStreamMode.Write))
                            cStream.Write(XmlClassify.ObjectToXElement(Settings).ToString().ToUtf8());
                    }
                }
                else
                    XmlClassify.SaveObjectToXmlFile(Settings, fileName);
            }
            catch (Exception e)
            {
                DlgMessage.Show("Error saving UniKey.settings.xml: " + e.Message, "Error", DlgType.Error, "OK");
                return false;
            }
            return true;
        }

        static byte[] keyboardState = new byte[256];
        static string getCharsFromKeys(Keys keys, bool shift)
        {
            var buf = new StringBuilder(16);
            keyboardState[(int) Keys.ShiftKey] = (byte) (shift ? 0xff : 0);
            WinAPI.ToUnicode((uint) keys, 0, keyboardState, buf, 16, 0);
            return buf.ToString();
        }

        static Keys[] _numKeys = new[] { Keys.NumLock, Keys.NumPad0, Keys.NumPad1, Keys.NumPad2, Keys.NumPad3, Keys.NumPad4,
            Keys.NumPad5, Keys.NumPad6, Keys.NumPad7, Keys.NumPad8, Keys.NumPad9, Keys.Multiply, Keys.Add, Keys.Subtract, Keys.Divide, Keys.Decimal };

        static void keyDown(object sender, GlobalKeyEventArgs e)
        {
            if (Processing)
                return;
            Processing = true;
            try
            {
#if DEBUG_LOG
                var buf = Encoding.UTF8.GetBytes("Down: " + e.KeyCode.ToString() + "\r\n");
                using (var f = File.Open(@"C:\temp\log", FileMode.Append, FileAccess.Write, FileShare.Write))
                {
                    f.Write(buf, 0, buf.Length);
                    f.Close();
                }
#endif

                //if (_numKeys.Contains(e.VirtualKeyCode))
                //    GuiThreadInvoker.BeginInvoke(new Action<Keys>(processNumPad), e.VirtualKeyCode);

                if (!Pressed.Contains(e.VirtualKeyCode))
                    Pressed.Add(e.VirtualKeyCode);

                if (_emptyKeys.Contains(e.VirtualKeyCode))
                {
                    Buffer = string.Empty;
                    UndoBufferFrom = null;
                }
                else if (e.VirtualKeyCode == Keys.Back && Buffer.Length > 0)
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
                else
                {
                    bool alt = Pressed.Contains(Keys.LMenu) || Pressed.Contains(Keys.RMenu);
                    bool ctrl = Pressed.Contains(Keys.LControlKey) || Pressed.Contains(Keys.RControlKey);
                    bool shift = Pressed.Contains(Keys.LShiftKey) || Pressed.Contains(Keys.RShiftKey);

                    if (!alt && !ctrl)
                    {
                        // special-case the secondary backslash key
                        var str = (e.VirtualKeyCode == Keys.Oem5) ? "←" : getCharsFromKeys(e.VirtualKeyCode, shift);
                        Buffer += str;
                        if (UndoBufferFrom != null)
                        {
                            UndoBufferFrom += str;
                            UndoBufferTo += str;
                        }
                    }
                }
            }
            finally
            {
                Processing = false;
            }
        }

        private static Form _gridForm;
        private static Screen _gridFormScreen;
        private static bool _isNumLockOn;
        private static bool _rightMouseButton;
        private static Point? _dragStartFrom;

        private static void closeGridForm()
        {
            _gridForm.Close();
            _gridForm.Dispose();
            _gridForm = null;
        }

        private static void processNumPad(Keys key)
        {
            var newBounds = _gridForm == null ? Screen.PrimaryScreen.Bounds : _gridForm.Bounds;

            if (key == Keys.NumLock)
            {
                _isNumLockOn = !_isNumLockOn;
                if (_gridForm != null)
                    closeGridForm();
                if (_isNumLockOn)
                {
                    _gridFormScreen = Screen.PrimaryScreen;
                    _gridForm = new Form { FormBorderStyle = FormBorderStyle.None, MinimizeBox = false, MaximizeBox = false };
                    _gridForm.TopMost = true;
                    newBounds = _gridFormScreen.Bounds;
                    _gridForm.Paint += (s, e) =>
                    {
                        var brush = new HatchBrush(HatchStyle.Percent50, Color.White, Color.Black);
                        e.Graphics.FillRectangle(brush, 0, 0, _gridFormScreen.Bounds.Width, _gridFormScreen.Bounds.Height);
                    };
                    _rightMouseButton = false;
                    _dragStartFrom = null;
                }
            }
            else
            {
                if (_isNumLockOn != Control.IsKeyLocked(Keys.NumLock))
                    throw new InvalidOperationException("Num Lock state inconsistent");
                if (_gridForm == null)
                    return;

                var pos = new Point(_gridForm.Left + _gridForm.Width / 2, _gridForm.Top + _gridForm.Height / 2);

                if (key == Keys.Subtract)
                    _rightMouseButton = true;
                else if (key == Keys.Divide)
                    _rightMouseButton = false;
                else if (key == Keys.Add)
                {
                    closeGridForm();
                    WinAPI.mouse_event(_rightMouseButton ? WinAPI.MOUSEEVENTF_RIGHTDOWN : WinAPI.MOUSEEVENTF_LEFTDOWN, pos.X, pos.Y, 0, 0);
                    WinAPI.mouse_event(_rightMouseButton ? WinAPI.MOUSEEVENTF_RIGHTUP : WinAPI.MOUSEEVENTF_LEFTUP, pos.X, pos.Y, 0, 0);
                    WinAPI.mouse_event(_rightMouseButton ? WinAPI.MOUSEEVENTF_RIGHTDOWN : WinAPI.MOUSEEVENTF_LEFTDOWN, pos.X, pos.Y, 0, 0);
                    WinAPI.mouse_event(_rightMouseButton ? WinAPI.MOUSEEVENTF_RIGHTUP : WinAPI.MOUSEEVENTF_LEFTUP, pos.X, pos.Y, 0, 0);
                    //WinAPI.keybd_event((byte) Keys.NumLock, 0x45, 0, 0);
                    //WinAPI.keybd_event((byte) Keys.NumLock, 0x45, WinAPI.KEYEVENTF_KEYUP, 0);
                }
                else if (key == Keys.NumPad0)
                {
                    _dragStartFrom = pos;
                    newBounds = _gridFormScreen.Bounds;
                }
                else if (key == Keys.Decimal)
                {
                    closeGridForm();
                    var from = _dragStartFrom ?? pos;
                    Cursor.Position = from;
                    WinAPI.mouse_event(_rightMouseButton ? WinAPI.MOUSEEVENTF_RIGHTDOWN : WinAPI.MOUSEEVENTF_LEFTDOWN, from.X, from.Y, 0, 0);
                    Cursor.Position = pos;
                    WinAPI.mouse_event(_rightMouseButton ? WinAPI.MOUSEEVENTF_RIGHTUP : WinAPI.MOUSEEVENTF_LEFTUP, pos.X, pos.Y, 0, 0);
                    //WinAPI.keybd_event((byte) Keys.NumLock, 0x45, 0, 0);
                    //WinAPI.keybd_event((byte) Keys.NumLock, 0x45, WinAPI.KEYEVENTF_KEYUP, 0);
                }
                else if (key == Keys.NumPad7)   // top left
                    newBounds = new Rectangle(newBounds.X, newBounds.Y, newBounds.Width / 3, newBounds.Height / 3);
                else if (key == Keys.NumPad8)   // top
                    newBounds = new Rectangle(newBounds.X + newBounds.Width / 3, newBounds.Y, newBounds.Width / 3, newBounds.Height / 3);
                else if (key == Keys.NumPad9)   // top right
                    newBounds = new Rectangle(newBounds.X + 2 * newBounds.Width / 3, newBounds.Y, newBounds.Width / 3, newBounds.Height / 3);
                else if (key == Keys.NumPad4)   // middle left
                    newBounds = new Rectangle(newBounds.X, newBounds.Y + newBounds.Height / 3, newBounds.Width / 3, newBounds.Height / 3);
                else if (key == Keys.NumPad5)   // center
                    newBounds = new Rectangle(newBounds.X + newBounds.Width / 3, newBounds.Y + newBounds.Height / 3, newBounds.Width / 3, newBounds.Height / 3);
                else if (key == Keys.NumPad6)   // middle right
                    newBounds = new Rectangle(newBounds.X + 2 * newBounds.Width / 3, newBounds.Y + newBounds.Height / 3, newBounds.Width / 3, newBounds.Height / 3);
                else if (key == Keys.NumPad1)   // bottom left
                    newBounds = new Rectangle(newBounds.X, newBounds.Y + 2 * newBounds.Height / 3, newBounds.Width / 3, newBounds.Height / 3);
                else if (key == Keys.NumPad2)   // bottom
                    newBounds = new Rectangle(newBounds.X + newBounds.Width / 3, newBounds.Y + 2 * newBounds.Height / 3, newBounds.Width / 3, newBounds.Height / 3);
                else if (key == Keys.NumPad3)   // bottom right
                    newBounds = new Rectangle(newBounds.X + 2 * newBounds.Width / 3, newBounds.Y + 2 * newBounds.Height / 3, newBounds.Width / 3, newBounds.Height / 3);
            }

            if (_gridForm != null)
            {
                if (newBounds.Width == 0)
                    newBounds.Width = 1;
                if (newBounds.Height == 0)
                    newBounds.Height = 1;
                _gridForm.Bounds = newBounds;

                var reg = new Region(new Rectangle(0, 0, _gridFormScreen.Bounds.Width, _gridFormScreen.Bounds.Height));
                var w = _gridForm.Bounds.Width / 3;
                var h = _gridForm.Bounds.Height / 3;

                reg.Xor(new Rectangle(1, 1, w - 1, h - 1));
                reg.Xor(new Rectangle(w + 1, 1, w - 1, h - 1));
                reg.Xor(new Rectangle(2 * w + 1, 1, _gridForm.Bounds.Width - 2 * w - 2, h - 1));

                reg.Xor(new Rectangle(1, h + 1, w - 1, h - 1));
                reg.Xor(new Rectangle(w + 1, h + 1, w - 1, h - 1));
                reg.Xor(new Rectangle(2 * w + 1, h + 1, _gridForm.Bounds.Width - 2 * w - 2, h - 1));

                reg.Xor(new Rectangle(1, 2 * h + 1, w - 1, _gridForm.Bounds.Height - 2 * h - 2));
                reg.Xor(new Rectangle(w + 1, 2 * h + 1, w - 1, _gridForm.Bounds.Height - 2 * h - 2));
                reg.Xor(new Rectangle(2 * w + 1, 2 * h + 1, _gridForm.Bounds.Width - 2 * w - 2, _gridForm.Bounds.Height - 2 * h - 2));

                _gridForm.Region = reg;
                _gridForm.Show();
                Cursor.Position = new Point(_gridForm.Left + _gridForm.Width / 2, _gridForm.Top + _gridForm.Height / 2);
            }
        }

        static void keyUp(object sender, GlobalKeyEventArgs e)
        {
            if (Processing)
                return;
            Processing = true;
            try
            {
#if DEBUG_LOG
                var buf = Encoding.UTF8.GetBytes("Up: " + e.KeyCode.ToString() + "\r\n");
                using (var f = File.Open(@"C:\temp\log", FileMode.Append, FileAccess.Write, FileShare.Write))
                {
                    f.Write(buf, 0, buf.Length);
                    f.Close();
                }
#endif

                Pressed.Remove(e.VirtualKeyCode);

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

                        var replace = GetReplace(Buffer.Substring(0, LastBufferCheck));
                        if (replace != null)
                        {
                            UndoBufferFrom = replace.ReplaceWith + Buffer.Substring(LastBufferCheck);
                            UndoBufferTo = Buffer.Substring(LastBufferCheck - replace.ReplaceLength, replace.ReplaceLength) + Buffer.Substring(LastBufferCheck);
                            Buffer = Buffer.Substring(0, LastBufferCheck - replace.ReplaceLength) + replace.ReplaceWith + Buffer.Substring(LastBufferCheck);
                            LastBufferCheck += replace.ReplaceWith.Length - replace.ReplaceLength;
                        }
                        else if (UndoBufferFrom != null && LastBufferCheck == Buffer.Length && Buffer[LastBufferCheck - 1] == '←')
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

        private static Keys[] _emptyKeys = Ut.NewArray<Keys>(Keys.LControlKey, Keys.RControlKey, Keys.Escape,
                            Keys.Enter, Keys.Return, Keys.Tab,
                            Keys.LMenu, Keys.RMenu, Keys.Up,
                            Keys.Down, Keys.Left, Keys.Right,
                            Keys.End, Keys.Home, Keys.PageDown, Keys.PageUp,
                            Keys.F1, Keys.F2, Keys.F3, Keys.F4, Keys.F5, Keys.F6, Keys.F7, Keys.F8, Keys.F9, Keys.F10, Keys.F11, Keys.F12);

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
