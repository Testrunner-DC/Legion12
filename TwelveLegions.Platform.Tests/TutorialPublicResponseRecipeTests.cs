using System.Security.Cryptography;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using TwelveLegions.Server;
using Xunit;

namespace TwelveLegions.Platform.Tests;

public sealed class TutorialPublicResponseRecipeTests
{
    // Found by the bounded 1701..1800 synthesis search, then frozen so the
    // regression replays one exact public-deck deal and command sequence.
    private const int FixedSeed = 1713;
    private const string StudentPublicCode = "J2DR72B52KQQ";
    private const string OpponentPublicCode = "RGKZ73S3NHMQ";
    private const string StudentContentHash = "E93351066B33A5BD2E8044DE02329F59870C4E1EE66F2725058B42B62F151738";
    private const string OpponentContentHash = "2FB47E86AEF1A16F3340176ACD4469B39FA42BA350F0DA1DD6F3C682E43937BD";

    private static readonly JsonSerializerOptions TutorialJson = new()
    {
        PropertyNameCaseInsensitive = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    private static readonly string DataDirectory = Path.Combine(AppContext.BaseDirectory, "TwelveLegions", "Data");
    private static readonly L12Catalog Catalog = L12Catalog.Load(DataDirectory);
    private static readonly FrozenDeckPair FrozenDecks = LoadFrozenDecks();

    [Fact]
    public void FixedPublicDecksReachTwoCardResponseRecipeThroughOnlyLegalKernelCommands()
    {
        // The regression never depends on a mutable search result, hidden-zone edits,
        // a GM command, or a teaching-only rules path.
        Assert.True(TryRunRecipe(FixedSeed, out var replay, out var replayFailure), replayFailure);
        Assert.NotNull(replay);
        Assert.Equal(2, replay!.LowHandCount);
        Assert.Equal(1, replay.MasterDamage);
        Assert.True(replay.EntrantActive);
        Assert.True(replay.ReturnedMoraleStayedReturned);
        Assert.Equal(StudentContentHash, replay.StudentContentHash);
        Assert.Equal(OpponentContentHash, replay.OpponentContentHash);
    }

    private static bool TryRunRecipe(int seed, out RecipeResult? result, out string failure)
    {
        try
        {
            result = RunRecipe(seed);
            failure = string.Empty;
            return true;
        }
        catch (Exception error)
        {
            result = null;
            failure = error.Message;
            return false;
        }
    }

    private static RecipeResult RunRecipe(int seed)
    {
        var game = new L12GameEngine(Catalog, $"tutorial-public-response-{seed}", "PUBLIC-RESPONSE", seed,
            ["学员", "教学对手"], [FrozenDecks.Student, FrozenDecks.Opponent], disasterMode: "none",
            autoPassEmptyResponses: true, concealHiddenResponseAvailability: true,
            stateFormatVersion: L12PersistenceContract.CurrentStateFormatVersion);

        var initiative = RequireSinglePrompt(game, "setup-initiative");
        Send(game, initiative.PlayerIndex, new("resolvePrompt", PromptId: initiative.PromptId,
            Choice: initiative.PlayerIndex == 0 ? "first" : "second"));

        var student = game.State.Players[0];
        var opponent = game.State.Players[1];
        var keptShanhe = student.Hand.FirstOrDefault(card => card.CardId == "S01-0117");
        var keptHiddenPass = student.Hand.FirstOrDefault(card => card.CardId == "ST01-10");
        var studentMulligan = student.Hand
            .Where(card => !ReferenceEquals(card, keptShanhe) && !ReferenceEquals(card, keptHiddenPass))
            .Select(card => card.InstanceId).ToList();
        var keptBoar = opponent.Hand.FirstOrDefault(card => card.CardId == "ST04-09");
        var opponentMulligan = opponent.Hand.Where(card => !ReferenceEquals(card, keptBoar))
            .Select(card => card.InstanceId).ToList();
        Send(game, 0, new("mulligan", CardInstanceIds: studentMulligan));
        Send(game, 1, new("mulligan", CardInstanceIds: opponentMulligan));

        Require(game.State.ActivePlayer == 0 && game.State.Phase == L12Phase.Main,
            "student did not legally become first active player");
        student = game.State.Players[0];
        opponent = game.State.Players[1];
        var shanhe = RequireSingleCard(student.Hand, "S01-0117", "opening Shanhe");
        var hiddenPass = RequireSingleCard(student.Hand, "ST01-10", "opening Hidden Pass");
        var openingBlockers = student.Hand.Where(card => card != shanhe && card != hiddenPass).ToArray();
        Require(openingBlockers.Length == 4 && openingBlockers.All(card => card.CardType == "legion"),
            "post-mulligan nonessential student hand is not four legal legion blockers");
        var boar = opponent.Hand.FirstOrDefault(card => card.CardId == "ST04-09")
            ?? throw new InvalidOperationException("opponent has no post-mulligan Boar King");

        Send(game, 0, new("endTurn"));
        Require(game.State.ActivePlayer == 1, "opponent did not receive its first turn");
        PlayFromHand(game, 1, boar, row: 0, slot: 0);
        AttackMasterAndDeclineKagutsuchi(game, boar);
        ResolveMasterDefense(game, openingBlockers.Select(card => card.InstanceId).ToList());
        Require(student.Hand.Count == 2 && student.Hand.Contains(shanhe) && student.Hand.Contains(hiddenPass),
            "first real block did not discard exactly the four unrelated opening legions");
        Send(game, 1, new("endTurn"));

        Require(game.State.ActivePlayer == 0, "student did not receive the Shanhe entry turn");
        var firstDraw = RequireSingleOtherHandCard(student, shanhe, hiddenPass, "first student draw");
        Require(firstDraw.CardType == "legion", "first student draw cannot be legally discarded as the second block");
        PlayFromHand(game, 0, shanhe);
        Require(ReferenceEquals(student.Relic, shanhe) && !shanhe.Tapped,
            "Shanhe did not legally enter the active relic zone");
        Require(student.Hand.Count == 2 && student.Hand.Contains(hiddenPass) && student.Hand.Contains(firstDraw),
            "Shanhe entry did not leave the expected two-card hand");
        Send(game, 0, new("endTurn"));

        Require(game.State.ActivePlayer == 1, "opponent did not receive its second Boar King turn");
        AttackMasterAndDeclineKagutsuchi(game, boar);
        ResolveMasterDefense(game, [firstDraw.InstanceId]);
        Require(student.Hand.Count == 1 && student.Hand.Single() == hiddenPass,
            "second real block did not leave only Hidden Pass");
        Send(game, 1, new("endTurn"));

        Require(game.State.ActivePlayer == 0 && !shanhe.Tapped,
            "student did not reach a later turn with Shanhe legally reset");
        var searchPayment = RequireSingleOtherHandCard(student, hiddenPass, null, "Shanhe search payment");
        Require(student.Hand.Count == 2, "low-hand checkpoint must contain exactly two real cards");
        Require(PublicFieldLegions(student).Count == 0,
            "low-hand checkpoint contains an unrelated battlefield legion");

        Send(game, 0, new("activateAbility", shanhe.InstanceId, Ability: "artifactSearch"));
        var discardPrompt = RequireSinglePrompt(game);
        Require(discardPrompt.ValidChoices.Contains(searchPayment.InstanceId),
            "Shanhe did not offer the real second hand card as its discard cost");
        Send(game, 0, new("resolvePrompt", PromptId: discardPrompt.PromptId,
            Choice: searchPayment.InstanceId));
        PassResponsePrompts(game);

        var searchPrompt = RequireSinglePrompt(game);
        Require(searchPrompt.Kind == "search"
                && searchPrompt.Data.GetValueOrDefault("action") == "shanhe-search-pick",
            $"expected Shanhe search choice, found {searchPrompt.Kind}/{searchPrompt.Continuation}");
        var entrant = searchPrompt.ValidChoices
            .Select(id => student.Library.FirstOrDefault(card => card.InstanceId == id))
            .SingleOrDefault(card => card?.CardId == "ST01-08")
            ?? throw new InvalidOperationException("frozen Shanhe top three no longer contain ST01-08");
        Require(entrant.CardType == "legion" && entrant.Faction == "tianting" && entrant.CurrentCost <= 4,
            "frozen Hidden Pass entrant is no longer a legal Tianting legion of cost at most four");
        Send(game, 0, new("resolvePrompt", PromptId: searchPrompt.PromptId, Choice: entrant.InstanceId));
        var orderPrompt = RequireSinglePrompt(game);
        Require(orderPrompt.Kind == "order"
                && orderPrompt.Continuation == "card-effect"
                && orderPrompt.Data.GetValueOrDefault("placementMode") == "all-top-bottom",
            "Shanhe remainder did not use the real all-top-or-bottom prompt");
        Send(game, 0, new("resolvePrompt", PromptId: orderPrompt.PromptId,
            TopCardInstanceIds: [.. orderPrompt.ValidChoices], BottomCardInstanceIds: []));
        PassResponsePrompts(game);

        Require(shanhe.Tapped, "Shanhe active effect did not rest its source");
        Require(student.Graveyard.Contains(searchPayment), "Shanhe discard payment did not stay paid");
        Require(student.Hand.Count == 2 && student.Hand.Contains(hiddenPass) && student.Hand.Contains(entrant),
            "Shanhe effect did not exchange one card for the declared Tianting legion");
        Require(game.State.Events.Any(entry => entry.Type == "effect-result"
            && entry.EffectResultStatus == "resolved"
            && entry.Cards.Any(card => card.InstanceId == shanhe.InstanceId)),
            "Shanhe active effect has no authoritative resolved result");

        PlayFromHand(game, 0, hiddenPass, row: 1, slot: 2);
        Require(student.Field[1][2] == hiddenPass && hiddenPass.Hidden,
            "Hidden Pass was not legally set as a covered tactic");
        Require(student.Hand.Count == 1 && student.Hand.Single() == entrant,
            "covered Hidden Pass did not leave only its declared entrant in hand");
        Send(game, 0, new("endTurn"));

        Require(game.State.ActivePlayer == 1, "opponent did not receive the response demonstration turn");
        var hpBefore = student.Hp;
        AttackMasterAndDeclineKagutsuchi(game, boar);
        ResolveMasterDefense(game, []);
        Require(student.Hp == hpBefore - 1,
            "real no-block damage was not committed before the post-attack response");

        var declaration = RequireHiddenPassPrompt(game, hiddenPass.InstanceId);
        Require(declaration.ValidChoices.Contains("mode:use"), "Hidden Pass cannot be declared after the attack");
        Send(game, 0, new("resolvePrompt", PromptId: declaration.PromptId, Choice: "mode:use"));
        var moralePrompt = RequireSinglePrompt(game);
        var returnedMorale = moralePrompt.ValidChoices
            .Select(id => student.Morale.FirstOrDefault(card => card.InstanceId == id && !card.Tapped))
            .FirstOrDefault(card => card is not null)
            ?? throw new InvalidOperationException("Hidden Pass has no real active morale to return");
        Send(game, 0, new("resolvePrompt", PromptId: moralePrompt.PromptId,
            Choice: returnedMorale.InstanceId));
        var entrantPrompt = RequireSinglePrompt(game);
        Require(entrantPrompt.ValidChoices.Contains(entrant.InstanceId),
            "Hidden Pass did not preserve the hand legion declaration");
        Send(game, 0, new("resolvePrompt", PromptId: entrantPrompt.PromptId,
            Choice: entrant.InstanceId));
        var slotPrompt = RequireSinglePrompt(game);
        var entrySlot = slotPrompt.ValidChoices.SingleOrDefault(choice => choice == "0:0")
            ?? throw new InvalidOperationException("frozen Hidden Pass entry slot 0:0 is no longer legal");
        Send(game, 0, new("resolvePrompt", PromptId: slotPrompt.PromptId, Choice: entrySlot));
        DrainPostDeclarationWork(game);

        Require(student.Hp == hpBefore - 1, "Hidden Pass incorrectly erased the already resolved attack damage");
        Require(!student.Morale.Any(card => card.InstanceId == returnedMorale.InstanceId)
                && student.MoraleDeck.Any(card => card.InstanceId == returnedMorale.InstanceId),
            "Hidden Pass returned morale was refunded after resolution");
        var entered = PublicFieldLegions(student).SingleOrDefault(card => card.InstanceId == entrant.InstanceId)
            ?? throw new InvalidOperationException("Hidden Pass entrant did not reach the battlefield");
        Require(!entered.Tapped, "Hidden Pass entrant did not enter active");
        Require(PublicFieldLegions(student).Count == 1,
            "response result contains an unrelated student battlefield legion");
        Require(student.Graveyard.Contains(hiddenPass), "resolved Hidden Pass did not leave the covered slot");
        Require(game.State.Events.Any(entry => entry.Type == "effect-result"
            && entry.EffectResultStatus == "resolved"
            && entry.Cards.Any(card => card.InstanceId == hiddenPass.InstanceId)),
            "Hidden Pass has no authoritative resolved result");
        Require(game.State.Events.Any(entry => entry.Type == "defense"
            && entry.PlayerCombat?.OutcomeCode == "unblocked" && entry.PlayerCombat.MasterDamage == 1),
            "combat log does not retain the real unblocked master damage");
        Require(!game.State.Events.Any(entry => entry.Type == "effect-negated"
            && entry.Cards.Any(card => card.InstanceId == boar.InstanceId)),
            "post-attack response was incorrectly represented as negating Boar King's attack");
        Require(game.State.PendingDefense is null && game.State.PendingPrompts.Count == 0
                && game.State.EffectStack.Count == 0,
            "response prefix did not settle at an engine checkpoint boundary");

        // This is an engine-only recipe, not a room/Journal integration test. It does prove
        // that the resulting current-format engine checkpoint itself restores byte-for-byte
        // to the same authoritative state hash; Journal append/reload remains a later room test.
        var checkpoint = game.SerializeFullState();
        var restored = L12GameEngine.RestoreCheckpoint(Catalog, checkpoint,
            game.RandomState ?? throw new InvalidOperationException("current-format random state is missing"),
            game.CardFactSignalSequence, autoPassEmptyResponses: true,
            concealHiddenResponseAvailability: true);
        Require(restored.ComputeStateHash() == game.ComputeStateHash(),
            "settled engine checkpoint did not restore to the same state hash");

        return new(seed, 2, hpBefore - student.Hp, !entered.Tapped,
            student.MoraleDeck.Any(card => card.InstanceId == returnedMorale.InstanceId),
            FrozenDecks.StudentHash, FrozenDecks.OpponentHash);
    }

    private static void AttackMasterAndDeclineKagutsuchi(L12GameEngine game, L12CardInstance boar)
    {
        Send(game, 1, new("attack", boar.InstanceId, Target: new("master")));
        for (var safety = 0; safety < 12 && game.State.PendingPrompts.FirstOrDefault() is { } prompt; safety++)
        {
            if (prompt.ValidChoices.Contains("mode:none"))
                Send(game, prompt.PlayerIndex, new("resolvePrompt", PromptId: prompt.PromptId, Choice: "mode:none"));
            else if (prompt.Kind == "response" && prompt.ValidChoices.Contains("pass"))
                Send(game, prompt.PlayerIndex, new("resolvePrompt", PromptId: prompt.PromptId, Choice: "pass"));
            else
                throw new InvalidOperationException($"unexpected attack prompt {prompt.Kind}/{prompt.Continuation}");
        }
        Require(game.State.PendingDefense is { Stage: L12CombatStage.DefenseChoice, AttackerPlayer: 1 },
            "Boar King did not reach a real master-defense choice");
    }

    private static void ResolveMasterDefense(L12GameEngine game, List<string> blockerIds)
    {
        Send(game, 0, new("resolveDefense", CardInstanceIds: blockerIds));
        if (blockerIds.Count == 0) return;
        for (var safety = 0; safety < 12 && game.State.PendingPrompts.FirstOrDefault() is { } prompt; safety++)
        {
            if (prompt.Kind == "response" && prompt.ValidChoices.Contains("pass"))
                Send(game, prompt.PlayerIndex, new("resolvePrompt", PromptId: prompt.PromptId, Choice: "pass"));
            else if (prompt.ValidChoices.Contains("mode:none"))
                Send(game, prompt.PlayerIndex, new("resolvePrompt", PromptId: prompt.PromptId, Choice: "mode:none"));
            else
                throw new InvalidOperationException($"unexpected block prompt {prompt.Kind}/{prompt.Continuation}");
        }
        Require(game.State.PendingDefense is null, "declared hand-legion block did not settle");
    }

    private static L12Prompt RequireHiddenPassPrompt(L12GameEngine game, string sourceInstanceId)
    {
        for (var safety = 0; safety < 12; safety++)
        {
            var prompt = RequireSinglePrompt(game);
            if (prompt.SourceInstanceId == sourceInstanceId
                || prompt.ActivationId is not null && game.State.PendingActivations.Any(activation =>
                    activation.ActivationId == prompt.ActivationId
                    && activation.SourceInstanceId == sourceInstanceId))
                return prompt;
            if (prompt.Kind == "response" && prompt.ValidChoices.Contains("pass"))
            {
                Send(game, prompt.PlayerIndex, new("resolvePrompt", PromptId: prompt.PromptId, Choice: "pass"));
                continue;
            }
            if (prompt.ValidChoices.Contains("mode:none"))
            {
                Send(game, prompt.PlayerIndex, new("resolvePrompt", PromptId: prompt.PromptId, Choice: "mode:none"));
                continue;
            }
            throw new InvalidOperationException($"unexpected prompt before Hidden Pass {prompt.Kind}/{prompt.Continuation}");
        }
        throw new InvalidOperationException("real attack did not reach the Hidden Pass declaration");
    }

    private static void DrainPostDeclarationWork(L12GameEngine game)
    {
        for (var safety = 0; safety < 32; safety++)
        {
            if (game.State.PendingPrompts.FirstOrDefault() is { } prompt)
            {
                if (prompt.Continuation == "trigger-batch-order")
                    Send(game, prompt.PlayerIndex, new("resolvePrompt", PromptId: prompt.PromptId,
                        CardInstanceIds: [.. prompt.ValidChoices]));
                else if (prompt.ValidChoices.Contains("mode:none"))
                    Send(game, prompt.PlayerIndex, new("resolvePrompt", PromptId: prompt.PromptId, Choice: "mode:none"));
                else if (prompt.Kind == "response" && prompt.ValidChoices.Contains("pass"))
                    Send(game, prompt.PlayerIndex, new("resolvePrompt", PromptId: prompt.PromptId, Choice: "pass"));
                else
                    throw new InvalidOperationException($"unexpected post-declaration prompt {prompt.Kind}/{prompt.Continuation}");
                continue;
            }
            if (game.State.PendingDefense is null && game.State.EffectStack.Count == 0
                && game.State.DeferredEffectStack.Count == 0) return;
        }
        throw new InvalidOperationException("post-attack response did not settle within the command budget");
    }

    private static void PassResponsePrompts(L12GameEngine game)
    {
        for (var safety = 0; safety < 12 && game.State.PendingPrompts.FirstOrDefault() is { Kind: "response" } prompt; safety++)
            Send(game, prompt.PlayerIndex,
                new("resolvePrompt", PromptId: prompt.PromptId, Choice: "pass"));
    }

    private static void PlayFromHand(L12GameEngine game, int actor, L12CardInstance card,
        int? row = null, int? slot = null)
    {
        var payment = game.State.Players[actor].Morale.Where(morale => !morale.Tapped)
            .Take(card.CurrentCost).Select(morale => morale.InstanceId).ToList();
        Require(payment.Count == card.CurrentCost, $"{card.Name} has insufficient active morale");
        Send(game, actor, new("playCard", card.InstanceId, row, slot, CardInstanceIds: payment));
        PassResponsePrompts(game);
    }

    private static void Send(L12GameEngine game, int actor, L12Command command)
    {
        var response = game.Handle(actor, command);
        Require(response.Accepted, $"{command.Type} rejected: {response.Error}");
    }

    private static L12Prompt RequireSinglePrompt(L12GameEngine game, string? continuation = null)
    {
        Require(game.State.PendingPrompts.Count == 1,
            $"expected one prompt, found {game.State.PendingPrompts.Count}");
        var prompt = game.State.PendingPrompts[0];
        if (continuation is not null)
            Require(prompt.Continuation == continuation,
                $"expected {continuation}, found {prompt.Kind}/{prompt.Continuation}");
        return prompt;
    }

    private static L12CardInstance RequireSingleCard(IEnumerable<L12CardInstance> cards, string cardId, string stage)
    {
        var matches = cards.Where(card => card.CardId == cardId).ToArray();
        Require(matches.Length == 1, $"{stage} requires exactly one {cardId}, found {matches.Length}");
        return matches[0];
    }

    private static L12CardInstance RequireSingleOtherHandCard(L12PlayerState player,
        L12CardInstance first, L12CardInstance? second, string stage)
    {
        var others = player.Hand.Where(card => card != first && card != second).ToArray();
        Require(others.Length == 1, $"{stage} requires exactly one other hand card, found {others.Length}");
        return others[0];
    }

    private static List<L12CardInstance> PublicFieldLegions(L12PlayerState player)
        => player.Field.SelectMany(row => row).Where(card => card?.CardType == "legion")
            .Cast<L12CardInstance>().ToList();

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }

