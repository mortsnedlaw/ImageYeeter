using System;
using System.Collections.Generic;
using System.Linq;
using DicomRouter.Core.Models;
using DicomRouter.Core.Services;
using FluentAssertions;
using Xunit;

namespace DicomRouter.Core.Tests.Services;

public class RoutingPlannerTests
{
    private readonly RoutingPlanner _planner = new();

    [Fact]
    public void Plan_NoListenerNode_ShouldReturnEmpty()
    {
        // Arrange
        var metadata = new Dictionary<string, string> { { "Modality", "CT" } };
        var nodes = new[] { new GraphNode { Id = "node1", Type = "Rule", ReferenceId = "rule1" } };
        var edges = Array.Empty<GraphEdge>();
        var rules = Array.Empty<RoutingRule>();

        // Act
        var result = _planner.Plan("listener1", metadata, rules, nodes, edges);

        // Assert
        result.Evaluations.Should().BeEmpty();
        result.DestinationIds.Should().BeEmpty();
    }

    [Fact]
    public void Plan_ListenerToDestinationDirect_ShouldReturnDestination()
    {
        // Arrange
        var metadata = new Dictionary<string, string>();
        var listenerNode = new GraphNode { Id = "listener-node", Type = "Listener", ReferenceId = "listener1" };
        var destNode = new GraphNode { Id = "dest-node", Type = "Destination", ReferenceId = "dest1" };
        var nodes = new[] { listenerNode, destNode };
        var edges = new[] { new GraphEdge { FromNodeId = "listener-node", ToNodeId = "dest-node", Branch = "True" } };
        var rules = Array.Empty<RoutingRule>();

        // Act
        var result = _planner.Plan("listener1", metadata, rules, nodes, edges);

        // Assert
        result.DestinationIds.Should().ContainSingle().Which.Should().Be("dest1");
    }

    [Fact]
    public void Plan_ListenerToRuleToDestination_ShouldFollowPath()
    {
        // Arrange
        var metadata = new Dictionary<string, string> { { "Modality", "CT" } };
        
        var listenerNode = new GraphNode { Id = "listener-node", Type = "Listener", ReferenceId = "listener1" };
        var ruleNode = new GraphNode { Id = "rule-node", Type = "Rule", ReferenceId = "rule1" };
        var destNode = new GraphNode { Id = "dest-node", Type = "Destination", ReferenceId = "dest1" };
        var nodes = new[] { listenerNode, ruleNode, destNode };

        var edges = new[]
        {
            new GraphEdge { FromNodeId = "listener-node", ToNodeId = "rule-node", Branch = "True" },
            new GraphEdge { FromNodeId = "rule-node", ToNodeId = "dest-node", Branch = "True" }
        };

        var rule = new RoutingRule
        {
            Id = "rule1",
            Name = "CT Rule",
            Enabled = true,
            Conditions = new List<Condition>
            {
                new() { Field = "Modality", Operator = ConditionOperator.Equals, Value = "CT" }
            }
        };

        // Act
        var result = _planner.Plan("listener1", metadata, new[] { rule }, nodes, edges);

        // Assert
        result.DestinationIds.Should().ContainSingle().Which.Should().Be("dest1");
        result.Evaluations.Should().ContainSingle();
        result.Evaluations[0].Result.Should().BeTrue();
        result.MatchedRuleIds.Should().ContainSingle().Which.Should().Be("rule1");
    }

    [Fact]
    public void Plan_RuleWithTrueBranch_ShouldFollowTruePath()
    {
        // Arrange
        var metadata = new Dictionary<string, string> { { "Modality", "CT" } };
        
        var listenerNode = new GraphNode { Id = "listener-node", Type = "Listener", ReferenceId = "listener1" };
        var ruleNode = new GraphNode { Id = "rule-node", Type = "Rule", ReferenceId = "rule1" };
        var destTrue = new GraphNode { Id = "dest-true", Type = "Destination", ReferenceId = "dest-true-ref" };
        var destFalse = new GraphNode { Id = "dest-false", Type = "Destination", ReferenceId = "dest-false-ref" };
        var nodes = new[] { listenerNode, ruleNode, destTrue, destFalse };

        var edges = new[]
        {
            new GraphEdge { FromNodeId = "listener-node", ToNodeId = "rule-node", Branch = "True" },
            new GraphEdge { FromNodeId = "rule-node", ToNodeId = "dest-true", Branch = "True" },
            new GraphEdge { FromNodeId = "rule-node", ToNodeId = "dest-false", Branch = "False" }
        };

        var rule = new RoutingRule
        {
            Id = "rule1",
            Name = "CT Rule",
            Enabled = true,
            Conditions = new List<Condition>
            {
                new() { Field = "Modality", Operator = ConditionOperator.Equals, Value = "CT" }
            }
        };

        // Act
        var result = _planner.Plan("listener1", metadata, new[] { rule }, nodes, edges);

        // Assert
        result.DestinationIds.Should().ContainSingle().Which.Should().Be("dest-true-ref");
    }

