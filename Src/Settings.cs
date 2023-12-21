using RT.Util;

namespace UniKey;

class Settings
{
    public bool MouseGridEnabled;
    public string UnicodeDataFile = null;
    public Dictionary<string, string> Replacers = [];
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
