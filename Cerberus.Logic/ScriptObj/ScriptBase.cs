using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.IO;

namespace Cerberus.Logic
{
    /// <summary>
    /// Base Script File Class
    /// </summary>
    public abstract class ScriptBase : IDisposable
    {
        public delegate void VerboseLogging(object value);
        public VerboseLogging VLog;
        /// <summary>
        /// Gets or Sets the Script File Path
        /// </summary>
        public string FilePath { get; set; }

        /// <summary>
        /// Gets the Script File Name
        /// </summary>
        public string FileName => Path.GetFileName(FilePath);

        /// <summary>
        /// Gets or Sets the name of the Game
        /// </summary>
        public abstract string Game { get; }

        /// <summary>
        /// Gets or Sets the Hash Table of Names, DVARs, etc.
        /// </summary>
        public Dictionary<uint, string> DWORDHashTable { get; set; }
        public Dictionary<ulong, string> QWORDHashTable { get; set; }

        /// <summary>
        /// Gets or Sets the Script Data Stream
        /// </summary>
        public BinaryReader Reader { get; set; }

        /// <summary>
        /// Size of the Data Stream as a KB String
        /// </summary>
        public string DisplaySize => string.Format("{0:0.000} KB", Reader.BaseStream.Position / 1000.0);

        /// <summary>
        /// Gets or Sets the Script Header
        /// </summary>
        public ScriptHeader Header { get; set; }

        /// <summary>
        /// Gets or Sets the list of Script Includes
        /// </summary>
        public List<ScriptInclude> Includes { get; set; }

        /// <summary>
        /// Gets or Sets the list of Script Strings
        /// </summary>
        public List<ScriptString> Strings { get; set; }

        /// <summary>
        /// Gets or Sets the list of Script Imports
        /// </summary>
        public List<ScriptExport> Exports { get; set; }

        /// <summary>
        /// Gets or Sets the list of Script Exports
        /// </summary>
        public List<ScriptImport> Imports { get; set; }

        /// <summary>
        /// Gets or Sets the list of Script Exports
        /// </summary>
        public List<ScriptAnimTree> AnimTrees { get; set; }

        public Dictionary<int, string> GlobalObjects { get; set; }

        /// <summary>
        /// Hash References that weren't replaced
        /// </summary>
        public Dictionary<uint, string> HashReferences = new Dictionary<uint, string>();

        /// <summary>
        /// Initializes an instance of the Script Class
        /// </summary>
        public ScriptBase(BinaryReader reader, Dictionary<uint, string> hashTable, Dictionary<ulong, string> qword_hashTable)
        {
            Reader = reader;
            DWORDHashTable = hashTable;
            QWORDHashTable = qword_hashTable;
        }

        public ScriptBase Load()
        {
            LoadHeader();
            LoadIncludes();
            LoadAnimTrees();
            LoadStrings();
            LoadImports();
            LoadExports();
            LoadGlobalObjects();
            return this;
        }

        /// <summary>
        /// Initializes an instance of the Script Class
        /// </summary>
        public ScriptBase(Stream stream, Dictionary<uint, string> dword_hashTable, Dictionary<ulong, string> qword_hashTable) : this(new BinaryReader(stream), dword_hashTable, qword_hashTable) { }

        /// <summary>
        /// Loads the Header from the GSC File
        /// </summary>
        public abstract void LoadHeader();
        public abstract void LoadIncludes();
        public abstract void LoadAnimTrees();
        public abstract void LoadStrings();
        public abstract void LoadImports();
        public abstract void LoadExports();
        public abstract void LoadGlobalObjects();
        public abstract List<ScriptOpSwitch> LoadEndSwitch();
        public abstract int GetJumpLocation(int from, int to);
        public abstract ScriptOp LoadOperation(int offset);

