    const page = {{PAGE_JSON}};
    const overlayEl = document.querySelector('.overlay');
    const statusEl = document.getElementById('status');
    const timeRemainingEl = document.getElementById('time-remaining');
    const contentEl = document.getElementById('content');
    const sourceEl = document.getElementById('source');
    let modelRootOpacity = 1;
    const browserOverlay = {
      module: null,
      register(module) {
        this.module = module;
      }
    };
    window.TmrBrowserOverlay = browserOverlay;

    function apiPath(path) {
      const url = new URL(path, window.location.href);
      const currentParams = new URLSearchParams(window.location.search);
      const forwardedQueryParameters = Array.isArray(page.forwardQueryParameters)
        ? page.forwardQueryParameters
        : [];
      for (const replayParam of forwardedQueryParameters) {
        const value = currentParams.get(replayParam);
        if (value !== null) {
          url.searchParams.set(replayParam, value);
        }
      }

      return `${url.pathname}${url.search}`;
    }

    window.TmrBrowserApiPath = apiPath;

    const formatSeconds = (value) => {
      if (!Number.isFinite(value)) return '--';
      const sign = value > 0 ? '+' : value < 0 ? '-' : '';
      const absolute = Math.abs(value);
      if (absolute >= 60) {
        const minutes = Math.floor(absolute / 60);
        const seconds = absolute - minutes * 60;
        return `${sign}${minutes}:${seconds.toFixed(1).padStart(4, '0')}`;
      }
      return `${sign}${absolute.toFixed(1)}`;
    };

    const formatNumber = (value, digits = 1) => Number.isFinite(value) ? value.toFixed(digits) : '--';
    const formatPercent = (value) => Number.isFinite(value) ? `${Math.round(value * 100)}%` : '--';
    const formatSpeed = (value) => Number.isFinite(value) ? `${Math.round(value * 3.6)} km/h` : '--';
    const escapeHtml = (value) => String(value ?? '').replace(/[&<>"']/g, (char) => ({
      '&': '&amp;',
      '<': '&lt;',
      '>': '&gt;',
      '"': '&quot;',
      "'": '&#39;'
    })[char]);
    const escapeAttribute = escapeHtml;

    const driverName = (row) => row?.driverName || row?.teamName || `Car ${row?.carIdx ?? '--'}`;
    const carNumber = (row) => row?.carNumber ? `#${String(row.carNumber).replace(/^#/, '')}` : `#${row?.carIdx ?? '--'}`;
    const themeColor = (name, fallback) => getComputedStyle(document.documentElement).getPropertyValue(name).trim() || fallback;
    const themeRgba = (name, alpha, fallback) => {
      const rgb = getComputedStyle(document.documentElement).getPropertyValue(name).trim();
      return rgb ? `rgba(${rgb}, ${alpha})` : fallback;
    };
    function roundedRectPath(ctx, left, top, width, height, radius) {
      const r = Math.max(0, Math.min(radius, width / 2, height / 2));
      ctx.beginPath();
      ctx.moveTo(left + r, top);
      ctx.lineTo(left + width - r, top);
      ctx.quadraticCurveTo(left + width, top, left + width, top + r);
      ctx.lineTo(left + width, top + height - r);
      ctx.quadraticCurveTo(left + width, top + height, left + width - r, top + height);
      ctx.lineTo(left + r, top + height);
      ctx.quadraticCurveTo(left, top + height, left, top + height - r);
      ctx.lineTo(left, top + r);
      ctx.quadraticCurveTo(left, top, left + r, top);
      ctx.closePath();
    }
    function drawRoundedRect(ctx, left, top, width, height, radius, fillStyle, strokeStyle = null) {
      roundedRectPath(ctx, left, top, width, height, radius);
      if (fillStyle) {
        ctx.fillStyle = fillStyle;
        ctx.fill();
      }
      if (strokeStyle) {
        ctx.strokeStyle = strokeStyle;
        ctx.stroke();
      }
    }
    const parsedHexColors = new Map();
    const classHeaderStyles = new Map();

    function clamp01(value, fallback = 1) {
      const numeric = Number(value);
      return Number.isFinite(numeric) ? Math.max(0, Math.min(1, numeric)) : fallback;
    }

    function rootOpacityFromModel(model) {
      return clamp01(model?.rootOpacity, 1);
    }

    function applyOverlayOpacity(visibilityAlpha = 1) {
      if (!overlayEl) return;
      overlayEl.style.opacity = String(clamp01(modelRootOpacity, 1) * clamp01(visibilityAlpha, 1));
    }

    function rowsTable(headers, rows) {
      if (!rows.length) {
        return '<div class="empty">Waiting for live rows.</div>';
      }
      const fixedWidth = headers.reduce((total, header) => total + columnWidth(header), 0);
      const useProportionalColumns = shouldScaleTableColumns() && fixedWidth > 0;
      const tableStyle = useProportionalColumns
        ? ' style="width:100%; min-width:0; table-layout:fixed;"'
        : fixedWidth > 0
        ? ` style="width:${fixedWidth}px; min-width:${fixedWidth}px; table-layout:fixed;"`
        : '';
      const colGroup = fixedWidth > 0
        ? `<colgroup>${headers.map((header) => `<col style="${columnWidthDeclaration(header, fixedWidth)}">`).join('')}</colgroup>`
        : '';
      const headerHtml = headers.map((header) => `<th${cellStyle(header, null, null, fixedWidth)}>${escapeHtml(header.label)}</th>`).join('');
      const rowHtml = rows.map((row) => {
        const classes = [
          row.isFocus || row.isReference || row.isReferenceCar ? 'focus' : '',
          (row.isPit || row.onPitRoad) && !(row.isFocus || row.isReference || row.isReferenceCar) ? 'pit' : '',
          row.isPendingGrid ? 'pending-grid' : '',
          row.isPartial ? 'partial' : '',
          row.isPlaceholder ? 'placeholder' : '',
          lapRelationshipClass(row),
          row.carClassColorHex && !isClassHeaderRow(row) ? 'class-colored' : '',
          isClassHeaderRow(row) ? 'class-header' : ''
        ].filter(Boolean).join(' ');
        if (isClassHeaderRow(row)) {
          return `<tr class="${classes}"${classHeaderStyle(row)}><td colspan="${Math.max(1, headers.length)}">${classHeaderContent(row)}</td></tr>`;
        }

        const cells = headers.map((header, columnIndex) => `<td${cellStyle(header, row, columnIndex, fixedWidth)}>${header.value(row)}</td>`).join('');
        return `<tr class="${classes}"${rowStyle(row)}>${cells}</tr>`;
      }).join('');
      return `<table${tableStyle}>${colGroup}<thead><tr>${headerHtml}</tr></thead><tbody>${rowHtml}</tbody></table>`;
    }

    function shouldScaleTableColumns() {
      return page?.id === 'relative';
    }

    function isClassHeaderRow(row) {
      return row?.rowKind === 'header' || row?.isClassHeader === true;
    }

    function lapRelationshipClass(row) {
      const delta = Number(row?.relativeLapDelta ?? row?.lapDeltaToReference);
      if (!Number.isFinite(delta) || delta === 0) return '';
      if (delta >= 2) return 'lap-ahead-2';
      if (delta === 1) return 'lap-ahead-1';
      if (delta === -1) return 'lap-behind-1';
      if (delta <= -2) return 'lap-behind-2';
      return '';
    }

    function classHeaderContent(row) {
      const title = escapeHtml(row?.headerTitle || row?.driverName || row?.className || row?.label || 'Class');
      const detail = row?.headerDetail
        ? escapeHtml(row.headerDetail)
        : [
          row?.rowCount ? `${row.rowCount} cars` : '',
          row?.estimatedLapsLabel || ''
        ].filter(Boolean).map(escapeHtml).join(' | ');
      return `
        <div class="class-header-band">
          <span class="class-header-title">${title}</span>
          <span class="class-header-detail">${detail}</span>
        </div>`;
    }

    function columnWidth(header) {
      const width = Number(header?.width);
      return Number.isFinite(width) && width > 0 ? Math.round(width) : 0;
    }

    function columnWidthDeclaration(header, totalWidth = 0) {
      const width = columnWidth(header);
      if (width <= 0) return '';
      if (shouldScaleTableColumns() && totalWidth > 0) {
        return `width:${(width / totalWidth * 100).toFixed(4)}%;`;
      }

      return `width:${width}px;`;
    }

    function cellStyle(header, row = null, columnIndex = null, totalWidth = 0) {
      const styles = [];
      const width = columnWidthDeclaration(header, totalWidth);
      if (width) styles.push(width);
      const align = ['left', 'right', 'center'].includes(header?.align) ? header.align : null;
      if (align) styles.push(`text-align:${align}`);
      if (header?.dataKey === 'driver') styles.push('padding-left:14px');
      const tone = row ? cellTone(row, columnIndex) : '';
      if (tone === 'best-lap') styles.push('color:var(--tmr-best-lap)');
      if (tone === 'personal-best') styles.push('color:var(--tmr-green)');
      return styles.length ? ` style="${styles.join(';')}"` : '';
    }

    function cellTone(row, columnIndex) {
      if (!Number.isInteger(columnIndex)) return '';
      const tones = Array.isArray(row?.cellTones) ? row.cellTones : [];
      return String(tones[columnIndex] || '').trim().toLowerCase();
    }

    function classHeaderStyle(row) {
      if (!isClassHeaderRow(row)) return '';
      return classColorStyle(row?.carClassColorHex);
    }

    function rowStyle(row) {
      if (isClassHeaderRow(row)) return classHeaderStyle(row);
      const color = parseHexColor(row?.carClassColorHex);
      return color
        ? ` style="--row-class-accent: #${color.key}; --row-class-bg: rgba(${color.r}, ${color.g}, ${color.b}, 0.13);"`
        : '';
    }

    function classColorStyle(value) {
      const color = parseHexColor(value);
      if (!color) return '';
      if (classHeaderStyles.has(color.key)) return classHeaderStyles.get(color.key);
      const style = ` style="--class-header-bg: rgba(${color.r}, ${color.g}, ${color.b}, 0.28); --class-header-fg: var(--tmr-text);"`;
      classHeaderStyles.set(color.key, style);
      return style;
    }

    function classColorCss(value, fallback = 'var(--tmr-text-secondary)') {
      const color = parseHexColor(value);
      return color ? `#${color.key}` : fallback;
    }

    function parseHexColor(value) {
      const match = /^(?:#|0x)?([0-9a-f]{6})$/i.exec(String(value || '').trim());
      if (!match) return null;
      const key = match[1].toUpperCase();
      if (parsedHexColors.has(key)) return parsedHexColors.get(key);
      const rgb = Number.parseInt(key, 16);
      const color = {
        key,
        r: (rgb >> 16) & 255,
        g: (rgb >> 8) & 255,
        b: rgb & 255
      };
      parsedHexColors.set(key, color);
      return color;
    }

    function metric(label, value) {
      return `<div class="metric"><div class="label">${escapeHtml(label)}</div><div class="value">${escapeHtml(value)}</div></div>`;
    }

    function toneClass(tone) {
      const normalized = String(tone || '').trim().toLowerCase();
      return ['live', 'modeled', 'normal', 'waiting', 'info', 'success', 'warning', 'error'].includes(normalized)
        ? normalized
        : 'normal';
    }

    function metricRow(row) {
      const tone = toneClass(row?.tone);
      const highlight = tone === 'info' ? ' highlight' : '';
      const segments = Array.isArray(row?.segments) ? row.segments : [];
      const hasSegments = segments.length > 0;
      const rowColor = metricColorStyle(row?.rowColorHex || row?.carClassColorHex);
      const valueHtml = hasSegments
        ? `<div class="value value-segments" style="--tmr-segment-count: ${Math.min(segments.length, 6)};">${segments.map(metricSegment).join('')}</div>`
        : `<div class="value">${escapeHtml(row?.value || '--')}</div>`;
      return `
        <div class="metric ${tone}${highlight}${hasSegments ? ' segmented' : ''}${rowColor ? ' class-colored' : ''}"${rowColor}>
          <div class="label">${escapeHtml(row?.label || '')}</div>
          ${valueHtml}
        </div>`;
    }

    function metricColorStyle(value) {
      const color = parseHexColor(value);
      return color
        ? ` style="--metric-accent: #${color.key}; --metric-bg: rgba(${color.r}, ${color.g}, ${color.b}, 0.13); --metric-border: rgba(${color.r}, ${color.g}, ${color.b}, 0.38);"`
        : '';
    }

    function metricSegment(segment) {
      const tone = toneClass(segment?.tone);
      const value = String(segment?.value || '--');
      const rotation = Number(segment?.rotationDegrees);
      const hasRotation = Number.isFinite(rotation);
      const colorStyle = segmentColorStyle(segment?.accentHex);
      const rotationStyle = hasRotation ? `--segment-rotation: ${rotation.toFixed(1)}deg;` : '';
      const style = colorStyle || rotationStyle ? ` style="${colorStyle}${rotationStyle}"` : '';
      const lengthClass = value.length > 14
        ? ' extra-long'
        : value.length > 11
          ? ' long'
          : '';
      const valueHtml = hasRotation
        ? `<span class="wind-arrow" aria-hidden="true"></span><span class="wind-label">${escapeHtml(value)}</span>`
        : escapeHtml(value);
      return `
        <div class="value-segment ${tone}${colorStyle ? ' custom-color' : ''}${hasRotation ? ' directional' : ''}${lengthClass}"${style}>
          <span class="segment-label">${escapeHtml(segment?.label || '')}</span>
          <span class="segment-value">${valueHtml}</span>
        </div>`;
    }

    function segmentColorStyle(value) {
      const color = parseHexColor(value);
      return color
        ? `--segment-accent: #${color.key}; --segment-accent-rgb: ${color.r}, ${color.g}, ${color.b};`
        : '';
    }

    function metricSection(section) {
      const rows = Array.isArray(section?.rows) ? section.rows : [];
      if (!rows.length) return '';
      return `
        <section class="metric-section">
          <div class="metric-section-title">${escapeHtml(section?.title || 'Details')}</div>
          <div class="metric-list">${rows.map(metricRow).join('')}</div>
        </section>`;
    }

    function gridSection(section) {
      const headers = Array.isArray(section?.headers) && section.headers.length
        ? section.headers
        : ['Info', 'FL', 'FR', 'RL', 'RR'];
      const rows = Array.isArray(section?.rows) ? section.rows : [];
      if (!rows.length) return '';
      const columns = `repeat(${Math.max(1, headers.length)}, minmax(44px, 1fr))`;
      const headerHtml = headers
        .map((header) => `<div class="tire-grid-header">${escapeHtml(header || '')}</div>`)
        .join('');
      const rowHtml = rows.map((row) => {
        const rowTone = toneClass(row?.tone);
        const cells = Array.isArray(row?.cells) ? row.cells : [];
        const cellHtml = cells
          .slice(0, Math.max(0, headers.length - 1))
          .map((cell) => `<div class="tire-grid-cell ${toneClass(cell?.tone)}">${escapeHtml(cell?.value || '--')}</div>`)
          .join('');
        return `
          <div class="tire-grid-row ${rowTone}">
            <div class="tire-grid-label">${escapeHtml(row?.label || '')}</div>
            ${cellHtml}
          </div>`;
      }).join('');
      return `
        <section class="metric-section">
          <div class="metric-section-title">${escapeHtml(section?.title || 'Details')}</div>
          <div class="tire-grid" style="--tmr-grid-columns: ${escapeHtml(columns)};">
            <div class="tire-grid-head">${headerHtml}</div>
            ${rowHtml}
          </div>
        </section>`;
    }

    async function fetchOverlayModel(overlayId) {
      const response = await fetch(apiPath(`/api/overlay-model/${encodeURIComponent(overlayId)}`), { cache: 'no-store' });
      if (!response.ok) throw new Error(`HTTP ${response.status}`);
      const payload = await response.json();
      return payload.model || null;
    }

    function renderOverlayModel(model) {
      if (!model) {
        contentEl.innerHTML = '<div class="empty">Waiting for overlay model.</div>';
        renderHeaderItems(null, 'waiting for model');
        clearFooterSource();
        return;
      }

      if (model.shouldRender === false) {
        modelRootOpacity = rootOpacityFromModel(model);
        applyOverlayOpacity(0);
        contentEl.innerHTML = '';
        renderHeaderItems(model, '');
        clearFooterSource();
        return;
      }

      modelRootOpacity = rootOpacityFromModel(model);
      applyOverlayOpacity(1);
      const metrics = Array.isArray(model.metrics) ? model.metrics : [];
      const metricSections = Array.isArray(model.metricSections) ? model.metricSections : [];
      const gridSections = Array.isArray(model.gridSections) ? model.gridSections : [];
      const rows = Array.isArray(model.rows) ? model.rows : [];
      const metricSectionHtml = metricSections.map(metricSection).join('');
      const sectionHtml = gridSections.map(gridSection).join('');
      if (model.bodyKind === 'summary-table') {
        const summary = metrics.length
          ? `<div class="metric-list" style="margin-bottom: 10px;">${metrics.map(metricRow).join('')}</div>`
          : '';
        contentEl.innerHTML = summary + rowsTable(displayModelHeaders(model), rows);
      } else if (model.bodyKind === 'graph') {
        const showGraphMetrics = model.overlayId !== 'gap-to-leader' && metrics.length;
        const summary = showGraphMetrics
          ? `<div class="metric-list graph-metrics">${metrics.map(metricRow).join('')}</div>`
          : '';
        contentEl.innerHTML = `${summary}<div class="model-graph-panel"><canvas class="model-graph" aria-label="Gap trend graph"></canvas></div>`;
        drawOverlayGraph(contentEl.querySelector('.model-graph'), model);
      } else if (model.bodyKind === 'metrics') {
        const metricsHtml = metrics.length && !metricSectionHtml
          ? `<div class="metric-list">${metrics.map(metricRow).join('')}</div>`
          : '';
        contentEl.innerHTML = metricsHtml || metricSectionHtml || sectionHtml
          ? `${metricsHtml}${metricSectionHtml}${sectionHtml}`
          : '<div class="empty">Waiting for live values.</div>';
      } else {
        contentEl.innerHTML = rowsTable(displayModelHeaders(model), rows);
      }

      renderHeaderItems(model, model.status || 'live');
      renderFooterSource(model);
    }

    function renderHeaderItems(model, fallbackStatus) {
      const hasHeaderItems = Array.isArray(model?.headerItems);
      const items = hasHeaderItems ? model.headerItems : [];
      const statusItem = items.find((item) => String(item?.key || '').toLowerCase() === 'status');
      const timeItem = items.find((item) => String(item?.key || '').toLowerCase() === 'timeremaining');
      const statusValue = statusItem ? String(statusItem.value || '').trim() : hasHeaderItems ? '' : fallbackStatus || '';
      statusEl.textContent = statusValue;
      statusEl.hidden = !statusValue;
      if (timeRemainingEl) {
        const value = timeItem?.value || '';
        timeRemainingEl.textContent = value;
        timeRemainingEl.hidden = !value;
      }
    }

    function renderFooterSource(model) {
      if (!sourceEl) return;
      const value = String(model?.source || '').trim();
      sourceEl.textContent = value;
      sourceEl.hidden = !value;
    }

    function clearFooterSource() {
      if (!sourceEl) return;
      sourceEl.hidden = true;
      sourceEl.textContent = '';
    }

    function drawOverlayGraph(canvas, model) {
      if (!canvas) return;
      const rect = canvas.getBoundingClientRect();
      const width = Math.max(1, rect.width);
      const height = Math.max(1, rect.height);
      const dpr = window.devicePixelRatio || 1;
      canvas.width = Math.round(width * dpr);
      canvas.height = Math.round(height * dpr);
      const ctx = canvas.getContext('2d');
      ctx.setTransform(dpr, 0, 0, dpr, 0, 0);
      ctx.clearRect(0, 0, width, height);

      if (drawOverlayGapGraph(ctx, width, height, model?.graph)) {
        return;
      }

      const points = Array.isArray(model?.points) ? model.points : [];
      const values = points.map(Number).filter(Number.isFinite);
      if (values.length < 2) {
        drawEmptyGapTrendFrame(ctx, width, height, model?.graph);
        return;
      }

      const { plot, axisWidth } = gapGraphLayout(width, height);
      const max = Math.max(1, ...values.map((value) => Math.max(0, value)));
      ctx.strokeStyle = themeRgba('--tmr-text-muted-rgb', 0.28, 'rgba(140, 174, 212, 0.28)');
      ctx.lineWidth = 1;
      for (let index = 1; index < 4; index += 1) {
        const y = plot.top + index * plot.height / 4;
        ctx.beginPath();
        ctx.moveTo(plot.left, y);
        ctx.lineTo(plot.left + plot.width, y);
        ctx.stroke();
      }
      ctx.strokeStyle = themeRgba('--tmr-text-muted-rgb', 0.24, 'rgba(140, 174, 212, 0.24)');
      ctx.beginPath();
      ctx.moveTo(plot.left, plot.top + plot.height);
      ctx.lineTo(plot.left + plot.width, plot.top + plot.height);
      ctx.stroke();
      ctx.strokeStyle = themeRgba('--tmr-text-muted-rgb', 0.34, 'rgba(140, 174, 212, 0.34)');
      ctx.beginPath();
      ctx.moveTo(plot.left, plot.top);
      ctx.lineTo(plot.left + plot.width, plot.top);
      ctx.stroke();

      ctx.fillStyle = themeColor('--tmr-text-muted', '#8caed4');
      ctx.font = '10px "Segoe UI", Arial, sans-serif';
      ctx.textBaseline = 'middle';
      ctx.textAlign = 'right';
      ctx.fillText('leader', axisWidth - 8, plot.top + 7);
      ctx.fillText(formatSignedGap(max), axisWidth - 8, plot.top + plot.height - 7);
      ctx.textAlign = 'left';
      ctx.fillText('10m', plot.left, plot.top + plot.height + 12);
      ctx.textAlign = 'right';
      ctx.fillText('now', plot.left + plot.width, plot.top + plot.height + 12);

      ctx.strokeStyle = themeColor('--tmr-cyan', '#00e8ff');
      ctx.lineWidth = 2;
      ctx.lineJoin = 'round';
      ctx.lineCap = 'round';
      ctx.beginPath();
      values.forEach((value, index) => {
        const progress = index / Math.max(1, values.length - 1);
        const normalized = Math.max(0, value) / max;
        const x = plot.left + progress * plot.width;
        const y = plot.top + normalized * plot.height;
        if (index === 0) {
          ctx.moveTo(x, y);
        } else {
          ctx.lineTo(x, y);
        }
      });
      ctx.stroke();
    }

    function drawOverlayGapGraph(ctx, width, height, graph) {
      const series = Array.isArray(graph?.series) ? graph.series : [];
      const totalSeriesPoints = series.reduce((total, item) => total + (Array.isArray(item?.points) ? item.points.length : 0), 0);
      if (totalSeriesPoints < 2) {
        return false;
      }

      const { plot, labelLane, metricsRect } = gapGraphLayout(width, height);
      const scale = graph.scale || { isFocusRelative: false, maxGapSeconds: graph.maxGapSeconds };
      const maxGapSeconds = Math.max(1, numberOr(scale.maxGapSeconds, graph.maxGapSeconds, 1));
      drawGapWeatherBands(ctx, graph, plot);
      drawGapLapIntervals(ctx, graph, plot);
      drawGapGrid(ctx, graph, scale, plot, maxGapSeconds);
      drawGapScaleLabels(ctx, graph, scale, plot, maxGapSeconds);
      drawGapLeaderMarkers(ctx, graph, plot);

      const labels = [];
      const orderedSeries = [...series].sort((a, b) =>
        Number(Boolean(a?.isClassLeader)) - Number(Boolean(b?.isClassLeader))
        || Number(Boolean(a?.isReference)) - Number(Boolean(b?.isReference)));
      orderedSeries.forEach((item, index) => {
        if (scale?.isFocusRelative === true && item?.isClassLeader) return;

        const color = graphSeriesColor(item, index, graph?.threatCarIdx);
        const alpha = clamp01(numberOr(item?.alpha, 1)) * graphSeriesAlphaMultiplier(item, graph?.threatCarIdx);
        const pointsForSeries = (Array.isArray(item?.points) ? item.points : [])
          .filter((point) => Number.isFinite(point?.axisSeconds) && Number.isFinite(point?.gapSeconds))
          .sort((a, b) => a.axisSeconds - b.axisSeconds);
        if (pointsForSeries.length === 0) return;

        ctx.save();
        ctx.globalAlpha = alpha;
        ctx.strokeStyle = color;
        ctx.fillStyle = color;
        ctx.lineWidth = item?.isReference ? 2.6 : item?.isClassLeader ? 1.8 : 1.25;
        ctx.lineJoin = 'round';
        ctx.lineCap = 'round';
        if (item?.isStale || item?.isStickyExit) ctx.setLineDash([6, 4]);
        drawGapSeriesSegments(ctx, graph, scale, plot, maxGapSeconds, pointsForSeries);
        ctx.restore();

        const latest = pointsForSeries[pointsForSeries.length - 1];
        const latestPoint = gapPoint(graph, scale, plot, maxGapSeconds, latest.axisSeconds, latest.gapSeconds);
        labels.push({
          text: item?.isClassLeader ? 'P1' : Number.isFinite(item?.classPosition) ? `P${item.classPosition}` : `#${item?.carIdx ?? '--'}`,
          point: latestPoint,
          color,
          isReference: Boolean(item?.isReference),
          isClassLeader: Boolean(item?.isClassLeader)
        });
        if (item?.isStale) {
          ctx.save();
          ctx.strokeStyle = color;
          ctx.lineWidth = 1.2;
          ctx.beginPath();
          ctx.moveTo(latestPoint.x - 4, latestPoint.y - 4);
          ctx.lineTo(latestPoint.x + 4, latestPoint.y + 4);
          ctx.moveTo(latestPoint.x - 4, latestPoint.y + 4);
          ctx.lineTo(latestPoint.x + 4, latestPoint.y - 4);
          ctx.stroke();
          ctx.restore();
        }
      });

      drawGapThreatAnnotation(ctx, graph?.activeThreat, plot);
      drawGapEndpointLabels(ctx, labels, plot, labelLane);
      drawGapDriverMarkers(ctx, graph, scale, plot, maxGapSeconds);
      if (metricsRect) drawGapFocusedMetricsTable(ctx, metricsRect, graph);
      return true;
    }

    function gapGraphLayout(width, height) {
      const axisWidth = 58;
      const xAxisHeight = 17;
      const labelLaneWidth = 38;
      const metricsWidth = gapMetricsTableWidth(width);
      const plotHeight = Math.max(40, height - xAxisHeight);
      const metricsRect = metricsWidth > 0
        ? { left: width - metricsWidth, top: 0, width: metricsWidth, height: plotHeight }
        : null;
      const chartRight = metricsRect ? metricsRect.left - 10 : width - 4;
      const labelLane = {
        left: chartRight - labelLaneWidth,
        top: 0,
        width: labelLaneWidth,
        height: plotHeight
      };
      const plot = {
        left: axisWidth,
        top: 0,
        width: Math.max(40, labelLane.left - axisWidth),
        height: plotHeight
      };
      return { plot, labelLane, metricsRect, plotHeight, axisWidth };
    }

    function drawEmptyGapTrendFrame(ctx, width, height, graph) {
      const { plot, metricsRect } = gapGraphLayout(width, height);
      ctx.save();
      ctx.strokeStyle = themeRgba('--tmr-text-muted-rgb', 0.18, 'rgba(140, 174, 212, 0.18)');
      ctx.lineWidth = 1;
      for (let index = 1; index < 4; index += 1) {
        const y = plot.top + index * plot.height / 4;
        ctx.beginPath();
        ctx.moveTo(plot.left, y);
        ctx.lineTo(plot.left + plot.width, y);
        ctx.stroke();
      }

      ctx.fillStyle = themeColor('--tmr-text-muted', '#8caed4');
      ctx.font = '700 12px "Segoe UI", Arial, sans-serif';
      ctx.textAlign = 'center';
      ctx.textBaseline = 'middle';
      ctx.fillText('waiting for timing', plot.left + plot.width / 2, plot.top + plot.height / 2);
      ctx.font = '10px "Segoe UI", Arial, sans-serif';
      ctx.fillStyle = 'rgba(140, 174, 212, 0.74)';
      ctx.fillText('trend will populate when live gaps are grounded', plot.left + plot.width / 2, plot.top + plot.height / 2 + 18);
      ctx.restore();
      if (metricsRect) drawGapFocusedMetricsTable(ctx, metricsRect, graph);
    }

    function drawGapSeriesSegments(ctx, graph, scale, plot, maxGapSeconds, points) {
      let segment = [];
      for (const point of points) {
        if (point.startsSegment === true && segment.length > 0) {
          drawGapSegment(ctx, segment);
          segment = [];
        }
        segment.push(gapPoint(graph, scale, plot, maxGapSeconds, point.axisSeconds, point.gapSeconds));
      }
      drawGapSegment(ctx, segment);
    }

    function drawGapSegment(ctx, segment) {
      if (segment.length === 0) return;
      if (segment.length === 1) {
        drawCanvasPoint(ctx, segment[0], 3.5);
        return;
      }
      ctx.beginPath();
      segment.forEach((point, index) => {
        if (index === 0) ctx.moveTo(point.x, point.y);
        else ctx.lineTo(point.x, point.y);
      });
      ctx.stroke();
      drawCanvasPoint(ctx, segment[segment.length - 1], 3.5);
    }

    function drawCanvasPoint(ctx, point, radius) {
      ctx.beginPath();
      ctx.arc(point.x, point.y, radius, 0, Math.PI * 2);
      ctx.fill();
    }

    function drawGapWeatherBands(ctx, graph, plot) {
      const weather = Array.isArray(graph?.weather) ? graph.weather : [];
      if (weather.length === 0) return;
      const domain = graphDomain(graph);
      weather.forEach((point, index) => {
        const color = weatherColor(point?.condition);
        if (!color) return;
        const nextAxis = Number.isFinite(weather[index + 1]?.axisSeconds) ? weather[index + 1].axisSeconds : domain.end;
        const start = Math.max(domain.start, numberOr(point?.axisSeconds, domain.start));
        const end = Math.min(domain.end, nextAxis);
        if (end <= start) return;
        const x = axisToX(graph, plot, start);
        const nextX = axisToX(graph, plot, end);
        ctx.fillStyle = color;
        ctx.fillRect(x, plot.top, Math.max(1, nextX - x), plot.height);
        if (isDeclaredWet(point?.condition)) {
          ctx.fillStyle = 'rgba(94, 190, 255, 0.17)';
          ctx.fillRect(x, plot.top, Math.max(1, nextX - x), 4);
        }
      });
    }

    function drawGapLapIntervals(ctx, graph, plot) {
      const lapSeconds = Number(graph?.lapReferenceSeconds);
      if (!Number.isFinite(lapSeconds) || lapSeconds < 20) return;
      const domain = graphDomain(graph);
      const duration = domain.end - domain.start;
      const interval = lapSeconds * 5;
      if (duration < interval * 0.75) return;
      ctx.save();
      ctx.strokeStyle = 'rgba(255, 255, 255, 0.13)';
      ctx.fillStyle = themeColor('--tmr-text-muted', '#8caed4');
      ctx.font = '10px "Segoe UI", Arial, sans-serif';
      ctx.textAlign = 'center';
      ctx.textBaseline = 'top';
      for (let elapsed = interval; elapsed < duration; elapsed += interval) {
        const x = plot.left + elapsed / duration * plot.width;
        ctx.beginPath();
        ctx.moveTo(x, plot.top);
        ctx.lineTo(x, plot.top + plot.height);
        ctx.stroke();
        ctx.fillText(`${Math.round(elapsed / lapSeconds)}L`, x, plot.top + plot.height + 1);
      }
      ctx.restore();
    }

    function drawGapGrid(ctx, graph, scale, plot, maxGapSeconds) {
      ctx.save();
      ctx.strokeStyle = themeRgba('--tmr-text-muted-rgb', 0.24, 'rgba(140, 174, 212, 0.24)');
      ctx.lineWidth = 1;
      ctx.beginPath();
      ctx.moveTo(plot.left, plot.top);
      ctx.lineTo(plot.left + plot.width, plot.top);
      ctx.moveTo(plot.left, plot.top + plot.height);
      ctx.lineTo(plot.left + plot.width, plot.top + plot.height);
      ctx.stroke();
      ctx.fillStyle = themeColor('--tmr-text-muted', '#8caed4');
      ctx.font = '10px "Segoe UI", Arial, sans-serif';
      ctx.textAlign = 'right';
      ctx.textBaseline = 'middle';

      if (scale?.isFocusRelative === true) {
        const referenceY = focusReferenceY(plot);
        ctx.strokeStyle = themeRgba('--tmr-green-rgb', 0.43, 'rgba(112, 224, 146, 0.43)');
        ctx.beginPath();
        ctx.moveTo(plot.left, referenceY);
        ctx.lineTo(plot.left + plot.width, referenceY);
        ctx.stroke();
        ctx.fillStyle = themeColor('--tmr-green', '#70e092');
        ctx.fillText('focus', plot.left - 8, referenceY);
        ctx.strokeStyle = themeRgba('--tmr-text-muted-rgb', 0.18, 'rgba(140, 174, 212, 0.18)');
        ctx.fillStyle = themeColor('--tmr-text-muted', '#8caed4');

        const aheadStep = niceGridStep(numberOr(scale.aheadSeconds, 1) / 2);
        for (let value = aheadStep; value < numberOr(scale.aheadSeconds, 0); value += aheadStep) {
          const y = gapDeltaToY(-value, scale, plot);
          drawGridLineWithLabel(ctx, plot, y, formatDeltaSeconds(-value));
        }
        const behindStep = niceGridStep(numberOr(scale.behindSeconds, 1) / 2);
        for (let value = behindStep; value < numberOr(scale.behindSeconds, 0); value += behindStep) {
          const y = gapDeltaToY(value, scale, plot);
          drawGridLineWithLabel(ctx, plot, y, formatDeltaSeconds(value));
        }
        ctx.restore();
        return;
      }

      const labelYs = [plot.top + 7, plot.top + plot.height - 7];
      const step = niceGridStep(maxGapSeconds / 4);
      for (let value = step; value < maxGapSeconds; value += step) {
        drawGridLineWithLabel(ctx, plot, gapToY(value, maxGapSeconds, plot), formatSignedGap(value), labelYs);
      }
      const lapSeconds = Number(graph?.lapReferenceSeconds);
      if (Number.isFinite(lapSeconds) && lapSeconds >= 20 && maxGapSeconds >= lapSeconds * 0.85) {
        ctx.strokeStyle = 'rgba(255, 255, 255, 0.58)';
        ctx.fillStyle = themeColor('--tmr-text', '#ffffff');
        for (let lap = 1; lap * lapSeconds < maxGapSeconds; lap += 1) {
          drawGridLineWithLabel(ctx, plot, gapToY(lap * lapSeconds, maxGapSeconds, plot), `+${lap} lap`, labelYs);
        }
      }
      ctx.restore();
    }

    function drawGridLineWithLabel(ctx, plot, y, label, labelYs = null) {
      ctx.beginPath();
      ctx.moveTo(plot.left, y);
      ctx.lineTo(plot.left + plot.width, y);
      ctx.stroke();
      if (Array.isArray(labelYs) && labelYs.some((usedY) => Math.abs(usedY - y) < 13)) {
        return;
      }
      ctx.fillText(label, plot.left - 8, y);
      if (Array.isArray(labelYs)) {
        labelYs.push(y);
      }
    }

    function drawGapScaleLabels(ctx, graph, scale, plot, maxGapSeconds) {
      ctx.save();
      ctx.fillStyle = themeColor('--tmr-text-muted', '#8caed4');
      ctx.font = '10px "Segoe UI", Arial, sans-serif';
      ctx.textBaseline = 'middle';
      ctx.textAlign = 'right';
      if (scale?.isFocusRelative === true) {
        ctx.fillText('local', plot.left - 8, plot.top + 7);
        ctx.fillText(formatDeltaSeconds(-numberOr(scale.aheadSeconds, 0)), plot.left - 8, plot.top + 18);
        ctx.fillText(formatDeltaSeconds(numberOr(scale.behindSeconds, 0)), plot.left - 8, plot.top + plot.height - 8);
      } else {
        ctx.fillText('leader', plot.left - 8, plot.top + 7);
        ctx.fillText(formatSignedGap(maxGapSeconds), plot.left - 8, plot.top + plot.height - 7);
      }
      ctx.textAlign = 'left';
      ctx.textBaseline = 'alphabetic';
      ctx.fillText(formatTrendWindow(graphDomain(graph).end - graphDomain(graph).start), plot.left, plot.top + plot.height + 13);
      ctx.textAlign = 'right';
      ctx.fillText('now', plot.left + plot.width, plot.top + plot.height + 13);
      ctx.restore();
    }

    function drawGapLeaderMarkers(ctx, graph, plot) {
      const markers = Array.isArray(graph?.leaderChanges) ? graph.leaderChanges : [];
      if (markers.length === 0) return;
      ctx.save();
      ctx.strokeStyle = 'rgba(255, 255, 255, 0.45)';
      ctx.fillStyle = themeColor('--tmr-text-muted', '#8caed4');
      ctx.font = '700 10px "Segoe UI", Arial, sans-serif';
      ctx.setLineDash([2, 4]);
      for (const marker of markers) {
        if (!Number.isFinite(marker?.axisSeconds)) continue;
        const x = axisToX(graph, plot, marker.axisSeconds);
        ctx.beginPath();
        ctx.moveTo(x, plot.top);
        ctx.lineTo(x, plot.top + plot.height);
        ctx.stroke();
        ctx.fillText('leader', x + 4, plot.top + 12);
      }
      ctx.restore();
    }

    function drawGapDriverMarkers(ctx, graph, scale, plot, maxGapSeconds) {
      const markers = Array.isArray(graph?.driverChanges) ? graph.driverChanges : [];
      if (markers.length === 0) return;
      ctx.save();
      ctx.font = '700 10px "Segoe UI", Arial, sans-serif';
      for (const marker of markers) {
        if (!Number.isFinite(marker?.axisSeconds) || !Number.isFinite(marker?.gapSeconds)) continue;
        const point = gapPoint(graph, scale, plot, maxGapSeconds, marker.axisSeconds, marker.gapSeconds);
        const color = marker.isReference ? themeColor('--tmr-green', '#70e092') : themeColor('--tmr-text-secondary', '#cdd8e4');
        ctx.strokeStyle = color;
        ctx.fillStyle = themeColor('--tmr-surface', '#121e2a');
        ctx.lineWidth = marker.isReference ? 1.8 : 1.3;
        ctx.beginPath();
        ctx.moveTo(point.x, point.y - 9);
        ctx.lineTo(point.x, point.y + 9);
        ctx.stroke();
        ctx.beginPath();
        ctx.arc(point.x, point.y, 4.5, 0, Math.PI * 2);
        ctx.fill();
        ctx.stroke();
        ctx.fillStyle = color;
        ctx.fillText(String(marker.label || 'DR').slice(0, 3), point.x + 6, point.y - 8);
      }
      ctx.restore();
    }

    function gapMetricsTableWidth(width) {
      const metricsWidth = 184;
      const availableAfterTable = width - 58 - 38 - 10 - metricsWidth;
      return availableAfterTable >= 300 ? metricsWidth : 0;
    }

    function drawGapThreatAnnotation(ctx, metric, plot) {
      const chaser = metric?.chaser;
      if (!chaser) return;
      const metricLabel = String(metric?.label || '').trim();
      const suffix = metricLabel && metricLabel.toLowerCase() !== 'threat' ? ` ${metricLabel}` : '';
      const text = `Threat ${chaser.label || `#${chaser.carIdx ?? '--'}`} ${formatFocusedTrendChangeSeconds(-chaser.gainSeconds)}${suffix}`;
      ctx.save();
      ctx.font = '700 8.5px "Segoe UI", Arial, sans-serif';
      const textWidth = ctx.measureText(text).width;
      const badgeHeight = 16;
      const x = Math.min(Math.max(plot.left + 2, plot.left + plot.width / 2 - textWidth / 2), plot.left + plot.width - textWidth - 8);
      const y = plot.top + plot.height - badgeHeight - 6;
      ctx.lineWidth = 1;
      drawRoundedRect(
        ctx,
        x - 4,
        y - 1,
        textWidth + 8,
        badgeHeight,
        3,
        'rgba(18, 24, 28, 0.84)',
        colorWithAlpha(themeColor('--tmr-error', '#ec7063'), 0.38));
      ctx.fillStyle = themeColor('--tmr-error', '#ec7063');
      ctx.textBaseline = 'middle';
      ctx.fillText(text, x, y + badgeHeight / 2 - 0.5);
      ctx.restore();
    }

    function drawGapFocusedMetricsTable(ctx, rect, graph) {
      const metrics = Array.isArray(graph?.trendMetrics) && graph.trendMetrics.length > 0
        ? graph.trendMetrics
        : [
            { label: '5L', state: 'unavailable' },
            { label: '10L', state: 'unavailable' },
            { label: 'Pit', state: 'unavailable' },
            { label: 'PLap', state: 'unavailable' },
            { label: 'Stint', state: 'unavailable' },
            { label: 'Tire', state: 'unavailable' },
            { label: 'Last', state: 'unavailable' },
            { label: 'Status', state: 'unavailable' }
          ];
      const visibleMetrics = metrics.filter(Boolean);
      const showThreatFooter = visibleMetrics.length <= 6;
      const rowAreaBottomPadding = showThreatFooter ? 48 : 8;
      const rowHeight = Math.max(9.5, Math.min(26, (rect.height - rowAreaBottomPadding - 38) / Math.max(1, visibleMetrics.length)));
      ctx.save();
      ctx.lineWidth = 1;
      drawRoundedRect(
        ctx,
        rect.left,
        rect.top,
        rect.width,
        rect.height,
        3,
        'rgba(18, 24, 28, 0.74)',
        themeRgba('--tmr-text-rgb', 0.15, 'rgba(247, 251, 255, 0.15)'));

      ctx.textBaseline = 'middle';
      ctx.font = '700 10px "Segoe UI", Arial, sans-serif';
      ctx.fillStyle = themeColor('--tmr-text', '#f7fbff');
      ctx.fillText('Trend', rect.left + 8, rect.top + 11);
      ctx.font = '8px "Segoe UI", Arial, sans-serif';
      ctx.fillStyle = themeColor('--tmr-text-muted', '#8caed4');
      ctx.fillText('Metric', rect.left + 8, rect.top + 26);
      ctx.fillText(graph?.comparisonLabel || '--', rect.left + 56, rect.top + 26);
      ctx.fillText('Threat', rect.left + 108, rect.top + 26);

      ctx.font = `${rowHeight < 16 ? '8px' : '9px'} "Segoe UI", Arial, sans-serif`;
      visibleMetrics.forEach((metric, index) => {
        const y = rect.top + 43 + index * rowHeight;
        ctx.fillStyle = themeColor('--tmr-text-secondary', '#cdd8e4');
        ctx.fillText(metric?.label || '--', rect.left + 8, y);
        ctx.fillStyle = gapMetricValueColor(metric, numberOr(graph?.metricDeadbandSeconds, 0.25));
        ctx.fillText(gapMetricValueText(metric), rect.left + 56, y);
        ctx.fillStyle = gapMetricChaserColor(metric);
        ctx.fillText(gapMetricChaserText(metric), rect.left + 108, y);
      });

      if (showThreatFooter) {
        const threat = graph?.activeThreat || metrics.find((metric) => metric?.chaser);
        const threatY = rect.top + rect.height - 28;
        const footerStroke = threat?.chaser
          ? colorWithAlpha(themeColor('--tmr-error', '#ec7063'), 0.34)
          : 'rgba(255, 255, 255, 0.10)';
        drawRoundedRect(
          ctx,
          rect.left + 8,
          threatY,
          rect.width - 16,
          20,
          3,
          threat?.chaser ? 'rgba(236, 112, 99, 0.14)' : 'rgba(255, 255, 255, 0.055)',
          footerStroke);
        ctx.font = '700 8px "Segoe UI", Arial, sans-serif';
        ctx.fillStyle = threat?.chaser
          ? themeColor('--tmr-error', '#ec7063')
          : 'rgba(140, 174, 212, 0.72)';
        ctx.fillText(threat?.chaser
          ? `Threat ${threat.chaser.label || `#${threat.chaser.carIdx ?? '--'}`} ${formatFocusedTrendChangeSeconds(-threat.chaser.gainSeconds)}`
          : 'Threat --', rect.left + 14, threatY + 10);
      }
      ctx.restore();
    }

    function gapMetricValueText(metric) {
      const state = String(metric?.state || '').toLowerCase();
      if (state === 'pit') return gapPitSecondsText(metric?.comparisonPit);
      if (state === 'pitlap') return gapPitLapText(metric?.comparisonPit);
      if (state === 'tire') return gapTireText(metric?.comparisonTire);
      if (state === 'stint') return metric?.comparisonText || '--';
      if (state === 'last' || state === 'status') return metric?.comparisonText || '--';
      if (state === 'ready' && Number.isFinite(metric?.focusGapChangeSeconds)) return formatFocusedTrendChangeSeconds(-metric.focusGapChangeSeconds);
      if (state === 'ready' && metric?.stateLabel) return metric.stateLabel;
      if (state === 'warming') return metric?.stateLabel || '--';
      if (state === 'leaderchanged') return 'leader';
      return '--';
    }

    function gapMetricValueColor(metric, deadbandSeconds) {
      const state = String(metric?.state || '').toLowerCase();
      if (state === 'pit') return gapPitSecondsComparisonColor(metric?.primaryPit, metric?.comparisonPit);
      if (state === 'pitlap') return gapPitLapComparisonColor(metric?.primaryPit, metric?.comparisonPit);
      if (state === 'tire') return gapTireComparisonColor(metric?.primaryTire, metric?.comparisonTire);
      if (state === 'last') return gapLastLapComparisonColor(metric?.primaryText, metric?.comparisonText);
      if (state === 'status' || state === 'stint') return gapNeutralMetricColor(metric?.comparisonText);
      const value = Number(metric?.focusGapChangeSeconds);
      if (state !== 'ready' || !Number.isFinite(value)) {
        return state === 'warming' || state === 'leaderchanged'
          ? themeColor('--tmr-text-muted', '#8caed4')
          : 'rgba(140, 174, 212, 0.72)';
      }
      if (Math.abs(value) < deadbandSeconds) return 'rgba(205, 218, 228, 0.88)';
      return value > 0 ? themeColor('--tmr-error', '#ec7063') : themeColor('--tmr-green', '#70e092');
    }

    function gapMetricChaserText(metric) {
      const state = String(metric?.state || '').toLowerCase();
      if (state === 'pit') return gapPitSecondsText(metric?.threatPit);
      if (state === 'pitlap') return gapPitLapText(metric?.threatPit);
      if (state === 'tire') return gapTireText(metric?.threatTire);
      if (state === 'stint') return metric?.threatText || '--';
      if (state === 'last' || state === 'status') return metric?.threatText || '--';
      if (state === 'ready' && metric?.chaser) {
        return `${metric.chaser.label || `#${metric.chaser.carIdx ?? '--'}`} ${formatFocusedTrendChangeSeconds(-metric.chaser.gainSeconds)}`;
      }
      if (state === 'leaderchanged') return 'reset';
      return '--';
    }

    function gapMetricChaserColor(metric) {
      const state = String(metric?.state || '').toLowerCase();
      if (state === 'pit') return gapPitSecondsComparisonColor(metric?.primaryPit, metric?.threatPit);
      if (state === 'pitlap') return gapPitLapComparisonColor(metric?.primaryPit, metric?.threatPit);
      if (state === 'tire') return gapTireComparisonColor(metric?.primaryTire, metric?.threatTire);
      if (state === 'last') return gapLastLapComparisonColor(metric?.primaryText, metric?.threatText);
      if (state === 'status' || state === 'stint') return gapNeutralMetricColor(metric?.threatText);
      return state === 'ready' && metric?.chaser
        ? themeColor('--tmr-error', '#ec7063')
        : 'rgba(140, 174, 212, 0.72)';
    }

    function gapPitSecondsText(pit) {
      const seconds = Number(pit?.seconds);
      if (!Number.isFinite(seconds)) return '--';
      return seconds >= 60
        ? `${Math.floor(seconds / 60)}:${Math.round(seconds % 60).toString().padStart(2, '0')}`
        : `${Math.round(seconds)}s`;
    }

    function gapPitLapText(pit) {
      const lap = Number(pit?.lap);
      return Number.isFinite(lap) && lap > 0 ? `L${lap}` : '--';
    }

    function gapPitSecondsComparisonColor(focusPit, comparisonPit) {
      const comparisonSeconds = Number(comparisonPit?.seconds);
      if (!Number.isFinite(comparisonSeconds)) return 'rgba(140, 174, 212, 0.72)';

      const focusSeconds = Number(focusPit?.seconds);
      if (!Number.isFinite(focusSeconds)) return 'rgba(205, 218, 228, 0.88)';

      const delta = focusSeconds - comparisonSeconds;
      if (Math.abs(delta) <= 1) return 'rgba(205, 218, 228, 0.88)';
      return delta < 0 ? themeColor('--tmr-green', '#70e092') : themeColor('--tmr-error', '#ec7063');
    }

    function gapPitLapComparisonColor(focusPit, comparisonPit) {
      const comparisonLap = Number(comparisonPit?.lap);
      if (!Number.isFinite(comparisonLap) || comparisonLap <= 0) return 'rgba(140, 174, 212, 0.72)';

      const focusLap = Number(focusPit?.lap);
      if (!Number.isFinite(focusLap) || focusLap <= 0 || focusLap === comparisonLap) {
        return 'rgba(205, 218, 228, 0.88)';
      }

      return focusLap < comparisonLap ? themeColor('--tmr-green', '#70e092') : themeColor('--tmr-error', '#ec7063');
    }

    function gapTireText(tire) {
      const text = String(tire?.shortLabel || tire?.label || '').trim();
      return text || '--';
    }

    function gapTireComparisonColor(focusTire, comparisonTire) {
      const comparison = String(comparisonTire?.shortLabel || comparisonTire?.label || '').trim().toLowerCase();
      if (!comparison) return 'rgba(140, 174, 212, 0.72)';

      const focus = String(focusTire?.shortLabel || focusTire?.label || '').trim().toLowerCase();
      if (!focus || focus === comparison) {
        return comparisonTire?.isWet === true
          ? themeColor('--tmr-cyan', '#00e8ff')
          : 'rgba(205, 218, 228, 0.88)';
      }

      return comparisonTire?.isWet === true
        ? themeColor('--tmr-cyan', '#00e8ff')
        : themeColor('--tmr-amber', '#ffcc66');
    }

    function gapLastLapComparisonColor(focusText, comparisonText) {
      const focus = lapTimeTextToSeconds(focusText);
      const comparison = lapTimeTextToSeconds(comparisonText);
      if (!Number.isFinite(comparison)) return 'rgba(140, 174, 212, 0.72)';
      if (!Number.isFinite(focus)) return 'rgba(205, 218, 228, 0.88)';
      const delta = comparison - focus;
      if (Math.abs(delta) <= 0.05) return 'rgba(205, 218, 228, 0.88)';
      return delta < 0 ? themeColor('--tmr-error', '#ec7063') : themeColor('--tmr-green', '#70e092');
    }

    function gapNeutralMetricColor(value) {
      const comparison = String(value || '').trim();
      if (!comparison || comparison === '--') return 'rgba(140, 174, 212, 0.72)';
      return 'rgba(205, 218, 228, 0.88)';
    }

    function lapTimeTextToSeconds(value) {
      const text = String(value || '').trim();
      if (!text || text === '--') return NaN;
      const match = /^(\d+):(\d+(?:\.\d+)?)$/.exec(text);
      if (match) {
        return Number(match[1]) * 60 + Number(match[2]);
      }

      const seconds = Number(text);
      return Number.isFinite(seconds) ? seconds : NaN;
    }

    function drawGapEndpointLabels(ctx, labels, plot, labelLane) {
      if (!labels.length) return;
      const labelHeight = 13;
      const pinned = labels.filter((label) => shouldPinGapEndpointLabel(label, plot));
      const floating = labels.filter((label) => !shouldPinGapEndpointLabel(label, plot));
      floating
        .sort((a, b) => gapEndpointLabelPriority(a) - gapEndpointLabelPriority(b) || a.point.y - b.point.y)
        .forEach((label) => {
          const y = clampGapEndpointLabelY(label.point.y - labelHeight / 2, plot, labelHeight);
          drawGapEndpointLabel(ctx, label, y, plot, plot, false);
        });

      const bounds = labelLane || plot;
      const ordered = pinned
        .map((label) => ({ ...label, y: clampGapEndpointLabelY(label.point.y - labelHeight / 2, bounds, labelHeight) }))
        .sort((a, b) => a.y - b.y || gapEndpointLabelPriority(a) - gapEndpointLabelPriority(b));
      if (ordered.length === 0) return;
      const minY = bounds.top + 1;
      const maxY = bounds.top + bounds.height - labelHeight - 1;
      for (let index = 0; index < ordered.length; index += 1) {
        ordered[index].y = Math.max(minY, Math.min(maxY, ordered[index].y));
        if (index > 0) {
          ordered[index].y = Math.max(ordered[index].y, ordered[index - 1].y + labelHeight + 1);
        }
      }
      if (ordered[ordered.length - 1].y > maxY) {
        const shift = ordered[ordered.length - 1].y - maxY;
        ordered.forEach((label) => { label.y = Math.max(minY, label.y - shift); });
      }

      ordered
        .sort((a, b) => gapEndpointLabelPriority(a) - gapEndpointLabelPriority(b))
        .forEach((label) => drawGapEndpointLabel(ctx, label, label.y, plot, bounds, true));
    }

    function drawGapEndpointLabel(ctx, label, y, plot, labelBounds, pinnedToLane) {
      const labelHeight = 13;
      ctx.save();
      ctx.font = `${label.isReference ? '700 10px' : '700 9px'} "Segoe UI", Arial, sans-serif`;
      ctx.textBaseline = 'middle';
      const textWidth = ctx.measureText(label.text).width;
      const x = pinnedToLane
        ? Math.min(labelBounds.left + labelBounds.width - textWidth - 1, Math.max(labelBounds.left + 4, label.point.x + 8))
        : Math.min(labelBounds.left + labelBounds.width - textWidth - 2, label.point.x + 6);
      if (pinnedToLane || Math.abs(y + labelHeight / 2 - label.point.y) > 3) {
        ctx.strokeStyle = colorWithAlpha(label.color, 0.32);
        ctx.beginPath();
        ctx.moveTo(label.point.x + 3, label.point.y);
        ctx.lineTo(x - 2, y + labelHeight / 2);
        ctx.stroke();
      }
      ctx.fillStyle = label.isReference ? 'rgba(18, 30, 42, 0.74)' : 'rgba(18, 30, 42, 0.59)';
      ctx.fillRect(x - 2, y, textWidth + 4, labelHeight);
      ctx.fillStyle = colorWithAlpha(label.color, label.isReference ? 1 : 0.78);
      ctx.fillText(label.text, x, y + labelHeight / 2);
      ctx.restore();
    }

    function shouldPinGapEndpointLabel(label, plot) {
      return label.point.x >= plot.left + plot.width - 4;
    }

    function clampGapEndpointLabelY(y, bounds, labelHeight) {
      return Math.max(bounds.top + 1, Math.min(bounds.top + bounds.height - labelHeight - 1, y));
    }

    function gapEndpointLabelPriority(label) {
      if (label.isReference) return 2;
      return label.isClassLeader ? 1 : 0;
    }

    function graphSeriesColor(series, index, threatCarIdx) {
      if (Number.isFinite(threatCarIdx) && series?.carIdx === threatCarIdx) return themeColor('--tmr-error', '#ec7063');
      if (series?.isReference) return themeColor('--tmr-cyan', '#00e8ff');
      if (series?.isClassLeader) return themeColor('--tmr-text', '#ffffff');
      const colors = [
        themeColor('--tmr-amber', '#ffd15b'),
        themeColor('--tmr-green', '#70e092'),
        themeColor('--tmr-magenta', '#ff62d2')
      ];
      return colors[index % colors.length];
    }

    function graphSeriesAlphaMultiplier(series, threatCarIdx) {
      return series?.isClassLeader || series?.isReference || (Number.isFinite(threatCarIdx) && series?.carIdx === threatCarIdx) ? 1 : 0.48;
    }

    function gapPoint(graph, scale, plot, maxGapSeconds, axisSeconds, gapSeconds) {
      const x = axisToX(graph, plot, axisSeconds);
      const y = scale?.isFocusRelative === true
        ? gapDeltaToY(gapSeconds - referenceGapAt(scale.referencePoints || [], axisSeconds), scale, plot)
        : gapToY(gapSeconds, maxGapSeconds, plot);
      return { x, y };
    }

    function axisToX(graph, plot, axisSeconds) {
      const domain = graphDomain(graph);
      const ratio = (axisSeconds - domain.start) / Math.max(1, domain.end - domain.start);
      return plot.left + Math.max(0, Math.min(1, ratio)) * plot.width;
    }

    function gapToY(gapSeconds, maxGapSeconds, plot) {
      return plot.top + Math.max(0, Math.min(1, gapSeconds / Math.max(1, maxGapSeconds))) * plot.height;
    }

    function gapDeltaToY(deltaSeconds, scale, plot) {
      const referenceY = focusReferenceY(plot);
      if (deltaSeconds < 0) {
        const ratio = Math.max(0, Math.min(1, Math.abs(deltaSeconds) / Math.max(1, numberOr(scale?.aheadSeconds, 1))));
        return referenceY - ratio * Math.max(1, referenceY - (plot.top + 18));
      }
      const ratio = Math.max(0, Math.min(1, deltaSeconds / Math.max(1, numberOr(scale?.behindSeconds, 1))));
      return referenceY + ratio * Math.max(1, plot.top + plot.height - 8 - referenceY);
    }

    function focusReferenceY(plot) {
      return plot.top + plot.height * 0.56;
    }

    function referenceGapAt(points, axisSeconds) {
      const ordered = (Array.isArray(points) ? points : [])
        .filter((point) => Number.isFinite(point?.axisSeconds) && Number.isFinite(point?.gapSeconds))
        .sort((a, b) => a.axisSeconds - b.axisSeconds);
      if (ordered.length === 0) return 0;
      if (axisSeconds <= ordered[0].axisSeconds) return ordered[0].gapSeconds;
      const last = ordered[ordered.length - 1];
      if (axisSeconds >= last.axisSeconds) return last.gapSeconds;
      const afterIndex = ordered.findIndex((point) => point.axisSeconds >= axisSeconds);
      const after = ordered[Math.max(0, afterIndex)];
      const before = ordered[Math.max(0, afterIndex - 1)];
      const span = after.axisSeconds - before.axisSeconds;
      if (span <= 0.001) return before.gapSeconds;
      const ratio = Math.max(0, Math.min(1, (axisSeconds - before.axisSeconds) / span));
      return before.gapSeconds + (after.gapSeconds - before.gapSeconds) * ratio;
    }

    function graphDomain(graph) {
      const start = numberOr(graph?.startSeconds, 0);
      const end = Math.max(start + 1, numberOr(graph?.endSeconds, start + 1));
      return { start, end };
    }

    function weatherColor(condition) {
      if (condition === 2 || String(condition).toLowerCase() === 'damp') return 'rgba(75, 170, 205, 0.08)';
      if (condition === 3 || String(condition).toLowerCase() === 'wet') return 'rgba(70, 135, 230, 0.13)';
      if (isDeclaredWet(condition)) return 'rgba(78, 142, 238, 0.17)';
      return null;
    }

    function isDeclaredWet(condition) {
      return condition === 4 || String(condition).toLowerCase() === 'declaredwet' || String(condition).toLowerCase() === 'declared-wet';
    }

    function niceGridStep(value) {
      if (!Number.isFinite(value) || value <= 0.25) return 0.25;
      const magnitude = Math.pow(10, Math.floor(Math.log10(value)));
      const normalized = value / magnitude;
      for (const step of [1, 2, 2.5, 5, 10]) {
        if (normalized <= step) return step * magnitude;
      }
      return 10 * magnitude;
    }

    function formatDeltaSeconds(value) {
      if (!Number.isFinite(value)) return '--';
      const sign = value > 0 ? '+' : value < 0 ? '-' : '';
      const absolute = Math.abs(value);
      if (absolute >= 60) {
        const minutes = Math.floor(absolute / 60);
        return `${sign}${minutes}:${(absolute % 60).toFixed(1).padStart(4, '0')}`;
      }
      return `${sign}${absolute.toFixed(1)}s`;
    }

    function formatChangeSeconds(value) {
      if (!Number.isFinite(value)) return '--';
      if (Math.abs(value) < 0.05) return '0.0';
      return `${value > 0 ? '+' : ''}${value.toFixed(1)}`;
    }

    function formatFocusedTrendChangeSeconds(value) {
      return formatChangeSeconds(value);
    }

    function formatTrendWindow(seconds) {
      if (!Number.isFinite(seconds) || seconds <= 0) return '--';
      return seconds >= 3600 ? `${(seconds / 3600).toFixed(seconds >= 36000 ? 0 : 1)}h` : `${Math.round(seconds / 60)}m`;
    }

    function numberOr(...values) {
      for (const value of values) {
        const number = Number(value);
        if (Number.isFinite(number)) return number;
      }
      return 0;
    }

    function clamp01(value) {
      return Math.max(0, Math.min(1, Number.isFinite(value) ? value : 0));
    }

    function colorWithAlpha(color, alpha) {
      const parsed = parseHexColor(color);
      if (parsed) return `rgba(${parsed.r}, ${parsed.g}, ${parsed.b}, ${alpha})`;
      return color;
    }

    function formatSignedGap(value) {
      if (!Number.isFinite(value)) return '--';
      if (Math.abs(value) >= 60) {
        const minutes = Math.floor(Math.abs(value) / 60);
        const seconds = Math.abs(value) - minutes * 60;
        return `+${minutes}:${seconds.toFixed(1).padStart(4, '0')}`;
      }
      return `+${value.toFixed(1)}s`;
    }

    function displayModelHeaders(model) {
      return (Array.isArray(model?.columns) ? model.columns : []).map((column, index) => ({
        label: column.label,
        dataKey: column.dataKey,
        width: column.width,
        align: column.alignment,
        value: (row) => escapeHtml((row?.cells || [])[index] || '')
      }));
    }

    function bar(label, value, color) {
      const percent = Number.isFinite(value) ? Math.max(0, Math.min(1, value)) * 100 : 0;
      return `
        <div class="bar-row">
          <div class="muted">${escapeHtml(label)}</div>
          <div class="bar"><span style="width:${percent.toFixed(0)}%; background:${color};"></span></div>
          <div>${formatPercent(Number.isFinite(value) ? value : null)}</div>
        </div>`;
    }

    {{MODULE_SCRIPT}}

    function render() {
      const module = browserOverlay.module;
      if (!module?.render) {
        contentEl.innerHTML = '<div class="empty">Unknown overlay route.</div>';
        statusEl.textContent = 'not configured';
        clearFooterSource();
        if (timeRemainingEl) {
          timeRemainingEl.hidden = true;
          timeRemainingEl.textContent = '';
        }
        return;
      }

      module.render();
    }

    async function refresh() {
      const module = browserOverlay.module;
      try {
        if (module?.beforeRefresh) {
          await module.beforeRefresh();
        }

        render();
      } catch (error) {
        if (module?.renderOffline) {
          module.renderOffline(error);
          return;
        }

        statusEl.textContent = 'localhost offline';
        clearFooterSource();
        if (timeRemainingEl) {
          timeRemainingEl.hidden = true;
          timeRemainingEl.textContent = '';
        }
        contentEl.innerHTML = `<div class="empty">${escapeHtml(error.message)}</div>`;
      }
    }

    const module = browserOverlay.module;
    if (module?.start) {
      module.start({ refresh });
    } else {
      refresh();
      setInterval(refresh, page.refreshIntervalMilliseconds || 250);
    }
