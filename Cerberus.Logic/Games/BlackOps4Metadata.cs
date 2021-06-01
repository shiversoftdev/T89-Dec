using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Cerberus.Logic
{
    public partial class BlackOps4Script
    {
        private static void LoadTable()
        {
            if(PrimaryTable == null)
            {
                PrimaryTable = new ScriptOpCode[0x1000];
                string[] lines = File.ReadAllLines("Games\\t8_primary.txt");
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
                SecondaryTable = new ScriptOpCode[0x1000];
                string[] lines = File.ReadAllLines("Games\\t8_orbis.txt");
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
            byte[] data = new byte[table.Length];
            int i = 0;
            foreach (var code in table)
                data[i++] = (byte)code;
            return data;
        }
        // t8 pc
        private static ScriptOpCode[] PrimaryTable = null;

        // t8 ps4
        private static ScriptOpCode[] SecondaryTable = null;
    }
}
