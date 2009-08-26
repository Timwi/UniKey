using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using RT.Util;
using RT.Util.ExtensionMethods;
using System.Windows.Forms;
using System.IO;

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

    internal class Shortcut
    {
        public string Key;
        public string Value;
        public override string ToString()
        {
            return Key + " ⇒ " + Value;
        }
    }

    internal class KeyboardMode
    {
        protected string Buffer = string.Empty;
        protected int LastBufferCheck = 0;
        private const bool DebugLog = false;

        // Maps from result to shortcut.
        protected List<Shortcut> Shortcuts = null;

        public virtual ReplaceResult GetReplace(string buffer)
        {
            if (Shortcuts != null)
                foreach (var sh in Shortcuts.OrderBy(kvp => -kvp.Key.Length))
                    if (buffer.EndsWith(sh.Key, StringComparison.Ordinal))
                        return new ReplaceResult(sh.Key.Length, sh.Value);
            return null;
        }

        protected void parse(string input)
        {
            Shortcuts = new List<Shortcut>();
            string[] items = input.Replace("\r", "").Replace("\n", "").Split(';');
            foreach (string item in items)
            {
                int i = item.IndexOf('>');
                if (i > 0)
                    Shortcuts.Add(new Shortcut { Key = item.Substring(0, i), Value = item.Substring(i + 1) });
            }
            Shortcuts.Sort((a, b) => b.Key.Length.CompareTo(a.Key.Length));
        }

        public virtual void Activate() { Buffer = string.Empty; }

        public virtual ProcessKeyAction ProcessKeyUp(Keys key, bool doReplacements)
        {
            if (DebugLog)
            {
                var buf = Encoding.UTF8.GetBytes("Up: " + key.ToString() + "\r\n");
                using (var f = File.Open(@"C:\temp\log", FileMode.Append, FileAccess.Write, FileShare.Write))
                {
                    f.Write(buf, 0, buf.Length);
                    f.Close();
                }
            }

            Program.Pressed.Remove(key);

            if (LastBufferCheck > Buffer.Length)
                LastBufferCheck = Buffer.Length;

            bool alt = Program.Pressed.Contains(Keys.LMenu) || Program.Pressed.Contains(Keys.RMenu);
            bool ctrl = Program.Pressed.Contains(Keys.LControlKey) || Program.Pressed.Contains(Keys.RControlKey);
            bool shift = Program.Pressed.Contains(Keys.LShiftKey) || Program.Pressed.Contains(Keys.RShiftKey);

            if (doReplacements && LastBufferCheck < Buffer.Length && !alt && !ctrl && !shift)
            {
                string oldBuffer = Buffer;
                while (LastBufferCheck < Buffer.Length)
                {
                    LastBufferCheck++;
                    if (Buffer.Substring(0, LastBufferCheck).EndsWith("[exit]"))
                    {
                        Ut.SendKeystrokes(((object) Keys.Back).Repeat(6));
                        Application.Exit();
                        return ProcessKeyAction.ProcessedButEmit;
                    }
                    foreach (var kvp in Program.Modes)
                    {
                        if (Buffer.Substring(0, LastBufferCheck).EndsWith("[" + kvp.Key + "]"))
                        {
                            Buffer = Buffer.Substring(0, LastBufferCheck - kvp.Key.Length - 2) + Buffer.Substring(LastBufferCheck);
                            LastBufferCheck -= kvp.Key.Length + 2;
                            Program.Mode = Program.Modes[kvp.Key];
                        }
                    }

                    var replace = Program.Mode.GetReplace(Buffer.Substring(0, LastBufferCheck));
                    if (replace != null)
                    {
                        Buffer = Buffer.Substring(0, LastBufferCheck - replace.ReplaceLength) + replace.ReplaceWith + Buffer.Substring(LastBufferCheck);
                        LastBufferCheck += replace.ReplaceWith.Length - replace.ReplaceLength;
                    }
                }

                if (Buffer != oldBuffer)
                {
                    int i = 0;
                    while (i < Buffer.Length && i < oldBuffer.Length && Buffer[i] == oldBuffer[i])
                        i++;
                    Ut.SendKeystrokes(Enumerable.Repeat<object>(Keys.Back, oldBuffer.Length - i).Concat(Buffer.Substring(i).Cast<object>()));
                    return ProcessKeyAction.ProcessedButEmit;
                }
            }

            return ProcessKeyAction.Continue;
        }

        public virtual ProcessKeyAction ProcessKeyDown(Keys key)
        {
            if (DebugLog)
            {
                var buf = Encoding.UTF8.GetBytes("Down: " + key.ToString() + "\r\n");
                using (var f = File.Open(@"C:\temp\log", FileMode.Append, FileAccess.Write, FileShare.Write))
                {
                    f.Write(buf, 0, buf.Length);
                    f.Close();
                }
            }

            if (!Program.Pressed.Contains(key))
                Program.Pressed.Add(key);

            if (key == Keys.LControlKey || key == Keys.RControlKey || key == Keys.Escape || key == Keys.Enter || key == Keys.Return || key == Keys.Tab)
                Buffer = string.Empty;
            else if (key == Keys.Back && Buffer.Length > 0)
            {
                Buffer = Buffer.Substring(0, Buffer.Length - 1);
                if (LastBufferCheck > 0) LastBufferCheck--;
            }

            bool alt = Program.Pressed.Contains(Keys.LMenu) || Program.Pressed.Contains(Keys.RMenu);
            bool ctrl = Program.Pressed.Contains(Keys.LControlKey) || Program.Pressed.Contains(Keys.RControlKey);
            bool shift = Program.Pressed.Contains(Keys.LShiftKey) || Program.Pressed.Contains(Keys.RShiftKey);

            if (!alt && !ctrl)
            {
                if (shift && KeyToChar.Shift.ContainsKey(key))
                    Buffer += KeyToChar.Shift[key];
                else if (!shift && KeyToChar.NoShift.ContainsKey(key))
                    Buffer += KeyToChar.NoShift[key];
            }

            return ProcessKeyAction.Continue;
        }
    }

    internal class EoMode : KeyboardMode
    {
        public EoMode()
            : base()
        {
            parse("cx>ĉ;gx>ĝ;hx>ĥ;jx>ĵ;sx>ŝ;ux>ŭ;" +
                "CX>Ĉ;GX>Ĝ;HX>Ĥ;JX>Ĵ;SX>Ŝ;UX>Ŭ;" +
                "Cx>Ĉ;Gx>Ĝ;Hx>Ĥ;Jx>Ĵ;Sx>Ŝ;Ux>Ŭ");
        }
    }

    internal class DeMode : KeyboardMode
    {
        public DeMode()
            : base()
        {
            parse("ae>ä;oe>ö;ue>ü;AE>Ä;OE>Ö;UE>Ü;Ae>Ä;Oe>Ö;Ue>Ü;" +
                "ä¬>ae;ö¬>oe;ü¬>ue;Ä¬>Ae;Ö¬>Oe;Ü¬>Ue;Ä¦>AE;Ö¦>OE;Ü¦>UE");
        }
    }
}
