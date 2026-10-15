using System.Text.Json;
using TwelveLegions.Server;
using Xunit;

namespace TwelveLegions.Tests;

public sealed class CanopicRelicPresenceRegressionTests
{
    private static readonly L12Catalog Catalog = L12Catalog.Load(Path.Combine(AppContext.BaseDirectory, "Data"));
    private static readonly JsonSerializerOptions Wire = new(JsonSerializerDefaults.Web);
    private static readonly string[] Family = ["S01-0216", "S01-0217", "S01-0218", "S01-0219", "S01-0220"];
    private static readonly DateTimeOffset Now = DateTimeOffset.Parse("2026-10-15T08:00:00Z");

    private static L12CardInstance Card(string id, string instance, int owner, string? name=null)
    {
        var d = Catalog.Cards[id];
        return new L12CardInstance { InstanceId=instance, CardId=id, OwnerIndex=owner,
            Name=name??d.NameZh, CardType=d.CardType, IsCounterTactic=d.IsCounterTactic, Faction=d.Faction,
            ImageUrl=d.ImageUrl, Cost=d.Cost??0, HasPrintedCost=d.Cost.HasValue, EffectText=d.Effect,
            Traits=[..d.Traits], Profession=d.Profession, BaseTroops=d.Troops??0, Troops=d.Troops??0,
            DisasterLevel=d.DisasterLevel??0, TrialValue=d.TrialValue??0, SummonRound=-1 };
    }

    private static L12GameEngine Create(int owner, string master="S01-02M1")
    {
        var basis=Catalog.DeckAt(0);
        var deck=new L12PresetDeckDefinition { Name="合成罐留场", MasterId=master,
            CardIds=[..basis.CardIds], MoraleIds=[..basis.MoraleIds], SpecialIds=[] };
        var game=new L12GameEngine(Catalog,"synthetic-canopic-presence","LOCAL",20261015,
            ["合成甲","合成乙"],[deck,deck],skipPreparation:true,disasterMode:"none",
            autoPassEmptyResponses:false,concealHiddenResponseAvailability:false,
            utcNow:()=>Now,stateFormatVersion:2);
        game.State.PendingPrompts.Clear(); game.State.ActivePlayer=owner; game.State.FirstPlayer=owner;
        game.State.Round=3; game.State.TurnSerial=10; game.State.Phase=L12Phase.Main;
        foreach(var p in game.State.Players)
        {
            foreach(var row in p.Field) Array.Clear(row);
            p.Hand.Clear();p.Library.Clear();p.Graveyard.Clear();p.Removed.Clear();p.Resolving.Clear();
            p.Relic=null;p.ExtraRelics.Clear();p.SpecialZones.CanopicProgress.Clear();
            p.SpecialZones.Trials.Clear();p.SpecialZones.GodPower.Clear();p.Morale.Clear();p.UsedAbilities.Clear();
            for(var i=0;i<10;i++)p.Library.Add(Card("S01-0003",$"filler-{p.PlayerIndex}-{i}",p.PlayerIndex));
        }
        return game;
    }
    private static L12GameEngine Restore(L12GameEngine game,bool restore)=>!restore?game:
        L12GameEngine.RestoreCheckpoint(Catalog,game.SerializeFullState(),game.RandomState!.Value,
            game.CardFactSignalSequence,autoPassEmptyResponses:false,concealHiddenResponseAvailability:false,utcNow:()=>Now);
    private static void Handle(L12GameEngine game,int actor,L12Command command)
    {var result=game.Handle(actor,command);Assert.True(result.Accepted,result.Error);}
    private static void Resolve(L12GameEngine game,string choice)
    {var prompt=Assert.Single(game.State.PendingPrompts);Handle(game,prompt.PlayerIndex,new("resolvePrompt",PromptId:prompt.PromptId,Choice:choice));}
    private static void Pass(L12GameEngine game)
    {for(var i=0;i<60&&game.State.PendingPrompts.FirstOrDefault()?.Kind=="response";i++)Resolve(game,"pass");Assert.NotEqual("response",game.State.PendingPrompts.FirstOrDefault()?.Kind);}
    private static JsonElement Player(L12GameSnapshot snapshot,int owner)=>JsonSerializer.SerializeToElement(snapshot,Wire).GetProperty("players")[owner];
    private static IEnumerable<L12CardInstance> All(L12PlayerState p)=>p.Hand.Concat(p.Library).Concat(p.Graveyard)
        .Concat(p.Removed).Concat(p.ExtraRelics).Concat(p.SpecialZones.CanopicProgress).Concat(p.Resolving)
        .Concat(p.Field.SelectMany(row=>row).OfType<L12CardInstance>()).Concat(p.Relic is null?[]:[p.Relic]);
    private static bool Lit(L12GameSnapshot snapshot,int owner,string id)=>Player(snapshot,owner)
        .GetProperty("specialZones").GetProperty("canopicTrack").EnumerateArray()
        .Single(c=>c.GetProperty("cardId").GetString()==id).GetProperty("completed").GetBoolean();

