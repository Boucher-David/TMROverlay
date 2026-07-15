let trackMapDisplayModel = null;

TmrBrowserOverlay.register({
  async beforeRefresh() {
    trackMapDisplayModel = await fetchOverlayModel('track-map');
  },
  render() {
    renderTrackMap(trackMapDisplayModel);
  },
  renderOffline() {
    // A failed refresh must never leave the last successful map visible.
    // Reuse the normal hidden state so content, chrome, and opacity clear
    // together for browser and OBS consumers.
    trackMapDisplayModel = null;
    renderTrackMap({
      overlayId: 'track-map',
      status: 'waiting for track map',
      shouldRender: false,
      rootOpacity: 1
    });
  }
});

function renderTrackMap(model) {
  if (model?.shouldRender === false) {
    postBrowserSourceEvent('model-hidden', model);
    modelRootOpacity = rootOpacityFromModel(model);
    applyOverlayOpacity(0);
    contentEl.innerHTML = '';
    clearHeaderItems();
    clearFooterSource();
    return;
  }

  postBrowserSourceEvent(model ? 'model-render' : 'model-null', model);
  modelRootOpacity = rootOpacityFromModel(model);
  applyOverlayOpacity(1);
  const renderModel = model?.trackMap?.renderModel;
  contentEl.innerHTML = `
    <div class="track">
      ${trackMapSvg(renderModel)}
    </div>`;
  renderHeaderItems(model, model?.status || 'waiting for position');
  renderFooterSource(model);
}

function trackMapSvg(renderModel) {
  if (!renderModel) {
    return '';
  }

  const width = finiteOr(renderModel.width, 360);
  const height = finiteOr(renderModel.height, 360);
  const primitives = (renderModel.primitives || []).map(primitiveMarkup).join('');
  const markers = (renderModel.markers || []).map(markerMarkup).join('');
  return `
    <svg viewBox="0 0 ${width} ${height}" role="img" aria-label="Track map">
      ${primitives}
      ${markers}
    </svg>`;
}

function primitiveMarkup(primitive) {
  switch (primitive?.kind) {
    case 'ellipse':
      return ellipseMarkup(primitive);
    case 'arc':
      return arcMarkup(primitive);
    case 'line':
      return lineMarkup(primitive);
    default:
      return pathMarkup(primitive);
  }
}

function pathMarkup(primitive) {
  const points = primitive?.points || [];
  if (points.length < 2) return '';
  const commands = points.map((point, index) => `${index === 0 ? 'M' : 'L'}${number(point.x)} ${number(point.y)}`);
  const d = `${commands.join(' ')}${primitive.closed ? ' Z' : ''}`;
  return `<path d="${d}" ${paintAttributes(primitive)} stroke-linecap="round" stroke-linejoin="round"></path>`;
}

function ellipseMarkup(primitive) {
  const rect = primitive?.rect;
  if (!rect) return '';
  const cx = finiteOr(rect.x, 0) + finiteOr(rect.width, 0) / 2;
  const cy = finiteOr(rect.y, 0) + finiteOr(rect.height, 0) / 2;
  const rx = finiteOr(rect.width, 0) / 2;
  const ry = finiteOr(rect.height, 0) / 2;
  return `<ellipse cx="${number(cx)}" cy="${number(cy)}" rx="${number(rx)}" ry="${number(ry)}" ${paintAttributes(primitive)} stroke-linecap="round"></ellipse>`;
}

function arcMarkup(primitive) {
  const rect = primitive?.rect;
  const stroke = colorCss(primitive?.stroke);
  if (!rect || !stroke) return '';
  const start = pointOnArc(rect, primitive.startDegrees);
  const end = pointOnArc(rect, finiteOr(primitive.startDegrees, 0) + finiteOr(primitive.sweepDegrees, 0));
  const largeArc = Math.abs(finiteOr(primitive.sweepDegrees, 0)) > 180 ? 1 : 0;
  const sweepFlag = finiteOr(primitive.sweepDegrees, 0) >= 0 ? 1 : 0;
  const d = `M${number(start.x)} ${number(start.y)} A${number(finiteOr(rect.width, 0) / 2)} ${number(finiteOr(rect.height, 0) / 2)} 0 ${largeArc} ${sweepFlag} ${number(end.x)} ${number(end.y)}`;
  return `<path d="${d}" fill="none" stroke="${stroke}" stroke-width="${number(primitive.strokeWidth)}" stroke-linecap="round"></path>`;
}

