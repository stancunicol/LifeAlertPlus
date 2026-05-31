const CACHE_NAME = 'lifealertplus-cache-v3';
const PRECACHE_URLS = [
  '/',
  'index.html',
  'offline.html',
  'css/app.css',
  'LifeAlertPlus.Client.styles.css',
  'favicon.png',
  'icon-192.png',
  'final_logo.png',
  'VERSION'
];

self.addEventListener('install', event => {
  event.waitUntil(
    caches.open(CACHE_NAME).then(cache => cache.addAll(PRECACHE_URLS))
  );
  self.skipWaiting();
});

self.addEventListener('activate', event => {
  event.waitUntil(
    caches.keys().then(keys => Promise.all(
      keys.map(key => {
        if (key !== CACHE_NAME) return caches.delete(key);
        return null;
      })
    ))
  );
  self.clients.claim();
});

self.addEventListener('fetch', event => {
  if (event.request.method !== 'GET') return;

  // Blazor framework assets (_framework/) carry their own SRI integrity hashes managed
  // by blazor.boot.json. Caching them here causes SRI mismatches after every build.
  // Let the browser fetch them directly so Blazor's own integrity checks always pass.
  if (event.request.url.includes('/_framework/')) return;

  // Navigation requests: network-first then fallback to cache/offline
  if (event.request.mode === 'navigate' || (event.request.headers.get('accept') || '').includes('text/html')) {
    event.respondWith(
      fetch(event.request).then(response => {
        const copy = response.clone();
        caches.open(CACHE_NAME).then(cache => cache.put(event.request, copy));
        return response;
      }).catch(() => caches.match(event.request).then(match => match || caches.match('offline.html')))
    );
    return;
  }

  // For other GET requests, try cache-first then network
  event.respondWith(
    caches.match(event.request).then(cached => {
      if (cached) return cached;
      return fetch(event.request).then(response => {
        if (!response || response.status !== 200 || response.type === 'opaque') return response;
        const clone = response.clone();
        caches.open(CACHE_NAME).then(cache => cache.put(event.request, clone));
        return response;
      }).catch(() => caches.match(event.request));
    })
  );
});

// Listen for skipWaiting message
self.addEventListener('message', event => {
  if (event.data && event.data.type === 'SKIP_WAITING') self.skipWaiting();
});

// ── Web Push ────────────────────────────────────────────
self.addEventListener('push', event => {
  if (!event.data) return;
  let payload;
  try { payload = event.data.json(); } catch { payload = { title: 'LifeAlertPlus', body: event.data.text(), severity: 'Info' }; }

  const title = payload.title || 'LifeAlertPlus';
  const body  = payload.body  || '';
  const icon  = payload.severity === 'Critical' ? '/icon-192.png' : '/icon-192.png';
  const badge = '/favicon.png';
  const tag   = payload.severity || 'info';

  event.waitUntil(
    self.registration.showNotification(title, { body, icon, badge, tag, renotify: true, data: { url: '/notifications' } })
  );
});

self.addEventListener('notificationclick', event => {
  event.notification.close();
  const url = (event.notification.data && event.notification.data.url) || '/';
  event.waitUntil(
    clients.matchAll({ type: 'window', includeUncontrolled: true }).then(list => {
      const existing = list.find(c => c.url.includes(self.location.origin));
      if (existing) { existing.focus(); existing.navigate(url); }
      else clients.openWindow(url);
    })
  );
});
