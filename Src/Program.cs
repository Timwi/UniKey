using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows.Forms;
using RT.Util;
using System.IO;
using System.Text;
using System.Threading;
using System.Runtime.InteropServices;
using System.ComponentModel;
using System.Diagnostics;

namespace UniKey
{
    internal enum ProcessKeyAction
    {
        Continue,
        ProcessedButEmit,
        ProcessedAndSwallow
    }

    static class Program
    {
        internal static KeyboardMode Mode = new KeyboardMode();
        internal static Dictionary<string, KeyboardMode> Modes = new Dictionary<string, KeyboardMode>();
        internal static List<Keys> Pressed = new List<Keys>();
        internal static GlobalKeyboardListener KeyboardListener;
        static bool Processing = false;

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

            Modes.Add(" ", new KeyboardMode());
            Modes.Add("eo", new EoMode());
            Modes.Add("de", new DeMode());
            // Modes.Add("cyr", new CyrMode());
            Modes.Add("u", new UniversalMode());

            KeyboardListener = new GlobalKeyboardListener();
            KeyboardListener.HookAllKeys = true;
            KeyboardListener.KeyDown += new KeyEventHandler(keyDown);
            KeyboardListener.KeyUp += new KeyEventHandler(keyUp);
            Application.Run();
        }

        static void keyUp(object sender, KeyEventArgs e)
        {
            if (Mode == null)
                Mode = new KeyboardMode();

            if (Processing)
                return;
            Processing = true;
            var result = Mode.ProcessKeyUp(e.KeyCode, true);
            if (result == ProcessKeyAction.ProcessedAndSwallow)
                e.Handled = true;
            Processing = false;
        }

        static void keyDown(object sender, KeyEventArgs e)
        {
            if (Mode == null)
                Mode = new KeyboardMode();

            if (Processing)
                return;
            Processing = true;
            var result = Mode.ProcessKeyDown(e.KeyCode);
            if (result == ProcessKeyAction.ProcessedAndSwallow)
                e.Handled = true;
            Processing = false;
        }
    }
}
