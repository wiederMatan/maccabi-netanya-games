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

  window.sfx = createSfx();

  if (window.matchMedia('(display-mode: standalone)').matches || navigator.standalone) {
    document.documentElement.classList.add('standalone');
  }

  // Installed on an iPhone, iOS reports a viewport that is short by the status
  // bar while drawing the page from the top of the screen, which leaves a white
  // band at the bottom. Stretch the page to the real screen height there.
  if (navigator.standalone === true) {
    var fillScreen = function () {
      var portrait = window.matchMedia('(orientation: portrait)').matches;
      var height = portrait ? Math.max(screen.width, screen.height) : Math.min(screen.width, screen.height);
      document.body.style.minHeight = height + 'px';
    };
    fillScreen();
    window.addEventListener('orientationchange', function () { setTimeout(fillScreen, 300); });
    window.addEventListener('resize', fillScreen);
  }

  // Stadium sound for the web games. Clips are CC0 / public-domain recordings
  // (see sounds/CREDITS.md); the ball kick is synthesised. A page opts in by
  // calling sfx.bindToggle(), which loads the clips straight away; iOS only lets
  // audio play once the player has tapped, so the first tap resumes it. A page
  // with its own audio (Math Strikers) passes { clips: false } and an onChange
  // callback, and just shares the one remembered mute setting.
  function createSfx() {
    var MUTE_KEY = 'maccabi-netanya-muted';
    var CLIPS = ['crowd', 'cheer', 'ohh', 'whistle', 'applause',
                 'music-anthem', 'music-drums', 'music-goal', 'music-win', 'music-star', 'music-tryagain'];
    // Background music loops, with the volume each one plays at.
    var MUSIC_VOLUME = { anthem: 0.3, drums: 0.22 };
    // Exact musical length of each loop. AAC encoding pads the end of a file
    // (the drums by ~14 ms), which would stutter at every repeat, so loop on
    // the bar line instead of the end of the decoded buffer.
    var MUSIC_LOOP = { anthem: 16 * 4 * 60 / 128, drums: 4 * 4 * 60 / 118 };
    var base = (document.currentScript && document.currentScript.src || '/app.js').replace(/app\.js.*$/, 'sounds/');
    var ctx = null, master = null, buffers = {}, loading = null, unlocked = false;
    var crowd = null, crowdGain = null, wantCrowd = false;
    var music = null, musicGain = null, musicName = null, wantMusic = null;
    var muted = false, listeners = [];
    try { muted = localStorage.getItem(MUTE_KEY) === '1'; } catch (e) {}

    function init() {
      if (ctx) return true;
      var AC = window.AudioContext || window.webkitAudioContext;
      if (!AC) return false;
      ctx = new AC();
      master = ctx.createGain();
      master.gain.value = muted ? 0 : 1;
      master.connect(ctx.destination);
      load();
      return true;
    }
    function unlock() {
      if (!ctx) return;
      if (ctx.state !== 'running' && !document.hidden) ctx.resume();
      if (unlocked) return;
      unlocked = true;
      // A silent buffer played inside the tap is what actually unlocks iOS.
      var src = ctx.createBufferSource();
      src.buffer = ctx.createBuffer(1, 1, 22050);
      src.connect(ctx.destination);
      src.start(0);
    }
    document.addEventListener('pointerdown', unlock, true);
    document.addEventListener('touchend', unlock, true);
    document.addEventListener('keydown', unlock, true);

    function load() {
      if (loading) return loading;
      loading = Promise.all(CLIPS.map(function (name) {
        return fetch(base + name + '.m4a')
          .then(function (r) { return r.arrayBuffer(); })
          .then(function (data) {
            return new Promise(function (ok, fail) { ctx.decodeAudioData(data, ok, fail); });
          })
          .then(function (buf) { buffers[name] = buf; })
          .catch(function () {});
      })).then(function () {
        if (wantCrowd) startCrowd();
        if (wantMusic) setMusic(wantMusic);
      });
      return loading;
    }

    function play(name, volume, rate) {
      if (!ctx || !buffers[name]) return;
      var src = ctx.createBufferSource();
      var gain = ctx.createGain();
      src.buffer = buffers[name];
      src.playbackRate.value = rate || 1;
      gain.gain.value = volume == null ? 1 : volume;
      src.connect(gain).connect(master);
      src.start();
    }

    // A short low thump with a touch of noise, like a boot on a ball.
    function kick() {
      if (!ctx) return;
      var t = ctx.currentTime;
      var osc = ctx.createOscillator(), g = ctx.createGain();
      osc.frequency.setValueAtTime(150, t);
      osc.frequency.exponentialRampToValueAtTime(45, t + 0.12);
      g.gain.setValueAtTime(0.9, t);
      g.gain.exponentialRampToValueAtTime(0.001, t + 0.16);
      osc.connect(g).connect(master);
      osc.start(t); osc.stop(t + 0.17);

      var len = Math.floor(ctx.sampleRate * 0.03);
      var noise = ctx.createBuffer(1, len, ctx.sampleRate), d = noise.getChannelData(0);
      for (var i = 0; i < len; i++) d[i] = (Math.random() * 2 - 1) * (1 - i / len);
      var n = ctx.createBufferSource(), ng = ctx.createGain(), hp = ctx.createBiquadFilter();
      hp.type = 'highpass'; hp.frequency.value = 1500;
      n.buffer = noise; ng.gain.value = 0.25;
      n.connect(hp).connect(ng).connect(master);
      n.start(t);
    }

    function startCrowd() {
      if (!ctx || !buffers.crowd || crowd) return;
      crowd = ctx.createBufferSource();
      crowdGain = ctx.createGain();
      crowd.buffer = buffers.crowd;
      crowd.loop = true;
      crowdGain.gain.setValueAtTime(0, ctx.currentTime);
      crowdGain.gain.linearRampToValueAtTime(0.35, ctx.currentTime + 1.5);
      crowd.connect(crowdGain).connect(master);
      crowd.start();
    }

    function stopCrowd(fade) {
      if (!crowd) return;
      var c = crowd, t = ctx.currentTime, f = fade == null ? 1 : fade;
      crowdGain.gain.cancelScheduledValues(t);
      crowdGain.gain.setValueAtTime(crowdGain.gain.value, t);
      crowdGain.gain.linearRampToValueAtTime(0, t + f);
      c.stop(t + f + 0.05);
      crowd = null;
    }

    // One music loop at a time: the anthem on menus, the drums in play.
    // Switching fades the old loop out and the new one in.
    function setMusic(name) {
      wantMusic = name;
      if (!ctx || name === musicName) return;
      if (music) {
        var old = music, oldGain = musicGain, t = ctx.currentTime;
        oldGain.gain.cancelScheduledValues(t);
        oldGain.gain.setValueAtTime(oldGain.gain.value, t);
        oldGain.gain.linearRampToValueAtTime(0, t + 0.8);
        old.stop(t + 0.85);
        music = null; musicName = null;
      }
      if (!name || !buffers['music-' + name]) return;
      music = ctx.createBufferSource();
      musicGain = ctx.createGain();
      music.buffer = buffers['music-' + name];
      music.loop = true;
      if (MUSIC_LOOP[name] && MUSIC_LOOP[name] < music.buffer.duration) {
        music.loopStart = 0;
        music.loopEnd = MUSIC_LOOP[name];
      }
      musicGain.gain.setValueAtTime(0, ctx.currentTime);
      musicGain.gain.linearRampToValueAtTime(MUSIC_VOLUME[name] || 0.25, ctx.currentTime + 0.8);
      music.connect(musicGain).connect(master);
      music.start();
      musicName = name;
    }

    function setMuted(value) {
      muted = value;
      try { localStorage.setItem(MUTE_KEY, muted ? '1' : '0'); } catch (e) {}
      if (master) master.gain.setTargetAtTime(muted ? 0 : 1, ctx.currentTime, 0.05);
      document.querySelectorAll('.sound-toggle').forEach(render);
      listeners.forEach(function (fn) { fn(muted); });
    }

    // Sized and stroked inline so the icon also renders on pages without the
    // shared stylesheet.
    var SOUND_ON = '<svg class="icon" viewBox="0 0 24 24" width="1em" height="1em" fill="none" stroke="currentColor" stroke-width="2.6" stroke-linecap="round" stroke-linejoin="round"><path d="M4 9h4l5-4v14l-5-4H4z" fill="currentColor"/><path d="M16.5 8.5a5 5 0 0 1 0 7M19 6a8.5 8.5 0 0 1 0 12"/></svg>';
    var SOUND_OFF = '<svg class="icon" viewBox="0 0 24 24" width="1em" height="1em" fill="none" stroke="currentColor" stroke-width="2.6" stroke-linecap="round" stroke-linejoin="round"><path d="M4 9h4l5-4v14l-5-4H4z" fill="currentColor"/><path d="M16.5 9.5l5 5M21.5 9.5l-5 5"/></svg>';

    function render(btn) {
      btn.innerHTML = muted ? SOUND_OFF : SOUND_ON;
      btn.setAttribute('aria-label', muted ? 'הפעל צלילים' : 'השתק צלילים');
      btn.setAttribute('aria-pressed', String(!muted));
    }

    // Stop everything when the app goes to the background, pick up on return.
    document.addEventListener('visibilitychange', function () {
      if (!ctx || !unlocked) return;
      if (document.hidden) ctx.suspend(); else ctx.resume();
    });

    return {
      kick: kick,
      cheer: function () { play('cheer', 0.8, 0.95 + Math.random() * 0.1); },
      ohh: function () { play('ohh', 0.45); },
      whistle: function () { play('whistle', 0.7); },
      // Referee's full-time whistle: two short blasts and a long one.
      finalWhistle: function () {
        if (!ctx) return;
        [0, 0.35, 0.7].forEach(function (delay, i) {
          setTimeout(function () { play('whistle', i === 2 ? 0.8 : 0.6, i === 2 ? 0.97 : 1.05); }, delay * 1000);
        });
      },
      applause: function () { play('applause', 0.8); },
      // Original music composed for the site (see sounds/CREDITS.md):
      // sfx.music('anthem' | 'drums' | null), sfx.jingle('goal' | 'win' | 'star' | 'tryagain').
      music: setMusic,
      jingle: function (name, volume) { play('music-' + name, volume == null ? 0.8 : volume); },
      crowd: function (on) {
        wantCrowd = on;
        if (on) startCrowd(); else stopCrowd();
      },
      isMuted: function () { return muted; },
      toggle: function () { setMuted(!muted); },
      bindToggle: function (btn, options) {
        options = options || {};
        if (options.clips !== false) init();
        if (options.onChange) listeners.push(options.onChange);
        render(btn);
        btn.addEventListener('click', function () { unlock(); setMuted(!muted); });
      }
    };
  }
})();

