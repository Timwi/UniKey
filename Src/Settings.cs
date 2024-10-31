namespace UniKey;

class Settings
{
    public bool MouseGridEnabled;
    public bool UndoDisabled;
    public string UnicodeDataFile = null;
    public Dictionary<string, string> Replacers = [];
    public string DebugLogPath = null;
}
