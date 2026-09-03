using System;
using System.Collections.Generic;
using DicomRouter.Core.Models;
using DicomRouter.Core.Services;
using FluentAssertions;
using Xunit;

namespace DicomRouter.Core.Tests.Services;

public class RuleEvaluatorTests
{
    private readonly RuleEvaluator _evaluator = new();

    [Fact]
    public void EvaluateCondition_Equals_ShouldReturnTrueWhenValuesMatch()
    {
        // Arrange
        var metadata = new Dictionary<string, string> { { "Modality", "CT" } };
        var condition = new Condition { Field = "Modality", Operator = ConditionOperator.Equals, Value = "CT" };

        // Act
        var result = _evaluator.EvaluateCondition(metadata, condition);

        // Assert
        result.Should().BeTrue();
    }

    [Fact]
    public void EvaluateCondition_Equals_ShouldBeCaseInsensitive()
    {
        // Arrange
        var metadata = new Dictionary<string, string> { { "Modality", "ct" } };
        var condition = new Condition { Field = "Modality", Operator = ConditionOperator.Equals, Value = "CT" };

        // Act
        var result = _evaluator.EvaluateCondition(metadata, condition);

        // Assert
        result.Should().BeTrue();
    }

    [Fact]
    public void EvaluateCondition_NotEquals_ShouldReturnTrueWhenValuesDiffer()
    {
        // Arrange
        var metadata = new Dictionary<string, string> { { "Modality", "MR" } };
        var condition = new Condition { Field = "Modality", Operator = ConditionOperator.NotEquals, Value = "CT" };

        // Act
        var result = _evaluator.EvaluateCondition(metadata, condition);

        // Assert
        result.Should().BeTrue();
    }

    [Fact]
    public void EvaluateCondition_Contains_ShouldReturnTrueWhenSubstringExists()
    {
        // Arrange
        var metadata = new Dictionary<string, string> { { "Description", "Head CT Scan" } };
        var condition = new Condition { Field = "Description", Operator = ConditionOperator.Contains, Value = "CT" };

        // Act
        var result = _evaluator.EvaluateCondition(metadata, condition);

        // Assert
        result.Should().BeTrue();
    }

    [Fact]
    public void EvaluateCondition_DoesNotContain_ShouldReturnTrueWhenSubstringAbsent()
    {
        // Arrange
        var metadata = new Dictionary<string, string> { { "Description", "Head Scan" } };
        var condition = new Condition { Field = "Description", Operator = ConditionOperator.DoesNotContain, Value = "CT" };

        // Act
        var result = _evaluator.EvaluateCondition(metadata, condition);

        // Assert
        result.Should().BeTrue();
    }

    [Fact]
    public void EvaluateCondition_StartsWith_ShouldReturnTrue()
    {
        // Arrange
        var metadata = new Dictionary<string, string> { { "PatientID", "PA12345" } };
        var condition = new Condition { Field = "PatientID", Operator = ConditionOperator.StartsWith, Value = "PA" };

        // Act
        var result = _evaluator.EvaluateCondition(metadata, condition);

        // Assert
        result.Should().BeTrue();
    }

    [Fact]
    public void EvaluateCondition_EndsWith_ShouldReturnTrue()
    {
        // Arrange
        var metadata = new Dictionary<string, string> { { "PatientID", "PA12345" } };
        var condition = new Condition { Field = "PatientID", Operator = ConditionOperator.EndsWith, Value = "345" };

        // Act
        var result = _evaluator.EvaluateCondition(metadata, condition);

        // Assert
        result.Should().BeTrue();
    }

    [Fact]
    public void EvaluateCondition_Exists_ShouldReturnTrueWhenFieldExists()
    {
        // Arrange
        var metadata = new Dictionary<string, string> { { "StudyDescription", "Some Study" } };
        var condition = new Condition { Field = "StudyDescription", Operator = ConditionOperator.Exists };

        // Act
        var result = _evaluator.EvaluateCondition(metadata, condition);

        // Assert
        result.Should().BeTrue();
    }

    [Fact]
    public void EvaluateCondition_Exists_ShouldReturnFalseWhenFieldAbsent()
    {
        // Arrange
        var metadata = new Dictionary<string, string>();
        var condition = new Condition { Field = "StudyDescription", Operator = ConditionOperator.Exists };

        // Act
        var result = _evaluator.EvaluateCondition(metadata, condition);

        // Assert
        result.Should().BeFalse();
    }

    [Fact]
    public void EvaluateCondition_Exists_ShouldReturnFalseWhenFieldIsEmpty()
    {
        // Arrange
        var metadata = new Dictionary<string, string> { { "StudyDescription", "" } };
        var condition = new Condition { Field = "StudyDescription", Operator = ConditionOperator.Exists };

        // Act
        var result = _evaluator.EvaluateCondition(metadata, condition);

        // Assert
        result.Should().BeFalse();
    }

