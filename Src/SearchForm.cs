using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Windows.Forms;
using RT.Util;

namespace UniKey
{
    internal class SearchForm : Form
    {
        protected override void WndProc(ref Message m)
        {
            if (m.Msg == WinAPI.WM_MOUSEACTIVATE)
            {
                m.Result = (IntPtr) WinAPI.MA_NOACTIVATE;
                return;
            }
            base.WndProc(ref m);
        }

        protected override bool ShowWithoutActivation { get { return true; } }
    }
}
