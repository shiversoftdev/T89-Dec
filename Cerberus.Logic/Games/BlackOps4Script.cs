using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.IO;
using PhilLibX;
using PhilLibX.IO;

namespace Cerberus.Logic
{
    public partial class BlackOps4Script : ScriptBase
    {
        /// <summary>
        /// Returns the Game Name for Black Ops 4
        /// </summary>
        public override string Game => "Black Ops 4";

        public BlackOps4Script(Stream stream, Dictionary<uint, string> hashTable, Dictionary<ulong, string> qword_hashTable) : base(stream, hashTable, qword_hashTable) { }
        public BlackOps4Script(BinaryReader reader, Dictionary<uint, string> hashTable, Dictionary<ulong, string> qword_hashTable) : base(reader, hashTable, qword_hashTable) { }

        public override void LoadHeader()
        {
            LoadTable();
            // Ensure we're at the header (skip 8 byte magic)
            Reader.BaseStream.Position = 8;

            Header = new ScriptHeader();
            #region legacy
            /*
            {
                SourceChecksum         = (uint)Reader.ReadUInt64(), //0x8
                IncludeTableOffset     = Reader.ReadInt32(), //0xC -- checked
                AnimTreeTableOffset    = Reader.ReadInt32(), //0x10 -- checked
                ByteCodeOffset         = Reader.ReadInt32(), //0x14
                StringTableOffset      = Reader.ReadInt32(), //0x18 -- checked
                DebugStringTableOffset = Reader.ReadInt32(), //0x1c -- checked
                ExportTableOffset      = Reader.ReadInt32(), //0x20 -- checked
                ImportTableOffset      = Reader.ReadInt32(), //0x24
                FixupTableOffset       = Reader.ReadInt32(), //0x28
                ProfileTableOffset     = Reader.ReadInt32(), //0x2c
                ByteCodeSize           = Reader.ReadInt32(), //0x30
                NameOffset             = Reader.ReadInt32(), //0x34
                StringCount            = Reader.ReadInt16(), //0x38
                ExportsCount           = Reader.ReadInt16(), //0x3A
                ImportsCount           = Reader.ReadInt16(), //0x3C
                FixupCount             = Reader.ReadInt16(), //0x3E
                ProfileCount           = Reader.ReadInt16(), //0x40
                DebugStringCount       = Reader.ReadInt16(), //0x42 -- checked
                IncludeCount           = Reader.ReadByte(), //0x44
                AnimTreeCount          = Reader.ReadByte(), //0x45
                Flags                  = Reader.ReadByte() //0x46
            };
            
            // Get name of this script from the header
            Reader.BaseStream.Position = Header.NameOffset;
            FilePath = Reader.ReadNullTerminatedString();
            */
            #endregion
            Header.SourceChecksum = (uint)Reader.ReadUInt64(); //0x8
            FilePath = GetHashValue(Reader.ReadUInt64(), "script_"); //0x10
            Header.IncludeTableOffset = Reader.ReadInt32(); //0x18
            Header.StringCount = Reader.ReadInt16(); //0x1C
            Header.ExportsCount = Reader.ReadInt16(); //0x1E
            Header.DebugStringTableOffset = Reader.ReadInt32(); //0x20 //unconfirmed
            Header.StringTableOffset = Reader.ReadInt32(); //0x24
            Header.ImportsCount = Reader.ReadInt16(); //0x28
            Header.FixupCount = Reader.ReadInt16(); //0x2A
            Reader.ReadInt32(); //0x2C //UNK_2C
            Header.ExportTableOffset = Reader.ReadInt32(); //0x30
            Reader.ReadInt32(); //unk 34
            Header.ImportTableOffset = Reader.ReadInt32(); //0x38
            Reader.ReadInt32(); //0x3C
            Header.FixupTableOffset = Reader.ReadInt32(); //0x40
            Reader.ReadInt32(); //0x44 -- unk fixups (events)
            Reader.ReadUInt64(); //0x48 -- unks
            Reader.ReadUInt64(); //0x50 -- unks
            Header.IncludeCount = Reader.ReadInt16();

            // Skip padding (header is 72 bytes in total)
            Reader.BaseStream.Position = 0x60;
        }

