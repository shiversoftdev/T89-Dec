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

        public GSIInfo GSI { get; set; }

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
        public Dictionary<int, ScriptImport> Imports { get; set; }

        /// <summary>
        /// Gets or Sets the list of Script Exports
        /// </summary>
        public Dictionary<int, ScriptAnimTree> AnimTrees = new Dictionary<int, ScriptAnimTree>();

        /// <summary>
        /// List of anims for back reference decompilation
        /// </summary>
        public Dictionary<int, ScriptAnim> AnimBackReferences = new Dictionary<int, ScriptAnim>();

        public Dictionary<int, string> GlobalObjects { get; set; }

        /// <summary>
        /// Hash References that weren't replaced
        /// </summary>
        public Dictionary<uint, string> HashReferences = new Dictionary<uint, string>();

        public Dictionary<string, Scr_Class> Classes = new Dictionary<string, Scr_Class>();

        /// <summary>
        /// Initializes an instance of the Script Class
        /// </summary>
        public ScriptBase(BinaryReader reader, Dictionary<uint, string> hashTable, Dictionary<ulong, string> qword_hashTable)
        {
            Reader = reader;
            DWORDHashTable = hashTable;
            QWORDHashTable = qword_hashTable;
        }

        public ScriptBase Load(GSIInfo inf)
        {
            GSI = inf;
            LoadHeader();
            LoadIncludes();
            LoadAnimTrees();
            LoadStrings();
            LoadImports();
            LoadExports();
            LoadGlobalObjects();
            LoadClasses();
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

        public abstract void LoadClasses();

        public abstract List<ScriptOpSwitch> LoadEndSwitch();
        public abstract int GetJumpLocation(int from, int to);
        public abstract ScriptOp LoadOperation(int offset);

        public void LoadFunction(ScriptExport function)
        {
            var eip = function.ByteCodeOffset;
            var endOffset = function.ByteCodeOffset + function.ByteCodeSize;
            Stack<int> OperationOffsets = new Stack<int>();
            int UnclosedSwitches = 0;
            int instr;
            //Stack<int> SwitchOffsets = new Stack<int>();
            while (function.ByteCodeSize == 0 // on black ops 3+ the end of the function is garbage filled when dumped.
                || OperationOffsets.Count > 0) 
            {
                ScriptOp op;
                try
                {
                    while ((instr = GetInstructionAt(function, eip)) != -1 && OperationOffsets.Count > 0)
                        eip = OperationOffsets.Pop();

                    if ((instr = GetInstructionAt(function, eip)) != -1) break;

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

                    if (op.Metadata.OpCode == ScriptOpCode.GetLocalFunction)
                    {
                        function.LocalFunctions[op] = new ScriptExport()
                        {
                            Checksum = 0xFFFFFFFF,
                            ByteCodeOffset = int.Parse(op.Operands[0].Value.ToString()),
                            Name = "anonymous",
                            Namespace = "anonymous",
                            Namespace2 = "anonymous",
                            ParameterCount = 0,
                            Flags = 0,
                            IsLocal = true
                        };
                        LoadFunction(function.LocalFunctions[op]);
                    }

                    if (op.Metadata.OpType == ScriptOpType.Return || op.Metadata.OpType == ScriptOpType.Jump)
                    {
                        endOffset = Math.Max(endOffset, eip + op.OpCodeSize);
                        if (UnclosedSwitches > 0)
                        {
                            instr = GetInstructionAt(function, eip);
                            eip += op.OpCodeSize;
                            while ((instr = GetInstructionAt(function, eip)) != -1) eip += function.Operations[instr].OpCodeSize; // we always expect a switch here
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

            function.Operations = function.Operations.OrderBy(x => x.OpCodeOffset).ToList();
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

        public Dictionary<string, HashSet<string>> ExportCollection = new Dictionary<string, HashSet<string>>();

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

        public string CreateTabDepth(int numTabs)
        {
            string result = "";
            for(int i = 0; i < numTabs; i++)
            {
                result += "\t";
            }
            return result;
        }

        public void EmitFunction(StringBuilder output, ref int lineNumber, ref string nameSpace, ScriptExport function, int numTabs, bool noNsOutput = false)
        {
            // Write the namspace if it differs
            if (!string.IsNullOrWhiteSpace(function.Namespace) && function.Namespace != nameSpace)
            {
                nameSpace = function.Namespace;
                if(!noNsOutput)
                {
                    output.AppendLine(string.Format("#namespace {0};\n", nameSpace));
                    lineNumber += 2;
                }    
            }

            string tabBasis = CreateTabDepth(numTabs);
            // Spit out some info
            output.AppendLine(tabBasis + "/*");
            output.AppendLine(tabBasis + string.Format("\tName: {0}", function.Name));
            output.AppendLine(tabBasis + string.Format("\tNamespace: {0}", function.Namespace));
            output.AppendLine(tabBasis + string.Format("\tChecksum: 0x{0:X}", function.Checksum));
            output.AppendLine(tabBasis + string.Format("\tOffset: 0x{0:X}", function.ByteCodeOffset));
            output.AppendLine(tabBasis + string.Format("\tSize: 0x{0:X}", function.ByteCodeSize));
            output.AppendLine(tabBasis + string.Format("\tParameters: {0}", function.ParameterCount));
            output.AppendLine(tabBasis + string.Format("\tFlags: {0}", ExportFlagsToString((byte)function.Flags)));
            output.AppendLine(tabBasis + "*/");
            lineNumber += 9;

            using (var decompiler = new Decompiler(function, this, numTabs))
            {
                function.DecompilerLine = lineNumber;
                var result = decompiler.GetWriterOutput();
                output.Append(tabBasis + result);
                output.AppendLine();
                lineNumber += Utility.GetLineCount(result);
                if(!ExportCollection.ContainsKey(function.Namespace))
                {
                    ExportCollection[function.Namespace] = new HashSet<string>();
                }
                ExportCollection[function.Namespace].Add(decompiler.BuildFunctionDefinition(true));
            }

            if (function.DirtyMessage != null)
            {
                output.AppendLine(tabBasis + $"/*{function.DirtyMessage}*/");
                lineNumber++;
            }
        }

        public string Decompile()
        {
            // Keep track of the line number for UI
            var output = new StringBuilder();
            int lineNumber = 0;

            // We need to store the current namespace
            var nameSpace = "";
            if(Header.VMRevision == 0x1C)
            {
                output.AppendLine("// Decompiled by Serious. Credits to Scoba for his original tool, Cerberus, which I heavily upgraded to support remaining features, other games, and other platforms.");
                lineNumber++;
            }

            if (Header.VMRevision == 0x36)
            {
                output.AppendLine("// Decompiled by Serious. Credits to Scoba for his original tool, Cerberus, which I heavily upgraded to support remaining features, other games, and other platforms.");
                lineNumber++;
            }

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

            foreach (var animTree in AnimTrees.Values)
            {
                output.AppendLine(string.Format("#using_animtree(\"{0}\");", animTree.Namespace));
                lineNumber++;
            }

            // Add a space
            if (AnimTrees.Count > 0)
            {
                output.AppendLine();
                lineNumber++;
            }

            HashSet<ScriptExport> ClassContainedCalls = new HashSet<ScriptExport>();

            foreach(var scrClass in Classes.Values)
            {
                ClassContainedCalls.Add(scrClass.Autogen);
                output.AppendLine(string.Format("class {0} {1}", scrClass.Name, (scrClass.SuperClasses.Count > 0) ? (": " + string.Join(", ", scrClass.SuperClasses.ToArray())): ""));
                output.AppendLine("{");
                lineNumber += 2;

                foreach(var variable in scrClass.Vars)
                {
                    output.AppendLine("\tvar " + variable + ";");
                    lineNumber += 1;
                }

                output.AppendLine();
                lineNumber += 1;

                if (scrClass.Constructor != null)
                {
                    ClassContainedCalls.Add(scrClass.Constructor);
                    scrClass.Constructor.Name = "constructor";
                    EmitFunction(output, ref lineNumber, ref nameSpace, scrClass.Constructor, 1, true);
                }

                if (scrClass.Destructor != null)
                {
                    ClassContainedCalls.Add(scrClass.Destructor);
                    scrClass.Destructor.Name = "destructor";
                    EmitFunction(output, ref lineNumber, ref nameSpace, scrClass.Destructor, 1, true);
                }

                foreach(var export in scrClass.IncludedExports.Values)
                {
                    EmitFunction(output, ref lineNumber, ref nameSpace, export, 1, true);
                    ClassContainedCalls.Add(export);
                }

                output.AppendLine("}");
                output.AppendLine();
                lineNumber += 2;
            }

            foreach (var function in Exports)
            {
                if(ClassContainedCalls.Contains(function))
                {
                    continue;
                }

                EmitFunction(output, ref lineNumber, ref nameSpace, function, 0);
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
            return Imports.ContainsKey(ptr) ? Imports[ptr] : default_import;
        }

        public ScriptExport GetExport(string ns, string fname)
        {
            foreach(var function in Exports)
            {
                if(function.Namespace != ns)
                {
                    continue;
                }
                if(function.Name != fname)
                {
                    continue;
                }
                return function;
            }
            return null;
        }

        /// <summary>
        /// Disposes of the Reader
        /// </summary>
        public void Dispose()
        {
            Reader.Dispose();
        }

        public ScriptAnimTree ResolveTreeForGetint(int pos)
        {
            foreach(ScriptAnimTree atr in AnimTrees.Values)
            {
                if (atr.UseAnimTreeEntries.Contains(pos))
                    return atr;
            }
            return null;
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
            var magic = reader.ReadUInt64();

            GSIInfo GSI = null;
            if ((magic & 0xFFFFFFFF) == 0x43495347)
            {
                GSI = new GSIInfo();
                GSI.NumFields = (int)(magic >> 32);

                for(int i = 0; i < GSI.NumFields; i++)
                {
                    var current = reader.ReadInt32();
                    var numEntries = reader.ReadInt32();

                    switch((GSIFields)current)
                    {
                        case GSIFields.Detours:
                            for(int j = 0; j < numEntries; j++)
                            {
                                GSI.Detours.Add(new ScriptDetour().Deserialize(reader));
                            }
                            break;
                    }
                }

                reader = new BinaryReader(new MemoryStream(reader.ReadBytes((int)(reader.BaseStream.Length - reader.BaseStream.Position))));
                magic = reader.ReadUInt64();
            }

            switch (magic)
            {
                case 0x38000A0D43534780:
                    ParseHashTables("t8_hash.map", "includes.map");
                    return new T9_VM38Script(reader, t8_dword, t8_qword).Load(GSI);
                case 0x37010A0D43534780:
                    ParseHashTables("t8_hash.map", "includes.map");
                    return new T9_VM37AScript(reader, t8_dword, t8_qword).Load(GSI);
                case 0x37000A0D43534780:
                    ParseHashTables("t8_hash.map", "includes.map");
                    return new T9_VM37Script(reader, t8_dword, t8_qword).Load(GSI);
                case 0x36000A0D43534780:
                    ParseHashTables("t8_hash.map", "includes.map");
                    return new BlackOps4Script(reader, t8_dword, t8_qword).Load(GSI);
                case 0x36FF0A0D43534780:
                    ParseHashTables("t8_hash.map", "includes.map");
                    return new BlackOps4Script(reader, t8_dword, t8_qword).SetPS4(true).Load(GSI);
                case 0x1B000A0D43534780:
                case 0x1C000A0D43534780:
                    LoadT7Hashes("t7_hash.map");
                    return new T7VM1CScript(reader, t7_dword).Load(GSI);
                case 0x1CFF0A0D43534780:
                    LoadT7Hashes("t7_hash.map");
                    return new T7VM1CScript(reader, t7_dword).SetPS4(true).Load(GSI);
                default:
                    throw new ArgumentException($"Invalid Script Magic Number; {magic:X}", "Magic");
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
    public class Scr_Class
    {
        public string Name;
        public Dictionary<string, ScriptExport> IncludedExports = new Dictionary<string, ScriptExport>();
        public ScriptExport Constructor;
        public ScriptExport Destructor;
        public ScriptExport Autogen;
        public HashSet<string> Vars = new HashSet<string>();
        public HashSet<string> SuperClasses = new HashSet<string>();
    }

    public class GSIInfo
    {
        public int NumFields = 0;
        public List<ScriptDetour> Detours = new List<ScriptDetour>();
    }

    public enum GSIFields
    {
        Detours = 0
    }

    public class ScriptDetour
    {
        private const int DetourNameMaxLength = 256 - 1 - (5 * 4);
        public uint FixupName;
        public uint ReplaceNamespace;
        public uint ReplaceFunction;
        public uint FixupOffset;
        public uint FixupSize;
        public string ReplaceScript;

        public override string ToString()
        {
            return $"{ReplaceNamespace:X}:{ReplaceFunction}:{ReplaceScript ?? "system"}";
        }

        public byte[] Serialize()
        {
            List<byte> toReturn = new List<byte>();
            toReturn.AddRange(BitConverter.GetBytes(FixupName));
            toReturn.AddRange(BitConverter.GetBytes(ReplaceNamespace));
            toReturn.AddRange(BitConverter.GetBytes(ReplaceFunction));
            toReturn.AddRange(BitConverter.GetBytes(FixupOffset));
            toReturn.AddRange(BitConverter.GetBytes(FixupSize));

            byte[] scriptPathBytes = new byte[DetourNameMaxLength + 1];
            if (ReplaceScript != null)
            {
                Encoding.ASCII.GetBytes(ReplaceScript.Substring(0, Math.Min(ReplaceScript.Length, DetourNameMaxLength))).CopyTo(scriptPathBytes, 0);
            }
            toReturn.AddRange(scriptPathBytes);
            return toReturn.ToArray();
        }

        public ScriptDetour Deserialize(BinaryReader reader)
        {
            FixupName = reader.ReadUInt32();
            ReplaceNamespace = reader.ReadUInt32();
            ReplaceFunction = reader.ReadUInt32();
            FixupOffset = reader.ReadUInt32();
            FixupSize = reader.ReadUInt32();
            byte[] scriptPathBytes = reader.ReadBytes(DetourNameMaxLength + 1);
            string res = Encoding.ASCII.GetString(scriptPathBytes).Replace("\x00", "").Trim();
            if (scriptPathBytes[0] != 0)
            {
                ReplaceScript = res;
            }
            return this;
        }
    }
}
