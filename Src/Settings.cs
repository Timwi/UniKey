﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using RT.Util;

namespace UniKey
{
    class Settings
    {
        public bool MouseGridEnabled;
        public Dictionary<string, string> Replacers = new Dictionary<string, string>();
    }

    [Settings("UniKey", SettingsKind.MachineSpecific)]
    class MachineSettings : SettingsBase
    {
        public string SettingsPath = @"$(AppPath)\UniKey.settings.xml";
        public string SettingsPathExpanded
        {
            get { return PathUtil.ExpandPath(SettingsPath); }
            set { SettingsPath = PathUtil.UnexpandPath(value); }
        }
    }
}
