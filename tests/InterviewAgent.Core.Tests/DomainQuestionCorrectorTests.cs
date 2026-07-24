using System.Diagnostics;
using InterviewAgent.Core.Corrections;
using InterviewAgent.Infrastructure.Corrections;

namespace InterviewAgent.Core.Tests;

public sealed class DomainQuestionCorrectorTests
{
    [Theory]
    [InlineData("circle注入的原理是什么", "SQL注入的原理是什么")]
    [InlineData("Java内存卡怎么排查", "Java内存马怎么排查")]
    [InlineData("shreo反序列化怎么利用", "Shiro反序列化怎么利用")]
    public void Correct_AppliesSafeDomainPhraseMappings(
        string transcript,
        string expected)
    {
        var corrector = CreateCorrector();

        var result = corrector.Correct(transcript);

        Assert.Equal(expected, result.CandidateQuestion);
        Assert.Equal(CorrectionConfidence.High, result.Confidence);
        Assert.Equal(CorrectionSource.Dictionary, result.Source);
    }

    [Fact]
    public void Correct_DoesNotReplaceLatinAliasInsideOrdinaryWord()
    {
        var corrector = CreateCorrector();

        var result = corrector.Correct("如何 trace 一次网络请求");

        Assert.Equal("如何 trace 一次网络请求", result.CandidateQuestion);
        Assert.Equal(CorrectionSource.Original, result.Source);
    }

    [Fact]
    public void Correct_UsesBoundedFuzzyMatchingForDomainTerms()
    {
        var corrector = CreateCorrector();

        var result = corrector.Correct("shrio 反序列化的原理是什么");

        Assert.Equal("Shiro 反序列化的原理是什么", result.CandidateQuestion);
        Assert.Equal(CorrectionConfidence.Medium, result.Confidence);
        Assert.Equal(CorrectionSource.FuzzyDomainTerm, result.Source);
    }

    [Fact]
    public void Correct_CombinesDictionaryAndFuzzyCorrections()
    {
        var corrector = CreateCorrector();

        var result = corrector.Correct("circle注入和 shrio 有什么关系");

        Assert.Equal("SQL注入和 Shiro 有什么关系", result.CandidateQuestion);
        Assert.Equal(CorrectionConfidence.Medium, result.Confidence);
        Assert.Equal(CorrectionSource.DictionaryAndFuzzy, result.Source);
    }

    [Fact]
    public void LoadOrEmpty_LoadsProductionCorrectionResources()
    {
        var corrector = CreateProductionCorrector();

        var result = corrector.Correct("circle注入和 shrio 有什么区别");

        Assert.Equal("SQL注入和 Shiro 有什么区别", result.CandidateQuestion);
        Assert.Equal(CorrectionSource.DictionaryAndFuzzy, result.Source);
    }

    [Fact]
    public void Correct_StaysWithinLocalLatencyBudget()
    {
        var corrector = CreateProductionCorrector();
        _ = corrector.Correct("shrio 反序列化的原理是什么");
        var stopwatch = Stopwatch.StartNew();

        for (var index = 0; index < 200; index++)
        {
            _ = corrector.Correct("shrio 反序列化的原理是什么");
        }

        stopwatch.Stop();
        Assert.True(
            stopwatch.Elapsed < TimeSpan.FromSeconds(1),
            $"200 corrections took {stopwatch.Elapsed.TotalMilliseconds:0.0} ms.");
    }

    private static DomainQuestionCorrector CreateCorrector() =>
        new(
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["ace"] = "ACE",
                ["circle注入"] = "SQL注入",
                ["java内存卡"] = "Java内存马",
                ["shreo"] = "Shiro",
            },
            new[]
            {
                ("Shiro", 10),
                ("SQLmap", 9),
                ("WebLogic", 10),
            });

    private static DomainQuestionCorrector CreateProductionCorrector()
    {
        var repositoryRoot = FindRepositoryRoot();
        return DomainQuestionCorrector.LoadOrEmpty(
            Path.Combine(repositoryRoot, "resources", "asr", "correct_words.json"),
            Path.Combine(repositoryRoot, "resources", "asr", "volcano_hotwords.txt"));
    }

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "InterviewAgent.sln")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException("找不到 InterviewAgent.sln。");
    }
}