function lineMarkup(primitive) {
  const points = primitive?.points || [];
  const stroke = colorCss(primitive?.stroke);
  if (points.length < 2 || !stroke) return '';
  return `<line x1="${number(points[0].x)}" y1="${number(points[0].y)}" x2="${number(points[1].x)}" y2="${number(points[1].y)}" stroke="${stroke}" stroke-width="${number(primitive.strokeWidth)}" stroke-linecap="round"></line>`;
}

function markerMarkup(marker) {
  const fill = colorCss(marker?.fill);
  const stroke = colorCss(marker?.stroke);
  if (!fill || !stroke) return '';
  const alertRing = alertRingMarkup(marker);
  const circle = `<circle cx="${number(marker.x)}" cy="${number(marker.y)}" r="${number(marker.radius)}" fill="${fill}" stroke="${stroke}" stroke-width="${number(marker.strokeWidth)}"></circle>`;
  if (!marker.label) {
    return `${alertRing}${circle}`;
  }

  return `
    <g>
      ${alertRing}
      ${circle}
      <text x="${number(marker.x)}" y="${number(finiteOr(marker.y, 0) + finiteOr(marker.labelFontSize, 7.6) * 0.38)}" text-anchor="middle" font-size="${number(marker.labelFontSize)}" font-weight="800" fill="${colorCss(marker.labelColor) || 'rgba(5,13,17,1)'}">${escapeHtml(marker.label)}</text>
    </g>`;
}

function alertRingMarkup(marker) {
  const stroke = colorCss(marker?.alertRingStroke);
  const radius = finiteOr(marker?.alertRingRadius, 0);
  const strokeWidth = finiteOr(marker?.alertRingStrokeWidth, 0);
  if (!stroke || radius <= 0 || strokeWidth <= 0) return '';
  return `<circle cx="${number(marker.x)}" cy="${number(marker.y)}" r="${number(radius)}" fill="none" stroke="${stroke}" stroke-width="${number(strokeWidth)}"></circle>`;
}

function paintAttributes(primitive) {
  const fill = colorCss(primitive?.fill);
  const stroke = colorCss(primitive?.stroke);
  const strokeWidth = finiteOr(primitive?.strokeWidth, 0);
  return [
    `fill="${fill || 'none'}"`,
    stroke ? `stroke="${stroke}"` : 'stroke="none"',
    stroke && strokeWidth > 0 ? `stroke-width="${number(strokeWidth)}"` : ''
  ].filter(Boolean).join(' ');
}

function pointOnArc(rect, degrees) {
  const radians = finiteOr(degrees, 0) * Math.PI / 180;
  return {
    x: finiteOr(rect.x, 0) + finiteOr(rect.width, 0) / 2 + Math.cos(radians) * finiteOr(rect.width, 0) / 2,
    y: finiteOr(rect.y, 0) + finiteOr(rect.height, 0) / 2 + Math.sin(radians) * finiteOr(rect.height, 0) / 2
  };
}

function colorCss(color) {
  if (!color) return null;
  const red = Math.max(0, Math.min(255, Math.round(finiteOr(color.red, 0))));
  const green = Math.max(0, Math.min(255, Math.round(finiteOr(color.green, 0))));
  const blue = Math.max(0, Math.min(255, Math.round(finiteOr(color.blue, 0))));
  const alpha = Math.max(0, Math.min(1, finiteOr(color.alpha, 255) / 255));
  return `rgba(${red},${green},${blue},${alpha.toFixed(3)})`;
}

function finiteOr(value, fallback) {
  return Number.isFinite(Number(value)) ? Number(value) : fallback;
}

function number(value) {
  return finiteOr(value, 0).toFixed(1).replace(/\.0$/, '');
}
