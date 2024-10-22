﻿// ------------------------------------------------------------------------
// Cerberus - A Call of Duty: Black Ops II/III GSC/CSC Decompiler
// Copyright (C) 2018 Philip/Scobalula
//
// This program is free software: you can redistribute it and/or modify
// it under the terms of the GNU General private License as published by
// the Free Software Foundation, either version 3 of the License, or
// (at your option) any later version.

// This program is distributed in the hope that it will be useful,
// but WITHOUT ANY WARRANTY; without even the implied warranty of
// MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
// GNU General private License for more details.

// You should have received a copy of the GNU General private License
// along with this program.  If not, see <http://www.gnu.org/licenses/>.
// ------------------------------------------------------------------------
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.CodeDom.Compiler;
using System.Diagnostics;
using System.Collections.Specialized;

namespace Cerberus.Logic
{
    // TODO: t7 character::save (in codescripts)
    /// <summary>
    /// Handles Decompiling GSC files
    /// </summary>
    internal class Decompiler : IDisposable
    {
        private const bool DisableVerbose = true;
        private static bool UseTernaryLogging = false, UseWhileLoopDetectionLogging = false, UseElseDetectionLogging = true,
            UseJumpDetectionLogging = true, UseForLoopVerbose = false, UseIfStatementLogging = true;
        /// <summary>
        /// Operators by Op Code
        /// </summary>
        static readonly Dictionary<ScriptOpCode, string> Operators = new Dictionary<ScriptOpCode, string>()
        {
            { ScriptOpCode.Plus,                          " + " },
            { ScriptOpCode.StringConcat,                  " + " },
            { ScriptOpCode.Minus,                         " - " },
            { ScriptOpCode.Multiply,                      " * " },
            { ScriptOpCode.Divide,                        " / " },
            { ScriptOpCode.Modulus,                       " % " },
            { ScriptOpCode.ShiftLeft,                     " << " },
            { ScriptOpCode.ShiftRight,                    " >> " },
            { ScriptOpCode.Bit_Or,                        " | " },
            { ScriptOpCode.Bit_Xor,                       " ^ " },
            { ScriptOpCode.Bit_And,                       " & " },
            { ScriptOpCode.Equal,                         " == " },
            { ScriptOpCode.NotEqual,                      " != " },
            { ScriptOpCode.LessThan,                      " < " },
            { ScriptOpCode.GreaterThan,                   " > " },
            { ScriptOpCode.LessThanOrEqualTo,             " <= " },
            { ScriptOpCode.GreaterThanOrEqualTo,          " >= " },
            { ScriptOpCode.SuperEqual,                    " === " },
            { ScriptOpCode.SuperNotEqual,                 " !== " },
        };

        /// <summary>
        /// Instructions that have a function to match
        /// </summary>
        static readonly Dictionary<ScriptOpCode, Tuple<string, int>> InstructionFunctions = new Dictionary<ScriptOpCode, Tuple<string, int>>()
        {
            // Op Code                                                             "Source Name"            "Parameter Count"
            { ScriptOpCode.RealWait,                        new Tuple<string, int>("realwait",              1) },
            { ScriptOpCode.Wait,                            new Tuple<string, int>("wait",                  1) },
            { ScriptOpCode.WaitRealTime,                    new Tuple<string, int>("waitrealtime",          1) },
            { ScriptOpCode.GetTime,                         new Tuple<string, int>("gettime",               0) },
            { ScriptOpCode.Abs,                             new Tuple<string, int>("abs",                   1) },
            { ScriptOpCode.FirstArrayKey,                   new Tuple<string, int>("getfirstarraykey",      1) },
            { ScriptOpCode.FirstArrayKeyCached,             new Tuple<string, int>("getfirstarraykeycached",1) },
            { ScriptOpCode.NextArrayKey,                    new Tuple<string, int>("getnextarraykey",       1) },
            { ScriptOpCode.GetArrayKeyIndex,                new Tuple<string, int>("getarraykeyindex",      1) },
            { ScriptOpCode.GetArrayValue,                   new Tuple<string, int>("getarrayvalue",         1) },
            { ScriptOpCode.AnglesToUp,                      new Tuple<string, int>("anglestoup",            1) },
            { ScriptOpCode.AnglesToRight,                   new Tuple<string, int>("anglestoright",         1) },
            { ScriptOpCode.AnglesToForward,                 new Tuple<string, int>("anglestoforward",       1) },
            { ScriptOpCode.AngleClamp180,                   new Tuple<string, int>("angleclamp180",         1) },
            { ScriptOpCode.VectorToAngles,                  new Tuple<string, int>("vectortoangles",        1) },
            { ScriptOpCode.VectorScale,                     new Tuple<string, int>("vectorscale",           2) },
            { ScriptOpCode.IsDefined,                       new Tuple<string, int>("isdefined",             1) },
            { ScriptOpCode.GetDvar,                         new Tuple<string, int>("getdvar",               1) },
            { ScriptOpCode.GetDvarInt,                      new Tuple<string, int>("getdvarint",            1) },
            { ScriptOpCode.GetDvarFloat,                    new Tuple<string, int>("getdvarfloat",          1) },
            { ScriptOpCode.GetDvarVector,                   new Tuple<string, int>("getdvarvector",         1) },
            { ScriptOpCode.GetDvarColorRed,                 new Tuple<string, int>("getdvarcolorred",       1) },
            { ScriptOpCode.GetDvarColorGreen,               new Tuple<string, int>("getdvarcolorgreen",     1) },
            { ScriptOpCode.GetDvarColorBlue,                new Tuple<string, int>("getdvarcolorblue",      1) },
            { ScriptOpCode.GetDvarColorAlpha,               new Tuple<string, int>("getdvarcoloralpha",     1) },
            { ScriptOpCode.WaitFrame,                       new Tuple<string, int>("waitframe",             1) },
            { ScriptOpCode.PixBeginEvent,                   new Tuple<string, int>("pixbeginevent",         0) },
            { ScriptOpCode.PixEndEvent,                     new Tuple<string, int>("pixendevent",           0) },
            { ScriptOpCode.EndOn,                           new Tuple<string, int>("endon",                 0) },
            { ScriptOpCode.EndOnCallback,                   new Tuple<string, int>("endoncallback",         0) },
            { ScriptOpCode.EndonCallbackA,                  new Tuple<string, int>("endoncallback",         0) },
            { ScriptOpCode.WaittillTimeoutS,                new Tuple<string, int>("waittilltimeout",       0) },
        };

        /// <summary>
        /// Gets or Sets the Function we're decompiling
        /// </summary>
        private ScriptExport Function { get; set; }

        /// <summary>
        /// Gets or Sets the Script the function belongs to
        /// </summary>
        private ScriptBase Script { get; set; }

        private HashSet<int> JumpLocations = new HashSet<int>();

        private bool IsBoolReturn = false;

        /// <summary>
        /// List of decompiler blocks
        /// </summary>
        private List<DecompilerBlock> Blocks = new List<DecompilerBlock>();

        /// <summary>
        /// List of local variable names
        /// </summary>
        private readonly List<string> LocalVariables = new List<string>();

        /// <summary>
        /// The virtual script stack
        /// </summary>
        private readonly Stack<string> Stack = new Stack<string>();

        /// <summary>
        /// Current Variable Reference
        /// </summary>
        private string CurrentReference = "";

        // List of default argument values
        private Dictionary<int, string> DefaultArguments = new Dictionary<int, string>();

        /// <summary>
        /// Internal Text Writer
        /// </summary>
        private StringWriter InternalWriter { get; set; }

        /// <summary>
        /// Indent Writer
        /// </summary>
        private IndentedTextWriter Writer { get; set; }

        private ScriptOp SCL = null;

        /// <summary>
        /// Initializes an instance of the Decompiler Class
        /// </summary>
        /// <param name="function"></param>
        /// <param name="script"></param>
        public Decompiler(ScriptExport function, ScriptBase script, int tabs)
        {
            try
            {
                Function = function;
                Script = script;
                SCL = null;

                var firstCode = Function.Operations[0];
                int fcindex = 0;

                while(firstCode.Metadata.OpCode == ScriptOpCode.Nop)
                {
                    firstCode.Visited = true;
                    firstCode = Function.Operations[++fcindex];
                }

                if (firstCode.Metadata.OpCode == ScriptOpCode.SafeCreateLocalVariables)
                {
                    SCL = firstCode;
                    foreach (var var in firstCode.Operands)
                    {
                        LocalVariables.Add((string)var.Value);
                    }

                    firstCode.Visited = true;
                }

                // Preprocess some operations
                foreach (var operation in Function.Operations)
                {
                    // Check for invalid operations
                    if (operation.Metadata.OpCode == ScriptOpCode.Invalid)
                    {
                        throw new Exception("Function contains invalid operation code");
                    }
                }

                if(Function.Operations.Count < 1)
                {
                    InternalWriter = new StringWriter();
                    Writer = new IndentedTextWriter(InternalWriter, "\t");
                    Writer.Indent = tabs;
                    Writer.WriteLine(BuildFunctionDefinition());
                    Writer.WriteLine("{");
                    Writer.Indent++;
                    Writer.WriteLine($"// Unsupported VM revision ({Script.Header.VMRevision:X}).");
                    Writer.Indent--;
                    Writer.WriteLine("}");
                    return;
                }

                // Remove end instruction if it's end,
                if (Function.Operations[Function.Operations.Count - 1].Metadata.OpCode == ScriptOpCode.End)
                {
                    Function.Operations[Function.Operations.Count - 1].Visited = true;
                }

                // Add the root of this function, the main block of execution
                Blocks.Add(new BasicBlock(function.ByteCodeOffset, function.ByteCodeOffset + function.ByteCodeSize + 1));

                // This performs several passes over the operations
                // we detect the most basic and easy to find first
                // then we move onto transforming them into their
                // respective parents

                // We need to find jump blocks before 
                // we find for loops as we can have a case
                // where we have modifiers within an if/else
                // and therefore it's NOT a for loop
                // but we need to resolve for loops before them
                // so this is the best way to ensure that
                DetectBoolReturn();
                FindSwitchCase();
                FindTernaries(0, Function.Operations.Count);
                FindWhileLoops();
                FindDoWhileLoops();
                FindIfStatements();
                FindJumpBlocks();
                FindForEachLoops();
                FindForLoops(); // probably a bug here, where a ternary assignment before a for loop will incorrectly decompile
                // Now that we've done what need to do we can remove jump blocks
                // to process them properly
                Blocks.RemoveAll(x => x is BasicBlock && x.StartOffset != Function.ByteCodeOffset);
                FindElseIfStatements();
                FindDevBlocks();
                ResolveParentBlocks();
                Stack.Clear();
                RestoreCaseOrder();
                MarkUnkJumps();
                DetectDefaultArgs();

                InternalWriter = new StringWriter();
                Writer = new IndentedTextWriter(InternalWriter, "\t");
                Writer.Indent = tabs;

                Writer.WriteLine(BuildFunctionDefinition());
                DecompileBlock(Blocks[0], tabs + 1);
            }
            catch(Exception e)
            {
                string s = GetWriterOutput();
                InternalWriter?.Dispose();
                InternalWriter = new StringWriter();
                Writer?.Dispose();
                Writer = new IndentedTextWriter(InternalWriter, "\t");
                Writer?.WriteLine("function {0}()", Function.Name);
                Writer?.WriteLine("{");
                Writer?.WriteLine(e.ToString().Trim());
                Writer?.WriteLine("/*\r\n{0}\r\n*/", s);
                DumpFunctionInfo();
                Writer?.WriteLine("}");
            }

            Writer?.Flush();
            ClearVisitedInstructions();
        }

        private void DetectBoolReturn()
        {
            for(int i = 0; i < Function.Operations.Count; i++)
            {
                if (Function.Operations[i].Metadata.OpCode != ScriptOpCode.Return)
                {
                    continue;
                }
                if(i == 0)
                {
                    return;
                }
                if(Function.Operations[i - 1].Metadata.OpCode != ScriptOpCode.GetByte && Function.Operations[i - 1].Metadata.OpCode != ScriptOpCode.GetZero)
                {
                    return;
                }
                if(Function.Operations[i - 1].Metadata.OpCode == ScriptOpCode.GetByte && (((int)Function.Operations[i - 1].Operands[0].Value & 0x7FE) > 0))
                {
                    return;
                }
            }
            IsBoolReturn = true;
        }

        private void DumpFunctionInfo()
        {
#if DEBUG
            Writer?.WriteLine("\r\n\t/* ======== */\r\n");

            // Dump stack
            Writer?.WriteLine("/* \r\n\tStack: ");
            foreach (var v in Stack)
                Writer?.WriteLine("\tStack Entry: " + v);

            Writer?.WriteLine("*/\r\n\t/* ======== */\r\n");
            
            // Dump Blocks
            Writer?.WriteLine("/* \r\n\tBlocks: ");
            foreach (var block in Blocks)
            {
                Writer?.WriteLine($"\t{block.GetType()} at 0x{block.StartOffset:X4}, end at 0x{block.EndOffset:X4}");
            }
            Writer?.WriteLine("*/\r\n\t/* ======== */\r\n");
#endif
        }

        public string GetWriterOutput()
        {
            return InternalWriter?.ToString() ?? "No Output";
        }

        private bool IsPrivate(byte flags)
        {
            if (Script.Header.VMRevision <= 0x37) return ((v1cScriptExportFlags)flags).HasFlag(v1cScriptExportFlags.Private);
            return ((v38ExportFlags)flags).HasFlag(v38ExportFlags.Private);
        }

        private bool IsAutoExec(byte flags)
        {
            if (Script.Header.VMRevision <= 0x37) return ((v1cScriptExportFlags)flags).HasFlag(v1cScriptExportFlags.AutoExec);
            return ((v38ExportFlags)flags).HasFlag(v38ExportFlags.AutoExec);
        }

        private bool IsEvent(byte flags)
        {
            if (Script.Header.VMRevision <= 0x37) return ((v1cScriptExportFlags)flags).HasFlag(v1cScriptExportFlags.Event);
            return ((v38ExportFlags)flags).HasFlag(v38ExportFlags.Event);
        }

        /// <summary>
        /// Builds the function definition
        /// </summary>
        public string BuildFunctionDefinition(bool NoDefaults = false)
        {
            var result = "";

            if(Function.IsLocal)
            {
                result = "function(";
                HashSet<int> locals = new HashSet<int>();
                foreach (var op in Function.Operations)
                {
                    if (op.Metadata.OpCode == ScriptOpCode.SetLocalVariableCached)
                    {
                        locals.Add((int)op.Operands[0].Value);
                    }
                    else if (op.Metadata.OpCode == ScriptOpCode.SetWaittillVariableFieldCached)
                    {
                        locals.Add((int)op.Operands[0].Value);
                    }
                }
                Function.ParameterCount = LocalVariables.Count - locals.Count;
            }
            else
            {
                result += IsEvent(Function.Flags) ? "event " : ((Function.Name == "constructor" || Function.Name == "destructor") ? "" : "function ");

                if (IsPrivate(Function.Flags))
                {
                    result += "private ";
                }
                if (IsAutoExec(Function.Flags))
                {
                    result += "autoexec ";
                }

                result += Function.Name + "(";
            }

            for (int i = 0; i < Function.ParameterCount && i < LocalVariables.Count; i++)
            {
                var stackValue = (DefaultArguments.ContainsKey(i) && !NoDefaults) ? DefaultArguments[i] : LocalVariables[i];

                if(SCL != null)
                {
                    Debug.Assert(SCL.Operands.Count > i);
                    if(SCL.Operands[i].IsByRef)
                    {
                        stackValue = "&" + stackValue;
                    }
                    else if(SCL.Operands[i].IsVarArg)
                    {
                        stackValue = "...";
                    }
                }
                result += string.Format("{0}{1}", stackValue, (i != Function.ParameterCount - 1) ? ", " : "");
                
            }

            if(Function.IsLocal)
            {
                return result + ") => ";
            }

            return result + ")";
        }

