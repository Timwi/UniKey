using System.Text.RegularExpressions;

namespace UniKey;

sealed class CommandInfo
{
    public string Regex;
    public string CommandName;
    public string HelpString;
    public Func<Match, ReplaceResult> Function;

    public CommandInfo(string regex, string commandName, string helpString, Func<Match, ReplaceResult> function)
    {
        Regex = regex;
        CommandName = commandName;
        HelpString = helpString;
        Function = function;
    }
}
