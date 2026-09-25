namespace Test.Benchmark.Reference
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Security.Cryptography;
    using System.Text;
    using System.Text.Json;
    using System.Threading;
    using System.Threading.Tasks;
    using Test.Benchmark.Client;

    /// <summary>
    /// A disk-backed embedding cache keyed by (model, SHA-256 of text), so the reference arm embeds each chunk once
    /// across runs. One JSONL file per model under the cache directory; new vectors are appended.
    /// </summary>
    public class EmbeddingCache
    {
        #region Public-Members

        /// <summary>
        /// Vectors served from the cache in this process.
        /// </summary>
        public int Hits { get; private set; } = 0;

        /// <summary>
        /// Vectors computed in this process.
        /// </summary>
        public int Misses { get; private set; } = 0;

        #endregion

        #region Private-Members

        private readonly string _Path;
        private readonly Dictionary<string, float[]> _Vectors = new Dictionary<string, float[]>(StringComparer.Ordinal);
        private readonly SemaphoreSlim _Lock = new SemaphoreSlim(1, 1);

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Open (or create) the cache for a model.
        /// </summary>
        /// <param name="directory">Cache directory.</param>
        /// <param name="model">Model name.</param>
        public EmbeddingCache(string directory, string model)
        {
            Directory.CreateDirectory(directory);
            StringBuilder safe = new StringBuilder();
            foreach (char c in model) safe.Append(char.IsLetterOrDigit(c) || c == '-' || c == '.' ? c : '_');
            _Path = Path.Combine(directory, "embeddings-" + safe + ".jsonl");
            if (!File.Exists(_Path)) return;
            foreach (string line in File.ReadLines(_Path))
            {
                if (string.IsNullOrWhiteSpace(line)) continue;
                try
                {
                    CachedEmbedding? entry = JsonSerializer.Deserialize<CachedEmbedding>(line);
                    if (entry != null && entry.Vector.Length > 0) _Vectors[entry.Hash] = entry.Vector;
                }
                catch (JsonException)
                {
                }
            }
        }

        #endregion

        #region Public-Methods

        /// <summary>
        /// Embed texts, serving cached vectors and computing (and persisting) the rest.
        /// </summary>
        /// <param name="client">Embedding client.</param>
        /// <param name="texts">Texts.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>One vector per text.</returns>
        public async Task<List<float[]>> EmbedAsync(DirectModelClient client, IReadOnlyList<string> texts, CancellationToken token)
        {
            float[]?[] result = new float[]?[texts.Count];
            List<string> missingTexts = new List<string>();
            List<int> missingIndexes = new List<int>();
            for (int i = 0; i < texts.Count; i++)
            {
                if (_Vectors.TryGetValue(Hash(texts[i]), out float[]? cached))
                {
                    result[i] = cached;
                    Hits++;
                }
                else
                {
                    missingTexts.Add(texts[i]);
                    missingIndexes.Add(i);
                }
            }

            if (missingTexts.Count > 0)
            {
                List<float[]> computed = await client.EmbedAsync(missingTexts, 32, token).ConfigureAwait(false);
                await _Lock.WaitAsync(token).ConfigureAwait(false);
                try
                {
                    using (StreamWriter writer = new StreamWriter(_Path, true, new UTF8Encoding(false)))
                    {
                        for (int i = 0; i < computed.Count; i++)
                        {
                            string hash = Hash(missingTexts[i]);
                            _Vectors[hash] = computed[i];
                            result[missingIndexes[i]] = computed[i];
                            await writer.WriteLineAsync(JsonSerializer.Serialize(new CachedEmbedding { Hash = hash, Vector = computed[i] })).ConfigureAwait(false);
                        }
                    }
                }
                finally
                {
                    _Lock.Release();
                }

                Misses += computed.Count;
            }

            List<float[]> vectors = new List<float[]>(texts.Count);
            foreach (float[]? vector in result) vectors.Add(vector ?? new float[0]);
            return vectors;
        }

        #endregion

        #region Private-Methods

        private static string Hash(string text)
        {
            byte[] bytes = SHA256.HashData(Encoding.UTF8.GetBytes(text ?? string.Empty));
            return Convert.ToHexString(bytes);
        }

        #endregion
    }
}
