using System.Reflection;
using System.Text.Json;
using TwelveLegions.Server;
using Xunit;

namespace TwelveLegions.Tests;

public sealed class Stage3AActivationPlayerInformationTests
{
    private static readonly L12Catalog Catalog = L12Catalog.Load(Path.Combine(AppContext.BaseDirectory, "Data"));

    [Fact]
    [Trait("L12Evidence", "battle-info-3a:pending-cost-and-private-projection")]
    public void CostChoiceExplainsPendingPaymentWithoutTellingOtherRecipientsItsPrivateCandidates()
    {
        var game = Game();
        var source = Card("S01-0103", "3a-source", 0);
        game.State.Players[0].Field[0][0] = source;
        var activation = Activation(source,
        [new L12ActivationSelectionStep
        {
            Kind = "resource-payment", Text = "选择1张士气支付主动效果费用",
            ValidChoices = ["3a-morale-a", "3a-morale-b"], MinChoose = 1, MaxChoose = 1,
            IsCostSelection = true, DeclarationKey = "cost",
        }]);
        game.State.PendingActivations.Add(activation);

        Render(game, activation);

        var prompt = Assert.Single(game.State.PendingPrompts);
        Assert.Equal(source.Name, prompt.Presentation!.Title);
        Assert.Equal("pending", prompt.Presentation.PaymentStatus);
        Assert.Contains("1张士气", prompt.Presentation.PaymentSummary);
        Assert.Contains("费用", prompt.Presentation.Instruction);
        Assert.Contains("提交本次声明", prompt.Presentation.SubmissionConsequence);
        Assert.Contains("取消本次发动声明", prompt.Presentation.ChoiceConsequences["skip"]);
        Assert.Empty(game.SnapshotFor(1).Prompts);
        Assert.Empty(game.SnapshotForSpectator().Prompts);
        Assert.Empty(game.SnapshotForReferee().Prompts);
        foreach (var waiting in new[]
                 {
                     game.SnapshotFor(1).WaitingPrompt,
                     game.SnapshotForSpectator().WaitingPrompt,
                     game.SnapshotForReferee().WaitingPrompt,
                 })
        {
            Assert.NotNull(waiting);
            var summary = JsonSerializer.SerializeToElement(waiting,
                new JsonSerializerOptions(JsonSerializerDefaults.Web))
                .GetProperty("waitingSummary").GetString();
            Assert.DoesNotContain("士气", summary);
            Assert.DoesNotContain("3a-morale", summary);
        }
    }

    [Fact]
    [Trait("L12Evidence", "battle-info-3a:declared-cost-not-paid")]
    public void SelectingAnEarlierCostDoesNotClaimItWasAlreadyPaidAtTargetStep()
    {
        var game = Game();
        var source = Card("S01-0103", "3a-source-two", 0);
        var target = Card("S01-0002", "3a-target", 1);
        game.State.Players[0].Field[0][0] = source;
        game.State.Players[1].Field[0][0] = target;
        var activation = Activation(source,
        [
            new L12ActivationSelectionStep
            {
                Kind = "resource-payment", Text = "选择1张士气支付主动效果费用",
                ValidChoices = ["3a-morale-a", "3a-morale-b"], MinChoose = 1, MaxChoose = 1,
                IsCostSelection = true, DeclarationKey = "cost",
            },
            new L12ActivationSelectionStep
            {
                Kind = "active-target", Text = "选择对方前排1张军团作为目标",
                ValidChoices = [target.InstanceId], MinChoose = 1, MaxChoose = 1,
                IsResponsePresentationTarget = true, DeclarationKey = "target",
            },
        ]);
        activation.CurrentStep = 1;
        activation.DeclaredValues["cost"] = ["3a-morale-a"];
        game.State.PendingActivations.Add(activation);

        Render(game, activation);

        var prompt = Assert.Single(game.State.PendingPrompts);
        Assert.Equal("pending", prompt.Presentation!.PaymentStatus);
        Assert.Contains("1张士气", prompt.Presentation.PaymentSummary);
        Assert.Contains("选择对方前排", prompt.Presentation.Situation);
        Assert.Contains("1项", prompt.Presentation.Instruction);
        Assert.DoesNotContain("已支付", prompt.Presentation.PaymentSummary);
        Assert.Empty(game.SnapshotFor(1).Prompts);
    }

