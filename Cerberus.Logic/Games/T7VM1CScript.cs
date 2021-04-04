using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.IO;
using PhilLibX;
using PhilLibX.IO;
using System.Runtime.CompilerServices;

namespace Cerberus.Logic
{
    public partial class T7VM1CScript : ScriptBase
    {
        /// <summary>
        /// Returns the Game Name for Black Ops 3
        /// </summary>
        public override string Game => "T71C_" + (IsPS4 ? "Orbis" : "PC");
        internal bool IsPS4;

        public T7VM1CScript(Stream stream, Dictionary<uint, string> hashTable, Dictionary<ulong, string> qword_hashTable) : base(stream, hashTable, qword_hashTable) { }
        public T7VM1CScript(BinaryReader reader, Dictionary<uint, string> hashTable) : base(reader, hashTable, null) { }

        public override void LoadHeader()
        {
            LoadTable();
            
            // Ensure we're at the header (skip 8 byte magic)
            Reader.BaseStream.Position = 7;

            Header = new ScriptHeader()
            {
                VMRevision = Reader.ReadByte(),
                SourceChecksum = Reader.ReadUInt32(),
                IncludeTableOffset = Reader.ReadInt32(),
                AnimTreeTableOffset = Reader.ReadInt32(),
                ByteCodeOffset = Reader.ReadInt32(),
                StringTableOffset = Reader.ReadInt32(),
                DebugStringTableOffset = Reader.ReadInt32(),
                //0x20
                ExportTableOffset = Reader.ReadInt32(),//0x20
                ImportTableOffset = Reader.ReadInt32(),//0x24
                FixupTableOffset = Reader.ReadInt32(),//28
                ProfileTableOffset = Reader.ReadInt32(),//2c
                ByteCodeSize = Reader.ReadInt32(),//30
                NameOffset = Reader.ReadInt32(),//34
                StringCount = Reader.ReadInt16(),//38
                ExportsCount = Reader.ReadInt16(),//3a
                ImportsCount = Reader.ReadInt16(),//3c
                FixupCount = Reader.ReadInt16(),//3e
                ProfileCount = Reader.ReadInt16(),//40
                DebugStringCount = Reader.ReadInt16(),//42
                IncludeCount = Reader.ReadByte(),//44
                AnimTreeCount = Reader.ReadByte(),//45
                Flags = Reader.ReadByte()
            };

            // Get name of this script from the header
            Reader.BaseStream.Position = Header.NameOffset;
            FilePath = Reader.ReadNullTerminatedString();

            // Skip padding (header is 72 bytes in total)
            Reader.BaseStream.Position = 72;
        }

        public override void LoadGlobalObjects() {}

        public override void LoadStrings()
        {
            Reader.BaseStream.Position = Header.StringTableOffset;

            Strings = new List<ScriptString>(Header.StringCount + Header.DebugStringCount);

            for (int i = 0; i < Header.StringCount; i++)
            {
                var scriptString = new ScriptString()
                {
                    Offset = Reader.ReadInt32(),
                    References = new List<int>()
                };

                var referenceCount = Reader.ReadByte();
                Reader.BaseStream.Position += 3;

                // We need to store the references as we'll use them
                // for resolving strings instead of using the pointers
                for (int j = 0; j < referenceCount; j++)
                {
                    scriptString.References.Add(Reader.ReadInt32());
                }

                // Store our current position as we'll need to return back here
                var offset = Reader.BaseStream.Position;
                Reader.BaseStream.Position = scriptString.Offset;

                scriptString.Value = Reader.ReadNullTerminatedString();

                // Go back to the table
                Reader.BaseStream.Position = offset;

                Strings.Add(scriptString);
            }

            Reader.BaseStream.Position = Header.DebugStringTableOffset;

            // For dev/debug strings we only load them for the purposes
            // of satisifying the string look up
            for (int i = 0; i < Header.DebugStringCount; i++)
            {
                // Strings within dev blocks are stored in the gdb 
                // and we don't support them
                var scriptString = new ScriptString()
                {
                    Value = "",
                    Offset = Reader.ReadInt32(),
                    References = new List<int>()
                };

                var referenceCount = Reader.ReadByte();
                Reader.BaseStream.Position += 3;

                // We need to store the references as we'll use them
                // for resolving strings instead of using the pointers
                for (int j = 0; j < referenceCount; j++)
                {
                    scriptString.References.Add(Reader.ReadInt32());
                }

                Strings.Add(scriptString);
            }
        }

