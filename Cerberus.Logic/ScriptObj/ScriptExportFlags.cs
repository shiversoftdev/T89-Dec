using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Cerberus.Logic
{
    public enum v1cScriptExportFlags
    {
        None = 0x0,
        Linked = 0x1,
        AutoExec = 0x2,
        Private = 0x4,
        Variadic = 0x20,
        Event = 64
    }

    public enum v38ExportFlags
    {
        None = 0x0,
        AutoExec = 0x1,
        Linked = 0x2,
        Private = 4,
        Event = 32,
        Variadic = 64
    }
}
