namespace Pneuma.Server.Services
{
    using System;
    using System.IO;
    using System.Threading;
    using System.Threading.Tasks;
    using WatsonWebserver.Core;

    /// <summary>
    /// Reads a request body with a size limit, whether it arrives with a <c>Content-Length</c> or with chunked transfer
    /// encoding. A declared length over the limit is refused before anything is read; a chunked body is refused as soon
    /// as the bytes received pass the limit, so an oversized upload is never buffered whole.
    /// </summary>
    public static class RequestBodyReader
    {
        #region Public-Methods

        /// <summary>Read the body of a request.</summary>
        /// <param name="ctx">HTTP context.</param>
        /// <param name="maxBytes">Largest accepted body, in bytes.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The body, or a result marked <see cref="RequestBodyResult.TooLarge"/>. A request with neither a length nor
        /// chunked encoding has an empty body.</returns>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="ctx"/> is null.</exception>
        public static async Task<RequestBodyResult> ReadAsync(HttpContextBase ctx, long maxBytes, CancellationToken token)
        {
            if (ctx == null) throw new ArgumentNullException(nameof(ctx));
            RequestBodyResult result = new RequestBodyResult();

            if (ctx.Request.ChunkedTransfer)
            {
                result.Chunked = true;
                using (MemoryStream buffer = new MemoryStream())
                {
                    while (true)
                    {
                        Chunk? chunk = await ctx.Request.ReadChunk(token).ConfigureAwait(false);
                        if (chunk == null) break;
                        if (chunk.Data != null && chunk.Length > 0)
                        {
                            int length = Math.Min(chunk.Length, chunk.Data.Length);
                            if (buffer.Length + length > maxBytes)
                            {
                                result.TooLarge = true;
                                result.BytesRead = buffer.Length + length;
                                return result;
                            }

                            buffer.Write(chunk.Data, 0, length);
                        }

                        if (chunk.IsFinal) break;
                    }

                    result.Data = buffer.ToArray();
                    result.BytesRead = buffer.Length;
                    return result;
                }
            }

            long declared = ctx.Request.ContentLength;
            if (declared > maxBytes)
            {
                result.TooLarge = true;
                result.BytesRead = 0;
                return result;
            }

            result.Data = declared > 0 ? ctx.Request.DataAsBytes : Array.Empty<byte>();
            result.BytesRead = result.Data?.LongLength ?? 0;
            return result;
        }

        #endregion
    }
}
