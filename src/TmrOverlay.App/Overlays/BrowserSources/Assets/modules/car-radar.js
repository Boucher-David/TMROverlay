ensureRadarStyle();
let carRadarDisplayModel = null;
let displayedRenderModel = null;
let fadeClearTimer = null;

TmrBrowserOverlay.register({
  async beforeRefresh() {
    carRadarDisplayModel = await fetchOverlayModel('car-radar');
  },
  render() {
    if (carRadarDisplayModel?.shouldRender === false) {
      modelRootOpacity = rootOpacityFromModel(carRadarDisplayModel);
      applyOverlayOpacity(0);
      clearRadarSurface();
      renderHeaderItems(carRadarDisplayModel, '');
      clearFooterSource();
      return;
    }

    const renderModel = carRadarDisplayModel?.carRadar?.renderModel || null;
    if (!isRenderableModel(renderModel)) {
      modelRootOpacity = rootOpacityFromModel(carRadarDisplayModel);
      applyOverlayOpacity(1);
      clearRadarSurface();
      renderHeaderItems(carRadarDisplayModel, carRadarDisplayModel?.status || 'waiting');
      renderFooterSource(carRadarDisplayModel);
      return;
    }

    modelRootOpacity = rootOpacityFromModel(carRadarDisplayModel);
    applyOverlayOpacity(1);
    if (renderModel.shouldRender) {
      displayedRenderModel = renderModel;
      clearFadeTimer();
      renderRadarSurface(renderModel);
    } else if (displayedRenderModel) {
      fadeOutRadarSurface(renderModel);
    } else {
      clearRadarSurface();
    }

    renderHeaderItems(carRadarDisplayModel, carRadarDisplayModel?.status || 'live');
    renderFooterSource(carRadarDisplayModel);
  },
  renderOffline() {
    clearRadarSurface();
    renderHeaderItems(null, '');
    clearFooterSource();
  }
});

function ensureRadarStyle() {
  if (document.getElementById('car-radar-render-model-style')) {
    return;
  }

  const style = document.createElement('style');
  style.id = 'car-radar-render-model-style';
  style.textContent = `
    .radar-v2 {
      width: min(var(--radar-width), 100vmin);
      aspect-ratio: var(--radar-aspect);
      display: block;
      overflow: visible;
      opacity: var(--radar-opacity);
      transition-property: opacity;
      transition-duration: var(--radar-fade-duration);
      transition-timing-function: linear;
    }

    .radar-v2 svg {
      display: block;
      width: 100%;
      height: 100%;
      overflow: visible;
    }

    .radar-label {
      font-family: "Segoe UI", Arial, sans-serif;
      letter-spacing: 0;
      dominant-baseline: middle;
      pointer-events: none;
      user-select: none;
    }
  `;
  document.head.appendChild(style);
}

function radarMarkup(renderModel, opacity, transitionModel = renderModel) {
  const width = renderModel.width;
  const height = renderModel.height;
  const duration = opacity > 0
    ? transitionModel.fadeInMilliseconds
    : transitionModel.fadeOutMilliseconds;
  return `
    <div class="radar-v2" aria-label="Car Radar" style="--radar-width:${num(width)}px;--radar-aspect:${num(width)} / ${num(height)};--radar-opacity:${num(opacity)};--radar-fade-duration:${num(duration)}ms;">
      <svg viewBox="0 0 ${formatNumber(width, 3)} ${formatNumber(height, 3)}" role="img">
        ${circleMarkup(renderModel.background)}
        ${(renderModel.multiclassArc ? [arcMarkup(renderModel.multiclassArc)] : []).join('')}
        ${(renderModel.rings || []).map(circleMarkup).join('')}
        ${(renderModel.cars || []).map(rectMarkup).join('')}
        ${(renderModel.labels || []).map(textMarkup).join('')}
      </svg>
    </div>`;
}

function renderRadarSurface(renderModel) {
  const hasExistingSurface = contentEl.querySelector('.radar-v2') !== null;
  contentEl.innerHTML = radarMarkup(renderModel, hasExistingSurface ? 1 : 0);
  const surface = contentEl.querySelector('.radar-v2');
  if (!surface) {
    return;
  }

  if (hasExistingSurface) {
    setRadarOpacity(surface, 1, renderModel);
    return;
  }

  window.requestAnimationFrame(() => setRadarOpacity(surface, 1, renderModel));
}

function fadeOutRadarSurface(renderModel) {
  let surface = contentEl.querySelector('.radar-v2');
  if (!surface) {
    contentEl.innerHTML = radarMarkup(displayedRenderModel, 1, renderModel);
    surface = contentEl.querySelector('.radar-v2');
  }

  if (surface) {
    window.requestAnimationFrame(() => setRadarOpacity(surface, 0, renderModel));
  }

  scheduleRadarClear(renderModel);
}