    public static IEnumerable<object[]> EntryCases()
    {foreach(var id in Family)foreach(var owner in new[]{0,1})foreach(var master in new[]{"S01-02M1","ST02-M1"})foreach(var restore in new[]{false,true})yield return[id,owner,master,restore];}
    [Theory][MemberData(nameof(EntryCases))]
    public void NegatedEntryRemainsExemptAndOrdinaryRelicCanStillEnter(string id,int owner,string master,bool recover)
    {
        var game=Create(owner,master);var p=game.State.Players[owner];
        p.Field[0][0]=Card("S01-0203","public-legion",owner);
        p.Hand.Add(Card(id,"entry-canopic",owner));
        var counter=Card("S01-0016","absolute-defense",1-owner);counter.Hidden=true;counter.SetRound=0;
        game.State.Players[1-owner].Field[1][2]=counter;
        game.State.Players[1-owner].Hand.Add(Card("S01-0003","counter-payment",1-owner));
        Handle(game,owner,new("playCard","entry-canopic"));
        Assert.Null(p.Relic);
        Assert.Single(p.ExtraRelics,c=>c.InstanceId=="entry-canopic");
        for(var n=0;n<12&&game.State.PendingPrompts.FirstOrDefault()?.Kind!="response";n++)Resolve(game,"public-legion");
        if(Assert.Single(game.State.PendingPrompts).PlayerIndex==owner)Resolve(game,"pass");
        Resolve(game,"absolute-defense");game=Restore(game,recover);Resolve(game,"counter-payment");
        game=Restore(game,recover);Pass(game);p=game.State.Players[owner];
        Assert.Single(p.ExtraRelics,c=>c.InstanceId=="entry-canopic");
        Assert.DoesNotContain(p.Graveyard,c=>c.InstanceId=="entry-canopic");
        if(master=="S01-02M1")Assert.True(Lit(game.SnapshotFor(owner),owner,id));
        p.TemporaryMorale=10;p.Hand.Add(Card("S01-0215","ordinary-after-canopic",owner));
        Handle(game,owner,new("playCard","ordinary-after-canopic"));Pass(game);
        Assert.Equal("ordinary-after-canopic",p.Relic?.InstanceId);
        Assert.Single(p.ExtraRelics,c=>c.InstanceId=="entry-canopic");
        Assert.Single(All(p),c=>c.InstanceId=="entry-canopic");
        var result=game.HandleGm(new("returnCardToHand",owner,CardInstanceId:"entry-canopic"));
        Assert.True(result.Accepted,result.Error);
        Assert.Single(p.Hand,c=>c.InstanceId=="entry-canopic");
        if(master=="S01-02M1")Assert.False(Lit(game.SnapshotFor(owner),owner,id));
        Assert.Empty(game.State.EffectStack);Assert.Empty(game.State.PendingPrompts);
        Assert.Empty(game.State.PendingActivations);Assert.Empty(game.State.PendingTriggerStackCandidates);
    }

