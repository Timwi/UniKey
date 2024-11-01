﻿using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Drawing.Drawing2D;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using RT.Keyboard;
using RT.Serialization;
using RT.Util;
using RT.Util.ExtensionMethods;
using RT.Util.Forms;
using Timer = System.Windows.Forms.Timer;

namespace UniKey;

static class Program
{
    static Settings Settings;
    static GlobalKeyboardListener KeyboardListener;
    static readonly List<Keys> Pressed = [];
    static bool Processing = false;
    static string Buffer = string.Empty;
    static int LastBufferCheck = 0;
    static string UndoBufferFrom = null;
    static string UndoBufferTo = null;
    static string Password = null;
    static FileSystemWatcher DetectSettingsFileChange;

    static Control GuiThreadInvoker;

    static readonly CommandInfo[] Commands = Ut.NewArray
    (
        new CommandInfo(@"\{help\}$", "*{{help}}*", @"Displays this help screen.", m => help(m.Length)),

        new CommandInfo(@"\{add ([^\{\}]+|\{[^\{\}]+\})\}$", "*{{add */key/*}}*",
            @"Adds a new entry to the replacements dictionary. The key is specified, the replacement value is taken from the clipboard. The key may be surrounded by curly braces, but may otherwise not contain any *{{* or *}}*.",
            m => add(m.Groups[1].Value, m.Length)),

        new CommandInfo(@"\{ren ([^ \{\}]+) ([^ \{\}]+)\}$", "*{{ren */oldkey/* */newkey/*}}*",
            @"Changes the key for an existing replacement rule. Each key may be surrounded by curly braces, but may otherwise not contain any *{{* or *}}*. (Note this command does not work for keys containing spaces; you must use *{{del */key/*}}* followed by *{{add */key/*}}* for those.)",
            m => ren(m.Groups[1].Value, m.Groups[2].Value, m.Length)),

        new CommandInfo(@"\{del ([^\{\}]+|\{[^\{\}]+\})\}$", "*{{del */key/*}}*",
            @"Deletes the specified key from the replacements dictionary. The key may be surrounded by curly braces, but may otherwise not contain any *{{* or *}}*.",
            m => del(m.Groups[1].Value, m.Length)),

        new CommandInfo(@"\{f\s+([^\{\}]+?)\s*\}$", @"*{{f */words/*}}*",
            @"Searches for a Unicode character using the specified keywords and outputs the best match.",
            m => find(m.Groups[1].Value, m.Length)),

        new CommandInfo(@"\{fa\s+([^\{\}]+?)\s*\}$", @"*{{fa */words/*}}*",
            @"Finds all Unicode characters whose names contain the specified words, and places a tabular list of those characters in the clipboard.",
            m => findAll(m.Groups[1].Value, m.Length)),

        new CommandInfo(@"\{re\}$", @"*{{re}}*",
            @"List all the replacement rules that generate as output the text that is currently in the clipboard.",
            m => findRules(m.Length)),

        new CommandInfo(@"\{html( ['""]+)?\}$", @"*{{html}}*, *{{html '}}*, *{{html """"}}*, *{{html '""""}}*", @"""HTML-escapes the current contents of the clipboard and outputs the result as keystrokes. By default, only <, > and & are escaped. Optionally specify ' (apostrophe) and/or """" (double-quote) to escape those, too.""",
            m => new ReplaceResult(m.Length, ClipboardGetText().HtmlEscape(!m.Groups[1].Value.Contains('\''), !m.Groups[1].Value.Contains('"')))),

        new CommandInfo(@"\{unhtml\}$", @"*{{unhtml}}*", @"Reverses HTML escaping (HTML entities) in the current contents of the clipboard and outputs the result as keystrokes.",
            m => new ReplaceResult(m.Length, unHtml(ClipboardGetText()))),

        new CommandInfo(@"\{url\}$", @"*{{url}}*", @"URL-escapes the current contents of the clipboard and outputs the result as keystrokes.",
            m => new ReplaceResult(m.Length, ClipboardGetText().UrlEscape())),

        new CommandInfo(@"\{unurl\}$", @"*{{unurl}}*", @"Reverses URL escaping in the current contents of the clipboard and outputs the result as keystrokes.",
            m => new ReplaceResult(m.Length, Ut.OnExceptionDefault(() => ClipboardGetText().UrlUnescape(), "The string contains invalid URL encoding."))),

        new CommandInfo(@"\{b64\}$", @"*{{b64}}*", @"Base64-escapes the current contents of the clipboard and outputs the result as keystrokes.",
            m => new ReplaceResult(m.Length, Convert.ToBase64String(ClipboardGetText().ToUtf8()))),

        new CommandInfo(@"\{unb64\}$", @"*{{unb64}}*", @"Reverses base64 escaping in the current contents of the clipboard and outputs the result as keystrokes.",
            m => new ReplaceResult(m.Length, Ut.OnExceptionDefault(() => Convert.FromBase64String(ClipboardGetText()).FromUtf8(), "The string contains invalid base64 encoding."))),

        new CommandInfo(@"\{u ([0-9a-f]+)\}$", @"*{{u */codepoint/*}}*", @"Outputs the specified Unicode character as a keystroke. The codepoint must be in hexadecimal.",
            m => int.TryParse(m.Groups[1].Value, NumberStyles.HexNumber, null, out var i)
                    ? new ReplaceResult(m.Length, char.ConvertFromUtf32(i))
                    : new ReplaceResult(m.Length, "Invalid codepoint.")),

        new CommandInfo(@"\{cp( .)?\}$", @"*{{cp */character/*}}*, *{{cp}}*", @"Outputs the hexadecimal Unicode codepoint value of the specified character, or the first character from the clipboard if none specified, as keystrokes.",
            m => new ReplaceResult(m.Length, (m.Groups[1].Length > 0 ? char.ConvertToUtf32(m.Groups[1].Value, 1) : char.ConvertToUtf32(ClipboardGetText(), 0)).ToString("X4"))),

        new CommandInfo(
            @"\{{({0}) ([^\{{\}}]+)\}}$".Fmt(Conversions.AllConversions.Select(c => c.Key).JoinString("|")),
            "*{{*/conversion/* */text/*}}*",
            "Converts the specified /text/ according to a specified /conversion/:\n" + Conversions.AllConversions.Select(c => "    [*" + c.Key + "* == " + c.Name + "]").JoinString("\n"),
            m => new ReplaceResult(m.Length, Conversions.Convert(Conversions.AllConversions.First(c => c.Key == m.Groups[1].Value), m.Groups[2].Value))),

        new CommandInfo(@"\{setpassword ([^\{\}]+)\}$", "*{{setpassword */newpassword/*}}*",
            @"Encrypts the UniKey data file using the specified password. You will be prompted for the password every time UniKey starts. If you forget the password, you will not be able to retrieve your UniKey data.",
            m => { Password = m.Groups[1].Value; saveLater(); return new ReplaceResult(m.Length, "done"); }),

        new CommandInfo(@"\{removepassword\}$", "*{{removepassword}}*",
            @"Saves the UniKey data file unencrypted. You will no longer be prompted for a password when UniKey starts.",
            m => { Password = null; saveLater(); return new ReplaceResult(m.Length, "done"); }),

        new CommandInfo(@"\{set mousegrid (on|off)\}", "*{{set mousegrid on}}*, *{{set mousegrid off}}*",
            @"Enables or disables the mouse grid feature. The mouse grid is activated by turning Num Lock on and then operated using the keys on the NumPad.",
            m =>
            {
                Settings.MouseGridEnabled = m.Groups[1].Value == "on";
                saveLater();
                return new ReplaceResult(m.Length, "Mouse grid now {0}.".Fmt(Settings.MouseGridEnabled ? "enabled" : "disabled"));
            }),

        new CommandInfo(@"\{set undo (on|off)\}", "*{{set undo on}}*, *{{set undo off}}*",
            @"Enables or disables the undo feature. When enabled, the latest replacement can be undone by pressing the backslash key (Oem5).",
            m =>
            {
                Settings.UndoDisabled = m.Groups[1].Value == "off";
                saveLater();
                return new ReplaceResult(m.Length, "Undo is now {0}.".Fmt(Settings.UndoDisabled ? "disabled" : "enabled"));
            }),

        new CommandInfo(@"\{guid\}$", @"*{{guid}}*", @"Outputs a randomly generated GUID.",
            m => new ReplaceResult(m.Length, new Guid(RndCrypto.NextBytes(16)).ToString().ToLower())),

        new CommandInfo(@"\{exit\}$", "*{{exit}}*", "Exits UniKey.",
            m =>
            {
                Application.Exit();
                return new ReplaceResult(m.Length, "Exiting.");
            })
    );

