using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Text;
using System.Windows.Forms;
using RT.Util.Controls;

namespace UniKey
{
    sealed class HelpForm : Form
    {
        TableLayoutPanel _layout;
        ScrollableLabelEx _label;
        Button _okButton;

        public HelpForm(string content)
        {
            Text = "UniKey Commands";
            StartPosition = FormStartPosition.Manual;
            Width = Screen.PrimaryScreen.WorkingArea.Width / 2;
            Height = Screen.PrimaryScreen.WorkingArea.Height * 7 / 8;
            Left = Screen.PrimaryScreen.WorkingArea.Left + Screen.PrimaryScreen.WorkingArea.Width / 4;
            Top = Screen.PrimaryScreen.WorkingArea.Top + Screen.PrimaryScreen.WorkingArea.Height / 16;
            Font = new Font("Calibri", 12);
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MinimizeBox = false;
            MaximizeBox = false;

            _layout = new TableLayoutPanel { Dock = DockStyle.Fill };
            _layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            _layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));

            _label = new ScrollableLabelEx { Dock = DockStyle.Fill, Padding = new Padding(20, 20, 20, 20) };
            _label.Label.Text = content;

            _okButton = new Button { Text = "OK", Anchor = AnchorStyles.Right, Height = 30, Width = 100 };
            _okButton.Click += close;

            _layout.Controls.Add(_label, 0, 0);
            _layout.Controls.Add(_okButton, 0, 1);
            Controls.Add(_layout);
            AcceptButton = _okButton;
            CancelButton = _okButton;
        }

        private void close(object _, EventArgs __)
        {
            Close();
        }

        protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
        {
            switch (keyData)
            {
                case Keys.Down:
                    _label.ScrollTo(_label.VerticalScroll.Value + 25);
                    break;
                case Keys.Up:
                    _label.ScrollTo(_label.VerticalScroll.Value - 25);
                    break;
                case Keys.PageDown:
                    _label.ScrollTo(_label.VerticalScroll.Value + _label.Height * 7 / 8);
                    break;
                case Keys.PageUp:
                    _label.ScrollTo(_label.VerticalScroll.Value - _label.Height * 7 / 8);
                    break;
                case Keys.Home:
                    _label.ScrollTo(0);
                    break;
                case Keys.End:
                    _label.ScrollTo(_label.Label.Height);
                    break;

                default:
                    return base.ProcessCmdKey(ref msg, keyData);
            }
            return true;
        }
    }
}