        public override void LoadExports()
        {
            Reader.BaseStream.Position = Header.ExportTableOffset;

            Exports = new List<ScriptExport>(Header.ExportsCount);

            var byteCodeEnd = Header.ByteCodeOffset + Header.ByteCodeSize;

            for(int i = 0; i < Header.ExportsCount; i++)
            {
                var export = new ScriptExport()
                {
                    Checksum       = Reader.ReadUInt32(),
                    ByteCodeOffset = Reader.ReadInt32(),
                    Name           = GetHashValue(Reader.ReadUInt32(), "function_"),
                    Namespace      = GetHashValue(Reader.ReadUInt32(), "namespace_"),
                    ParameterCount = Reader.ReadByte(),
                    Flags          = Reader.ReadByte()
                };
                Reader.BaseStream.Position += 2;
                var crc32 = new CRC32();

                // Store our current position as we'll need to return back here
                // (+ 2 to skip padding)
                var offset = Reader.BaseStream.Position;
                Reader.BaseStream.Position = export.ByteCodeOffset;
                LoadFunction(export);

                // Go back to the table
                Reader.BaseStream.Position = offset;

                Exports.Add(export);
            }
        }

        public override void LoadImports()
        {
            Reader.BaseStream.Position = Header.ImportTableOffset;

            Imports = new List<ScriptImport>(Header.ImportsCount);

            for (int i = 0; i < Header.ImportsCount; i++)
            {
                var import = new ScriptImport()
                {
                    Name = GetHashValue(Reader.ReadUInt32(), "function_"),
                    Namespace = GetHashValue(Reader.ReadUInt32(), "namespace_"),
                    References = new List<int>()
                };

                var referenceCount = Reader.ReadInt16();
                import.ParameterCount = Reader.ReadByte();
                import.Flags = $"{Reader.ReadByte():X2}";

                for (int j = 0; j < referenceCount; j++)
                {
                    import.References.Add(Reader.ReadInt32());
                }

                Imports.Add(import);
            }
        }

        public override void LoadIncludes()
        {
            Reader.BaseStream.Position = Header.IncludeTableOffset;

            Includes = new List<ScriptInclude>(Header.IncludeCount);

            for (int i = 0; i < Header.IncludeCount; i++)
            {
                Includes.Add(new ScriptInclude(Reader.PeekNullTerminatedString(Reader.ReadInt32())));
            }

            Includes.Sort();
        }

