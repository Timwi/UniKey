using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace UniKey
{
    sealed class ReplaceResult
    {
        private int _replaceLength;
        private string _replaceWith;
        public ReplaceResult(int replaceLength, string replaceWith)
        {
            _replaceLength = replaceLength;
            _replaceWith = replaceWith;
        }
        public int ReplaceLength { get { return _replaceLength; } }
        public string ReplaceWith { get { return _replaceWith; } }
    }
}
