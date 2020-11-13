using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Cerberus.Logic
{
    /// <summary>
    /// A class to hold an if Block
    /// </summary>
    internal class TernaryBlock : DecompilerBlock
    {
        /// <summary>
        /// The comparison being made
        /// </summary>
        public string Comparison { get; set; }

        public string TrueCondition { get; set; }

        public string FalseCondition { get; set; }

        public ScriptOpCode JumpCode { get; set; }

        public TernaryBlock ParentBlock { get; set; } // 3arc makes me want to pepekms sometimes

        /// <summary>
        /// Initializes an if Block
        /// </summary>
        public TernaryBlock(int startOffset, int endOffset) : base(startOffset, endOffset) { RequiresBraces = false; }

        public string PushVal { get; set; }

        /// <summary>
        /// Gets the header
        /// </summary>
        public override string GetHeader() 
        { 
            switch(JumpCode)
            {
                case ScriptOpCode.JumpOnTrue:
                    return string.Format("({0} ? {2} : {1})", Comparison, TrueCondition, FalseCondition);
                case ScriptOpCode.JumpOnFalse:
                    return string.Format("({0} ? {1} : {2})", Comparison, TrueCondition, FalseCondition);
                default:
                    return string.Format("({0} ? {1} : {2})", Comparison, TrueCondition, FalseCondition);
            }
        }

        /// <summary>
        /// Gets the footer
        /// </summary>
        public override string GetFooter() => null;
    }
}
