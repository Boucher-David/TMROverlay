let inputDisplayModel = null;
let inputTrace = [];
ensureInputStyle();

TmrBrowserOverlay.register({
  async beforeRefresh() {
    inputDisplayModel = await fetchOverlayModel('input-state');
  },
  render() {
    const model = inputDisplayModel;
    const inputs = model?.inputs || {};
    inputTrace = Array.isArray(inputs.trace) ? inputs.trace : [];
    const hasGraph = inputGraphEnabled(inputs);
    const railEnabled = inputRailEnabled(inputs);
    const hasContent = inputs.hasContent ?? (hasGraph || railEnabled);
    applyInputOverlayLayoutClasses(hasGraph, railEnabled, hasContent);
    modelRootOpacity = rootOpacityFromModel(model);
    if (!inputs.isAvailable || !hasContent) {
      applyOverlayOpacity(0);
      contentEl.innerHTML = `<div class="empty">${escapeHtml(model?.status || 'Waiting for player in car.')}</div>`;
      renderHeaderItems(model, '');
      renderFooterSource(model);
      return;
    }

    applyOverlayOpacity(1);
    const brakeAbsActive = inputs.brakeAbsActive === true;
    const layoutClass = [
      'input-layout',
      hasGraph ? '' : 'rail-only',
      railEnabled ? '' : 'no-rail'
    ].filter(Boolean).join(' ');
    contentEl.innerHTML = `
      <div class="${layoutClass}">
        ${hasGraph ? `
          <div class="input-graph-panel">
            <canvas class="input-graph" aria-label="Input trace graph"></canvas>
          </div>` : ''}
        ${railEnabled ? renderInputRail(inputs, brakeAbsActive) : ''}
      </div>`;
    if (hasGraph) {
      drawInputGraph(contentEl.querySelector('.input-graph'), inputs);
    }
    renderHeaderItems(model, '');
    renderFooterSource(model);
  }
});

function ensureInputStyle() {
  if (document.getElementById('input-state-browser-style')) {
    return;
  }

  const style = document.createElement('style');
  style.id = 'input-state-browser-style';
  style.textContent = `
    body.input-state-page .overlay {
      width: min(520px, calc(100vw - 16px));
      height: min(260px, calc(100vh - 16px));
      min-width: 0;
      max-width: calc(100vw - 16px);
    }

    body.input-state-page .overlay.input-graph-only {
      width: min(380px, calc(100vw - 16px));
    }

    body.input-state-page .overlay.input-rail-only {
      width: min(276px, calc(100vw - 16px));
    }

    body.input-state-page .content {
      width: 100%;
      height: calc(100% - 38px);
      padding: 12px 16px 14px;
    }

    .input-layout {
      display: grid;
      grid-template-columns: minmax(160px, 1fr) minmax(136px, 40%);
      gap: 18px;
      width: 100%;
      height: 100%;
      min-width: 0;
    }

    .input-layout.no-rail {
      grid-template-columns: minmax(220px, 1fr);
    }

    .input-layout.rail-only {
      grid-template-columns: minmax(0, 1fr);
    }

    .input-graph-panel,
    .input-rail {
      min-width: 0;
      min-height: 0;
      border: 1px solid var(--tmr-border-muted);
      border-radius: 5px;
      background: var(--tmr-surface-inset);
    }

    .input-graph {
      display: block;
      width: 100%;
      height: 100%;
    }

    .input-rail {
      display: grid;
      grid-template-rows: auto minmax(0, 1fr) auto;
      gap: 8px;
      padding: 8px;
      overflow: hidden;
      background: var(--tmr-surface-raised);
    }

    .input-layout.rail-only .input-rail {
      width: 100%;
      justify-self: stretch;
    }

    .input-bars,
    .input-readouts {
      display: grid;
      gap: 7px;
    }

    .input-readouts {
      gap: 5px;
    }

    .input-bar,
    .input-readout {
      display: grid;
      grid-template-columns: 42px minmax(0, 1fr);
      column-gap: 6px;
      min-width: 0;
    }

    .input-bar {
      grid-template-rows: 15px 11px;
      min-height: 25px;
    }

    .input-readout {
      align-items: center;
      min-height: 20px;
    }

    .input-bar-label,
    .input-readout-label,
    .input-wheel-label {
      color: var(--tmr-text-muted);
      font-size: 9px;
      font-weight: 800;
      text-transform: uppercase;
      white-space: nowrap;
    }

    .input-bar-value,
    .input-readout-value,
    .input-wheel-value {
      color: var(--tmr-text);
      font-family: Consolas, "Courier New", monospace;
      font-size: 12px;
      font-weight: 800;
      white-space: nowrap;
    }

    .input-bar-track {
      align-self: center;
      position: relative;
      height: 12px;
      border-radius: 6px;
      background: var(--tmr-surface-inset);
      overflow: hidden;
    }

    .input-bar-track span {
      position: absolute;
      left: 0;
      top: 0;
      height: 100%;
      border-radius: inherit;
    }

    .input-bar-value {
      grid-column: 2;
      color: var(--tmr-text-muted);
      font-size: 9px;
      text-align: right;
    }

    .input-readout-value {
      text-align: right;
    }

    .input-wheel {
      display: grid;
      grid-template-columns: 1fr auto;
      grid-template-rows: 14px minmax(0, 1fr);
      column-gap: 8px;
      align-items: center;
      min-height: 0;
      overflow: hidden;
    }

    .input-wheel svg {
      grid-column: 1 / -1;
      grid-row: 2;
      align-self: center;
      justify-self: center;
      width: min(52px, 100%);
      height: min(52px, 100%);
      max-height: 100%;
    }
  `;
  document.head.appendChild(style);
}

