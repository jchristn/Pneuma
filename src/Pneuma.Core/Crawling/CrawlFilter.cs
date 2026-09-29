namespace Pneuma.Core.Crawling
{
    using System;
    using System.Collections.Generic;

    /// <summary>
    /// Which enumerated objects a crawl plan keeps. Patterns are globs matched against the object's key (a URL for web
    /// and sitemap plans, a key or path for buckets and shares): <c>*</c> matches any run of characters, <c>?</c> one
    /// character. An object is kept when it matches an include pattern (or there are none), matches no exclude pattern,
    /// has an allowed content type (or the list is empty), and is within the size bounds.
    /// </summary>
    public class CrawlFilter
    {
        #region Public-Members

        /// <summary>Globs an object key must match (any of) to be kept; empty keeps everything. Never null.</summary>
        public List<string> IncludePatterns
        {
            get { return _IncludePatterns; }
            set { _IncludePatterns = value ?? new List<string>(); }
        }

        /// <summary>Globs that exclude an object key. Never null.</summary>
        public List<string> ExcludePatterns
        {
            get { return _ExcludePatterns; }
            set { _ExcludePatterns = value ?? new List<string>(); }
        }

        /// <summary>
        /// Content types to keep (for example text/html, application/pdf); matching ignores parameters and case. Empty
        /// keeps every type. Never null.
        /// </summary>
        public List<string> AllowedContentTypes
        {
            get { return _AllowedContentTypes; }
            set { _AllowedContentTypes = value ?? new List<string>(); }
        }

        /// <summary>Smallest object kept, in bytes; 0 for no minimum. Minimum 0.</summary>
        public long MinSizeBytes
        {
            get { return _MinSizeBytes; }
            set { _MinSizeBytes = Math.Max(0L, value); }
        }

        /// <summary>Largest object kept, in bytes; 0 for no maximum. Minimum 0.</summary>
        public long MaxSizeBytes
        {
            get { return _MaxSizeBytes; }
            set { _MaxSizeBytes = Math.Max(0L, value); }
        }

        /// <summary>Most objects one run keeps; 0 for no limit. Clamped to [0, 1000000].</summary>
        public int MaxObjects
        {
            get { return _MaxObjects; }
            set { _MaxObjects = Math.Clamp(value, 0, 1000000); }
        }

        #endregion

        #region Private-Members

        private List<string> _IncludePatterns = new List<string>();
        private List<string> _ExcludePatterns = new List<string>();
        private List<string> _AllowedContentTypes = new List<string>();
        private long _MinSizeBytes = 0;
        private long _MaxSizeBytes = 0;
        private int _MaxObjects = 0;

        #endregion
    }
}
