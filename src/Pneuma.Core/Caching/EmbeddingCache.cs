namespace Pneuma.Core.Caching
{
    using System;
    using System.Collections.Generic;
    using System.Security.Cryptography;
    using System.Text;
    using global::Caching;

    /// <summary>
    /// Bounded, thread-safe cache of embedding vectors keyed by (embedding model, content hash), so identical
    /// text is not re-embedded across re-ingestions or duplicate content. Backed by an LRU cache with a
    /// configurable capacity (a global system limit); a capacity of zero disables caching entirely. Cached
    /// vectors are copied in and out so a caller mutating a returned list cannot corrupt a shared entry.
    /// </summary>
    public class EmbeddingCache
    {
        #region Private-Members

        private readonly LRUCache<string, float[]>? _Cache;
        private readonly int _Capacity;

        #endregion

        #region Constructors-and-Factories

        /// <summary>Instantiate the cache.</summary>
        /// <param name="capacity">Maximum number of embeddings to retain across all subjects; zero or negative disables the cache.</param>
        public EmbeddingCache(int capacity)
        {
            _Capacity = capacity < 0 ? 0 : capacity;
            if (_Capacity > 0)
            {
                int evict = Math.Max(1, _Capacity / 10);
                _Cache = new LRUCache<string, float[]>(_Capacity, evict, StringComparer.Ordinal);
            }
        }

        #endregion

        #region Public-Members

        /// <summary>Whether caching is enabled (capacity &gt; 0).</summary>
        public bool Enabled { get { return _Cache != null; } }

        /// <summary>The configured maximum number of cached embeddings.</summary>
        public int Capacity { get { return _Capacity; } }

        #endregion

        #region Public-Methods

        /// <summary>Number of embeddings currently cached (0 when disabled).</summary>
        /// <returns>The cached entry count.</returns>
        public int Count()
        {
            return _Cache != null ? _Cache.Count() : 0;
        }

        /// <summary>Try to retrieve a cached embedding for the given embedding model and text.</summary>
        /// <param name="model">Embedding model / endpoint identifier (part of the key, so vectors from different models never collide).</param>
        /// <param name="text">The text that was embedded.</param>
        /// <param name="embedding">The cached embedding (a fresh copy), or an empty list on miss.</param>
        /// <returns>True on a cache hit.</returns>
        public bool TryGet(string? model, string text, out List<float> embedding)
        {
            embedding = new List<float>();
            if (_Cache == null || String.IsNullOrEmpty(text)) return false;
            if (_Cache.TryGet(Key(model, text), out float[]? vector) && vector != null && vector.Length > 0)
            {
                embedding = new List<float>(vector);
                return true;
            }
            return false;
        }

        /// <summary>Store an embedding for the given embedding model and text. No-op when disabled or the vector is empty.</summary>
        /// <param name="model">Embedding model / endpoint identifier.</param>
        /// <param name="text">The text that was embedded.</param>
        /// <param name="embedding">The embedding vector to cache.</param>
        public void Set(string? model, string text, IReadOnlyList<float>? embedding)
        {
            if (_Cache == null || String.IsNullOrEmpty(text) || embedding == null || embedding.Count == 0) return;
            float[] copy = new float[embedding.Count];
            for (int i = 0; i < embedding.Count; i++) copy[i] = embedding[i];
            _Cache.AddReplace(Key(model, text), copy, null);
        }

        #endregion

        #region Private-Methods

        private static string Key(string? model, string text)
        {
            using (SHA256 sha = SHA256.Create())
            {
                byte[] hash = sha.ComputeHash(Encoding.UTF8.GetBytes(text));
                StringBuilder builder = new StringBuilder((model != null ? model.Length : 0) + 1 + (hash.Length * 2));
                builder.Append(model ?? String.Empty);
                builder.Append(':');
                for (int i = 0; i < hash.Length; i++) builder.Append(hash[i].ToString("x2"));
                return builder.ToString();
            }
        }

        #endregion
    }
}
