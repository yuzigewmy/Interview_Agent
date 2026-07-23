using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using InterviewAgent.Core.Abstractions;
using InterviewAgent.Core.Knowledge;
using InterviewAgent.Core.Retrieval;
using InterviewAgent.Core.Text;
using InterviewAgent.Infrastructure.Documents;
using Microsoft.Data.Sqlite;

namespace InterviewAgent.Infrastructure.Knowledge;

public sealed class SqliteKnowledgeRepository : IKnowledgeRepository
{
    private const int CandidateLimit = 16;
    private readonly string _connectionString;
    private readonly DocxParagraphReader _reader;
    private readonly IEmbeddingProvider _embeddingProvider;
    private readonly SemaphoreSlim _importGate = new(1, 1);

    public SqliteKnowledgeRepository(
        string databasePath,
        DocxParagraphReader reader,
        IEmbeddingProvider embeddingProvider)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(databasePath);
        _reader = reader ?? throw new ArgumentNullException(nameof(reader));
        _embeddingProvider = embeddingProvider ??
            throw new ArgumentNullException(nameof(embeddingProvider));

        var fullPath = Path.GetFullPath(databasePath);
        var directory = Path.GetDirectoryName(fullPath);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        _connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = fullPath,
            Mode = SqliteOpenMode.ReadWriteCreate,
            Cache = SqliteCacheMode.Shared,
            Pooling = false,
        }.ToString();
    }

    public async Task<ImportResult> ImportDocxAsync(
        string path,
        CancellationToken cancellationToken)
    {
        var stopwatch = Stopwatch.StartNew();
        var paragraphs = await _reader.ReadAsync(path, cancellationToken);
        var chunks = KnowledgeChunker.Chunk(paragraphs);
        if (chunks.Count == 0)
        {
            throw new InvalidDataException("DOCX 中没有可导入的文字内容。");
        }

        await _importGate.WaitAsync(cancellationToken);
        try
        {
            await using var connection = await OpenAsync(cancellationToken);
            await using var transaction = await connection.BeginTransactionAsync(cancellationToken);

            await ExecuteAsync(
                connection,
                transaction,
                "DELETE FROM knowledge_fts; DELETE FROM knowledge_chunks;",
                cancellationToken);

            var sourceFileName = Path.GetFileName(path);
            foreach (var chunk in chunks)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var embedding = await _embeddingProvider.EmbedAsync(
                    $"{chunk.HeadingPath}\n{chunk.Question}\n{chunk.Content}",
                    cancellationToken);
                var id = CreateStableId(sourceFileName, chunk);

                await InsertChunkAsync(
                    connection,
                    transaction,
                    id,
                    sourceFileName,
                    chunk,
                    embedding,
                    cancellationToken);
            }

            await transaction.CommitAsync(cancellationToken);
            return new ImportResult(
                sourceFileName,
                chunks.Count,
                stopwatch.Elapsed,
                _embeddingProvider.Name);
        }
        finally
        {
            _importGate.Release();
        }
    }

    public async Task<IReadOnlyList<KnowledgeHit>> SearchAsync(
        string question,
        int limit,
        CancellationToken cancellationToken)
    {
        var normalized = QuestionNormalizer.Normalize(question);
        ArgumentOutOfRangeException.ThrowIfLessThan(limit, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(limit, 10);

        await using var connection = await OpenAsync(cancellationToken);
        var rows = await LoadAllAsync(connection, cancellationToken);
        if (rows.Count == 0)
        {
            return Array.Empty<KnowledgeHit>();
        }

        var exact = rows.Values
            .Where(row => string.Equals(
                row.NormalizedQuestion,
                normalized,
                StringComparison.Ordinal))
            .OrderBy(row => row.Sequence)
            .Take(CandidateLimit)
            .Select((row, index) => new RankedDocument(row.Id, index + 1))
            .ToArray();

        var lexical = await SearchFtsAsync(connection, question, cancellationToken);
        var queryVector = await _embeddingProvider.EmbedAsync(question, cancellationToken);
        var semantic = rows.Values
            .Select(row => new
            {
                row.Id,
                Score = Cosine(queryVector, row.Embedding),
            })
            .OrderByDescending(item => item.Score)
            .ThenBy(item => item.Id, StringComparer.Ordinal)
            .Take(CandidateLimit)
            .Select((item, index) => new RankedDocument(item.Id, index + 1))
            .ToArray();

        var rankings = exact.Length == 0
            ? new IEnumerable<RankedDocument>[] { lexical, semantic }
            : new IEnumerable<RankedDocument>[] { exact, exact, lexical, semantic };
        var fused = RrfFusion.Fuse(rankings, limit);

        return fused
            .Where(item => rows.ContainsKey(item.DocumentId))
            .Select(item =>
            {
                var row = rows[item.DocumentId];
                var title = string.IsNullOrWhiteSpace(row.Question)
                    ? row.HeadingPath
                    : $"{row.HeadingPath} · {row.Question}".Trim(' ', '·');
                return new KnowledgeHit(
                    row.Id,
                    title,
                    row.Content,
                    row.SourceFileName,
                    item.Score);
            })
            .ToArray();
    }

    private async Task<SqliteConnection> OpenAsync(CancellationToken cancellationToken)
    {
        var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            PRAGMA journal_mode = WAL;
            PRAGMA synchronous = NORMAL;
            CREATE TABLE IF NOT EXISTS knowledge_chunks (
                id TEXT PRIMARY KEY,
                sequence INTEGER NOT NULL,
                source_file_name TEXT NOT NULL,
                heading_path TEXT NOT NULL,
                question TEXT NOT NULL,
                normalized_question TEXT NOT NULL,
                content TEXT NOT NULL,
                embedding BLOB NOT NULL
            );
            CREATE INDEX IF NOT EXISTS idx_knowledge_normalized_question
                ON knowledge_chunks(normalized_question);
            CREATE VIRTUAL TABLE IF NOT EXISTS knowledge_fts
                USING fts5(id UNINDEXED, search_text, tokenize = 'unicode61');
            """;
        await command.ExecuteNonQueryAsync(cancellationToken);
        return connection;
    }

    private static async Task InsertChunkAsync(
        SqliteConnection connection,
        System.Data.Common.DbTransaction transaction,
        string id,
        string sourceFileName,
        KnowledgeChunk chunk,
        float[] embedding,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = (SqliteTransaction)transaction;
        command.CommandText =
            """
            INSERT INTO knowledge_chunks (
                id, sequence, source_file_name, heading_path, question,
                normalized_question, content, embedding)
            VALUES (
                @id, @sequence, @source, @heading, @question,
                @normalized, @content, @embedding);
            INSERT INTO knowledge_fts (id, search_text)
            VALUES (@id, @search);
            """;
        command.Parameters.AddWithValue("@id", id);
        command.Parameters.AddWithValue("@sequence", chunk.Sequence);
        command.Parameters.AddWithValue("@source", sourceFileName);
        command.Parameters.AddWithValue("@heading", chunk.HeadingPath);
        command.Parameters.AddWithValue("@question", chunk.Question);
        command.Parameters.AddWithValue(
            "@normalized",
            string.IsNullOrWhiteSpace(chunk.Question)
                ? string.Empty
                : QuestionNormalizer.Normalize(chunk.Question));
        command.Parameters.AddWithValue("@content", chunk.Content);
        command.Parameters.Add("@embedding", SqliteType.Blob).Value = ToBytes(embedding);
        command.Parameters.AddWithValue(
            "@search",
            SearchTokenizer.ForIndex(
                $"{chunk.HeadingPath} {chunk.Question} {chunk.Content}"));
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task<Dictionary<string, StoredChunk>> LoadAllAsync(
        SqliteConnection connection,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            SELECT id, sequence, source_file_name, heading_path, question,
                   normalized_question, content, embedding
            FROM knowledge_chunks;
            """;
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        var rows = new Dictionary<string, StoredChunk>(StringComparer.Ordinal);
        while (await reader.ReadAsync(cancellationToken))
        {
            var row = new StoredChunk(
                reader.GetString(0),
                reader.GetInt32(1),
                reader.GetString(2),
                reader.GetString(3),
                reader.GetString(4),
                reader.GetString(5),
                reader.GetString(6),
                FromBytes((byte[])reader[7]));
            rows[row.Id] = row;
        }

        return rows;
    }

    private static async Task<RankedDocument[]> SearchFtsAsync(
        SqliteConnection connection,
        string question,
        CancellationToken cancellationToken)
    {
        var match = SearchTokenizer.ForMatch(question);
        if (match.Length == 0)
        {
            return Array.Empty<RankedDocument>();
        }

        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            SELECT id
            FROM knowledge_fts
            WHERE knowledge_fts MATCH @match
            ORDER BY bm25(knowledge_fts)
            LIMIT @limit;
            """;
        command.Parameters.AddWithValue("@match", match);
        command.Parameters.AddWithValue("@limit", CandidateLimit);

        var result = new List<RankedDocument>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            result.Add(new RankedDocument(reader.GetString(0), result.Count + 1));
        }

        return result.ToArray();
    }

    private static async Task ExecuteAsync(
        SqliteConnection connection,
        System.Data.Common.DbTransaction transaction,
        string sql,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = (SqliteTransaction)transaction;
        command.CommandText = sql;
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static string CreateStableId(string sourceFileName, KnowledgeChunk chunk)
    {
        var value = $"{sourceFileName}\n{chunk.Sequence}\n{chunk.HeadingPath}\n{chunk.Content}";
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)))[..24];
    }

    private static byte[] ToBytes(float[] values)
    {
        var bytes = new byte[values.Length * sizeof(float)];
        Buffer.BlockCopy(values, 0, bytes, 0, bytes.Length);
        return bytes;
    }

    private static float[] FromBytes(byte[] bytes)
    {
        if (bytes.Length % sizeof(float) != 0)
        {
            throw new InvalidDataException("知识库向量数据损坏。");
        }

        var values = new float[bytes.Length / sizeof(float)];
        Buffer.BlockCopy(bytes, 0, values, 0, bytes.Length);
        return values;
    }

    private static double Cosine(float[] left, float[] right)
    {
        if (left.Length != right.Length)
        {
            return double.NegativeInfinity;
        }

        double score = 0;
        for (var index = 0; index < left.Length; index++)
        {
            score += left[index] * right[index];
        }

        return score;
    }

    private sealed record StoredChunk(
        string Id,
        int Sequence,
        string SourceFileName,
        string HeadingPath,
        string Question,
        string NormalizedQuestion,
        string Content,
        float[] Embedding);
}
