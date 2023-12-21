namespace UniKey;

sealed class ReplaceResult(int replaceLength, string replaceWith)
{
    public int ReplaceLength => replaceLength;
    public string ReplaceWith => replaceWith;
}
