#!/usr/bin/env node
/**
 * Offline tests for the file upload helpers. A loopback HTTP server records what the SDK sends, so no Pneuma server
 * is needed: uploadFile sends the raw bytes with the file name, labels, and tags in the query string; an error status
 * throws PneumaError; downloadLinkSource returns the bytes, type, and decoded file name.
 *
 * Usage: node test/upload.test.mjs
 */

import assert from 'node:assert/strict';
import http from 'node:http';
import { PneumaClient, PneumaError } from '../src/index.js';

const recorded = [];

const server = http.createServer((req, res) => {
    const chunks = [];
    req.on('data', (c) => chunks.push(c));
    req.on('end', () => {
        const url = new URL(req.url, 'http://127.0.0.1');
        if (req.method === 'GET' && url.pathname === '/v1.0/links/lnk_1/source') {
            res.writeHead(200, {
                'Content-Type': 'application/pdf',
                'Content-Disposition': "attachment; filename=\"Q3 r_sum_.pdf\"; filename*=UTF-8''Q3%20r%C3%A9sum%C3%A9.pdf"
            });
            res.end(Buffer.from('%PDF-1.4 bytes'));
            return;
        }
        if (req.method === 'GET') {
            res.writeHead(200, { 'Content-Type': 'application/json' });
            res.end(JSON.stringify({ maxUploadBytes: 1024, maxFileNameLength: 256, supportedTypes: [] }));
            return;
        }
        recorded.push({ path: url.pathname, query: url.searchParams, contentType: req.headers['content-type'], auth: req.headers.authorization, transferEncoding: req.headers['transfer-encoding'], contentLength: req.headers['content-length'], body: Buffer.concat(chunks) });
        if (url.pathname.includes('/sub_missing/')) {
            res.writeHead(404, { 'Content-Type': 'application/json' });
            res.end(JSON.stringify({ error: 'NotFound', message: 'Subject not found.' }));
            return;
        }
        res.writeHead(201, { 'Content-Type': 'application/json' });
        res.end(JSON.stringify({ index: 0, statusCode: 201, replaced: false, jobId: 'job_1', link: { id: 'lnk_1', fileName: url.searchParams.get('filename') } }));
    });
});

await new Promise((resolve) => server.listen(0, '127.0.0.1', resolve));
const client = new PneumaClient(`http://127.0.0.1:${server.address().port}`, 'tok');
let failures = 0;

async function step(name, fn) {
    try {
        await fn();
        console.log(`  [PASS] ${name}`);
    } catch (err) {
        failures++;
        console.log(`  [FAIL] ${name}: ${err.message}`);
    }
}

await step('uploadFile sends bytes and metadata', async () => {
    const result = await client.uploadFile('sub_1', Buffer.from('# Notes'), {
        filename: 'Release notes.md', contentType: 'text/markdown', labels: ['one', 'two,three'], tags: { team: 'finance' }, externalKey: 'notes'
    });
    assert.equal(result.link.fileName, 'Release notes.md');
    const sent = recorded.at(-1);
    assert.equal(sent.path, '/v1.0/subjects/sub_1/files');
    assert.equal(sent.query.get('filename'), 'Release notes.md');
    assert.deepEqual(sent.query.getAll('labels'), ['one', 'two,three']);
    assert.equal(sent.query.get('tag.team'), 'finance');
    assert.equal(sent.query.get('externalKey'), 'notes');
    assert.equal(sent.contentType, 'text/markdown');
    assert.equal(sent.auth, 'Bearer tok');
    assert.equal(sent.body.toString(), '# Notes');
});

await step('uploadFile takes a File name and type', async () => {
    const file = new File([new Uint8Array([0x25, 0x50, 0x44, 0x46])], 'deck.pdf', { type: 'application/pdf' });
    await client.uploadFile('sub_1', file);
    const sent = recorded.at(-1);
    assert.equal(sent.query.get('filename'), 'deck.pdf');
    assert.equal(sent.contentType, 'application/pdf');
    assert.deepEqual([...sent.body], [0x25, 0x50, 0x44, 0x46]);
});

await step('uploadFile defaults to octet-stream and requires a name', async () => {
    await client.uploadFile('sub_1', new Uint8Array([1, 2, 3]), { filename: 'data.bin' });
    assert.equal(recorded.at(-1).contentType, 'application/octet-stream');
    await assert.rejects(() => client.uploadFile('sub_1', new Uint8Array([1])), TypeError);
});

await step('uploadFile streams a Node Readable and a web ReadableStream chunked', async () => {
    const { Readable } = await import('node:stream');
    const parts = [Buffer.from('part one, '), Buffer.from('part two, '), Buffer.from('part three')];
    await client.uploadFile('sub_1', Readable.from(parts), { filename: 'streamed.txt', contentType: 'text/plain' });
    let sent = recorded.at(-1);
    assert.equal(sent.transferEncoding, 'chunked');
    assert.equal(sent.contentLength, undefined);
    assert.equal(sent.body.toString(), 'part one, part two, part three');

    const web = new ReadableStream({ start(c) { c.enqueue(new Uint8Array([7, 8])); c.enqueue(new Uint8Array([9])); c.close(); } });
    await client.uploadFile('sub_1', web, { filename: 'web.bin' });
    sent = recorded.at(-1);
    assert.equal(sent.transferEncoding, 'chunked');
    assert.deepEqual([...sent.body], [7, 8, 9]);
});

await step('uploadFile throws PneumaError on an error status', async () => {
    await assert.rejects(() => client.uploadFile('sub_missing', Buffer.from('x'), { filename: 'a.txt' }), (err) => {
        assert.ok(err instanceof PneumaError);
        assert.equal(err.status, 404);
        assert.equal(err.message, 'Subject not found.');
        return true;
    });
});

await step('downloadLinkSource returns bytes, type, and name', async () => {
    const source = await client.downloadLinkSource('lnk_1');
    assert.equal(Buffer.from(source.data).toString(), '%PDF-1.4 bytes');
    assert.equal(source.contentType, 'application/pdf');
    assert.equal(source.fileName, 'Q3 résumé.pdf');
});

await step('getUploadSettings', async () => {
    assert.equal((await client.getUploadSettings()).maxUploadBytes, 1024);
});

server.close();
console.log(failures === 0 ? 'Upload tests passed.' : `${failures} upload test(s) failed.`);
process.exit(failures === 0 ? 0 : 1);
