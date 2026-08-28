namespace TwelveLegions.Server;

public sealed record L12RuntimeDependencyView(
    string Name,
    bool Configured,
    string State,
    string? Detail,
    DateTimeOffset ObservedAt);

public sealed record L12RuntimeStatusView(
    DateTimeOffset ObservedAt,
    string ServiceVersion,
    int CardCount,
    int OnlineAccountCount,
    int WebSocketConnectionCount,
    int RoomCount,
    int ActiveGameCount,
    IReadOnlyList<L12ReleaseEnvironmentView> ReleaseEnvironments,
    L12RuntimeDependencyView Cdn);
