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
        static MachineSettings MachineSettings;
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
                    Application.Exit();
                    return new ReplaceResult(m.Length, "Exiting.");
                }),

            new CommandInfo(@"\{del ([^\{\}]+|\{[^\{\}]+\})\}$", "{del <key>}",
                @"Deletes the specified key from the replacements dictionary. The key may be surrounded by curly braces, but may otherwise not contain any '{' or '}'.",
                m => del(m.Groups[1].Value, m.Length)),

            new CommandInfo(@"\{add ([^\{\}]+|\{[^\{\}]+\})\}$", "{add <key>}",
                @"Adds a new entry to the replacements dictionary. The key is specified, the replacement value is taken from the clipboard. The key may be surrounded by curly braces, but may otherwise not contain any '{' or '}'.",
                m => add(m.Groups[1].Value, m.Length)),

            new CommandInfo(@"\{ren ([^ \{\}]+) ([^ \{\}]+)\}$", "{ren <oldkey> <newkey>}",
                @"Changes the key for an existing replacement rule. Each key may be surrounded by curly braces, but may otherwise not contain any '{' or '}'. (Note this command does not work for keys containing spaces; you must use {del <key>} followed by {add <key>} for those.)",
                m => ren(m.Groups[1].Value, m.Groups[2].Value, m.Length)),

            new CommandInfo(@"\{setpassword ([^\{\}]+)\}$", "{setpassword <newpassword>}",
                @"Encrypts the UniKey data file using the specified password. You will be prompted for the password every time UniKey starts. If you forget the password, you will not be able to retrieve your UniKey data.",
                m => { Password = m.Groups[1].Value; saveLater(); return new ReplaceResult(m.Length, "done"); }),

            new CommandInfo(@"\{removepassword\}$", "{removepassword}",
                @"Saves the UniKey data file unencrypted. You will no longer be prompted for a password when UniKey starts.",
                m => { Password = null; saveLater(); return new ReplaceResult(m.Length, "done"); }),

            new CommandInfo(@"\{help\}$", "{help}", @"Displays this help screen.", m => help(m.Length)),

            new CommandInfo(@"\{f\s+([^\{\}]+?)\s*\}$", @"{f <words>}",
                @"Searches for a Unicode character using the specified keywords and outputs the best match.",
                m => find(m.Groups[1].Value, m.Length)),

            new CommandInfo(@"\{fa\s+([^\{\}]+?)\s*\}$", @"{fa <words>}",
                @"Finds all Unicode characters whose names contain the specified words, and places a tabular list of those characters in the clipboard.",
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

            new CommandInfo(@"\{cp( .)?\}$", @"{cp <character>}, {cp}", @"Outputs the hexadecimal Unicode codepoint value of the specified character, or the first character from the clipboard if none specified, as keystrokes.",
                m => new ReplaceResult(m.Length, (m.Groups[1].Length > 0 ? char.ConvertToUtf32(m.Groups[1].Value, 1) : char.ConvertToUtf32(Clipboard.GetText(), 0)).ToString("X4"))),

            //new CommandInfo(@"([^\]]+)(\W)$", "<text>", @"Converts all text to Cyrillic when Scroll Lock is on.",
            //    m => Control.IsKeyLocked(Keys.Scroll) ? new ReplaceResult(m.Length, Conversions.Convert(Conversions.CyrillicNative, m.Groups[1].Value) + m.Groups[2].Value) : null),

            new CommandInfo(@"\{c ([^\{\}]+)\}$", "{c <text>}", @"Converts the specified text to Cyrillic.",
                m => new ReplaceResult(m.Length, Conversions.Convert(Conversions.Cyrillic, m.Groups[1].Value))),
            new CommandInfo(@"\{el ([^\{\}]+)\}$", "{el <text>}", @"Converts the specified text to Greek.",
                m => new ReplaceResult(m.Length, Conversions.Convert(Conversions.Greek, m.Groups[1].Value))),
            new CommandInfo(@"\{hi ([^\{\}]+)\}$", "{hi <text>}", @"Converts the specified text to Hiragana.",
                m => new ReplaceResult(m.Length, Conversions.Convert(Conversions.Hiragana, m.Groups[1].Value))),
            new CommandInfo(@"\{ka ([^\{\}]+)\}$", "{ka <text>}", @"Converts the specified text to Katakana.",
                m => new ReplaceResult(m.Length, Conversions.Convert(Conversions.Katakana, m.Groups[1].Value))),

            new CommandInfo(@"\{set mousegrid (on|off)\}", "{set mousegrid <on/off>}",
                @"Enables or disables the mouse grid feature. The mouse grid is activated by turning Num Lock on and then operated using the keys on the NumPad.",
                m =>
                {
                    Settings.MouseGridEnabled = m.Groups[1].Value == "on";
                    saveLater();
                    return new ReplaceResult(m.Length, "Mouse grid now {0}.".Fmt(m.Groups[1].Value));
                })
        );

        private static ReplaceResult del(string key, int length)
        {
            if (!Settings.Replacers.ContainsKey(key))
                return new ReplaceResult(length, "not found");
            Settings.Replacers.Remove(key);
            saveLater();
            return new ReplaceResult(length, "done");
        }

        private static ReplaceResult add(string key, int length)
        {
            var newRepl = Clipboard.GetText();
            string existing;
            if (Settings.Replacers.TryGetValue(key, out existing))
            {
                Settings.Replacers[key] = newRepl;
                saveLater();
                return new ReplaceResult(length, "updated: {0} ⇒ {1}".Fmt(existing, newRepl));
            }
            Settings.Replacers[key] = newRepl;
            saveLater();
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
            saveLater();
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

            if (!loadSettings())
                return;

            GuiThreadInvoker = new Form();
            var _ = GuiThreadInvoker.Handle;

            var reloadSettingsFileTimer = new Timer { Enabled = false, Interval = 5000 };
            reloadSettingsFileTimer.Tick += delegate
            {
                reloadSettingsFileTimer.Enabled = false;
                if (!loadSettings())
                    Application.Exit();
            };

            var fsw = new FileSystemWatcher(Path.GetDirectoryName(MachineSettings.SettingsPathExpanded), Path.GetFileName(MachineSettings.SettingsPathExpanded))
            {
                IncludeSubdirectories = false,
                NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.Size,
            };

            FileSystemEventHandler scheduleReloadSettings = delegate
            {
                GuiThreadInvoker.BeginInvoke(new Action(() =>
                {
                    reloadSettingsFileTimer.Enabled = false;
                    reloadSettingsFileTimer.Enabled = true;
                }));
            };
            fsw.Changed += scheduleReloadSettings;
            fsw.Created += scheduleReloadSettings;
            fsw.EnableRaisingEvents = true;

            KeyboardListener = new GlobalKeyboardListener();
            KeyboardListener.HookAllKeys = true;
            KeyboardListener.KeyDown += keyDown;
            KeyboardListener.KeyUp += keyUp;
            Application.Run();
        }

        private static bool loadSettings()
        {
            SettingsUtil.LoadSettings(out MachineSettings);
            MachineSettings.SaveQuiet();
            again:
            try
            {
                bool usePw = true;
                using (var f = File.Open(MachineSettings.SettingsPathExpanded, FileMode.Open))
                {
                    var start = f.Read(5).FromUtf8();
                    if (start == "passw")
                    {
                        if (Password == null)
                            Password = InputBox.GetLine("Enter password:", caption: "Password");
                        if (Password == null)
                            return false;
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
                            DlgMessage.Show("Could not decrypt {0}:\n{1}\nPassword may be wrong. Exiting.".Fmt(MachineSettings.SettingsPathExpanded, e.Message), "Error", DlgType.Error, "E&xit UniKey");
                            return false;
                        }
                    }
                    else
                        usePw = false;
                }
                if (!usePw)
                    Settings = XmlClassify.LoadObjectFromXmlFile<Settings>(MachineSettings.SettingsPathExpanded);
            }
            catch (Exception e)
            {
                again2:
                var result = DlgMessage.Show("Could not load {0}: {1}".Fmt(MachineSettings.SettingsPathExpanded, e.Message), "Error", DlgType.Question, "&Create new file here", "&Browse for new/existing file", "E&xit UniKey");
                if (result == 2)
                    return false;
                if (result == 1)
                {
                    var dlg = new OpenFileDialog();
                    dlg.Title = "Choose the location of your settings file";
                    dlg.CheckPathExists = dlg.CheckFileExists = false;
                    if (dlg.ShowDialog() != DialogResult.OK)
                        goto again2;
                    MachineSettings.SettingsPathExpanded = dlg.FileName;
                    MachineSettings.SaveLoud();
                    goto again;
                }
                Settings = new Settings();
                if (!save())
                    return false;
            }
            return true;
        }

        private static ReplaceResult GetReplace(string buffer)
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

        static void saveLater()
        {
            GuiThreadInvoker.BeginInvoke(new Action(() => { save(); }));
        }

        static bool save()
        {
            try
            {
                if (Password != null)
                {
                    var passwordDeriveBytes = new PasswordDeriveBytes(Password, _salt);
                    var key = passwordDeriveBytes.GetBytes(16);
                    var rij = Rijndael.Create();

                    var rijEnc = rij.CreateEncryptor(key, _iv);
                    using (var outputStream = File.Open(MachineSettings.SettingsPathExpanded, FileMode.Create))
                    {
                        outputStream.Write("passw".ToUtf8());
                        using (var cStream = new CryptoStream(outputStream, rijEnc, CryptoStreamMode.Write))
                            cStream.Write(XmlClassify.ObjectToXElement(Settings).ToString().ToUtf8());
                    }
                }
                else
                    XmlClassify.SaveObjectToXmlFile(Settings, MachineSettings.SettingsPathExpanded);
            }
            catch (Exception e)
            {
                DlgMessage.Show("Error saving {0}: {1}".Fmt(MachineSettings.SettingsPathExpanded, e.Message), "Error", DlgType.Error, "OK");
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

        static Keys[] _numKeys = Ut.NewArray(
            // Without shift
            Keys.NumPad0, Keys.NumPad1, Keys.NumPad2, Keys.NumPad3, Keys.NumPad4, Keys.NumPad5, Keys.NumPad6, Keys.NumPad7, Keys.NumPad8, Keys.NumPad9, Keys.Decimal,
            // With shift
            Keys.Insert, Keys.End, Keys.Down, Keys.PageDown, Keys.Left, Keys.Clear, Keys.Right, Keys.Home, Keys.Up, Keys.PageUp, Keys.Delete,
            // Unshiftable
            Keys.Multiply, Keys.Add, Keys.Subtract, Keys.Divide, Keys.Escape
        );

        static void keyDown(object sender, GlobalKeyEventArgs e)
        {
            if (Processing)
                return;
            Processing = true;
            try
            {
#if DEBUG_LOG
                var buf = Encoding.UTF8.GetBytes("Down: {0} (NumLock: {1})\r\n".Fmt(e.VirtualKeyCode, Control.IsKeyLocked(Keys.NumLock)));
                using (var f = File.Open(@"C:\temp\log", FileMode.Append, FileAccess.Write, FileShare.Write))
                {
                    f.Write(buf, 0, buf.Length);
                    f.Close();
                }
#endif
                if (Settings.MouseGridEnabled)
                {
                    if (e.VirtualKeyCode == Keys.NumLock && Control.IsKeyLocked(Keys.NumLock))
                        setGridBounds(false, null, Rectangle.Empty, null, false);
                    else if (Control.IsKeyLocked(Keys.NumLock) && _numKeys.Contains(e.VirtualKeyCode))
                    {
                        var key =
                            e.VirtualKeyCode == Keys.Insert ? (Keys.ShiftKey | Keys.NumPad0) :
                            e.VirtualKeyCode == Keys.End ? (Keys.ShiftKey | Keys.NumPad1) :
                            e.VirtualKeyCode == Keys.Down ? (Keys.ShiftKey | Keys.NumPad2) :
                            e.VirtualKeyCode == Keys.PageDown ? (Keys.ShiftKey | Keys.NumPad3) :
                            e.VirtualKeyCode == Keys.Left ? (Keys.ShiftKey | Keys.NumPad4) :
                            e.VirtualKeyCode == Keys.Clear ? (Keys.ShiftKey | Keys.NumPad5) :
                            e.VirtualKeyCode == Keys.Right ? (Keys.ShiftKey | Keys.NumPad6) :
                            e.VirtualKeyCode == Keys.Home ? (Keys.ShiftKey | Keys.NumPad7) :
                            e.VirtualKeyCode == Keys.Up ? (Keys.ShiftKey | Keys.NumPad8) :
                            e.VirtualKeyCode == Keys.PageUp ? (Keys.ShiftKey | Keys.NumPad9) :
                            e.VirtualKeyCode == Keys.Delete ? (Keys.ShiftKey | Keys.Decimal) : e.VirtualKeyCode;
                        if (processNumPad(key))
                        {
                            e.Handled = true;
                            return;
                        }
                    }
                }

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
        private static Stack<Rectangle> _gridUndo = new Stack<Rectangle>();
        private static bool _rightMouseButton;
        private static Point? _dragStartFrom;

        private static void setGridBounds(bool shown, Screen screen, Rectangle bounds, Action extraAction, bool regionFirst)
        {
            GuiThreadInvoker.BeginInvoke(new Action(() =>
            {
                if (shown)
                {
                    _gridFormScreen = screen;

                    if (_gridForm == null)
                    {
                        _gridForm = new Form
                        {
                            FormBorderStyle = FormBorderStyle.None,
                            MinimizeBox = false,
                            MaximizeBox = false,
                            StartPosition = FormStartPosition.Manual,
                            ShowInTaskbar = false
                        };
                        _gridForm.TopMost = true;
                        _gridForm.Paint += (s, e) =>
                        {
                            var brush = new HatchBrush(HatchStyle.Percent50, Color.White, Color.Black);
                            e.Graphics.FillRectangle(brush, 0, 0, screen.Bounds.Width, _gridFormScreen.Bounds.Height);
                        };
                        _rightMouseButton = false;
                        _dragStartFrom = null;
                    }

                    if (bounds.Width == 0)
                        bounds.Width = 1;
                    if (bounds.Height == 0)
                        bounds.Height = 1;

                    var reg = new Region(new Rectangle(0, 0, bounds.Width, bounds.Height));
                    var w = bounds.Width / 3;
                    var h = bounds.Height / 3;

                    reg.Xor(new Rectangle(1, 1, w - 1, h - 1));
                    reg.Xor(new Rectangle(w + 1, 1, w - 1, h - 1));
                    reg.Xor(new Rectangle(2 * w + 1, 1, bounds.Width - 2 * w - 2, h - 1));

                    reg.Xor(new Rectangle(1, h + 1, w - 1, h - 1));
                    reg.Xor(new Rectangle(w + 1, h + 1, w - 1, h - 1));
                    reg.Xor(new Rectangle(2 * w + 1, h + 1, bounds.Width - 2 * w - 2, h - 1));

                    reg.Xor(new Rectangle(1, 2 * h + 1, w - 1, bounds.Height - 2 * h - 2));
                    reg.Xor(new Rectangle(w + 1, 2 * h + 1, w - 1, bounds.Height - 2 * h - 2));
                    reg.Xor(new Rectangle(2 * w + 1, 2 * h + 1, bounds.Width - 2 * w - 2, bounds.Height - 2 * h - 2));

                    if (regionFirst)
                    {
                        _gridForm.Region = reg;
                        _gridForm.Bounds = bounds;
                    }
                    else
                    {
                        _gridForm.Bounds = bounds;
                        _gridForm.Region = reg;
                    }
                    _gridForm.Show();

                    Cursor.Position = new Point(_gridForm.Left + _gridForm.Width / 2, _gridForm.Top + _gridForm.Height / 2);
                }
                else
                {
                    if (_gridForm != null)
                    {
                        _gridForm.Close();
                        _gridForm.Dispose();
                        _gridForm = null;
                    }
                }
                if (extraAction != null)
                    extraAction();
            }));
        }

        private static bool processNumPad(Keys key)
        {
            if (_gridForm == null)
                _gridUndo.Clear();

            bool newShown = _gridForm != null;
            Screen newScreen = _gridForm != null ? _gridFormScreen : Screen.PrimaryScreen;
            Rectangle newBounds = _gridForm != null ? _gridForm.Bounds : Screen.PrimaryScreen.Bounds;
            Point pos = new Point(newBounds.Left + newBounds.Width / 2, newBounds.Top + newBounds.Height / 2);
            Action execute = null;
            bool regionFirst = false;

            var keyWithoutShift = key & ~Keys.ShiftKey;
            var shift = (key & Keys.ShiftKey) == Keys.ShiftKey;

            switch (keyWithoutShift)
            {
                case Keys.NumLock:
                    newShown = false;
                    break;

                case Keys.Multiply:
                    if (_gridUndo.Count > 0)
                    {
                        newBounds = _gridUndo.Pop();
                        regionFirst = true;
                    }
                    break;

                case Keys.Subtract:
                    _rightMouseButton = true;
                    newShown = true;
                    break;
                case Keys.Divide:
                    _rightMouseButton = false;
                    newShown = true;
                    break;

                case Keys.Add:
                    newShown = false;
                    execute = () =>
                    {
                        WinAPI.mouse_event(_rightMouseButton ? WinAPI.MOUSEEVENTF_RIGHTDOWN : WinAPI.MOUSEEVENTF_LEFTDOWN, pos.X, pos.Y, 0, 0);
                        WinAPI.mouse_event(_rightMouseButton ? WinAPI.MOUSEEVENTF_RIGHTUP : WinAPI.MOUSEEVENTF_LEFTUP, pos.X, pos.Y, 0, 0);
                        WinAPI.mouse_event(_rightMouseButton ? WinAPI.MOUSEEVENTF_RIGHTDOWN : WinAPI.MOUSEEVENTF_LEFTDOWN, pos.X, pos.Y, 0, 0);
                        WinAPI.mouse_event(_rightMouseButton ? WinAPI.MOUSEEVENTF_RIGHTUP : WinAPI.MOUSEEVENTF_LEFTUP, pos.X, pos.Y, 0, 0);
                    };
                    break;

                case Keys.NumPad0:
                    _dragStartFrom = pos;
                    newBounds = _gridFormScreen.Bounds;
                    break;

                case Keys.Decimal:
                    newShown = false;
                    var from = _dragStartFrom ?? pos;
                    execute = () =>
                    {
                        Cursor.Position = from;
                        WinAPI.mouse_event(_rightMouseButton ? WinAPI.MOUSEEVENTF_RIGHTDOWN : WinAPI.MOUSEEVENTF_LEFTDOWN, from.X, from.Y, 0, 0);
                        Cursor.Position = pos;
                        WinAPI.mouse_event(_rightMouseButton ? WinAPI.MOUSEEVENTF_RIGHTUP : WinAPI.MOUSEEVENTF_LEFTUP, pos.X, pos.Y, 0, 0);
                    };
                    break;

                case Keys.NumPad7:
                case Keys.NumPad8:
                case Keys.NumPad9:
                case Keys.NumPad4:
                case Keys.NumPad5:
                case Keys.NumPad6:
                case Keys.NumPad1:
                case Keys.NumPad2:
                case Keys.NumPad3:
                    if (shift && newShown)
                    {
                        newBounds =
                            keyWithoutShift == Keys.NumPad7 ? new Rectangle(newBounds.X - newBounds.Width, newBounds.Y - newBounds.Height, newBounds.Width, newBounds.Height) :
                            keyWithoutShift == Keys.NumPad8 ? new Rectangle(newBounds.X, newBounds.Y - newBounds.Height, newBounds.Width, newBounds.Height) :
                            keyWithoutShift == Keys.NumPad9 ? new Rectangle(newBounds.X + newBounds.Width, newBounds.Y - newBounds.Height, newBounds.Width, newBounds.Height) :
                            keyWithoutShift == Keys.NumPad4 ? new Rectangle(newBounds.X - newBounds.Width, newBounds.Y, newBounds.Width, newBounds.Height) :
                            keyWithoutShift == Keys.NumPad6 ? new Rectangle(newBounds.X + newBounds.Width, newBounds.Y, newBounds.Width, newBounds.Height) :
                            keyWithoutShift == Keys.NumPad1 ? new Rectangle(newBounds.X - newBounds.Width, newBounds.Y + newBounds.Height, newBounds.Width, newBounds.Height) :
                            keyWithoutShift == Keys.NumPad2 ? new Rectangle(newBounds.X, newBounds.Y + newBounds.Height, newBounds.Width, newBounds.Height) :
                            keyWithoutShift == Keys.NumPad3 ? new Rectangle(newBounds.X + newBounds.Width, newBounds.Y + newBounds.Height, newBounds.Width, newBounds.Height) :
                            newBounds;
                        if (newBounds.X < 0) newBounds.X = 0;
                        if (newBounds.Y < 0) newBounds.Y = 0;
                        if (newBounds.Right > _gridFormScreen.Bounds.Width) newBounds.X = _gridFormScreen.Bounds.Width - newBounds.Width;
                        if (newBounds.Bottom > _gridFormScreen.Bounds.Height) newBounds.Y = _gridFormScreen.Bounds.Height - newBounds.Height;
                    }
                    else
                    {
                        newShown = true;
                        _gridUndo.Push(newBounds);
                        newBounds =
                            keyWithoutShift == Keys.NumPad7 ? new Rectangle(newBounds.X, newBounds.Y, newBounds.Width / 3, newBounds.Height / 3) :
                            keyWithoutShift == Keys.NumPad8 ? new Rectangle(newBounds.X + newBounds.Width / 3, newBounds.Y, newBounds.Width / 3, newBounds.Height / 3) :
                            keyWithoutShift == Keys.NumPad9 ? new Rectangle(newBounds.X + 2 * newBounds.Width / 3, newBounds.Y, newBounds.Width / 3, newBounds.Height / 3) :
                            keyWithoutShift == Keys.NumPad4 ? new Rectangle(newBounds.X, newBounds.Y + newBounds.Height / 3, newBounds.Width / 3, newBounds.Height / 3) :
                            keyWithoutShift == Keys.NumPad5 ? new Rectangle(newBounds.X + newBounds.Width / 3, newBounds.Y + newBounds.Height / 3, newBounds.Width / 3, newBounds.Height / 3) :
                            keyWithoutShift == Keys.NumPad6 ? new Rectangle(newBounds.X + 2 * newBounds.Width / 3, newBounds.Y + newBounds.Height / 3, newBounds.Width / 3, newBounds.Height / 3) :
                            keyWithoutShift == Keys.NumPad1 ? new Rectangle(newBounds.X, newBounds.Y + 2 * newBounds.Height / 3, newBounds.Width / 3, newBounds.Height / 3) :
                            keyWithoutShift == Keys.NumPad2 ? new Rectangle(newBounds.X + newBounds.Width / 3, newBounds.Y + 2 * newBounds.Height / 3, newBounds.Width / 3, newBounds.Height / 3) :
                            keyWithoutShift == Keys.NumPad3 ? new Rectangle(newBounds.X + 2 * newBounds.Width / 3, newBounds.Y + 2 * newBounds.Height / 3, newBounds.Width / 3, newBounds.Height / 3) :
                            newBounds;
                    }
                    break;

                default:
                    return false;
            }

            setGridBounds(newShown, newScreen, newBounds, execute, regionFirst);
            return true;
        }

        static void keyUp(object sender, GlobalKeyEventArgs e)
        {
            if (Processing)
                return;
            Processing = true;
            try
            {
#if DEBUG_LOG
                var buf = Encoding.UTF8.GetBytes("Up: " + e.VirtualKeyCode.ToString() + "\r\n");
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
                bool win = Pressed.Contains(Keys.LWin) || Pressed.Contains(Keys.RWin);

                if (LastBufferCheck < Buffer.Length && !alt && !ctrl && !shift && !win)
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
                            Keys.LMenu, Keys.RMenu, Keys.LWin, Keys.RWin,
                            Keys.Scroll,
                            Keys.Up, Keys.Down, Keys.Left, Keys.Right,
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
