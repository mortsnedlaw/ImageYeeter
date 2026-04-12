using System.Collections.Generic;

namespace DicomRouter.Core.Models
{
    /// <summary>
    /// A routing rule that contains conditions and destinations.
    /// </summary>
    public class RoutingRule
    {
        /// <summary>
        /// Unique name of the rule.
        /// </summary>
        public string Name { get; set; } = string.Empty;

        /// <summary>
        /// Priority. Lower number = higher priority.
        /// </summary>
        public int Priority { get; set; }

        /// <summary>
        /// If true, rule is active.
        /// </summary>
        public bool Enabled { get; set; } = true;

        /// <summary>
        /// Conditions that must all be true for a match (AND semantics).
        /// </summary>
        public List<Condition> Conditions { get; set; } = new List<Condition>();

        /// <summary>
        /// Destination names to forward to when matched.
        /// </summary>
        public List<string> DestinationNames { get; set; } = new List<string>();

        /// <summary>
        /// Optional tag overrides to apply before forwarding. Key is tag name.
        /// </summary>
        public Dictionary<string, string> TagOverrides { get; set; } = new Dictionary<string, string>();

        /// <summary>
        /// If true, stop processing further rules after this match.
        /// </summary>
        public bool StopOnMatch { get; set; } = true;
    }
}
