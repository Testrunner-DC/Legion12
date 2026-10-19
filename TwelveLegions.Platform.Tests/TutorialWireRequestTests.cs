using System.Text.Json;
using TwelveLegions.Server;
using Xunit;

namespace TwelveLegions.Platform.Tests;

public sealed class TutorialWireRequestTests
{
    [Theory]
    [InlineData("deploy")]
    [InlineData("move")]
    [InlineData("combat")]
    [InlineData("response")]
    [InlineData("preparation")]
    [InlineData("finale")]
    public void EveryCourseSceneUsesTheSameStrictWireEnvelope(string lesson)
    {
        Assert.True(L12WebSocketServer.TryReadTutorialRequest(JsonSerializer.SerializeToElement(new
            { type = "createTutorial", lessonId = lesson, requestId = "course-request" }), out var readLesson, out var readId));
        Assert.Equal(lesson, readLesson);
        Assert.Equal("course-request", readId);
    }

    [Theory]
    [InlineData("{\"type\":\"createTutorial\",\"lessonId\":\"finale\",\"requestId\":\"x\",\"seed\":1}")]
    [InlineData("{\"type\":\"createTutorial\",\"lessonId\":\"preparation\",\"requestId\":\"x\",\"gm\":true}")]
    [InlineData("{\"type\":\"createTutorial\",\"lessonId\":\"finale\",\"lessonId\":\"preparation\",\"requestId\":\"x\"}")]
    [InlineData("{\"type\":\"createTutorial\",\"lessonId\":\"unknown\",\"requestId\":\"x\"}")]
    [InlineData("{\"type\":\"createTutorial\",\"lessonId\":\"finale\",\"requestId\":\" x\"}")]
    public void NewScenesDoNotRelaxUnknownFieldsDuplicatesOrRequestBinding(string json)
    {
        using var document = JsonDocument.Parse(json);
        Assert.False(L12WebSocketServer.TryReadTutorialRequest(document.RootElement, out _, out _));
    }
}
