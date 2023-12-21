using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using RT.Util;

namespace UniKey
{
    class Settings
    {
        public bool MouseGridEnabled;
        public string UnicodeDataFile = null;
        public Dictionary<string, string> Replacers = new Dictionary<string, string>();
        public string DebugLogPath = null;
    }

    class MachineSettings
    {
        public string SettingsPath = @"$(AppPath)\UniKey.settings.xml";
        public string SettingsPathExpanded
        {
            get { return PathUtil.ExpandPath(SettingsPath); }
            set { SettingsPath = PathUtil.UnexpandPath(value); }
        }
    }
}
