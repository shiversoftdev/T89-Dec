using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Cerberus.Logic
{
    /// <summary>
    /// A class to hold a Script Anim Tree
    /// </summary>
    public class ScriptAnimTree
    {
        /// <summary>
        /// Gets or Sets the Anim Tree Name
        /// </summary>
        public string Namespace { get; set; }

        /// <summary>
        /// Gets or Sets the offset to the Anim Tree Name
        /// </summary>
        public int lpNamespace { get; set; }

        /// <summary>
        /// Gets or Sets the list of animation references
        /// </summary>
        public Dictionary<int, ScriptAnim> AnimationReferences = new Dictionary<int, ScriptAnim>();

        public int Count { get; set; }
    }
}
