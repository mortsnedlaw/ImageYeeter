using System;
using System.Collections.Generic;
using System.Linq;
using DicomRouter.Core.Models;

namespace DicomRouter.Core.Services;

public sealed record RuleEvaluationResult(
    string RuleId,
    bool Result,
    IReadOnlyList<string> MatchedConditions,
    string Branch,
    IReadOnlyList<string> DestinationIds);

public sealed record RoutingPlanResult(
    IReadOnlyList<RuleEvaluationResult> Evaluations,
    IReadOnlyList<string> DestinationIds)
{
    public IReadOnlyList<string> MatchedRuleIds => Evaluations.Where(x => x.Result).Select(x => x.RuleId).ToArray();
}

public sealed class RoutingPlanner
{
    private readonly RuleEvaluator _evaluator;

    public RoutingPlanner(RuleEvaluator? evaluator = null) => _evaluator = evaluator ?? new RuleEvaluator();

    public RoutingPlanResult Plan(
        string listenerId,
        IDictionary<string, string> metadata,
        IEnumerable<RoutingRule> rules,
        IEnumerable<GraphNode> graphNodes,
        IEnumerable<GraphEdge> graphEdges)
    {
        var nodes = graphNodes.ToDictionary(x => x.Id, StringComparer.OrdinalIgnoreCase);
        var edges = graphEdges.ToLookup(x => x.FromNodeId, StringComparer.OrdinalIgnoreCase);
        var rulesById = rules.ToDictionary(x => x.Id, StringComparer.OrdinalIgnoreCase);
        var listener = nodes.Values.FirstOrDefault(x => x.Type == "Listener" && x.ReferenceId.Equals(listenerId, StringComparison.OrdinalIgnoreCase));
        if (listener == null) return new RoutingPlanResult(Array.Empty<RuleEvaluationResult>(), Array.Empty<string>());

        var pending = new Queue<string>(edges[listener.Id].Select(x => x.ToNodeId));
        var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var evaluations = new List<RuleEvaluationResult>();
        var destinationIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        while (pending.Count > 0)
        {
            var nodeId = pending.Dequeue();
            if (!visited.Add(nodeId) || !nodes.TryGetValue(nodeId, out var node)) continue;

            if (node.Type == "Destination")
            {
                destinationIds.Add(node.ReferenceId);
                continue;
            }

            if (node.Type != "Rule" || !rulesById.TryGetValue(node.ReferenceId, out var rule) || !rule.Enabled) continue;
            var result = _evaluator.EvaluateRule(metadata, rule);
            var branch = result ? "True" : "False";
            var branchEdges = edges[node.Id].Where(x => x.Branch.Equals(branch, StringComparison.OrdinalIgnoreCase)).ToArray();
            var branchDestinationIds = branchEdges
                .Select(x => nodes.TryGetValue(x.ToNodeId, out var target) && target.Type == "Destination" ? target.ReferenceId : null)
                .OfType<string>()
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();
            foreach (var destinationId in branchDestinationIds) destinationIds.Add(destinationId);
            foreach (var edge in branchEdges) pending.Enqueue(edge.ToNodeId);

            evaluations.Add(new RuleEvaluationResult(rule.Id, result, DescribeConditions(metadata, rule), branch, branchDestinationIds));
        }

        return new RoutingPlanResult(evaluations, destinationIds.ToArray());
    }

    private IReadOnlyList<string> DescribeConditions(IDictionary<string, string> metadata, RoutingRule rule)
    {
        var conditions = rule.ConditionTree == null ? rule.Conditions : Flatten(rule.ConditionTree);
        return conditions.Where(x => _evaluator.EvaluateCondition(metadata, x))
            .Select(x => $"{x.Field} {x.Operator} {x.Value}")
            .ToArray();
    }

    private static IEnumerable<Condition> Flatten(ConditionGroup group) => group.Conditions.Concat(group.Groups.SelectMany(Flatten));
}