        /// <summary>
        /// Clears Visited Instructions
        /// </summary>
        private void ClearVisitedInstructions()
        {
            Function.Operations.ForEach(x => x.Visited = false);
        }

        /// <summary>
        /// Restores the original order of the case block
        /// 
        /// For child-block resolving purposes we sort the blocks
        /// based off their offsets, etc. but then the order of their
        /// execution is wrong
        /// 
        /// To fix this we store the original index and then restore it
        /// here
        /// </summary>
        private void RestoreCaseOrder()
        {
            foreach(var block in Blocks)
            {
                if(block is SwitchBlock caseBlock)
                {
                    for(int i = 0; i < block.ChildBlockIndices.Count; i++)
                    {
                        if(!(Blocks[block.ChildBlockIndices[i]] is CaseBlock)) // TODO: how the fuck did this get in here and why is it in here????!
                        {
                            block.ChildBlockIndices.RemoveAt(i);
                            i--;
                        }
                    }
                    block.ChildBlockIndices = block.ChildBlockIndices.OrderBy(x => (Blocks[x] as CaseBlock)?.OriginalIndex ?? (Blocks.Count + x)).ToList();
                }
            }
        }

        private void FindJumpBlocks()
        {
            foreach(var instruction in Function.Operations)
            {
                if (instruction.Metadata.OpCode != ScriptOpCode.Jump || instruction.Visited)
                    continue;

                // Check positive jump
                if ((int)instruction.Operands[0].Value <= 0) continue;

                VerboseCondition($"[{Function.Name}] Marking a jump at 0x{instruction.OpCodeOffset:X4}", UseJumpDetectionLogging);
                // Add it as a basic block
                Blocks.Add(new BasicBlock(
                    instruction.OpCodeOffset + instruction.OpCodeSize,
                    Script.GetJumpLocation(
                        instruction.OpCodeOffset + instruction.OpCodeSize,
                        (int)instruction.Operands[0].Value)));
                
            }
        }

        private TernaryBlock FindTernaryAt(int offset)
        {
            var results = Blocks.Where((x) => x.StartOffset == offset);
            foreach (var block in results)
                if (block is TernaryBlock t)
                {
                    while (t.ParentBlock != null) t = t.ParentBlock;
                    return t;
                }
            return null;
        }

        private void FindTernaries(int startIndex, int endIndex)
        {
            //List<TernaryBlock> collected = new List<TernaryBlock>();
            for (int i = startIndex; i < endIndex; i++)
            {
                var op = Function.Operations[i];

                if (op.Metadata.OpType != ScriptOpType.JumpCondition) continue;
                if (op.Visited || (int)op.Operands[0].Value < 0) continue;

                // is ternary logic
                var jloc = Script.GetJumpLocation(op.OpCodeOffset + op.OpCodeSize, (int)op.Operands[0].Value);
                var jindex = GetInstructionAt(jloc) - 1;

                if (jindex >= Function.Operations.Count || jindex < 0)
                    continue;

                var jmp = Function.Operations[jindex];

                if (jmp.Metadata.OpCode != ScriptOpCode.Jump) //all ternaries have a conditional skip
                    continue;

                if (Function.Operations[jindex - 1].Metadata.OpType == ScriptOpType.JumpCondition)
                    continue; //empty stack

                FindTernaries(i + 1, jindex);
                int jcstartindex = FindStartIndexEx(jindex - 1);
                if (jcstartindex - 1 != i) // full stack between jump condition and jmp ?
                    continue;

                var jeloc = Script.GetJumpLocation(jmp.OpCodeOffset + jmp.OpCodeSize, (int)jmp.Operands[0].Value);
                int jePrevIndex = GetInstructionAt(jeloc) - 1;

                if (Function.Operations[jePrevIndex].Metadata.OpType == ScriptOpType.Jump)
                    continue; //empty stack

                FindTernaries(jindex + 1, jePrevIndex + 1);
                int jeStartIndex = FindStartIndexEx(jePrevIndex);
                if (jeStartIndex - 1 != jindex) // full stack between jmp and jump end
                    continue;

                bool IsTwoOperandExpression = op.Metadata.OpCode == ScriptOpCode.JumpOnGreaterThan || op.Metadata.OpCode == ScriptOpCode.JumpOnLessThan;
                var _startIndex = FindStartIndexTernary(i - 1, jeloc, IsTwoOperandExpression);
                TernaryBlock parent;
                List<TernaryBlock> children = new List<TernaryBlock>();

                VerboseCondition($"[{Function.Name}]: Ternary Block -- {_startIndex:X4}, {i:X4}, {jindex:X4}, {(jePrevIndex + 1):X4}", UseTernaryLogging);
                Blocks.Add(parent = new TernaryBlock(Function.Operations[_startIndex].OpCodeOffset, jeloc)
                {
                    Comparison = BuildStackEmission(_startIndex, i, children, op.Metadata.OpCode),
                    TrueCondition = BuildStackEmission(i + 1, jindex, children),
                    FalseCondition = BuildStackEmission(jindex + 1, jePrevIndex + 1, children),
                });

                if (Function.Operations[_startIndex].Metadata.OpCode == ScriptOpCode.EvalLocalVariablesCached && !IsTwoOperandExpression)
                {
                    parent.PushVal = GetVariableNames(Function.Operations[_startIndex]).Item1;
                }

                foreach (var child in children)
                    child.ParentBlock = parent;

                jmp.Visited = true;
                jmp.IsTernary = true;
                op.IsTernary = true;
                op.Visited = true;
            }
        }

        private void FindIfStatements()
        {
            for (int i = 0; i < Function.Operations.Count; i++)
            {
                var op = Function.Operations[i];

                if (op.Metadata.OpType != ScriptOpType.JumpCondition) continue;
                if (op.Visited || op.IsTernary || (int)op.Operands[0].Value <= 0) continue;

                op.Visited = true;

                var startIndex = FindStartIndexEx(i - 1);
                VerboseCondition($"[{Function.Name}] Determined an if statement starts at {Function.Operations[startIndex].OpCodeOffset:X4}", UseIfStatementLogging);
                Blocks.Add(new IfBlock(Function.Operations[startIndex].OpCodeOffset, Script.GetJumpLocation(op.OpCodeOffset + op.OpCodeSize, (int)op.Operands[0].Value))
                {
                    Comparison = BuildCondition(startIndex)
                });
                
            }
        }

        private void DetectDefaultArgs()
        {
            if (Script.Header.VMRevision == 0x1C)
            {
                detect_args_internal_1c();
                return;
            }
            if (Script.Header.VMRevision == 0x36)
            {
                detect_args_internal_36();
                return;
            }
            // TODO ADJUST FOR T9
        }

        private void detect_args_internal_36()
        {
            for (int i = 1; i < Blocks.Count; i++)
            {
                if (!(Blocks[i] is IfBlock argBlock)) return;
                int startIndex = GetInstructionAt(argBlock.StartOffset);
                if (Function.Operations[startIndex].Metadata.OpCode != ScriptOpCode.EvalLocalVariableDefined || Function.Operations[startIndex + 1].Metadata.OpCode != ScriptOpCode.JumpOnTrue) return;
                if (i == 1 && startIndex != 1) return; // prevents blocks from having prefix code. index must be 1 (after safecreate) for default params
                string vname = GetVariableName(Function.Operations[startIndex], true);
                if (!LocalVariables.Contains(vname)) return;
                int vIndex = LocalVariables.IndexOf(vname);
                var jmp = Function.Operations[startIndex + 1];
                var jloc = Script.GetJumpLocation(jmp.OpCodeOffset + jmp.OpCodeSize, (int)jmp.Operands[0].Value);
                int endIndex = GetInstructionAt(jloc);
                if (Function.Operations[endIndex - 1].Metadata.OpCode != ScriptOpCode.SetLocalVariableCached) return;
                var startTestIndex = FindStartIndexEx(endIndex - 2) - 1;
                if (startTestIndex != startIndex + 1) return; // we should only have stackops between the assignment and the condition.
                BuildCondition(startIndex + 2, false, endIndex - 1);
                var stack = Stack.Pop();
                DefaultArguments[vIndex] = string.Format("{0} = {1}", vname, stack);
                argBlock.Visited = true;
                for (int j = startIndex; j < endIndex; j++)
                {
                    Function.Operations[j].Visited = true;
                }
            }
        }

        private void detect_args_internal_1c()
        {
            for (int i = 1; i < Blocks.Count; i++)
            {
                if (!(Blocks[i] is IfBlock argBlock)) return;
                int startIndex = GetInstructionAt(argBlock.StartOffset);
                if (Function.Operations[startIndex].Metadata.OpCode != ScriptOpCode.EvalLocalVariableCached) return;
                if (i == 1 && startIndex != 1) return; // prevents blocks from having prefix code. index must be 1 (after safecreate) for default params
                string vname = GetVariableName(Function.Operations[startIndex]);
                if (!LocalVariables.Contains(vname)) return;
                int vIndex = LocalVariables.IndexOf(vname);
                if (Function.Operations[startIndex + 1].Metadata.OpCode != ScriptOpCode.IsDefined ||
                   Function.Operations[startIndex + 2].Metadata.OpCode != ScriptOpCode.JumpOnTrue
                    ) return;
                var jmp = Function.Operations[startIndex + 2];
                var jloc = Script.GetJumpLocation(jmp.OpCodeOffset + jmp.OpCodeSize, (int)jmp.Operands[0].Value);
                int endIndex = GetInstructionAt(jloc);
                if (Function.Operations[endIndex - 1].Metadata.OpCode != ScriptOpCode.SetVariableField) return;
                if (Function.Operations[endIndex - 2].Metadata.OpCode != ScriptOpCode.EvalLocalVariableRefCached) return;
                var startTestIndex = FindStartIndexEx(endIndex - 3) - 1;
                if (startTestIndex != startIndex + 2) return; // we should only have stackops between the assignment and the condition.
                BuildCondition(startIndex + 3, false, endIndex - 2);
                var stack = Stack.Pop();
                DefaultArguments[vIndex] = string.Format("{0} = {1}", vname, stack);
                argBlock.Visited = true;
                for (int j = startIndex; j < endIndex; j++)
                {
                    Function.Operations[j].Visited = true;
                }
            }
        }

        private string BuildStackEmission(int startIndex, int stopIndex, List<TernaryBlock> children, ScriptOpCode code = ScriptOpCode.Invalid)
        {
            VerboseCondition($"[{Function.Name}] Begin Stack Emission (range: {startIndex:X3} - {stopIndex:X3})", UseTernaryLogging);
            for (int i = startIndex; i < stopIndex; i++)
            {
                var op = Function.Operations[i];

                // Check if we hit a non-stack/expression operation
                if (!IsStackOperation(op))
                {
                    throw new Exception($"Found a non stack operation ({op.Metadata.OpCode}, 0x{op.OpCodeOffset:X4}) in BuildStackEmission when non stack operations should have been purged");
                }

                if (op.IsTernary)
                {
                    var t = FindTernaryAt(op.OpCodeOffset);

                    if (t == null)
                    {
                        throw new Exception($"Ternary start index OOB {op.OpCodeOffset:X4}");
                    } 

                    if (!t.Visited)
                    {
                        if (t.PushVal != null) Stack.Push(t.PushVal);
                        Stack.Push(t.GetHeader());
                        t.Visited = true;
                        children.Add(t);
                    }

                    i = GetInstructionAt(t.EndOffset) - 1;
                    continue;
                }

                object refOut = null;
                ProcessInstruction(op, ref refOut);
                op.Visited = true;
                op.IsTernary = true;
            }

            if (code == ScriptOpCode.JumpOnLessThan || code == ScriptOpCode.JumpOnGreaterThan)
            {
                string second = Stack.Pop();
                string first = Stack.Pop();
                return code == ScriptOpCode.JumpOnLessThan ? $"{first} > {second}" : $"{first} < {second}";
            }
                
            return Stack.Pop();
        }

        /// <summary>
        /// Checks if the instruction is inside a child block
        /// 
        /// This is used by the for loop finder to determine 
        /// if an index modifier, etc. is actually within another
        /// block
        /// </summary>
        private bool IsInsideChildBlock(int offset, DecompilerBlock block)
        {
            for(int i = Blocks.Count - 1; i >= 0; i--)
            {
                if (Blocks[i].StartOffset > block.StartOffset && 
                    Blocks[i].EndOffset <= block.EndOffset)
                {
                    if (offset >= Blocks[i].StartOffset && offset < Blocks[i].EndOffset)
                    {
                        return true;
                    }
                }
            }

            return false;
        }

        private bool LoopHasReferences(WhileLoop loop)
        {
            foreach(var op in Function.Operations)
            {
                if(!op.Visited)
                {
                    if(op.Metadata.OpCode == ScriptOpCode.Jump)
                    {
                        var jumpLocation = Script.GetJumpLocation(
                                op.OpCodeOffset + op.OpCodeSize,
                                (int)op.Operands[0].Value);

                        if(jumpLocation == loop.ContinueOffset)
                        {
                            return true;
                        }
                    }
                }
            }

            return false;
        }

