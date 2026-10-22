using System.Text.Json;
using System.Text.Json.Nodes;
using TwelveLegions.Server;
using Xunit;
using Xunit.Abstractions;

namespace TwelveLegions.Platform.Tests;

public sealed class EventWindowDeltaTests(ITestOutputHelper output)
{
    private static byte[] Bytes(JsonNode node) => L12SnapshotJson.SerializeToUtf8Bytes(node);
    private static JsonObject Frame(long revision, IEnumerable<long> sequences) => new()
    {
        ["type"] = "gameState", ["state"] = new JsonObject
        {
            ["matchId"] = "window-test", ["revision"] = revision, ["stable"] = new string('稳', 2400),
            ["recentEvents"] = new JsonArray(sequences.Select(n => (JsonNode)new JsonObject
            {
                ["sequence"] = n, ["type"] = "move", ["text"] = $"frozen-{n}",
                ["cards"] = new JsonArray(new JsonObject { ["instanceId"] = $"card-{n}", ["effectText"] = new string('史', 80),
                    ["costModifier"] = -2, ["timedModifiers"] = new JsonArray(new JsonObject { ["troopsDelta"] = 1000, ["source"] = "frozen-source", ["consumedTroopsBonus"] = 3 }),
                    ["attachedCards"] = new JsonArray(new JsonObject { ["name"] = "same", ["instanceId"] = $"a-{n}" },new JsonObject { ["name"] = "same", ["instanceId"] = $"b-{n}" }) }),
            }).ToArray()), ["lastAction"] = new JsonObject { ["text"] = "independent-last-action", ["cards"] = new JsonArray() },
        },
    };
    private static L12PreparedWirePayload Next(L12SnapshotWireCodec codec, JsonObject before, JsonObject after)
    {
        var first = codec.Prepare(new(Bytes(before),true,false)); first.Commit();
        return codec.Prepare(new(Bytes(after),true,false));
    }
    private static JsonObject Root(L12PreparedWirePayload wire) => JsonNode.Parse(wire.Payload)!.AsObject();

    [Theory]
    [InlineData(127,0,1)]
    [InlineData(128,1,1)]
    [InlineData(128,6,6)]
    [InlineData(128,1,0)]
    [InlineData(3,1,2)]
    public void AppendingAndSlidingPreservesEveryFrozenFieldAndOrder(int count,int remove,int append)
    {
        var oldSequences=Enumerable.Range(0,count).Select(i=>1L+i*3).ToArray();
        var newSequences=oldSequences.Skip(remove).Concat(Enumerable.Range(1,append).Select(i=>oldSequences[^1]+i*4));
        var before=Frame(1,oldSequences); var after=Frame(2,newSequences);
        var codec=new L12SnapshotWireCodec(); codec.SetDeltaEnabled(true,true);
        var wire=Next(codec,before,after); Assert.True(wire.IsDelta);
        var root=Root(wire); var window=root["eventWindow"]!;
        Assert.Equal(remove,window["removeFirst"]!.GetValue<int>());
        Assert.Equal(append,window["append"]!.AsArray().Count);
        Assert.DoesNotContain(root["changes"]!.AsArray(), c=>c!["path"]!.ToJsonString().Contains("recentEvents"));
        Assert.True(JsonNode.DeepEquals(after,L12SnapshotWireCodec.ApplyDelta(before,wire.Payload)));
        output.WriteLine($"Anonymous synthetic count={count} remove={remove} append={append} full={Bytes(after).Length}B window={wire.Payload.Length}B");
    }

    [Theory]
    [InlineData("text")]
    [InlineData("cards")]
    [InlineData("playerPresentationFactSequences")]
    public void ChangedHistoricalProjectionUsesOriginalDiffEvenWithSameSequence(string field)
    {
        var before=Frame(1,[1,4,8]); var after=Frame(2,[4,8,12]);
        after["state"]!["recentEvents"]![0]![field]=field=="cards" ? new JsonArray() : JsonValue.Create("changed");
        var codec=new L12SnapshotWireCodec(); codec.SetDeltaEnabled(true,true);
        var wire=Next(codec,before,after); Assert.True(wire.IsDelta); Assert.False(Root(wire).ContainsKey("eventWindow"));
        Assert.True(JsonNode.DeepEquals(after,L12SnapshotWireCodec.ApplyDelta(before,wire.Payload)));
    }

