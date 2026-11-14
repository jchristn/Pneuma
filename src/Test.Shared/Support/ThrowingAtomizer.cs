namespace Test.Shared.Support
{
    using System;
    using System.Collections.Generic;
    using System.Threading;
    using System.Threading.Tasks;
    using Pneuma.Core.Integrations.Abstractions;
    using Pneuma.Core.Integrations.Models;

    /// <summary>An atomizer whose every call fails, standing in for an unreachable DocumentAtom.</summary>
    public class ThrowingAtomizer : IAtomizer
    {
        /// <summary>How many times type detection was attempted.</summary>
        public int DetectCalls { get; private set; } = 0;

        /// <inheritdoc />
        public Task<TypeDetectResult> DetectTypeAsync(byte[] data, CancellationToken token = default)
        {
            DetectCalls++;
            throw new InvalidOperationException("DocumentAtom is unreachable.");
        }

        /// <inheritdoc />
        public Task<List<ExtractedCell>> ExtractCellsAsync(string documentType, byte[] data, CancellationToken token = default)
        {
            throw new InvalidOperationException("DocumentAtom is unreachable.");
        }
    }
}