    private static string unHtml(string s)
    {
        string lastEntity = null;
        try
        {
            return Regex.Replace(
                s,
                @"&(#x[0-9a-fA-F]+|#\d+|\w+);",
                v =>
                {
                    lastEntity = v.Value;
                    var val = v.Groups[1].Value;
                    if (val.StartsWith("#x"))
                        return char.ConvertFromUtf32(Convert.ToInt32(val.Substring(2), 16));
                    if (val.StartsWith('#'))
                        return char.ConvertFromUtf32(Convert.ToInt32(val.Substring(1), 10));
                    return HtmlEntities.Data[val];
                }).Replace("\r", "");
        }
        catch
        {
            return "Invalid entity: " + lastEntity;
        }
    }

    private static string ClipboardGetText()
    {
        return Ut.OnExceptionRetry(() => Clipboard.GetText(), 10, 100);
    }

    private static void ClipboardSetText(string text)
    {
        Ut.OnExceptionRetry(() => { Clipboard.SetText(text); }, 10, 100);
    }

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
        var newRepl = ClipboardGetText();
        if (Settings.Replacers.TryGetValue(key, out var existing))
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
        if (!Settings.Replacers.TryGetValue(oldInput, out var value))
            return new ReplaceResult(length, "not found");
        if (Settings.Replacers.ContainsKey(newInput))
            return new ReplaceResult(length, "failed: new key already exists");
        var replaceWith = value;
        Settings.Replacers.Remove(oldInput);
        Settings.Replacers[newInput] = replaceWith;
        saveLater();
        return new ReplaceResult(length, "done");
    }

    private static ReplaceResult find(string input, int length)
    {
        if (UnicodeData == null)
            return new ReplaceResult(length, "Unicode data not available: " + UnicodeDataError);
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
        if (UnicodeData == null)
            return new ReplaceResult(length, "Unicode data not available: " + UnicodeDataError);
        string[] words = input.Length == 0 ? null : input.Split(' ').Where(s => s.Length > 0).Select(s => s.ToUpperInvariant()).ToArray();
        if (words == null || words.Length < 1)
            return new ReplaceResult(length, "No search terms given.");
        var candidatesStr = FindCharacters(words)
            .Select(si => (char) (0x202a /* left-to-right override */) + char.ConvertFromUtf32(si.CodePoint) + (char) (0x202c /* pop directional formatting */) + "\t" +
                                   si.GetReplacer(Settings.Replacers) + "\tU+" + si.CodePoint.ToString("X4") + "\t" + si.Name + Environment.NewLine)
            .JoinString();
        if (candidatesStr.Length > 0)
            ClipboardSetText(candidatesStr);
        else
            ClipboardSetText("No character found matching: " + words.JoinString(" "));
        return new ReplaceResult(length, "");
    }

    private static ReplaceResult findRules(int length)
    {
        var output = Clipboard.GetText();
        return new ReplaceResult(length, Settings.Replacers.Where(kvp => kvp.Value == output).Select(kvp => kvp.Key).JoinString("; "));
    }

    private static ReplaceResult help(int length)
    {
        GuiThreadInvoker.BeginInvoke(new Action(() =>
        {
            var str = new StringBuilder();
            foreach (var info in Commands)
            {
                str.AppendLine(info.CommandName);
                str.AppendLine("    " + info.HelpString);
                str.AppendLine();
            }

            new HelpForm(str.ToString()).Show();
        }));
        return new ReplaceResult(length, "");
    }

    static Dictionary<int, string> UnicodeData;
    static string UnicodeDataError = null;

    private static readonly byte[] _iv = "A,EW9%9Enp{1!oiN".ToUtf8();
    private static readonly byte[] _salt = "kdSkeuDkj3%k".ToUtf8();

    [STAThread]
    static void Main()
    {
        ApplicationConfiguration.Initialize();

        foreach (var p in Process.GetProcessesByName("UniKey"))
        {
            if (p.Id != Environment.ProcessId)
                p.Kill();
        }

        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);

        if (!loadSettings())
            return;

        try
        {
            UnicodeData = [];
            var filename = Settings.UnicodeDataFile == null ? PathUtil.AppPathCombine("UnicodeData.txt") : PathUtil.ExpandPath(Settings.UnicodeDataFile);
            foreach (var line in File.ReadAllText(filename).Replace("\r", "").Split('\n'))
            {
                if (line.Length == 0)
                    continue;
                var fields = line.Split(';');
                if (fields.Length < 2)
                    continue;
                if (fields[1].Length == 0 || fields[1][0] == '<')
                    continue;
                UnicodeData[int.Parse(fields[0], NumberStyles.HexNumber)] = fields[1].ToUpperInvariant();
            }
        }
        catch (Exception e)
        {
            UnicodeData = null;
            UnicodeDataError = e.Message;
        }

        GuiThreadInvoker = new Form();
        var _ = GuiThreadInvoker.Handle;

        var reloadSettingsFileTimer = new Timer { Enabled = false, Interval = 1500 };
        reloadSettingsFileTimer.Tick += delegate
        {
            reloadSettingsFileTimer.Enabled = false;
            if (!loadSettings())
                Application.Exit();
        };

        DetectSettingsFileChange = new FileSystemWatcher(Path.GetDirectoryName(settingsPath), Path.GetFileName(settingsPath))
        {
            IncludeSubdirectories = false,
            NotifyFilter = NotifyFilters.CreationTime | NotifyFilters.FileName | NotifyFilters.LastWrite | NotifyFilters.Size,
        };

        void scheduleReloadSettings()
        {
            reloadSettingsFileTimer.Enabled = false;
            reloadSettingsFileTimer.Enabled = true;
        }
        DetectSettingsFileChange.Changed += delegate { GuiThreadInvoker.BeginInvoke(scheduleReloadSettings); };
        DetectSettingsFileChange.Created += delegate { GuiThreadInvoker.BeginInvoke(scheduleReloadSettings); };
        DetectSettingsFileChange.Deleted += delegate { GuiThreadInvoker.BeginInvoke(scheduleReloadSettings); };
        DetectSettingsFileChange.Renamed += delegate { GuiThreadInvoker.BeginInvoke(scheduleReloadSettings); };
        DetectSettingsFileChange.EnableRaisingEvents = true;

        KeyboardListener = new() { HookAllKeys = true };
        KeyboardListener.KeyDown += keyDown;
        KeyboardListener.KeyUp += keyUp;
        Application.Run();
    }

    private static readonly string settingsPath = PathUtil.AppPathCombine("UniKey.settings.xml");

    private static bool loadSettings()
    {
        again:
        try
        {
            bool usePw = true;
            bool result = Ut.OnExceptionRetry(() => Ut.WaitSharingVio(() =>
            {
                using var f = File.Open(settingsPath, FileMode.Open);
                var start = f.Read(5).FromUtf8();
                if (start == "passw")
                {
                    Password ??= InputBox.GetLine("Enter password:", caption: "Password");
                    if (Password == null)
                        return false;
                    var passwordDeriveBytes = new PasswordDeriveBytes(Password, _salt);
                    var key = passwordDeriveBytes.GetBytes(16);
                    var rij = Aes.Create();
                    var rijDec = rij.CreateDecryptor(key, _iv);
                    try
                    {
                        using var cStream = new CryptoStream(f, rijDec, CryptoStreamMode.Read);
                        Settings = ClassifyXml.Deserialize<Settings>(XElement.Parse(cStream.ReadAllBytes().FromUtf8()));
                    }
                    catch (Exception e)
                    {
                        DlgMessage.Show("Could not decrypt {0}:\n{1}\nPassword may be wrong. Exiting.".Fmt(settingsPath, e.Message), "Error", DlgType.Error, "E&xit UniKey");
                        return false;
                    }
                }
                else
                    usePw = false;
                return true;
            }, maximum: TimeSpan.FromSeconds(20)), attempts: 3, delayMs: 3 * 1000);
            if (!result)
                return false;
            if (!usePw)
                Settings = ClassifyXml.DeserializeFile<Settings>(settingsPath);
        }
        catch (Exception e)
        {
            again2:
            var result = DlgMessage.Show("Could not load {0}: {1}".Fmt(settingsPath, e.Message), "Error", DlgType.Question, "&Create new file here", "&Import existing file", "&Retry", "E&xit UniKey");
            switch (result)
            {
                case 0:     // Create new file here
                    Settings = new Settings();
                    if (!save())
                        return false;
                    break;

                case 1:     // Import existing file
                    var dlg = new OpenFileDialog();
                    dlg.Title = "Choose settings file to import";
                    dlg.CheckPathExists = dlg.CheckFileExists = false;
                    if (dlg.ShowDialog() != DialogResult.OK || !File.Exists(dlg.FileName))
                        goto again2;
                    File.Copy(dlg.FileName, settingsPath);
                    goto again;

                case 2:     // Retry
                    goto again;

                case 3:     // Exit UniKey
                    return false;
            }
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
            .Where(kvp => words.All(w => kvp.Value.Contains(w) || kvp.Key.ToString("X4").Contains(w)))
            .Select(kvp =>
            {
                var split = kvp.Value.Split(' ');
                return new SearchItem
                {
                    CodePoint = kvp.Key,
                    Name = kvp.Value,
                    Score =
                        split.All(words.Contains) && words.All(split.Contains) ? 1000 :
                        words.Length == 1 && kvp.Key.ToString("X4").Contains(words[0]) ? 999 :
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
        retry:
        try
        {
            Ut.OnExceptionRetry(() =>
            {
                Ut.WaitSharingVio(() =>
                {
                    if (Password != null)
                    {
                        var passwordDeriveBytes = new PasswordDeriveBytes(Password, _salt);
                        var key = passwordDeriveBytes.GetBytes(16);
                        var rij = Aes.Create();

                        var rijEnc = rij.CreateEncryptor(key, _iv);
                        using var outputStream = File.Open(settingsPath, FileMode.Create);
                        outputStream.Write("passw".ToUtf8());
                        using var cStream = new CryptoStream(outputStream, rijEnc, CryptoStreamMode.Write);
                        cStream.Write(ClassifyXml.Serialize(Settings).ToString().ToUtf8());
                    }
                    else
                        ClassifyXml.SerializeToFile(Settings, settingsPath);
                }, TimeSpan.FromSeconds(20));
            }, attempts: 3, delayMs: 5 * 1000);
        }
        catch (Exception e)
        {
            if (DlgMessage.Show("Error saving {0}: {1}".Fmt(settingsPath, e.Message), "Error", DlgType.Error, "&Retry", "&Ignore") == 0)
                goto retry;
            return false;
        }
        return true;
    }

    static readonly byte[] keyboardState = new byte[256];

    [SuppressMessage("Performance", "CA1806:Do not ignore method results", Justification = "not necessary in this case")]
    static string getCharsFromKeys(Keys keys, bool shift)
    {
        var buf = new StringBuilder(16);
        keyboardState[(int) Keys.ShiftKey] = (byte) (shift ? 0xff : 0);
        WinAPI.ToUnicode((uint) keys, 0, keyboardState, buf, 16, 0);
        return buf.ToString();
    }

    static readonly Keys[] _numKeys = Ut.NewArray(
        // Without shift
        Keys.NumPad0, Keys.NumPad1, Keys.NumPad2, Keys.NumPad3, Keys.NumPad4, Keys.NumPad5, Keys.NumPad6, Keys.NumPad7, Keys.NumPad8, Keys.NumPad9, Keys.Decimal,
        // With shift
        Keys.Insert, Keys.End, Keys.Down, Keys.PageDown, Keys.Left, Keys.Clear, Keys.Right, Keys.Home, Keys.Up, Keys.PageUp, Keys.Delete,
        // Unshiftable
        Keys.Multiply, Keys.Add, Keys.Subtract, Keys.Divide, Keys.Escape
    );

    static DateTime _lastHeartbeat;

    static void keyDown(object sender, GlobalKeyEventArgs e)
    {
        var start = DateTime.UtcNow;
        if (Processing)
            return;
        Processing = true;
        try
        {
            if (Settings.MouseGridEnabled && !Pressed.Contains(Keys.LControlKey) && !Pressed.Contains(Keys.RControlKey))
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
                    var str = (!Settings.UndoDisabled && e.VirtualKeyCode == Keys.Oem5) ? "←" : getCharsFromKeys(e.VirtualKeyCode, shift);
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
            if (Settings.DebugLogPath != null)
            {
                if ((DateTime.UtcNow - start).TotalMilliseconds >= 100)
                    File.AppendAllLines(Settings.DebugLogPath, ["{2}  Down: {0}, took {1:0} ms".Fmt(e.VirtualKeyCode, (DateTime.UtcNow - start).TotalMilliseconds, DateTime.Now)]);
                else if ((DateTime.UtcNow - _lastHeartbeat).TotalSeconds >= 10)
                {
                    _lastHeartbeat = DateTime.UtcNow;
                    File.AppendAllLines(Settings.DebugLogPath, ["{0}  Still alive!".Fmt(DateTime.Now)]);
                }
            }
        }
    }

    private static Form _gridForm;
    private static Screen _gridFormScreen;
    private static readonly Stack<Rectangle> _gridUndo = new();
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
                        ShowInTaskbar = false,
                        TopMost = true,
                    };
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
            extraAction?.Invoke();
        }));
    }

    private static bool processNumPad(Keys key)
    {
        if (_gridForm == null)
            _gridUndo.Clear();

        bool newShown = _gridForm != null;
        Screen newScreen = _gridForm != null ? _gridFormScreen : Screen.PrimaryScreen;
        Rectangle newBounds = _gridForm != null ? _gridForm.Bounds : Screen.PrimaryScreen.Bounds;
        Point pos = new(newBounds.Left + newBounds.Width / 2, newBounds.Top + newBounds.Height / 2);
        Action execute = null;
        bool regionFirst = false;

        var keyWithoutShift = key & ~Keys.ShiftKey;
        var shift = (key & Keys.ShiftKey) == Keys.ShiftKey;
        var from = _dragStartFrom ?? pos;

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
                    Cursor.Position = from;
                    WinAPI.mouse_event(_rightMouseButton ? WinAPI.MOUSEEVENTF_RIGHTDOWN : WinAPI.MOUSEEVENTF_LEFTDOWN, pos.X, pos.Y, 0, 0);
                    WinAPI.mouse_event(_rightMouseButton ? WinAPI.MOUSEEVENTF_RIGHTUP : WinAPI.MOUSEEVENTF_LEFTUP, pos.X, pos.Y, 0, 0);
                    WinAPI.mouse_event(_rightMouseButton ? WinAPI.MOUSEEVENTF_RIGHTDOWN : WinAPI.MOUSEEVENTF_LEFTDOWN, pos.X, pos.Y, 0, 0);
                    Cursor.Position = pos;
                    WinAPI.mouse_event(_rightMouseButton ? WinAPI.MOUSEEVENTF_RIGHTUP : WinAPI.MOUSEEVENTF_LEFTUP, pos.X, pos.Y, 0, 0);
                };
                break;

            case Keys.NumPad0:
                _dragStartFrom = pos;
                newBounds = _gridFormScreen.Bounds;
                break;

            case Keys.Decimal:
                newShown = false;
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
        var start = DateTime.UtcNow;
        if (Processing)
            return;
        Processing = true;
        try
        {
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
                        (UndoBufferFrom, UndoBufferTo) = (UndoBufferTo, UndoBufferFrom);
                        LastBufferCheck = Buffer.Length;
                    }
                }

                if (Buffer != oldBuffer)
                {
                    int i = 0;
                    while (i < Buffer.Length && i < oldBuffer.Length && Buffer[i] == oldBuffer[i])
                        i++;
                    var keystrokes = new List<object>(oldBuffer.Length - i + Buffer.Length - i + 1);
                    if (e.VirtualKeyCode == Keys.LShiftKey || e.VirtualKeyCode == Keys.RShiftKey)
                    {
                        e.Handled = true;
                        keystrokes.Add(Tuple.Create(e.VirtualKeyCode, true));
                    }
                    keystrokes.AddRange(Enumerable.Repeat<object>(Keys.Back, oldBuffer.Length - i));
                    for (int ix = i; ix < Buffer.Length; ix++)
                        if (Buffer[ix] == '\t')
                            keystrokes.Add(Keys.Tab);
                        else if (Buffer[ix] != '\r')
                            keystrokes.Add(Buffer[ix] == '\n' ? (object) Keys.Enter : Buffer[ix]);
                    UtWin.SendKeystrokes(keystrokes);
                }
            }
        }
        finally
        {
            Processing = false;
            if (Settings.DebugLogPath != null)
                if ((DateTime.UtcNow - start).TotalMilliseconds >= 100)
                    File.AppendAllLines(Settings.DebugLogPath, ["{2}  Up: {0}, took {1:0} ms".Fmt(e.VirtualKeyCode, (DateTime.UtcNow - start).TotalMilliseconds, DateTime.Now)]);
        }
    }

    private static readonly Keys[] _emptyKeys = Ut.NewArray<Keys>(Keys.LControlKey, Keys.RControlKey, Keys.Escape,
                        Keys.Enter, Keys.Return, Keys.Tab,
                        Keys.LMenu, Keys.RMenu, Keys.LWin, Keys.RWin,
                        Keys.Scroll,
                        Keys.Up, Keys.Down, Keys.Left, Keys.Right,
                        Keys.End, Keys.Home, Keys.PageDown, Keys.PageUp,
                        Keys.F1, Keys.F2, Keys.F3, Keys.F4, Keys.F5, Keys.F6, Keys.F7, Keys.F8, Keys.F9, Keys.F10, Keys.F11, Keys.F12);
}