        public override ScriptOp LoadOperation(int offset)
        {
            Reader.BaseStream.Position = offset;
            var opCodeIndex = Reader.ReadUInt16();
            ScriptOp operation;
            ScriptAnimTree atr_ref = null;
            if (opCodeIndex == 0)
                return null;

            if ((IsPS4 || (opCodeIndex & 0x2000) == 0) && opCodeIndex < 0x4000)
            {
                ScriptOpCode[] Table = null;

                Table = IsPS4 ? SecondaryTable : PrimaryTable;

                if (Table.Length <= opCodeIndex) throw new ArgumentException($"Unknown Op Code (0x{opCodeIndex:X4})");

                var opCode = Table[opCodeIndex];

                if (opCode == ScriptOpCode.Invalid) throw new ArgumentException($"Unknown Op Code (0x{opCodeIndex:X4})");

                operation = new ScriptOp()
                {
                    Metadata = ScriptOpMetadata.OperationInfo[(int)opCode],
                    OpCodeOffset = (int)Reader.BaseStream.Position - 2,
                };
            }
            else
            {
                return null;
                //throw new ArgumentException("Invalid Op Code");
            }

            // Use a type rather than large switch for each operation
            // so we can easily fix bugs and adjust across multiple op codes
            // Like Black Ops 2 all are aligned to different values                 
            switch (operation.Metadata.OperandType)
            {
                case ScriptOperandType.None:
                    {
                        break;
                    }
                case ScriptOperandType.Int8:
                    {
                        operation.Operands.Add(new ScriptOpOperand(Reader.ReadSByte()));
                        break;
                    }
                case ScriptOperandType.UInt8:
                    {
                        if (operation.Metadata.OpCode == ScriptOpCode.WaitTill) break;
                        if (operation.Metadata.OpCode == ScriptOpCode.EndOn) break;
                        if (operation.Metadata.OpCode == ScriptOpCode.GetNegByte)
                        {
                            operation.Operands.Add(new ScriptOpOperand(Reader.ReadByte() * -1));
                        }
                        else
                        {
                            operation.Operands.Add(new ScriptOpOperand(Reader.ReadByte()));
                        }
                        break;
                    }
                case ScriptOperandType.Int16:
                    {
                        Reader.BaseStream.Position += Utility.ComputePadding((int)Reader.BaseStream.Position, 2);
                        operation.Operands.Add(new ScriptOpOperand(Reader.ReadInt16()));
                        break;
                    }
                case ScriptOperandType.UInt16:
                    {
                        Reader.BaseStream.Position += Utility.ComputePadding((int)Reader.BaseStream.Position, 2);
                        if (operation.Metadata.OpCode == ScriptOpCode.GetNegUnsignedShort)
                        {
                            operation.Operands.Add(new ScriptOpOperand(Reader.ReadUInt16() * -1));
                        }
                        else
                        {
                            operation.Operands.Add(new ScriptOpOperand(Reader.ReadUInt16()));
                        }
                        break;
                    }
                case ScriptOperandType.Int32:
                    {
                        Reader.BaseStream.Position += Utility.ComputePadding((int)Reader.BaseStream.Position, 4);
                        if(operation.Metadata.OpCode != ScriptOpCode.GetInteger || (atr_ref = ResolveTreeForGetint((int)Reader.BaseStream.Position)) == null)
                        {
                            operation.Operands.Add(new ScriptOpOperand(Reader.ReadInt32()));
                            break;
                        }
                        var skip = Reader.ReadInt32();
                        operation.Operands.Add(new ScriptOpOperand("$" + atr_ref.Namespace));
                        break;
                    }
                case ScriptOperandType.UInt32:
                    {
                        Reader.BaseStream.Position += Utility.ComputePadding((int)Reader.BaseStream.Position, 4);
                        operation.Operands.Add(new ScriptOpOperand(Reader.ReadUInt32()));
                        break;
                    }
                case ScriptOperandType.Hash:
                    {
                        Reader.BaseStream.Position += Utility.ComputePadding((int)Reader.BaseStream.Position, 4);
                        string val = "#\"" + GetHashValue(Reader.ReadUInt32(), "hash_") + "\"";
                        operation.Operands.Add(new ScriptOpOperand(val));
                        break;
                    }
                case ScriptOperandType.Float:
                    {
                        Reader.BaseStream.Position += Utility.ComputePadding((int)Reader.BaseStream.Position, 4);
                        operation.Operands.Add(new ScriptOpOperand(Reader.ReadSingle()));
                        break;
                    }
                case ScriptOperandType.Vector:
                    {
                        Reader.BaseStream.Position += Utility.ComputePadding((int)Reader.BaseStream.Position, 4);
                        operation.Operands.Add(new ScriptOpOperand(Reader.ReadSingle()));
                        break;
                    }
                case ScriptOperandType.VectorFlags:
                    {
                        var flags = Reader.ReadByte();

                        // Set each flag, it's either 1.0, -1.0, or simply 0.0
                        operation.Operands.Add(new ScriptOpOperand(
                            string.Format("({0}, {1}, {2})",
                            (flags & 0x20) != 0 ? 1.0f : (flags & 0x10) != 0 ? -1.0f : 0.0f,
                            (flags & 0x08) != 0 ? 1.0f : (flags & 0x04) != 0 ? -1.0f : 0.0f,
                            (flags & 0x02) != 0 ? 1.0f : (flags & 0x01) != 0 ? -1.0f : 0.0f)));
                        break;
                    }
                case ScriptOperandType.String:
                    {
                        // If it's anim animation, etc. we can just read at the location, but for strings
                        // we can just grab via pointer
                        switch (operation.Metadata.OpCode)
                        {
                            case ScriptOpCode.GetString:
                                Reader.BaseStream.Position += Utility.ComputePadding((int)Reader.BaseStream.Position, 4);
                                operation.Operands.Add(new ScriptOpOperand("\"" + GetString((int)Reader.BaseStream.Position)?.Value + "\""));
                                Reader.BaseStream.Position += 4;
                                break;
                            case ScriptOpCode.GetIString:
                                Reader.BaseStream.Position += Utility.ComputePadding((int)Reader.BaseStream.Position, 4);
                                operation.Operands.Add(new ScriptOpOperand("&\"" + GetString((int)Reader.BaseStream.Position)?.Value + "\""));
                                Reader.BaseStream.Position += 4;
                                break;
                            case ScriptOpCode.GetAnimation:
                                Reader.BaseStream.Position += Utility.ComputePadding((int)Reader.BaseStream.Position, 8);
                                int backPos = (int)Reader.BaseStream.Position;
                                if(AnimBackReferences.ContainsKey(backPos))
                                {
                                    operation.Operands.Add(new ScriptOpOperand($"%{AnimBackReferences[backPos].OwningTree.Namespace}::{AnimBackReferences[backPos].Name}"));
                                }
                                else
                                {
                                    operation.Operands.Add(new ScriptOpOperand($"%unknown_anim"));
                                }
                                Reader.BaseStream.Position += 8;
                                break;
                        }

                        break;
                    }
                case ScriptOperandType.VariableName:
                    {
                        Reader.BaseStream.Position += Utility.ComputePadding((int)Reader.BaseStream.Position, 4);

                        var name = GetHashValue(Reader.ReadUInt32(), "var_");
                        operation.Operands.Add(new ScriptOpOperand(name));
                        break;
                    }
                case ScriptOperandType.FunctionPointer:
                    {
                        Reader.BaseStream.Position += Utility.ComputePadding((int)Reader.BaseStream.Position, 8);
                        operation.Operands.Add(new ScriptOpOperand("&" + GetHashValue(Reader.ReadUInt32(), "function_")));
                        Reader.BaseStream.Position += 4;
                        break;
                    }
                case ScriptOperandType.Call:
                    {
                        if (operation.Metadata.OpCode == ScriptOpCode.ClassFunctionCall || operation.Metadata.OpCode == ScriptOpCode.ClassFunctionThreadCall || operation.Metadata.OpCode == ScriptOpCode.ClassFunctionThreadCall2)
                        {
                            var paramterCount = Reader.ReadByte();
                            Reader.BaseStream.Position += Utility.ComputePadding((int)Reader.BaseStream.Position, 4);
                            operation.Operands.Add(new ScriptOpOperand(GetHashValue(Reader.ReadUInt32(), "function_")));
                            operation.Operands.Add(new ScriptOpOperand(paramterCount));
                        }
                        else
                        {
                            // Skip param count, it isn't stored here until in memory
                            Reader.BaseStream.Position += 1;
                            Reader.BaseStream.Position += Utility.ComputePadding((int)Reader.BaseStream.Position, 8);
                            operation.Operands.Add(new ScriptOpOperand(GetHashValue(Reader.ReadUInt32(), "function_")));
                            Reader.BaseStream.Position += 4;
                        }
                        break;
                    }
                case ScriptOperandType.VariableList:
                    {
                        var varCount = Reader.ReadByte();

                        for(int i = 0; i < varCount; i++)
                        {
                            Reader.BaseStream.Position += Utility.ComputePadding((int)Reader.BaseStream.Position, 4);
                            operation.Operands.Add(new ScriptOpOperand(GetHashValue(Reader.ReadUInt32(), "var_")));
                            Reader.BaseStream.Position += 1;
                        }

                        break;
                    }
                case ScriptOperandType.SwitchEnd:
                    {
                        var switches = LoadEndSwitch();

                        foreach (var switchBlock in switches)
                        {
                            operation.Operands.Add(new ScriptOpOperand(switchBlock));
                        }
                        break;
                    }
                case ScriptOperandType.GlobalFieldVariable:
                    {
                        operation.Operands.Add(new ScriptOpOperand(Reader.ReadByte()));
                        Reader.BaseStream.Position += 1;
                        Reader.BaseStream.Position += Utility.ComputePadding((int)Reader.BaseStream.Position, 4);
                        operation.Operands.Add(new ScriptOpOperand(GetHashValue(Reader.ReadUInt32(), "var_")));
                        break;
                    }
                default:
                    {
                        throw new ArgumentException("Invalid Op Type", "OpType");
                    }
            }

            // Ensure we're at the next op, all operations are aligned to 2 bytes
            Reader.BaseStream.Position += Utility.ComputePadding((int)Reader.BaseStream.Position, 2);

            operation.OpCodeSize = (int)Reader.BaseStream.Position - offset;

            return operation;
        }

