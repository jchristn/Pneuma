namespace Pneuma.Chunking.Chunking
{
    using Pneuma.Chunking.Models;
    using Pneuma.Chunking.Tokenization;

    /// <summary>
    /// Static methods for table-specific chunking strategies.
    /// All methods assume table[0] is the header row.
    /// Tables with 0 or 1 rows (header only) return empty.
    /// </summary>
    public static class TableChunker
    {
        /// <summary>
        /// Each data row as space-separated values (no headers).
        /// </summary>
        public static List<string> ChunkByRow(List<List<string>> table, ChunkingConfiguration config, ITokenizerAdapter tokenizer, int tokenLimit)
        {
            if (table.Count <= 1) return new List<string>();

            List<string> chunks = new List<string>();
            for (int i = 1; i < table.Count; i++)
            {
                string rowText = string.Join(" ", table[i]);
                if (tokenizer.CountTokens(rowText) <= tokenLimit)
                    chunks.Add(rowText);
                else
                    chunks.AddRange(ChunkCells(table[i], " ", config, tokenizer, tokenLimit));
            }
            return chunks;
        }

        /// <summary>
        /// Each data row as a markdown table with headers prepended.
        /// </summary>
        public static List<string> ChunkByRowWithHeaders(List<List<string>> table, ChunkingConfiguration config, ITokenizerAdapter tokenizer, int tokenLimit)
        {
            if (table.Count <= 1) return new List<string>();

            List<string> headers = table[0];

            List<string> chunks = new List<string>();
            for (int i = 1; i < table.Count; i++)
            {
                string rowChunk = SerializeRowWithHeaders(headers, table[i]);
                if (tokenizer.CountTokens(rowChunk) <= tokenLimit)
                {
                    chunks.Add(rowChunk);
                }
                else
                {
                    chunks.AddRange(ChunkHeaderValueCells(headers, table[i], config, tokenizer, tokenLimit, ", "));
                }
            }
            return chunks;
        }

        /// <summary>
        /// Groups of N data rows with headers prepended (markdown table format).
        /// </summary>
        public static List<string> ChunkByRowGroupWithHeaders(List<List<string>> table, int groupSize, ChunkingConfiguration config, ITokenizerAdapter tokenizer, int tokenLimit)
        {
            if (table.Count <= 1) return new List<string>();
            if (groupSize < 1) groupSize = 1;

            List<string> headers = table[0];

            List<string> chunks = new List<string>();
            for (int i = 1; i < table.Count; i += groupSize)
            {
                List<List<string>> groupRows = new List<List<string>>();
                for (int j = i; j < i + groupSize && j < table.Count; j++)
                {
                    groupRows.Add(table[j]);
                }

                string groupChunk = SerializeRowGroupWithHeaders(headers, groupRows);
                if (tokenizer.CountTokens(groupChunk) <= tokenLimit)
                {
                    chunks.Add(groupChunk);
                }
                else
                {
                    foreach (List<string> row in groupRows)
                    {
                        string rowChunk = SerializeRowWithHeaders(headers, row);
                        if (tokenizer.CountTokens(rowChunk) <= tokenLimit)
                            chunks.Add(rowChunk);
                        else
                            chunks.AddRange(ChunkHeaderValueCells(headers, row, config, tokenizer, tokenLimit, ", "));
                    }
                }
            }
            return chunks;
        }

        /// <summary>
        /// Each data row as key-value pairs: "key1: val1, key2: val2, ...".
        /// </summary>
        public static List<string> ChunkByKeyValuePairs(List<List<string>> table, ChunkingConfiguration config, ITokenizerAdapter tokenizer, int tokenLimit)
        {
            if (table.Count <= 1) return new List<string>();

            List<string> headers = table[0];
            List<string> chunks = new List<string>();

            for (int i = 1; i < table.Count; i++)
            {
                List<string> pairs = new List<string>();
                for (int j = 0; j < headers.Count && j < table[i].Count; j++)
                {
                    pairs.Add(headers[j] + ": " + table[i][j]);
                }

                string rowText = string.Join(", ", pairs);
                if (tokenizer.CountTokens(rowText) <= tokenLimit)
                {
                    chunks.Add(rowText);
                }
                else
                {
                    chunks.AddRange(ChunkingHelpers.ChunkUnits(
                        pairs,
                        ", ",
                        tokenLimit,
                        tokenizer,
                        0,
                        pair => ChunkingHelpers.ChunkByTokenSpans(pair, config, tokenizer, tokenLimit)));
                }
            }
            return chunks;
        }

        /// <summary>
        /// Entire table as a single markdown table chunk.
        /// </summary>
        public static List<string> ChunkWholeTable(List<List<string>> table, int rowGroupSize, ChunkingConfiguration config, ITokenizerAdapter tokenizer, int tokenLimit)
        {
            if (table.Count <= 1) return new List<string>();

            string wholeTable = SerializeWholeTable(table);
            if (tokenizer.CountTokens(wholeTable) <= tokenLimit)
                return new List<string> { wholeTable };

            return ChunkByRowGroupWithHeaders(table, rowGroupSize, config, tokenizer, tokenLimit);
        }

        private static string SerializeWholeTable(List<List<string>> table)
        {
            List<string> headers = table[0];
            string headerLine = "| " + string.Join(" | ", headers) + " |";
            string separatorLine = "|" + string.Join("|", headers.Select(_ => "---")) + "|";
            List<string> lines = new List<string> { headerLine, separatorLine };

            for (int i = 1; i < table.Count; i++)
                lines.Add("| " + string.Join(" | ", table[i]) + " |");

            return string.Join("\n", lines);
        }

        private static string SerializeRowWithHeaders(List<string> headers, List<string> row)
        {
            string headerLine = "| " + string.Join(" | ", headers) + " |";
            string separatorLine = "|" + string.Join("|", headers.Select(_ => "---")) + "|";
            string rowLine = "| " + string.Join(" | ", row) + " |";
            return headerLine + "\n" + separatorLine + "\n" + rowLine;
        }

        private static string SerializeRowGroupWithHeaders(List<string> headers, List<List<string>> rows)
        {
            string headerLine = "| " + string.Join(" | ", headers) + " |";
            string separatorLine = "|" + string.Join("|", headers.Select(_ => "---")) + "|";
            List<string> rowLines = rows.Select(row => "| " + string.Join(" | ", row) + " |").ToList();
            return headerLine + "\n" + separatorLine + "\n" + string.Join("\n", rowLines);
        }

        private static List<string> ChunkCells(
            List<string> cells,
            string separator,
            ChunkingConfiguration config,
            ITokenizerAdapter tokenizer,
            int tokenLimit)
        {
            return ChunkingHelpers.ChunkUnits(
                cells,
                separator,
                tokenLimit,
                tokenizer,
                0,
                cell => ChunkingHelpers.ChunkByTokenSpans(cell, config, tokenizer, tokenLimit));
        }

        private static List<string> ChunkHeaderValueCells(
            List<string> headers,
            List<string> row,
            ChunkingConfiguration config,
            ITokenizerAdapter tokenizer,
            int tokenLimit,
            string separator)
        {
            List<string> cellUnits = new List<string>();
            for (int i = 0; i < headers.Count && i < row.Count; i++)
            {
                cellUnits.Add(headers[i] + ": " + row[i]);
            }

            return ChunkingHelpers.ChunkUnits(
                cellUnits,
                separator,
                tokenLimit,
                tokenizer,
                0,
                cell => ChunkingHelpers.ChunkByTokenSpans(cell, config, tokenizer, tokenLimit));
        }
    }
}
