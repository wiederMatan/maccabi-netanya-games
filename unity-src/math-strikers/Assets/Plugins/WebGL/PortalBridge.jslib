// Lets the Unity games share progress with the website (same localStorage keys
// the portal reads) and use the phone's vibration motor.
mergeInto(LibraryManager.library, {
  PortalBridgeAddStars: function (count) {
    try {
      var key = "maccabi-netanya-stars";
      localStorage.setItem(key, (parseInt(localStorage.getItem(key) || "0", 10) + count).toString());
    } catch (e) {}
  },
  PortalBridgeReportBest: function (slugPtr, score) {
    try {
      var key = "maccabi-netanya-best-" + UTF8ToString(slugPtr);
      if (score > parseInt(localStorage.getItem(key) || "0", 10)) localStorage.setItem(key, score.toString());
    } catch (e) {}
  },
  PortalBridgeMarkPlayed: function (slugPtr) {
    try {
      var slug = UTF8ToString(slugPtr);
      localStorage.setItem("maccabi-netanya-played-" + slug, "1");
      localStorage.setItem("maccabi-netanya-last", slug);
    } catch (e) {}
  },
  PortalBridgeHaptic: function (ms) {
    try { if (navigator.vibrate) navigator.vibrate(ms); } catch (e) {}
  }
});
