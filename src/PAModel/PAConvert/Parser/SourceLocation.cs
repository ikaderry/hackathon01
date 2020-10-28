using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace Microsoft.PowerPlatform.Formulas.Tools.IR
{
    internal struct SourceLocation
    {
        public readonly int StartLine;
        public readonly int StartChar;
        public readonly int EndLine;
        public readonly int EndChar;
        public readonly string FileName;

        public SourceLocation(int startLine, int startChar, int endLine, int endChar, string fileName)
        {
            StartLine = startLine;
            StartChar = startChar;
            EndLine = endLine;
            EndChar = endChar;
            FileName = fileName;
        }

        public static SourceLocation FromChildren(List<SourceLocation> locations)
        {
            SourceLocation minLoc = locations.First(), maxLoc = locations.First();

            foreach (var loc in locations)
            {
                if (loc.StartLine < minLoc.StartLine ||
                    (loc.StartLine == minLoc.StartLine && loc.StartChar < minLoc.StartChar))
                {
                    minLoc = loc;
                }
                if (loc.EndLine > maxLoc.EndLine ||
                    (loc.EndLine == minLoc.EndLine && loc.EndChar > minLoc.EndChar))
                {
                    maxLoc = loc;
                }
            }

            return new SourceLocation(minLoc.StartLine, minLoc.StartChar, maxLoc.EndLine, maxLoc.EndChar, maxLoc.FileName);
        }
    }
}
