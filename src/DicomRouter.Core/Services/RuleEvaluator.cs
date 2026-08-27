using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;
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
                var allTrue = rule.ConditionTree != null ? EvaluateGroup(metadata, rule.ConditionTree) : rule.Conditions.All(cond => EvaluateCondition(metadata, cond));

                if (allTrue)
                {
                    matches.Add(rule.Name);
                    if (rule.StopOnMatch)
                        break;
                }
            }

            return matches;
        }

        public bool EvaluateCondition(IDictionary<string, string> metadata, Condition cond)
        {
            var raw = ResolveValue(metadata, cond);
            raw ??= string.Empty;
            var cmp = cond.Value ?? string.Empty;

            switch (cond.Operator)
            {
                case ConditionOperator.Equals:
                    return string.Equals(raw, cmp, StringComparison.OrdinalIgnoreCase);
                case ConditionOperator.NotEquals:
                    return !string.Equals(raw, cmp, StringComparison.OrdinalIgnoreCase);
                case ConditionOperator.Exists:
                    return metadata.ContainsKey(cond.Field) && !string.IsNullOrEmpty(raw);
                case ConditionOperator.DoesNotExist:
                    return !metadata.ContainsKey(cond.Field) || string.IsNullOrEmpty(raw);
                case ConditionOperator.Contains:
                    return raw.IndexOf(cmp, StringComparison.OrdinalIgnoreCase) >= 0;
                case ConditionOperator.DoesNotContain:
                    return raw.IndexOf(cmp, StringComparison.OrdinalIgnoreCase) < 0;
                case ConditionOperator.StartsWith:
                    return raw.StartsWith(cmp, StringComparison.OrdinalIgnoreCase);
                case ConditionOperator.EndsWith:
                    return raw.EndsWith(cmp, StringComparison.OrdinalIgnoreCase);
                case ConditionOperator.Regex:
                    try { return Regex.IsMatch(raw, cmp, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1)); } catch (ArgumentException) { return false; }
                case ConditionOperator.GreaterThan:
                    if (double.TryParse(raw, NumberStyles.Any, CultureInfo.InvariantCulture, out var d1) &&
                        double.TryParse(cmp, NumberStyles.Any, CultureInfo.InvariantCulture, out var d2))
                        return d1 > d2;
                    return false;
                case ConditionOperator.GreaterThanOrEqual:
                    return double.TryParse(raw, NumberStyles.Any, CultureInfo.InvariantCulture, out var ge1) && double.TryParse(cmp, NumberStyles.Any, CultureInfo.InvariantCulture, out var ge2) && ge1 >= ge2;
                case ConditionOperator.LessThan:
                    if (double.TryParse(raw, NumberStyles.Any, CultureInfo.InvariantCulture, out var ld1) &&
                        double.TryParse(cmp, NumberStyles.Any, CultureInfo.InvariantCulture, out var ld2))
                        return ld1 < ld2;
                    return false;
                case ConditionOperator.LessThanOrEqual:
                    return double.TryParse(raw, NumberStyles.Any, CultureInfo.InvariantCulture, out var le1) && double.TryParse(cmp, NumberStyles.Any, CultureInfo.InvariantCulture, out var le2) && le1 <= le2;
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

        private static string? ResolveValue(IDictionary<string, string> metadata, Condition condition)
        {
            if (!string.IsNullOrWhiteSpace(condition.Field) && metadata.TryGetValue(condition.Field, out var named)) return named;
            if (condition.Tag == 0) return null;
            var key = $"({condition.Tag >> 16:X4},{condition.Tag & 0xFFFF:X4})";
            return metadata.TryGetValue(key, out var tagged) ? tagged : null;
        }

        private bool EvaluateGroup(IDictionary<string, string> metadata, ConditionGroup group)
        {
            var values = group.Conditions.Select(x => EvaluateCondition(metadata, x)).Concat(group.Groups.Select(x => EvaluateGroup(metadata, x))).ToList();
            var result = group.Operator == ConditionGroupOperator.And ? values.All(x => x) : values.Any(x => x);
            return group.Negate ? !result : result;
        }
    }
}