        private void FindElseIfStatements()
        {
            for (int i = 1; i < Blocks.Count; i++)
            {
                if (Blocks[i] is IfBlock || Blocks[i] is ElseIfBlock)
                {
                    var index = GetInstructionAt(Blocks[i].EndOffset);

                    if (Function.Operations[index - 1].Visited)
                        continue;

                    if(Function.Operations[index - 1].Metadata.OpCode == ScriptOpCode.Jump && !Function.Operations[index - 1].IsTernary)
                    {
                        var elseJump = Function.Operations[index - 1];

                        // Attempt to locate an else if, otherwise we're using this as the jump
                        var endBlock = GetBlockIndexAt(Blocks[i].EndOffset); // end of top if-block
                        var elseJumpLocation = Script.GetJumpLocation(
                                elseJump.OpCodeOffset + elseJump.OpCodeSize,
                                (int)elseJump.Operands[0].Value);

                        bool isBreak = IsBreak(elseJump.OpCodeOffset, elseJumpLocation);
                        bool isContinue = IsContinue(elseJump.OpCodeOffset, elseJumpLocation);

                        VerboseCondition($"[{Function.Name}] Determining Jump Role: IsBreak(0x{elseJump.OpCodeOffset:X4}, 0x{elseJumpLocation:X4}):{isBreak}, IsContinue:{isContinue}", UseElseDetectionLogging);
                        if (!isContinue && !isBreak)
                        {
                            Function.Operations[index - 1].Visited = true;
                            bool hasEndBlock = endBlock > 0;

                            if (hasEndBlock && Blocks[endBlock] is IfBlock elseIf && elseJumpLocation != Blocks[i].EndOffset)
                            {
                                ScriptOp jumpOnTrueOrFalse = null;
                                foreach(var op in Function.Operations)
                                {
                                    if (op.OpCodeOffset < elseIf.StartOffset) continue;
                                    if (op.OpCodeOffset >= elseIf.EndOffset) continue;
                                    if (op.Metadata.OpType == ScriptOpType.JumpCondition)
                                    {
                                        jumpOnTrueOrFalse = op;
                                        break;
                                    }
                                }

                                var elseIfJumpLoc = Script.GetJumpLocation(
                                        jumpOnTrueOrFalse.OpCodeOffset + jumpOnTrueOrFalse.OpCodeSize,
                                        (int)jumpOnTrueOrFalse.Operands[0].Value);

                                if (elseIfJumpLoc == elseJumpLocation)
                                {
                                    // Mark this block
                                    Blocks[endBlock] = new ElseIfBlock(elseIf.StartOffset, elseIf.EndOffset)
                                    {
                                        Comparison = elseIf.Comparison
                                    };

                                    VerboseCondition($"[{Function.Name}] Determined an else-if statement starts at {elseIf.StartOffset:X4} and ends at {elseIf.EndOffset:X4}", UseIfStatementLogging);

                                    if (Blocks[i] is IfBlock ifb)
                                    {
                                        ifb.ChildElse = Blocks[endBlock];
                                    }
                                    //else if (Blocks[i] is ElseIfBlock _eifb)
                                    //{
                                    //    _eifb.ChildElse = Blocks[endBlock];
                                    //}
                                    continue;
                                }
                            }

                            VerboseCondition($"[{Function.Name}] Determined an else statement starts at {(elseJump.OpCodeOffset + elseJump.OpCodeSize):X4} and ends at {(Script.GetJumpLocation(elseJump.OpCodeOffset + elseJump.OpCodeSize, (int)elseJump.Operands[0].Value)):X4}", UseIfStatementLogging);
                            var elseBlock = new ElseBlock(
                                elseJump.OpCodeOffset + elseJump.OpCodeSize,
                            Script.GetJumpLocation(
                                elseJump.OpCodeOffset + elseJump.OpCodeSize,
                                (int)elseJump.Operands[0].Value));

                            if (Blocks[i] is IfBlock _ib)
                            {
                                _ib.ChildElse = elseBlock;
                            }

                            //if (Blocks[i] is ElseIfBlock eifb)
                            //{
                            //    eifb.ChildElse = elseBlock;
                            //}

                            // Mark this block
                            Blocks.Add(elseBlock);
                        }
                    }
                }
            }
        }

        /// <summary>
        /// Locates DoWhile Loops
        /// 
        /// They're actually quite simple, they are simply negative jump statements
        /// So we can consider a non-visited JumpOn with a negative jump to be a DoWhile(...)
        /// </summary>
        private void FindDoWhileLoops()
        {
            for(int i = 0; i < Function.Operations.Count; i++)
            {
                var op = Function.Operations[i];

                if (op.Metadata.OpType != ScriptOpType.JumpCondition) continue;
                if (op.Visited || (int)op.Operands[0].Value >= 0) continue;
                
                op.Visited = true;
                Blocks.Add(new DoWhileLoop(Script.GetJumpLocation(op.OpCodeOffset + op.OpCodeSize, (int)op.Operands[0].Value), op.OpCodeOffset + op.OpCodeSize)
                {
                    Comparison = BuildCondition(FindStartIndexEx(i - 1), true),
                    BreakOffset = op.OpCodeOffset + op.OpCodeSize
                });
            }
        }
        private void FindSwitchCase()
        {
            foreach (var operation in Function.Operations)
            {
                if (operation.Metadata.OpCode == ScriptOpCode.Switch)
                {
                    var switchBlock = new SwitchBlock(
                        operation.OpCodeOffset, 
                        Script.GetJumpLocation(
                            operation.OpCodeOffset + operation.OpCodeSize, 
                            (int)operation.Operands[0].Value));
                    
                    Blocks.Add(switchBlock);

                    Script.Reader.BaseStream.Position = switchBlock.EndOffset;
                    var cases = Script.LoadEndSwitch();

                    for(int i = 0; i < cases.Count; i++)
                    {
                        var block = cases[i];
                        var caseBlock = new CaseBlock(block.ByteCodeOffset, 0)
                        {
                            Value = block.CaseValue,
                            OriginalIndex = block.OriginalIndex
                        };

                        // We can either use this block, or the main switch, as our end offset
                        if(i == cases.Count - 1)
                        {
                            caseBlock.EndOffset = switchBlock.EndOffset;
                        }
                        else
                        {
                            // Use the next block
                            caseBlock.EndOffset = cases[i + 1].ByteCodeOffset;
                        }

                        Blocks.Add(caseBlock);
                    }
                    #region VM1C (T7)
                    if(Script.Header.VMRevision == 0x1C)
                    {
                        switchBlock.BreakOffset = switchBlock.EndOffset + (cases.Count * 8) + 4;
                    }
                    #endregion
                    #region VM37
                    else
                    {
                        switchBlock.BreakOffset = Utility.AlignValue(switchBlock.EndOffset + 4, 8) + (cases.Count * 16);
                    }
                    #endregion
                }
            }
        }

        private void ResolveParentBlocks()
        {
            // Sort it
            Blocks = Blocks.OrderBy(x => x.StartOffset).ToList();

            // Move else statements directly behind an if statement to avoid weird decompilation bugs with them
            for(int i = 0; i < Blocks.Count; i++)
            {
                if(Blocks[i] is IfBlock ib && ib.ChildElse != null)
                {
                    Blocks.Remove(ib.ChildElse);
                    Blocks.Insert(i + 1, ib.ChildElse);
                }
            }

            for(int i = Blocks.Count - 1; i >= 0; i--) // child
            {
                // if (Blocks[i] is DevBlock db && db.ParentDetected) continue;
                for (int j = Blocks.Count - 1; j >= 0; j--) // parent
                {
                    if (Blocks[i] == Blocks[j]) continue;
                    if (Blocks[j] is DevBlock _db && _db.ParentDetected) continue;
                    if (Blocks[j] is CaseBlock && !(Blocks[i] is SwitchBlock) && !(Blocks[i] is CaseBlock))
                    {
                        if (Blocks[i].StartOffset >= Blocks[j].StartOffset && Blocks[i].EndOffset <= Blocks[j].EndOffset)
                        {
                            Blocks[j].ChildBlockIndices.Add(i);
                            Blocks[i].ParentBlockIndices.Add(j);
                            break;
                        }
                    }
                    else if (Blocks[i] is CaseBlock && !(Blocks[j] is SwitchBlock))
                    {
                        continue;
                    }
                    else
                    {
                        if (Blocks[i].StartOffset >= Blocks[j].StartOffset && Blocks[i].EndOffset <= Blocks[j].EndOffset)
                        {
                            Blocks[j].ChildBlockIndices.Add(i);
                            Blocks[i].ParentBlockIndices.Add(j);
                            break;
                        }
                    }
                }
            }
        }

        /// <summary>
        /// Generates a source function call (i.e. Func(p1, p2, p3))
        /// 
        /// Method means we're calling on something
        /// </summary>
        private string GenerateFunctionCall(string functionName, int paramCount, int threaded, bool method)
        {
            string result = "";

            if(method)
            {
                result += Stack.Pop() + " ";
            }

            if(threaded == 1)
            {
                result += "thread ";
            }
            else if(threaded == 2)
            {
                result += "childthread ";
            }

            result += functionName + "(";

            for(int i = 0; i < paramCount; i++)
            {
                result += Stack.Pop();

                if(i != paramCount - 1)
                {
                    result += ", ";
                }
            }

            result += ")";

            return result;
        }

        private void WriteHeader(DecompilerBlock block, bool UseLine = true)
        {
            var size = block.EndOffset - block.StartOffset;

            if (!string.IsNullOrWhiteSpace(block.GetHeader()))
            {
                if(UseLine)
                    Writer?.WriteLine(block.GetHeader());
                else
                    Writer?.Write(block.GetHeader());
            }

            if(block.RequiresBraces)
            {
                if (size <= 0 && block.CanSkipZeroSize)
                {
                    return;
                }

                Writer?.WriteLine("{");
            }
        }

        private void WriteFooter(DecompilerBlock block)
        {
            var size = block.EndOffset - block.StartOffset;

            if (block.RequiresBraces)
            {
                if (size <= 0 && block.CanSkipZeroSize)
                {
                    return;
                }

                Writer?.WriteLine("}");
            }

            if (!string.IsNullOrWhiteSpace(block.GetFooter()))
            {
                Writer?.WriteLine(block.GetFooter());
            }
        }

        private bool IsContinue(int ip, int jumpOffset)
        {
            if (Blocks.FindIndex(block =>
             (block.StartOffset >= jumpOffset && block.EndOffset > jumpOffset) &&
             block.ContinueOffset == jumpOffset &&
             (block.StartOffset < ip && block.EndOffset >= ip)) >= 0)
                return true; //standard continue

            //for loop continue
            if (Blocks.FindIndex(block => (block is ForLoopBlock || block is ForEach) &&
             (block.StartOffset <= jumpOffset && block.EndOffset > jumpOffset) &&
             block.ContinueOffset == jumpOffset &&
             (block.StartOffset < ip && block.EndOffset >= ip)) >= 0)
                return true;

            //forever loop
            return Blocks.FindIndex(block => block is ForEver fblk &&
             (fblk.StartOffset <= jumpOffset && fblk.EndOffset >= jumpOffset) &&
             block.ContinueOffset == jumpOffset &&
             (block.StartOffset < ip && block.EndOffset >= ip)) >= 0;
        }

        private bool IsBreak(int ip, int jumpOffset)
        {
            return Blocks.FindIndex(block => 
            (block.StartOffset < jumpOffset && block.EndOffset <= jumpOffset) && 
            block.BreakOffset == jumpOffset && (block.StartOffset <= ip && block.EndOffset > ip)) >= 0;
        }

        private void DecompileBlock(DecompilerBlock decompilerBlock, int tabs)
        {
            Verbose($"[{Function.Name}] is decompiling {decompilerBlock.GetType()} at 0x{decompilerBlock.StartOffset:X4}, end at 0x{decompilerBlock.EndOffset:X4}");
            if(decompilerBlock is SwitchBlock switchBlock)
            {
                switchBlock.Value = Stack.Pop();
            }

            if(decompilerBlock is TernaryBlock tblock)
            {
                return;
            }

            if(decompilerBlock.Visited)
            {
                return;
            }

            decompilerBlock.Visited = true;
            if(!decompilerBlock.DisableHeader)
            {
                WriteHeader(decompilerBlock);
            }
            Writer.Indent = tabs;
            int i;
            for (i = GetInstructionAt(decompilerBlock.StartOffset); i < Function.Operations.Count && Function.Operations[i].OpCodeOffset < decompilerBlock.EndOffset; i++)
            {
                var operation = Function.Operations[i];

                if (operation.Metadata.OpCode == ScriptOpCode.Invalid)
                {
                    throw new Exception("Function contains invalid OpCode.");
                }

                foreach (var childIndex in decompilerBlock.ChildBlockIndices)
                {
                    if(Blocks[childIndex].StartOffset == operation.OpCodeOffset)
                    {
                        DecompileBlock(Blocks[childIndex], tabs + 1);
                    }
                }

                if (operation.IsTernary)
                {
                    var t = FindTernaryAt(operation.OpCodeOffset);

                    if (t == null)
                        throw new Exception($"Operation is ternary, but the block decompiler couldn't locate one at 0x{operation.OpCodeOffset:X6}");

                    if (!t.Visited)
                    {
                        if (t.PushVal != null) Stack.Push(t.PushVal);
                        Stack.Push(t.GetHeader());
                        t.Visited = true;
                    }

                    i = GetInstructionAt(t.EndOffset) - 1;
                    continue;
                }

                if (operation.Visited)
                {
                    continue;
                }

                Function.Operations[i].Visited = true;

                // Pass to processor
                object refOut = null;
                ProcessInstruction(operation, ref refOut);
                if (refOut is bool && (bool)refOut && (operation.Metadata.OpCode == ScriptOpCode.ScriptFunctionCall || operation.Metadata.OpCode == ScriptOpCode.ClassFunctionCall))
                {
                    operation.Visited = true;
                    Function.Operations[i + 1].Visited = true;
                }
            }

            Writer.Indent = tabs - 1;
            WriteFooter(decompilerBlock);
        }

        /// <summary>
        /// Locates Developer Blocks
        /// </summary>
        void FindDevBlocks()
        {
            foreach(var operation in Function.Operations)
            {
                if(operation.Metadata.OpCode == ScriptOpCode.DevblockBegin)
                {
                    var iblock = GetBlockIndexAt(operation.OpCodeOffset);

                    if (iblock > -1)
                    {
                        var db = new DevBlock(operation.OpCodeOffset, Script.GetJumpLocation(operation.OpCodeOffset + operation.OpCodeSize, (int)operation.Operands[0].Value), true);
                        var block = Blocks[iblock];
                        block.ChildBlockIndices.Add(Blocks.Count);
                        Blocks.Add(db);
                    }
                    else
                    {
                        Blocks.Add(new DevBlock(operation.OpCodeOffset, Script.GetJumpLocation(operation.OpCodeOffset + operation.OpCodeSize, (int)operation.Operands[0].Value), false));
                    }
                }
            }
        }