    [Fact]
    public void Plan_RuleWithFalseBranch_ShouldFollowFalsePath()
    {
        // Arrange
        var metadata = new Dictionary<string, string> { { "Modality", "MR" } };
        
        var listenerNode = new GraphNode { Id = "listener-node", Type = "Listener", ReferenceId = "listener1" };
        var ruleNode = new GraphNode { Id = "rule-node", Type = "Rule", ReferenceId = "rule1" };
        var destTrue = new GraphNode { Id = "dest-true", Type = "Destination", ReferenceId = "dest-true-ref" };
        var destFalse = new GraphNode { Id = "dest-false", Type = "Destination", ReferenceId = "dest-false-ref" };
        var nodes = new[] { listenerNode, ruleNode, destTrue, destFalse };

        var edges = new[]
        {
            new GraphEdge { FromNodeId = "listener-node", ToNodeId = "rule-node", Branch = "True" },
            new GraphEdge { FromNodeId = "rule-node", ToNodeId = "dest-true", Branch = "True" },
            new GraphEdge { FromNodeId = "rule-node", ToNodeId = "dest-false", Branch = "False" }
        };

        var rule = new RoutingRule
        {
            Id = "rule1",
            Name = "CT Rule",
            Enabled = true,
            Conditions = new List<Condition>
            {
                new() { Field = "Modality", Operator = ConditionOperator.Equals, Value = "CT" }
            }
        };

        // Act
        var result = _planner.Plan("listener1", metadata, new[] { rule }, nodes, edges);

        // Assert
        result.DestinationIds.Should().ContainSingle().Which.Should().Be("dest-false-ref");
    }

    [Fact]
    public void Plan_MultipleDestinations_ShouldReturnAll()
    {
        // Arrange
        var metadata = new Dictionary<string, string> { { "Modality", "CT" } };
        
        var listenerNode = new GraphNode { Id = "listener-node", Type = "Listener", ReferenceId = "listener1" };
        var ruleNode = new GraphNode { Id = "rule-node", Type = "Rule", ReferenceId = "rule1" };
        var dest1 = new GraphNode { Id = "dest1-node", Type = "Destination", ReferenceId = "dest1" };
        var dest2 = new GraphNode { Id = "dest2-node", Type = "Destination", ReferenceId = "dest2" };
        var nodes = new[] { listenerNode, ruleNode, dest1, dest2 };

        var edges = new[]
        {
            new GraphEdge { FromNodeId = "listener-node", ToNodeId = "rule-node", Branch = "True" },
            new GraphEdge { FromNodeId = "rule-node", ToNodeId = "dest1-node", Branch = "True" },
            new GraphEdge { FromNodeId = "rule-node", ToNodeId = "dest2-node", Branch = "True" }
        };

        var rule = new RoutingRule
        {
            Id = "rule1",
            Name = "CT Rule",
            Enabled = true,
            Conditions = new List<Condition>
            {
                new() { Field = "Modality", Operator = ConditionOperator.Equals, Value = "CT" }
            }
        };

        // Act
        var result = _planner.Plan("listener1", metadata, new[] { rule }, nodes, edges);

        // Assert
        result.DestinationIds.Should().HaveCount(2).And.Contain("dest1").And.Contain("dest2");
    }

    [Fact]
    public void Plan_DisabledRule_ShouldNotBeEvaluated()
    {
        // Arrange
        var metadata = new Dictionary<string, string> { { "Modality", "CT" } };
        
        var listenerNode = new GraphNode { Id = "listener-node", Type = "Listener", ReferenceId = "listener1" };
        var ruleNode = new GraphNode { Id = "rule-node", Type = "Rule", ReferenceId = "rule1" };
        var destNode = new GraphNode { Id = "dest-node", Type = "Destination", ReferenceId = "dest1" };
        var nodes = new[] { listenerNode, ruleNode, destNode };

        var edges = new[]
        {
            new GraphEdge { FromNodeId = "listener-node", ToNodeId = "rule-node", Branch = "True" },
            new GraphEdge { FromNodeId = "rule-node", ToNodeId = "dest-node", Branch = "True" }
        };

        var rule = new RoutingRule
        {
            Id = "rule1",
            Name = "CT Rule",
            Enabled = false, // Disabled
            Conditions = new List<Condition>
            {
                new() { Field = "Modality", Operator = ConditionOperator.Equals, Value = "CT" }
            }
        };

        // Act
        var result = _planner.Plan("listener1", metadata, new[] { rule }, nodes, edges);

        // Assert
        result.DestinationIds.Should().BeEmpty();
        result.Evaluations.Should().BeEmpty();
    }

