using System.Text.Json;
using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;

namespace ClaudeMem.Core.Services.Embeddings;

/// <summary>
/// Local embedding provider using ONNX Runtime.
/// Uses all-MiniLM-L6-v2 model (same as Transformers.js default).
/// Cross-platform: Windows, Linux, macOS, ARM.
/// </summary>
public class LocalOnnxEmbeddingProvider : IEmbeddingProvider, IDisposable
{
    private readonly string _modelDir;
    private readonly string _modelName;
    private InferenceSession? _session;
    private Dictionary<string, int>? _vocab;
    private readonly SemaphoreSlim _initLock = new(1, 1);
    private bool _initialized;

    // HuggingFace model URLs
    private const string HF_BASE_URL = "https://huggingface.co/sentence-transformers/all-MiniLM-L6-v2/resolve/main";

    public string Name => "local-onnx";
    public int Dimension => 384; // all-MiniLM-L6-v2 outputs 384 dimensions

    public LocalOnnxEmbeddingProvider(string? modelDir = null, string modelName = "all-MiniLM-L6-v2")
    {
        _modelName = modelName;
        _modelDir = modelDir ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "ClaudeMem", "models", modelName);
    }

    public async Task<float[]> GenerateEmbeddingAsync(string text, CancellationToken ct = default)
    {
        await EnsureInitializedAsync(ct);
        return GenerateEmbedding(text);
    }

    public async Task<List<float[]>> GenerateEmbeddingsAsync(IEnumerable<string> texts, CancellationToken ct = default)
    {
        await EnsureInitializedAsync(ct);
        return texts.Select(GenerateEmbedding).ToList();
    }

    public async Task<bool> IsAvailableAsync(CancellationToken ct = default)
    {
        try
        {
            await EnsureInitializedAsync(ct);
            return _session != null && _vocab != null;
        }
        catch
        {
            return false;
        }
    }

    private async Task EnsureInitializedAsync(CancellationToken ct)
    {
        if (_initialized) return;

        await _initLock.WaitAsync(ct);
        try
        {
            if (_initialized) return;

            Directory.CreateDirectory(_modelDir);

            // Download model files if not present
            await DownloadModelFilesAsync(ct);

            // Load vocab
            var vocabPath = Path.Combine(_modelDir, "vocab.txt");
            _vocab = LoadVocab(vocabPath);

            // Load ONNX model
            var modelPath = Path.Combine(_modelDir, "model.onnx");
            var options = new SessionOptions();
            options.GraphOptimizationLevel = GraphOptimizationLevel.ORT_ENABLE_ALL;
            _session = new InferenceSession(modelPath, options);

            _initialized = true;
        }
        finally
        {
            _initLock.Release();
        }
    }

    private async Task DownloadModelFilesAsync(CancellationToken ct)
    {
        var files = new[]
        {
            ("onnx/model.onnx", "model.onnx"),
            ("vocab.txt", "vocab.txt"),
            ("tokenizer_config.json", "tokenizer_config.json")
        };

        using var http = new HttpClient();
        http.Timeout = TimeSpan.FromMinutes(10);

        foreach (var (remotePath, localName) in files)
        {
            var localPath = Path.Combine(_modelDir, localName);
            if (File.Exists(localPath)) continue;

            var url = $"{HF_BASE_URL}/{remotePath}";
            Console.WriteLine($"[LocalOnnx] Downloading {localName}...");

            var response = await http.GetAsync(url, ct);
            response.EnsureSuccessStatusCode();

            var bytes = await response.Content.ReadAsByteArrayAsync(ct);
            await File.WriteAllBytesAsync(localPath, bytes, ct);

            Console.WriteLine($"[LocalOnnx] Downloaded {localName} ({bytes.Length / 1024}KB)");
        }
    }

    private Dictionary<string, int> LoadVocab(string path)
    {
        var vocab = new Dictionary<string, int>();
        var lines = File.ReadAllLines(path);
        for (int i = 0; i < lines.Length; i++)
        {
            vocab[lines[i]] = i;
        }
        return vocab;
    }

    private float[] GenerateEmbedding(string text)
    {
        if (_session == null || _vocab == null)
            throw new InvalidOperationException("Provider not initialized");

        // Tokenize
        var tokens = Tokenize(text);

        // Create input tensors
        var inputIds = new DenseTensor<long>(new[] { 1, tokens.Length });
        var attentionMask = new DenseTensor<long>(new[] { 1, tokens.Length });
        var tokenTypeIds = new DenseTensor<long>(new[] { 1, tokens.Length });

        for (int i = 0; i < tokens.Length; i++)
        {
            inputIds[0, i] = tokens[i];
            attentionMask[0, i] = 1;
            tokenTypeIds[0, i] = 0;
        }

        var inputs = new List<NamedOnnxValue>
        {
            NamedOnnxValue.CreateFromTensor("input_ids", inputIds),
            NamedOnnxValue.CreateFromTensor("attention_mask", attentionMask),
            NamedOnnxValue.CreateFromTensor("token_type_ids", tokenTypeIds)
        };

        // Run inference
        using var results = _session.Run(inputs);

        // Get the last_hidden_state output and mean pool
        var lastHiddenState = results.First(r => r.Name == "last_hidden_state").AsTensor<float>();
        return MeanPool(lastHiddenState, tokens.Length);
    }

    private int[] Tokenize(string text)
    {
        if (_vocab == null) throw new InvalidOperationException("Vocab not loaded");

        const int maxLength = 512;
        var tokens = new List<int> { _vocab["[CLS]"] };

        // Simple wordpiece tokenization
        var words = text.ToLowerInvariant().Split(new[] { ' ', '\t', '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries);
        foreach (var word in words)
        {
            var subwords = TokenizeWord(word);
            foreach (var subword in subwords)
            {
                if (tokens.Count >= maxLength - 1) break;
                tokens.Add(subword);
            }
            if (tokens.Count >= maxLength - 1) break;
        }

        tokens.Add(_vocab["[SEP]"]);
        return tokens.ToArray();
    }

    private IEnumerable<int> TokenizeWord(string word)
    {
        if (_vocab == null) yield break;

        // Try full word first
        if (_vocab.TryGetValue(word, out int id))
        {
            yield return id;
            yield break;
        }

        // WordPiece: break into subwords
        int start = 0;
        while (start < word.Length)
        {
            int end = word.Length;
            string? bestMatch = null;

            while (start < end)
            {
                var substr = word.Substring(start, end - start);
                var token = start > 0 ? "##" + substr : substr;

                if (_vocab.ContainsKey(token))
                {
                    bestMatch = token;
                    break;
                }
                end--;
            }

            if (bestMatch == null)
            {
                // Unknown character, use [UNK]
                yield return _vocab.GetValueOrDefault("[UNK]", 100);
                start++;
            }
            else
            {
                yield return _vocab[bestMatch];
                start = end;
            }
        }
    }

    private float[] MeanPool(Tensor<float> hiddenState, int seqLen)
    {
        var embedding = new float[Dimension];
        
        // Mean pool over sequence length (skip [CLS] and [SEP])
        for (int d = 0; d < Dimension; d++)
        {
            float sum = 0;
            for (int s = 0; s < seqLen; s++)
            {
                sum += hiddenState[0, s, d];
            }
            embedding[d] = sum / seqLen;
        }

        // L2 normalize
        var norm = (float)Math.Sqrt(embedding.Sum(x => x * x));
        if (norm > 0)
        {
            for (int i = 0; i < embedding.Length; i++)
                embedding[i] /= norm;
        }

        return embedding;
    }

    public void Dispose()
    {
        _session?.Dispose();
        _initLock.Dispose();
    }
}