function applyInputOverlayLayoutClasses(hasGraph, hasRail, hasContent) {
  overlayEl.classList.toggle('input-full', hasContent && hasGraph && hasRail);
  overlayEl.classList.toggle('input-graph-only', hasContent && hasGraph && !hasRail);
  overlayEl.classList.toggle('input-rail-only', hasContent && !hasGraph && hasRail);
  overlayEl.classList.toggle('input-empty', !hasContent);
}

function inputRailEnabled(inputs) {
  if (typeof inputs.hasRail === 'boolean') {
    return inputs.hasRail;
  }

  return inputs.showThrottle
    || inputs.showBrake
    || inputs.showClutch
    || inputs.showSteering
    || inputs.showGear
    || inputs.showSpeed;
}

function inputGraphEnabled(inputs) {
  if (typeof inputs.hasGraph === 'boolean') {
    return inputs.hasGraph;
  }

  return inputs.showThrottleTrace
    || inputs.showBrakeTrace
    || inputs.showClutchTrace;
}

function renderInputRail(inputs, brakeAbsActive) {
  const bars = [
    inputs.showThrottle ? railBar('THR', inputs.throttle, 'var(--tmr-green)') : '',
    inputs.showBrake ? railBar(brakeAbsActive ? 'ABS' : 'BRK', inputs.brake, brakeAbsActive ? 'var(--tmr-amber)' : 'var(--tmr-error)') : '',
    inputs.showClutch ? railBar('CLT', inputs.clutch, 'var(--tmr-cyan)') : ''
  ].filter(Boolean).join('');
  const readouts = [
    inputs.showGear ? railReadout('GEAR', inputs.gearText || formatGear(inputs.gear)) : '',
    inputs.showSpeed ? railReadout('SPD', inputs.speedText || '--') : ''
  ].filter(Boolean).join('');
  return `
    <div class="input-rail">
      ${bars ? `<div class="input-bars">${bars}</div>` : ''}
      ${inputs.showSteering ? renderWheel(inputs.steeringWheelAngle, inputs.steeringText) : ''}
      ${readouts ? `<div class="input-readouts">${readouts}</div>` : ''}
    </div>`;
}

function railReadout(label, value) {
  return `
    <div class="input-readout">
      <div class="input-readout-label">${escapeHtml(label)}</div>
      <div class="input-readout-value">${escapeHtml(value)}</div>
    </div>`;
}

function railBar(label, value, color) {
  const normalized = clamp01(value);
  return `
    <div class="input-bar">
      <div class="input-bar-label">${escapeHtml(label)}</div>
      <div class="input-bar-track"><span style="width:${(normalized * 100).toFixed(0)}%; background:${color};"></span></div>
      <div class="input-bar-value">${formatPercent(Number.isFinite(value) ? normalized : null)}</div>
    </div>`;
}

function renderWheel(angleRadians, angleText) {
  const angleDegrees = Number.isFinite(angleRadians) ? angleRadians * 180 / Math.PI : null;
  const displayDegrees = Number.isFinite(angleDegrees) ? angleDegrees : 0;
  return `
    <div class="input-wheel">
      <div class="input-wheel-label">Wheel</div>
      <div class="input-wheel-value">${escapeHtml(angleText || (Number.isFinite(angleDegrees) ? `${formatSignedDegrees(angleDegrees)} deg` : '--'))}</div>
      <svg viewBox="0 0 64 64" aria-hidden="true">
        <circle cx="32" cy="32" r="25" fill="none" stroke="var(--tmr-text-secondary)" stroke-width="4"></circle>
        <g transform="rotate(${escapeAttribute(displayDegrees)} 32 32)" stroke="var(--tmr-cyan)" stroke-width="3" stroke-linecap="round">
          <line x1="32" y1="32" x2="32" y2="12"></line>
          <line x1="32" y1="32" x2="49" y2="43"></line>
          <line x1="32" y1="32" x2="15" y2="43"></line>
        </g>
      </svg>
    </div>`;
}