        public override void LoadStrings()
        {
            Reader.BaseStream.Position = Header.StringTableOffset;

            Strings = new List<ScriptString>(Header.StringCount);

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

                if (Reader.ReadByte() == 0x9F)
                    Reader.ReadByte();
                else
                    Reader.BaseStream.Position--;

                scriptString.Value = Reader.ReadNullTerminatedString();

                // Go back to the table
                Reader.BaseStream.Position = offset;

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
                    Namespace2     = GetHashValue(Reader.ReadUInt32(), "namespace_"),
                    ParameterCount = Reader.ReadByte(),
                    Flags          = (ScriptExportFlags)Reader.ReadByte()
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
                Reader.BaseStream.Position += 1;

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
                Includes.Add(new ScriptInclude(GetHashValue(Reader.ReadUInt64(), "script_")));
            }

            Includes.Sort();
        }

        public override ScriptOp LoadOperation(int offset)
        {
            Reader.BaseStream.Position = offset;
            var opCodeIndex = Reader.ReadUInt16();
            ScriptOp operation;

            if (opCodeIndex == 0)
                return null;

            if((opCodeIndex & 0xFFFFEFFF) < 0x1000 )
            {
                ScriptOpCode[] Table = null;

                if ((opCodeIndex & 0x1000) > 0)
                    Table = SecondaryTable;
                else
                    Table = PrimaryTable;

                if(Table.Length <= (opCodeIndex & 0xEFFF))
                    throw new ArgumentException($"Unknown Op Code (0x{opCodeIndex:X4})");

                var opCode = Table[opCodeIndex & 0xEFFF];

                if (opCode == ScriptOpCode.Invalid)
                {
                    throw new ArgumentException($"Unknown Op Code (0x{opCodeIndex:X4})");
                }

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
                        operation.Operands.Add(new ScriptOpOperand(Reader.ReadInt32()));
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
                        Reader.BaseStream.Position += Utility.ComputePadding((int)Reader.BaseStream.Position, 8);
                        string val = "\"" + GetHashValue(Reader.ReadUInt64(), "hash_") + "\"";
                        if (!val.StartsWith("\"hash_"))
                            val = "#" + val;
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
                                operation.Operands.Add(new ScriptOpOperand("%" + Reader.PeekNullTerminatedString(Reader.ReadInt32())));
                                Reader.BaseStream.Position += 4;
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
                        if (operation.Metadata.OpCode == ScriptOpCode.ClassFunctionCall)
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

            Reader.BaseStream.Position += Utility.ComputePadding((int)Reader.BaseStream.Position, 8);
            for (int i = 0; i < switchCount; i++)
            {
                var scriptString = GetString((int)Reader.BaseStream.Position);
                string switchString;

                // For Bo3 it seems the only way to check if it's a string
                // is to check for a reference in the string section...
                if (scriptString != null)
                {
                    Reader.BaseStream.Position += 8;
                    switchString = "\"" + scriptString.Value + "\"";
                }
                else
                {
                    // Check if 0 and at end, seems best way to check for 
                    // default since the compiler sorts them and so if we 
                    // had 0 it would be at the start
                    var switchValue = Reader.ReadInt64();

                    if(switchValue == 0 && i == switchCount - 1)
                    {
                        switchString = "default";
                    }
                    else
                    {
                        if (switchValue > 0xFFFFFFFF) //its a hash most likely
                            switchString = $"\"{GetHashValue((ulong)switchValue, "hash_")}\"";
                        else
                            switchString = switchValue.ToString();
                    }
                }

                switches.Add(new ScriptOpSwitch()
                {
                    CaseValue = switchString,
                    ByteCodeOffset = (int)((int)Reader.BaseStream.Position + Reader.ReadInt64() + 8),
                    OriginalIndex = i
                });
            }

           
            return switches.OrderBy(x => x.ByteCodeOffset).ToList();
        }

        public override void LoadAnimTrees()
        {
            AnimTrees = new List<ScriptAnimTree>(Header.AnimTreeCount);
        }
    }
}
