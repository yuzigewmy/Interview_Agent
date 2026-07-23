using System.Text.Json;
using InterviewAgent.Infrastructure.Volcano;

namespace InterviewAgent.Core.Tests;

public sealed class VolcanoAsrRequestTests
{
    [Fact]
    public void BuildInitialRequestJson_IncludesConfiguredBoostingTable()
    {
        using var document = JsonDocument.Parse(
            VolcanoStreamingTranscriber.BuildInitialRequestJson(
                new VolcanoAsrOptions(ApiKey: "test-key")));

        var request = document.RootElement.GetProperty("request");
        var corpus = request.GetProperty("corpus");

        Assert.Equal(
            "f09c571c-a6d7-4b95-90a1-fc059be2db0f",
            corpus.GetProperty("boosting_table_id").GetString());
    }
}