    [Fact]
    public void EvaluateCondition_DoesNotExist_ShouldReturnTrueWhenFieldAbsent()
    {
        // Arrange
        var metadata = new Dictionary<string, string>();
        var condition = new Condition { Field = "StudyDescription", Operator = ConditionOperator.DoesNotExist };

        // Act
        var result = _evaluator.EvaluateCondition(metadata, condition);

        // Assert
        result.Should().BeTrue();
    }

    [Fact]
    public void EvaluateCondition_GreaterThan_ShouldReturnTrueForNumericComparison()
    {
        // Arrange
        var metadata = new Dictionary<string, string> { { "SliceThickness", "5.0" } };
        var condition = new Condition { Field = "SliceThickness", Operator = ConditionOperator.GreaterThan, Value = "3.0" };

        // Act
        var result = _evaluator.EvaluateCondition(metadata, condition);

        // Assert
        result.Should().BeTrue();
    }

    [Fact]
    public void EvaluateCondition_LessThan_ShouldReturnTrueForNumericComparison()
    {
        // Arrange
        var metadata = new Dictionary<string, string> { { "SliceThickness", "2.0" } };
        var condition = new Condition { Field = "SliceThickness", Operator = ConditionOperator.LessThan, Value = "3.0" };

        // Act
        var result = _evaluator.EvaluateCondition(metadata, condition);

        // Assert
        result.Should().BeTrue();
    }

    [Fact]
    public void EvaluateCondition_Regex_ShouldMatchPattern()
    {
        // Arrange
        var metadata = new Dictionary<string, string> { { "PatientID", "PA12345" } };
        var condition = new Condition { Field = "PatientID", Operator = ConditionOperator.Regex, Value = @"^PA\d+$" };

        // Act
        var result = _evaluator.EvaluateCondition(metadata, condition);

        // Assert
        result.Should().BeTrue();
    }

    [Fact]
    public void EvaluateCondition_Regex_ShouldNotMatchInvalidPattern()
    {
        // Arrange
        var metadata = new Dictionary<string, string> { { "PatientID", "PA12345" } };
        var condition = new Condition { Field = "PatientID", Operator = ConditionOperator.Regex, Value = @"^MR\d+$" };

        // Act
        var result = _evaluator.EvaluateCondition(metadata, condition);

        // Assert
        result.Should().BeFalse();
    }

    [Fact]
    public void EvaluateCondition_Regex_ShouldReturnFalseForInvalidRegex()
    {
        // Arrange
        var metadata = new Dictionary<string, string> { { "PatientID", "PA12345" } };
        var condition = new Condition { Field = "PatientID", Operator = ConditionOperator.Regex, Value = "[invalid(" };

        // Act
        var result = _evaluator.EvaluateCondition(metadata, condition);

        // Assert
        result.Should().BeFalse();
    }

    [Fact]
    public void EvaluateCondition_BeforeDate_ShouldReturnTrueWhenDateIsBefore()
    {
        // Arrange
        var metadata = new Dictionary<string, string> { { "StudyDate", "20230101" } };
        var condition = new Condition { Field = "StudyDate", Operator = ConditionOperator.BeforeDate, Value = "20230201" };

        // Act
        var result = _evaluator.EvaluateCondition(metadata, condition);

        // Assert
        result.Should().BeTrue();
    }

    [Fact]
    public void EvaluateCondition_AfterDate_ShouldReturnTrueWhenDateIsAfter()
    {
        // Arrange
        var metadata = new Dictionary<string, string> { { "StudyDate", "20230301" } };
        var condition = new Condition { Field = "StudyDate", Operator = ConditionOperator.AfterDate, Value = "20230201" };

        // Act
        var result = _evaluator.EvaluateCondition(metadata, condition);

        // Assert
        result.Should().BeTrue();
    }

    [Fact]
    public void EvaluateCondition_ByTag_ShouldResolveHexadecimalTag()
    {
        // Arrange
        var metadata = new Dictionary<string, string> { { "(0008,0060)", "CT" } }; // Modality tag
        var condition = new Condition { Tag = 0x00080060, Operator = ConditionOperator.Equals, Value = "CT" };

        // Act
        var result = _evaluator.EvaluateCondition(metadata, condition);

        // Assert
        result.Should().BeTrue();
    }

    [Fact]
    public void Evaluate_SingleRule_ShouldReturnRuleNameWhenMatched()
    {
        // Arrange
        var metadata = new Dictionary<string, string> { { "Modality", "CT" } };
        var rule = new RoutingRule
        {
            Id = "rule1",
            Name = "CT Rule",
            Priority = 1,
            Enabled = true,
            Conditions = new List<Condition>
            {
                new() { Field = "Modality", Operator = ConditionOperator.Equals, Value = "CT" }
            }
        };

        // Act
        var results = _evaluator.Evaluate(metadata, new[] { rule });

        // Assert
        results.Should().ContainSingle().Which.Should().Be("CT Rule");
    }

