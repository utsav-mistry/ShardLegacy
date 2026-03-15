import { fileURLToPath, URL } from 'node:url';

import { defineConfig } from 'vite';
import plugin from '@vitejs/plugin-react';
import fs from 'fs';
import path from 'path';
import child_process from 'child_process';
import { env } from 'process';

const __filename = fileURLToPath(import.meta.url);
const __dirname = path.dirname(__filename);

const baseFolder =
    env.APPDATA !== undefined && env.APPDATA !== ''
        ? `${env.APPDATA}/ASP.NET/https`
        : `${env.HOME}/.aspnet/https`;

const certificateName = "shardlegacy.client";
const certFilePath = path.join(baseFolder, `${certificateName}.pem`);
const keyFilePath = path.join(baseFolder, `${certificateName}.key`);

if (!fs.existsSync(baseFolder)) {
    fs.mkdirSync(baseFolder, { recursive: true });
}

if (!fs.existsSync(certFilePath) || !fs.existsSync(keyFilePath)) {
    if (0 !== child_process.spawnSync('dotnet', [
        'dev-certs',
        'https',
        '--export-path',
        certFilePath,
        '--format',
        'Pem',
        '--no-password',
    ], { stdio: 'inherit', }).status) {
        throw new Error("Could not create certificate.");
    }
}

let target = null;

// Prefer explicit HTTPS port if provided
if (env.ASPNETCORE_HTTPS_PORT) {
    target = `https://localhost:${env.ASPNETCORE_HTTPS_PORT}`;
} else if (env.ASPNETCORE_URLS) {
    target = env.ASPNETCORE_URLS.split(';')[0];
} else {
    // Fallback: try to read the server's launchSettings.json to discover the applicationUrl
    try {
        const launchPath = path.resolve(__dirname, '../ShardLegacy.Server/Properties/launchSettings.json');
        if (fs.existsSync(launchPath)) {
            const launchJson = JSON.parse(fs.readFileSync(launchPath, { encoding: 'utf8' }));
            const profiles = launchJson && launchJson.profiles;
            if (profiles) {
                // Prefer the https profile, then https-like entries, then any with applicationUrl
                const profileOrder = ['https', 'http'];
                let appUrl = null;
                for (const name of profileOrder) {
                    if (profiles[name] && profiles[name].applicationUrl) {
                        appUrl = profiles[name].applicationUrl;
                        break;
                    }
                }

                if (!appUrl) {
                    for (const key of Object.keys(profiles)) {
                        if (profiles[key] && profiles[key].applicationUrl) {
                            appUrl = profiles[key].applicationUrl;
                            break;
                        }
                    }
                }

                if (appUrl) {
                    // applicationUrl may contain multiple urls separated by ';' - pick the first
                    target = appUrl.split(';')[0];
                }
            }
        }
    } catch (e) {
        // ignore and fall back to default
    }

    // final default if nothing found
    if (!target) target = 'https://localhost:7048';
}

export default defineConfig({
    plugins: [plugin()],
    resolve: {
        alias: {
            '@': fileURLToPath(new URL('./src', import.meta.url))
        }
    },
    server: {
        proxy: {
            '^/weatherforecast': {
                target,
                secure: false
            },
            '^/api': {
                target,
                secure: false
            }
        },
        port: parseInt(env.DEV_SERVER_PORT || '55730'),
        https: {
            key: fs.readFileSync(keyFilePath),
            cert: fs.readFileSync(certFilePath),
        }
    }
})
// Print resolved backend target so it's obvious where the proxy will forward requests
console.log(`[vite] proxy target -> ${target}`);
