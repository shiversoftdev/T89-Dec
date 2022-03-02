
namespace Cerberus.Logic
{
    /// <summary>
    /// A class to hold a Dev Block
    /// </summary>
    internal class DevBlock : DecompilerBlock
    {
        public bool ParentDetected { get; private set; }
        public DevBlock(int startOffset, int endOffset, bool parentDetected) : base(startOffset, endOffset)
        {
            RequiresBraces = false;
            ParentDetected = parentDetected;
        }

        /// <summary>
        /// Gets the header
        /// </summary>
        public override string GetHeader() => "/#";

        /// <summary>
        /// Gets the footer
        /// </summary>
        public override string GetFooter() => "#/";
    }
}