    public static IEnumerable<object[]> PresenceCases()
    {foreach(var id in Family)foreach(var zone in new[]{"relic","extra","progress"})foreach(var owner in new[]{0,1})foreach(var restore in new[]{false,true})yield return[id,zone,owner,restore];}
    [Theory][MemberData(nameof(PresenceCases))]
    public void AllRealRelicRepresentationsLightTheSameMarkerAndEveryRecipientIsPure(string id,string zone,int owner,bool recover)
    {
        var game=Create(owner);var p=game.State.Players[owner];var card=Card(id,"present-canopic",owner);
        if(zone=="relic")p.Relic=card;else if(zone=="extra")p.ExtraRelics.Add(card);else p.SpecialZones.CanopicProgress.Add(card);
        game=Restore(game,recover);var before=game.SerializeFullState();var hash=game.ComputeStateHash();
        var random=game.RandomState;var sequence=game.CardFactSignalSequence;
        for(var n=0;n<2;n++)foreach(var snapshot in new[]{game.SnapshotFor(owner),game.SnapshotFor(1-owner),game.SnapshotForSpectator(),game.SnapshotForReferee()})
        {
            Assert.True(Lit(snapshot,owner,id));Assert.Equal(hash,snapshot.StateHash);
            Assert.Equal(before,game.SerializeFullState());Assert.Equal(hash,game.ComputeStateHash());
            Assert.Equal(random,game.RandomState);Assert.Equal(sequence,game.CardFactSignalSequence);
            Assert.Single(All(game.State.Players[owner]),c=>c.InstanceId=="present-canopic");
        }
    }

    [Theory][MemberData(nameof(PresenceCases))]
    public void LaterOrdinaryPlacementPreservesLegacyPrimaryOrOtherRealCanopic(string id,string zone,int owner,bool recover)
    {
        var game=Create(owner,"ST02-M1");var p=game.State.Players[owner];var card=Card(id,"legacy-canopic",owner);
        if(zone=="relic")p.Relic=card;else if(zone=="extra")p.ExtraRelics.Add(card);else p.SpecialZones.CanopicProgress.Add(card);
        game=Restore(game,recover);p=game.State.Players[owner];
        p.TemporaryMorale=10;p.Hand.Add(Card("S01-0215","ordinary",owner));
        Handle(game,owner,new("playCard","ordinary"));Pass(game);
        Assert.Equal("ordinary",p.Relic?.InstanceId);Assert.DoesNotContain(p.Graveyard,c=>c.InstanceId=="legacy-canopic");
        Assert.Single(All(p),c=>c.InstanceId=="legacy-canopic");
    }

    [Theory][InlineData(0,false)][InlineData(0,true)][InlineData(1,false)][InlineData(1,true)]
    public void FiveDistinctRealCanopicsAcrossStorageEnableTheExistingVictory(int owner,bool recover)
    {
        var game=Create(owner);var p=game.State.Players[owner];
        p.Relic=Card(Family[0],"victory-primary",owner);
        foreach(var id in Family.Skip(1).Take(3))p.ExtraRelics.Add(Card(id,$"victory-{id}",owner));
        p.SpecialZones.CanopicProgress.Add(Card(Family[4],"victory-progress",owner));
        p.Graveyard.Add(Card("S01-02M2","victory-osiris",owner));game=Restore(game,recover);
        var ability=Player(game.SnapshotFor(owner),owner).GetProperty("master").GetProperty("abilities")
            .EnumerateArray().Single(a=>a.GetProperty("id").GetString()=="isisVictory");
        Assert.True(ability.GetProperty("enabled").GetBoolean());
        Handle(game,owner,new("activateAbility","victory-osiris",Ability:"isisVictory"));Pass(game);
        Assert.Equal(owner,game.State.Winner);Assert.Single(game.State.Events,e=>e.Type=="special-victory");
    }

    [Theory][InlineData(0,false)][InlineData(0,true)][InlineData(1,false)][InlineData(1,true)]
    public void IsisGraveCandidatesExcludeATypeAlreadyPresentInAnOrdinaryRelicStorage(int owner,bool recover)
    {
        var game=Create(owner);var p=game.State.Players[owner];
        for(var i=0;i<3;i++)p.Field[0][i]=Card("S01-0212",$"guard-{i}",owner);
        p.ExtraRelics.Add(Card(Family[0],"already-present",owner));
        p.Graveyard.Add(Card(Family[0],"duplicate-type",owner));p.Graveyard.Add(Card(Family[1],"available-type",owner));
        game=Restore(game,recover);Handle(game,owner,new("activateAbility",$"master-{owner}",Ability:"isisCanopic"));
        var prompt=Assert.Single(game.State.PendingPrompts);
        Assert.Contains("available-type",prompt.ValidChoices);Assert.DoesNotContain("duplicate-type",prompt.ValidChoices);
    }

