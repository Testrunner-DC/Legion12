using System.Reflection;
using TwelveLegions.Server;
using Xunit;

namespace TwelveLegions.Tests;

public sealed class PromptSemanticsRegressionTests
{
    private static readonly string[] ForbiddenPlayerTerms =
    [
        "预先声明", "预先选择", "公开区域", "私密区域", "公开资源", "公开目标", "公开对象",
        "公开登场位置", "私密选择", "独立段", "独立的", "结算模式",
    ];

    private static readonly L12Catalog Catalog = L12Catalog.Load(Path.Combine(AppContext.BaseDirectory, "Data"));

    private static L12GameEngine Create()
        => new(Catalog, "prompt-semantics", "PROMPT", 90201, ["甲", "乙"], [0, 0],
            skipPreparation: true);

    private static L12CardInstance Card()
    {
        var definition = Catalog.Cards["S01-0104"];
        return new L12CardInstance
        {
            InstanceId = "prompt-semantics-source",
            CardId = definition.Id,
            Name = definition.NameZh,
            CardType = definition.CardType,
            Faction = definition.Faction,
            ImageUrl = definition.ImageUrl,
            Cost = definition.Cost ?? 0,
            EffectText = definition.Effect,
            Traits = [.. definition.Traits],
            Profession = definition.Profession,
            BaseTroops = definition.Troops ?? 0,
            Troops = definition.Troops ?? 0,
        };
    }

    private static L12Prompt Begin(L12ActivationSelectionStep step)
    {
        var game = Create();
        var method = typeof(L12GameEngine).GetMethods(BindingFlags.Instance | BindingFlags.NonPublic)
            .Single(candidate => candidate.Name == "BeginPendingActivationSequence"
                && candidate.GetParameters().Length == 7);
        var result = Assert.IsType<CommandResult>(method.Invoke(game,
            [0, Card(), "prompt-semantics", new[] { step }, null, null, null]));
        Assert.True(result.Accepted, result.Error);
        return Assert.Single(game.State.PendingPrompts);
    }

    private static void AssertUniqueProtocolLabels(L12Prompt prompt)
    {
        Assert.Equal(prompt.ValidChoices.Count, prompt.ValidChoices.Distinct(StringComparer.OrdinalIgnoreCase).Count());
        Assert.All(prompt.ValidChoices, choice => Assert.True(prompt.ChoiceLabels.ContainsKey(choice)));
        Assert.Equal(prompt.ValidChoices.Count,
            prompt.ValidChoices.Select(choice => prompt.ChoiceLabels[choice]).Distinct(StringComparer.Ordinal).Count());
    }

    [Theory]
    [InlineData("mode:none", "mode:use")]
    [InlineData("no", "yes")]
    [Trait("L12Evidence", "prompt:explicit-decline-no-synonymous-skip")]
    public void ExplicitDeclineChoicesDoNotReceiveAnAutomaticSkip(string decline, string accept)
    {
        var prompt = Begin(new L12ActivationSelectionStep
        {
            Kind = "option",
            Text = "选择是否发动",
            ValidChoices = [decline, accept],
        });

        Assert.Equal([decline, accept], prompt.ValidChoices);
        Assert.DoesNotContain("skip", prompt.ValidChoices);
        AssertUniqueProtocolLabels(prompt);
    }

    [Fact]
    [Trait("L12Evidence", "prompt:separate-segment-and-activation-cancellation")]
    public void ARealSegmentSkipAndWholeActivationCancellationUseExplicitPolicyAndDifferentLabels()
    {
        var prompt = Begin(new L12ActivationSelectionStep
        {
            Kind = "option",
            Text = "选择是否发动随后效果",
            ValidChoices = ["mode:none", "mode:use"],
            ChoiceLabels = new(StringComparer.OrdinalIgnoreCase)
            {
                ["mode:none"] = "不发动随后效果",
                ["mode:use"] = "发动随后效果",
            },
            CancellationPolicy = L12ActivationCancellationPolicy.SeparateChoice,
        });

        Assert.Contains("mode:none", prompt.ValidChoices);
        Assert.Contains("skip", prompt.ValidChoices);
        Assert.Equal("不发动随后效果", prompt.ChoiceLabels["mode:none"]);
        Assert.Equal("取消整次发动", prompt.ChoiceLabels["skip"]);
        AssertUniqueProtocolLabels(prompt);
    }

    [Fact]
    [Trait("L12Evidence", "prompt:player-visible-internal-terms-gate")]
    public void PromptLabelsAndRejectMessagesRemoveInternalPlanningTermsAtThePublicBoundary()
    {
        var internalText = string.Join('、', ForbiddenPlayerTerms);
        var prompt = Begin(new L12ActivationSelectionStep
        {
            Kind = "option",
            Text = internalText,
            ValidChoices = ["mode:none", "mode:use"],
            ChoiceLabels = new(StringComparer.OrdinalIgnoreCase)
            {
                ["mode:none"] = internalText,
                ["mode:use"] = "发动",
            },
        });
        var rejection = CommandResult.Reject(internalText);

        Assert.All(ForbiddenPlayerTerms, term =>
        {
            Assert.DoesNotContain(term, prompt.Text, StringComparison.Ordinal);
            Assert.DoesNotContain(term, prompt.ChoiceLabels["mode:none"], StringComparison.Ordinal);
            Assert.DoesNotContain(term, rejection.Error!, StringComparison.Ordinal);
        });
    }
}
