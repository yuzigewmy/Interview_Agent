using InterviewAgent.Infrastructure.Documents;

namespace InterviewAgent.Core.Tests;

public sealed class InterviewExportFileNameTests
{
    [Fact]
    public void Create_UsesSafeFirstQuestionAndTimestamp()
    {
        var result = InterviewExportFileName.Create(
            "  Shiro/反序列化：怎么防御？  ",
            DateTimeOffset.Parse("2026-07-24T15:06:07+08:00"));

        Assert.Equal(
            "Shiro_反序列化_怎么防御__20260724_150607.docx",
            result);
        Assert.DoesNotContain(
            Path.GetInvalidFileNameChars(),
            character => result.Contains(character));
    }

    [Fact]
    public void Create_FallsBackWhenQuestionContainsOnlyInvalidCharacters()
    {
        var result = InterviewExportFileName.Create(
            """<>:"/\|?*""",
            DateTimeOffset.Parse("2026-07-24T15:06:07+08:00"));

        Assert.Equal("面试记录_20260724_150607.docx", result);
    }
}