    [Fact]
    public void Plan_ChainedRules_ShouldEvaluateSequence()
    {
        // Arrange
        var metadata = new Dictionary<string, string> 
        { 
            { "Modality", "CT" },
            { "StudyDescription", "Head" }
        };
        
        var listenerNode = new GraphNode { Id = "listener-node", Type = "Listener", ReferenceId = "listener1" };
        var rule1Node = new GraphNode { Id = "rule1-node", Type = "Rule", ReferenceId = "rule1" };
        var rule2Node = new GraphNode { Id = "rule2-node", Type = "Rule", ReferenceId = "rule2" };
        var destNode = new GraphNode { Id = "dest-node", Type = "Destination", ReferenceId = "dest1" };
        var nodes = new[] { listenerNode, rule1Node, rule2Node, destNode };

        var edges = new[]
        {
            new GraphEdge { FromNodeId = "listener-node", ToNodeId = "rule1-node", Branch = "True" },
            new GraphEdge { FromNodeId = "rule1-node", ToNodeId = "rule2-node", Branch = "True" },
            new GraphEdge { FromNodeId = "rule2-node", ToNodeId = "dest-node", Branch = "True" }
        };

        var rule1 = new RoutingRule
        {
            Id = "rule1",
            Name = "First Rule",
            Enabled = true,
            Conditions = new List<Condition> { new() { Field = "Modality", Operator = ConditionOperator.Equals, Value = "CT" } }
        };

        var rule2 = new RoutingRule
        {
            Id = "rule2",
            Name = "Second Rule",
            Enabled = true,
            Conditions = new List<Condition> { new() { Field = "StudyDescription", Operator = ConditionOperator.Contains, Value = "Head" } }
        };

        // Act
        var result = _planner.Plan("listener1", metadata, new[] { rule1, rule2 }, nodes, edges);

        // Assert
        result.DestinationIds.Should().ContainSingle().Which.Should().Be("dest1");
        result.Evaluations.Should().HaveCount(2);
        result.MatchedRuleIds.Should().HaveCount(2);
    }

    [Fact]
    public void Plan_CyclicEdges_ShouldNotCauseInfiniteLoop()
    {
        // Arrange - create a simple cycle: listener -> rule1 -> rule2 -> rule1
        var metadata = new Dictionary<string, string> { { "Modality", "CT" } };
        
        var listenerNode = new GraphNode { Id = "listener-node", Type = "Listener", ReferenceId = "listener1" };
        var rule1Node = new GraphNode { Id = "rule1-node", Type = "Rule", ReferenceId = "rule1" };
        var rule2Node = new GraphNode { Id = "rule2-node", Type = "Rule", ReferenceId = "rule2" };
        var destNode = new GraphNode { Id = "dest-node", Type = "Destination", ReferenceId = "dest1" };
        var nodes = new[] { listenerNode, rule1Node, rule2Node, destNode };

        var edges = new[]
        {
            new GraphEdge { FromNodeId = "listener-node", ToNodeId = "rule1-node", Branch = "True" },
            new GraphEdge { FromNodeId = "rule1-node", ToNodeId = "rule2-node", Branch = "True" },
            new GraphEdge { FromNodeId = "rule2-node", ToNodeId = "rule1-node", Branch = "True" }, // Cycle!
            new GraphEdge { FromNodeId = "rule2-node", ToNodeId = "dest-node", Branch = "False" }
        };

        var rule1 = new RoutingRule { Id = "rule1", Name = "Rule1", Enabled = true, Conditions = new List<Condition>() };
        var rule2 = new RoutingRule { Id = "rule2", Name = "Rule2", Enabled = true, Conditions = new List<Condition>() };

        // Act - should not hang
        var result = _planner.Plan("listener1", metadata, new[] { rule1, rule2 }, nodes, edges);

        // Assert - should complete without visiting the same node twice
        result.Evaluations.Should().HaveCount(2); // Each rule evaluated once only
    }

    [Fact]
    public void Plan_BranchEvaluationRecordsCorrectBranch()
    {
        // Arrange
        var metadata = new Dictionary<string, string> { { "Modality", "CT" } };
        
        var listenerNode = new GraphNode { Id = "listener-node", Type = "Listener", ReferenceId = "listener1" };
        var ruleNode = new GraphNode { Id = "rule-node", Type = "Rule", ReferenceId = "rule1" };
        var destTrue = new GraphNode { Id = "dest-true", Type = "Destination", ReferenceId = "dest-true" };
        var destFalse = new GraphNode { Id = "dest-false", Type = "Destination", ReferenceId = "dest-false" };
        var nodes = new[] { listenerNode, ruleNode, destTrue, destFalse };

        var edges = new[]
        {
            new GraphEdge { FromNodeId = "listener-node", ToNodeId = "rule-node", Branch = "True" },
            new GraphEdge { FromNodeId = "rule-node", ToNodeId = "dest-true", Branch = "True" },
            new GraphEdge { FromNodeId = "rule-node", ToNodeId = "dest-false", Branch = "False" }
        };

        var rule = new RoutingRule
        {
            Id = "rule1",
            Name = "CT Rule",
            Enabled = true,
            Conditions = new List<Condition>
            {
                new() { Field = "Modality", Operator = ConditionOperator.Equals, Value = "CT" }
            }
        };

        // Act
        var result = _planner.Plan("listener1", metadata, new[] { rule }, nodes, edges);

        // Assert
        result.Evaluations.Should().ContainSingle();
        result.Evaluations[0].Branch.Should().Be("True");
        result.Evaluations[0].Result.Should().BeTrue();
    }
}
