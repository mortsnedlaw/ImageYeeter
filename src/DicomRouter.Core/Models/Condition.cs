using System;
using System.Collections.Generic;

namespace DicomRouter.Core.Models
{
    /// <summary>
    /// A condition that compares a metadata field to a value using an operator.
    /// </summary>
    public class Condition
    {
        /// <summary>
        /// The metadata field name (e.g., "Modality", "SliceThickness").
        /// </summary>
        public string Field { get; set; } = string.Empty;

        /// <summary>
        /// The operator to apply.
        /// </summary>
        public ConditionOperator Operator { get; set; }

        /// <summary>
        /// The value to compare against.
        /// </summary>
        public string Value { get; set; } = string.Empty;
    }

    /// <summary>
    /// Supported comparison operators for conditions.
    /// </summary>
    public enum ConditionOperator
    {
        Equals,
        NotEquals,
        DoesNotContain,
        GreaterThan,
        GreaterThanOrEqual,
        LessThan,
        LessThanOrEqual,
        Contains,
        StartsWith,
        EndsWith,
        Regex,
        Exists,
        DoesNotExist,
        BeforeDate,
        AfterDate
    }

    public enum ConditionGroupOperator { And, Or }

    public class ConditionGroup
    {
        public ConditionGroupOperator Operator { get; set; } = ConditionGroupOperator.And;
        public bool Negate { get; set; }
        public List<Condition> Conditions { get; set; } = new();
        public List<ConditionGroup> Groups { get; set; } = new();
    }
}