        /// <summary>
        /// Builds a basic list of blocks
        /// 
        /// As per usual, any jump we can consider to start and end a block
        /// However, GSC, being as high level as it is, contains much more info
        /// we can use.
        /// Examples are switch statements and dev blocks. The real trouble comes with
        /// determining loops and if/else, which itself is quite easy, on the loop side
        /// however we need to be able to identify foreach and for loops, along with 
        /// ternary
        /// We can consider a ternary operator to be a if statement that has 1 stack 
        /// instruction, but this is more data anaylsis than control flow
        /// For loops we can check initialization, comparison, and incrementation,
        /// along with checking jump statements within if statements, as they can 
        /// jump onto the incrementer
        /// </summary>
        private void FindWhileLoops()
        {
            // We perform a reverse scan, since we're looking for negative jumps
            // If we come across a negative jump it can be one of 2 things: a continue
            // statement, or a loop. We can determine if it's a continue if we can find
            // the jump location within our list of blocks, if we can't, it's a new loop

            // We don't check for break statements here as we first need to ensure we've
            // resolved for loops, etc. later
            for(int i = Function.Operations.Count - 1; i >= 0; i--)
            {
                switch(Function.Operations[i].Metadata.OpCode)
                {
                    case ScriptOpCode.Jump:
                        {
                            // Check for a negative jumps, is almost always a loop
                            if(!Function.Operations[i].Visited && (int)Function.Operations[i].Operands[0].Value < 0)
                            {
                                VerboseCondition($"[{Function.Name}] thinks 0x{Function.Operations[i].OpCodeOffset:X4} is probably a while loop.", UseWhileLoopDetectionLogging);
                                // Compute the jump location based as some games align the value
                                var offset = Script.GetJumpLocation(
                                    Function.Operations[i].OpCodeOffset + Function.Operations[i].OpCodeSize, 
                                    (int)Function.Operations[i].Operands[0].Value);

                                var res = !IsContinue(Function.Operations[i].OpCodeOffset, offset);
                                VerboseCondition($"[{Function.Name}] !IsContinue({Function.Operations[i].OpCodeOffset:X4}, {offset:X4}): {res}", UseWhileLoopDetectionLogging);
                                // Attempt to locate the block at the current location, if we fail we're
                                // creating a new loop, otherwise this is probably continue statement
                                if (res)
                                {
                                    Function.Operations[i].Visited = true;
                                    VerboseCondition($"[{Function.Name}] confirmed 0x{Function.Operations[i].OpCodeOffset:X4} is not a continue.", UseWhileLoopDetectionLogging);
                                    // Attempt to resolve is this a for(;;) or a while(...)
                                    bool hasCondition = false;
                                    var end = Function.Operations[i].OpCodeOffset;
                                    var conditionStart = GetInstructionAt(offset);

                                    // Keep going until we either hit 
                                    for (int j = conditionStart; j < Function.Operations.Count; j++)
                                    {
                                        var op = Function.Operations[j];

                                        if (op.IsTernary)
                                            continue;

                                        // Check if we hit a JumpOn, if we do, we're probably a while(...)
                                        if (op.Metadata.OpType == ScriptOpType.JumpCondition)
                                        {
                                            var jumpLocation = Script.GetJumpLocation(op.OpCodeOffset + Function.Operations[i].OpCodeSize, (int)op.Operands[0].Value);
                                            var jumpedTo = GetInstructionAt(jumpLocation);
                                            if (jumpedTo != i + 1 &&
                                                // Detect if this is a for loop
                                                ((jumpedTo != i - 2 && jumpedTo != i - 1) || Function.Operations[i - 1].Metadata.OpType != ScriptOpType.SingleOperand))
                                            {
                                                continue; // This fixes "for(;;) if(..." converting into a while loop
                                            }
                                            hasCondition = true;
                                            break;
                                        }

                                        // Check if we hit a non-stack/expression operation
                                        if (!IsStackOperation(op) && op.Metadata.OpType != ScriptOpType.JumpExpression)
                                        {
                                            break;
                                        }
                                    }

                                    if (hasCondition)
                                    {
                                        VerboseCondition($"[{Function.Name}] marking [0x{offset:X4}:0x{end:X4}] as a while loop, condition start at 0x{Function.Operations[conditionStart].OpCodeOffset:X4}", UseWhileLoopDetectionLogging);
                                        // We have a condition, add as a while
                                        var whileLoop = new WhileLoop(offset, end)
                                        {
                                            Comparison = BuildCondition(conditionStart),
                                            ContinueOffset = Function.Operations[conditionStart].OpCodeOffset,
                                            BreakOffset = end + Function.Operations[i].OpCodeSize
                                        };

                                        Blocks.Add(whileLoop);
                                    }
                                    else
                                    {
                                        // We're an infinite for(;;)
                                        Blocks.Add(new ForEver(offset, end)
                                        {
                                            BreakOffset = end + Function.Operations[i].OpCodeSize,
                                            ContinueOffset = end
                                        });
                                        VerboseCondition($"[{Function.Name}] marking [0x{offset:X4}:0x{end:X4}:0x{(end + Function.Operations[i].OpCodeSize):X4}] as a forever loop", UseWhileLoopDetectionLogging);
                                    }
                                }
                            }
                            break;
                        }
                }
            }
        }

        /// <summary>
        /// Builds an expression jump
        /// </summary>
        private string BuildExpression(ScriptOp startOp)
        {
            // JumpOnTrue is || JumpOnFalse is &&
            var requiresBraces = false;
            var endOffset = Script.GetJumpLocation(startOp.OpCodeOffset + startOp.OpCodeSize, (int)startOp.Operands[0].Value);

            var startIndex = GetInstructionAt(startOp.OpCodeOffset + startOp.OpCodeSize);
            var endIndex = GetInstructionAt(endOffset);

            // Attempt to build it
            for (int j = startIndex; j < endIndex; j++)
            {
                var op = Function.Operations[j];
                // Check if we hit a non-stack/expression operation
                if (!IsStackOperation(op))
                {
                    throw new Exception("Unexpected non-stack operation within jump expression");
                }

                if (op.IsTernary)
                {
                    var t = FindTernaryAt(op.OpCodeOffset);

                    if (t == null)
                        throw new Exception("Operation is ternary, but some shit happened and its not the start of the block for build expression");

                    t.Visited = true;
                    if (t.PushVal != null) Stack.Push(t.PushVal);
                    Stack.Push(t.GetHeader());
                    j = GetInstructionAt(t.EndOffset) - 1;
                    continue;
                }

                if (op.Visited == true)
                {
                    continue;
                }

                if (op.Metadata.OpType == ScriptOpType.JumpExpression)
                {
                    requiresBraces = true;
                }

                object refOut = null;
                ProcessInstruction(op, ref refOut);
                op.Visited = true;
            }

            var result = Stack.Pop();

            // Determine if it needs braces (nested expressions)
            if (requiresBraces)
                result = "(" + result + ")";

            return result;
        }

        private string BuildCondition(int startIndex, bool invert=false, int endIndex = -1)
        {
            var result = "";
            var requiresBraces = false;
            if (endIndex == -1) endIndex = Function.Operations.Count;
            for (int j = startIndex; j < endIndex; j++)
            {
                var op = Function.Operations[j];

                if(op.IsTernary)
                {
                    var t = FindTernaryAt(op.OpCodeOffset);

                    if (t == null)
                        throw new Exception("Operation is ternary, but some shit happened and its not the start of the block for build condition");
                    
                    if(!t.Visited)
                    {
                        t.Visited = true;
                        if (t.PushVal != null) Stack.Push(t.PushVal);
                        Stack.Push(t.GetHeader());
                    }
                    j = GetInstructionAt(t.EndOffset) - 1;
                    continue;
                }

                if (op.Metadata.OpType == ScriptOpType.JumpExpression)
                {
                    requiresBraces = true;
                }

                if (op.Metadata.OpType == ScriptOpType.JumpCondition)
                {
                    if(op.Metadata.OpCode == ScriptOpCode.JumpOnTrue || op.Metadata.OpCode == ScriptOpCode.JumpOnFalse)
                    {
                        result += Stack.Pop();
                        // If it's a forward JumpOnTrue, we add ! since the block won't execute if the
                        // the condition is true, however for dowhile this will be JumpOnTrue because
                        // we want to continue with the execution (back to start) if it's true
                        if (op.Metadata.OpCode == ScriptOpCode.JumpOnTrue != invert)
                        {
                            if (requiresBraces)
                            {
                                result = "(" + result + ")";
                            }

                            result = "!" + result;
                        }
                    }
                    else
                    {
                        // inverted bullshit to make the code more readable
                        string _comp = op.Metadata.OpCode == ScriptOpCode.JumpOnGreaterThan ? "<" : ">";
                        string _reversedop = Stack.Pop();
                        result += $"{Stack.Pop()} {_comp} {_reversedop}";

                        if (requiresBraces)
                        {
                            result = "(" + result + ")";
                        }
                    }
                    

                    op.Visited = true;
                    break;
                }

                if (op.Visited)
                {
                    continue;
                }

                // Check if we hit a non-stack/expression operation
                if (!IsStackOperation(op))
                {
                    break;
                }

                object refOut = null;
                ProcessInstruction(op, ref refOut);
                op.Visited = true;
            }

            return result;
        }

        /// <summary>
        /// Locates ForEach loops
        /// 
        /// A explicit call to First/NextArrayKey via an instruction is always going
        /// to be foreach, attempting to call GetFirstArrayKey in GSC will generate the
        /// call, where as foreach will generate the instruction
        /// </summary>
        private void FindForEachLoops()
        {
            for (int i = 0; i < Function.Operations.Count; i++)
            {
                var op = Function.Operations[i];

                if (op.Metadata.OpCode == ScriptOpCode.FirstArrayKeyCached)
                {
                    if (Function.Operations[i - 1].Metadata.OpCode != ScriptOpCode.SetLocalVariableCached)
                        continue;
                    TryMarkForeachVM36(i);
                }

                if (op.Metadata.OpCode == ScriptOpCode.FirstArrayKey)
                {
                    if (Function.Operations[i - 1].Metadata.OpCode != ScriptOpCode.EvalLocalVariableCached && Function.Operations[i - 1].Metadata.OpCode != ScriptOpCode.EvalLocalVariableCached2) 
                        continue;
                    if (Script.Header.VMRevision == 0x1C)
                    {
                        TryMarkForeachVM1C(i);
                        continue;
                    }
                    TryMarkForeachVM37(i);
                }
            }
        }

        private void TryMarkForeachVM37(int i)
        {
            var op = Function.Operations[i];
            var index = GetBlockIndexAt(Function.Operations[i + 2].OpCodeOffset);

            if (!(Blocks[index] is WhileLoop loop))
                return;

            int continueOffset;
            var variableBeginIndex = FindStartIndex(i - 3);
            // Process so we can obtain the real variable name
            for (int j = variableBeginIndex; j < i - 2; j++)
            {
                object refOut = null;
                ProcessInstruction(Function.Operations[j], ref refOut);
                Function.Operations[j].Visited = true;
            }
            var ArrayName = Stack.Pop();

            Function.Operations[i - 02].Visited = true; // OP_SetLocalVariableCached
            Function.Operations[i - 01].Visited = true; // OP_EvalLocalVariableCached
            Function.Operations[i + 00].Visited = true; // OP_FirstArrayKey
            Function.Operations[i + 01].Visited = true; // OP_SetLocalVariableCached
            Function.Operations[i + 02].Visited = true; // OP_EvalLocalVariableDefined
            Function.Operations[i + 03].Visited = true; // OP_JumpOnFalse
            Function.Operations[i + 04].Visited = true; // OP_EvalLocalVariableCached
            Function.Operations[i + 05].Visited = true; // OP_GetArrayKeyIndex
            Function.Operations[i + 06].Visited = true; // OP_SetLocalVariableCached
            Function.Operations[i + 07].Visited = true; // OP_EvalLocalVariableCached
            Function.Operations[i + 08].Visited = true; // OP_GetArrayValue
            Function.Operations[i + 09].Visited = true; // OP_SetLocalVariableCached
            Function.Operations[i + 10].Visited = true; // OP_EvalLocalVariableCached
            Function.Operations[i + 11].Visited = true; // OP_NextArrayKey
            Function.Operations[i + 12].Visited = true; // OP_SetLocalVariableCached

            // Clear the instructions at the end
            var opIndex = GetInstructionAt(loop.EndOffset);

            var KeyVar = Function.Operations[i + 6];

            // iterate the foreach body, and try to find some reference to our key
            // if we find a key ref, its a double foreach
            bool UseKey = false;
            for (int j = i + 13; j < opIndex - 2; j++)
            {
                switch (Function.Operations[j].Metadata.OpCode)
                {
                    case ScriptOpCode.EvalLocalVariableDefined:
                    case ScriptOpCode.SetLocalVariableCached:
                    case ScriptOpCode.SetNextArrayKeyCached:
                    case ScriptOpCode.EvalLocalVariableCached:
                    case ScriptOpCode.EvalLocalVariableRefCached:
                    case ScriptOpCode.EvalLocalVariableRefCached2:
                    case ScriptOpCode.EvalLocalArrayRefCached:
                        if (int.Parse(Function.Operations[j].Operands[0].Value.ToString()) ==
                            int.Parse(KeyVar.Operands[0].Value.ToString())) UseKey = true;
                        break;
                }
                if (UseKey) break;
            }

            // Now that we've determined it's a foreach, we need to remove the necessary
            // operations, along with determining the continue location
            Function.Operations[opIndex - 1].Visited = true; // OP_EvalLocalVariableCached
            Function.Operations[opIndex - 2].Visited = true; // OP_SetLocalVariableCached
            continueOffset = Function.Operations[opIndex - 2].OpCodeOffset;

            Blocks[index] = new ForEach(loop.StartOffset, loop.EndOffset)
            {
                ArrayName = ArrayName,
                KeyName = UseKey ? GetVariableName(KeyVar) : null,
                IteratorName = GetVariableName(Function.Operations[i + 9]),
                ContinueOffset = continueOffset,
                BreakOffset = loop.BreakOffset
            };
        }

        private void TryMarkForeachVM1C(int i)
        {
            var op = Function.Operations[i];

            var index = GetBlockIndexAt(Function.Operations[i + 3].OpCodeOffset);

            if (!(Blocks[index] is WhileLoop loop))
                throw new ArgumentException("Expecting While Loop At FirstArrayKey: index(" + index + ")");

            int continueOffset;
            var variableBeginIndex = FindStartIndex(i - 4);
            // Process so we can obtain the real variable name
            for (int j = variableBeginIndex; j < i - 3; j++)
            {
                object refOut = null;
                ProcessInstruction(Function.Operations[j], ref refOut);
                Function.Operations[j].Visited = true;
            }
            var ArrayName = Stack.Pop();

            // Mark these operations
            Function.Operations[i - 3].Visited = true;
            Function.Operations[i - 2].Visited = true;
            Function.Operations[i - 1].Visited = true;
            Function.Operations[i + 0].Visited = true;
            Function.Operations[i + 1].Visited = true;
            Function.Operations[i + 2].Visited = true;
            Function.Operations[i + 3].Visited = true;
            Function.Operations[i + 4].Visited = true;
            Function.Operations[i + 5].Visited = true;
            Function.Operations[i + 6].Visited = true;
            Function.Operations[i + 7].Visited = true;
            Function.Operations[i + 8].Visited = true;
            Function.Operations[i + 9].Visited = true;
            Function.Operations[i + 10].Visited = true;

            // Clear the instructions at the end
            var opIndex = GetInstructionAt(loop.EndOffset);

            var KeyVar = Function.Operations[i + 1];

            // iterate the foreach body, and try to find some reference to our key
            // if we find a key ref, its a double foreach
            bool UseKey = false;
            for (int j = i + 15; j < opIndex - 3; j++)
            {
                switch (Function.Operations[j].Metadata.OpCode)
                {
                    case ScriptOpCode.EvalLocalVariableDefined:
                    case ScriptOpCode.SetLocalVariableCached:
                    case ScriptOpCode.SetNextArrayKeyCached:
                    case ScriptOpCode.EvalLocalVariableCached:
                    case ScriptOpCode.EvalLocalVariableRefCached:
                    case ScriptOpCode.EvalLocalVariableRefCached2:
                    case ScriptOpCode.EvalLocalArrayRefCached:
                        if (int.Parse(Function.Operations[j].Operands[0].Value.ToString()) ==
                            int.Parse(KeyVar.Operands[0].Value.ToString())) UseKey = true;
                        break;
                }
                if (UseKey) break;
            }

            // Now that we've determined it's a foreach, we need to remove the necessary
            // operations, along with determining the continue location, since Bo3 and 2 
            // are slightly different
            if (Function.Operations[i + 13].Metadata.OpCode == ScriptOpCode.NextArrayKey)
            {
                Function.Operations[i + 11].Visited = true;
                Function.Operations[i + 12].Visited = true;
                Function.Operations[i + 13].Visited = true;
                Function.Operations[i + 14].Visited = true;
                Function.Operations[i + 15].Visited = true;
                Function.Operations[opIndex - 1].Visited = true;
                Function.Operations[opIndex - 2].Visited = true;
                Function.Operations[opIndex - 3].Visited = true;
                continueOffset = Function.Operations[opIndex - 3].OpCodeOffset;
            }
            else
            {
                Function.Operations[opIndex - 1].Visited = true;
                Function.Operations[opIndex - 2].Visited = true;
                Function.Operations[opIndex - 3].Visited = true;
                Function.Operations[opIndex - 4].Visited = true;
                Function.Operations[opIndex - 5].Visited = true;
                continueOffset = Function.Operations[opIndex - 5].OpCodeOffset;
            }

            Blocks[index] = new ForEach(loop.StartOffset, loop.EndOffset)
            {
                ArrayName = ArrayName,
                KeyName = UseKey ? GetVariableName(KeyVar) : null,
                IteratorName = GetVariableName(Function.Operations[i + 9]),
                ContinueOffset = continueOffset,
                BreakOffset = loop.BreakOffset
            };
        }

