namespace Test.Benchmark.Reference
{
    using System;
    using System.Collections.Generic;
    using System.Linq;

    /// <summary>
    /// Brute-force cosine search over chunk vectors, scoring each document by its best chunk (max-pooling).
    /// </summary>
    public class DenseIndex
    {
        #region Public-Members

        /// <summary>
        /// Indexed chunks.
        /// </summary>
        public int Count
        {
            get
            {
                return _Vectors.Count;
            }
        }

        #endregion

        #region Private-Members

        private readonly List<string> _DocIds = new List<string>();
        private readonly List<float[]> _Vectors = new List<float[]>();

        #endregion

        #region Public-Methods

        /// <summary>
        /// Add a chunk vector for a document.
        /// </summary>
        /// <param name="docId">Document id.</param>
        /// <param name="vector">Chunk vector (normalized here).</param>
        public void Add(string docId, float[] vector)
        {
            if (vector == null || vector.Length == 0) return;
            _DocIds.Add(docId);
            _Vectors.Add(Normalize(vector));
        }

        /// <summary>
        /// Rank documents for a query vector.
        /// </summary>
        /// <param name="query">Query vector.</param>
        /// <param name="k">Results.</param>
        /// <returns>Document ids, best first.</returns>
        public List<string> Search(float[] query, int k)
        {
            float[] q = Normalize(query);
            Dictionary<string, double> best = new Dictionary<string, double>(StringComparer.Ordinal);
            for (int i = 0; i < _Vectors.Count; i++)
            {
                float[] v = _Vectors[i];
                if (v.Length != q.Length) continue;
                double dot = 0.0;
                for (int d = 0; d < v.Length; d++) dot += v[d] * q[d];
                if (!best.TryGetValue(_DocIds[i], out double existing) || dot > existing) best[_DocIds[i]] = dot;
            }

            return best.OrderByDescending(b => b.Value).ThenBy(b => b.Key, StringComparer.Ordinal).Take(k).Select(b => b.Key).ToList();
        }

        #endregion

        #region Private-Methods

        private static float[] Normalize(float[] vector)
        {
            double norm = 0.0;
            foreach (float v in vector) norm += v * v;
            if (norm <= 0.0) return vector;
            float scale = (float)(1.0 / Math.Sqrt(norm));
            float[] result = new float[vector.Length];
            for (int i = 0; i < vector.Length; i++) result[i] = vector[i] * scale;
            return result;
        }

        #endregion
    }
}
