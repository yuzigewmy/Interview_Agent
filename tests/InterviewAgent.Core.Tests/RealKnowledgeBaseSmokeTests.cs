using System.Diagnostics;
using InterviewAgent.Infrastructure.Documents;
using InterviewAgent.Infrastructure.Embeddings;
using InterviewAgent.Infrastructure.Knowledge;

namespace InterviewAgent.Core.Tests;

public sealed class RealKnowledgeBaseSmokeTests
{
    [Fact]
    public async Task DesktopKnowledgeBase_ImportsAndSearchesWithinLocalBudget()
    {
        var source = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory),
            "面经.docx");
        if (!File.Exists(source))
        {
            return;
        }

        var database = Path.Combine(
            Path.GetTempPath(),
            $"interview-agent-real-{Guid.NewGuid():N}.db");
        try
        {
            var repository = new SqliteKnowledgeRepository(
                database,
                new DocxParagraphReader(),
                new LocalHashEmbeddingProvider(384));
            var imported = await repository.ImportDocxAsync(
                source,
                CancellationToken.None);
            var stopwatch = Stopwatch.StartNew();
            var hits = await repository.SearchAsync(
                "HashMap 和 ConcurrentHashMap 有什么区别？",
                4,
                CancellationToken.None);

            Assert.True(imported.ChunkCount > 100);
            Assert.NotEmpty(hits);
            Assert.True(
                stopwatch.Elapsed < TimeSpan.FromMilliseconds(500),
                $"Cold local search took {stopwatch.Elapsed.TotalMilliseconds:0} ms.");
        }
        finally
        {
            foreach (var suffix in new[] { string.Empty, "-shm", "-wal" })
            {
                var path = database + suffix;
                if (File.Exists(path))
                {
                    File.Delete(path);
                }
            }
        }
    }
}

