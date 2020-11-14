using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Cerberus.Logic
{
    public partial class T9_VM37Script
    {
        private static void LoadTable()
        {
            if(PrimaryTable == null)
            {
                PrimaryTable = new ScriptOpCode[0x1000];
                string[] lines = File.ReadAllLines("Games\\t9_vm37_codes.txt");
                foreach(string line in lines)
                {
                    string[] split = line.Split('=');
                    split[1] = split[1].Substring(0, split[1].IndexOf(';')).Trim();
                    split[0] = split[0].Replace("0x", "");
                    int index = int.Parse(split[0], System.Globalization.NumberStyles.HexNumber);
                    if (!Enum.TryParse(split[1], true, out PrimaryTable[index]))
                        PrimaryTable[index] = ScriptOpCode.Invalid;
                }
            }
        }
        // Black Ops 4 Stock OP Code Table (Stock GSCs)
        private static ScriptOpCode[] PrimaryTable = null;
        public static byte[] GetTableData()
        {
            LoadTable();
            byte[] data = new byte[PrimaryTable.Length];
            int i = 0;
            foreach (var code in PrimaryTable)
                data[i++] = (byte)code;
            return data;
        }

        private static ScriptOpCode[] SecondaryTable =
        {
            
        };
    }
}
