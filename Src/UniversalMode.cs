using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using RT.Util;
using System.Windows.Forms;
using System.ComponentModel;
using System.Drawing;
using UniKey.Properties;
using System.Globalization;
using System.Text.RegularExpressions;
using System.Drawing.Text;

namespace UniKey
{
    internal class SearchItem
    {
        public int CodePoint;
        public string Name;
        public int Score;
        public int Index;
        public string GetShortcut(List<Shortcut> shortcuts)
        {
            string str = char.ConvertFromUtf32(CodePoint);
            return shortcuts.Where(s => s.Value == str).Select(s => s.Key).FirstOrDefault();
        }
    }

    internal class UniversalMode : KeyboardMode
    {
        private SearchForm Searchform;
        private Label SearchformPrompt;
        private ListBox SearchformList;
        private SearchItem[] SearchformCandidates;

        private Font bigFont = new Font("Calibri", 20, FontStyle.Regular);
        private Font mediumFont = new Font("Calibri", 15, FontStyle.Regular);
        private Font smallFont = new Font("Calibri", 12, FontStyle.Regular);

        private bool SearchformVisible;
        private Dictionary<int, string> UnicodeData = null;

        public UniversalMode()
            : base()
        {
            parse(Resources.Shortcuts);

            Searchform = new SearchForm
            {
                FormBorderStyle = FormBorderStyle.FixedToolWindow,
                ControlBox = false,
                MaximizeBox = false,
                MinimizeBox = false,
                TopMost = true,
                Visible = false,
                Text = "UniKey",
                Left = Screen.AllScreens.Max(s => s.Bounds.Right) + 10,
                StartPosition = FormStartPosition.Manual,
                Font = new Font("Calibri", 11, FontStyle.Bold)
            };
            SearchformPrompt = new Label
            {
                Text = "Search for...?",
                Left = 0,
                Top = 0,
                AutoSize = true,
                Visible = true
            };
            SearchformList = new ListBox
            {
                Dock = DockStyle.Fill,
                Visible = false,
                Left = 0,
                Top = 0,
                IntegralHeight = false,
                DrawMode = DrawMode.OwnerDrawVariable
            };
            SearchformList.MeasureItem += new MeasureItemEventHandler(measureItem);
            SearchformList.DrawItem += new DrawItemEventHandler(drawItem);
            Searchform.Controls.Add(SearchformPrompt);
            Searchform.Controls.Add(SearchformList);
            Searchform.ClientSize = SearchformPrompt.Size;
            Searchform.Load += (s, e) =>
            {
                Timer t = new Timer { Interval = 1, Enabled = true }; t.Tick += (s2, e2) =>
                {
                    t.Enabled = false;
                    WinAPI.SetWindowPos(Searchform.Handle, (IntPtr) WinAPI.HWND_TOPMOST, 0, 0, 0, 0, WinAPI.SWP_NOMOVE | WinAPI.SWP_NOSIZE | WinAPI.SWP_ASYNCWINDOWPOS | WinAPI.SWP_NOACTIVATE | WinAPI.SWP_NOZORDER | WinAPI.SWP_HIDEWINDOW);
                };
            };
            Searchform.Show();
            SearchformVisible = false;
        }

        public override bool EnableUndo() { return true; }

        private void measureItem(object sender, MeasureItemEventArgs e)
        {
            e.ItemHeight = (int) e.Graphics.MeasureString("Wg", bigFont).Height + 5;
        }

