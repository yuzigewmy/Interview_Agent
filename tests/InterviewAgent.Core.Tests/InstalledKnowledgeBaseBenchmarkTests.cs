using System.Diagnostics;
using InterviewAgent.Infrastructure.Documents;
using InterviewAgent.Infrastructure.Embeddings;
using InterviewAgent.Infrastructure.Knowledge;
using Xunit.Abstractions;

namespace InterviewAgent.Core.Tests;

public sealed class InstalledKnowledgeBaseBenchmarkTests(ITestOutputHelper output)
{
    [Fact]
    public async Task InstalledBgeKnowledgeBase_SearchesRepresentativeQuestionsWithinBudget()
    {
        var root = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "InterviewAgent");
        var databasePath = Path.Combine(root, "data", "knowledge.db");
        var modelDirectory = Path.Combine(root, "models", "bge-small-zh-v1.5");
        var modelPath = Path.Combine(modelDirectory, "model.onnx");
        var vocabularyPath = Path.Combine(modelDirectory, "vocab.txt");
        if (!File.Exists(databasePath) ||
            !File.Exists(modelPath) ||
            !File.Exists(vocabularyPath))
        {
            return;
        }

        using var provider = new OnnxBgeEmbeddingProvider(modelPath, vocabularyPath);
        var repository = new SqliteKnowledgeRepository(
            databasePath,
            new DocxParagraphReader(),
            provider);
        (string Question, string ExpectedTerm)[] questions =
        [
            ("psexec 与 wmicexec 有什么区别？", "psexec"),
            ("Fastjson 怎么绕过 WAF？", "fastjson"),
            ("Redis 未授权访问有哪些利用方法？", "Redis"),
            ("Shiro 反序列化怎么绕过 WAF？", "shiro"),
            ("笛卡尔注入的原理是什么？", "笛卡尔"),
        ];

        var durations = new List<double>();
        foreach (var (question, expectedTerm) in questions)
        {
            var stopwatch = Stopwatch.StartNew();
            var hits = await repository.SearchAsync(question, 4, CancellationToken.None);
            stopwatch.Stop();
            durations.Add(stopwatch.Elapsed.TotalMilliseconds);
            Assert.NotEmpty(hits);
            output.WriteLine(
                $"{stopwatch.Elapsed.TotalMilliseconds:0.0} ms | {question} | " +
                string.Join(" || ", hits.Select(hit => hit.Title)));
            Assert.Contains(
                hits,
                hit => $"{hit.Title}\n{hit.Content}".Contains(
                    expectedTerm,
                    StringComparison.OrdinalIgnoreCase));
        }

        var maximum = durations.Max();
        var average = durations.Average();
        output.WriteLine($"average={average:0.0} ms, maximum={maximum:0.0} ms");
        Assert.True(
            maximum < 750,
            $"BGE-backed retrieval exceeded 750 ms: {maximum:0.0} ms.");
    }
}
