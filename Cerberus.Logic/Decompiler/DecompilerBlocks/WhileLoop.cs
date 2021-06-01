namespace Cerberus.Logic
{
    /// <summary>
    /// A class to hold a While Loop
    /// </summary>
    internal class WhileLoop : DecompilerBlock
    {
        /// <summary>
        /// The comparison being made
        /// </summary>
        public string Comparison { get; set; }

        /// <summary>
        /// Initializes a While Loop
        /// </summary>
        /// <param name="startOffset"></param>
        /// <param name="endOffset"></param>
        public WhileLoop(int startOffset, int endOffset) : base(startOffset, endOffset) { }

        /// <summary>
        /// Gets the Header
        /// </summary>
        public override string GetHeader()
        {
            if (Comparison == "1") Comparison = "true";
            return string.Format("while({0})", Comparison);
        }

        /// <summary>
        /// Gets the footer
        /// </summary>
        public override string GetFooter() => null;
    }
}
