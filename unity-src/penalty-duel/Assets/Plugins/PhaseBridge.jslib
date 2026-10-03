// Lets the web page (and the browser tests) see where the duel is: Menu, Shoot,
// Pass, PassShown, Save, Kick or Over, in window.penaltyDuelPhase.
mergeInto(LibraryManager.library, {
  ReportPhase: function (phase) {
    window.penaltyDuelPhase = UTF8ToString(phase);
  }
});
