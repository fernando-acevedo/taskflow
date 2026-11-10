// Completes the Content-Security-Policy of the built app (runs as `postbuild`; `--check` only verifies).
//
// public/staticwebapp.config.json holds the policy with two placeholders, and `ng build` copies it to dist as is:
//   __SCRIPT_HASHES__  the 'sha256-...' sources of the inline <script>s of the BUILT dist/.../index.html (the
//                      anti-flash theme script from src/index.html, and the one the build adds to load the
//                      critical-CSS stylesheet). Hashing the built file, not src/index.html, keeps the hash right
//                      whatever the line endings of the checkout or the transformations the build applies.
//   __TELEMETRY_ORIGIN__ the Application Insights ingestion origin of `appInsightsConnectionString`; removed when it is empty.
//   __API_ORIGIN__     the origin of `apiUrl` in src/environments/environment.ts (the deploy workflow writes it
//                      from vars.API_URL before building). Empty `apiUrl` leaves only 'self'.
//
// Usage: node scripts/apply-csp.mjs [--check]
//   (default) rewrites dist/frontend/browser/staticwebapp.config.json with the placeholders resolved.
//   --check   fails (exit 1) if that file's hashes or API origin don't match the built index.html / environment.
import { createHash } from 'node:crypto';
import { readFileSync, writeFileSync } from 'node:fs';
import { dirname, join } from 'node:path';
import { fileURLToPath, pathToFileURL } from 'node:url';

const root = join(dirname(fileURLToPath(import.meta.url)), '..');
const distDir = join(root, 'dist', 'frontend', 'browser');
const configPath = join(distDir, 'staticwebapp.config.json');

export function inlineScriptHashes(html) {
  const hashes = [];
  for (const [, attributes, body] of html.matchAll(/<script\b([^>]*)>([\s\S]*?)<\/script>/gi)) {
    if (/\bsrc\s*=/i.test(attributes)) {
      continue;
    }
    hashes.push(`'sha256-${createHash('sha256').update(body, 'utf8').digest('base64')}'`);
  }
  return hashes;
}

// Inline event handlers (onclick="...") are blocked by the policy (no 'unsafe-inline' in script-src).
export function inlineEventHandlers(html) {
  return [...html.matchAll(/<[a-z][^>]*\s(on[a-z]+)\s*=/gi)].map((match) => match[1].toLowerCase());
}

export function apiOrigin(environmentSource) {
  const url = /apiUrl:\s*'([^']*)'/.exec(environmentSource)?.[1] ?? '';
  return url === '' ? '' : new URL(url).origin;
}

export function telemetryOrigin(environmentSource) {
  const connection = /appInsightsConnectionString:\s*'([^']*)'/.exec(environmentSource)?.[1] ?? '';
  if (connection === '') {
    return '';
  }
  const endpoint = /IngestionEndpoint=([^;]+)/i.exec(connection)?.[1] ?? 'https://dc.services.visualstudio.com';
  return new URL(endpoint).origin;
}

/** The sources of the policy's connect-src directive. */
export function connectSources(policy) {
  const directive = policy.split(';').map((part) => part.trim()).find((part) => part.startsWith('connect-src'));
  return directive ? directive.split(/\s+/).slice(1) : [];
}

function expectedPolicy(template) {
  const html = readFileSync(join(distDir, 'index.html'), 'utf8');
  const hashes = inlineScriptHashes(html);
  if (hashes.length === 0) {
    throw new Error('No inline script found in the built index.html; the CSP placeholders would be wrong.');
  }
  const origin = apiOrigin(readFileSync(join(root, 'src', 'environments', 'environment.ts'), 'utf8'));
  const environmentSource = readFileSync(join(root, 'src', 'environments', 'environment.ts'), 'utf8');
  const telemetry = telemetryOrigin(environmentSource);
  return template
    .replace('__SCRIPT_HASHES__', hashes.join(' '))
    .replace(/ ?__API_ORIGIN__/, origin ? ` ${origin}` : '')
    .replace(/ ?__TELEMETRY_ORIGIN__/, telemetry ? ` ${telemetry}` : '');
}

function main() {
  const check = process.argv.includes('--check');
  const current = readFileSync(configPath, 'utf8');

  if (check) {
    // The template is the file as it was before the rewrite: rebuild it from the resolved policy.
    const policy = JSON.parse(current).globalHeaders['Content-Security-Policy'];
    const html = readFileSync(join(distDir, 'index.html'), 'utf8');
    const missing = inlineScriptHashes(html).filter((hash) => !policy.includes(hash));
    const origin = apiOrigin(readFileSync(join(root, 'src', 'environments', 'environment.ts'), 'utf8'));
    const telemetry = telemetryOrigin(readFileSync(join(root, 'src', 'environments', 'environment.ts'), 'utf8'));
    const problems = [
      ...inlineEventHandlers(html).map((name) => `index.html has an inline ${name}= attribute, which the CSP blocks`),
      ...missing.map((hash) => `inline script hash ${hash} is not in the CSP`),
      ...(policy.includes('__') ? ['the CSP still has a placeholder'] : []),
      ...(origin && !connectSources(policy).includes(origin) ? [`connect-src lacks the API origin ${origin}`] : []),
    ...(telemetry && !connectSources(policy).includes(telemetry)
      ? [`connect-src lacks the telemetry origin ${telemetry} (appInsightsConnectionString is set)`]
      : []),
    ];
    if (problems.length > 0) {
      console.error(`CSP check failed:\n- ${problems.join('\n- ')}`);
      process.exit(1);
    }
    console.log('CSP matches the built index.html.');
  } else {
    if (!current.includes('__SCRIPT_HASHES__')) {
      console.log('staticwebapp.config.json has no placeholders; nothing to do.');
    } else {
      writeFileSync(configPath, expectedPolicy(current));
      console.log('CSP written to dist/frontend/browser/staticwebapp.config.json.');
    }
  }
}

if (process.argv[1] && import.meta.url === pathToFileURL(process.argv[1]).href) {
  main();
}