        public override int GetJumpLocation(int from, int to)
        {
            // We must align it to 2 bytes, for some reason they store
            // misaligned values...
            return from + (short)Utility.AlignValue((ushort)to, 2);
        }

        public override List<ScriptOpSwitch> LoadEndSwitch()
        {
            List<ScriptOpSwitch> switches = new List<ScriptOpSwitch>();
            Reader.BaseStream.Position += Utility.ComputePadding((int)Reader.BaseStream.Position, 4);
            var switchCount = Reader.ReadInt32();

            for (int i = 0; i < switchCount; i++)
            {
                var scriptString = GetString((int)Reader.BaseStream.Position);
                string switchString;

                // For Bo3 it seems the only way to check if it's a string
                // is to check for a reference in the string section...
                if (scriptString != null)
                {
                    Reader.BaseStream.Position += 4;
                    switchString = "\"" + scriptString.Value + "\"";
                }
                else
                {
                    // Check if 0 and at end, seems best way to check for 
                    // default since the compiler sorts them and so if we 
                    // had 0 it would be at the start
                    var switchValue = Reader.ReadInt32();

                    if (switchValue == 0 && i == switchCount - 1)
                    {
                        switchString = "default";
                    }
                    else
                    {
                        switchString = switchValue.ToString();
                    }
                }

                switches.Add(new ScriptOpSwitch()
                {
                    CaseValue = switchString,
                    ByteCodeOffset = (int)Reader.BaseStream.Position + Reader.ReadInt32() + 4,
                    OriginalIndex = i
                });
            }


            return switches.OrderBy(x => x.ByteCodeOffset).ToList();
        }

