using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Ched.Plugins
{
    public class ScoreBookImportPluginArgs : IScoreBookImportPluginArgs
    {
        private List<Diagnostic> _diagnostics { get; } = new List<Diagnostic>();
        public IReadOnlyList<Diagnostic> Diagnostics => _diagnostics;
        public TextReader Reader { get; }

        public ScoreBookImportPluginArgs(TextReader reader)
        {
            Reader = reader;
        }

        public void ReportDiagnostic(Diagnostic diagnostic)
        {
            _diagnostics.Add(diagnostic);
        }
    }
}
