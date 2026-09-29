namespace Test.Shared.Suites
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Net;
    using System.Net.Http;
    using System.Text;
    using System.Threading;
    using System.Threading.Tasks;
    using Pneuma.Core.Enums;
    using Pneuma.Core.Ingestion.Configuration;
    using Pneuma.Core.Ingestion.Enums;
    using Pneuma.Core.Ingestion.Models;
    using Pneuma.Core.Ingestion.Pipeline;
    using Pneuma.Core.Integrations.Implementations;
    using Pneuma.Core.Models;
    using Test.Shared.Support;
    using Touchstone.Core;

    /// <summary>
    /// Fetch safety: only http and https, no private or internal addresses unless allow-listed (checked at connect time,
    /// covering redirects and DNS rebinding), a streaming size cap, a per-host limit, and refusal at link submission.
    /// </summary>
    public static class FetchSafetySuite
    {
        /// <summary>Build the suite.</summary>
        /// <returns>Suite descriptor.</returns>
        public static TestSuiteDescriptor Build()
        {
            return new TestSuiteDescriptor(
                suiteId: "FetchSafety",
                displayName: "Fetch safety (SSRF guard, size cap, per-host limit)",
                cases: new List<TestCaseDescriptor>
                {
                    new TestCaseDescriptor("FetchSafety", "PrivateRanges_AreNotPublic", "Loopback, private, link-local, CGNAT, unique-local, and multicast addresses are not public",
                        executeAsync: ct =>
                        {
                            string[] blocked = { "127.0.0.1", "10.1.2.3", "172.16.0.1", "172.31.255.255", "192.168.1.1", "169.254.169.254", "100.64.0.1", "0.0.0.0", "224.0.0.1", "::1", "fc00::1", "fd00:ec2::254", "fe80::1", "::ffff:10.0.0.1" };
                            foreach (string text in blocked)
                            {
                                if (FetchSafetyPolicy.IsPublicAddress(IPAddress.Parse(text))) throw new Exception(text + " must not be public");
                            }

                            string[] allowed = { "93.184.216.34", "8.8.8.8", "172.32.0.1", "2606:4700::1111" };
                            foreach (string text in allowed)
                            {
                                if (!FetchSafetyPolicy.IsPublicAddress(IPAddress.Parse(text))) throw new Exception(text + " should be public");
                            }

                            return Task.CompletedTask;
                        }),

                    new TestCaseDescriptor("FetchSafety", "UrlShape_RejectsSchemesAndLiterals", "Submission-time checks reject other schemes, relative URLs, and private IP literals",
                        executeAsync: ct =>
                        {
                            FetchSafetyPolicy policy = new FetchSafetyPolicy();
                            Expect(policy.CheckUrlShape("https://example.com/a"), null);
                            Expect(policy.CheckUrlShape("ftp://example.com/a"), "scheme");
                            Expect(policy.CheckUrlShape("file:///etc/passwd"), "scheme");
                            Expect(policy.CheckUrlShape("/relative/path"), "invalid-url");
                            Expect(policy.CheckUrlShape("http://127.0.0.1:8600/"), "private-address");
                            Expect(policy.CheckUrlShape("http://localhost/"), "private-address");
                            Expect(policy.CheckUrlShape("http://[::1]/"), "private-address");
                            Expect(policy.CheckUrlShape("http://169.254.169.254/latest/meta-data"), "private-address");

                            FetchSafetyPolicy allowing = new FetchSafetyPolicy(new FetchSafetySettings { AllowedPrivateHosts = new List<string> { "127.0.0.1", "10.0.0.0/8" } });
                            Expect(allowing.CheckUrlShape("http://127.0.0.1:8600/"), null);
                            Expect(allowing.CheckUrlShape("http://10.9.8.7/"), null);
                            Expect(allowing.CheckUrlShape("http://192.168.0.1/"), "private-address");
                            return Task.CompletedTask;
                        }),

                    new TestCaseDescriptor("FetchSafety", "Resolution_BlocksPrivateAnswersAndHonorsAllowList", "A host resolving to a private address is blocked unless the host or its range is allow-listed",
                        executeAsync: async ct =>
                        {
                            FetchSafetyPolicy policy = new FetchSafetyPolicy(null, Resolver(new Dictionary<string, string> { { "public.test", "93.184.216.34" }, { "internal.test", "10.0.0.5" } }));
                            await policy.EnsureAllowedAsync("https://public.test/a", ct);
                            if (await policy.IsAllowedAsync("https://internal.test/a", ct)) throw new Exception("a host resolving to 10.0.0.5 must be blocked");

                            FetchSafetyPolicy byHost = new FetchSafetyPolicy(new FetchSafetySettings { AllowedPrivateHosts = new List<string> { "*.test" } }, Resolver(new Dictionary<string, string> { { "internal.test", "10.0.0.5" } }));
                            if (!await byHost.IsAllowedAsync("https://internal.test/a", ct)) throw new Exception("a wildcard host entry should allow the host");

                            FetchSafetyPolicy byRange = new FetchSafetyPolicy(new FetchSafetySettings { AllowedPrivateHosts = new List<string> { "10.0.0.0/24" } }, Resolver(new Dictionary<string, string> { { "internal.test", "10.0.0.5" }, { "other.test", "10.0.1.5" } }));
                            if (!await byRange.IsAllowedAsync("https://internal.test/a", ct)) throw new Exception("a CIDR entry should allow an address inside it");
                            if (await byRange.IsAllowedAsync("https://other.test/a", ct)) throw new Exception("a CIDR entry must not allow an address outside it");

                            FetchSafetyPolicy open = new FetchSafetyPolicy(new FetchSafetySettings { BlockPrivateAddresses = false }, Resolver(new Dictionary<string, string> { { "internal.test", "10.0.0.5" } }));
                            if (!await open.IsAllowedAsync("https://internal.test/a", ct)) throw new Exception("with blocking off, private addresses are allowed");
                        }),

                    new TestCaseDescriptor("FetchSafety", "Fetch_AllowListedLoopbackSucceeds", "A fetch to an allow-listed loopback site returns its bytes",
                        executeAsync: async ct =>
                        {
                            using (StubWebSite site = new StubWebSite())
                            {
                                site.Html("/a", "<html><body>hello</body></html>");
                                using (HttpContentFetcher fetcher = new HttpContentFetcher(null, new FetchSafetyPolicy(new FetchSafetySettings { AllowedPrivateHosts = new List<string> { "127.0.0.1" } })))
                                {
                                    byte[] body = await fetcher.FetchAsync(site.Url("/a"), ct);
                                    if (!Encoding.UTF8.GetString(body).Contains("hello")) throw new Exception("unexpected body");
                                }
                            }
                        }),

                    new TestCaseDescriptor("FetchSafety", "Fetch_DnsRebindingIsBlockedAtConnect", "A host name that resolves to loopback at connect time is refused even though the URL looks public",
                        executeAsync: async ct =>
                        {
                            using (StubWebSite site = new StubWebSite())
                            {
                                site.Html("/a", "secret");
                                FetchSafetyPolicy policy = new FetchSafetyPolicy(null, Resolver(new Dictionary<string, string> { { "rebind.test", "127.0.0.1" } }));
                                using (HttpContentFetcher fetcher = new HttpContentFetcher(null, policy))
                                {
                                    await ExpectBlockedAsync(() => fetcher.FetchAsync("http://rebind.test:" + site.Port + "/a", ct));
                                }

                                if (site.Hits("/a") != 0) throw new Exception("the blocked request must never reach the site");
                            }
                        }),

                    new TestCaseDescriptor("FetchSafety", "Fetch_RedirectToPrivateIsBlocked", "A redirect from an allowed host to a private host is refused",
                        executeAsync: async ct =>
                        {
                            using (StubWebSite site = new StubWebSite())
                            {
                                site.Redirect("/start", "http://internal.test:" + site.Port + "/secret");
                                site.Html("/secret", "secret");
                                FetchSafetyPolicy policy = new FetchSafetyPolicy(new FetchSafetySettings { AllowedPrivateHosts = new List<string> { "allowed.test" } },
                                    Resolver(new Dictionary<string, string> { { "allowed.test", "127.0.0.1" }, { "internal.test", "127.0.0.1" } }));
                                using (HttpContentFetcher fetcher = new HttpContentFetcher(null, policy))
                                {
                                    await ExpectBlockedAsync(() => fetcher.FetchAsync("http://allowed.test:" + site.Port + "/start", ct));
                                }

                                if (site.Hits("/start") != 1) throw new Exception("the allowed first hop should be fetched");
                                if (site.Hits("/secret") != 0) throw new Exception("the redirect target must never be fetched");
                            }
                        }),

                    new TestCaseDescriptor("FetchSafety", "Fetch_OversizeResponseIsAbandoned", "A response over MaxDownloadBytes fails with ContentTooLargeException",
                        executeAsync: async ct =>
                        {
                            using (StubWebSite site = new StubWebSite())
                            {
                                site.Set("/big", new StubPage { ContentType = "text/plain", Body = Encoding.UTF8.GetBytes(new string('x', 5000)) });
                                FetchSafetySettings settings = new FetchSafetySettings { AllowedPrivateHosts = new List<string> { "127.0.0.1" }, MaxDownloadBytes = 1024 };
                                using (HttpContentFetcher fetcher = new HttpContentFetcher(null, new FetchSafetyPolicy(settings)))
                                {
                                    try
                                    {
                                        await fetcher.FetchAsync(site.Url("/big"), ct);
                                        throw new Exception("an oversize response should fail");
                                    }
                                    catch (ContentTooLargeException)
                                    {
                                    }
                                }
                            }

                            if (IngestionFailureClassifier.Classify(new ContentTooLargeException(1, "x"), IngestionStageEnum.ContentRetrieval).Category != IngestionFailureCategoryEnum.TooLarge)
                                throw new Exception("an oversize fetch should be categorized TooLarge");
                        }),

                    new TestCaseDescriptor("FetchSafety", "Fetch_OtherSchemesAreRefused", "The fetcher refuses file and ftp URLs before any request",
                        executeAsync: async ct =>
                        {
                            using (HttpContentFetcher fetcher = new HttpContentFetcher())
                            {
                                await ExpectBlockedAsync(() => fetcher.FetchAsync("file:///etc/passwd", ct));
                                await ExpectBlockedAsync(() => fetcher.FetchAsync("ftp://example.com/a", ct));
                            }
                        }),

                    new TestCaseDescriptor("FetchSafety", "HostLimiter_BoundsConcurrencyPerHost", "The per-host limiter lets MaxPerHost requests through per host and no more",
                        executeAsync: async ct =>
                        {
                            HostRequestLimiter limiter = new HostRequestLimiter(2);
                            IDisposable a = await limiter.AcquireAsync("https://one.test/a", ct);
                            IDisposable b = await limiter.AcquireAsync("https://one.test/b", ct);
                            Task<IDisposable> third = limiter.AcquireAsync("https://one.test/c", ct);
                            IDisposable other = await limiter.AcquireAsync("https://two.test/a", ct);
                            await Task.Delay(100, ct);
                            if (third.IsCompleted) throw new Exception("a third request to the same host must wait");
                            a.Dispose();
                            a.Dispose();
                            IDisposable c = await third.WaitAsync(TimeSpan.FromSeconds(5), ct);
                            b.Dispose();
                            c.Dispose();
                            other.Dispose();
                        }),

                    new TestCaseDescriptor("FetchSafety", "LinkSubmit_RejectsUnsafeUrlsAndAudits", "Submitting a private or non-http URL returns 400 and records a FetchBlocked audit event",
                        executeAsync: async ct =>
                        {
                            await using (TestServer server = await TestServer.CreateAsync(ct))
                            {
                                string token = await ApiClientHelper.LoginAsync(server.BaseUrl, "admin@pneuma", "password", ct);
                                string subjectId = await ApiClientHelper.CreateConfiguredSubjectAsync(server.BaseUrl, token, ct);
                                string linksUrl = server.BaseUrl + "/v1.0/subjects/" + subjectId + "/links";

                                ApiResult loopback = await ApiClientHelper.CallAsync(HttpMethod.Post, linksUrl, token, "{\"url\":\"http://127.0.0.1:8600/\"}", ct);
                                if (loopback.StatusCode != 400) throw new Exception("a loopback URL should be 400, got " + loopback.StatusCode);
                                ApiResult ftp = await ApiClientHelper.CallAsync(HttpMethod.Post, linksUrl, token, "{\"url\":\"ftp://example.com/a\"}", ct);
                                if (ftp.StatusCode != 400) throw new Exception("an ftp URL should be 400, got " + ftp.StatusCode);
                                ApiResult bulk = await ApiClientHelper.CallAsync(HttpMethod.Post, linksUrl + "/bulk", token, "{\"urls\":[\"https://example.com/ok\",\"http://169.254.169.254/\"]}", ct);
                                if (bulk.StatusCode != 400) throw new Exception("a bulk batch with a metadata URL should be 400, got " + bulk.StatusCode);
                                ApiResult ok = await ApiClientHelper.CallAsync(HttpMethod.Post, linksUrl, token, "{\"url\":\"https://example.com/a\"}", ct);
                                if (ok.StatusCode != 201) throw new Exception("a public URL should be accepted, got " + ok.StatusCode + " " + ok.Body);

                                List<AuditRecord> audit = await server.Database.Audit.EnumerateAsync(null, 1000, ct);
                                int blocked = audit.Count(r => r.EventType == AuditEventTypeEnum.FetchBlocked);
                                if (blocked < 3) throw new Exception("expected at least 3 FetchBlocked audit records, got " + blocked);
                            }
                        })
                });
        }

        private static void Expect(string? actual, string? expected)
        {
            if (!String.Equals(actual, expected, StringComparison.Ordinal)) throw new Exception("expected " + (expected ?? "null") + ", got " + (actual ?? "null"));
        }

        private static Func<string, CancellationToken, Task<IPAddress[]>> Resolver(Dictionary<string, string> answers)
        {
            return (host, token) => Task.FromResult(answers.TryGetValue(host, out string? address) ? new[] { IPAddress.Parse(address) } : new[] { IPAddress.Parse("93.184.216.34") });
        }

        private static async Task ExpectBlockedAsync(Func<Task> action)
        {
            try
            {
                await action();
            }
            catch (FetchBlockedException)
            {
                return;
            }

            throw new Exception("expected the fetch to be blocked");
        }
    }
}
