// Shared by every page: makes the site behave like an installed app.
(function () {
  if ('serviceWorker' in navigator) {
    window.addEventListener('load', function () {
      navigator.serviceWorker.register('/sw.js').catch(function () {});
    });
  }

  // iOS Safari ignores user-scalable=no; block pinch zoom so it never fights the
  // games. Double-tap zoom is turned off in CSS (touch-action: manipulation).
  document.addEventListener('gesturestart', function (e) { e.preventDefault(); });

  // Short vibration on Android; a no-op where unsupported (iOS).
  window.haptic = function (pattern) {
    if (navigator.vibrate) navigator.vibrate(pattern || 15);
  };

  if (window.matchMedia('(display-mode: standalone)').matches || navigator.standalone) {
    document.documentElement.classList.add('standalone');
  }
})();