    [Fact]
    public void DuplicateTypeAndRenamedUnrelatedArtifactCannotCompleteFiveKinds()
    {
        var game=Create(0);var p=game.State.Players[0];
        for(var i=0;i<5;i++)p.ExtraRelics.Add(Card(Family[0],$"same-type-{i}",0));
        var spoof=Card("S01-0215","spoof",0,"卡诺匹斯罐 四");p.Relic=spoof;
        p.Graveyard.Add(Card("S01-02M2","osiris",0));
        var snapshot=game.SnapshotFor(0);Assert.True(Lit(snapshot,0,Family[0]));Assert.False(Lit(snapshot,0,Family[4]));
        Assert.False(game.Handle(0,new("activateAbility","osiris",Ability:"isisVictory")).Accepted);
        Assert.Null(game.State.Winner);
    }

    [Fact]
    public void CanopicIdentityIsExactlyTheFiveStructuredRelicsInTheCurrentCatalog()
    {
        Assert.Equal(Family.Order(),Catalog.Cards.Keys
            .Where(L12StructuredCardSemantics.IgnoresRelicZoneLimit).Order());
        foreach(var id in Family) Assert.Equal("artifact",Catalog.Cards[id].CardType);
    }

    [Fact]
    public void ExportAuthoritativePresenceSnapshotsForRealComponentAcceptance()
    {
        var fixtures=new List<object>();
        foreach(var master in new[]{"S01-02M1","ST02-M1"})
        foreach(var owner in new[]{0,1})
        foreach(var scenario in new[]{"one-extra","one-primary","one-progress","five-extra","five-mixed"})
        foreach(var recover in new[]{false,true})
        {
            var game=Create(owner,master);var p=game.State.Players[owner];
            var count=scenario.StartsWith("five")?5:1;
            for(var i=0;i<count;i++)
            {
                var card=Card(Family[i],$"ui-canopic-{i}",owner);
                if(scenario=="one-primary"||scenario=="five-mixed"&&i==0)p.Relic=card;
                else if(scenario=="one-progress"||scenario=="five-mixed"&&i==4)p.SpecialZones.CanopicProgress.Add(card);
                else p.ExtraRelics.Add(card);
            }
            var normal=p.Relic is null?"ui-ordinary":null;
            if(normal is not null)p.Relic=Card("S01-0215",normal,owner);
            game=Restore(game,recover);
            var raw=game.SerializeFullState();var hash=game.ComputeStateHash();
            var rng=game.RandomState;var fact=game.CardFactSignalSequence;
            foreach(var view in new[]{"owner","opponent","spectator","referee"})
            {
                var snapshot=view switch {
                    "owner"=>game.SnapshotFor(owner),"opponent"=>game.SnapshotFor(1-owner),
                    "spectator"=>game.SnapshotForSpectator(),_=>game.SnapshotForReferee() };
                Assert.Equal(count,Player(snapshot,owner).GetProperty("specialZones").GetProperty("canopicProgress").GetArrayLength());
                Assert.Equal(raw,game.SerializeFullState());Assert.Equal(hash,game.ComputeStateHash());
                Assert.Equal(rng,game.RandomState);Assert.Equal(fact,game.CardFactSignalSequence);
                fixtures.Add(new {id=$"{master}-{owner}-{scenario}-{recover}-{view}",master,owner,scenario,recover,view,
                    count,normalId=normal,canopicIds=Enumerable.Range(0,count).Select(i=>$"ui-canopic-{i}"),snapshot});
            }
        }
        Assert.Equal(160,fixtures.Count);
        var output=Environment.GetEnvironmentVariable("L12_CANOPIC_FIXTURE_OUT");
        if(!string.IsNullOrWhiteSpace(output))File.WriteAllText(output,JsonSerializer.Serialize(fixtures,Wire));
    }
}
