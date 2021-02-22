using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Cerberus.Logic
{
    public class ScriptAnim
    {
        /// <summary>
        /// Gets or Sets the Anim Name
        /// </summary>
        public string Name { get; set; }

        /// <summary>
        /// Gets or Sets the offset to the Anim Name
        /// </summary>
        public int lpAnimName { get; set; }

        public ScriptAnimTree OwningTree { get; set; }
    }
}
