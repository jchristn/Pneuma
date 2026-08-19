namespace Pneuma.Server.Streaming
{
    using System;
    using System.Collections.Generic;
    using System.Threading;
    using System.Threading.Tasks;
    using Pneuma.Core.Serialization;
    using WatsonWebserver.Core;

    /// <summary>
    /// Writes server-sent events (SSE) to a Watson response for streaming surfaces such as grounded
    /// chat. Each event payload is serialized to JSON (camelCase) and sent as one SSE frame; callers
    /// mark the final frame so the connection closes cleanly. The event schema is a JSON object with a
    /// <c>type</c> discriminator (<c>metadata</c>, <c>delta</c>, <c>complete</c>, <c>error</c>).
    /// </summary>
    public class SseWriter
    {
        #region Private-Members

        private readonly HttpContextBase _Ctx;

        #endregion

        #region Constructors-and-Factories

        /// <summary>Begin an SSE response on the given context.</summary>
        /// <param name="ctx">HTTP context.</param>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="ctx"/> is null.</exception>
        public SseWriter(HttpContextBase ctx)
        {
            if (ctx == null) throw new ArgumentNullException(nameof(ctx));
            _Ctx = ctx;
            _Ctx.Response.ServerSentEvents = true;
            _Ctx.Response.ContentType = "text/event-stream";
        }

        #endregion

        #region Public-Methods

        /// <summary>Send one event frame carrying a JSON payload.</summary>
        /// <param name="payload">Event payload; serialized to JSON.</param>
        /// <param name="isFinal">True for the last frame (closes the stream).</param>
        /// <param name="token">Cancellation token.</param>
        public async Task SendAsync(object payload, bool isFinal, CancellationToken token = default)
        {
            ServerSentEvent sse = new ServerSentEvent { Data = Json.Serialize(payload) };
            await _Ctx.Response.SendEvent(sse, isFinal, token).ConfigureAwait(false);
        }

        /// <summary>
        /// Split a text answer into ordered delta chunks for streaming. Splits on whitespace boundaries
        /// so words are never broken, packing up to <paramref name="chunkSize"/> characters per chunk.
        /// Used to stream a fully-computed answer as incremental deltas until true token streaming is
        /// wired through the model layer.
        /// </summary>
        /// <param name="text">Full answer text.</param>
        /// <param name="chunkSize">Approximate maximum characters per chunk; minimum 1.</param>
        /// <returns>Ordered chunks whose concatenation equals the input.</returns>
        public static List<string> SplitIntoChunks(string text, int chunkSize)
        {
            List<string> chunks = new List<string>();
            if (String.IsNullOrEmpty(text)) return chunks;

            int size = Math.Max(1, chunkSize);
            int start = 0;
            while (start < text.Length)
            {
                int length = Math.Min(size, text.Length - start);

                // Prefer to end the chunk at the last whitespace within the window so words are not split,
                // unless this is the final chunk or no whitespace exists in the window.
                if (start + length < text.Length)
                {
                    int lastSpace = text.LastIndexOf(' ', start + length - 1, length);
                    if (lastSpace > start)
                    {
                        length = lastSpace - start + 1;
                    }
                }

                chunks.Add(text.Substring(start, length));
                start += length;
            }

            return chunks;
        }

        #endregion
    }
}
