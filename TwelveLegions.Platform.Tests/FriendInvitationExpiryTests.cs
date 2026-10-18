using System.Reflection;
using System.Text.Json;
using System.Text.Json.Nodes;
using TwelveLegions.Server;
using Xunit;

namespace TwelveLegions.Platform.Tests;

[Collection("Platform environment")]
public sealed class FriendInvitationExpiryTests
{
    private sealed class Clock
    {
        internal DateTimeOffset Now { get; set; } = DateTimeOffset.Parse("2027-01-01T00:00:00Z");
    }

    private static JsonNode Payload(OutgoingMessage message)
        => JsonSerializer.SerializeToNode(message.Payload, new JsonSerializerOptions(JsonSerializerDefaults.Web))!;

    private static async Task<(Guid Sender, Guid Recipient, string RecipientAccountId)> ConnectFriends(
        DeploymentDrainFixture fixture)
    {
        var sender = fixture.CreatePlayer("inviter");
        var recipient = fixture.CreatePlayer("invitee");
        Assert.True(fixture.Platform.SendFriendRequest(sender.Id, recipient.Id).Success);
        Assert.True(fixture.Platform.ResolveFriendRequest(recipient.Id, sender.Id, true).Success);
        var senderSession = Guid.NewGuid();
        var recipientSession = Guid.NewGuid();
        await fixture.Rooms.ConnectAsync(senderSession, sender.Id, sender.Username);
        await fixture.Rooms.ConnectAsync(recipientSession, recipient.Id, recipient.Username);
        return (senderSession, recipientSession, recipient.Id);
    }

    private static string Invite(DeploymentDrainFixture fixture, Guid sender, string recipientId)
        => fixture.Rooms.InviteFriend(sender, recipientId).Select(Payload)
            .Single(message => message["type"]!.GetValue<string>() == "friendInvitationSent")
            ["invitationId"]!.GetValue<string>();

    private static void AssertError(IReadOnlyList<OutgoingMessage> result)
        => Assert.Equal("error", Payload(Assert.Single(result))["type"]!.GetValue<string>());

    private static object InvitationField(L12RoomManager rooms, string name)
        => typeof(L12RoomManager).GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!
            .GetValue(rooms)!;

    private static int MemoryInvitationCount(L12RoomManager rooms)
    {
        var invitations = InvitationField(rooms, "_friendInvitations");
        return (int)invitations.GetType().GetProperty("Count")!.GetValue(invitations)!;
    }

