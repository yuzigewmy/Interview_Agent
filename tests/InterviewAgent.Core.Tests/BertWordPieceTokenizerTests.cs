using InterviewAgent.Infrastructure.Embeddings;

namespace InterviewAgent.Core.Tests;

public sealed class BertWordPieceTokenizerTests
{
    private static readonly string[] Vocabulary =
    [
        "[PAD]",
        "[UNK]",
        "[CLS]",
        "[SEP]",
        "你",
        "好",
        "java",
        "##script",
        "面",
        "试",
        "？",
    ];

    [Fact]
    public void Encode_ChineseAndWordPiece_AddsBertBoundaryTokens()
    {
        var tokenizer = new BertWordPieceTokenizer(Vocabulary, maxTokens: 16);

        var encoded = tokenizer.Encode("你好 javascript");

        Assert.Equal([2L, 4L, 5L, 6L, 7L, 3L], encoded.InputIds);
        Assert.All(encoded.AttentionMask, value => Assert.Equal(1L, value));
        Assert.All(encoded.TokenTypeIds, value => Assert.Equal(0L, value));
    }

    [Fact]
    public void Encode_UnknownText_UsesUnknownToken()
    {
        var tokenizer = new BertWordPieceTokenizer(Vocabulary, maxTokens: 16);

        var encoded = tokenizer.Encode("unlisted");

        Assert.Equal([2L, 1L, 3L], encoded.InputIds);
    }

    [Fact]
    public void Encode_LongText_TruncatesAndKeepsSeparator()
    {
        var tokenizer = new BertWordPieceTokenizer(Vocabulary, maxTokens: 6);

        var encoded = tokenizer.Encode("你好面试你好");

        Assert.Equal(6, encoded.InputIds.Length);
        Assert.Equal(2, encoded.InputIds[0]);
        Assert.Equal(3, encoded.InputIds[^1]);
    }

    [Fact]
    public void Constructor_MissingRequiredToken_RejectsVocabulary()
    {
        var incomplete = Vocabulary.Where(token => token != "[SEP]").ToArray();

        var exception = Assert.Throws<InvalidDataException>(
            () => new BertWordPieceTokenizer(incomplete));

        Assert.Contains("[SEP]", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Encode_EmptyVocabularyLine_PreservesFollowingTokenId()
    {
        string[] vocabulary = ["[PAD]", "[UNK]", "[CLS]", "[SEP]", "", "你"];
        var tokenizer = new BertWordPieceTokenizer(vocabulary);

        var encoded = tokenizer.Encode("你");

        Assert.Equal([2L, 5L, 3L], encoded.InputIds);
    }
}