        private void TryMarkForeachVM36(int i)
        {
            var op = Function.Operations[i];

            var index = GetBlockIndexAt(Function.Operations[i + 2].OpCodeOffset);

            if (!(Blocks[index] is WhileLoop loop))
            {
                return;
            }

            int continueOffset;
            var variableBeginIndex = FindStartIndex(i - 2);
            // Process so we can obtain the real variable name
            for (int j = variableBeginIndex; j < i - 1; j++)
            {
                object refOut = null;
                ProcessInstruction(Function.Operations[j], ref refOut);
                Function.Operations[j].Visited = true;
            }
            var ArrayName = Stack.Pop();

            Function.Operations[i - 1].Visited = true; // OP_SetLocalVariableCached
            Function.Operations[i + 0].Visited = true; // OP_FirstArrayKeyCached
            Function.Operations[i + 1].Visited = true; // OP_SetLocalVariableCached
            Function.Operations[i + 2].Visited = true; // OP_EvalLocalVariableDefined
            Function.Operations[i + 3].Visited = true; // OP_JumpOnFalse
            Function.Operations[i + 4].Visited = true; // OP_EvalLocalVariableCached
            Function.Operations[i + 5].Visited = true; // OP_EvalLocalVariableCached
            Function.Operations[i + 6].Visited = true; // OP_EvalArray
            Function.Operations[i + 7].Visited = true; // OP_SetLocalVariableCached
            Function.Operations[i + 8].Visited = true; // OP_EvalLocalVariableCached
            Function.Operations[i + 9].Visited = true; // OP_EvalLocalVariableCached
            Function.Operations[i + 10].Visited = true; // OP_SetNextArrayKeyCached

            // Clear the instructions at the end
            var opIndex = GetInstructionAt(loop.EndOffset);

            var KeyVar = Function.Operations[i + 1];

            // iterate the foreach body, and try to find some reference to our key
            // if we find a key ref, its a double foreach
            bool UseKey = false;
            for (int j = i + 11; j < opIndex - 2; j++)
            {
                switch (Function.Operations[j].Metadata.OpCode)
                {
                    case ScriptOpCode.EvalLocalVariableDefined:
                    case ScriptOpCode.SetLocalVariableCached:
                    case ScriptOpCode.SetNextArrayKeyCached:
                    case ScriptOpCode.EvalLocalVariableCached:
                    case ScriptOpCode.EvalLocalVariableCached2:
                    case ScriptOpCode.EvalLocalVariableRefCached:
                    case ScriptOpCode.EvalLocalVariableRefCached2:
                    case ScriptOpCode.EvalLocalArrayRefCached:
                        if (int.Parse(Function.Operations[j].Operands[0].Value.ToString()) ==
                            int.Parse(KeyVar.Operands[0].Value.ToString())) UseKey = true;
                        break;
                }
                if (UseKey) break;
            }

            // Now that we've determined it's a foreach, we need to remove the necessary
            // operations, along with determining the continue location

            Function.Operations[opIndex - 1].Visited = true; // OP_EvalLocalVariableCached
            Function.Operations[opIndex - 2].Visited = true; // OP_SetLocalVariableCached
            continueOffset = Function.Operations[opIndex - 2].OpCodeOffset;

            Blocks[index] = new ForEach(loop.StartOffset, loop.EndOffset)
            {
                ArrayName = ArrayName,
                KeyName = UseKey ? GetVariableName(KeyVar) : null,
                IteratorName = GetVariableName(Function.Operations[i + 7]),
                ContinueOffset = continueOffset,
                BreakOffset = loop.BreakOffset
            };
        }

        private int GetBlockForInstruction(ScriptOp op)
        {
            for(int i = Blocks.Count - 1; i >= 0; i--)
            {
                if(op.OpCodeOffset >= Blocks[i].StartOffset && op.OpCodeOffset < Blocks[i].EndOffset)
                {
                    return i;
                }
            }

            return -1;
        }

        private void FindForLoops()
        {
            for(int i = 0; i < Blocks.Count; i++)
            {
                var block = Blocks[i];

                Dictionary<int, bool> ProcessedSnapshot = new Dictionary<int, bool>();
                for(int k = 0; k < Function.Operations.Count; k++)
                {
                    ProcessedSnapshot[k] = Function.Operations[k].Visited;
                }

                if(block is WhileLoop whileLoop)
                {
                    VerboseCondition($"[{Function.Name}] Checking potential for loop conditions {whileLoop.StartOffset:X}:{whileLoop.EndOffset:X}", UseForLoopVerbose);
                    if (LoopHasReferences(whileLoop))
                    {
                        VerboseCondition($"[{Function.Name}] Failed because of references {whileLoop.StartOffset:X}:{whileLoop.EndOffset:X}", UseForLoopVerbose);
                        continue;
                    }
                    
                    var index = GetInstructionAt(whileLoop.StartOffset);
                    var SetIndex = FindStartIndex(index - 1, true) - 1;
                    // For now we just check for Variable Reference + SetVariableField, a comparison, and a increment
                    if (
                        Function.Operations[SetIndex].Metadata.OpType == ScriptOpType.SetVariable)
                    {
                        bool isCompared = false;
                        bool b_revert = true;

                        // Attempt to resolve info
                        var variableBeginIndex = FindStartIndex(SetIndex - 1, true);

                        // Validate if it's inside a previous block
                        var blockIndex = GetBlockForInstruction(Function.Operations[variableBeginIndex]);

                        if(blockIndex > -1 && blockIndex != i)
                        {
                            if (whileLoop.StartOffset >= Blocks[blockIndex].EndOffset && whileLoop.StartOffset >= Blocks[blockIndex].StartOffset)
                            {
                                VerboseCondition($"[{Function.Name}] Failed because of block condition offsets {whileLoop.StartOffset:X}:{whileLoop.EndOffset:X}", UseForLoopVerbose);
                                continue;
                            }
                        }

                        try
                        {
                            // Process so we can obtain the real variable name
                            for (int j = variableBeginIndex; j < index; j++)
                            {
                                if (Function.Operations[j].Visited)
                                    continue;

                                object refOut = null;
                                ProcessInstruction(Function.Operations[j], ref refOut);
                            }

                            // First let's see if this is the variable we can use
                            var variableName = CurrentReference;

                            // Attempt to hit a comparison to this
                            for (int j = index; j < Function.Operations.Count; j++)
                            {

                                var op = Function.Operations[j];

                                // Check if we hit a JumpOn..etc. we're done
                                if (op.Metadata.OpType == ScriptOpType.JumpCondition)
                                {
                                    break;
                                }

                                // Check if we hit a non-stack/expression operation
                                if (!IsStackOperation(op) && op.Metadata.OpType != ScriptOpType.JumpExpression)
                                {
                                    break;
                                }

                                if (op.Metadata.OperandType == ScriptOperandType.DoubleUInt8)
                                {
                                    var pair = GetVariableNames(op);
                                    if (pair.Item1 == variableName || pair.Item2 == variableName)
                                    {
                                        isCompared = true;
                                        break;
                                    }
                                }
                                else if (op.Metadata.OpType == ScriptOpType.Variable)
                                {
                                    var compVar = GetVariableName(op);

                                    // At some point, this variable is being compared
                                    // So we can try and use it
                                    if (compVar == variableName)
                                    {
                                        isCompared = true;
                                        break;
                                    }
                                }
                            }

                            VerboseCondition($"[{Function.Name}] For loop isCompared {variableName}:{isCompared}", UseForLoopVerbose);
                            // Check if we hit a valid comparison
                            if (isCompared)
                            {
                                // Now let's do a backwards scan like the while, until we hit a reference to the variable, if we do
                                // We can be fairly confident it's a for loop and can mark is as such
                                // We can also run a jump check again to see forward jumps that match us
                                var endIndex = GetInstructionAt(whileLoop.EndOffset) - ((Script is BlackOps4Script) ? 2 : 1);
                                bool isModified = false;
                                int referenceIndex = -1;
                                int _j = endIndex;
                                // Attempt to hit a comparison to this
                                for (_j = endIndex; _j >= 0; _j--)
                                {
                                    var op = Function.Operations[_j];

                                    if (IsInsideChildBlock(op.OpCodeOffset, whileLoop))
                                    {
                                        break;
                                    }

                                    if (op.Metadata.OpType == ScriptOpType.JumpCondition)
                                    {
                                        break;
                                    }

                                    if (op.Metadata.OpType == ScriptOpType.SingleOperand &&
                                        op.Metadata.OpCode != ScriptOpCode.DecCached &&
                                        op.Metadata.OpCode != ScriptOpCode.IncCached)
                                    {
                                        continue;
                                    }

                                    if (op.OpCodeOffset < whileLoop.StartOffset)
                                    {
                                        continue;
                                    }

                                    if (op.Metadata.OpType == ScriptOpType.VariableReference)
                                    {
                                        var compVar = GetVariableName(op);

                                        // At some point, this variable is being compared
                                        // So we can try and use it
                                        if (compVar == variableName)
                                        {
                                            referenceIndex = _j;
                                            isModified = true;
                                            break;
                                        }
                                    }

                                    if (op.Metadata.OpCode == ScriptOpCode.DecCached || op.Metadata.OpCode == ScriptOpCode.IncCached)
                                    {
                                        var compVar = GetVariableName(op);

                                        // At some point, this variable is being compared
                                        // So we can try and use it
                                        if (compVar == variableName)
                                        {
                                            referenceIndex = _j;
                                            isModified = true;
                                            break;
                                        }
                                    }

                                    if (!IsStackOperation(op))
                                    {
                                        break;
                                    }
                                }

                                VerboseCondition($"[{Function.Name}] For loop isModified index.{referenceIndex:X3}:{isModified}:{_j:X3}", UseForLoopVerbose);
                                // Final straw, IS SHE MODIFIED
                                if (isModified)
                                {

                                    // First let's build the intializer
                                    var initBegin = FindStartIndex(SetIndex - 1, true);

                                    var forLoop = new ForLoopBlock(whileLoop.StartOffset, whileLoop.EndOffset)
                                    {
                                        Comparison = whileLoop.Comparison,
                                        BreakOffset = whileLoop.BreakOffset,
                                    };

                                    for (int j = initBegin; j < index; j++)
                                    {
                                        // We've now visited/processed this
                                        Function.Operations[j].Visited = true;
                                        var op = Function.Operations[j];

                                        if (op.Metadata.OpType == ScriptOpType.SetVariable)
                                        {
                                            switch (op.Metadata.OpCode)
                                            {
                                                case ScriptOpCode.SafeSetVariableFieldCached:
                                                    forLoop.Initializer = $"{GetLocalVariable((int)op.Operands[0].Value)}.{op.Operands[1].Value} = {Stack.Pop()}";
                                                    break;
                                                case ScriptOpCode.SetArrayField:
                                                    forLoop.Initializer = $"{CurrentReference += "[" + Stack.Pop() + "]"} = {Stack.Pop()}";
                                                    break;
                                                case ScriptOpCode.SetGlobalObjectFieldVariable:
                                                    forLoop.Initializer = $"{Script.GlobalObjects[op.OpCodeOffset + 2]}.{op.Operands[1].Value} = {Stack.Pop()};";
                                                    break;
                                                case ScriptOpCode.SetLocalVariableCached:
                                                    forLoop.Initializer = string.Format("{0} = {1}", LocalVariables[LocalVariables.Count - (int)op.Operands[0].Value - 1], Stack.Pop());
                                                    break;
                                                case ScriptOpCode.SetVariableFieldRef:
                                                    forLoop.Initializer = string.Format("{0} = {1}", $"{CurrentReference}.{op.Operands[0].Value}", Stack.Pop());
                                                    break;
                                                default:
                                                    forLoop.Initializer = string.Format("{0} = {1}", CurrentReference, Stack.Pop());
                                                    CurrentReference = "";
                                                    break;
                                            }
                                            break;
                                        }

                                        object refOut = null;
                                        ProcessInstruction(op, ref refOut);
                                    }

                                    int modifierStart;

                                    // Last we build the modifier
                                    // First we can check if it's a simple case of op inc

                                    if (
                                        Function.Operations[referenceIndex + 1].Metadata.OpCode == ScriptOpCode.Dec ||
                                        Function.Operations[referenceIndex + 1].Metadata.OpCode == ScriptOpCode.Inc
                                        )
                                    {
                                        modifierStart = referenceIndex;
                                    }
                                    else if (Function.Operations[referenceIndex].Metadata.OpCode == ScriptOpCode.DecCached || Function.Operations[referenceIndex].Metadata.OpCode == ScriptOpCode.IncCached)
                                    {
                                        modifierStart = referenceIndex;
                                    }
                                    else
                                    {
                                        // Resolve it
                                        modifierStart = FindStartIndex(referenceIndex) - 1;
                                    }

                                    forLoop.ContinueOffset = Function.Operations[modifierStart].OpCodeOffset;
                                    VerboseCondition($"[{Function.Name}] Marking for loop [Continue:0x{forLoop.ContinueOffset:X4},Start:0x{forLoop.StartOffset:X4},End:0x{forLoop.EndOffset:X4}]", UseForLoopVerbose);
                                    for (int j = modifierStart; j < Function.Operations.Count && Function.Operations[j].OpCodeOffset < whileLoop.EndOffset; j++)
                                    {
                                        // We've now visited/processed this
                                        Function.Operations[j].Visited = true;
                                        var op = Function.Operations[j];

                                        if (FoundForLoopModifier(op, forLoop))
                                        {
                                            break;
                                        }
                                    }

                                    for (int j = variableBeginIndex; j < index; j++)
                                    {
                                        Function.Operations[j].Visited = true;
                                    }
                                    Blocks[i] = forLoop;
                                    b_revert = false;
                                }
                            }
                        }
                        catch
                        {

                        }
                        

                        // we have to revert because the for loop detection messes up so many things
                        if(b_revert)
                        {
                            foreach(var kvp in ProcessedSnapshot)
                            {
                                Function.Operations[kvp.Key].Visited = kvp.Value;
                            }
                            ProcessedSnapshot.Clear();
                        }
                    }
                }
            }
        }

