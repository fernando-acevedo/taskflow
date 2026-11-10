// Run with `npm run test:scripts` (node's built-in test runner).
import assert from 'node:assert/strict';
import { test } from 'node:test';
import { apiOrigin, connectSources, telemetryOrigin } from './apply-csp.mjs';

const env = (connection, api = 'https://api.example.com/') =>
  `export const environment = { apiUrl: '${api}', appInsightsConnectionString: '${connection}' };`;

test('telemetryOrigin is empty without a connection string', () => {
  assert.equal(telemetryOrigin(env('')), '');
});

test('telemetryOrigin takes the origin of the IngestionEndpoint, dropping the path', () => {
  const connection = 'InstrumentationKey=k;IngestionEndpoint=https://westeurope-5.in.applicationinsights.azure.com/;LiveEndpoint=https://x';
  assert.equal(telemetryOrigin(env(connection)), 'https://westeurope-5.in.applicationinsights.azure.com');
});

test('telemetryOrigin falls back to the global endpoint when the string has none', () => {
  assert.equal(telemetryOrigin(env('InstrumentationKey=k')), 'https://dc.services.visualstudio.com');
});

test('apiOrigin is the origin of apiUrl, or empty', () => {
  assert.equal(apiOrigin(env('', 'https://api.example.com/v1')), 'https://api.example.com');
  assert.equal(apiOrigin(env('', '')), '');
});

test('connectSources reads only the connect-src directive', () => {
  const policy = "default-src 'self'; img-src https://img.test; connect-src 'self' https://api.test https://dc.test; frame-ancestors 'none'";
  assert.deepEqual(connectSources(policy), ["'self'", 'https://api.test', 'https://dc.test']);
  assert.ok(!connectSources(policy).includes('https://img.test'));
  assert.deepEqual(connectSources("default-src 'self'"), []);
});