    [Theory]
    [InlineData(-1, false)]
    [InlineData(0, false)]
    [InlineData(1, true)]
    public async Task ExactTenMinuteBoundaryRetainsOnlyAcceptableInvitations(long extraTicks, bool expired)
    {
        var clock = new Clock();
        await using var fixture = await DeploymentDrainFixture.CreateAsync(utcNow: () => clock.Now);
        var pair = await ConnectFriends(fixture);
        _ = Invite(fixture, pair.Sender, pair.RecipientAccountId);
        Assert.Equal(1, fixture.Rooms.CaptureDeploymentRoomReadiness().Blockers["pending_invitations"]);
        clock.Now = clock.Now.AddMinutes(10).AddTicks(extraTicks);
        fixture.Controller.Begin(fixture.Owner);

        var result = await fixture.Controller.SealAsync(fixture.Owner);

        Assert.Equal(expired ? "Applied" : "work_remaining", result.Code);
        Assert.Equal(expired, result.Granted);
        Assert.Equal(expired ? 0 : 1, result.Rooms!.Blockers["pending_invitations"]);
        if (expired)
        {
            Assert.NotNull(result.Permit);
            Assert.True(result.Durability!.Clear);
            Assert.True(result.Platform!.Clear);
            Assert.True(fixture.Coordinator.IsCurrentSealPermit(result.Permit!));
        }
        else Assert.Null(result.Permit);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ExpiredAcceptOrDeclineRemovesOnlyExpiredMemoryAndNeverCreatesRoom(bool accept)
    {
        var clock = new Clock();
        await using var fixture = await DeploymentDrainFixture.CreateAsync(utcNow: () => clock.Now);
        var pair = await ConnectFriends(fixture);
        var id = Invite(fixture, pair.Sender, pair.RecipientAccountId);
        clock.Now = clock.Now.AddMinutes(10).AddTicks(1);
        var revision = fixture.Platform.CaptureDeploymentPlatformReadiness().Revision;

        AssertError(fixture.Rooms.ResolveFriendInvitation(pair.Recipient, id, accept));

        Assert.Equal(0, fixture.Rooms.RuntimeStats().RoomCount);
        Assert.True(fixture.Rooms.CaptureDeploymentRoomReadiness().Clear);
        Assert.Equal(revision, fixture.Platform.CaptureDeploymentPlatformReadiness().Revision);
        AssertError(fixture.Rooms.ResolveFriendInvitation(pair.Recipient, id, accept));
    }

    [Fact]
    public async Task NewInvitationPrunesOldItemWithoutRemovingItsReplacement()
    {
        var clock = new Clock();
        await using var fixture = await DeploymentDrainFixture.CreateAsync(utcNow: () => clock.Now);
        var pair = await ConnectFriends(fixture);
        var oldId = Invite(fixture, pair.Sender, pair.RecipientAccountId);
        clock.Now = clock.Now.AddMinutes(11);
        var newId = Invite(fixture, pair.Sender, pair.RecipientAccountId);

        Assert.NotEqual(oldId, newId);
        Assert.Equal(1, MemoryInvitationCount(fixture.Rooms));
        AssertError(fixture.Rooms.ResolveFriendInvitation(pair.Recipient, oldId, false));
        Assert.Equal(1, fixture.Rooms.CaptureDeploymentRoomReadiness().Blockers["pending_invitations"]);
        Assert.All(fixture.Rooms.CancelFriendInvitation(pair.Sender, newId),
            message => Assert.Equal("friendInvitationRevoked", Payload(message)["type"]!.GetValue<string>()));
        Assert.True(fixture.Rooms.CaptureDeploymentRoomReadiness().Clear);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task DisconnectionDoesNotDiscardValidInvitationButExpiryAllowsNormalSeal(bool expired)
    {
        var clock = new Clock();
        await using var fixture = await DeploymentDrainFixture.CreateAsync(utcNow: () => clock.Now);
        var pair = await ConnectFriends(fixture);
        _ = Invite(fixture, pair.Sender, pair.RecipientAccountId);
        _ = fixture.Rooms.Disconnect(pair.Sender);
        _ = fixture.Rooms.Disconnect(pair.Recipient);
        if (expired) clock.Now = clock.Now.AddMinutes(11);
        fixture.Controller.Begin(fixture.Owner);

        var result = await fixture.Controller.SealAsync(fixture.Owner);

        Assert.Equal(expired, result.Granted);
        Assert.Equal(expired ? 0 : 1, result.Rooms!.Blockers["pending_invitations"]);
    }

    [Fact]
    public async Task ConcurrentAcceptConsumesOneInvitationAndCreatesExactlyOneRoom()
    {
        var clock = new Clock();
        await using var fixture = await DeploymentDrainFixture.CreateAsync(utcNow: () => clock.Now);
        var pair = await ConnectFriends(fixture);
        var id = Invite(fixture, pair.Sender, pair.RecipientAccountId);

        var results = await Task.WhenAll(Enumerable.Range(0, 2).Select(_ => Task.Run(
            () => fixture.Rooms.ResolveFriendInvitation(pair.Recipient, id, true))));

        Assert.Equal(2, results.SelectMany(result => result).Select(Payload)
            .Count(message => message["type"]!.GetValue<string>() == "friendRoomCreated"));
        Assert.Equal(1, fixture.Rooms.RuntimeStats().RoomCount);
        Assert.Equal(0, fixture.Rooms.CaptureDeploymentRoomReadiness().Blockers["pending_invitations"]);
        AssertError(fixture.Rooms.ResolveFriendInvitation(pair.Recipient, id, true));
    }

    [Fact]
    public async Task WrongRecipientCannotConsumeValidInvitation()
    {
        var clock = new Clock();
        await using var fixture = await DeploymentDrainFixture.CreateAsync(utcNow: () => clock.Now);
        var pair = await ConnectFriends(fixture);
        var other = fixture.CreatePlayer("other");
        var otherSession = Guid.NewGuid();
        await fixture.Rooms.ConnectAsync(otherSession, other.Id, other.Username);
        var id = Invite(fixture, pair.Sender, pair.RecipientAccountId);

        AssertError(fixture.Rooms.ResolveFriendInvitation(otherSession, id, true));
        Assert.Equal(1, fixture.Rooms.CaptureDeploymentRoomReadiness().Blockers["pending_invitations"]);
        Assert.Contains(fixture.Rooms.ResolveFriendInvitation(pair.Recipient, id, true),
            message => Payload(message)["type"]!.GetValue<string>() == "friendRoomCreated");
    }

    [Fact]
    public async Task DrainingRejectsCreationAndAcceptWithoutConsumingExistingValidInvitation()
    {
        var clock = new Clock();
        await using var fixture = await DeploymentDrainFixture.CreateAsync(utcNow: () => clock.Now);
        var pair = await ConnectFriends(fixture);
        var id = Invite(fixture, pair.Sender, pair.RecipientAccountId);
        fixture.Controller.Begin(fixture.Owner);

        foreach (var messages in new[]
        {
            fixture.Rooms.InviteFriend(pair.Sender, pair.RecipientAccountId),
            fixture.Rooms.ResolveFriendInvitation(pair.Recipient, id, true),
        })
            Assert.Equal("deploymentDrainActive", Payload(Assert.Single(messages))["code"]!.GetValue<string>());
        var result = await fixture.Controller.SealAsync(fixture.Owner);
        Assert.False(result.Granted);
        Assert.Equal(1, result.Rooms!.Blockers["pending_invitations"]);
        fixture.Controller.Cancel(fixture.Owner);
        Assert.Contains(fixture.Rooms.ResolveFriendInvitation(pair.Recipient, id, true),
            message => Payload(message)["type"]!.GetValue<string>() == "friendRoomCreated");
    }

    [Theory]
    [InlineData(false, "pregame_rooms")]
    [InlineData(true, "unfinished_rooms")]
    public async Task ExpiredInvitationNeverHidesARealRoom(bool live, string blocker)
    {
        var clock = new Clock();
        await using var fixture = await DeploymentDrainFixture.CreateAsync(utcNow: () => clock.Now);
        var pair = await ConnectFriends(fixture);
        _ = Invite(fixture, pair.Sender, pair.RecipientAccountId);
        var third = fixture.CreatePlayer("room");
        var thirdSession = Guid.NewGuid();
        await fixture.Rooms.ConnectAsync(thirdSession, third.Id, third.Username);
        if (live)
        {
            Assert.True(fixture.Platform.SetRole(fixture.LoginAdmin().Account!, third.Id, "admin"));
            _ = await fixture.Rooms.CreateSandboxAsync(thirdSession, null);
        }
        else _ = fixture.Rooms.CreateRoom(thirdSession);
        clock.Now = clock.Now.AddMinutes(11);
        fixture.Controller.Begin(fixture.Owner);

        var result = await fixture.Controller.SealAsync(fixture.Owner);

        Assert.Equal("work_remaining", result.Code);
        Assert.False(result.Granted);
        Assert.Equal(0, result.Rooms!.Blockers["pending_invitations"]);
        Assert.True(result.Rooms.Blockers[blocker] > 0);
    }

    [Fact]
    public async Task ReadinessFailsClosedWithoutWaitingForTheInvitationGate()
    {
        var clock = new Clock();
        await using var fixture = await DeploymentDrainFixture.CreateAsync(utcNow: () => clock.Now);
        var pair = await ConnectFriends(fixture);
        _ = Invite(fixture, pair.Sender, pair.RecipientAccountId);
        var gate = InvitationField(fixture.Rooms, "_friendInvitationGate");
        using var entered = new ManualResetEventSlim();
        using var release = new ManualResetEventSlim();
        var holder = Task.Run(() =>
        {
            lock (gate)
            {
                entered.Set();
                Assert.True(release.Wait(TimeSpan.FromSeconds(10)));
            }
        });
        try
        {
            Assert.True(entered.Wait(TimeSpan.FromSeconds(5)));
            var readiness = await Task.Run(fixture.Rooms.CaptureDeploymentRoomReadiness)
                .WaitAsync(TimeSpan.FromSeconds(5));
            Assert.False(readiness.Clear);
            Assert.Equal(1, readiness.Blockers["room_lock_busy"]);
        }
        finally { release.Set(); await holder; }
        Assert.Equal(1, MemoryInvitationCount(fixture.Rooms));
    }

    [Fact]
    public async Task PreexistingCreatorLeaseCannotBeLostWhileWaitingForTheInvitationGate()
    {
        var clock = new Clock();
        await using var fixture = await DeploymentDrainFixture.CreateAsync(utcNow: () => clock.Now);
        var pair = await ConnectFriends(fixture);
        var gate = InvitationField(fixture.Rooms, "_friendInvitationGate");
        Task<IReadOnlyList<OutgoingMessage>> creator;
        using var entered = new ManualResetEventSlim();
        using var release = new ManualResetEventSlim();
        var holder = Task.Run(() =>
        {
            lock (gate)
            {
                entered.Set();
                Assert.True(release.Wait(TimeSpan.FromSeconds(10)));
            }
        });
        try
        {
            Assert.True(entered.Wait(TimeSpan.FromSeconds(5)));
            creator = Task.Run(() => fixture.Rooms.InviteFriend(pair.Sender, pair.RecipientAccountId));
            Assert.True(SpinWait.SpinUntil(() => fixture.Coordinator.Snapshot().AdmissionLeases > 0,
                TimeSpan.FromSeconds(5)));
            fixture.Controller.Begin(fixture.Owner);
            var blocked = await fixture.Controller.SealAsync(fixture.Owner);
            Assert.Equal("OutstandingLeases", blocked.Code);
            Assert.False(blocked.Granted);
        }
        finally { release.Set(); await holder; }

        Assert.Contains(await creator.WaitAsync(TimeSpan.FromSeconds(5)),
            message => Payload(message)["type"]!.GetValue<string>() == "friendInvitationSent");
        Assert.Equal(0, fixture.Coordinator.Snapshot().AdmissionLeases);
        var result = await fixture.Controller.SealAsync(fixture.Owner);
        Assert.False(result.Granted);
        Assert.Equal(1, result.Rooms!.Blockers["pending_invitations"]);
    }

    [Fact]
    public async Task ValidInvitationCanStillBeCancelledDuringDrain()
    {
        var clock = new Clock();
        await using var fixture = await DeploymentDrainFixture.CreateAsync(utcNow: () => clock.Now);
        var pair = await ConnectFriends(fixture);
        var id = Invite(fixture, pair.Sender, pair.RecipientAccountId);
        fixture.Controller.Begin(fixture.Owner);

        var revoked = fixture.Rooms.CancelFriendInvitation(pair.Sender, id);

        Assert.Equal(2, revoked.Count);
        Assert.All(revoked, message => Assert.Equal("friendInvitationRevoked",
            Payload(message)["type"]!.GetValue<string>()));
        Assert.Equal(0, MemoryInvitationCount(fixture.Rooms));
        Assert.True((await fixture.Controller.SealAsync(fixture.Owner)).Granted);
    }

    [Fact]
    public async Task BackwardClockDoesNotExpireAnOtherwiseValidInvitation()
    {
        var clock = new Clock();
        await using var fixture = await DeploymentDrainFixture.CreateAsync(utcNow: () => clock.Now);
        var pair = await ConnectFriends(fixture);
        _ = Invite(fixture, pair.Sender, pair.RecipientAccountId);
        clock.Now = clock.Now.AddHours(-1).ToOffset(TimeSpan.FromHours(8));

        Assert.Equal(1, fixture.Rooms.CaptureDeploymentRoomReadiness().Blockers["pending_invitations"]);

        clock.Now = clock.Now.AddHours(1).AddMinutes(10).AddTicks(1);
        Assert.True(fixture.Rooms.CaptureDeploymentRoomReadiness().Clear);
        Assert.Equal(0, MemoryInvitationCount(fixture.Rooms));
    }
}
