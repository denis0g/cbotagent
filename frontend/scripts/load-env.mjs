import { mkdir, readFile, writeFile } from 'node:fs/promises';
import { fileURLToPath } from 'node:url';
import path from 'node:path';

const frontendRoot = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '..');
const rootEnvPath = path.resolve(frontendRoot, '..', '.env');
const outputPath = path.join(frontendRoot, 'src', 'environments', 'environment.ts');

let envContents;
try {
  envContents = await readFile(rootEnvPath, 'utf8');
} catch {
  throw new Error(`Missing ${rootEnvPath}. Copy .env.example to .env and configure it first.`);
}

const apiUrlLine = envContents
  .split(/\r?\n/)
  .find((line) => /^\s*FRONTEND_API_URL\s*=/.test(line));

if (!apiUrlLine) {
  throw new Error('FRONTEND_API_URL is missing from the root .env file.');
}

const apiUrl = apiUrlLine.slice(apiUrlLine.indexOf('=') + 1).trim().replace(/^['"]|['"]$/g, '').replace(/\/$/, '');
if (!apiUrl) {
  throw new Error('FRONTEND_API_URL must have a value.');
}

await mkdir(path.dirname(outputPath), { recursive: true });
await writeFile(
  outputPath,
  `// Generated from the root .env file. Do not commit this file.\nexport const environment = { apiUrl: ${JSON.stringify(apiUrl)} };\n`,
);
