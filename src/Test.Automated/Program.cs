using System;
using System.Threading.Tasks;
using Test.Shared;
using Touchstone.Cli;

// Test.Automated CLI. Database connection details for the four-provider contract suite may be supplied as
// arguments (they are mapped onto the PNEUMA_TEST_DB_* environment variables that TestDatabase reads, so the
// same configuration also applies when the suites are driven from Test.Xunit / Test.Nunit via those vars):
//
//   --type <sqlite|postgresql|mysql|sqlserver>   database provider (default sqlite)
//   --host <host>                                server host
//   --port <port>                                server port
//   --user <user>                                username
//   --pass <password>  (alias --password)        password
//   --schema <schema>                            schema (PostgreSQL / SQL Server)
//   --database <name>  (alias --dbname)          maintenance/base database the admin connection targets to
//                                                create the per-case isolated databases (defaults to the
//                                                provider's admin database: postgres/mysql/master)
//   --results <path>                             write the run results to <path>
//
// Example: dotnet run --project src/Test.Automated -- --type postgresql --host localhost --port 5432 \
//          --user pneuma --pass pneuma --database pneuma_test

string? resultsPath = null;

for (int i = 0; i + 1 < args.Length; i++)
{
    string value = args[i + 1];
    switch (args[i])
    {
        case "--results": resultsPath = value; break;
        case "--type": Environment.SetEnvironmentVariable("PNEUMA_TEST_DB_TYPE", value); break;
        case "--host": Environment.SetEnvironmentVariable("PNEUMA_TEST_DB_HOST", value); break;
        case "--port": Environment.SetEnvironmentVariable("PNEUMA_TEST_DB_PORT", value); break;
        case "--user": Environment.SetEnvironmentVariable("PNEUMA_TEST_DB_USER", value); break;
        case "--pass":
        case "--password": Environment.SetEnvironmentVariable("PNEUMA_TEST_DB_PASSWORD", value); break;
        case "--schema": Environment.SetEnvironmentVariable("PNEUMA_TEST_DB_SCHEMA", value); break;
        case "--database":
        case "--dbname": Environment.SetEnvironmentVariable("PNEUMA_TEST_DB_NAME", value); break;
        default: break;
    }
}

return await ConsoleRunner.RunAsync(PneumaSuites.All, resultsPath: resultsPath);
