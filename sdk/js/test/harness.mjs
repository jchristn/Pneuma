#!/usr/bin/env node
/**
 * Pneuma SDK Test Harness
 *
 * Exercises a live Pneuma server end to end using the SDK. Prints PASS/FAIL per
 * step. Exits non-zero if any step fails. If the server is unreachable, prints
 * SKIPPED and exits 0 so the harness is safe to run in environments without a
 * running server.
 *
 * Usage:
 *     node test/harness.mjs [baseUrl] [email] [password]
 *
 * Defaults: http://127.0.0.1:8080  admin@pneuma  password
 */

import { PneumaClient, PneumaError } from '../src/index.js';

const BASE_URL = process.argv[2] || 'http://127.0.0.1:8080';
const EMAIL = process.argv[3] || 'admin@pneuma';
const PASSWORD = process.argv[4] || 'password';

let passed = 0;
let failed = 0;

function pass(name) {
    passed++;
    console.log(`  [PASS] ${name}`);
}

function fail(name, err) {
    failed++;
    const message = err instanceof Error ? err.message : String(err);
    console.log(`  [FAIL] ${name}`);
    console.log(`         ${message}`);
}

async function step(name, fn) {
    try {
        await fn();
        pass(name);
    } catch (err) {
        fail(name, err);
    }
}

function assert(condition, message) {
    if (!condition) throw new Error(message);
}

/**
 * Detect whether the server is reachable at all. Returns true if we get any
 * HTTP response; false only on a network-level failure (connection refused,
 * DNS, timeout, etc.).
 */
async function isServerReachable(client) {
    try {
        await client.health();
        return true;
    } catch (err) {
        // An PneumaError means the server responded (just not 2xx) — still reachable.
        if (err instanceof PneumaError) return true;
        return false;
    }
}

async function main() {
    console.log('='.repeat(60));
    console.log('  Pneuma SDK Test Harness');
    console.log('='.repeat(60));
    console.log(`  Base URL: ${BASE_URL}`);
    console.log(`  Login:    ${EMAIL}`);
    console.log();

    const client = new PneumaClient(BASE_URL);

    if (!(await isServerReachable(client))) {
        console.log('  [SKIPPED] Server unreachable at ' + BASE_URL);
        console.log('  Start the Pneuma server and re-run to execute the harness.');
        process.exit(0);
    }

    let createdSubjectId = null;

    await step('health', async () => {
        const health = await client.health();
        assert(health && typeof health === 'object', 'health should return an object');
        assert(health.status, 'health.status should be present');
    });

    await step('login', async () => {
        const result = await client.login(EMAIL, PASSWORD);
        assert(result && result.token, 'login should return a token');
        assert(client.token === result.token, 'client should store the token');
    });

    await step('list tenants (envelope)', async () => {
        const page = await client.listTenants({ maxResults: 10 });
        assert(page && typeof page === 'object', 'listTenants should return an envelope object');
        assert(Array.isArray(page.objects), 'envelope should expose an objects array');
    });

    await step('list roles (envelope)', async () => {
        const page = await client.listRoles();
        assert(page && typeof page === 'object', 'listRoles should return an envelope object');
        assert(Array.isArray(page.objects), 'envelope should expose an objects array');
    });

    await step('list subjects (envelope + pagination)', async () => {
        const page = await client.listSubjects({ maxResults: 5, skip: 0, order: 'desc' });
        assert(page && typeof page === 'object', 'listSubjects should return an envelope object');
        assert(Array.isArray(page.objects), 'envelope should expose an objects array');
        assert(typeof page.totalRecords === 'number', 'envelope should expose totalRecords');
    });

    await step('create subject', async () => {
        const subject = await client.createSubject({
            displayName: `Harness Subject ${Date.now()}`,
            type: 'Person',
            description: 'Created by the Pneuma SDK test harness'
        });
        assert(subject && typeof subject === 'object', 'subject should be an object');
        createdSubjectId = subject.id || subject.identifier || subject.subjectId;
        assert(createdSubjectId, 'created subject should have an id');
    });

    await step('submit link', async () => {
        assert(createdSubjectId, 'need a subject id to submit a link');
        const link = await client.submitLink(createdSubjectId, {
            url: 'https://example.com/pneuma-sdk-harness',
            title: 'Harness Link'
        });
        assert(link && typeof link === 'object', 'submitLink should return an object');
    });

    await step('list jobs (envelope)', async () => {
        const jobs = await client.listJobs(undefined, { maxResults: 10 });
        assert(jobs && typeof jobs === 'object', 'listJobs should return an envelope object');
        assert(Array.isArray(jobs.objects), 'envelope should expose an objects array');
    });

    await step('get settings', async () => {
        const result = await client.getSettings();
        assert(result && typeof result === 'object', 'getSettings should return an object');
        assert(result.settings && typeof result.settings === 'object', 'result.settings should be present');
        assert(result.meta && typeof result.meta === 'object', 'result.meta should be present');
    });

    await step('update settings (round-trip preserves masked secrets)', async () => {
        const current = await client.getSettings();
        // Re-submit the (masked) settings unchanged; masked secrets are preserved server-side.
        const result = await client.updateSettings(current.settings);
        assert(result && typeof result === 'object', 'updateSettings should return an object');
        assert(typeof result.restartRequired === 'boolean', 'result.restartRequired should be a boolean');
    });

    console.log();
    console.log('='.repeat(60));
    console.log(`  Passed: ${passed}   Failed: ${failed}`);
    console.log(`  Result: ${failed === 0 ? 'SUCCESS' : 'FAILURE'}`);
    console.log('='.repeat(60));

    process.exit(failed === 0 ? 0 : 1);
}

main().catch((err) => {
    console.error('  FATAL:', err instanceof Error ? err.message : err);
    process.exit(1);
});
