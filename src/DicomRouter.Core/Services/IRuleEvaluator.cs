using System.Collections.Generic;
using DicomRouter.Core.Models;

namespace DicomRouter.Core.Services
{
    /// <summary>
    /// Evaluates routing rules against a metadata dictionary extracted from a DICOM instance or series.
    /// </summary>
    public interface IRuleEvaluator
    {
        /// <summary>
        /// Returns the ordered list of matching rule names for the provided metadata dictionary.
        /// </summary>
        /// <param name="metadata">A dictionary of metadata field name to string value.</param>
        /// <param name="rules">Available rules to evaluate.</param>
        /// <returns>List of rule names that matched in priority order.</returns>
        List<string> Evaluate(IDictionary<string, string> metadata, IEnumerable<RoutingRule> rules);
    }
}
