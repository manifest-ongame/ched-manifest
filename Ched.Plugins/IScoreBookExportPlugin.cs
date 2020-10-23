using System;
using System.Collections.Generic;
using System.ComponentModel.Design;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

using Ched.Core;

namespace Ched.Plugins
{
    /// <summary>
    /// 譜面データのエクスポートを行うプラグインを表します。
    /// </summary>
    public interface IScoreBookExportPlugin : IPlugin
    {
        string FileFilter { get; }
        void Export(ScoreBook book, TextWriter writer);
        string GetCustomData();
        void SetCustomData();
    }
}
