// Offline-first service worker for published PWA builds.

let assetsManifest = null;
try {
    self.importScripts('./service-worker-assets.js');
    assetsManifest = self.assetsManifest;
} catch {
    assetsManifest = null;
}

const cacheNamePrefix = 'flashcards-offline-';
const cacheVersion = assetsManifest?.version ?? 'published';
const cacheName = `${cacheNamePrefix}${cacheVersion}`;
const offlineAssetsInclude = [ /\.dll$/, /\.pdb$/, /\.wasm$/, /\.html$/, /\.js$/, /\.json$/, /\.css$/, /\.woff2?$/, /\.png$/, /\.jpe?g$/, /\.gif$/, /\.ico$/, /\.blat$/, /\.dat$/ ];
const offlineAssetsExclude = [ /^service-worker\.js$/ ];
const coreAssets = [
    './',
    './index.html',
    './manifest.webmanifest',
    './css/app.css',
    './recallcraft.js',
    './icon-192.png',
    './icon-512.png',
    './favicon.png'
];

self.addEventListener('install', event => {
    self.skipWaiting();
    event.waitUntil(onInstall());
});

self.addEventListener('activate', event => {
    event.waitUntil(onActivate());
});

self.addEventListener('fetch', event => {
    if (event.request.method !== 'GET') {
        return;
    }

    event.respondWith(onFetch(event.request));
});

async function onInstall() {
    const cache = await caches.open(cacheName);
    await Promise.allSettled(coreAssets.map(asset => cache.add(asset)));

    if (!assetsManifest) {
        return;
    }

    const baseUrl = new URL('./', self.origin);
    const assetRequests = assetsManifest.assets
        .filter(asset => offlineAssetsInclude.some(pattern => pattern.test(asset.url)))
        .filter(asset => !offlineAssetsExclude.some(pattern => pattern.test(asset.url)))
        .map(asset => new Request(new URL(asset.url, baseUrl), {
            integrity: asset.hash,
            cache: 'no-cache'
        }));

    await Promise.allSettled(assetRequests.map(request => cache.add(request)));
}

async function onActivate() {
    self.clients.claim();
    const keys = await caches.keys();
    await Promise.all(
        keys
            .filter(key => key.startsWith(cacheNamePrefix) && key !== cacheName)
            .map(key => caches.delete(key)));
}

async function onFetch(request) {
    const cache = await caches.open(cacheName);

    if (request.mode === 'navigate') {
        try {
            const response = await fetch(request);
            cache.put('./index.html', response.clone());
            return response;
        } catch {
            return await cache.match('./index.html') || await cache.match('index.html');
        }
    }

    const cached = await cache.match(request);
    if (cached) {
        return cached;
    }

    try {
        const response = await fetch(request);
        if (response.ok && new URL(request.url).origin === self.origin) {
            cache.put(request, response.clone());
        }
        return response;
    } catch {
        return cached || Response.error();
    }
}
