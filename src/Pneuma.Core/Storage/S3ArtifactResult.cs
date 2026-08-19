namespace Pneuma.Core.Storage
{
    using System;

    /// <summary>
    /// The bytes and content type of a single artifact retrieved from object storage.
    /// </summary>
    public class S3ArtifactResult
    {
        #region Public-Members

        /// <summary>Raw artifact bytes.</summary>
        public byte[] Data
        {
            get
            {
                return _Data;
            }
            set
            {
                _Data = value ?? throw new ArgumentNullException(nameof(Data));
            }
        }

        /// <summary>MIME content type reported for the stored object.</summary>
        public string ContentType
        {
            get
            {
                return _ContentType;
            }
            set
            {
                _ContentType = value ?? "application/octet-stream";
            }
        }

        #endregion

        #region Private-Members

        private byte[] _Data = Array.Empty<byte>();
        private string _ContentType = "application/octet-stream";

        #endregion

        #region Constructors-and-Factories

        /// <summary>Instantiate an empty artifact result.</summary>
        public S3ArtifactResult()
        {
        }

        /// <summary>Instantiate an artifact result.</summary>
        /// <param name="data">Artifact bytes.</param>
        /// <param name="contentType">MIME content type.</param>
        public S3ArtifactResult(byte[] data, string contentType)
        {
            Data = data;
            ContentType = contentType;
        }

        #endregion
    }
}
