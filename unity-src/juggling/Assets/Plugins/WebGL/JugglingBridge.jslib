// Publishes the ball's position to the page as window.__juggling, converted from
// Unity's screen pixels (origin bottom-left, drawing-buffer pixels) to CSS pixels
// from the canvas's top-left - so a browser test can find the ball and tap it.
mergeInto(LibraryManager.library, {
  JugglingReport: function (x, y, hitRadius, state, score, combo) {
    var canvas = Module.canvas || document.querySelector("#unity-canvas");
    if (!canvas || !canvas.width || !canvas.height) return;
    var sx = canvas.clientWidth / canvas.width;
    var sy = canvas.clientHeight / canvas.height;
    window.__juggling = {
      x: x * sx,
      y: (canvas.height - y) * sy,
      r: hitRadius * sx,
      state: ["menu", "ready", "playing", "dropped"][state] || state,
      score: score,
      combo: combo
    };
  }
});
