using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Cerberus.Logic
{
    public partial class T7VM1CScript
    {
        private static void LoadTable()
        {
            if(PrimaryTable == null)
            {
                PrimaryTable = new ScriptOpCode[0x4000];
                string[] lines = File.ReadAllLines("Games\\t7vm1cpc.txt");
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
            if (SecondaryTable == null)
            {
                SecondaryTable = new ScriptOpCode[0x4000];
                string[] lines = File.ReadAllLines("Games\\t7vm1corbis.txt");
                foreach (string line in lines)
                {
                    string[] split = line.Split('=');
                    split[1] = split[1].Substring(0, split[1].IndexOf(';')).Trim();
                    split[0] = split[0].Replace("0x", "");
                    int index = int.Parse(split[0], System.Globalization.NumberStyles.HexNumber);
                    if (!Enum.TryParse(split[1], true, out SecondaryTable[index]))
                        SecondaryTable[index] = ScriptOpCode.Invalid;
                }
            }
        }

        public static byte[] GetTableData(bool isPS4)
        {
            LoadTable();
            var table = isPS4 ? SecondaryTable : PrimaryTable;
            byte[] data = new byte[isPS4 ? table.Length : (table.Length / 2)];
            int i = 0;
            foreach (var code in table)
            {
                data[i++] = (byte)code;
                if (i == data.Length) break;
            }
            return data;
        }
        // Black Ops 3 Stock OP Code Table PC
        private static ScriptOpCode[] PrimaryTable = null;
        // Black Ops 3 Stock OP Code Table PS4
        private static ScriptOpCode[] SecondaryTable = null;
    }
}