    [Theory]
    [InlineData("duplicate")]
    [InlineData("unordered")]
    [InlineData("zero")]
    [InlineData("negative")]
    [InlineData("unsafe")]
    [InlineData("fraction")]
    [InlineData("string")]
    [InlineData("null")]
    [InlineData("too-long")]
    public void InvalidWindowEligibilityNeverDropsOrdinaryChanges(string kind)
    {
        var before=Frame(1,[1,4,8]); var after=Frame(2,[4,8,12]);
        var events=after["state"]!["recentEvents"]!.AsArray();
        if(kind=="too-long") after["state"]!["recentEvents"]=Frame(2,Enumerable.Range(1,129).Select(i=>(long)i))["state"]!["recentEvents"]!.DeepClone();
        else events[2]!["sequence"]=kind switch { "duplicate"=>JsonValue.Create(8L),"unordered"=>JsonValue.Create(3L),"zero"=>JsonValue.Create(0L),
            "negative"=>JsonValue.Create(-1L),"unsafe"=>JsonValue.Create(9007199254740992L),"fraction"=>JsonValue.Create(12.5),"string"=>JsonValue.Create("12"),_=>null };
        var codec=new L12SnapshotWireCodec(); codec.SetDeltaEnabled(true,true);
        var wire=Next(codec,before,after);
        if(wire.IsDelta) { Assert.False(Root(wire).ContainsKey("eventWindow")); Assert.True(JsonNode.DeepEquals(after,L12SnapshotWireCodec.ApplyDelta(before,wire.Payload))); }
        else Assert.True(JsonNode.DeepEquals(after,Root(wire)));
    }

    [Theory]
    [InlineData(false,false)]
    [InlineData(false,true)]
    [InlineData(true,false)]
    public void UnnegotiatedConnectionsNeverReceiveWindowProtocol(bool delta,bool window)
    {
        var before=Frame(1,[1,4,8]); var after=Frame(2,[4,8,12]);
        var codec=new L12SnapshotWireCodec(); codec.SetDeltaEnabled(delta,window);
        var wire=Next(codec,before,after); Assert.False(Root(wire).ContainsKey("eventWindow"));
        Assert.True(JsonNode.DeepEquals(after,wire.IsDelta ? L12SnapshotWireCodec.ApplyDelta(before,wire.Payload) : Root(wire)));
    }

    [Fact]
    public void FailedSendUsesLastSuccessfulBaseAndCapabilityChangeForcesFull()
    {
        var a=Frame(1,[1,4,8]); var b=Frame(2,[4,8,12]); var c=Frame(3,[8,12,16]);
        var codec=new L12SnapshotWireCodec(); codec.SetDeltaEnabled(true,true);
        var notCommitted=Next(codec,a,b); Assert.NotNull(Root(notCommitted)["eventWindow"]);
        var retry=codec.Prepare(new(Bytes(c),true,false));
        Assert.Equal(1,Root(retry)["baseRevision"]!.GetValue<int>());
        Assert.True(JsonNode.DeepEquals(c,L12SnapshotWireCodec.ApplyDelta(a,retry.Payload)));
        retry.Commit(); codec.SetDeltaEnabled(true,false);
        Assert.False(codec.Prepare(new(Bytes(Frame(4,[12,16,20])),true,false)).IsDelta);
    }

    [Fact]
    public void FullRecoveryFinalIntervalAndMatchBoundaryStillForceFull()
    {
        var codec=new L12SnapshotWireCodec(); codec.SetDeltaEnabled(true,true);
        for(var i=0;i<=32;i++)
        {
            var full=Frame(i+1,Enumerable.Range(i+1,128).Select(n=>(long)n));
            var wire=codec.Prepare(new(Bytes(full),true,false));
            Assert.Equal(i is not (0 or 32),wire.IsDelta); wire.Commit();
        }
        Assert.False(codec.Prepare(new(Bytes(Frame(40,[130,131])),true,true)).IsDelta);
        var other=Frame(41,[130,131]); other["state"]!["matchId"]="another-match";
        Assert.False(codec.Prepare(new(Bytes(other),true,false)).IsDelta);
    }

