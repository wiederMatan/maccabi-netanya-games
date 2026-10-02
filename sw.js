// Offline support. The app shell and squad photos are cached on install, so the
// portal and the memory game work with no network. The Unity build (~33MB) is
// cached the first time it loads, so Math Strikers works offline after one play.
// Its file names never change, so bump VERSION whenever a new build ships.
const VERSION = 'v1';
const SHELL_CACHE = 'shell-' + VERSION;
const GAME_CACHE = 'game-' + VERSION;

const SHELL = [
  "/",
  "/index.html",
  "/style.css",
  "/portal.js",
  "/app.js",
  "/manifest.webmanifest",
  "/icons/icon.svg",
  "/icons/icon-192.png",
  "/icons/icon-512.png",
  "/icons/apple-touch-icon.png",
  "/games/memory/index.html",
  "/games/math-strikers/index.html",
  "/images/players/1-antma.webp",
  "/images/players/2-morozov.webp",
  "/images/players/4-ben-shabat.webp",
  "/images/players/5-kolikov.webp",
  "/images/players/6-konate.webp",
  "/images/players/8-haziza.webp",
  "/images/players/10-oz.webp",
  "/images/players/11-hugi.webp",
  "/images/players/12-azugi.webp",
  "/images/players/13-nidam.webp",
  "/images/players/14-liem.webp",
  "/images/players/15-maor.webp",
  "/images/players/16-zarura.webp",
  "/images/players/17-yarin.webp",
  "/images/players/18-shamir.webp",
  "/images/players/21-talpa.webp",
  "/images/players/22-samu.webp",
  "/images/players/24-amit-cohen.webp",
  "/images/players/25-cifrian.webp",
  "/images/players/26-jabber.webp",
  "/images/players/40-saba.webp",
  "/images/players/44-feldman.webp",
  "/images/players/83-davo.webp",
  "/images/players/aziz.webp",
  "/images/players/daniel-cohen.webp",
];

self.addEventListener('install', (event) => {
  event.waitUntil(caches.open(SHELL_CACHE).then((cache) => cache.addAll(SHELL)));
  self.skipWaiting();
});

self.addEventListener('activate', (event) => {
  event.waitUntil(
    caches.keys().then((keys) => Promise.all(
      keys.filter((k) => k !== SHELL_CACHE && k !== GAME_CACHE).map((k) => caches.delete(k))
    )).then(() => self.clients.claim())
  );
});

self.addEventListener('fetch', (event) => {
  const req = event.request;
  if (req.method !== 'GET') return;
  const url = new URL(req.url);
  if (url.origin !== location.origin) return;

  // Big, versioned-by-deploy game binaries: cache first.
  if (url.pathname.includes('/Build/')) {
    event.respondWith(
      caches.open(GAME_CACHE).then((cache) =>
        cache.match(req).then((hit) => hit || fetch(req).then((res) => {
          if (res.ok) cache.put(req, res.clone());
          return res;
        }))
      )
    );
    return;
  }

  // Everything else: network first so updates show up, cache as the fallback.
  event.respondWith(
    fetch(req).then((res) => {
      if (res.ok) {
        const copy = res.clone();
        caches.open(SHELL_CACHE).then((cache) => cache.put(req, copy));
      }
      return res;
    }).catch(() =>
      caches.match(req, { ignoreSearch: true }).then((hit) => hit || caches.match('/index.html'))
    )
  );
});