    [Fact]
    [Trait("L12Evidence", "battle-info-3a:optional-versus-mandatory")]
    public void OptionalDeclineExplainsThatMandatoryFollowingStepsRemain()
    {
        var game = Game();
        var source = Card("S01-0103", "3a-optional-source", 0);
        game.State.Players[0].Field[0][0] = source;
        var activation = Activation(source,
        [
            new L12ActivationSelectionStep
            {
                Kind = "option", Text = "是否执行本次可选效果",
                ValidChoices = ["mode:use", "mode:none"], MinChoose = 1, MaxChoose = 1,
                CancellationPolicy = L12ActivationCancellationPolicy.NotAllowed,
            },
            new L12ActivationSelectionStep
            {
                Kind = "active-target", Text = "选择后续必须处理的对象",
                ValidChoices = ["later"], MinChoose = 1, MaxChoose = 1,
            },
        ]);
        game.State.PendingActivations.Add(activation);

        Render(game, activation);

        var prompt = Assert.Single(game.State.PendingPrompts);
        Assert.Contains("后续必须完成", prompt.Presentation!.ChoiceConsequences["mode:none"]);
        Assert.Contains("下一步声明", prompt.Presentation.SubmissionConsequence);
        Assert.Null(prompt.Presentation.PaymentStatus);
        Assert.DoesNotContain("skip", prompt.ValidChoices);
    }

    [Fact]
    [Trait("L12Evidence", "battle-info-3a:cancelled-declaration")]
    public void CancelChoiceEndsThePendingDeclarationWithoutClaimingAnEffectResult()
    {
        var game = Game();
        var source = Card("S01-0103", "3a-cancel-source", 0);
        game.State.Players[0].Field[0][0] = source;
        var activation = Activation(source,
        [new L12ActivationSelectionStep
        {
            Kind = "active-target", Text = "选择本次效果的对象",
            ValidChoices = ["target-a", "target-b"], MinChoose = 1, MaxChoose = 1,
        }]);
        game.State.PendingActivations.Add(activation);
        Render(game, activation);
        var prompt = Assert.Single(game.State.PendingPrompts);
        Assert.Contains("取消本次发动声明", prompt.Presentation!.ChoiceConsequences["skip"]);

        Assert.True(game.Handle(0, new L12Command("resolvePrompt", PromptId: prompt.PromptId,
            Choice: "skip")).Accepted);

        Assert.Empty(game.State.PendingActivations);
        Assert.Empty(game.State.PendingPrompts);
        Assert.Empty(game.State.EffectStack);
        Assert.Contains(game.State.Events, item => item.Type == "ability-cancelled");
    }

    [Fact]
    [Trait("L12Evidence", "battle-info-3a:no-legal-target")]
    public void NoLegalObjectRejectsTheDeclarationWithoutShowingAFalseChoiceOrPaidCost()
    {
        var game = Game();
        var source = Card("S01-0103", "3a-no-target-source", 0);
        game.State.Players[0].Field[0][0] = source;
        var activation = Activation(source,
        [new L12ActivationSelectionStep
        {
            Kind = "active-target", Text = "选择本次效果的合法对象",
            ValidChoices = [], MinChoose = 1, MaxChoose = 1,
        }]);
        game.State.PendingActivations.Add(activation);

        Render(game, activation);

        Assert.Empty(game.State.PendingPrompts);
        Assert.Empty(game.State.PendingActivations);
        Assert.Contains(game.State.Events, item => item.Type == "ability-rejected");
        Assert.Empty(game.State.EffectStack);
    }

    private static L12GameEngine Game() => new(Catalog, "battle-info-3a", "BINFO-3A", 20260929,
        ["甲", "乙"], [0, 0], skipPreparation: true, stateFormatVersion: 2);

    private static L12PendingActivation Activation(L12CardInstance source,
        List<L12ActivationSelectionStep> steps) => new()
    {
        ActivationId = $"3a-{source.InstanceId}", Controller = 0,
        SourceInstanceId = source.InstanceId, SourceCardId = source.CardId,
        Ability = "stage3a-presentation", Text = "本次效果声明", ValidChoices = [],
        CreatedRevision = 0, SelectionSteps = steps,
    };

    private static L12CardInstance Card(string cardId, string instanceId, int owner)
    {
        var definition = Catalog.Cards[cardId];
        return new L12CardInstance
        {
            InstanceId = instanceId, CardId = cardId, Name = definition.NameZh,
            CardType = definition.CardType, Faction = definition.Faction,
            ImageUrl = definition.ImageUrl, Cost = definition.Cost ?? 0,
            EffectText = definition.Effect, Traits = [.. definition.Traits],
            Profession = definition.Profession, EffectiveProfession = definition.Profession,
            BaseTroops = definition.Troops ?? 0, Troops = definition.Troops ?? 0,
            OwnerIndex = owner, SummonRound = -1,
        };
    }

    private static void Render(L12GameEngine game, L12PendingActivation activation)
    {
        var method = typeof(L12GameEngine).GetMethod("CreateActivationStepPrompt",
            BindingFlags.Instance | BindingFlags.NonPublic)!;
        method.Invoke(game, [activation]);
    }
}
