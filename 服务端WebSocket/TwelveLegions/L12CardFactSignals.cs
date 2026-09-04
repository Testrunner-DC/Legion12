namespace TwelveLegions.Server;

/// <summary>
/// A machine-readable analytics signal emitted by the authoritative rules engine.
/// Text shown to players is deliberately excluded from this contract.
/// </summary>
public sealed record L12CardFactSignal(
    long Sequence,
    long Revision,
    int Round,
    int Turn,
    string Phase,
    string Kind,
    int? PlayerIndex,
    string? CardId,
    string? CardInstanceId,
    string? SourceZone = null,
    string? DestinationZone = null,
    int? Amount = null,
    string Coverage = "exact",
    IReadOnlyDictionary<string, string>? Data = null);

public sealed partial class L12GameEngine
{
    private readonly List<L12CardFactSignal> _cardFactSignals = [];
    private readonly HashSet<string> _fizzledAnalyticsStackItems = new(StringComparer.Ordinal);
    private long _cardFactSignalSequence;

    public IReadOnlyList<L12CardFactSignal> CardFactSignals => _cardFactSignals;

    private void TrackCardFact(
        string kind,
        int? playerIndex,
        string? cardId,
        string? cardInstanceId,
        string? sourceZone = null,
        string? destinationZone = null,
        int? amount = null,
        string coverage = "exact",
        IReadOnlyDictionary<string, string>? data = null)
    {
        _cardFactSignals.Add(new L12CardFactSignal(
            ++_cardFactSignalSequence,
            State.Revision,
            State.Round,
            State.TurnSerial,
            State.Phase.ToString(),
            kind,
            playerIndex,
            string.IsNullOrWhiteSpace(cardId) ? null : cardId,
            string.IsNullOrWhiteSpace(cardInstanceId) ? null : cardInstanceId,
            sourceZone,
            destinationZone,
            amount,
            coverage,
            data));
    }

    private void TrackCardFact(string kind, int? playerIndex, L12CardInstance card,
        string? sourceZone = null, string? destinationZone = null, int? amount = null,
        string coverage = "exact", IReadOnlyDictionary<string, string>? data = null)
        => TrackCardFact(kind, playerIndex, card.CardId, card.InstanceId, sourceZone,
            destinationZone, amount, coverage, data);

    private void TrackStackFact(string kind, L12StackItem item, string coverage = "exact",
        IReadOnlyDictionary<string, string>? data = null)
    {
        var metadata = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["stackItemId"] = item.StackItemId,
            ["trigger"] = item.Trigger,
        };
        if (data is not null)
            foreach (var pair in data) metadata[pair.Key] = pair.Value;
        TrackCardFact(kind, item.Controller, item.SourceCardId, item.SourceInstanceId,
            coverage: coverage, data: metadata);
    }

    private void TrackStructuredEventFacts(string type, int? playerIndex,
        IReadOnlyList<L12CardInstance> cards)
    {
        var kind = type switch
        {
            "play" => "play",
            "effect-activation" or "effect-trigger" or "effect-response" => "activate",
            "stack-push" or "stack-deferred" or "response" => "push",
            _ => null,
        };
        if (kind is not null)
        {
            foreach (var card in cards)
                TrackCardFact(kind, playerIndex, card, data: new Dictionary<string, string>
                {
                    ["eventType"] = type,
                });
        }

        if (type is not ("effect-failed" or "effect-cancelled" or "effect-noop")) return;
        var currentStack = State.EffectStack.LastOrDefault();
        if (currentStack is not null)
        {
            _fizzledAnalyticsStackItems.Add(currentStack.StackItemId);
            return;
        }
        foreach (var card in cards)
            TrackCardFact("fizzle", playerIndex, card, coverage: "partial",
                data: new Dictionary<string, string> { ["eventType"] = type });
    }

    private void TrackStackCompletion(L12StackItem item)
    {
        if (item.Negated)
        {
            TrackStackFact("negate", item);
            _fizzledAnalyticsStackItems.Remove(item.StackItemId);
            return;
        }
        if (_fizzledAnalyticsStackItems.Remove(item.StackItemId))
        {
            TrackStackFact("fizzle", item, "partial");
            return;
        }
        TrackStackFact("resolve", item);
    }

    private void TrackMasterDamageFact(int targetPlayerIndex, int amount, int? declaredSourcePlayer,
        bool neutralSource, bool combatDamage)
    {
        var source = State.EffectStack.LastOrDefault();
        var sourcePlayer = ResolveDamageSourcePlayer(declaredSourcePlayer, neutralSource);
        var data = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["targetPlayerIndex"] = targetPlayerIndex.ToString(),
            ["targetKind"] = "master",
            ["combatDamage"] = combatDamage ? "true" : "false",
        };
        TrackCardFact("damage", sourcePlayer ?? targetPlayerIndex,
            sourcePlayer is not null ? source?.SourceCardId : null,
            sourcePlayer is not null ? source?.SourceInstanceId : null,
            amount: amount,
            coverage: sourcePlayer is not null && source is not null ? "exact" : "partial",
            data: data);
    }
}
