using System.Reflection;
using TwelveLegions.Server;
using Xunit;

namespace TwelveLegions.Tests;

public sealed class EffectSelectionPurposeTests
{
    [Theory]
    [InlineData("field-legion")]
    [InlineData("hand-card")]
    [InlineData("grave-card")]
    [InlineData("library-card")]
    [InlineData("target-morale")]
    [InlineData("active-target")]
    public void UniqueEffectObjectCannotOptIntoAutomaticSelection(string kind)
    {
        var method = typeof(L12GameEngine).GetMethod("MayAutoSelectDeclaration",
            BindingFlags.NonPublic | BindingFlags.Static)!;
        var effect = new L12ActivationSelectionStep { Kind = kind, Text = "选择对象",
            ValidChoices = ["instance-1"], MinChoose = 1, MaxChoose = 1, AutoSelectWhenExact = true };
        Assert.False((bool)method.Invoke(null, [effect])!);
        var cost = new L12ActivationSelectionStep { Kind = kind, Text = "支付费用",
            ValidChoices = ["instance-1"], MinChoose = 1, MaxChoose = 1,
            AutoSelectWhenExact = true, IsCostSelection = true };
        Assert.True((bool)method.Invoke(null, [cost])!);
    }
}