        private bool FoundForLoopModifier(ScriptOp op, ForLoopBlock loop)
        {
            switch (op.Metadata.OpType)
            {
                case ScriptOpType.SetVariable:
                    {
                        switch(op.Metadata.OpCode)
                        {
                            case ScriptOpCode.SafeSetVariableFieldCached:
                                loop.Modifier = $"{GetLocalVariable((int)op.Operands[0].Value)}.{op.Operands[1].Value} = {Stack.Pop()};";
                                break;
                            case ScriptOpCode.SetArrayField:
                                loop.Modifier = $"{CurrentReference += "[" + Stack.Pop() + "]"} = {Stack.Pop()};";
                                break;
                            case ScriptOpCode.SetGlobalObjectFieldVariable:
                                loop.Modifier = $"{Script.GlobalObjects[op.OpCodeOffset + 2]}.{op.Operands[1].Value} = {Stack.Pop()};";
                                break;
                            case ScriptOpCode.SetLocalVariableCached:
                                loop.Modifier = string.Format("{0} = {1}", LocalVariables[LocalVariables.Count - (int)op.Operands[0].Value - 1], Stack.Pop());
                                break;
                            case ScriptOpCode.SetVariableFieldRef:
                                loop.Modifier = string.Format("{0} = {1}", $"{CurrentReference}.{op.Operands[0].Value}", Stack.Pop());
                                break;
                            default:
                                loop.Modifier = string.Format("{0} = {1}", CurrentReference, Stack.Pop());
                                CurrentReference = "";
                                break;
                        }
                        return true;
                    }
                case ScriptOpType.SingleOperand:
                    {
                        switch (op.Metadata.OpCode)
                        {
                            case ScriptOpCode.Dec:
                                loop.Modifier = string.Format("{0}--", CurrentReference);
                                return true;
                            case ScriptOpCode.Inc:
                                loop.Modifier = string.Format("{0}++", CurrentReference);
                                return true;
                            case ScriptOpCode.IncCached:
                                loop.Modifier = string.Format("{0}++", LocalVariables[LocalVariables.Count - (int)op.Operands[0].Value - 1]);
                                return true;
                            case ScriptOpCode.DecCached:
                                loop.Modifier = string.Format("{0}--", LocalVariables[LocalVariables.Count - (int)op.Operands[0].Value - 1]);
                                return true;
                        }

                        return false;
                    }
                default:
                    object refOut = null;
                    ProcessInstruction(op, ref refOut);
                    return false;
            }
        }

        /// <summary>
        /// Attempts to find the start of the given statement, etc. by looping until we hit 
        /// a non-stack operation
        /// </summary>
        /// <param name="index"></param>
        /// <returns></returns>
        private int FindStartIndexEx(int index)
        {
            // Attempt to hit a comparison to this
            for (int j = index; j >= 0; j--)
            {
                var op = Function.Operations[j];

                // Check if we hit a non-stack/expression operation
                if (IsStackOperation(op) || op.Metadata.OpType == ScriptOpType.JumpExpression)
                {
                    continue;
                }

                // We add 1 since we want the next operation, this one is not what we want
                return j + 1;
            }

            return -1;
        }

        private int FindStartIndexTernary(int index, int EndExpression, bool IsTwoOperandExpression)
        {
            VerboseCondition($"[{Function.Name}]: Looking at Ternary, Possible Start 0x{Function.Operations[index].OpCodeOffset:X4}", UseTernaryLogging);
            int j = index;
            int StackSizeCached = Stack.Count; // cache the stack size
            Dictionary<int, int> StackBaseMap = new Dictionary<int, int>();
            for (; j >= 0; j--)
            {
                var op = Function.Operations[j];
                if(op.IsTernary) continue; // walk to start of previous ternary
                if (op.Visited) break;
                if(op.Metadata.OpType == ScriptOpType.JumpExpression)
                {
                    int targetSpot = Script.GetJumpLocation(
                            op.OpCodeOffset + op.OpCodeSize,
                            (int)op.Operands[0].Value);
                    if (targetSpot >= EndExpression)
                        break;
                }
                if (IsStackOperation(op)) continue;
                break;
            }

            if(j++ < 0)
                return -1;

            int start = j;
            for (int i = j; i <= index; i++)
            {
                var op = Function.Operations[i];

                if (op.IsTernary)
                {
                    var t = FindTernaryAt(op.OpCodeOffset);

                    if (t == null)
                        throw new Exception("Operation is ternary, but the nested ternary start index decompiler is a 5head");

                    if (!t.Visited)
                    {
                        if (t.PushVal != null) Stack.Push(t.PushVal);
                        Stack.Push(t.GetHeader());
                    }

                    VerboseCondition($"<Nested Ternary:{t.StartOffset:X4} - {t.EndOffset:X4}> Stack Size: {Stack.Count}", UseTernaryLogging);
                    StackBaseMap[i] = Stack.Count; // log the stack size at this instruction
                    i = GetInstructionAt(t.EndOffset) - 1;
                    continue;
                }

                object refOut = null;
                ProcessInstruction(op, ref refOut);
                VerboseCondition($"{op.Metadata.OpCode} Stack Size: {Stack.Count}", UseTernaryLogging);
                StackBaseMap[i] = Stack.Count; // log the stack size at this instruction
            }

            // reset this
            for (int i = start; i <= index; i++)
                Function.Operations[i].Visited = false;

            // now we need to check for excess stack
            int k = FindLowMatchStackBaseIndex(StackBaseMap, index);
            int tval = StackBaseMap[k];

            if (IsTwoOperandExpression) tval--;

            var next = FindLowMatchStackBaseIndex(StackBaseMap, k - 1);
            VerboseCondition($"Start Stack Inspection: {next:X3}, tval: {tval}", UseTernaryLogging);
            if (next == -1 || StackBaseMap[k] < tval) // determine where the stack needs to end
            {
                j = k;
                goto cleanup;
            }

            k--;

            while (((next = FindLowMatchStackBaseIndex(StackBaseMap, k)) != -1) && StackBaseMap[k = next] >= tval)
            {
                k--;
                if (((next = FindLowMatchStackBaseIndex(StackBaseMap, k)) == -1))
                    break;
                if(Function.Operations[k = next].Metadata.OpType == ScriptOpType.JumpExpression && !Function.Operations[k = next].IsTernary)
                {
                    tval = StackBaseMap[k];
                    continue;
                }
            }

            j = k = FindHighMatchStackBaseIndex(StackBaseMap, k + 1);

            cleanup:
            // and cleanup
            while (Stack.Count > StackSizeCached)
                Stack.Pop();

            VerboseCondition($"End Ternary, expecting opcode: {(j >= 0 ? Function.Operations[j].OpCodeOffset.ToString("X4") : "No Operation")}", UseTernaryLogging);
            return j;
        }

        /// <summary>
        /// given k, finds either k or the next lowest value in the base map
        /// </summary>
        /// <param name="StackBaseMap"></param>
        /// <param name="k"></param>
        /// <returns></returns>
        private int FindLowMatchStackBaseIndex(Dictionary<int, int> StackBaseMap, int k)
        {
            if (StackBaseMap.ContainsKey(k))
                return k;

            var results = StackBaseMap.Where((x) => x.Key < k).ToArray();
            
            if (results.Length < 1)
                return -1;

            int Highest = -1;

            foreach (var result in results)
                if (result.Key > Highest)
                    Highest = result.Key;

            return Highest;
        }

        /// <summary>
        /// given k, finds either k or the next highest value in the base map
        /// </summary>
        /// <param name="StackBaseMap"></param>
        /// <param name="k"></param>
        /// <returns></returns>
        private int FindHighMatchStackBaseIndex(Dictionary<int, int> StackBaseMap, int k)
        {
            if (StackBaseMap.ContainsKey(k))
                return k;

            var results = StackBaseMap.Where((x) => x.Key > k).ToArray();

            if (results.Length < 1)
                return -1;

            int Lowest = 0x7FFFFFFF;

            foreach (var result in results)
                if (result.Key < Lowest)
                    Lowest = result.Key;

            return Lowest;
        }

        /// <summary>
        /// Attempts to find the start of the given statement, etc. by looping until we hit 
        /// a non-stack operation
        /// </summary>
        /// <param name="index"></param>
        /// <returns></returns>
        private int FindStartIndex(int index, bool referenceAllowed = false)
        {
            // Attempt to hit a comparison to this
            for (int j = index; j >= 0; j--)
            {
                var op = Function.Operations[j];

                // For some statements we can count the reference
                if(referenceAllowed && op.Metadata.OpType == ScriptOpType.VariableReference)
                {
                    continue;
                }

                // Check if we hit a non-stack/expression operation
                if (IsStackOperation(op) || op.Metadata.OpType == ScriptOpType.JumpExpression)
                {
                    continue;
                }

                // We add 1 since we want the next operation, this one is not what we want
                return j + 1;
            }

            return -1;
        }

        /// <summary>
        /// Attempts to find the start of the given statement, etc. by looping until we hit 
        /// a non-stack operation
        /// </summary>
        /// <param name="index"></param>
        /// <returns></returns>
        private int FindLoopModifierBegin(int index)
        {
            // Attempt to hit a comparison to this
            for (int j = index; j >= 0; j--)
            {
                var op = Function.Operations[j];

                // Check if we hit a non-stack/expression operation
                if (IsStackOperation(op) && op.Metadata.OpType == ScriptOpType.JumpExpression)
                {
                    continue;
                }

                // We add 1 since we want the next operation, this one is not what we want
                return j - 1;
            }

            return -1;
        }

        private string GetLocalVariable(int index)
        {
            return LocalVariables[LocalVariables.Count + ~index];
        }

        private (string, string) GetVariableNames(ScriptOp op)
        {
            return (LocalVariables[LocalVariables.Count + ~(int)op.Operands[0].Value], LocalVariables[LocalVariables.Count + ~(int)op.Operands[0].Value]);
        }

