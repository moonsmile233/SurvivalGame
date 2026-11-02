// Canvas click points for the v1.2 Unity menu, derived from the layout anchors in
// Assets/Editor/SceneSetup.cs (do not eyeball these from screenshots).
//
// CanvasScaler: ScaleWithScreenSize, reference 1280x720, or 640x360 when Screen.width < 1000
// (HudCanvasScaler), matchWidthOrHeight = 0.5:
//     scale = sqrt((screenW / refW) * (screenH / refH))
// Percentage-anchored bands therefore keep the same fractional centre at every viewport;
// fixed-size elements (HUD pause button) are converted through canvas units.
//
// Keep this table in sync with SceneSetup.CreateHud when anchors change.

const DESKTOP = { refW: 1280, refH: 720 };
const MOBILE = { refW: 640, refH: 360 };

export function canvasMetrics(screenWidth, screenHeight) {
  const reference = screenWidth < 1000 ? MOBILE : DESKTOP;
  const scale = Math.sqrt((screenWidth / reference.refW) * (screenHeight / reference.refH));
  return {
    ...reference,
    scale,
    unitsW: screenWidth / scale,
    unitsH: screenHeight / scale,
  };
}

/** Horizontal band (anchorMin.x=xMin, anchorMax.x=xMax, pivot 0.5, y anchored at yAnchor). */
function band(xMin, xMax, yAnchor) {
  return { fx: (xMin + xMax) / 2, fy: 1 - yAnchor };
}

/** SetAnchored(top-right, pivot top-right, anchoredPosition (-16,-12), size 140x80). */
function hudPauseButton(metrics) {
  const centerX = metrics.unitsW - 16 - 140 / 2;
  const centerY = metrics.unitsH - 12 - 80 / 2;
  return { fx: centerX / metrics.unitsW, fy: 1 - centerY / metrics.unitsH };
}

export function getLayout(screenWidth, screenHeight) {
  const metrics = canvasMetrics(screenWidth, screenHeight);

  // DifficultyCards row: x 0.12..0.88, y 0.36..0.62; each card is one third with a 1% inset.
  const cards = [0, 1, 2].map((index) => {
    const xMin = index / 3 + 0.01;
    const xMax = (index + 1) / 3 - 0.01;
    const cardCenter = (xMin + xMax) / 2;
    return {
      fx: 0.12 + 0.76 * cardCenter,
      fy: 1 - (0.36 + 0.62) / 2,
    };
  });

  return {
    metrics,
    menuStart: band(0.27, 0.73, 0.2),
    menuCards: cards,
    hudPause: hudPauseButton(metrics),
    pauseResume: band(0.055, 0.348, 0.3),
    pauseRestart: band(0.351, 0.649, 0.3),
    pauseMenu: band(0.652, 0.945, 0.3),
    resultAgain: band(0.16, 0.48, 0.23),
    resultMenu: band(0.52, 0.84, 0.23),
  };
}