    private static FrozenDeckPair LoadFrozenDecks()
    {
        var path = Path.Combine(DataDirectory, "tutorial-decks.v1.json");
        var snapshot = JsonSerializer.Deserialize<FrozenDeckSnapshot>(File.ReadAllText(path), TutorialJson)
            ?? throw new InvalidDataException("tutorial public deck snapshot is unreadable");
        Require(snapshot.SchemaVersion == 1 && snapshot.Decks.Count == 2,
            "tutorial public deck snapshot schema changed");
        var student = snapshot.Decks.Single(deck => deck.Role == "student");
        var opponent = snapshot.Decks.Single(deck => deck.Role == "opponent");
        var studentHash = VerifyDeck(student, StudentPublicCode, StudentContentHash);
        var opponentHash = VerifyDeck(opponent, OpponentPublicCode, OpponentContentHash);
        var studentPreset = ToPreset(student);
        var opponentPreset = ToPreset(opponent);
        Require(L12DeckValidator.TryValidatePreset(Catalog, studentPreset, out _)
                && L12DeckValidator.TryValidatePreset(Catalog, opponentPreset, out _),
            "frozen public tutorial decks are no longer legal presets");
        return new(studentPreset, opponentPreset, studentHash, opponentHash);
    }

    private static string VerifyDeck(FrozenDeckEntry deck, string publicCode, string expectedHash)
    {
        var canonical = JsonSerializer.Serialize(new
        {
            deck.Name, deck.MasterId, deck.CardIds, deck.MoraleIds, deck.SpecialIds,
        }, TutorialJson);
        var actualHash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(canonical)));
        Require(deck.PublicCode == publicCode && deck.PublicationVersion == 1
                && deck.NormalizedContentSha256 == expectedHash && actualHash == expectedHash,
            $"frozen public deck {publicCode} identity or content changed");
        return actualHash;
    }

    private static L12PresetDeckDefinition ToPreset(FrozenDeckEntry deck) => new()
    {
        Name = deck.Name,
        MasterId = deck.MasterId,
        CardIds = [.. deck.CardIds],
        MoraleIds = [.. deck.MoraleIds],
        SpecialIds = [.. deck.SpecialIds],
    };

    private sealed class FrozenDeckSnapshot
    {
        public int SchemaVersion { get; init; }
        public List<FrozenDeckEntry> Decks { get; init; } = [];
    }

    private sealed class FrozenDeckEntry
    {
        public string Role { get; init; } = string.Empty;
        public string PublicCode { get; init; } = string.Empty;
        public int PublicationVersion { get; init; }
        public string NormalizedContentSha256 { get; init; } = string.Empty;
        public string Name { get; init; } = string.Empty;
        public string MasterId { get; init; } = string.Empty;
        public List<string> CardIds { get; init; } = [];
        public List<string> MoraleIds { get; init; } = [];
        public List<string> SpecialIds { get; init; } = [];
    }

    private sealed record FrozenDeckPair(L12PresetDeckDefinition Student, L12PresetDeckDefinition Opponent,
        string StudentHash, string OpponentHash);

    private sealed record RecipeResult(int Seed, int LowHandCount, int MasterDamage, bool EntrantActive,
        bool ReturnedMoraleStayedReturned, string StudentContentHash, string OpponentContentHash);
}
