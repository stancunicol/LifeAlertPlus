// Service worker pentru funcționalitatea PWA (Progressive Web App) — caching offline + Web Push
// Versiunea din nume (v3) trebuie incrementată la schimbări de cache, ca activate să șteargă cache-ul vechi
const CACHE_NAME = 'lifealertplus-cache-v3';
const PRECACHE_URLS = [
  '/',
  'index.html',
  'offline.html', // Pagină afișată când nu există nici cache, nici conexiune
  'css/app.css',
  'LifeAlertPlus.Client.styles.css',
  'favicon.png',
  'icon-192.png',
  'final_logo.png',
  'VERSION'
];

// La instalare: trecem imediat la activare (skipWaiting) și pre-încărcăm resursele esențiale în cache
self.addEventListener('install', event => {
  self.skipWaiting();
  event.waitUntil(
    caches.open(CACHE_NAME).then(cache =>
      Promise.allSettled(PRECACHE_URLS.map(url => cache.add(url))) // allSettled: un URL care eșuează nu blochează restul
    )
  );
});

// La activare: ștergem cache-urile vechi (versiuni anterioare) și luăm imediat controlul tab-urilor deschise
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
  if (event.request.method !== 'GET') return; // Nu cache-uim POST/PUT/DELETE (mutații, nu trebuie servite din cache)

  // Resursele Blazor (_framework/) au propriile hash-uri de integritate SRI, gestionate de blazor.boot.json.
  // Cache-uirea lor aici ar cauza nepotriviri SRI după fiecare build nou.
  // Lăsăm browserul să le ceară direct, ca verificările de integritate ale Blazor să treacă mereu.
  if (event.request.url.includes('/_framework/')) return;

  // Cereri de navigare (încărcare pagină): network-first, cu fallback pe cache sau pagina offline
  if (event.request.mode === 'navigate' || (event.request.headers.get('accept') || '').includes('text/html')) {
    event.respondWith(
      fetch(event.request).then(response => {
        const copy = response.clone(); // Răspunsul e un stream — clonăm înainte de a-l citi de două ori (cache + return)
        caches.open(CACHE_NAME).then(cache => cache.put(event.request, copy));
        return response;
      }).catch(() => caches.match(event.request).then(match => match || caches.match('offline.html')))
    );
    return;
  }

  // Pentru restul cererilor GET (CSS, imagini etc.): cache-first, apoi rețea dacă nu e în cache
  event.respondWith(
    caches.match(event.request).then(cached => {
      if (cached) return cached;
      return fetch(event.request).then(response => {
        if (!response || response.status !== 200 || response.type === 'opaque') return response; // Nu cache-uim erori/răspunsuri opace (cross-origin fără CORS)
        const clone = response.clone();
        caches.open(CACHE_NAME).then(cache => cache.put(event.request, clone));
        return response;
      }).catch(() => caches.match(event.request));
    })
  );
});

// Ascultă mesajul de la client pentru a forța activarea noului service worker imediat (skip waiting),
// declanșat de obicei dintr-un prompt UI de "există o versiune nouă, reîncarcă"
self.addEventListener('message', event => {
  if (event.data && event.data.type === 'SKIP_WAITING') self.skipWaiting();
});

// ── Web Push ────────────────────────────────────────────
// Primește notificarea push trimisă de backend (PushNotificationService) și o afișează utilizatorului,
// chiar dacă aplicația nu e deschisă în niciun tab (specific PWA/service worker)
self.addEventListener('push', event => {
  if (!event.data) return;
  let payload;
  try { payload = event.data.json(); } catch { payload = { title: 'LifeAlertPlus', body: event.data.text(), severity: 'Info' }; } // Fallback dacă payload-ul nu e JSON valid

  const title = payload.title || 'LifeAlertPlus';
  const body  = payload.body  || '';
  const icon  = payload.severity === 'Critical' ? '/icon-192.png' : '/icon-192.png';
  const badge = '/favicon.png';
  const tag   = payload.severity || 'info'; // renotify+tag: o notificare nouă cu aceeași severitate o înlocuiește pe cea veche, nu se acumulează

  event.waitUntil(
    self.registration.showNotification(title, { body, icon, badge, tag, renotify: true, data: { url: '/notifications' } })
  );
});

// La click pe notificare: focalizează tab-ul existent al aplicației (dacă există) și navighează la pagina de notificări,
// altfel deschide un tab nou — comportament standard PWA pentru notificări push
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
