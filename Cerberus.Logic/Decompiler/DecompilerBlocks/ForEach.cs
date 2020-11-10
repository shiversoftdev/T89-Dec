using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Cerberus.Logic
{
    /// <summary>
    /// A class to hold a For Loop
    /// </summary>
    internal class ForEach : DecompilerBlock
    {
        public string ArrayName { get; set; }

        public string IteratorName { get; set; }

        public string KeyName { get; set; }

        public ForEach(int startOffset, int endOffset) : base(startOffset, endOffset) { }

        public override string GetHeader()
        {
            if(KeyName != null)
                return string.Format("foreach({0}, {1} in {2})", KeyName, IteratorName, ArrayName);

            return string.Format("foreach({0} in {1})", IteratorName, ArrayName);
        }

        /// <summary>
        /// Gets the footer
        /// </summary>
        public override string GetFooter() => null;
    }
}
