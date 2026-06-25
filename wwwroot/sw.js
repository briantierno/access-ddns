const CACHE_NAME = 'access-ddns-v2'; // Cambiar versión para forzar renovación
const ASSETS_TO_CACHE = [
  '/',
  '/index.html',
  '/manifest.json',
  '/icon-192.png',
  '/icon-512.png',
  'https://cdnjs.cloudflare.com/ajax/libs/font-awesome/6.4.0/css/all.min.css'
];

// Install event - cache assets
self.addEventListener('install', event => {
  event.waitUntil(
    caches.open(CACHE_NAME).then(cache => {
      return cache.addAll(ASSETS_TO_CACHE).catch(err => {
        console.log('Cache addAll error:', err);
        return cache.add('/');
      });
    })
  );
  self.skipWaiting();
});

// Activate event - clean old caches
self.addEventListener('activate', event => {
  event.waitUntil(
    caches.keys().then(cacheNames => {
      return Promise.all(
        cacheNames.map(cacheName => {
          if (cacheName !== CACHE_NAME) {
            console.log('Deleting old cache:', cacheName);
            return caches.delete(cacheName);
          }
        })
      );
    })
  );
  self.clients.claim();
});

// Fetch event - Network First para APIs, Cache First para assets
self.addEventListener('fetch', event => {
  const url = new URL(event.request.url);
  
  // APIs: SIEMPRE ir a network, sin cachear
  if (url.pathname.startsWith('/api/')) {
    event.respondWith(
      fetch(event.request, { cache: 'no-store' })
        .catch(() => new Response(JSON.stringify({ error: 'Network error - offline' }), {
          status: 503,
          headers: { 'Content-Type': 'application/json' }
        }))
    );
    return;
  }

  // External APIs (ipinfo.io): Network first con reintentos
  if (event.request.url.includes('ipinfo.io')) {
    event.respondWith(
      fetch(event.request, { cache: 'no-store' })
        .catch(() => new Response(JSON.stringify({ error: 'Network error' }), {
          status: 503,
          headers: { 'Content-Type': 'application/json' }
        }))
    );
    return;
  }

  // Assets: Cache first, fallback a network
  event.respondWith(
    caches.match(event.request)
      .then(response => {
        if (response) {
          return response;
        }
        return fetch(event.request);
      })
      .catch(() => {
        return caches.match('/index.html');
      })
  );
});
