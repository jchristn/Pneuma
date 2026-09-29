namespace Test.Shared.Suites
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.IO.Compression;
    using System.Linq;
    using System.Text;
    using System.Threading;
    using System.Threading.Tasks;
    using Pneuma.Core.Crawling;
    using Pneuma.Core.Crawling.Crawlers;
    using Pneuma.Core.Enums;
    using Pneuma.Core.Ingestion.Configuration;
    using Pneuma.Core.Ingestion.Models;
    using Pneuma.Core.Integrations.Implementations;
    using Pneuma.Core.Models;
    using Test.Shared.Support;
    using Touchstone.Core;

    /// <summary>
    /// The built-in connectors: the web crawler (CrawlSharp) and the sitemap crawler against a loopback stub site,
    /// the shared bucket and share crawler logic over a local directory, and the connectivity tests of the S3, CIFS,
    /// and NFS crawlers.
    /// </summary>
    public static class CrawlersSuite
    {
        /// <summary>Build the suite.</summary>
        /// <returns>Suite descriptor.</returns>
        public static TestSuiteDescriptor Build()
        {
            return new TestSuiteDescriptor(
                suiteId: "Crawlers",
                displayName: "Built-in crawlers",
                cases: new List<TestCaseDescriptor>
                {
                    new TestCaseDescriptor("Crawlers", "Web_FollowsLinksWithinScope", "The web crawler follows links on the start host, drops tracking parameters, skips other hosts, and versions pages by content",
                        executeAsync: async ct =>
                        {
                            using (StubWebSite site = new StubWebSite())
                            using (CrawlHttpClient http = AllowLoopback())
                            {
                                SeedSite(site);
                                WebSiteCrawler crawler = new WebSiteCrawler(http);
                                List<CrawledObject> pages = await ListAsync(crawler, WebPlan(site, null), ct);
                                List<string> keys = pages.Select(p => p.Key).OrderBy(k => k, StringComparer.Ordinal).ToList();
                                Expect(keys.Contains(site.Url("/")) && keys.Contains(site.Url("/a")) && keys.Contains(site.Url("/b")) && keys.Contains(site.Url("/c")), "all linked pages: " + String.Join(", ", keys));
                                Expect(!keys.Any(k => k.Contains("utm_source")), "tracking parameters dropped: " + String.Join(", ", keys));
                                Expect(!keys.Any(k => k.Contains("example.invalid")), "other hosts skipped");
                                Expect(keys.Distinct().Count() == keys.Count, "no duplicates");
                                Expect(pages.All(p => p.VersionToken != null && p.VersionToken.StartsWith("sha256:")), "content versions");
                                Expect(pages.First(p => p.Key == site.Url("/a")).ContentType == "text/html", "media type without parameters");
                            }
                        }),

                    new TestCaseDescriptor("Crawlers", "Web_MaxPagesStops", "The web crawler stops after the plan's page limit",
                        executeAsync: async ct =>
                        {
                            using (StubWebSite site = new StubWebSite())
                            using (CrawlHttpClient http = AllowLoopback())
                            {
                                SeedSite(site);
                                List<CrawledObject> pages = await ListAsync(new WebSiteCrawler(http), WebPlan(site, w => w.MaxPages = 2), ct);
                                Expect(pages.Count == 2, "2 pages, got " + pages.Count);
                            }
                        }),

                    new TestCaseDescriptor("Crawlers", "Web_RobotsTxtHonored", "Pages robots.txt disallows are skipped, unless the plan turns robots.txt off",
                        executeAsync: async ct =>
                        {
                            using (StubWebSite site = new StubWebSite())
                            using (CrawlHttpClient http = AllowLoopback())
                            {
                                SeedSite(site);
                                site.Set("/robots.txt", new StubPage { ContentType = "text/plain", Body = Encoding.UTF8.GetBytes("User-agent: *\nDisallow: /b\n") });
                                WebSiteCrawler crawler = new WebSiteCrawler(http);
                                List<string> polite = (await ListAsync(crawler, WebPlan(site, null), ct)).Select(p => p.Key).ToList();
                                Expect(!polite.Contains(site.Url("/b")) && polite.Contains(site.Url("/a")), "the disallowed page is skipped: " + String.Join(", ", polite));
                                List<string> impolite = (await ListAsync(crawler, WebPlan(site, w => w.RespectRobotsTxt = false), ct)).Select(p => p.Key).ToList();
                                Expect(impolite.Contains(site.Url("/b")), "robots.txt off includes it: " + String.Join(", ", impolite));
                            }
                        }),

                    new TestCaseDescriptor("Crawlers", "Web_AuthenticationUsed", "Basic authentication is sent while crawling, testing, and reading content; without it the test reports the auth step",
                        executeAsync: async ct =>
                        {
                            using (StubWebSite site = new StubWebSite())
                            using (CrawlHttpClient http = AllowLoopback())
                            {
                                SeedSite(site);
                                site.RequiredAuthorization = "Basic " + Convert.ToBase64String(Encoding.UTF8.GetBytes("reader:pw"));
                                WebSiteCrawler crawler = new WebSiteCrawler(http);
                                CrawlPlan authed = WebPlan(site, w => { w.Authentication = "Basic"; w.Username = "reader"; w.Password = "pw"; });
                                List<CrawledObject> pages = await ListAsync(crawler, authed, ct);
                                Expect(pages.Count >= 3, "authenticated crawl finds pages, got " + pages.Count);
                                ResolvedContent content = await crawler.OpenAsync(authed, site.Url("/a"), ct);
                                Expect(Encoding.UTF8.GetString(content.Bytes).Contains("Page A"), "content read with authentication");
                                ConnectivityResult ok = await crawler.TestAsync(authed, ct);
                                Expect(ok.Success, "test passes with credentials: " + Describe(ok));

                                ConnectivityResult denied = await crawler.TestAsync(WebPlan(site, null), ct);
                                Expect(!denied.Success && denied.Layers.Last().Name == "auth", "test stops at the auth step: " + Describe(denied));
                            }
                        }),

                    new TestCaseDescriptor("Crawlers", "Web_PrivateStartUrlBlocked", "A start URL on a private address is refused unless allowed, and the test names the policy step",
                        executeAsync: async ct =>
                        {
                            using (StubWebSite site = new StubWebSite())
                            using (CrawlHttpClient http = new CrawlHttpClient(new FetchSafetyPolicy(new FetchSafetySettings())))
                            {
                                SeedSite(site);
                                WebSiteCrawler crawler = new WebSiteCrawler(http);
                                bool blocked = false;
                                try { await ListAsync(crawler, WebPlan(site, null), ct); } catch (FetchBlockedException) { blocked = true; }
                                Expect(blocked, "enumeration refused");
                                ConnectivityResult result = await crawler.TestAsync(WebPlan(site, null), ct);
                                Expect(!result.Success && result.Layers.Last().Name == "policy", "test stops at the policy step: " + Describe(result));
                                Expect(site.Hits("/") == 0, "the site was never contacted");
                            }
                        }),

                    new TestCaseDescriptor("Crawlers", "Web_InvalidSettingsReported", "A web plan without start URLs, or with a non-http start URL, fails the settings step",
                        executeAsync: async ct =>
                        {
                            using (CrawlHttpClient http = AllowLoopback())
                            {
                                WebSiteCrawler crawler = new WebSiteCrawler(http);
                                ConnectivityResult none = await crawler.TestAsync(new CrawlPlan { Type = CrawlPlanTypeEnum.Web, Web = new WebCrawlSettings() }, ct);
                                ConnectivityResult ftp = await crawler.TestAsync(new CrawlPlan { Type = CrawlPlanTypeEnum.Web, Web = new WebCrawlSettings { StartUrls = new List<string> { "ftp://files.example/" } } }, ct);
                                Expect(!none.Success && none.Layers[0].Name == "settings" && !ftp.Success && ftp.Layers[0].Name == "settings", "settings step fails");
                            }
                        }),

                    new TestCaseDescriptor("Crawlers", "Web_EndToEndSync", "A web plan run end to end adds every page, re-ingests only the page that changed, and marks a removed page Missing",
                        executeAsync: async ct =>
                        {
                            using (StubWebSite site = new StubWebSite())
                            using (CrawlHttpClient http = AllowLoopback())
                            {
                                SeedSite(site);
                                await using (CrawlTestRig rig = await CrawlTestRig.CreateAsync(ct))
                                {
                                    rig.Crawlers.Register(new WebSiteCrawler(http));
                                    CrawlPlan plan = await rig.CreatePlanAsync(p => p.Web = new WebCrawlSettings { StartUrls = new List<string> { site.Url("/") }, CrawlDelayMs = 0 }, ct);
                                    CrawlOperation first = await rig.RunToEndAsync(plan.Id, ct);
                                    Expect(first.Status == CrawlOperationStatusEnum.Succeeded && first.Added == 4, "4 pages added, got " + first.Added + " " + first.Status + " " + first.Error);

                                    site.Html("/c", "<html><body><h1>Page C</h1><p>Page C now says something new about crawling.</p></body></html>");
                                    CrawlOperation second = await rig.RunToEndAsync(plan.Id, ct);
                                    Expect(second.Updated == 1 && second.Unchanged == 3, "only the changed page is re-ingested, got " + second.Updated + " updated " + second.Unchanged + " unchanged");

                                    site.Html("/a", "<html><body><h1>Page A</h1><p>Page A has no link to C any more.</p></body></html>");
                                    site.Remove("/c");
                                    CrawlOperation third = await rig.RunToEndAsync(plan.Id, ct);
                                    Expect(third.Missing == 1, "the removed page is Missing, got " + third.Missing);
                                }
                            }
                        }),

                    new TestCaseDescriptor("Crawlers", "Sitemap_IndexGzipAndLastmod", "The sitemap crawler follows an index into plain and gzipped sitemaps, uses lastmod as the version, and honors the URL limit",
                        executeAsync: async ct =>
                        {
                            using (StubWebSite site = new StubWebSite())
                            using (CrawlHttpClient http = AllowLoopback())
                            {
                                SeedSitemaps(site);
                                SitemapCrawler crawler = new SitemapCrawler(http);
                                CrawlPlan plan = new CrawlPlan { Type = CrawlPlanTypeEnum.Sitemap, Sitemap = new SitemapCrawlSettings { SitemapUrls = new List<string> { site.Url("/sitemap_index.xml") } } };
                                List<CrawledObject> urls = await ListAsync(crawler, plan, ct);
                                Expect(urls.Count == 3, "3 URLs, got " + urls.Count + ": " + String.Join(", ", urls.Select(u => u.Key)));
                                CrawledObject a = urls.First(u => u.Key == site.Url("/a"));
                                Expect(a.VersionToken == "lastmod:2026-09-01T00:00:00.0000000Z", "lastmod is the version, got " + a.VersionToken);
                                Expect(urls.First(u => u.Key == site.Url("/c")).VersionToken == null, "no lastmod means no version");

                                plan.Sitemap.UseLastModified = false;
                                Expect((await ListAsync(crawler, plan, ct)).All(u => u.VersionToken == null), "lastmod ignored when off");
                                plan.Sitemap.MaxUrls = 2;
                                Expect((await ListAsync(crawler, plan, ct)).Count == 2, "URL limit");
                                ConnectivityResult test = await crawler.TestAsync(plan, ct);
                                Expect(test.Success, "test passes: " + Describe(test));
                                ResolvedContent page = await crawler.OpenAsync(plan, site.Url("/a"), ct);
                                Expect(Encoding.UTF8.GetString(page.Bytes).Contains("Page A"), "page content read");
                            }
                        }),

                    new TestCaseDescriptor("Crawlers", "Sitemap_InvalidDocumentFails", "A document that is not a sitemap fails enumeration and the connectivity test; DTDs are refused",
                        executeAsync: async ct =>
                        {
                            using (StubWebSite site = new StubWebSite())
                            using (CrawlHttpClient http = AllowLoopback())
                            {
                                site.Html("/sitemap.xml", "<html><body>not a sitemap</body></html>");
                                SitemapCrawler crawler = new SitemapCrawler(http);
                                CrawlPlan plan = new CrawlPlan { Type = CrawlPlanTypeEnum.Sitemap, Sitemap = new SitemapCrawlSettings { SitemapUrls = new List<string> { site.Url("/sitemap.xml") } } };
                                bool failed = false;
                                try { await ListAsync(crawler, plan, ct); } catch (InvalidDataException) { failed = true; }
                                Expect(failed, "enumeration fails");
                                ConnectivityResult test = await crawler.TestAsync(plan, ct);
                                Expect(!test.Success && test.Layers.Last().Name == "root", "test fails at the root step: " + Describe(test));

                                bool dtdRefused = false;
                                string dtd = "<?xml version=\"1.0\"?><!DOCTYPE urlset [<!ENTITY x \"y\">]><urlset><url><loc>https://a.example/&x;</loc></url></urlset>";
                                try { SitemapCrawler.Parse(Encoding.UTF8.GetBytes(dtd), new List<string>(), new List<CrawledObject>(), true); } catch (InvalidDataException) { dtdRefused = true; }
                                Expect(dtdRefused, "a DTD is refused");
                            }
                        }),

                    new TestCaseDescriptor("Crawlers", "Blob_PrefixSubfoldersAndVersions", "The bucket and share logic lists under a prefix, honors the subfolder switch, guesses content types, and versions by size and time",
                        executeAsync: async ct =>
                        {
                            string root = TempDir();
                            try
                            {
                                Write(root, "docs/a.txt", "alpha");
                                Write(root, "docs/sub/b.md", "# beta");
                                Write(root, "other/c.pdf", "%PDF-1.4");
                                DiskTestCrawler crawler = new DiskTestCrawler(root);
                                CrawlPlan plan = new CrawlPlan { Type = CrawlPlanTypeEnum.S3, S3 = new S3CrawlSettings { Bucket = "test", Prefix = "docs" } };
                                List<CrawledObject> all = await ListAsync(crawler, plan, ct);
                                List<string> keys = all.Select(o => o.Key.Replace('\\', '/')).OrderBy(k => k).ToList();
                                Expect(keys.SequenceEqual(new[] { "docs/a.txt", "docs/sub/b.md" }), "listed under the prefix: " + String.Join(", ", keys));
                                Expect(all.First(o => o.Key.EndsWith("b.md")).ContentType == "text/markdown", "content type guessed from the name");
                                Expect(all.All(o => o.VersionToken != null), "versions present");

                                plan.S3.IncludeSubfolders = false;
                                List<CrawledObject> top = await ListAsync(crawler, plan, ct);
                                Expect(top.Count == 1 && top[0].Key.EndsWith("a.txt"), "subfolders excluded: " + String.Join(", ", top.Select(o => o.Key)));

                                string before = top[0].VersionToken!;
                                Write(root, "docs/a.txt", "alpha, changed");
                                string after = (await ListAsync(crawler, plan, ct))[0].VersionToken!;
                                Expect(before != after, "a changed file gets a new version");

                                ResolvedContent content = await crawler.OpenAsync(plan, top[0].Key, ct);
                                Expect(Encoding.UTF8.GetString(content.Bytes) == "alpha, changed", "content read");
                                bool tooLarge = false;
                                try { await new DiskTestCrawler(root, 4).OpenAsync(plan, top[0].Key, ct); } catch (ContentTooLargeException) { tooLarge = true; }
                                Expect(tooLarge, "the download limit applies");
                            }
                            finally
                            {
                                TryDelete(root);
                            }
                        }),

                    new TestCaseDescriptor("Crawlers", "Blob_EndToEndSync", "A bucket plan run end to end adds files, re-ingests a changed file, and deletes a removed one when deletions are on",
                        executeAsync: async ct =>
                        {
                            string root = TempDir();
                            try
                            {
                                Write(root, "kb/one.txt", "The first document talks about crawling shares.");
                                Write(root, "kb/two.txt", "The second document talks about crawling buckets.");
                                Write(root, "kb/three.txt", "The third document talks about schedules.");
                                await using (CrawlTestRig rig = await CrawlTestRig.CreateAsync(ct))
                                {
                                    rig.Crawlers.Register(new DiskTestCrawler(root));
                                    CrawlPlan plan = await rig.CreatePlanAsync(p =>
                                    {
                                        p.Type = CrawlPlanTypeEnum.S3;
                                        p.Web = null;
                                        p.S3 = new S3CrawlSettings { Bucket = "test", Region = "us-west-1", Prefix = "kb" };
                                        p.ProcessDeletions = true;
                                        p.MaxDeletionFraction = 0.5;
                                    }, ct);
                                    CrawlOperation first = await rig.RunToEndAsync(plan.Id, ct);
                                    Expect(first.Added == 3 && first.Status == CrawlOperationStatusEnum.Succeeded, "3 added, got " + first.Added + " " + first.Status + " " + first.Error);
                                    List<SubjectLink> links = await rig.H.Db.SubjectLinks.EnumerateByCrawlPlanAsync(rig.H.TenantId, plan.Id, ct);
                                    Expect(links.All(l => l.Url.StartsWith("file://")), "links carry the storage URI");

                                    Thread.Sleep(20);
                                    Write(root, "kb/two.txt", "The second document now talks about crawling buckets and prefixes.");
                                    File.Delete(Path.Combine(root, "kb", "three.txt"));
                                    CrawlOperation second = await rig.RunToEndAsync(plan.Id, ct);
                                    Expect(second.Updated == 1 && second.Deleted == 1 && second.Unchanged == 1, "1 updated, 1 deleted, 1 unchanged; got " + second.Updated + "/" + second.Deleted + "/" + second.Unchanged);
                                }
                            }
                            finally
                            {
                                TryDelete(root);
                            }
                        }),

                    new TestCaseDescriptor("Crawlers", "Shares_TestStepsReportProblems", "S3, CIFS, and NFS tests report missing settings and unresolvable hosts at the right step",
                        executeAsync: async ct =>
                        {
                            S3Crawler s3 = new S3Crawler(0);
                            CifsCrawler cifs = new CifsCrawler(0);
                            NfsCrawler nfs = new NfsCrawler(0);
                            ConnectivityResult s3Missing = await s3.TestAsync(new CrawlPlan { Type = CrawlPlanTypeEnum.S3, S3 = new S3CrawlSettings() }, ct);
                            ConnectivityResult s3Keys = await s3.TestAsync(new CrawlPlan { Type = CrawlPlanTypeEnum.S3, S3 = new S3CrawlSettings { Bucket = "b", AccessKey = "only-one" } }, ct);
                            ConnectivityResult cifsMissing = await cifs.TestAsync(new CrawlPlan { Type = CrawlPlanTypeEnum.Cifs, Cifs = new CifsCrawlSettings { Host = "files" } }, ct);
                            ConnectivityResult nfsMissing = await nfs.TestAsync(new CrawlPlan { Type = CrawlPlanTypeEnum.Nfs, Nfs = new NfsCrawlSettings { Host = "files", Export = "/e", Version = "V4" } }, ct);
                            foreach (ConnectivityResult r in new[] { s3Missing, s3Keys, cifsMissing, nfsMissing })
                                Expect(!r.Success && r.Layers.Count == 1 && r.Layers[0].Name == "settings", "settings step fails: " + Describe(r));

                            ConnectivityResult cifsDns = await cifs.TestAsync(new CrawlPlan { Type = CrawlPlanTypeEnum.Cifs, Cifs = new CifsCrawlSettings { Host = "no-such-host.invalid", Share = "docs", Username = "u", Password = "p" } }, ct);
                            ConnectivityResult nfsDns = await nfs.TestAsync(new CrawlPlan { Type = CrawlPlanTypeEnum.Nfs, Nfs = new NfsCrawlSettings { Host = "no-such-host.invalid", Export = "/exports" } }, ct);
                            ConnectivityResult s3Dns = await s3.TestAsync(new CrawlPlan { Type = CrawlPlanTypeEnum.S3, S3 = new S3CrawlSettings { Bucket = "b", Endpoint = "http://no-such-host.invalid:9000/" } }, ct);
                            foreach (ConnectivityResult r in new[] { cifsDns, nfsDns, s3Dns })
                                Expect(!r.Success && r.Layers.Last().Name == "dns", "dns step fails: " + Describe(r));

                            ConnectivityResult refused = await nfs.TestAsync(new CrawlPlan { Type = CrawlPlanTypeEnum.Nfs, Nfs = new NfsCrawlSettings { Host = "127.0.0.1", Export = "/exports" } }, ct);
                            Expect(!refused.Success && refused.Layers.Last().Name == "tcp", "a closed port fails the tcp step: " + Describe(refused));
                        }),

                    new TestCaseDescriptor("Crawlers", "Catalog_ListsBuiltInTypes", "A server lists the built-in crawler types with their settings schemas; local folders are off until an administrator allows roots",
                        executeAsync: async ct =>
                        {
                            await using (TestServer server = await TestServer.CreateAsync(ct))
                            {
                                List<CrawlPlanTypeInfo> catalog = server.Crawlers.Catalog();
                                Expect(catalog.Select(c => c.Type).SequenceEqual(new[] { CrawlPlanTypeEnum.Web, CrawlPlanTypeEnum.Sitemap, CrawlPlanTypeEnum.S3, CrawlPlanTypeEnum.Cifs, CrawlPlanTypeEnum.Nfs, CrawlPlanTypeEnum.GitHub, CrawlPlanTypeEnum.AzureBlob, CrawlPlanTypeEnum.GoogleCloud }), "eight types (local folders stay off without allowed roots): " + String.Join(", ", catalog.Select(c => c.Type)));
                                Expect(catalog.All(c => c.Fields.Count > 0 && !String.IsNullOrEmpty(c.Description)), "each has a schema and a description");
                                Expect(catalog.First(c => c.Type == CrawlPlanTypeEnum.S3).Fields.Any(f => f.Name == "secretKey" && f.Kind == "secret"), "secrets are marked");
                            }
                        }),

                    new TestCaseDescriptor("Crawlers", "Web_ErrorPagesAndRedirectLoops", "Pages that return errors are not listed, a redirect loop ends without looping, and a redirected page is listed once at its final address",
                        executeAsync: async ct =>
                        {
                            using (StubWebSite site = new StubWebSite())
                            using (CrawlHttpClient http = AllowLoopback())
                            {
                                site.Html("/", "<html><body><a href=\"/ok\">ok</a> <a href=\"/broken\">broken</a> <a href=\"/loop1\">loop</a></body></html>");
                                site.Html("/ok", "<html><body>fine <a href=\"/old\">old</a></body></html>");
                                site.Redirect("/old", site.Url("/new"));
                                site.Html("/new", "<html><body>moved here</body></html>");
                                site.Set("/broken", new StubPage { Status = 500, ContentType = "text/plain", Body = Encoding.UTF8.GetBytes("boom") });
                                site.Redirect("/loop1", site.Url("/loop2"));
                                site.Redirect("/loop2", site.Url("/loop1"));
                                Task<List<CrawledObject>> crawl = ListAsync(new WebSiteCrawler(http), WebPlan(site, null), ct);
                                Task finished = await Task.WhenAny(crawl, Task.Delay(TimeSpan.FromSeconds(60), ct));
                                Expect(finished == crawl, "the crawl finishes");
                                List<string> keys = crawl.Result.Select(p => p.Key).ToList();
                                Expect(keys.Contains(site.Url("/ok")) && !keys.Contains(site.Url("/broken")), "error pages are not listed: " + String.Join(", ", keys));
                                Expect(!keys.Any(k => k.Contains("loop")), "redirect loops are not listed: " + String.Join(", ", keys));
                                Expect(keys.Contains(site.Url("/new")) && !keys.Contains(site.Url("/old")), "a redirected page is listed once, at the address its content came from: " + String.Join(", ", keys));
                                ResolvedContent moved = await new WebSiteCrawler(http).OpenAsync(WebPlan(site, null), site.Url("/new"), ct);
                                Expect(Encoding.UTF8.GetString(moved.Bytes).Contains("moved here"), "its content comes from the redirect target");
                            }
                        }),

                    new TestCaseDescriptor("Crawlers", "Sitemap_RobotsDiscoveryAndHostRule", "A robots.txt sitemap URL leads to its sitemaps, and URLs on another host are ignored",
                        executeAsync: async ct =>
                        {
                            using (StubWebSite site = new StubWebSite())
                            using (CrawlHttpClient http = AllowLoopback())
                            {
                                site.Set("/robots.txt", new StubPage { ContentType = "text/plain", Body = Encoding.UTF8.GetBytes("User-agent: *\nDisallow:\nSitemap: " + site.Url("/sitemap.xml") + "\n") });
                                site.Set("/sitemap.xml", Xml("<?xml version=\"1.0\"?><urlset xmlns=\"http://www.sitemaps.org/schemas/sitemap/0.9\">" +
                                    "<url><loc>" + site.Url("/a") + "</loc></url><url><loc>http://elsewhere.invalid/p</loc></url></urlset>"));
                                CrawlPlan plan = new CrawlPlan { Type = CrawlPlanTypeEnum.Sitemap, Sitemap = new SitemapCrawlSettings { SitemapUrls = new List<string> { site.Url("/robots.txt") } } };
                                List<string> keys = (await ListAsync(new SitemapCrawler(http), plan, ct)).Select(o => o.Key).ToList();
                                Expect(keys.Count == 1 && keys[0] == site.Url("/a"), "only the same-host URL: " + String.Join(", ", keys));
                            }
                        }),

                    new TestCaseDescriptor("Crawlers", "Sitemap_PrivateAddressBlocked", "A sitemap on a private address is refused unless allowed",
                        executeAsync: async ct =>
                        {
                            using (StubWebSite site = new StubWebSite())
                            using (CrawlHttpClient http = new CrawlHttpClient(new FetchSafetyPolicy(new FetchSafetySettings())))
                            {
                                SeedSitemaps(site);
                                CrawlPlan plan = new CrawlPlan { Type = CrawlPlanTypeEnum.Sitemap, Sitemap = new SitemapCrawlSettings { SitemapUrls = new List<string> { site.Url("/sitemap1.xml") } } };
                                bool blocked = false;
                                try { await ListAsync(new SitemapCrawler(http), plan, ct); } catch (FetchBlockedException) { blocked = true; }
                                Expect(blocked && site.Hits("/sitemap1.xml") == 0, "refused before any request");
                            }
                        }),

                    new TestCaseDescriptor("Crawlers", "Blob_SkipsSystemFiles", "Share and bucket listings skip system, lock, temporary, and hidden-folder files",
                        executeAsync: async ct =>
                        {
                            string root = TempDir();
                            try
                            {
                                foreach (string name in new[] { "kb/a.txt", "kb/Thumbs.db", "kb/~$draft.docx", "kb/.git/config", "kb/work.tmp", "kb/._a.txt" }) Write(root, name, "x");
                                CrawlPlan plan = new CrawlPlan { Type = CrawlPlanTypeEnum.S3, S3 = new S3CrawlSettings { Bucket = "test", Prefix = "kb" } };
                                List<string> keys = (await ListAsync(new DiskTestCrawler(root), plan, ct)).Select(o => o.Key.Replace('\\', '/')).ToList();
                                Expect(keys.Count == 1 && keys[0] == "kb/a.txt", "only the real file: " + String.Join(", ", keys));
                                Expect(!BlobCrawlerBase.IsSkippedName("docs/report.pdf") && BlobCrawlerBase.IsSkippedName("$RECYCLE.BIN/x.pdf") && BlobCrawlerBase.IsSkippedName("a/desktop.ini"), "skip rules");
                            }
                            finally
                            {
                                TryDelete(root);
                            }
                        }),

                    new TestCaseDescriptor("Crawlers", "ContainerHost_MapsLoopback", "Inside a container a loopback share host maps to host.docker.internal; outside it is unchanged",
                        executeAsync: ct =>
                        {
                            Expect(ContainerHost.Resolve("localhost", true) == "host.docker.internal" && ContainerHost.Resolve("127.0.0.1", true) == "host.docker.internal", "mapped in a container");
                            Expect(ContainerHost.Resolve("localhost", false) == "localhost" && ContainerHost.Resolve("files.corp", true) == "files.corp", "unchanged otherwise");
                            return Task.CompletedTask;
                        }),

                    LiveCase("S3_LiveServer", "Against an S3-compatible server (for example Less3) the S3 crawler passes its test, lists under a prefix with ETag versions, reads content, and reports bad keys", "PNEUMA_TEST_S3_ENDPOINT", async ct =>
                    {
                        S3CrawlSettings settings = new S3CrawlSettings
                        {
                            Endpoint = Env("PNEUMA_TEST_S3_ENDPOINT"),
                            UseSsl = Env("PNEUMA_TEST_S3_SSL") == "true",
                            Region = Env("PNEUMA_TEST_S3_REGION") ?? "us-west-1",
                            Bucket = Env("PNEUMA_TEST_S3_BUCKET") ?? "default",
                            AccessKey = Env("PNEUMA_TEST_S3_ACCESS_KEY") ?? "default",
                            SecretKey = Env("PNEUMA_TEST_S3_SECRET_KEY") ?? "default"
                        };
                        string prefix = "pneuma-crawl-test-" + Guid.NewGuid().ToString("N").Substring(0, 8) + "/";
                        string endpoint = settings.Endpoint!.Contains("://") ? settings.Endpoint : (settings.UseSsl ? "https://" : "http://") + settings.Endpoint;
                        if (!endpoint.EndsWith("/")) endpoint += "/";
                        Blobject.AmazonS3.AmazonS3BlobClient writer = new Blobject.AmazonS3.AmazonS3BlobClient(new Blobject.AmazonS3.AwsSettings(endpoint, settings.UseSsl, settings.AccessKey, settings.SecretKey, settings.Region, settings.Bucket, endpoint + "{bucket}/{key}"));
                        try
                        {
                            await writer.WriteAsync(prefix + "a.txt", "text/plain", Encoding.UTF8.GetBytes("Alpha document for the S3 crawler."), ct);
                            await writer.WriteAsync(prefix + "sub/b.md", "text/markdown", Encoding.UTF8.GetBytes("# Beta"), ct);
                            S3Crawler crawler = new S3Crawler(0);
                            settings.Prefix = prefix;
                            CrawlPlan plan = new CrawlPlan { Type = CrawlPlanTypeEnum.S3, S3 = settings };
                            ConnectivityResult test = await crawler.TestAsync(plan, ct);
                            Expect(test.Success, "test passes: " + Describe(test));
                            List<CrawledObject> listed = await ListAsync(crawler, plan, ct);
                            List<string> keys = listed.Select(o => o.Key).OrderBy(k => k, StringComparer.Ordinal).ToList();
                            Expect(keys.SequenceEqual(new[] { prefix + "a.txt", prefix + "sub/b.md" }), "listed under the prefix: " + String.Join(", ", keys));
                            Expect(listed.All(o => o.VersionToken != null), "versions present: " + String.Join(", ", listed.Select(o => o.VersionToken)));
                            Expect(listed.First(o => o.Key.EndsWith("a.txt")).Uri == "s3://" + settings.Bucket + "/" + prefix + "a.txt", "s3 URI");
                            ResolvedContent content = await crawler.OpenAsync(plan, prefix + "a.txt", ct);
                            Expect(Encoding.UTF8.GetString(content.Bytes).Contains("Alpha document"), "content read");
                            settings.IncludeSubfolders = false;
                            Expect((await ListAsync(crawler, plan, ct)).Count == 1, "sub-prefixes excluded");

                            string before = listed.First(o => o.Key.EndsWith("a.txt")).VersionToken!;
                            await writer.WriteAsync(prefix + "a.txt", "text/plain", Encoding.UTF8.GetBytes("Alpha document, changed."), ct);
                            string after = (await ListAsync(crawler, plan, ct)).First(o => o.Key.EndsWith("a.txt")).VersionToken!;
                            Expect(before != after, "a rewritten object gets a new version");

                            S3CrawlSettings bad = new S3CrawlSettings { Endpoint = settings.Endpoint, UseSsl = settings.UseSsl, Region = settings.Region, Bucket = settings.Bucket, AccessKey = "wrong", SecretKey = "wrong" };
                            ConnectivityResult denied = await crawler.TestAsync(new CrawlPlan { Type = CrawlPlanTypeEnum.S3, S3 = bad }, ct);
                            Expect(!denied.Success && denied.Layers.Last().Name == "root", "bad keys fail at the listing step: " + Describe(denied));
                        }
                        finally
                        {
                            foreach (string key in new[] { prefix + "a.txt", prefix + "sub/b.md" })
                            {
                                try { await writer.DeleteAsync(key, CancellationToken.None); } catch (Exception) { }
                            }
                            (writer as IDisposable)?.Dispose();
                        }
                    }),

                    new TestCaseDescriptor("Crawlers", "Cifs_OpenCifsServer", "The CIFS crawler signs in to an OpenCIFS share on a custom port, lists under a folder, skips system files, reads content, and reports a bad password at the auth step",
                        executeAsync: async ct =>
                        {
                            await using (ShareTestServers servers = await ShareTestServers.StartAsync(true, false, ct))
                            {
                                SeedShare(servers);
                                CifsCrawler crawler = new CifsCrawler(0);
                                CrawlPlan plan = CifsPlan(servers, null);
                                ConnectivityResult test = await crawler.TestAsync(plan, ct);
                                Expect(test.Success, "test passes: " + Describe(test));
                                List<CrawledObject> all = await ListAsync(crawler, plan, ct);
                                List<string> keys = all.Select(o => o.Key).OrderBy(k => k, StringComparer.Ordinal).ToList();
                                Expect(keys.SequenceEqual(new[] { "kb/guide.md", "kb/policies/leave.txt" }), "listed under the folder: " + String.Join(", ", keys));
                                Expect(all.All(o => o.VersionToken != null && o.VersionToken.Contains("modified:")), "versions from size and time");
                                Expect(all.First(o => o.Key == "kb/guide.md").ContentType == "text/markdown", "content type from the name");
                                Expect(all.First(o => o.Key == "kb/guide.md").Uri == "smb://127.0.0.1:" + servers.CifsPort + "/docs/kb/guide.md", "smb URI with the port");
                                ResolvedContent content = await crawler.OpenAsync(plan, "kb/policies/leave.txt", ct);
                                Expect(Encoding.UTF8.GetString(content.Bytes).Contains("Leave policy"), "content read");
                                Expect((await ListAsync(crawler, CifsPlan(servers, c => c.IncludeSubfolders = false), ct)).Count == 1, "subfolders excluded");
                                bool tooLarge = false;
                                try { await new CifsCrawler(4).OpenAsync(plan, "kb/guide.md", ct); } catch (ContentTooLargeException) { tooLarge = true; }
                                Expect(tooLarge, "the download limit applies");

                                ConnectivityResult denied = await crawler.TestAsync(CifsPlan(servers, c => c.Password = "wrong"), ct);
                                Expect(!denied.Success && denied.Layers.Last().Name == "auth", "a bad password fails the auth step: " + Describe(denied));
                                ConnectivityResult noShare = await crawler.TestAsync(CifsPlan(servers, c => c.Share = "missing"), ct);
                                Expect(!noShare.Success && noShare.Layers.Last().Name == "auth", "a missing share fails the auth step: " + Describe(noShare));
                            }
                        }),

                    new TestCaseDescriptor("Crawlers", "Nfs_OpenNfsServer", "The NFS crawler mounts an OpenNFS export on custom ports, lists under a folder, skips system files, reads content, and reports a missing export at the auth step",
                        executeAsync: async ct =>
                        {
                            await using (ShareTestServers servers = await ShareTestServers.StartAsync(false, true, ct))
                            {
                                SeedShare(servers);
                                NfsCrawler crawler = new NfsCrawler(0);
                                CrawlPlan plan = NfsPlan(servers, null);
                                ConnectivityResult test = await crawler.TestAsync(plan, ct);
                                Expect(test.Success, "test passes: " + Describe(test));
                                List<CrawledObject> all = await ListAsync(crawler, plan, ct);
                                List<string> keys = all.Select(o => o.Key).OrderBy(k => k, StringComparer.Ordinal).ToList();
                                Expect(keys.SequenceEqual(new[] { "kb/guide.md", "kb/policies/leave.txt" }), "listed under the folder: " + String.Join(", ", keys));
                                Expect(all.All(o => o.VersionToken != null && o.VersionToken.Contains("modified:")), "versions from size and time, as before the move to Blobject");
                                ResolvedContent content = await crawler.OpenAsync(plan, "kb/guide.md", ct);
                                Expect(Encoding.UTF8.GetString(content.Bytes).Contains("Guide"), "content read");
                                Expect((await ListAsync(crawler, NfsPlan(servers, n => n.IncludeSubfolders = false), ct)).Count == 1, "subfolders excluded");

                                ConnectivityResult missing = await crawler.TestAsync(NfsPlan(servers, n => n.Export = "/nope"), ct);
                                Expect(!missing.Success && missing.Layers.Last().Name == "auth", "a missing export fails the auth step: " + Describe(missing));
                            }
                        }),

                    new TestCaseDescriptor("Crawlers", "Cifs_EndToEndSync", "A CIFS plan run end to end adds files, re-ingests a changed file, and deletes a removed one when deletions are on",
                        executeAsync: async ct =>
                        {
                            await using (ShareTestServers servers = await ShareTestServers.StartAsync(true, false, ct))
                            {
                                SeedShare(servers);
                                await using (CrawlTestRig rig = await CrawlTestRig.CreateAsync(ct))
                                {
                                    rig.Crawlers.Register(new CifsCrawler(0));
                                    CrawlPlan plan = await rig.CreatePlanAsync(p =>
                                    {
                                        p.Type = CrawlPlanTypeEnum.Cifs;
                                        p.Web = null;
                                        p.Cifs = CifsPlan(servers, null).Cifs;
                                        p.ProcessDeletions = true;
                                        p.MaxDeletionFraction = 0.5;
                                    }, ct);
                                    CrawlOperation first = await rig.RunToEndAsync(plan.Id, ct);
                                    Expect(first.Added == 2 && first.Status == CrawlOperationStatusEnum.Succeeded, "2 added, got " + first.Added + " " + first.Status + " " + first.Error);

                                    Thread.Sleep(1100);
                                    servers.Write("kb/guide.md", "# Guide\n\nThe guide now covers crawling shares over SMB.");
                                    servers.Delete("kb/policies/leave.txt");
                                    servers.Write("kb/new.txt", "A new file about schedules.");
                                    CrawlOperation second = await rig.RunToEndAsync(plan.Id, ct);
                                    Expect(second.Updated == 1 && second.Added == 1 && second.Deleted == 1, "1 updated, 1 added, 1 deleted; got " + second.Updated + "/" + second.Added + "/" + second.Deleted + " " + second.Error);
                                }
                            }
                        }),

                    new TestCaseDescriptor("Crawlers", "GitHub_ListsReadsAndReportsErrors", "The GitHub crawler lists a repository through the contents API with its token, filters by folder, links to the file on GitHub, reads content, and reports a missing repository",
                        executeAsync: async ct =>
                        {
                            using (StubWebSite site = new StubWebSite())
                            {
                                SeedGitHub(site);
                                site.RequiredAuthorization = "token gh-secret";
                                GitHubRepositoryCrawler crawler = GitHubCrawler(site);
                                CrawlPlan plan = GitHubPlan("https://github.com/acme/handbook", null, "gh-secret");
                                ConnectivityResult test = await crawler.TestAsync(plan, ct);
                                Expect(test.Success, "test passes: " + Describe(test));
                                List<CrawledObject> all = await ListAsync(crawler, plan, ct);
                                List<string> keys = all.Select(o => o.Key).OrderBy(k => k, StringComparer.Ordinal).ToList();
                                Expect(keys.SequenceEqual(new[] { "https://raw.githubusercontent.com/acme/handbook/main/README.md", "https://raw.githubusercontent.com/acme/handbook/main/docs/setup.md" }), "files listed (system files skipped): " + String.Join(", ", keys));
                                CrawledObject setup = all.First(o => o.Key.EndsWith("setup.md"));
                                Expect(setup.Uri == "https://github.com/acme/handbook/blob/main/docs/setup.md" && setup.Title == "setup.md" && setup.ContentType == "text/markdown" && setup.VersionToken == null, "GitHub URI, title, and type");
                                Expect((await ListAsync(crawler, GitHubPlan("https://github.com/acme/handbook.git", "docs", "gh-secret"), ct)).Count == 1, "folder filter");
                                ResolvedContent content = await crawler.OpenAsync(plan, setup.Key, ct);
                                Expect(Encoding.UTF8.GetString(content.Bytes).Contains("Install the tools"), "content read");

                                ConnectivityResult noToken = await crawler.TestAsync(GitHubPlan("https://github.com/acme/handbook", null, null), ct);
                                Expect(!noToken.Success && noToken.Layers.Last().Name == "root", "without the token the listing fails: " + Describe(noToken));
                                ConnectivityResult missing = await crawler.TestAsync(GitHubPlan("https://github.com/acme/nope", null, "gh-secret"), ct);
                                Expect(!missing.Success && missing.Layers.Last().Message.Contains("not found", StringComparison.OrdinalIgnoreCase), "a missing repository is named: " + Describe(missing));
                                ConnectivityResult bad = await crawler.TestAsync(GitHubPlan("https://gitlab.com/acme/handbook", null, null), ct);
                                Expect(!bad.Success && bad.Layers[0].Name == "settings" && crawler.CheckSettings(GitHubPlan("not a url", null, null)).Count == 1, "a non-GitHub URL is a settings problem");
                            }
                        }),

                    new TestCaseDescriptor("Crawlers", "GitHub_EndToEndSync", "A GitHub plan run end to end adds every file, and a second run re-reads them (the library reports no SHAs) without duplicating links",
                        executeAsync: async ct =>
                        {
                            using (StubWebSite site = new StubWebSite())
                            {
                                SeedGitHub(site);
                                await using (CrawlTestRig rig = await CrawlTestRig.CreateAsync(ct))
                                {
                                    rig.Crawlers.Register(GitHubCrawler(site));
                                    CrawlPlan plan = await rig.CreatePlanAsync(p => { p.Type = CrawlPlanTypeEnum.GitHub; p.Web = null; p.GitHub = GitHubPlan("https://github.com/acme/handbook", null, null).GitHub; }, ct);
                                    CrawlOperation first = await rig.RunToEndAsync(plan.Id, ct);
                                    Expect(first.Added == 2 && first.Status == CrawlOperationStatusEnum.Succeeded, "2 added, got " + first.Added + " " + first.Status + " " + first.Error);
                                    CrawlOperation second = await rig.RunToEndAsync(plan.Id, ct);
                                    Expect(second.Updated == 2 && second.Added == 0, "both re-read, got " + second.Updated + " updated " + second.Added + " added");
                                    Expect((await rig.H.Db.SubjectLinks.EnumerateByCrawlPlanAsync(rig.H.TenantId, plan.Id, ct)).Count == 2, "still 2 links");
                                }
                            }
                        }),

                    new TestCaseDescriptor("Crawlers", "LocalFolder_AllowedRootsOnly", "Local folder plans read only folders under the allowed roots; others, relative paths, and a server without roots are refused",
                        executeAsync: async ct =>
                        {
                            string root = TempDir();
                            string outside = TempDir();
                            try
                            {
                                Write(root, "handbook/a.txt", "Alpha");
                                Write(root, "handbook/sub/b.md", "# Beta");
                                Write(root, "handbook/desktop.ini", "junk");
                                LocalFolderCrawler crawler = new LocalFolderCrawler(new[] { root }, 0);
                                CrawlPlan plan = LocalPlan(Path.Combine(root, "handbook"));
                                ConnectivityResult test = await crawler.TestAsync(plan, ct);
                                Expect(test.Success && !test.Layers.Any(l => l.Name == "dns" || l.Name == "tcp"), "test passes without network steps: " + Describe(test));
                                List<string> keys = (await ListAsync(crawler, plan, ct)).Select(o => o.Key.Replace('\\', '/')).OrderBy(k => k).ToList();
                                Expect(keys.SequenceEqual(new[] { "a.txt", "sub/b.md" }), "files listed: " + String.Join(", ", keys));
                                ResolvedContent content = await crawler.OpenAsync(plan, "a.txt", ct);
                                Expect(Encoding.UTF8.GetString(content.Bytes) == "Alpha", "content read");
                                Expect(crawler.CheckSettings(plan).Count == 0, "an allowed folder passes");
                                Expect(crawler.CheckSettings(LocalPlan(outside)).Count == 1, "a folder outside the roots is refused");
                                Expect(crawler.CheckSettings(LocalPlan(Path.Combine(root, "..", Path.GetFileName(outside)))).Count == 1, "a path that climbs out of a root is refused");
                                Expect(crawler.CheckSettings(LocalPlan("handbook")).Count == 1, "a relative path is refused");
                                Expect(new LocalFolderCrawler(new string[0], 0).CheckSettings(plan).Count == 1, "no roots means no local folders");
                                bool enumerationRefused = false;
                                try { await ListAsync(crawler, LocalPlan(outside), ct); } catch (ArgumentException) { enumerationRefused = true; }
                                Expect(enumerationRefused, "enumeration refuses a folder outside the roots too");

                                await using (CrawlTestRig rig = await CrawlTestRig.CreateAsync(ct))
                                {
                                    rig.Crawlers.Register(crawler);
                                    bool saveRefused = false;
                                    try { await rig.CreatePlanAsync(p => { p.Type = CrawlPlanTypeEnum.LocalFolder; p.Web = null; p.LocalFolder = LocalPlan(outside).LocalFolder; }, ct); } catch (InvalidOperationException) { saveRefused = true; }
                                    Expect(saveRefused, "saving a plan outside the roots is refused");
                                    CrawlPlan saved = await rig.CreatePlanAsync(p => { p.Type = CrawlPlanTypeEnum.LocalFolder; p.Web = null; p.LocalFolder = plan.LocalFolder; }, ct);
                                    CrawlOperation op = await rig.RunToEndAsync(saved.Id, ct);
                                    Expect(op.Added == 2 && op.Status == CrawlOperationStatusEnum.Succeeded, "2 added end to end, got " + op.Added + " " + op.Error);
                                }
                            }
                            finally
                            {
                                TryDelete(root);
                                TryDelete(outside);
                            }
                        }),

                    new TestCaseDescriptor("Crawlers", "Cloud_TestStepsReportProblems", "Azure Blob and Google Cloud tests report missing settings and unresolvable endpoints at the right step",
                        executeAsync: async ct =>
                        {
                            AzureBlobCrawler azure = new AzureBlobCrawler(0);
                            GoogleCloudCrawler gcs = new GoogleCloudCrawler(0);
                            ConnectivityResult azureMissing = await azure.TestAsync(new CrawlPlan { Type = CrawlPlanTypeEnum.AzureBlob, AzureBlob = new AzureBlobCrawlSettings { AccountName = "acct" } }, ct);
                            ConnectivityResult gcsMissing = await gcs.TestAsync(new CrawlPlan { Type = CrawlPlanTypeEnum.GoogleCloud, GoogleCloud = new GoogleCloudCrawlSettings { ProjectId = "p", Bucket = "b", JsonCredentials = "not json" } }, ct);
                            foreach (ConnectivityResult r in new[] { azureMissing, gcsMissing })
                                Expect(!r.Success && r.Layers.Count == 1 && r.Layers[0].Name == "settings", "settings step fails: " + Describe(r));
                            ConnectivityResult azureDns = await azure.TestAsync(new CrawlPlan { Type = CrawlPlanTypeEnum.AzureBlob, AzureBlob = new AzureBlobCrawlSettings { AccountName = "acct", Container = "c", AccessKey = "a2V5", Endpoint = "http://no-such-host.invalid:10000/acct" } }, ct);
                            ConnectivityResult gcsDns = await gcs.TestAsync(new CrawlPlan { Type = CrawlPlanTypeEnum.GoogleCloud, GoogleCloud = new GoogleCloudCrawlSettings { ProjectId = "p", Bucket = "b", JsonCredentials = "{}", Endpoint = "http://no-such-host.invalid:4443/" } }, ct);
                            foreach (ConnectivityResult r in new[] { azureDns, gcsDns })
                                Expect(!r.Success && r.Layers.Last().Name == "dns", "dns step fails: " + Describe(r));
                        }),

                    LiveCase("Azure_LiveServer", "Against Azure Blob Storage or Azurite the Azure crawler passes its test, lists under a prefix with ETag versions, and reads content", "PNEUMA_TEST_AZURE_ENDPOINT", async ct =>
                    {
                        AzureBlobCrawlSettings settings = new AzureBlobCrawlSettings
                        {
                            Endpoint = Env("PNEUMA_TEST_AZURE_ENDPOINT"),
                            AccountName = Env("PNEUMA_TEST_AZURE_ACCOUNT") ?? "devstoreaccount1",
                            AccessKey = Env("PNEUMA_TEST_AZURE_KEY") ?? "Eby8vdM02xNOcqFlqUwJPLlmEtlCDXJ1OUzFT50uSRZ6IFsuFq2UVErCz4I6tq/K1SZFPTOtr/KBHBeksoGMGw==",
                            Container = Env("PNEUMA_TEST_AZURE_CONTAINER") ?? "pneuma-crawl-test",
                            Prefix = "run-" + Guid.NewGuid().ToString("N").Substring(0, 8) + "/"
                        };
                        await ExpectLiveBucketAsync(new AzureBlobCrawler(0), new CrawlPlan { Type = CrawlPlanTypeEnum.AzureBlob, AzureBlob = settings }, settings.Prefix!, ct);
                    }),

                    LiveCase("GoogleCloud_LiveServer", "Against Google Cloud Storage or an emulator the Google Cloud crawler passes its test, lists under a prefix, and reads content", "PNEUMA_TEST_GCS_ENDPOINT", async ct =>
                    {
                        GoogleCloudCrawlSettings settings = new GoogleCloudCrawlSettings
                        {
                            Endpoint = Env("PNEUMA_TEST_GCS_ENDPOINT"),
                            ProjectId = Env("PNEUMA_TEST_GCS_PROJECT") ?? "test-project",
                            Bucket = Env("PNEUMA_TEST_GCS_BUCKET") ?? "pneuma-crawl-test",
                            JsonCredentials = File.ReadAllText(Env("PNEUMA_TEST_GCS_KEY_FILE") ?? throw new Exception("Set PNEUMA_TEST_GCS_KEY_FILE to a service account key file.")),
                            Prefix = "run-" + Guid.NewGuid().ToString("N").Substring(0, 8) + "/"
                        };
                        await ExpectLiveBucketAsync(new GoogleCloudCrawler(0), new CrawlPlan { Type = CrawlPlanTypeEnum.GoogleCloud, GoogleCloud = settings }, settings.Prefix!, ct);
                    }),

                    new TestCaseDescriptor("Crawlers", "Url_Normalization", "Crawl keys drop fragments, default ports, and listed query parameters, and lower-case the host",
                        executeAsync: ct =>
                        {
                            Expect(CrawlUrl.Normalize("HTTPS://Docs.Example.com:443/a?utm_source=x&id=1#top", new[] { "utm_*" }) == "https://docs.example.com/a?id=1", "normalized");
                            Expect(CrawlUrl.Normalize("http://a.example:8080/b?fbclid=1", new[] { "fbclid" }) == "http://a.example:8080/b", "parameter dropped");
                            Expect(CrawlUrl.Normalize("mailto:someone@example.com", null) == null && CrawlUrl.Normalize("/relative", null) == null, "non-http rejected");
                            return Task.CompletedTask;
                        })
                });
        }

        // A case against a real storage server, skipped unless the named environment variable is set.
        private static TestCaseDescriptor LiveCase(string caseId, string name, string gate, Func<CancellationToken, Task> body)
        {
            bool skip = String.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(gate));
            return new TestCaseDescriptor("Crawlers", caseId, name, body, null, skip, skip ? "Set " + gate + " to run against a live server." : null);
        }

        private static async Task ExpectLiveAsync(ICrawler crawler, CrawlPlan plan, CancellationToken ct)
        {
            ConnectivityResult test = await crawler.TestAsync(plan, ct);
            Expect(test.Success, "connectivity test passes: " + Describe(test));
            List<CrawledObject> listed = await ListAsync(crawler, plan, ct);
            if (listed.Count > 0)
            {
                ResolvedContent first = await crawler.OpenAsync(plan, listed[0].Key, ct);
                Expect(first.Bytes.Length == listed[0].SizeBytes || listed[0].SizeBytes == 0, "the first object reads in full");
            }
        }

        private static string? Env(string name)
        {
            string? value = Environment.GetEnvironmentVariable(name);
            return String.IsNullOrWhiteSpace(value) ? null : value;
        }

        private static void SeedShare(ShareTestServers servers)
        {
            servers.Write("kb/guide.md", "# Guide\n\nHow the knowledge base is organized.");
            servers.Write("kb/policies/leave.txt", "Leave policy: request leave two weeks ahead.");
            servers.Write("kb/Thumbs.db", "junk");
            servers.Write("other/ignored.txt", "Outside the crawled folder.");
        }

        private static CrawlPlan CifsPlan(ShareTestServers servers, Action<CifsCrawlSettings>? configure)
        {
            CifsCrawlSettings cifs = new CifsCrawlSettings { Host = "127.0.0.1", Port = servers.CifsPort, Share = servers.CifsShare, Path = "kb", Domain = "WORKGROUP", Username = servers.CifsUser, Password = servers.CifsPassword };
            configure?.Invoke(cifs);
            return new CrawlPlan { Type = CrawlPlanTypeEnum.Cifs, Cifs = cifs };
        }

        private static CrawlPlan NfsPlan(ShareTestServers servers, Action<NfsCrawlSettings>? configure)
        {
            NfsCrawlSettings nfs = new NfsCrawlSettings { Host = "127.0.0.1", Port = servers.NfsPort, MountPort = servers.MountPort, Export = servers.NfsExport, Path = "kb" };
            configure?.Invoke(nfs);
            return new CrawlPlan { Type = CrawlPlanTypeEnum.Nfs, Nfs = nfs };
        }

        private static GitHubRepositoryCrawler GitHubCrawler(StubWebSite site)
        {
            Dictionary<string, string> prefixes = new Dictionary<string, string> { { "api.github.com", "/api" }, { "raw.githubusercontent.com", "/raw" } };
            return new GitHubRepositoryCrawler(() => new HostRewriteHandler(site.BaseUrl, prefixes), 0);
        }

        private static CrawlPlan GitHubPlan(string url, string? path, string? token)
        {
            return new CrawlPlan { Type = CrawlPlanTypeEnum.GitHub, GitHub = new GitHubCrawlSettings { RepositoryUrl = url, Path = path, Token = token } };
        }

        private static void SeedGitHub(StubWebSite site)
        {
            string raw = "https://raw.githubusercontent.com/acme/handbook/main/";
            site.Set("/api/repos/acme/handbook/contents/", Json("[" +
                "{\"name\":\"README.md\",\"path\":\"README.md\",\"type\":\"file\",\"download_url\":\"" + raw + "README.md\"}," +
                "{\"name\":\".DS_Store\",\"path\":\".DS_Store\",\"type\":\"file\",\"download_url\":\"" + raw + ".DS_Store\"}," +
                "{\"name\":\"docs\",\"path\":\"docs\",\"type\":\"dir\",\"download_url\":null}]"));
            site.Set("/api/repos/acme/handbook/contents/docs", Json("[{\"name\":\"setup.md\",\"path\":\"docs/setup.md\",\"type\":\"file\",\"download_url\":\"" + raw + "docs/setup.md\"}]"));
            site.Set("/raw/acme/handbook/main/README.md", new StubPage { ContentType = "text/plain", Body = Encoding.UTF8.GetBytes("# Handbook\n\nThe team handbook.") });
            site.Set("/raw/acme/handbook/main/docs/setup.md", new StubPage { ContentType = "text/plain", Body = Encoding.UTF8.GetBytes("# Setup\n\nInstall the tools before your first day.") });
        }

        private static StubPage Json(string json)
        {
            return new StubPage { ContentType = "application/json", Body = Encoding.UTF8.GetBytes(json) };
        }

        private static CrawlPlan LocalPlan(string folder)
        {
            return new CrawlPlan { Type = CrawlPlanTypeEnum.LocalFolder, LocalFolder = new LocalFolderCrawlSettings { Folder = folder } };
        }

        // Upload two objects through the crawler's own storage client, then test, list, and read them.
        private static async Task ExpectLiveBucketAsync(BlobCrawlerBase crawler, CrawlPlan plan, string prefix, CancellationToken ct)
        {
            Blobject.Core.BlobClientBase writer = LiveWriter(plan);
            try
            {
                await writer.WriteAsync(prefix + "a.txt", "text/plain", Encoding.UTF8.GetBytes("Alpha document for the bucket crawler."), ct);
                await writer.WriteAsync(prefix + "sub/b.md", "text/markdown", Encoding.UTF8.GetBytes("# Beta"), ct);
                ConnectivityResult test = await crawler.TestAsync(plan, ct);
                Expect(test.Success, "test passes: " + Describe(test));
                List<CrawledObject> listed = await ListAsync(crawler, plan, ct);
                List<string> keys = listed.Select(o => o.Key).OrderBy(k => k, StringComparer.Ordinal).ToList();
                Expect(keys.SequenceEqual(new[] { prefix + "a.txt", prefix + "sub/b.md" }), "listed under the prefix: " + String.Join(", ", keys));
                Expect(listed.All(o => o.VersionToken != null), "versions present");
                ResolvedContent content = await crawler.OpenAsync(plan, prefix + "a.txt", ct);
                Expect(Encoding.UTF8.GetString(content.Bytes).Contains("Alpha document"), "content read");
            }
            finally
            {
                foreach (string key in new[] { prefix + "a.txt", prefix + "sub/b.md" })
                {
                    try { await writer.DeleteAsync(key, CancellationToken.None); } catch (Exception) { }
                }
                (writer as IDisposable)?.Dispose();
            }
        }

        private static Blobject.Core.BlobClientBase LiveWriter(CrawlPlan plan)
        {
            if (plan.AzureBlob != null)
            {
                AzureBlobCrawlSettings a = plan.AzureBlob;
                string endpoint = a.Endpoint!.EndsWith("/") ? a.Endpoint : a.Endpoint + "/";
                string connection = "DefaultEndpointsProtocol=" + new Uri(endpoint).Scheme + ";AccountName=" + a.AccountName + ";AccountKey=" + a.AccessKey + ";BlobEndpoint=" + endpoint.TrimEnd('/') + ";";
                new Azure.Storage.Blobs.BlobContainerClient(connection, a.Container).CreateIfNotExists();
                return new Blobject.AzureBlob.AzureBlobClient(new Blobject.AzureBlob.AzureBlobSettings(a.AccountName, a.AccessKey!, endpoint, a.Container));
            }
            GoogleCloudCrawlSettings g = plan.GoogleCloud!;
            return new Blobject.GoogleCloud.GcpBlobClient(new Blobject.GoogleCloud.GcpBlobSettings(g.ProjectId, g.Bucket, g.JsonCredentials!, g.Endpoint!));
        }

        private static CrawlHttpClient AllowLoopback()
        {
            FetchSafetySettings settings = new FetchSafetySettings();
            settings.AllowedPrivateHosts = new List<string> { "127.0.0.1" };
            return new CrawlHttpClient(new FetchSafetyPolicy(settings));
        }

        private static CrawlPlan WebPlan(StubWebSite site, Action<WebCrawlSettings>? configure)
        {
            WebCrawlSettings web = new WebCrawlSettings { StartUrls = new List<string> { site.Url("/") }, CrawlDelayMs = 0, UseSitemap = false };
            configure?.Invoke(web);
            return new CrawlPlan { Type = CrawlPlanTypeEnum.Web, Web = web };
        }

        private static void SeedSite(StubWebSite site)
        {
            site.Html("/", "<html><body><h1>Home</h1><a href=\"/a\">A</a> <a href=\"/b?utm_source=news\">B</a> <a href=\"http://example.invalid/x\">Elsewhere</a></body></html>");
            site.Html("/a", "<html><body><h1>Page A</h1><p>Page A links on to C.</p><a href=\"/c\">C</a></body></html>");
            site.Html("/b", "<html><body><h1>Page B</h1><p>Page B is short.</p></body></html>");
            site.Html("/c", "<html><body><h1>Page C</h1><p>Page C is the deepest page.</p></body></html>");
        }

        private static void SeedSitemaps(StubWebSite site)
        {
            site.Set("/sitemap_index.xml", Xml("<?xml version=\"1.0\"?><sitemapindex xmlns=\"http://www.sitemaps.org/schemas/sitemap/0.9\">" +
                "<sitemap><loc>" + site.Url("/sitemap1.xml") + "</loc></sitemap><sitemap><loc>" + site.Url("/sitemap2.xml.gz") + "</loc></sitemap></sitemapindex>"));
            site.Set("/sitemap1.xml", Xml("<?xml version=\"1.0\"?><urlset xmlns=\"http://www.sitemaps.org/schemas/sitemap/0.9\">" +
                "<url><loc>" + site.Url("/a") + "</loc><lastmod>2026-09-01</lastmod></url><url><loc>" + site.Url("/b") + "</loc><lastmod>2026-09-02T10:00:00Z</lastmod></url></urlset>"));
            byte[] plain = Encoding.UTF8.GetBytes("<?xml version=\"1.0\"?><urlset xmlns=\"http://www.sitemaps.org/schemas/sitemap/0.9\"><url><loc>" + site.Url("/c") + "</loc></url></urlset>");
            using (MemoryStream output = new MemoryStream())
            {
                using (GZipStream gzip = new GZipStream(output, CompressionLevel.Fastest, true)) gzip.Write(plain, 0, plain.Length);
                site.Set("/sitemap2.xml.gz", new StubPage { ContentType = "application/gzip", Body = output.ToArray() });
            }
            site.Html("/a", "<html><body><h1>Page A</h1></body></html>");
        }

        private static StubPage Xml(string xml)
        {
            return new StubPage { ContentType = "application/xml", Body = Encoding.UTF8.GetBytes(xml) };
        }

        private static async Task<List<CrawledObject>> ListAsync(ICrawler crawler, CrawlPlan plan, CancellationToken ct)
        {
            List<CrawledObject> result = new List<CrawledObject>();
            await foreach (CrawledObject obj in crawler.EnumerateAsync(plan, ct)) result.Add(obj);
            return result;
        }

        private static string TempDir()
        {
            string dir = Path.Combine(Path.GetTempPath(), "pneuma-crawl-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(dir);
            return dir;
        }

        private static void Write(string root, string relative, string content)
        {
            string path = Path.Combine(root, relative.Replace('/', Path.DirectorySeparatorChar));
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, content);
            File.SetLastWriteTimeUtc(path, DateTime.UtcNow);
        }

        private static void TryDelete(string dir)
        {
            try { Directory.Delete(dir, true); } catch (IOException) { } catch (UnauthorizedAccessException) { }
        }

        private static string Describe(ConnectivityResult result)
        {
            return String.Join(" | ", result.Layers.Select(l => l.Name + ":" + (l.Success ? "ok" : "fail") + " " + l.Message));
        }

        private static void Expect(bool condition, string message)
        {
            if (!condition) throw new Exception(message);
        }
    }
}