function setRadarOpacity(surface, opacity, transitionModel) {
  const duration = opacity > 0
    ? transitionModel.fadeInMilliseconds
    : transitionModel.fadeOutMilliseconds;
  surface.style.setProperty('--radar-opacity', num(opacity));
  surface.style.setProperty('--radar-fade-duration', `${num(duration)}ms`);
}

function isRenderableModel(renderModel) {
  return renderModel
    && Number.isFinite(renderModel.width)
    && renderModel.width > 0
    && Number.isFinite(renderModel.height)
    && renderModel.height > 0;
}

function scheduleRadarClear(renderModel) {
  clearFadeTimer();
  const duration = Math.max(0, Number.isFinite(renderModel.fadeOutMilliseconds) ? renderModel.fadeOutMilliseconds : 0);
  fadeClearTimer = window.setTimeout(() => {
    if (carRadarDisplayModel?.carRadar?.renderModel?.shouldRender) {
      return;
    }

    clearRadarSurface();
  }, duration);
}

function clearFadeTimer() {
  if (fadeClearTimer !== null) {
    window.clearTimeout(fadeClearTimer);
    fadeClearTimer = null;
  }
}

function clearRadarSurface() {
  clearFadeTimer();
  displayedRenderModel = null;
  contentEl.innerHTML = '';
}

function circleMarkup(circle) {
  if (!circle) return '';
  return `<ellipse cx="${num(circle.x + circle.width / 2)}" cy="${num(circle.y + circle.height / 2)}" rx="${num(circle.width / 2)}" ry="${num(circle.height / 2)}"${paintAttrs(circle)}></ellipse>`;
}

function rectMarkup(rect) {
  if (!rect) return '';
  const classes = ['radar-car-shape', rect.kind ? `radar-car-${escapeAttribute(rect.kind)}` : ''].filter(Boolean).join(' ');
  return `<rect class="${classes}" x="${num(rect.x)}" y="${num(rect.y)}" width="${num(rect.width)}" height="${num(rect.height)}" rx="${num(rect.radius)}" ry="${num(rect.radius)}"${paintAttrs(rect)}></rect>`;
}

function arcMarkup(arc) {
  if (!arc) return '';
  const path = arcPath(arc);
  return `<path d="${path}" fill="none" stroke="${colorCss(arc.stroke)}" stroke-width="${num(arc.strokeWidth)}" stroke-linecap="round"></path>`;
}

function textMarkup(label) {
  if (!label) return '';
  const anchor = label.alignment === 'center'
    ? 'middle'
    : label.alignment === 'far'
      ? 'end'
      : 'start';
  const x = label.alignment === 'center'
    ? label.x + label.width / 2
    : label.alignment === 'far'
      ? label.x + label.width
      : label.x;
  return `<text class="radar-label" x="${num(x)}" y="${num(label.y + label.height / 2)}" fill="${colorCss(label.color)}" font-size="${num(label.fontSize)}" font-weight="${label.bold ? 800 : 400}" text-anchor="${anchor}">${escapeHtml(label.text || '')}</text>`;
}

function paintAttrs(shape) {
  const attrs = [];
  attrs.push(` fill="${shape.fill ? colorCss(shape.fill) : 'none'}"`);
  if (shape.stroke) {
    attrs.push(` stroke="${colorCss(shape.stroke)}"`);
    attrs.push(` stroke-width="${num(shape.strokeWidth || 1)}"`);
  }
  return attrs.join('');
}

function arcPath(arc) {
  const centerX = arc.x + arc.width / 2;
  const centerY = arc.y + arc.height / 2;
  const radiusX = arc.width / 2;
  const radiusY = arc.height / 2;
  const start = polarPoint(centerX, centerY, radiusX, radiusY, arc.startDegrees);
  const end = polarPoint(centerX, centerY, radiusX, radiusY, arc.startDegrees + arc.sweepDegrees);
  const largeArc = Math.abs(arc.sweepDegrees) > 180 ? 1 : 0;
  const sweep = arc.sweepDegrees >= 0 ? 1 : 0;
  return `M ${num(start.x)} ${num(start.y)} A ${num(radiusX)} ${num(radiusY)} 0 ${largeArc} ${sweep} ${num(end.x)} ${num(end.y)}`;
}

function polarPoint(centerX, centerY, radiusX, radiusY, degrees) {
  const radians = degrees * Math.PI / 180;
  return {
    x: centerX + Math.cos(radians) * radiusX,
    y: centerY + Math.sin(radians) * radiusY
  };
}

function colorCss(color) {
  if (!color) return 'transparent';
  const alpha = Number.isFinite(color.alpha) ? Math.max(0, Math.min(255, color.alpha)) / 255 : 1;
  return `rgba(${clampByte(color.red)}, ${clampByte(color.green)}, ${clampByte(color.blue)}, ${Number(alpha.toFixed(3))})`;
}

function clampByte(value) {
  return Math.max(0, Math.min(255, Math.round(Number.isFinite(value) ? value : 0)));
}

function num(value) {
  return formatNumber(Number(value), 3);
}