        public override void LoadAnimTrees()
        {
            Reader.BaseStream.Position = Header.AnimTreeTableOffset;
            for (int i = 0; i < Header.AnimTreeCount; i++)
            {
                var atr = new ScriptAnimTree();
                var pos = Reader.BaseStream.Position;
                atr.lpNamespace = Reader.ReadInt32();
                atr.Namespace = Reader.PeekNullTerminatedString(atr.lpNamespace);
                atr.NumATRRefs = Reader.ReadInt16(); // unk padding
                atr.Count = Reader.ReadInt16();

                for(int j = 0; j < atr.NumATRRefs; j++)
                {
                    var fixupPos = Reader.ReadInt32();
                    atr.UseAnimTreeEntries.Add(fixupPos);
                }

                for(int j = 0; j < atr.Count; j++)
                {
                    var aref = new ScriptAnim();
                    aref.OwningTree = atr;
                    aref.lpAnimName = Reader.ReadInt32();
                    Reader.ReadInt32();
                    aref.Name = Reader.PeekNullTerminatedString(aref.lpAnimName);
                    var aref_pos = Reader.ReadInt32();
                    atr.AnimationReferences[aref_pos] = aref;
                    AnimBackReferences[aref_pos] = aref;
                    Reader.ReadInt32();
                }
                AnimTrees[(int)pos] = atr;
            }
        }
    }
}
