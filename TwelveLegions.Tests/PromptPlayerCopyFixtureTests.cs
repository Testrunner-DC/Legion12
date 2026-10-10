using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text.Json;
using TwelveLegions.Server;
using Xunit;

namespace TwelveLegions.Tests;

public sealed class PromptPlayerCopyFixtureTests
{
    [Fact]
    public void NamedEnginePromptMatrixExportsOnlyOwnerSnapshotsWithoutChangingAuthoritativeState()
    {
        // Reuse the named, independently asserted engine scenarios instead of
        // reconstructing their presentation by hand in browser fixtures.
        var cases = new (string Name, string Factory, object[] Extra)[]
        {
            ("oddr-optional", "BeginS1OptionalEnterPrompt", ["S01-0313", "copy", false]),
            ("erik-grave", "BeginAsgardSummonSelectionPrompt", ["copy"]),
            ("joan-hand-cost", "BeginJoanPrompt", ["copy"]),
            ("ring-optional", "BeginRingPrompt", ["copy"]),
            ("magatama-search", "BeginMagatamaPrompt", [true]),
            ("thutmose-followup", "BeginThutmoseTriggeredTargetPrompt", ["attack", 2000]),
        };
        var snapshots = new List<object>();
        foreach (var controller in new[] { 0, 1 })
        foreach (var scenario in cases)
        {
            var factory = typeof(PromptNarrativeMatrixTests).GetMethod(scenario.Factory,
                BindingFlags.NonPublic | BindingFlags.Static);
            Assert.NotNull(factory);
            var tuple = Assert.IsAssignableFrom<ITuple>(factory.Invoke(null,
                [202609300 + controller, controller, .. scenario.Extra]));
            var game = Assert.IsType<L12GameEngine>(tuple[0]);
            var prompt = Assert.IsType<L12Prompt>(tuple[1]);
            Assert.NotNull(prompt.Presentation);
            var before = game.SerializeFullState();
            var owner = game.SnapshotFor(prompt.PlayerIndex);
            var projected = JsonSerializer.SerializeToElement(owner.Prompts,
                new JsonSerializerOptions(JsonSerializerDefaults.Web));
            Assert.Contains(projected.EnumerateArray(), item => item.GetProperty("promptId").GetString() == prompt.PromptId);
            Assert.Empty(game.SnapshotFor(1 - prompt.PlayerIndex).Prompts);
            Assert.Empty(game.SnapshotForSpectator().Prompts);
            Assert.Equal(before, game.SerializeFullState());
            snapshots.Add(new { name = $"{scenario.Name}-p{controller}", game = owner });
        }
        // The named same-name-target response regression supplies both the
        // response decision and the subsequent real Absolute Defense cost.
        object? ResponseFixture(string method, params object?[] arguments)
            => typeof(StackResponseChoiceRegressionTests).GetMethod(method,
                BindingFlags.Static | BindingFlags.NonPublic)!.Invoke(null, arguments);
        var responseGame = Assert.IsType<L12GameEngine>(ResponseFixture("Create"));
        var victim = Assert.IsType<L12CardInstance>(ResponseFixture("Card", "S01-0103", "copy-victim", 1, null, null));
        responseGame.State.Players[1].Field[0][0] = Assert.IsType<L12CardInstance>(ResponseFixture("Card", "S01-0103", "copy-same-name", 1, null, null));
        responseGame.State.Players[1].Field[0][1] = victim;
        var effect = new L12StackItem { StackItemId = "copy-punishment", Controller = 0, Trigger = "play",
            SourceInstanceId = "copy-punishment-card", SourceCardId = "S01-0418", SourceName = "天诛",
            Text = "击杀对方1张费用不高于7的军团。" };
        effect.Targets.Add(victim.InstanceId);
        responseGame.State.EffectStack.Add(effect);
        var counter = Assert.IsType<L12CardInstance>(ResponseFixture("Counter", responseGame, 0, "S01-0016"));
        responseGame.State.Players[1].Hand.Add(Assert.IsType<L12CardInstance>(ResponseFixture("Card", "S01-0003", "copy-payment", 1, null, null)));
        ResponseFixture("Offer", responseGame, 1);
        foreach (var step in new[] { "response-selected-target", "response-discard-cost" })
        {
            var current = Assert.Single(responseGame.State.PendingPrompts);
            Assert.Contains($"你的前排中格〈{victim.Name}〉", current.Text);
            snapshots.Add(new { name = step, game = JsonSerializer.SerializeToElement(responseGame.SnapshotFor(1),
                new JsonSerializerOptions(JsonSerializerDefaults.Web)) });
            if (step == "response-selected-target") ResponseFixture("Resolve", responseGame, counter.InstanceId);
        }
        Assert.Equal(14, snapshots.Count);
        // Opt-in QA output only; normal regression runs do not write fixtures.
        if (Environment.GetEnvironmentVariable("L12_PROMPT_COPY_FIXTURES") is { Length: > 0 } output)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(output))!);
            File.WriteAllText(output, JsonSerializer.Serialize(snapshots,
                new JsonSerializerOptions(JsonSerializerDefaults.Web) { WriteIndented = true }));
        }
    }
}
