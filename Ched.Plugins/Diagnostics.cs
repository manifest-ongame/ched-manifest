using System;
using System.Collections.Generic;
using System.Dynamic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Ched.Plugins
{
    public interface IDiagnosable
    {
        void ReportDiagnostic(Diagnostic diagnostic);
    }

    public class Diagnostic
    {
        public DiagnosticSeverity Severity { get; }
        public string Message { get; }

        protected Diagnostic(DiagnosticSeverity severity, string message)
        {
            Severity = severity;
            Message = message;
        }
    }

    public enum DiagnosticSeverity
    {
        Hidden = 0,
        Info = 1,
        Warning = 2,
        Error = 3
    }
}