        public void LoadFunction(ScriptExport function)
        {
            var eip = function.ByteCodeOffset;
            var endOffset = function.ByteCodeOffset + function.ByteCodeSize;
            Stack<int> OperationOffsets = new Stack<int>();
            int UnclosedSwitches = 0;
            //Stack<int> SwitchOffsets = new Stack<int>();
            while (function.ByteCodeSize == 0 // on black ops 3+ the end of the function is garbage filled when dumped.
                || OperationOffsets.Count > 0) 
            {
                ScriptOp op;
                try
                {
                    op = LoadOperation(eip);
                    if (op == null) break;
                    function.Operations.Add(op);

                    if(op.Metadata.OpCode == ScriptOpCode.Switch) UnclosedSwitches++;
                    if (op.Metadata.OpCode == ScriptOpCode.EndSwitch) UnclosedSwitches--;

                    if (op.Metadata.OpType == ScriptOpType.Jump || 
                    op.Metadata.OpType == ScriptOpType.JumpCondition ||
                    op.Metadata.OpType == ScriptOpType.JumpExpression)
                    {
                        OperationOffsets.Push(GetJumpLocation(op.OpCodeOffset + op.OpCodeSize, int.Parse(op.Operands[0].Value.ToString())));
                    }
                    if(op.Metadata.OpType == ScriptOpType.Return)
                    {
                        endOffset = Math.Max(endOffset, eip + op.OpCodeSize);
                        if (UnclosedSwitches > 0)
                        {
                            eip += op.OpCodeSize;
                        }
                        else
                        {
                            while (GetInstructionAt(function, eip) != -1 && OperationOffsets.Count > 0)
                                eip = OperationOffsets.Pop();
                            if (GetInstructionAt(function, eip) != -1) break;
                        }
                        continue;
                    }
                    eip += op.OpCodeSize;
                }
                catch(ArgumentException e)
                {
                    function.DirtyMessage = $"{e.Message} at {eip:X4}";
                    break;
                }
            }

            function.ByteCodeSize = endOffset - function.ByteCodeOffset;
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="offset"></param>
        /// <returns></returns>
        private int GetInstructionAt(ScriptExport function, int offset)
        {
            return function.Operations.FindIndex(x => x.OpCodeOffset == offset);
        }


        private string ExportFlagsToString(byte flags)
        {
            List<string> results = new List<string>();
            for(int i = 0; i < 8; i++)
            {
                byte f = (byte)(1 << i);
                if ((flags & f) == 0) continue;
                if (Header.VMRevision <= 0x37 && Enum.IsDefined(typeof(v1cScriptExportFlags), (int)f)) results.Add(((v1cScriptExportFlags)f).ToString());
                else if (Header.VMRevision > 0x37 && Enum.IsDefined(typeof(v38ExportFlags), (int)f)) results.Add(((v38ExportFlags)f).ToString());
                else results.Add(f.ToString());
            }
            return results.Count > 0 ? string.Join(", ", results) : "None";
        }

        /// <summary>
        /// Disassembles the entire script and returns a string containing the disassembly
        /// </summary>
        public string Disassemble()
        {
            // Keep track of the line number for UI
            var lineNumber = 0;
            var output = new StringBuilder();

            foreach(var include in Includes)
            {
                output.AppendLine(string.Format("#using {0};", include));
                lineNumber++;
            }

            // Add a space
            if (Includes.Count > 0)
            {
                output.AppendLine();
                lineNumber++;
            }

            foreach (var function in Exports)
            {
                try
                {
                    // Spit out some info
                    output.AppendLine("/*");
                    output.AppendLine(string.Format("\tName: {0}", function.Name));
                    output.AppendLine(string.Format("\tNamespace: {0}", function.Namespace));
                    output.AppendLine(string.Format("\tChecksum: 0x{0:X}", function.Checksum));
                    output.AppendLine(string.Format("\tOffset: 0x{0:X}", function.ByteCodeOffset));
                    output.AppendLine(string.Format("\tSize: 0x{0:X}", function.ByteCodeSize));
                    output.AppendLine(string.Format("\tParameters: {0}", function.ParameterCount));
                    output.AppendLine(string.Format("\tFlags: {0}", ExportFlagsToString((byte)function.Flags)));
                    output.AppendLine("*/");
                    lineNumber += 9;

                    // Use the liner number AFTER the info above, we want to go
                    // to the literal start
                    function.DisassemblyLine = lineNumber;

                    // If we have a namespace we can add it, for decompiler we'll use
                    // #namespace but for disassembly we'll add it to the call
                    output.AppendLine(string.Format("function {0}{1}(...)",
                        string.IsNullOrWhiteSpace(function.Namespace) ? "" : function.Namespace + "::",
                        function.Name));
                    output.AppendLine("{");
                    lineNumber += 2;

                    int index = 0;
                    foreach(var operation in function.Operations)
                    {
                        // Add IP and Size Info
                        output.AppendFormat("\t/* IP: 0x{0} - Size 0x{1} Index: {2} */\t\t\tOP_{3}(",
                            operation.OpCodeOffset.ToString("X6"),
                            operation.OpCodeSize.ToString("X4"),
                            index.ToString("X3"),
                            operation.Metadata.OpCode);

                        for (int i = 0; i < operation.Operands.Count; i++)
                        {
                            if(operation.Metadata.OpType == ScriptOpType.Jump || operation.Metadata.OpType == ScriptOpType.JumpCondition || operation.Metadata.OpType == ScriptOpType.JumpExpression)
                            {
                                output.AppendFormat("{0}{1}", "0x" + (operation.OpCodeSize + operation.OpCodeOffset + int.Parse(operation.Operands[i].Value.ToString())).ToString("X"), i == operation.Operands.Count - 1 ? "" : ", ");
                            }
                            else
                            {
                                output.AppendFormat("{0}{1}", operation.Operands[i].Value, i == operation.Operands.Count - 1 ? "" : ", ");
                            }
                            
                        }

                        output.AppendLine(");");
                        index++;
                        lineNumber++;
                    }

                    if(function.DirtyMessage != null)
                    {
                        output.AppendLine($"/*{function.DirtyMessage}*/");
                        lineNumber++;
                    }

                    output.AppendLine("}");
                    lineNumber++;
                }
                catch(Exception e)
                {
                    output.AppendLine("/* " + e.ToString() + " */");
                    lineNumber += e.ToString().Split('\n').Length;
                    output.AppendLine("}");
                }
            }

            return output.ToString();
        }

        public string Decompile()
        {
            // Keep track of the line number for UI
            var output = new StringBuilder();
            int lineNumber = 0;

            // We need to store the current namespace
            var nameSpace = "";

            foreach (var include in Includes)
            {
                output.AppendLine(string.Format("#using {0};", include));
                lineNumber++;
            }

            // Add a space
            if (Includes.Count > 0)
            {
                output.AppendLine();
                lineNumber++;
            }

            foreach (var animTree in AnimTrees)
            {
                output.AppendLine(string.Format("#using_animtree(\"{0}\");", animTree.Name));
                lineNumber++;
            }

            // Add a space
            if (AnimTrees.Count > 0)
            {
                output.AppendLine();
                lineNumber++;
            }

            foreach (var function in Exports)
            {
                // Write the namspace if it differs
                if (!string.IsNullOrWhiteSpace(function.Namespace) && function.Namespace != nameSpace)
                {
                    nameSpace = function.Namespace;
                    output.AppendLine(string.Format("#namespace {0};\n", nameSpace));
                    lineNumber += 2;
                }

                // Spit out some info
                output.AppendLine("/*");
                output.AppendLine(string.Format("\tName: {0}", function.Name));
                output.AppendLine(string.Format("\tNamespace: {0}", function.Namespace));
                output.AppendLine(string.Format("\tChecksum: 0x{0:X}", function.Checksum));
                output.AppendLine(string.Format("\tOffset: 0x{0:X}", function.ByteCodeOffset));
                output.AppendLine(string.Format("\tSize: 0x{0:X}", function.ByteCodeSize));
                output.AppendLine(string.Format("\tParameters: {0}", function.ParameterCount));
                output.AppendLine(string.Format("\tFlags: {0}", ExportFlagsToString((byte)function.Flags)));
                output.AppendLine("*/");
                lineNumber += 9;

                using (var decompiler = new Decompiler(function, this))
                {
                    function.DecompilerLine = lineNumber;
                    var result = decompiler.GetWriterOutput();
                    output.Append(result);
                    output.AppendLine();
                    lineNumber += Utility.GetLineCount(result);
                }

                if (function.DirtyMessage != null)
                {
                    output.AppendLine($"/*{function.DirtyMessage}*/");
                    lineNumber++;
                }
            }

            return output.ToString();
        }

        /// <summary>
        /// Exports Hash Table (unnamed variables, etc.)
        /// </summary>
        /// <returns></returns>
        public string ExportHashTable()
        {
            var output = new StringBuilder();

            output.AppendLine("hash,name");

            foreach(var hashVal in HashReferences)
            {
                output.AppendFormat("0x{0:X},{1}\n", hashVal.Key, hashVal.Value);
            }

            return output.ToString();
        }

        /// <summary>
        /// Gets the string for the given hash, otherwise returns the default value or a formatted hex value
        /// </summary>
        public string GetHashValue(uint value, string prefix = "__", string defaultVal = "")
        {
            if (value == 0) return "";

            // Check if it's in the hash table
            if(DWORDHashTable.TryGetValue(value, out var result))
            {
                return result;
            }

            // If we have a default value (i.e. Bo3 var names) use that
            if (!string.IsNullOrWhiteSpace(defaultVal))
            {
                return defaultVal;
            }

            // Otherwise we're just using the formatted result
            var hashed = string.Format("{0}{1:x}", prefix, value);

            // Add it to the list
            if(!HashReferences.ContainsKey(value))
            {
                HashReferences.Add(value, hashed);
            }

            return hashed;
        }

        /// <summary>
        /// Gets the string for the given hash, otherwise returns the default value or a formatted hex value
        /// </summary>
        public string GetHashValue(ulong value, string prefix = "__", string defaultVal = "")
        {
            if ((value & 0xFFFFFFFF00000000L) == 0)
                return GetHashValue((uint)value, prefix, defaultVal);

            if (QWORDHashTable != null && QWORDHashTable.TryGetValue(value, out string result))
                return result;

            if (!string.IsNullOrWhiteSpace(defaultVal))
            {
                return defaultVal;
            }

            return string.Format("{0}{1:x}", prefix, value);
        }

        /// <summary>
        /// Gets the string by pointer reference
        /// </summary>
        public ScriptString GetString(int ptr)
        {
            return Strings.Where(x => x.References.Contains(ptr)).FirstOrDefault();
        }

        private static ScriptImport default_import = new ScriptImport()
        {
            Name = "nullsub",
            Namespace = "null",
            ParameterCount = 0,
            References = new List<int>()
        };
        /// <summary>
        /// Gets the import by pointer reference
        /// </summary>
        public ScriptImport GetImport(int ptr)
        {
            return Imports.Where(x => x.References.Contains(ptr)).FirstOrDefault() ?? default_import;
        }

        /// <summary>
        /// Disposes of the Reader
        /// </summary>
        public void Dispose()
        {
            Reader.Dispose();
        }

        private static Dictionary<uint, string> t8_dword;
        private static Dictionary<ulong, string> t8_qword;
        private static Dictionary<uint, string> t7_dword;
        /// <summary>
        /// Loads the given script using the respective game class
        /// </summary>
        /// <param name="reader">Reader/Stream</param>
        public static ScriptBase LoadScript(BinaryReader reader, Dictionary<string, Dictionary<uint, string>> hashTables = null)
        {
            // We can use the magic to determine game
            switch(reader.ReadUInt64())
            {
                case 0x38000A0D43534780:
                    ParseHashTables("t8_hash.map", "includes.map");
                    return new T9_VM38Script(reader, t8_dword, t8_qword).Load();
                case 0x37000A0D43534780:
                    ParseHashTables("t8_hash.map", "includes.map");
                    return new T9_VM37Script(reader, t8_dword, t8_qword).Load();
                case 0x36000A0D43534780:
                    ParseHashTables("t8_hash.map", "includes.map");
                    return new BlackOps4Script(reader, t8_dword, t8_qword).Load();
                case 0x1C000A0D43534780:
                    LoadT7Hashes("t7_hash.map");
                    return new T7VM1CScript(reader, t7_dword).Load();
                case 0x1CFF0A0D43534780:
                    LoadT7Hashes("t7_hash.map");
                    var scr =  new T7VM1CScript(reader, t7_dword);
                    scr.IsPS4 = true;
                    return scr.Load();
                default:
                    throw new ArgumentException("Invalid Script Magic Number.", "Magic");
            }
        }

        private static void LoadT7Hashes(string filePath)
        {
            if (t7_dword != null) return;
            t7_dword = new Dictionary<uint, string>();
            if (!File.Exists(filePath)) return;
            var hashData = File.ReadAllBytes(filePath);
            int i = 0;
            while (i < hashData.Length)
            {
                uint dwordValue = BitConverter.ToUInt32(hashData, i);
                i += 4;
                byte strlen = hashData[i];
                string rawString = Encoding.ASCII.GetString(hashData, i + 1, strlen);
                i += 1 + strlen;
                t7_dword[dwordValue] = rawString;
            }
        }

        private static void ParseHashTables(string filePath, string include_map)
        {
            if (t8_dword != null)
                return;
            t8_dword = new Dictionary<uint, string>();
            t8_qword = new Dictionary<ulong, string>();
            if (!File.Exists(filePath) || !File.Exists(include_map))
                return;

            var hashData = File.ReadAllBytes(filePath);

            int i = 0;
            while(i < hashData.Length)
            {
                uint dwordValue = BitConverter.ToUInt32(hashData, i);
                ulong qwordValue = BitConverter.ToUInt64(hashData, i + 4);
                i += 12;
                byte strlen = hashData[i];
                string rawString = Encoding.ASCII.GetString(hashData, i + 1, strlen);
                i += 1 + strlen;
                t8_dword[dwordValue] = rawString;
                t8_qword[qwordValue] = rawString;
            }

            hashData = File.ReadAllBytes(include_map);
            i = 0;
            while(i < hashData.Length)
            {
                ulong include_id = BitConverter.ToUInt64(hashData, i);
                i += 8;
                byte strlen = hashData[i];
                string rawString = Encoding.ASCII.GetString(hashData, i + 1, strlen);
                i += 1 + strlen;
                t8_qword[include_id] = rawString;
            }
        }
    }
}