    [Fact]
    public void NonOverlappingEmptyAndUnchangedWindowsKeepOriginalPath()
    {
        foreach(var seqs in new long[][] { [12,14],[],[1,4,8] })
        {
            var before=Frame(1,[1,4,8]);var after=Frame(2,seqs);var codec=new L12SnapshotWireCodec();codec.SetDeltaEnabled(true,true);
            var wire=Next(codec,before,after);Assert.True(wire.IsDelta);Assert.False(Root(wire).ContainsKey("eventWindow"));
            Assert.True(JsonNode.DeepEquals(after,L12SnapshotWireCodec.ApplyDelta(before,wire.Payload)));
        }
    }

    [Fact]
    public void MaximumGenericChangesStillForceFullWithWindowEnabled()
    {
        var before=Frame(1,[1,4,8]);var after=Frame(2,[4,8,12]);
        before["state"]!["large"]=new JsonObject();after["state"]!["large"]=new JsonObject();
        for(var i=0;i<4200;i++){before["state"]!["large"]![$"k{i}"]=0;after["state"]!["large"]![$"k{i}"]=1;}
        var codec=new L12SnapshotWireCodec();codec.SetDeltaEnabled(true,true);
        Assert.False(Next(codec,before,after).IsDelta);
    }

    [Fact]
    public void NinetyPercentSizeBudgetStillForcesFullWithWindowEnabled()
    {
        var before=Frame(1,[1,4,8]);var after=Frame(2,[4,8,12]);
        before["state"]!["stable"]="a";after["state"]!["stable"]=new string('变',16000);
        var codec=new L12SnapshotWireCodec();codec.SetDeltaEnabled(true,true);
        Assert.False(Next(codec,before,after).IsDelta);
    }

    [Theory]
    [InlineData("base")]
    [InlineData("result")]
    [InlineData("negative-remove")]
    [InlineData("overflow-remove")]
    [InlineData("zero-overlap")]
    [InlineData("string-remove")]
    [InlineData("fraction-remove")]
    [InlineData("duplicate-append")]
    [InlineData("conflict")]
    public void DecoderRejectsBadWindowWithoutMutatingBaseline(string fault)
    {
        var before=Frame(1,[1,4,8]); var frozen=Bytes(before); var after=Frame(2,[4,8,12]);
        var codec=new L12SnapshotWireCodec();codec.SetDeltaEnabled(true,true);var delta=Root(Next(codec,before,after)); var w=delta["eventWindow"]!;
        switch(fault)
        {
            case "base": w["baseSequences"]![0]=2; break;
            case "result": w["resultSequences"]![0]=2; break;
            case "negative-remove": w["removeFirst"]=-1; break;
            case "overflow-remove": w["removeFirst"]=4; break;
            case "zero-overlap": w["removeFirst"]=3; w["resultSequences"]=new JsonArray(12); break;
            case "string-remove": w["removeFirst"]="1"; break;
            case "fraction-remove": w["removeFirst"]=1.5; break;
            case "duplicate-append": w["append"]!.AsArray().Add(w["append"]![0]!.DeepClone()); break;
            case "conflict": delta["changes"]!.AsArray().Add(new JsonObject{["path"]=new JsonArray("state","recentEvents"),["value"]=new JsonArray(),["remove"]=false}); break;
        }
        Assert.ThrowsAny<Exception>(()=>L12SnapshotWireCodec.ApplyDelta(before,Bytes(delta)));
        Assert.Equal(frozen,Bytes(before));
    }

    [Fact]
    public void DecoderRejectsEmptyBaselineWithoutPublishingAnyAppend()
    {
        var before=Frame(1,[]);var frozen=Bytes(before);
        var baseFrame=Frame(1,[1,4,8]);var after=Frame(2,[4,8,12]);
        var codec=new L12SnapshotWireCodec();codec.SetDeltaEnabled(true,true);
        var delta=Root(Next(codec,baseFrame,after));
        delta["eventWindow"]!["baseSequences"]=new JsonArray();
        delta["eventWindow"]!["removeFirst"]=0;
        delta["eventWindow"]!["resultSequences"]=new JsonArray(12);
        Assert.Throws<InvalidDataException>(()=>L12SnapshotWireCodec.ApplyDelta(before,Bytes(delta)));
        Assert.Equal(frozen,Bytes(before));
    }
}
