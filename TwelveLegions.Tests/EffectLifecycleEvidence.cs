using System.Reflection;
using TwelveLegions.Server;
using Xunit;

namespace TwelveLegions.Tests;

// Evidence is bound to the reviewed structure hash, not just a card or timing.
// A source reference is never itself a claim that the test passed on a release.
[AttributeUsage(AttributeTargets.Method, AllowMultiple = true)]
internal sealed class L12AbilityEvidenceAttribute(string abilityId, params string[] scopes) : Attribute
{
    public string AbilityId { get; } = abilityId;
    public string[] Scopes { get; } = scopes;
}

internal sealed record L12AbilityTestReference(string AbilityId, string TestMethod,
    string CaseCardId, string[] Scopes, string Status = "linked-not-execution-receipt");

internal static class EffectLifecycleEvidence
{
    internal static L12AbilityTestReference[] Read(L12Catalog catalog)
    {
        var identities = catalog.AtomicEffects.All.SelectMany(card => card.Abilities)
            .Select(ability => ability.AbilityId).ToHashSet(StringComparer.Ordinal);
        var references = new List<L12AbilityTestReference>();
        foreach (var method in typeof(EffectLifecycleEvidence).Assembly.GetTypes()
                     .SelectMany(type => type.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)))
        foreach (var evidence in method.GetCustomAttributes<L12AbilityEvidenceAttribute>())
        {
            if (!identities.Contains(evidence.AbilityId))
                throw new InvalidOperationException($"Stale lifecycle evidence: {evidence.AbilityId} at {method.Name}");
            var fact = method.GetCustomAttribute<FactAttribute>();
            if (fact is null || fact.Skip is not null || evidence.Scopes.Length == 0)
                throw new InvalidOperationException($"Lifecycle evidence must reference an enabled named xUnit test: {method.Name}");
            var cardId = evidence.AbilityId.Split(':')[0];
            if (method.GetParameters().Length > 0 && !method.GetCustomAttributes<InlineDataAttribute>()
                    .SelectMany(data => data.GetData(method)).Any(args => args.Contains(cardId)))
                throw new InvalidOperationException($"Lifecycle evidence lacks its exact theory case: {method.Name} / {cardId}");
            references.Add(new(evidence.AbilityId, $"{method.DeclaringType!.FullName}.{method.Name}", cardId,
                evidence.Scopes.Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray()));
        }
        return references.OrderBy(item => item.AbilityId, StringComparer.Ordinal)
            .ThenBy(item => item.TestMethod, StringComparer.Ordinal).ToArray();
    }
}