function drawInputGraph(canvas, inputs) {
  if (!canvas) {
    return;
  }

  const rect = canvas.getBoundingClientRect();
  const width = Math.max(1, rect.width);
  const height = Math.max(1, rect.height);
  const dpr = window.devicePixelRatio || 1;
  canvas.width = Math.round(width * dpr);
  canvas.height = Math.round(height * dpr);

  const ctx = canvas.getContext('2d');
  ctx.setTransform(dpr, 0, 0, dpr, 0, 0);
  ctx.clearRect(0, 0, width, height);
  ctx.strokeStyle = themeRgba('--tmr-text-muted-rgb', 0.18, 'rgba(140, 174, 212, 0.18)');
  ctx.lineWidth = 1;
  for (let step = 1; step < 4; step += 1) {
    const y = Math.round(height * step / 4) + 0.5;
    ctx.beginPath();
    ctx.moveTo(0, y);
    ctx.lineTo(width, y);
    ctx.stroke();
  }

  const series = inputGraphSeries(inputs);
  for (const item of series) {
    drawTraceLine(ctx, width, height, item.key, item.color, item.lineWidth);
  }
  if (inputs.showBrakeTrace) {
    drawAbsSegments(ctx, width, height);
  }

  if (inputTrace.length < 2 && !inputs.isAvailable) {
    ctx.fillStyle = themeColor('--tmr-text-muted', '#8caed4');
    ctx.font = '700 13px "Segoe UI", Arial, sans-serif';
    ctx.textAlign = 'center';
    ctx.textBaseline = 'middle';
    ctx.fillText('waiting for inputs', width / 2, height / 2);
  }
}

function drawTraceLine(ctx, width, height, key, color, lineWidth) {
  if (inputTrace.length < 2) {
    return;
  }

  ctx.strokeStyle = color;
  ctx.lineWidth = lineWidth;
  ctx.lineJoin = 'round';
  ctx.lineCap = 'round';
  const points = inputTrace.map((point, index) => ({
    x: xForTracePoint(index, width),
    y: yForTraceValue(point[key], height)
  }));
  ctx.save();
  ctx.beginPath();
  ctx.rect(0, 0, width, height);
  ctx.clip();
  ctx.beginPath();
  drawSmoothTracePath(ctx, points);
  ctx.stroke();
  ctx.restore();
}

function drawSmoothTracePath(ctx, points) {
  if (!Array.isArray(points) || points.length === 0) {
    return;
  }

  ctx.moveTo(points[0].x, points[0].y);
  for (let index = 0; index < points.length - 1; index += 1) {
    const p0 = index === 0 ? points[index] : points[index - 1];
    const p1 = points[index];
    const p2 = points[index + 1];
    const p3 = index + 2 < points.length ? points[index + 2] : p2;
    const control1 = {
      x: p1.x + (p2.x - p0.x) / 6,
      y: clampSmoothTraceControlY(p1.y + (p2.y - p0.y) / 6, p1.y, p2.y)
    };
    const control2 = {
      x: p2.x - (p3.x - p1.x) / 6,
      y: clampSmoothTraceControlY(p2.y - (p3.y - p1.y) / 6, p1.y, p2.y)
    };
    ctx.bezierCurveTo(control1.x, control1.y, control2.x, control2.y, p2.x, p2.y);
  }
}

function clampSmoothTraceControlY(value, segmentStartY, segmentEndY) {
  const min = Math.min(segmentStartY, segmentEndY);
  const max = Math.max(segmentStartY, segmentEndY);
  return Math.max(min, Math.min(max, value));
}

function drawAbsSegments(ctx, width, height) {
  if (inputTrace.length < 2) {
    return;
  }

  ctx.strokeStyle = themeColor('--tmr-amber', '#ffd15b');
  ctx.lineWidth = 3;
  ctx.lineCap = 'round';
  for (let index = 1; index < inputTrace.length; index += 1) {
    if (inputTrace[index].brakeAbsActive !== true) {
      continue;
    }

    ctx.beginPath();
    ctx.moveTo(xForTracePoint(index - 1, width), yForTraceValue(inputTrace[index - 1].brake, height));
    ctx.lineTo(xForTracePoint(index, width), yForTraceValue(inputTrace[index].brake, height));
    ctx.stroke();
  }
}

function inputGraphSeries(inputs) {
  return [
    inputs.showThrottleTrace ? { key: 'throttle', label: 'Throttle', color: themeColor('--tmr-green', '#62ff9f'), lineWidth: 2 } : null,
    inputs.showBrakeTrace ? { key: 'brake', label: 'Brake', color: themeColor('--tmr-error', '#ff6274'), lineWidth: 2 } : null,
    inputs.showClutchTrace ? { key: 'clutch', label: 'Clutch', color: themeColor('--tmr-cyan', '#00e8ff'), lineWidth: 2 } : null
  ].filter(Boolean);
}

function xForTracePoint(index, width) {
  return inputTrace.length <= 1 ? 0 : index / (inputTrace.length - 1) * width;
}

function yForTraceValue(value, height) {
  return height - clamp01(value) * height;
}

function clamp01(value) {
  return Number.isFinite(value) ? Math.max(0, Math.min(1, value)) : 0;
}

function formatGear(value) {
  return value === -1 ? 'R' : value === 0 ? 'N' : Number.isFinite(value) ? String(value) : '--';
}

function formatSignedDegrees(value) {
  const rounded = Math.round(value);
  if (rounded === 0) {
    return '0';
  }

  return rounded > 0 ? `+${rounded}` : String(rounded);
}
