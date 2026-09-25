// Starts the API for the end-to-end tests: its own database (reset on every run), its own storage folder in the
// OS temp directory and its own port, so a developer's running backend and data are never touched.
// The API is built to a separate output folder (Release) so a running Debug backend cannot lock its files.
import { spawn, spawnSync } from 'node:child_process';
import { rmSync } from 'node:fs';
import { tmpdir } from 'node:os';
import { dirname, join, resolve } from 'node:path';
import { fileURLToPath } from 'node:url';

const here = dirname(fileURLToPath(import.meta.url));
const project = resolve(here, '../../../backend/src/BildiriKitabi.Api');
const work = join(tmpdir(), 'bildiri-kitabi-e2e');
const output = join(work, 'api');
const storage = join(work, 'storage');
const uploads = join(work, 'uploads');
const port = process.env.E2E_BACKEND_PORT ?? '5081';
const connectionString =
  process.env.E2E_CONNECTION_STRING ??
  'Server=(localdb)\\MSSQLLocalDB;Database=BildiriKitabi_E2E;Trusted_Connection=True;TrustServerCertificate=True;MultipleActiveResultSets=False';

rmSync(storage, { recursive: true, force: true });
rmSync(uploads, { recursive: true, force: true });

const build = spawnSync('dotnet', ['build', project, '-c', 'Release', '-o', output, '--nologo', '-v', 'q'], {
  stdio: 'inherit',
});
if (build.status !== 0) {
  process.exit(build.status ?? 1);
}

const api = spawn('dotnet', [join(output, 'BildiriKitabi.Api.dll')], {
  cwd: output,
  stdio: 'inherit',
  env: {
    ...process.env,
    ASPNETCORE_ENVIRONMENT: 'Development',
    ASPNETCORE_URLS: `http://localhost:${port}`,
    ConnectionStrings__Default: connectionString,
    Database__ApplyMigrationsOnStartup: 'true',
    Database__ResetOnStartup: 'true',
    Storage__RootPath: storage,
    Upload__TempPath: uploads,
    RateLimiting__UploadPermitsPerMinute: '1000',
    RateLimiting__GeneratePermitsPerMinute: '1000',
    Logging__LogLevel__Default: 'Warning',
  },
});

for (const signal of ['SIGINT', 'SIGTERM']) {
  process.on(signal, () => {
    api.kill();
  });
}

api.on('exit', (code) => {
  process.exit(code ?? 0);
});
