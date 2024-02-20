using System.Text.RegularExpressions;

namespace UniKey;

sealed class CommandInfo(string regex, string commandName, string helpString, Func<Match, ReplaceResult> function)
{
    public readonly string Regex = regex;
    public readonly string CommandName = commandName;
    public readonly string HelpString = helpString;
    public readonly Func<Match, ReplaceResult> Function = function;
}
