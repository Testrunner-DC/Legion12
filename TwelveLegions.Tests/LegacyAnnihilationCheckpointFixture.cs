using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;
using TwelveLegions.Server;
using Xunit;

namespace TwelveLegions.Tests;

public sealed class LegacyAnnihilationCheckpointFixture
{
    internal const string CardId = "S01-DS10";
    internal const string CanonicalName = "湮灭";
    internal const string LegacyName = "\u5819\u706D";
    internal const string StaticAbilityId = "S01-DS10:ability:static:33501d2503c08b73";
    internal const string TurnStartAbilityId = "S01-DS10:ability:turn-start:a790e35d0012c86f";

    internal L12Catalog Catalog { get; } =
        L12Catalog.Load(Path.Combine(AppContext.BaseDirectory, "Data"));

    internal LegacyCheckpoint CreateCheckpoint(bool explicitLegacyAtomicFlow)
    {
        var game = new L12GameEngine(Catalog,
            explicitLegacyAtomicFlow ? "legacy-annihilation-atomic-flow" : "legacy-annihilation-source-name",
            "ANV2CK", explicitLegacyAtomicFlow ? 93212 : 93211,
            ["甲", "乙"], [0, 0], skipPreparation: true, disasterMode: "all",
            autoPassEmptyResponses: false, concealHiddenResponseAvailability: false,
            stateFormatVersion: L12PersistenceContract.CurrentStateFormatVersion);

        PrepareDeterministicContinuationState(game);
        var disaster = Card(CardId, "legacy-annihilation-active");
        game.State.ActiveDisaster = disaster;

        const string stackItemId = "legacy-annihilation-stack";
        var stackItem = new L12StackItem
        {
            StackItemId = stackItemId,
            Controller = 0,
            SourceInstanceId = disaster.InstanceId,
            SourceCardId = disaster.CardId,
            SourceName = disaster.Name,
            Trigger = "disaster",
            Text = "天灾主动触发效果",
            SourceSnapshot = disaster.Clone(),
        };
        stackItem.Data["opening"] = "false";
        stackItem.Data["triggerSource"] = "gm";
        stackItem.Data["atomicFlow"] = CanonicalName;
        game.State.EffectStack.Add(stackItem);
        game.State.StackSequence = 1;

        game.State.ResponseWindow = new L12ResponseWindow
        {
            PriorityPlayer = 0,
            ConsecutivePasses = 0,
        };
        game.State.PendingPrompts.Add(new L12Prompt
        {
            PromptId = "legacy-annihilation-pass",
            PlayerIndex = 0,
            Kind = "response",
            Text = "旧检查点：确认不响应该天灾权威效果",
            ValidChoices = ["pass"],
            MinChoose = 1,
            MaxChoose = 1,
            IsPrivate = true,
            Continuation = "stack-response",
            StackItemId = stackItemId,
            Data = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["choiceMode"] = "instant",
                ["responseTargetIds"] = stackItemId,
            },
        });
        game.State.PromptSequence = 1;

        var currentJson = game.SerializeFullState();
        var legacyJson = RewriteAsLegacyV2Checkpoint(currentJson, explicitLegacyAtomicFlow);
        var randomState = Assert.IsType<L12RandomState>(game.RandomState);
        return new LegacyCheckpoint(legacyJson, Sha256(legacyJson), randomState,
            game.CardFactSignalSequence, stackItemId, explicitLegacyAtomicFlow);
    }

    private void PrepareDeterministicContinuationState(L12GameEngine game)
    {
        game.State.ActivePlayer = 0;
        game.State.FirstPlayer = 0;
        game.State.Round = 3;
        game.State.TurnSerial = 7;
        game.State.Phase = L12Phase.Main;
        game.State.DisasterValue = 0;
        game.State.PendingDefense = null;
        game.State.PendingPrompts.Clear();
        game.State.PendingActivations.Clear();
        game.State.EffectStack.Clear();
        game.State.DeferredEffectStack.Clear();
        game.State.PendingTriggerBatches.Clear();
        game.State.PendingTriggerStackCandidates.Clear();
        game.State.AuthorityEvents.Clear();
        game.State.ResponseWindow = null;
        game.State.IsResolvingStack = false;
        game.State.ResumeTurnStartAfterStack = false;
        game.State.CheckDisasterAfterStack = false;
        game.State.PendingDisasterTriggerSource = null;
        game.State.LastTurnStartDisasterEffectTurn = -1;
        game.State.LastTurnStartDisasterEffectInstanceId = null;

        game.State.DisasterPool.Clear();
        game.State.DisasterDeck.Clear();
        game.State.BannedDisasters.Clear();
        game.State.RemovedDisasters.Clear();
        game.State.SelectedDisasters.Clear();
        game.State.RevealedDisasters.Clear();
        game.State.ChosenDisasters.Clear();
        game.State.CustomDisasters.Clear();
        game.State.ChosenDisasterOwners.Clear();
        game.State.ActiveDisaster = null;

        // At the next turn start, player 0 proves the exact one-point loss while
        // player 1 proves that the same damage remains non-lethal at one HP.
        game.State.Players[0].Hp = 2;
        game.State.Players[1].Hp = 1;
    }

    private string RewriteAsLegacyV2Checkpoint(string currentJson, bool explicitLegacyAtomicFlow)
    {
        var root = JsonNode.Parse(currentJson)?.AsObject()
            ?? throw new InvalidDataException("Current V2 checkpoint did not parse as an object.");
        root[nameof(L12GameState.ActiveDisaster)]![nameof(L12CardInstance.Name)] = LegacyName;

        var stack = root[nameof(L12GameState.EffectStack)]?.AsArray().Single()?.AsObject()
            ?? throw new InvalidDataException("Expected exactly one pending disaster stack item.");
        stack[nameof(L12StackItem.SourceName)] = LegacyName;
        stack[nameof(L12StackItem.SourceSnapshot)]![nameof(L12CardInstance.Name)] = LegacyName;
        var data = stack[nameof(L12StackItem.Data)]?.AsObject()
            ?? throw new InvalidDataException("Pending stack item did not contain Data.");
        if (explicitLegacyAtomicFlow) data["atomicFlow"] = LegacyName;
        else data.Remove("atomicFlow");

        return root.ToJsonString();
    }

    private L12CardInstance Card(string cardId, string instanceId)
    {
        var definition = Catalog.Cards[cardId];
        return new L12CardInstance
        {
            InstanceId = instanceId,
            CardId = definition.Id,
            Name = definition.NameZh,
            CardType = definition.CardType,
            IsCounterTactic = definition.IsCounterTactic,
            Faction = definition.Faction,
            ImageUrl = definition.ImageUrl,
            Cost = definition.Cost ?? 0,
            HasPrintedCost = definition.Cost.HasValue,
            EffectText = definition.Effect,
            BaseTroops = definition.Troops ?? 0,
            Troops = definition.Troops ?? 0,
            DisasterLevel = definition.DisasterLevel ?? 0,
            TrialValue = definition.TrialValue ?? 0,
            Traits = [.. definition.Traits],
            Profession = definition.Profession,
            EffectiveProfession = definition.Profession,
        };
    }

    internal static string Sha256(string value)
        => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();
}

internal sealed record LegacyCheckpoint(
    string StateJson,
    string StateHash,
    L12RandomState RandomState,
    long CardFactSignalSequence,
    string StackItemId,
    bool ExplicitLegacyAtomicFlow);
