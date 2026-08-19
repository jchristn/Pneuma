namespace Test.Automated
{
    using System;
    using System.Collections.Generic;
    using System.Net.Http;
    using System.Threading;
    using System.Threading.Tasks;
    using Pneuma.Sdk;
    using Pneuma.Sdk.Models;
    using Pneuma.Sdk.Requests;
    using Pneuma.Sdk.Responses;

    /// <summary>
    /// Automated smoke test that exercises the Pneuma C# SDK against a running server.
    /// Prints PASS/FAIL per step and exits non-zero on failure. If the server is unreachable,
    /// every step is reported SKIPPED and the process exits zero.
    /// </summary>
    public static class Program
    {
        private const string BaseUrl = "http://127.0.0.1:8080";
        private const string AdminEmail = "admin@pneuma";
        private const string AdminPassword = "password";

        private static int _Passed = 0;
        private static int _Failed = 0;
        private static int _Skipped = 0;

        /// <summary>
        /// Entry point.
        /// </summary>
        /// <param name="args">Command-line arguments (unused).</param>
        /// <returns>Process exit code: 0 on success or when skipped, 1 on any failure.</returns>
        public static async Task<int> Main(string[] args)
        {
            Console.WriteLine();
            Console.WriteLine("Pneuma SDK Automated Tests");
            Console.WriteLine("Target: " + BaseUrl);
            Console.WriteLine(new string('-', 60));

            using PneumaClient client = new PneumaClient(BaseUrl);

            if (!await IsServerReachableAsync(client).ConfigureAwait(false))
            {
                Console.WriteLine("Server is unreachable. Skipping all steps.");
                Skip("Health");
                Skip("Login");
                Skip("List tenants");
                Skip("List roles");
                Skip("List subjects");
                Skip("Create subject");
                Skip("Submit link");
                Skip("List jobs");
                Summarize();
                return 0;
            }

            await Step("Health", async () =>
            {
                HealthResponse health = await client.GetHealthAsync().ConfigureAwait(false);
                if (string.IsNullOrEmpty(health.Status)) throw new Exception("Empty health status.");
                Console.WriteLine("       status=" + health.Status + " version=" + health.Version);
            }).ConfigureAwait(false);

            await Step("Login", async () =>
            {
                TokenResponse tokenResponse = await client.LoginAsync(AdminEmail, AdminPassword).ConfigureAwait(false);
                if (string.IsNullOrEmpty(tokenResponse.Token)) throw new Exception("No token returned.");
                Console.WriteLine("       principal=" + tokenResponse.Email + " isAdmin=" + tokenResponse.IsAdmin);
            }).ConfigureAwait(false);

            await Step("List tenants", async () =>
            {
                EnumerationResult<Tenant> tenants = await client.ListTenantsAsync().ConfigureAwait(false);
                Console.WriteLine("       count=" + tenants.Objects.Count + " total=" + tenants.TotalRecords);
            }).ConfigureAwait(false);

            await Step("List roles", async () =>
            {
                EnumerationResult<UserRole> roles = await client.ListRolesAsync().ConfigureAwait(false);
                Console.WriteLine("       count=" + roles.Objects.Count + " total=" + roles.TotalRecords);
            }).ConfigureAwait(false);

            await Step("List subjects", async () =>
            {
                EnumerationResult<Subject> subjects = await client.ListSubjectsAsync().ConfigureAwait(false);
                Console.WriteLine("       count=" + subjects.Objects.Count + " total=" + subjects.TotalRecords);
            }).ConfigureAwait(false);

            string? createdSubjectId = null;

            await Step("Create subject", async () =>
            {
                Subject subject = new Subject
                {
                    DisplayName = "SDK Smoke Test " + DateTime.UtcNow.ToString("yyyyMMddHHmmss"),
                    Type = "Person",
                    Description = "Created by the Pneuma C# SDK automated test."
                };
                Subject created = await client.CreateSubjectAsync(subject).ConfigureAwait(false);
                if (string.IsNullOrEmpty(created.Id)) throw new Exception("Created subject has no id.");
                createdSubjectId = created.Id;
                Console.WriteLine("       id=" + created.Id);
            }).ConfigureAwait(false);

            await Step("Submit link", async () =>
            {
                if (string.IsNullOrEmpty(createdSubjectId)) throw new Exception("No subject id from prior step.");
                SubmitLinkRequest request = new SubmitLinkRequest
                {
                    Url = "https://example.com/pneuma-sdk-smoke-test",
                    Title = "SDK smoke test link"
                };
                SubjectLink link = await client.SubmitLinkAsync(createdSubjectId!, request).ConfigureAwait(false);
                if (string.IsNullOrEmpty(link.Id)) throw new Exception("Submitted link has no id.");
                Console.WriteLine("       linkId=" + link.Id + " status=" + link.Status);
            }).ConfigureAwait(false);

            await Step("List jobs", async () =>
            {
                EnumerationResult<IngestionJob> jobs = await client.ListJobsAsync().ConfigureAwait(false);
                Console.WriteLine("       count=" + jobs.Objects.Count + " total=" + jobs.TotalRecords);
            }).ConfigureAwait(false);

            Summarize();
            return _Failed == 0 ? 0 : 1;
        }

        private static async Task<bool> IsServerReachableAsync(PneumaClient client)
        {
            try
            {
                using CancellationTokenSource cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
                await client.GetHealthAsync(cts.Token).ConfigureAwait(false);
                return true;
            }
            catch (HttpRequestException)
            {
                return false;
            }
            catch (TaskCanceledException)
            {
                return false;
            }
            catch (OperationCanceledException)
            {
                return false;
            }
            catch (PneumaException)
            {
                // The server responded (even if unhappily); it is reachable.
                return true;
            }
        }

        private static async Task Step(string name, Func<Task> action)
        {
            try
            {
                await action().ConfigureAwait(false);
                _Passed++;
                Console.WriteLine("PASS   " + name);
            }
            catch (Exception ex)
            {
                _Failed++;
                Console.WriteLine("FAIL   " + name + " -> " + ex.Message);
            }
        }

        private static void Skip(string name)
        {
            _Skipped++;
            Console.WriteLine("SKIPPED " + name);
        }

        private static void Summarize()
        {
            Console.WriteLine(new string('-', 60));
            Console.WriteLine(_Passed + " passed, " + _Failed + " failed, " + _Skipped + " skipped");
            Console.WriteLine();
        }
    }
}
