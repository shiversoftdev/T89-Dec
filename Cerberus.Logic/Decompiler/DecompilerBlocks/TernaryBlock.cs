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

        public bool IsJumpOnTrue { get; set; }

        /// <summary>
        /// Initializes an if Block
        /// </summary>
        public TernaryBlock(int startOffset, int endOffset) : base(startOffset, endOffset) { RequiresBraces = false; }

        /// <summary>
        /// Gets the header
        /// </summary>
        public override string GetHeader() => string.Format(IsJumpOnTrue ? "({0} ? {2} : {1})" : "({0} ? {1} : {2})", Comparison, TrueCondition, FalseCondition);

        /// <summary>
        /// Gets the footer
        /// </summary>
        public override string GetFooter() => null;
    }
}