        private void drawItem(object sender, DrawItemEventArgs e)
        {
            e.DrawBackground();
            e.DrawFocusRectangle();
            e.Graphics.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
            var item = SearchformCandidates[e.Index];
            StringFormat sf = new StringFormat { LineAlignment = StringAlignment.Center };

            // circled number
            e.Graphics.DrawString("" + (char) (item.Index < 9 ? 0x2460 + item.Index : 0x2474 + item.Index - 9), mediumFont, new SolidBrush(e.ForeColor), new PointF(e.Bounds.Left + 5, e.Bounds.Top + e.Bounds.Height / 2), sf);

            // character
            e.Graphics.DrawString(char.ConvertFromUtf32(item.CodePoint), bigFont, new SolidBrush(e.ForeColor), new PointF(e.Bounds.Left + 40, e.Bounds.Top + e.Bounds.Height / 2), sf);
            var width = SearchformCandidates.Max(c => e.Graphics.MeasureString(char.ConvertFromUtf32(c.CodePoint), bigFont).Width);

            // shortcut
            string sh = item.GetShortcut(Shortcuts);
            if (sh != null)
                e.Graphics.DrawString(sh.Substring(0, sh.Length - 1), mediumFont, new SolidBrush(e.ForeColor), new PointF(e.Bounds.Left + width + 50, e.Bounds.Top + e.Bounds.Height / 2), sf);
            width += SearchformCandidates.Max(c => e.Graphics.MeasureString(c.GetShortcut(Shortcuts), mediumFont).Width);

            // hex code
            e.Graphics.DrawString("0x" + item.CodePoint.ToString("X"), smallFont, new SolidBrush(e.ForeColor), new PointF(e.Bounds.Left + 100 + width, e.Bounds.Top + e.Bounds.Height / 2), new StringFormat { Alignment = StringAlignment.Far, LineAlignment = StringAlignment.Center });

            // character name
            e.Graphics.DrawString(item.Name, smallFont, new SolidBrush(e.ForeColor), new PointF(e.Bounds.Left + 110 + width, e.Bounds.Top + e.Bounds.Height / 2), sf);
        }

        public override ProcessKeyAction ProcessKeyUp(Keys key, bool doReplacements)
        {
            return base.ProcessKeyUp(key, doReplacements && !SearchformVisible);
        }

        public override ProcessKeyAction ProcessKeyDown(Keys key)
        {
            var action = base.ProcessKeyDown(key);
            if (action != ProcessKeyAction.Continue)
                return action;

            uint swp = WinAPI.SWP_NOSIZE | WinAPI.SWP_ASYNCWINDOWPOS | WinAPI.SWP_NOACTIVATE | WinAPI.SWP_NOZORDER;

            if (!SearchformVisible)
            {
                if (key != Keys.OemBackslash || !Program.Pressed.Contains(Keys.RMenu))
                    return ProcessKeyAction.Continue;

                // Show the search form
                Searchform.Text = "UniKey search";
                placeSearchform(swp | WinAPI.SWP_SHOWWINDOW);
                Buffer = string.Empty;
                SearchformVisible = true;
                return ProcessKeyAction.ProcessedAndSwallow;
            }
            else
            {
                bool hide = false;
                ProcessKeyAction actionIfHide = ProcessKeyAction.Continue;
                if (key >= Keys.D0 && key <= Keys.D9)
                {
                    int index = key - Keys.D1;
                    if (Program.Pressed.Contains(Keys.LShiftKey) || Program.Pressed.Contains(Keys.RShiftKey))
                        index += 9;
                    if (SearchformCandidates != null && index < SearchformCandidates.Length)
                    {
                        Ut.SendKeystrokesForText(char.ConvertFromUtf32(SearchformCandidates[index].CodePoint));
                        hide = true;
                        actionIfHide = ProcessKeyAction.ProcessedAndSwallow;
                    }
                }
                else if ((key == Keys.OemBackslash || key == Keys.Escape || !KeyToChar.NoShift.ContainsKey(key)) && key != Keys.Back && key != Keys.LShiftKey && key != Keys.RShiftKey)
                {
                    hide = true;
                    actionIfHide = (key == Keys.OemBackslash || key == Keys.Escape)
                        ? ProcessKeyAction.ProcessedAndSwallow
                        : ProcessKeyAction.ProcessedButEmit;
                }

                if (hide)
                {
                    // Hide the search form
                    Buffer = string.Empty;
                    WinAPI.SetWindowPos(Searchform.Handle, (IntPtr) WinAPI.HWND_TOPMOST, 0, 0, 0, 0, swp | WinAPI.SWP_NOMOVE | WinAPI.SWP_HIDEWINDOW);
                    SearchformPrompt.Visible = true;
                    Searchform.ClientSize = SearchformPrompt.Size;
                    SearchformVisible = false;
                    return actionIfHide;
                }

                if (key != Keys.LShiftKey && key != Keys.RShiftKey)
                {
                    Searchform.Text = Buffer.Length == 0 ? "UniKey search" : Buffer;
                    initUnicodeDataCache();
                    string[] words = Buffer.Length == 0 ? null : Buffer.Split(' ').Where(s => s.Length > 0).Select(s => s.ToUpperInvariant()).ToArray();
                    SearchformCandidates = (words == null || words.Length == 0) ? null : UnicodeData
                        .Where(kvp => words.All(w => kvp.Value.Contains(w)))
                        .Select(kvp => new SearchItem { CodePoint = kvp.Key, Name = kvp.Value, Score = words.Sum(w => Regex.IsMatch(kvp.Value, "\\b" + Regex.Escape(w) + "\\b") ? 20 : 10) - (kvp.Value.Length / 3) })
                        .OrderBy(item => -item.Score)
                        .Take(18).ToArray();

                    if (SearchformCandidates == null || SearchformCandidates.Length == 0)
                    {
                        SearchformPrompt.Visible = true;
                        SearchformList.Visible = false;
                        Searchform.ClientSize = SearchformPrompt.Size;
                    }
                    else
                    {
                        for (int i = 0; i < SearchformCandidates.Length; i++)
                            SearchformCandidates[i].Index = i;
                        SearchformPrompt.Visible = false;
                        SearchformList.Items.Clear();
                        SearchformList.Items.AddRange(SearchformCandidates);
                        SearchformList.Visible = true;
                        Searchform.ClientSize = new Size(
                            SearchformCandidates.Max(c => TextRenderer.MeasureText("" + c.Name, smallFont).Width) +
                            SearchformCandidates.Max(c => TextRenderer.MeasureText(char.ConvertFromUtf32(c.CodePoint), bigFont).Width) +
                            SearchformCandidates.Max(c => TextRenderer.MeasureText("" + c.GetShortcut(Shortcuts), mediumFont).Width) + 125, (TextRenderer.MeasureText("Wg", bigFont).Height + 7) * SearchformCandidates.Count() + 5);
                    }

                    placeSearchform(swp);
                }
                return ProcessKeyAction.ProcessedAndSwallow;
            }
        }

