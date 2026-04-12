using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using DicomRouter.Core.Models;

namespace DicomRouter.Core.Services
{
    /// <summary>
    /// Basic rule evaluator that checks all conditions (AND) within a rule.
    /// </summary>
    public class RuleEvaluator : IRuleEvaluator
    {
        /// <inheritdoc />
        public List<string> Evaluate(IDictionary<string, string> metadata, IEnumerable<RoutingRule> rules)
        {
            var sorted = rules.OrderBy(r => r.Priority).Where(r => r.Enabled);
            var matches = new List<string>();

            foreach (var rule in sorted)
            {
                bool allTrue = true;
                foreach (var cond in rule.Conditions)
                {
                    if (!EvaluateCondition(metadata, cond))
                    {
                        allTrue = false;
                        break;
                    }
                }

                if (allTrue)
                {
                    matches.Add(rule.Name);
                    if (rule.StopOnMatch)
                        break;
                }
            }

            return matches;
        }

        private bool EvaluateCondition(IDictionary<string, string> metadata, Condition cond)
        {
            metadata.TryGetValue(cond.Field, out var raw);
            raw ??= string.Empty;
            var cmp = cond.Value ?? string.Empty;

            switch (cond.Operator)
            {
                case ConditionOperator.Equals:
                    return string.Equals(raw, cmp, StringComparison.OrdinalIgnoreCase);
                case ConditionOperator.NotEquals:
                    return !string.Equals(raw, cmp, StringComparison.OrdinalIgnoreCase);
                case ConditionOperator.Contains:
                    return raw.IndexOf(cmp, StringComparison.OrdinalIgnoreCase) >= 0;
                case ConditionOperator.StartsWith:
                    return raw.StartsWith(cmp, StringComparison.OrdinalIgnoreCase);
                case ConditionOperator.GreaterThan:
                    if (double.TryParse(raw, NumberStyles.Any, CultureInfo.InvariantCulture, out var d1) &&
                        double.TryParse(cmp, NumberStyles.Any, CultureInfo.InvariantCulture, out var d2))
                        return d1 > d2;
                    return false;
                case ConditionOperator.LessThan:
                    if (double.TryParse(raw, NumberStyles.Any, CultureInfo.InvariantCulture, out var ld1) &&
                        double.TryParse(cmp, NumberStyles.Any, CultureInfo.InvariantCulture, out var ld2))
                        return ld1 < ld2;
                    return false;
                case ConditionOperator.BeforeDate:
                    if (DateTime.TryParseExact(raw, new[] {"yyyyMMdd", "yyyy-MM-dd"}, CultureInfo.InvariantCulture, DateTimeStyles.None, out var dt1) &&
                        DateTime.TryParseExact(cmp, new[] {"yyyyMMdd", "yyyy-MM-dd"}, CultureInfo.InvariantCulture, DateTimeStyles.None, out var dt2))
                        return dt1 < dt2;
                    return false;
                case ConditionOperator.AfterDate:
                    if (DateTime.TryParseExact(raw, new[] {"yyyyMMdd", "yyyy-MM-dd"}, CultureInfo.InvariantCulture, DateTimeStyles.None, out var ad1) &&
                        DateTime.TryParseExact(cmp, new[] {"yyyyMMdd", "yyyy-MM-dd"}, CultureInfo.InvariantCulture, DateTimeStyles.None, out var ad2))
                        return ad1 > ad2;
                    return false;
                default:
                    return false;
            }
        }
    }
}
