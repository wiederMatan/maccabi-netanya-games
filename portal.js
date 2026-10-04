// Fills the portal from what the games saved in localStorage: total stars,
// a "new" badge on games not played yet, each game's best score, and a
// "continue playing" card for the last game.
(function () {
  function read(key) {
    try { return localStorage.getItem(key); } catch (e) { return null; }
  }

  document.getElementById('total-stars').textContent = parseInt(read('maccabi-netanya-stars') || '0', 10);

  var cards = document.querySelectorAll('.game-card[data-slug]');
  cards.forEach(function (card) {
    var slug = card.dataset.slug;
    if (!read('maccabi-netanya-played-' + slug)) {
      var badge = document.createElement('span');
      badge.className = 'badge';
      badge.textContent = 'חדש!';
      card.appendChild(badge);
    }
    var best = parseInt(read('maccabi-netanya-best-' + slug) || '0', 10);
    if (best > 0) {
      var pill = document.createElement('span');
      pill.className = 'pill best';
      pill.innerHTML = '<span class="star">🏆</span>שיא: ' + best;
      card.appendChild(pill);
    }
  });

  var last = read('maccabi-netanya-last');
  var lastCard = last && document.querySelector('.game-card[data-slug="' + last + '"]');
  if (lastCard) {
    document.getElementById('continue').href = lastCard.getAttribute('href');
    document.getElementById('continue-thumb').src = lastCard.querySelector('.thumb').getAttribute('src');
    document.getElementById('continue-name').textContent = lastCard.querySelector('.game-title').textContent;
    document.getElementById('continue-section').hidden = false;
  }
})();