        private string GetVariableName(ScriptOp op, bool bare = false)
        {
            switch(op.Metadata.OpCode)
            {
                case ScriptOpCode.DiscardWaittillVariableField:
                    return "$_";
                case ScriptOpCode.EvalLocalArrayCached:
                    return $"{LocalVariables[LocalVariables.Count + ~(int)op.Operands[0].Value]}[{Stack.Peek()}]";
                case ScriptOpCode.EvalGlobalObjectFieldVariableRef:
                    return $"{Script.GlobalObjects[op.OpCodeOffset + 2]}.{op.Operands[1].Value}";
                case ScriptOpCode.IncCached:
                case ScriptOpCode.DecCached:
                case ScriptOpCode.SetLocalVariableCached:
                case ScriptOpCode.SetNextArrayKeyCached:
                case ScriptOpCode.SetWaittillVariableFieldCached:
                case ScriptOpCode.EvalLocalVariableCached:
                case ScriptOpCode.EvalLocalVariableCached2:
                case ScriptOpCode.EvalLocalVariableRefCached2:
                case ScriptOpCode.EvalLocalVariableRefCached:
                case ScriptOpCode.EvalLocalArrayRefCached:
                case ScriptOpCode.SafeSetVariableFieldCached:
                    return GetLocalVariable((int)op.Operands[0].Value);
                case ScriptOpCode.EvalFieldVariable:
                case ScriptOpCode.EvalFieldVariableRef:
                    return CurrentReference + "." + (string)op.Operands[0].Value;
                case ScriptOpCode.EvalLevelFieldVariable:
                case ScriptOpCode.EvalLevelFieldVariableRef:
                    return "level." + (string)op.Operands[0].Value;
                case ScriptOpCode.EvalSelfFieldVariable:
                case ScriptOpCode.EvalSelfFieldVariableRef:
                    if(Function.IsClassFunction)
                    {
                        return (string)op.Operands[0].Value;
                    }
                    return "self." + (string)op.Operands[0].Value;
                case ScriptOpCode.EvalLocalVariableDefined:
                    if(bare) return LocalVariables[LocalVariables.Count + ~(int)op.Operands[0].Value];
                    return $"isdefined({LocalVariables[LocalVariables.Count + ~(int)op.Operands[0].Value]})";
                case ScriptOpCode.EvalGlobalObjectFieldVariable:
                    return $"{Script.GlobalObjects[op.OpCodeOffset + 2]}.{op.Operands[1].Value}";
                case ScriptOpCode.CastAndEvalFieldVariable:
                    if (Stack.Count < 1)
                        return $"?.{op.Operands[0].Value}";
                    return $"{Stack.Peek()}.{op.Operands[0].Value}";
                case ScriptOpCode.SetArrayField:
                    return CurrentReference + "[" + Stack.Peek() + "]";
                case ScriptOpCode.EvalFieldVariableRefCached:
                case ScriptOpCode.EvalFieldVariableCached:
                    return $"{GetLocalVariable((int)op.Operands[0].Value)}.{op.Operands[1].Value}";
                default:
                    throw new ArgumentException($"Invalid Op Code for GetVariableName '{op.Metadata.OpCode}'");
            }
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="offset"></param>
        /// <returns></returns>
        private int GetBlockIndexAt(int offset)
        {
            return Blocks.FindIndex(x => x.StartOffset == offset);
        }


        /// <summary>
        /// 
        /// </summary>
        /// <param name="offset"></param>
        /// <returns></returns>
        private int GetInstructionAt(int offset)
        {
            return Function.Operations.FindIndex(x => x.OpCodeOffset == offset);
        }

        private bool IsStackOperation(ScriptOp op)
        {
            if (op.IsTernary)
                return true;

            if (op.Metadata.OpType == ScriptOpType.StackPush ||
                op.Metadata.OpType == ScriptOpType.Variable ||
                op.Metadata.OpType == ScriptOpType.Call ||
                op.Metadata.OpType == ScriptOpType.Array ||
                op.Metadata.OpType == ScriptOpType.DoubleOperand ||
                op.Metadata.OpType == ScriptOpType.Comparison ||
                op.Metadata.OpType == ScriptOpType.SizeOf ||
                op.Metadata.OpType == ScriptOpType.JumpExpression ||
                op.Metadata.OpCode == ScriptOpCode.PreScriptCall ||
                op.Metadata.OpCode == ScriptOpCode.SizeOf ||
                op.Metadata.OpCode == ScriptOpCode.BoolNot ||
                op.Metadata.OpType == ScriptOpType.Object ||
                op.Metadata.OpType == ScriptOpType.VariableReference ||
                op.Metadata.OpType == ScriptOpType.Cast || 
                op.Metadata.OpType == ScriptOpType.DoubleOperand ||
                op.Metadata.OpCode == ScriptOpCode.Bit_Not ||
                op.Metadata.OpCode == ScriptOpCode.AddToArray ||
                op.Metadata.OpCode == ScriptOpCode.AddToStruct
                )
            {
                if (op.Metadata.OpCode != ScriptOpCode.Wait && 
                    op.Metadata.OpCode != ScriptOpCode.PixBeginEvent && op.Metadata.OpCode != ScriptOpCode.PixEndEvent &&
                    op.Metadata.OpCode != ScriptOpCode.WaitRealTime && op.Metadata.OpCode != ScriptOpCode.WaitFrame)
                {
                    return true;
                }
            }
            
            return false;
        }

        private void MarkUnkJumps()
        {
            foreach(var op in Function.Operations)
            {
                if (op.Metadata.OpCode != ScriptOpCode.Jump) continue;
                var jumpLoc = Script.GetJumpLocation(
                            op.OpCodeOffset + op.OpCodeSize,
                            (int)op.Operands[0].Value);

                if (IsBreak(op.OpCodeOffset, jumpLoc) || IsContinue(op.OpCodeOffset, jumpLoc))
                {
                    continue;
                }
                if (op.Visited) continue;
                JumpLocations.Add(jumpLoc);
            }
        }

        private string ConvertReturn(string ret)
        {
            return IsBoolReturn ? (ret == "0" ? "false" : "true") : ret;
        }

        /// <summary>
        /// Processes the given instructions and returns true if the instruction emission was canceled
        /// </summary>
        private bool ProcessInstruction(ScriptOp operation, ref object refOut, DecompilerBlock block = null)
        {
            if(operation.Metadata.OpCode == ScriptOpCode.Invalid)
            {
                throw new Exception("Function contains invalid OpCode.");
            }

            if(JumpLocations.Contains(operation.OpCodeOffset))
                Writer?.WriteLine($"loc_{operation.OpCodeOffset:X8}:");

            switch (operation.Metadata.OpType)
            {
                case ScriptOpType.Return:
                    {
                        if (operation.Metadata.OpCode == ScriptOpCode.End)
                        {
                            Writer?.WriteLine("return;");

                        }
                        else
                        {
                            Writer?.WriteLine("return {0};", ConvertReturn(Stack.Pop()));
                        }

                        return false;
                    }
                case ScriptOpType.StackPop:
                    {
                        string result = Stack.Pop();
                        
                        //if(result.Contains(" new "))
                        //{
                        //    string new_sub = result.Substring(result.IndexOf("new"));
                        //    new_sub = new_sub.Substring(0, new_sub.IndexOf(")") + 1);
                        //    Writer?.WriteLine("object = {0};", new_sub);
                        //    Writer?.WriteLine("{0};", result.Replace(new_sub, "object"));
                        //    Stack.Push("object");
                        //    break;
                        //}

                        Writer?.WriteLine("{0};", result);
                        break;
                    }
                case ScriptOpType.SizeOf:
                    {
                        Stack.Push(Stack.Pop() + ".size");
                        break;
                    }
                case ScriptOpType.Jump:
                    {
                        var jumpLoc = Script.GetJumpLocation(
                            operation.OpCodeOffset + operation.OpCodeSize,
                            (int)operation.Operands[0].Value);

                        if (IsBreak(operation.OpCodeOffset, jumpLoc))
                        {
                            Writer?.WriteLine("break;");
                        }
                        else if(IsContinue(operation.OpCodeOffset, jumpLoc))
                        {
                            Writer?.WriteLine("continue;");
                        }
                        else if(jumpLoc == Function.Operations[Function.Operations.Count - 1].OpCodeOffset)
                        {
                            Writer?.WriteLine("return;");
                        }
                        else
                        {
                            Verbose($"[{Function.Name}] found a jump condition [0x{operation.OpCodeOffset:X4}:0x{jumpLoc:X4}] which does not suit the conditions of break or continue");
                            Writer?.WriteLine("jump loc_{0};", jumpLoc.ToString("X8"));
                            if(!JumpLocations.Contains(jumpLoc))
                            {
                                JumpLocations.Add(jumpLoc);
                            }
                        }

                        return false;
                    }
                case ScriptOpType.JumpExpression:
                    {
                        string tmp = Stack.Pop() + (operation.Metadata.OpCode == ScriptOpCode.JumpOnFalseExpr ? " && " : " || ");
                        Stack.Push(tmp + BuildExpression(operation));
                        break;
                    }
                case ScriptOpType.ObjectReference:
                    {
                        switch (operation.Metadata.OpCode)
                        {
                            case ScriptOpCode.GetGameRef:
                                CurrentReference = "game";
                                break;
                        }
                        break;
                    }
                case ScriptOpType.StackPush:
                    {
                        // If we have no operands we shall push what needs to be pushed
                        if (operation.Metadata.OperandType == ScriptOperandType.None)
                        {
                            // For these we just need to manually give it
                            // what the source equivilent would be
                            switch (operation.Metadata.OpCode)
                            {
                                case ScriptOpCode.GetUndefined:
                                    Stack.Push("undefined");
                                    break;
                                case ScriptOpCode.GetZero:
                                    Stack.Push("0");
                                    break;
                                case ScriptOpCode.GetSelf:
                                    Stack.Push("self");
                                    break;
                                case ScriptOpCode.GetLevel:
                                    Stack.Push("level");
                                    break;
                                case ScriptOpCode.GetGame:
                                    Stack.Push("game");
                                    break;
                                case ScriptOpCode.GetAnim:
                                    Stack.Push("anim");
                                    break;
                                case ScriptOpCode.GetWorld:
                                    Stack.Push("world");
                                    break;
                                case ScriptOpCode.GetEmptyArray:
                                    Stack.Push("[]");
                                    break;
                                case ScriptOpCode.Vector:
                                    Stack.Push(string.Format("({0}, {1}, {2})", Stack.Pop(), Stack.Pop(), Stack.Pop()));
                                    break;
                                case ScriptOpCode.CreateStruct:
                                    Stack.Push("{}");
                                    break;
                                case ScriptOpCode.AddToArray:
                                    string toPush = "";
                                    string indexer = Stack.Pop();
                                    string val = Stack.Pop();
                                    string arrVal = Stack.Pop();
                                    if(arrVal[arrVal.Length - 1] != ']')
                                    {
                                        toPush = $"{arrVal}[{indexer}:{val}]";
                                    }
                                    else
                                    {
                                        if (arrVal[arrVal.Length - 2] == '[')
                                            toPush = $"[{indexer}:{val}]";
                                        else
                                        {
                                            toPush = $"{arrVal.Substring(0, arrVal.Length - 1)}, {indexer}:{val}]";
                                        }
                                    }

                                    Stack.Push(toPush);
                                    break;
                                case ScriptOpCode.GetObjectHandle:
                                    Stack.Push($"&{CurrentReference}");
                                    break;
                            }
                        }
                        else
                        {
                            switch (operation.Metadata.OperandType)
                            {
                                case ScriptOperandType.FunctionPointer:
                                    {
                                        var import = Script.GetImport(operation.OpCodeOffset);

                                        var functionName = import.Name;

                                        // Check if we can omit the namespace, if it's the same as this, otherwise we need to add it
                                        if (!string.IsNullOrWhiteSpace(import.Namespace) && import.Namespace != Function.Namespace)
                                        {
                                            functionName = import.Namespace + "::" + functionName;
                                        }

                                        Stack.Push("&" + functionName);
                                        break;
                                    }
                                default:
                                    {
                                        if(operation.Metadata.OpCode == ScriptOpCode.GetGlobalObject)
                                        {
                                            Stack.Push(Script.GlobalObjects[operation.OpCodeOffset + 2]);
                                            break;
                                        }
                                        
                                        if (operation.Metadata.OpCode == ScriptOpCode.GetGlobalObjectRef)
                                        {
                                            CurrentReference = Script.GlobalObjects[operation.OpCodeOffset + 2];
                                            break;
                                        }

                                        if (operation.Metadata.OpCode == ScriptOpCode.GetObjectType)
                                        {
                                            Stack.Push($"new {operation.Operands[0].Value.ToString().Replace("var_", "class_")}()");
                                            break;
                                        }

                                        if(operation.Metadata.OpCode == ScriptOpCode.LazyGetFunction)
                                        {
                                            Stack.Push(operation.Operands[0].Value.ToString());
                                            break;
                                        }

                                        if(operation.Metadata.OpCode == ScriptOpCode.GetLocalFunction)
                                        {
                                            using (var decompiler = new Decompiler(Function.LocalFunctions[operation], Script, Writer.Indent))
                                            {
                                                Stack.Push(decompiler.GetWriterOutput().Trim());
                                            }
                                            break;
                                        }

                                        // We have a value
                                        Stack.Push(operation.Operands[0].Value.ToString());
                                        break;
                                    }
                            }
                        }
                        break;
                    }
                case ScriptOpType.Object:
                    {
                        switch (operation.Metadata.OpCode)
                        {
                            case ScriptOpCode.GetSelfObject:
                                CurrentReference = "self";
                                break;
                            case ScriptOpCode.GetLevelObject:
                                CurrentReference = "level";
                                break;
                            case ScriptOpCode.GetAnimObject:
                                CurrentReference = "anim";
                                break;
                            case ScriptOpCode.GetWorldObject:
                                CurrentReference = "world";
                                break;
                            case ScriptOpCode.GetClassesObject:
                                CurrentReference = "classes";
                                break;
                            case ScriptOpCode.CastFieldObject:
                                CurrentReference = Stack.Pop();
                                break;
                        }
                        break;
                    }
                case ScriptOpType.ClearVariable:
                    {
                        switch (operation.Metadata.OpCode)
                        {
                            case ScriptOpCode.ClearFieldVariable:
                                {
                                    Writer?.WriteLine("{0}.{1} = undefined;", CurrentReference, operation.Operands[0].Value);
                                    break;
                                }
                            case ScriptOpCode.ClearFieldVariableOnStack:
                                {
                                    Writer?.WriteLine("{0}.({1}) = undefined;", CurrentReference, Stack.Pop());
                                    break;
                                }
                            case ScriptOpCode.ClearArray:
                                {
                                    Writer?.WriteLine("{0}[{1}] = undefined;", CurrentReference, Stack.Pop());
                                    CurrentReference = "";
                                    break;
                                }
                        }
                        break;
                    }
                case ScriptOpType.Comparison:
                    {
                        var right = Stack.Pop();
                        var left = Stack.Pop();
                        if(hasOperatorInString(right))
                        {
                            right = $"({right})";
                        }
                        if (hasOperatorInString(left))
                        {
                            left = $"({left})";
                        }
                        Stack.Push(string.Format("{0}{1}{2}", left, Operators[operation.Metadata.OpCode], right));
                        break;
                    }
                case ScriptOpType.DoubleOperand:
                    {
                        var right = Stack.Pop();
                        var left = Stack.Pop();
                        if (hasOperatorInString(right))
                        {
                            right = $"({right})";
                        }
                        if (hasOperatorInString(left))
                        {
                            left = $"({left})";
                        }
                        Stack.Push(string.Format("{0}{1}{2}", left, Operators[operation.Metadata.OpCode], right));
                        break;
                    }
                case ScriptOpType.SingleOperand:
                    {
                        switch (operation.Metadata.OpCode)
                        {
                            case ScriptOpCode.Dec:
                                {
                                    Writer?.WriteLine("" + CurrentReference + "--;");
                                    CurrentReference = "";
                                    break;
                                }
                            case ScriptOpCode.Inc:
                                {
                                    Writer?.WriteLine("" + CurrentReference + "++;");
                                    CurrentReference = "";
                                    break;
                                }
                            case ScriptOpCode.Bit_Not:
                                {
                                    var right = Stack.Pop();
                                    string fmt = hasOperatorInString(right) ? "~({0})" : "~{0}";
                                    Stack.Push(string.Format(fmt, right));
                                    break;
                                }
                            case ScriptOpCode.IncCached:
                                {
                                    Writer?.WriteLine(LocalVariables[LocalVariables.Count + ~(int)operation.Operands[0].Value] + "++;");
                                    break;
                                }
                            case ScriptOpCode.DecCached:
                                {
                                    Writer?.WriteLine(LocalVariables[LocalVariables.Count + ~(int)operation.Operands[0].Value] + "--;");
                                    break;
                                }
                        }
                        break;
                    }
                case ScriptOpType.Cast:
                    {
                        switch (operation.Metadata.OpCode)
                        {
                            case ScriptOpCode.BoolNot:
                                {
                                    var value = Stack.Pop();

                                    if(value.Contains("&&") || value.Contains("||") || value.Contains(">") || value.Contains("<"))
                                    {
                                        value = "(" + value + ")";
                                    }

                                    Stack.Push(string.Format("!{0}", value));
                                    break;
                                }
                        }
                        break;
                    }
                case ScriptOpType.Call:
                    {
                        // Store here as we'll resolve the method type
                        string functionName;
                        int paramCount;
                        int threaded = 0;
                        bool method = false;
                        bool specialDoNotEmit = false;

                        switch (operation.Metadata.OpCode)
                        {
                            case ScriptOpCode.Breakpoint:
                                Writer?.WriteLine("__debugbreak();");
                                return true;
                            case ScriptOpCode.ScriptFunctionCallPointer:
                            case ScriptOpCode.ScriptMethodCallPointer:
                            case ScriptOpCode.ScriptThreadCallPointer:
                            case ScriptOpCode.ScriptMethodThreadCallPointer:
                            case ScriptOpCode.ScriptThreadCallPointer2:
                            case ScriptOpCode.ScriptMethodThreadCallPointer2:
                                {
                                    // Pointers are wrapped
                                    functionName = "[[" + Stack.Pop() + "]]";
                                    paramCount = (int)operation.Operands[0].Value;

                                    // Check for thread calls
                                    if (
                                        operation.Metadata.OpCode == ScriptOpCode.ScriptThreadCallPointer ||
                                        operation.Metadata.OpCode == ScriptOpCode.ScriptMethodThreadCallPointer ||
                                        operation.Metadata.OpCode == ScriptOpCode.ScriptThreadCallPointer2 ||
                                        operation.Metadata.OpCode == ScriptOpCode.ScriptMethodThreadCallPointer2)
                                    {
                                        threaded = 1 + (operation.Metadata.OpCode == ScriptOpCode.ScriptThreadCallPointer2 || operation.Metadata.OpCode == ScriptOpCode.ScriptMethodThreadCallPointer2 ? 1 : 0);
                                    }

                                    // Check for method calls
                                    if (
                                    operation.Metadata.OpCode == ScriptOpCode.ScriptMethodCallPointer ||
                                    operation.Metadata.OpCode == ScriptOpCode.ScriptMethodThreadCallPointer ||
                                    operation.Metadata.OpCode == ScriptOpCode.ScriptMethodThreadCallPointer2)
                                    {
                                        method = true;
                                    }
                                    break;
                                }
                            case ScriptOpCode.CallBuiltin:
                            case ScriptOpCode.CallBuiltinMethod:
                            case ScriptOpCode.ScriptFunctionCall:
                            case ScriptOpCode.ScriptMethodCall:
                            case ScriptOpCode.ScriptMethodThreadCall2:
                            case ScriptOpCode.ScriptThreadCall2:
                            case ScriptOpCode.ScriptMethodThreadCall:
                            case ScriptOpCode.ScriptThreadCall:
                                {
                                    var functionImport = Script.GetImport(operation.OpCodeOffset);

                                    functionName = functionImport.Name;
                                    paramCount = functionImport.ParameterCount;

                                    if(functionName == "__constructor" || functionName == "__destructor")
                                    {
                                        specialDoNotEmit = true;
                                        break;
                                    }

                                    // Check if we can omit the namespace, if it's the same as this, otherwise we need to add it
                                    if (!string.IsNullOrWhiteSpace(functionImport.Namespace) && functionImport.Namespace != Function.Namespace)
                                    {
                                        functionName = functionImport.Namespace + "::" + functionName;
                                    }

                                    // Check for thread calls
                                    if (
                                        operation.Metadata.OpCode == ScriptOpCode.ScriptThreadCall ||
                                        operation.Metadata.OpCode == ScriptOpCode.ScriptMethodThreadCall ||
                                        operation.Metadata.OpCode == ScriptOpCode.ScriptThreadCall2 ||
                                        operation.Metadata.OpCode == ScriptOpCode.ScriptMethodThreadCall2)
                                    {
                                        threaded = 1 + (operation.Metadata.OpCode == ScriptOpCode.ScriptThreadCall2 || operation.Metadata.OpCode == ScriptOpCode.ScriptMethodThreadCall2 ? 1 : 0);
                                    }

                                    // Check for method calls
                                    if (
                                    operation.Metadata.OpCode == ScriptOpCode.ScriptMethodCall ||
                                    operation.Metadata.OpCode == ScriptOpCode.CallBuiltinMethod || 
                                    operation.Metadata.OpCode == ScriptOpCode.ScriptMethodThreadCall || 
                                    operation.Metadata.OpCode == ScriptOpCode.ScriptMethodThreadCall2)
                                    {
                                        method = true;
                                    }
                                    break;
                                }
                            case ScriptOpCode.ClassFunctionThreadCall:
                            case ScriptOpCode.ClassFunctionThreadCall2:
                            case ScriptOpCode.ClassFunctionCall:
                                {
                                    functionName = (string)operation.Operands[0].Value;
                                    paramCount = (int)operation.Operands[1].Value;
                                    if (functionName == "__constructor")
                                    {
                                        specialDoNotEmit = true;
                                    }
                                    break;
                                }
                            default:
                                {
                                    // Everything else take from the instruction table
                                    var opFunc = InstructionFunctions[operation.Metadata.OpCode];
                                    functionName = opFunc.Item1;
                                    paramCount = opFunc.Item2;
                                    if(functionName == "getfirstarraykeycached")
                                    {
                                        var variableindex = (int)operation.Operands[0].Value;
                                        Stack.Push(GetLocalVariable(variableindex));
                                    }
                                    break;
                                }
                        }

                        if(specialDoNotEmit)
                        {
                            refOut = true;
                            return false;
                        }

                        paramCount = Math.Min(paramCount, Stack.Count + (method ? -1 : 0));
                        Verbose($"[{Function.Name}] log call at 0x{operation.OpCodeOffset:X6} with {paramCount} params, stack size: {Stack.Count}");
                        // Push the call as it's basically a stack item
                        // wait is not pushed, it's technically not a call
                        if (operation.Metadata.OpCode == ScriptOpCode.Wait || operation.Metadata.OpCode == ScriptOpCode.WaitRealTime || 
                            operation.Metadata.OpCode == ScriptOpCode.WaitFrame || 
                            operation.Metadata.OpCode == ScriptOpCode.PixBeginEvent || operation.Metadata.OpCode == ScriptOpCode.PixEndEvent)
                        {
                            Writer?.WriteLine("{0};", GenerateFunctionCall(functionName, paramCount, threaded, method));
                        }
                        else if(operation.Metadata.OpCode == ScriptOpCode.ClassFunctionCall)
                        {
                            string caller = Stack.Pop();
                            string front_half = GenerateFunctionCall(functionName, paramCount, threaded, method);
                            Stack.Push($"[[ {caller} ]]->{front_half}");
                        }
                        else if(operation.Metadata.OpCode == ScriptOpCode.ClassFunctionThreadCall || operation.Metadata.OpCode == ScriptOpCode.ClassFunctionThreadCall2)
                        {
                            string caller = Stack.Pop();
                            string front_half = GenerateFunctionCall(functionName, paramCount, threaded, method);
                            Stack.Push($"{(operation.Metadata.OpCode == ScriptOpCode.ClassFunctionThreadCall2 ? "childthread" : "thread")} [[ {caller} ]]->{front_half}");
                        }
                        else
                        {
                            Stack.Push(GenerateFunctionCall(functionName, paramCount, threaded, method));
                        }

                        break;
                    }
                case ScriptOpType.Notification:
                    {
                        switch (operation.Metadata.OpCode)
                        {
                            case ScriptOpCode.EndonCallbackA:
                            case ScriptOpCode.EndOnCallback:
                            case ScriptOpCode.EndOn:
                                {
                                    #region VM1C (T7)
                                    if (Script.Header.VMRevision == 0x1C)
                                    {
                                        Writer?.WriteLine("{0} endon({1});", Stack.Pop(), Stack.Pop());
                                        break;
                                    }
                                    #endregion
                                    #region VM37
                                    Writer?.Write($"{Stack.Pop()} {(InstructionFunctions[operation.Metadata.OpCode].Item1)}(");
                                    List<string> nArgs = new List<string>();
                                    for (byte b = 0; b < byte.Parse(operation.Operands[0].Value.ToString()); b++) nArgs.Add(Stack.Pop());
                                    Writer?.Write(string.Join(", ", nArgs));
                                    Writer?.WriteLine(");");
                                    #endregion
                                    break;
                                }
                            case ScriptOpCode.Notify:
                                {
                                    Writer?.Write("{0} notify(", Stack.Pop());

                                    List<string> nArgs = new List<string>();
                                    while(Stack.Count > 0) nArgs.Add(Stack.Pop());

                                    Writer?.Write(string.Join(", ", nArgs));

                                    Writer?.WriteLine(");");

                                    break;
                                }
                            case ScriptOpCode.WaitTillMatch:
                                {
                                    #region VM1C (T7)
                                    if (Script.Header.VMRevision == 0x1C)
                                    {
                                        Writer.Write("{0} {1}({2}", Stack.Pop(), "waittillmatch", Stack.Pop());
                                        // Parse the variables created by a waittill
                                        var index = GetInstructionAt(operation.OpCodeOffset) + 1;
                                        while (Function.Operations[index].Metadata.OpCode == ScriptOpCode.SetWaittillVariableFieldCached || Function.Operations[index].Metadata.OpCode == ScriptOpCode.DiscardWaittillVariableField)
                                        {
                                            Writer.Write(", {0}", GetVariableName(Function.Operations[index]));
                                            index++;
                                        }
                                        Writer.WriteLine(");");
                                        break;
                                    }
                                    #endregion
                                    #region VM37
                                    string pushOp = "";
                                    pushOp += $"{Stack.Pop()} waittillmatch(";
                                    List<string> nArgs = new List<string>();
                                    for (byte b = 0; b < byte.Parse(operation.Operands[0].Value.ToString()); b++) nArgs.Add(Stack.Pop());
                                    pushOp += string.Join(", ", nArgs);
                                    pushOp += ")";
                                    Stack.Push(pushOp);
                                    break;
                                    #endregion
                                }
                            case ScriptOpCode.WaittillTimeoutS:
                            case ScriptOpCode.WaittillTimeout:
                            case ScriptOpCode.WaitTill:
                                {
                                    #region VM1C (T7)
                                    if (Script.Header.VMRevision == 0x1C)
                                    {
                                        Writer.Write("{0} {1}({2}", Stack.Pop(), (operation.Metadata.OpCode == ScriptOpCode.WaitTill ? "waittill" : "waittilltimeout"), Stack.Pop());
                                        // Parse the variables created by a waittill
                                        var index = GetInstructionAt(operation.OpCodeOffset) + 1;
                                        while (Function.Operations[index].Metadata.OpCode == ScriptOpCode.SetWaittillVariableFieldCached || Function.Operations[index].Metadata.OpCode == ScriptOpCode.DiscardWaittillVariableField)
                                        {
                                            Writer.Write(", {0}", GetVariableName(Function.Operations[index]));
                                            index++;
                                        }
                                        Writer.WriteLine(");");
                                        break;
                                    }
                                    #endregion
                                    #region VM37
                                    string pushOp = "";
                                    pushOp += $"{Stack.Pop()} {(operation.Metadata.OpCode == ScriptOpCode.WaitTill ? "waittill" : "waittilltimeout")}(";
                                    var nArgs = new List<string>();
                                    for (byte b = 0; b < byte.Parse(operation.Operands[0].Value.ToString()); b++) nArgs.Add(Stack.Pop());
                                    pushOp += string.Join(", ", nArgs);
                                    pushOp += ")";
                                    Stack.Push(pushOp);
                                    break;
                                    #endregion
                                }
                            case ScriptOpCode.WaitTillFrameEnd:
                                {
                                    Writer?.WriteLine("waittillframeend();");
                                    break;
                                }
                        }
                        break;
                    }
                case ScriptOpType.Variable:
                    {
                        switch (operation.Metadata.OpCode)
                        {
                            case ScriptOpCode.EvalLocalArrayCached:
                                {
                                    Stack.Push($"{LocalVariables[LocalVariables.Count + ~(int)operation.Operands[0].Value]}[{Stack.Pop()}]");
                                    break;
                                }
                            case ScriptOpCode.EvalLocalVariableCached2:
                            case ScriptOpCode.EvalLocalVariableCached:
                                {
                                    Stack.Push(LocalVariables[LocalVariables.Count + ~(int)operation.Operands[0].Value]);
                                    break;
                                }
                            case ScriptOpCode.EvalFieldVariable:
                                {
                                    Stack.Push(CurrentReference + "." + (string)operation.Operands[0].Value);
                                    break;
                                }
                            // Black Ops 3 merges level/self eval into 1
                            case ScriptOpCode.EvalLevelFieldVariable:
                                {
                                    Stack.Push("level." + (string)operation.Operands[0].Value);
                                    break;
                                }
                            case ScriptOpCode.EvalSelfFieldVariable:
                                {
                                    if(Function.IsClassFunction)
                                    {
                                        Stack.Push((string)operation.Operands[0].Value);
                                    }
                                    else
                                    {
                                        Stack.Push("self." + (string)operation.Operands[0].Value);
                                    }
                                    
                                    break;
                                }
                            case ScriptOpCode.EvalLocalVariableDefined:
                                Stack.Push($"isdefined({LocalVariables[LocalVariables.Count + ~(int)operation.Operands[0].Value]})");
                                break;
                            case ScriptOpCode.EvalGlobalObjectFieldVariable:
                                Stack.Push($"{Script.GlobalObjects[operation.OpCodeOffset + 2]}.{operation.Operands[1].Value}");
                                break;
                            case ScriptOpCode.CastAndEvalFieldVariable:
                                CurrentReference = Stack.Pop();
                                Stack.Push(CurrentReference + "." + (string)operation.Operands[0].Value);
                                break;
                            case ScriptOpCode.EvalFieldVariableOnStack:
                                Stack.Push($"{CurrentReference}.({Stack.Pop()})");
                                break;
                            case ScriptOpCode.EvalLocalVariablesCached:
                                Stack.Push(LocalVariables[LocalVariables.Count + ~(int)operation.Operands[0].Value]);
                                Stack.Push(LocalVariables[LocalVariables.Count + ~(int)operation.Operands[1].Value]);
                                break;
                            case ScriptOpCode.EvalFieldVariableCached:
                                Stack.Push($"{GetLocalVariable((int)operation.Operands[0].Value)}.{operation.Operands[1].Value}");
                                break;
                        }
                        break;
                    }
                case ScriptOpType.VariableReference:
                    {
                        switch (operation.Metadata.OpCode)
                        {
                            case ScriptOpCode.EvalLocalVariableRefCached2:
                            case ScriptOpCode.EvalLocalVariableRefCached:
                                {
                                    CurrentReference = LocalVariables[LocalVariables.Count - (int)operation.Operands[0].Value - 1];
                                    break;
                                }
                            case ScriptOpCode.EvalFieldVariableRef:
                                {
                                    CurrentReference = CurrentReference + "." + (string)operation.Operands[0].Value;
                                    break;
                                }
                            // Black Ops 3 merges level/self eval into 1
                            case ScriptOpCode.EvalLevelFieldVariableRef:
                                {
                                    CurrentReference = "level." + (string)operation.Operands[0].Value;
                                    break;
                                }
                            case ScriptOpCode.EvalSelfFieldVariableRef:
                                {
                                    if (Function.IsClassFunction)
                                    {
                                        CurrentReference = (string)operation.Operands[0].Value;
                                    }
                                    else
                                    {
                                        CurrentReference = "self." + (string)operation.Operands[0].Value;
                                    }
                                    break;
                                }
                            case ScriptOpCode.EvalFieldVariableOnStackRef:
                                {
                                    CurrentReference = $"{CurrentReference}.({Stack.Pop()})";
                                    break;
                                }
                            case ScriptOpCode.EvalGlobalObjectFieldVariableRef:
                                {
                                    CurrentReference = $"{Script.GlobalObjects[operation.OpCodeOffset + 2]}.{operation.Operands[1].Value}";
                                    break;
                                }
                            case ScriptOpCode.EvalFieldVariableRefCached:
                                {
                                    CurrentReference = $"{GetLocalVariable((int)operation.Operands[0].Value)}.{operation.Operands[1].Value}";
                                    break;
                                }
                        }
                        break;
                    }
                case ScriptOpType.Array:
                    {
                        var var = Stack.Pop();
                        var key = Stack.Pop();
                        Stack.Push(var + "[" + key + "]");
                        break;
                    }
                case ScriptOpType.ArrayReference:
                    {
                        CurrentReference += "[" + Stack.Pop() + "]";
                        break;
                    }
                case ScriptOpType.SetVariable:
                    {
                        if (operation.Metadata.OpCode == ScriptOpCode.SetLocalVariableCached)
                            CurrentReference = LocalVariables[LocalVariables.Count - (int)operation.Operands[0].Value - 1];
                        else if (operation.Metadata.OpCode == ScriptOpCode.SetVariableFieldRef)
                        {
                            Writer?.WriteLine("{0} = {1};", $"{CurrentReference}.{operation.Operands[0].Value}", Stack.Pop());
                            break;
                        }
                        else if (operation.Metadata.OpCode == ScriptOpCode.AddToStruct)
                        {
                            string skey = Stack.Pop();
                            string sval = Stack.Pop();
                            string original_struct = Stack.Pop();
                            original_struct = original_struct.Substring(0, original_struct.Length - 1);
                            if (original_struct[original_struct.Length - 1] != '{')
                                original_struct += ", ";
                            Stack.Push($"{original_struct}{skey.Replace("\"", "")}:{sval}}}");
                            break;
                        }
                        else if(operation.Metadata.OpCode == ScriptOpCode.SetNextArrayKeyCached)
                        {
                            Writer?.WriteLine("{0} = GetNextArrayKey({1}, {2});", LocalVariables[LocalVariables.Count - (int)operation.Operands[0].Value - 1], Stack.Pop(), Stack.Pop());
                            break;
                        }
                        else if(operation.Metadata.OpCode == ScriptOpCode.SetGlobalObjectFieldVariable)
                        {
                            CurrentReference = $"{Script.GlobalObjects[operation.OpCodeOffset + 2]}.{operation.Operands[1].Value}";
                        }
                        else if(operation.Metadata.OpCode == ScriptOpCode.SetArrayField)
                        {
                            CurrentReference += "[" + Stack.Pop() + "]";
                        }
                        else if(operation.Metadata.OpCode == ScriptOpCode.SafeSetVariableFieldCached)
                        {
                            CurrentReference = $"{GetLocalVariable((int)operation.Operands[0].Value)}.{operation.Operands[1].Value}";
                        }

                        Writer?.WriteLine("{0} = {1};", CurrentReference, Stack.Pop());
                        break;
                    }
            }

            return true;
        }

        private bool hasOperatorInString(string stackEntry)
        {
            if (stackEntry.Length < 1)
            {
                return false;
            }

            if(stackEntry[0] == '(' && stackEntry[stackEntry.Length - 1] == ')')
            {
                return false;
            }

            if(stackEntry[0] == '-')
            {
                return false;
            }

            for(int i = 0; i < stackEntry.Length; i++)
            {
                switch(stackEntry[i])
                {
                    case '+':
                    case '-':
                    case '/':
                    case '*':
                    case '~':
                    case '|':
                    case '>':
                    case '<':
                    case '%':
                    case '^':
                    case '&':
                        return true;
                }
            }
            return false;
        }

        /// <summary>
        /// Disposes of the internal writers
        /// </summary>
        public void Dispose()
        {
            InternalWriter?.Dispose();
            Writer?.Dispose();
        }

        private void Verbose(object value)
        {
#if DEBUG
            if (DisableVerbose) return;
            Script?.VLog?.Invoke(value);
#endif
        }

        private void VerboseCondition(object value, bool condition)
        {
            if (!condition) return;
            Verbose(value);
        }
    }
}