    [Fact]
    public void Evaluate_MultipleConditions_ShouldRequireAllToMatch()
    {
        // Arrange
        var metadata = new Dictionary<string, string>
        {
            { "Modality", "CT" },
            { "StudyDescription", "Head" }
        };
        var rule = new RoutingRule
        {
            Id = "rule1",
            Name = "CT Head",
            Priority = 1,
            Enabled = true,
            Conditions = new List<Condition>
            {
                new() { Field = "Modality", Operator = ConditionOperator.Equals, Value = "CT" },
                new() { Field = "StudyDescription", Operator = ConditionOperator.Contains, Value = "Head" }
            }
        };

        // Act
        var results = _evaluator.Evaluate(metadata, new[] { rule });

        // Assert
        results.Should().ContainSingle();
    }

    [Fact]
    public void Evaluate_MultipleConditions_ShouldFailIfOneDoesNotMatch()
    {
        // Arrange
        var metadata = new Dictionary<string, string>
        {
            { "Modality", "MR" },
            { "StudyDescription", "Head" }
        };
        var rule = new RoutingRule
        {
            Id = "rule1",
            Name = "CT Head",
            Priority = 1,
            Enabled = true,
            Conditions = new List<Condition>
            {
                new() { Field = "Modality", Operator = ConditionOperator.Equals, Value = "CT" },
                new() { Field = "StudyDescription", Operator = ConditionOperator.Contains, Value = "Head" }
            }
        };

        // Act
        var results = _evaluator.Evaluate(metadata, new[] { rule });

        // Assert
        results.Should().BeEmpty();
    }

    [Fact]
    public void Evaluate_PriorityOrdering_ShouldEvaluateByPriority()
    {
        // Arrange
        var metadata = new Dictionary<string, string> { { "Modality", "CT" } };
        var rule1 = new RoutingRule { Id = "rule1", Name = "First", Priority = 2, Enabled = true, StopOnMatch = false, Conditions = new List<Condition> { new() { Field = "Modality", Operator = ConditionOperator.Equals, Value = "CT" } } };
        var rule2 = new RoutingRule { Id = "rule2", Name = "Second", Priority = 1, Enabled = true, StopOnMatch = false, Conditions = new List<Condition> { new() { Field = "Modality", Operator = ConditionOperator.Equals, Value = "CT" } } };

        // Act
        var results = _evaluator.Evaluate(metadata, new[] { rule1, rule2 });

        // Assert
        results.Should().HaveCount(2);
        results[0].Should().Be("Second"); // Lower priority number evaluated first
    }

    [Fact]
    public void Evaluate_StopOnMatch_ShouldHaltEvaluationWhenTrue()
    {
        // Arrange
        var metadata = new Dictionary<string, string> { { "Modality", "CT" } };
        var rule1 = new RoutingRule { Id = "rule1", Name = "First", Priority = 1, Enabled = true, StopOnMatch = true, Conditions = new List<Condition> { new() { Field = "Modality", Operator = ConditionOperator.Equals, Value = "CT" } } };
        var rule2 = new RoutingRule { Id = "rule2", Name = "Second", Priority = 2, Enabled = true, Conditions = new List<Condition> { new() { Field = "Modality", Operator = ConditionOperator.Equals, Value = "CT" } } };

        // Act
        var results = _evaluator.Evaluate(metadata, new[] { rule1, rule2 });

        // Assert
        results.Should().ContainSingle().Which.Should().Be("First");
    }

    [Fact]
    public void Evaluate_DisabledRule_ShouldBeSkipped()
    {
        // Arrange
        var metadata = new Dictionary<string, string> { { "Modality", "CT" } };
        var rule = new RoutingRule { Id = "rule1", Name = "CT Rule", Priority = 1, Enabled = false, Conditions = new List<Condition> { new() { Field = "Modality", Operator = ConditionOperator.Equals, Value = "CT" } } };

        // Act
        var results = _evaluator.Evaluate(metadata, new[] { rule });

        // Assert
        results.Should().BeEmpty();
    }

    [Fact]
    public void EvaluateRule_ShouldEvaluateSingleRule()
    {
        // Arrange
        var metadata = new Dictionary<string, string> { { "Modality", "CT" } };
        var rule = new RoutingRule
        {
            Id = "rule1",
            Name = "CT Rule",
            Conditions = new List<Condition>
            {
                new() { Field = "Modality", Operator = ConditionOperator.Equals, Value = "CT" }
            }
        };

        // Act
        var result = _evaluator.EvaluateRule(metadata, rule);

        // Assert
        result.Should().BeTrue();
    }
}