        private void placeSearchform(uint flags)
        {
            IntPtr curFocus = WinAPI.GetFocusedControlInActiveWindow(Searchform.Handle);
            WinAPI.RECT rect = new WinAPI.RECT();
            WinAPI.GetWindowRect(curFocus, ref rect);

            Point[] candidates = new Point[] {
                new Point(rect.Left, rect.Bottom + 2),
                new Point(rect.Right - Searchform.Width, rect.Bottom + 2),
                new Point(rect.Left, rect.Top - Searchform.Height - 2),
                new Point(rect.Right - Searchform.Width, rect.Top - Searchform.Height - 2),
                new Point(rect.Right + 2, rect.Top),
                new Point(rect.Right + 2, rect.Bottom - Searchform.Height),
                new Point(rect.Left - Searchform.Width - 2, rect.Top),
                new Point(rect.Left - Searchform.Width - 2, rect.Bottom - Searchform.Height),
            };
            Point position;
            var workingArea = Screen.GetWorkingArea(Searchform);
            try { position = candidates.First(p => p.X > workingArea.Left && p.X + Searchform.Width < workingArea.Right && p.Y > workingArea.Top && p.Y + Searchform.Height < workingArea.Bottom); }
            catch (InvalidOperationException) { position = new Point(workingArea.Right - Searchform.Width, workingArea.Bottom - Searchform.Height); }
            WinAPI.SetWindowPos(Searchform.Handle, (IntPtr) WinAPI.HWND_TOPMOST, position.X, position.Y, 0, 0, flags);
        }

        private void initUnicodeDataCache()
        {
            if (UnicodeData != null)
                return;
            UnicodeData = new Dictionary<int, string>();
            foreach (var line in Resources.UnicodeData.Replace("\r", "").Split('\n'))
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
    }
}
