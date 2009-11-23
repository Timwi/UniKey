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
        private const bool DebugLog = false;

        protected string Buffer = string.Empty;
        protected int LastBufferCheck = 0;
        protected string UndoBufferFrom = null;
        protected string UndoBufferTo = null;

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
        public virtual bool EnableUndo() { return false; }

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
                        Ut.SendKeystrokes(Enumerable.Repeat<object>(Keys.Back, 6));
                        Application.Exit();
                        return ProcessKeyAction.ProcessedButEmit;
                    }
                    foreach (var kvp in Program.Modes)
                    {
                        if (Buffer.Substring(0, LastBufferCheck).EndsWith("[" + kvp.Key + "]"))
                        {
                            Buffer = Buffer.Substring(0, LastBufferCheck - kvp.Key.Length - 2) + Buffer.Substring(LastBufferCheck);
                            LastBufferCheck -= kvp.Key.Length + 2;
                            UndoBufferFrom = null;
                            Program.Mode = Program.Modes[kvp.Key];
                        }
                    }

                    var replace = Program.Mode.GetReplace(Buffer.Substring(0, LastBufferCheck));
                    if (replace != null)
                    {
                        UndoBufferFrom = replace.ReplaceWith + Buffer.Substring(LastBufferCheck);
                        UndoBufferTo = Buffer.Substring(LastBufferCheck - replace.ReplaceLength, replace.ReplaceLength) + Buffer.Substring(LastBufferCheck);
                        Buffer = Buffer.Substring(0, LastBufferCheck - replace.ReplaceLength) + replace.ReplaceWith + Buffer.Substring(LastBufferCheck);
                        LastBufferCheck += replace.ReplaceWith.Length - replace.ReplaceLength;
                    }
                    else if (EnableUndo() && UndoBufferFrom != null && LastBufferCheck == Buffer.Length && Buffer[LastBufferCheck - 1] == '\\')
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
            {
                Buffer = string.Empty;
                UndoBufferFrom = null;
            }
            else if (key == Keys.Back && Buffer.Length > 0)
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

            bool alt = Program.Pressed.Contains(Keys.LMenu) || Program.Pressed.Contains(Keys.RMenu);
            bool ctrl = Program.Pressed.Contains(Keys.LControlKey) || Program.Pressed.Contains(Keys.RControlKey);
            bool shift = Program.Pressed.Contains(Keys.LShiftKey) || Program.Pressed.Contains(Keys.RShiftKey);

            if (!alt && !ctrl)
            {
                char? ch = null;
                if (shift && KeyToChar.Shift.ContainsKey(key))
                    ch = KeyToChar.Shift[key];
                else if (!shift && KeyToChar.NoShift.ContainsKey(key))
                    ch = KeyToChar.NoShift[key];
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

        public override bool EnableUndo() { return true; }
    }

    internal class DeMode : KeyboardMode
    {
        public DeMode()
            : base()
        {
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
                "manue>manue;MANUE>MANUE;Manue>Manue;");
        }

        public override bool EnableUndo() { return true; }
    }
}
