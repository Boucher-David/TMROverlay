let flagsDisplayModel = null;

TmrBrowserOverlay.register({
  async beforeRefresh() {
    flagsDisplayModel = await fetchOverlayModel('flags');
  },
  render() {
    renderFlags(flagsDisplayModel);
  },
  renderOffline() {
    clearFlagsSurface();
    renderHeaderItems(null, '');
    clearFooterSource();
  }
});

function renderFlags(model) {
  const flags = Array.isArray(model?.flags?.flags) ? model.flags.flags : [];
  if (model?.shouldRender === false || flags.length === 0) {
    clearFlagsSurface();
    renderHeaderItems(model, '');
    clearFooterSource();
    return;
  }

  contentEl.innerHTML = flagsSvg(flags);
  renderHeaderItems(model, model?.status || '');
  renderFooterSource(model);
}

function clearFlagsSurface() {
  contentEl.innerHTML = '';
}

function flagsSvg(flags) {
  const width = Math.max(180, window.innerWidth || 360);
  const height = Math.max(96, window.innerHeight || 170);
  const padding = 8;
  const gap = 8;
  const { columns, rows } = gridFor(flags.length);
  const bounds = {
    x: padding,
    y: padding,
    width: Math.max(1, width - padding * 2),
    height: Math.max(1, height - padding * 2)
  };
  const cellWidth = (bounds.width - (columns - 1) * gap) / columns;
  const cellHeight = (bounds.height - (rows - 1) * gap) / rows;
  const cells = flags.map((flag, index) => {
    const row = Math.floor(index / columns);
    const column = index % columns;
    return flagCell(flag, {
      x: bounds.x + column * (cellWidth + gap),
      y: bounds.y + row * (cellHeight + gap),
      width: cellWidth,
      height: cellHeight
    }, index);
  }).join('');

  return `
    <svg class="flags-v2" viewBox="0 0 ${number(width)} ${number(height)}" role="img" aria-label="Flags">
      ${cells}
    </svg>`;
}

function flagCell(flag, cell, index) {
  const compact = cell.height < 92 || cell.width < 132;
  const poleX = cell.x + Math.max(12, cell.width * 0.16);
  const poleTop = cell.y + 4;
  const poleBottom = cell.y + cell.height - 2;
  const clothLeft = poleX + 1;
  const clothWidth = Math.max(48, cell.x + cell.width - clothLeft - 8);
  const clothHeight = Math.max(24, Math.min(cell.height * 0.7, clothWidth * 0.58));
  const clothTop = cell.y + Math.max(4, (cell.height - clothHeight) * 0.32);
  const cloth = { x: clothLeft, y: clothTop, width: clothWidth, height: clothHeight };
  const path = flagPath(cloth, compact ? 3.5 : 5.5, index);
  const title = [flag?.label, flag?.detail].filter(Boolean).join(' | ');

  return `
    <g class="flag-cell flag-${className(flag?.kind)}">
      <title>${escapeHtml(title || 'Flag')}</title>
      <line x1="${number(poleX + 1)}" y1="${number(poleTop + 1)}" x2="${number(poleX + 1)}" y2="${number(poleBottom + 1)}" stroke="rgba(0,0,0,0.47)" stroke-width="${compact ? 2 : 3}" stroke-linecap="round"></line>
      <line x1="${number(poleX)}" y1="${number(poleTop)}" x2="${number(poleX)}" y2="${number(poleBottom)}" stroke="rgba(214,220,226,0.88)" stroke-width="${compact ? 2 : 3}" stroke-linecap="round"></line>
      ${flagCloth(flag, path, cloth)}
    </g>`;
}

function flagCloth(flag, path, cloth) {
  const kind = String(flag?.kind || '').toLowerCase();
  if (kind === 'checkered') {
    return checkeredFlag(path, cloth);
  }

  const fill = fillColor(kind);
  const outline = kind === 'white' ? 'rgba(26,30,34,0.86)' : 'rgba(255,255,255,0.67)';
  const extras = [];
  if (kind === 'meatball') {
    const diameter = Math.min(cloth.width, cloth.height) * 0.44;
    extras.push(`<circle cx="${number(cloth.x + cloth.width / 2)}" cy="${number(cloth.y + cloth.height / 2)}" r="${number(diameter / 2)}" fill="rgb(245,124,38)"></circle>`);
  } else if (kind === 'caution') {
    extras.push(cautionStripes(path, cloth));
  }

  return `
    <path d="${path}" fill="${fill}"></path>
    ${extras.join('')}
    <path d="${path}" fill="none" stroke="${outline}" stroke-width="1.4" stroke-linejoin="round"></path>`;
}

function checkeredFlag(path, cloth) {
  const columns = 6;
  const rows = 4;
  const squareWidth = cloth.width / columns;
  const squareHeight = cloth.height / rows;
  const id = `flagClip${Math.round(cloth.x * 10)}_${Math.round(cloth.y * 10)}`;
  const squares = [];
  for (let row = 0; row < rows; row += 1) {
    for (let column = 0; column < columns; column += 1) {
      if ((row + column) % 2 === 0) continue;
      squares.push(`<rect x="${number(cloth.x + column * squareWidth)}" y="${number(cloth.y + row * squareHeight)}" width="${number(squareWidth + 1)}" height="${number(squareHeight + 1)}" fill="rgb(8,10,12)"></rect>`);
    }
  }

  return `
    <defs><clipPath id="${id}"><path d="${path}"></path></clipPath></defs>
    <g clip-path="url(#${id})">
      <rect x="${number(cloth.x)}" y="${number(cloth.y)}" width="${number(cloth.width)}" height="${number(cloth.height)}" fill="rgb(245,247,250)"></rect>
      ${squares.join('')}
    </g>
    <path d="${path}" fill="none" stroke="rgba(26,30,34,0.86)" stroke-width="1.4" stroke-linejoin="round"></path>`;
}

function cautionStripes(path, cloth) {
  const stripeWidth = Math.max(8, cloth.width * 0.12);
  const id = `cautionClip${Math.round(cloth.x * 10)}_${Math.round(cloth.y * 10)}`;
  const stripes = [];
  for (let x = cloth.x - cloth.height; x < cloth.x + cloth.width; x += stripeWidth * 2.5) {
    stripes.push(`<polygon points="${number(x)},${number(cloth.y + cloth.height)} ${number(x + stripeWidth)},${number(cloth.y + cloth.height)} ${number(x + stripeWidth + cloth.height)},${number(cloth.y)} ${number(x + cloth.height)},${number(cloth.y)}" fill="rgba(0,0,0,0.28)"></polygon>`);
  }

  return `
    <defs><clipPath id="${id}"><path d="${path}"></path></clipPath></defs>
    <g clip-path="url(#${id})">${stripes.join('')}</g>`;
}

function flagPath(bounds, wave, index) {
  const phase = index % 2 === 0 ? 1 : -1;
  const leftTop = { x: bounds.x, y: bounds.y };
  const rightTop = { x: bounds.x + bounds.width, y: bounds.y + wave * phase };
  const rightBottom = { x: bounds.x + bounds.width, y: bounds.y + bounds.height + wave * 0.4 * phase };
  const leftBottom = { x: bounds.x, y: bounds.y + bounds.height };
  return [
    `M ${number(leftTop.x)} ${number(leftTop.y)}`,
    `C ${number(bounds.x + bounds.width * 0.28)} ${number(bounds.y - wave * phase)} ${number(bounds.x + bounds.width * 0.62)} ${number(bounds.y + wave * phase)} ${number(rightTop.x)} ${number(rightTop.y)}`,
    `L ${number(rightBottom.x)} ${number(rightBottom.y)}`,
    `C ${number(bounds.x + bounds.width * 0.62)} ${number(bounds.y + bounds.height - wave * phase)} ${number(bounds.x + bounds.width * 0.28)} ${number(bounds.y + bounds.height + wave * phase)} ${number(leftBottom.x)} ${number(leftBottom.y)}`,
    'Z'
  ].join(' ');
}

function fillColor(kind) {
  switch (kind) {
    case 'green':
      return 'rgb(48,214,109)';
    case 'blue':
      return 'rgb(55,162,255)';
    case 'yellow':
    case 'caution':
      return 'rgb(255,207,74)';
    case 'red':
      return 'rgb(236,76,86)';
    case 'black':
    case 'meatball':
      return 'rgb(8,10,12)';
    case 'white':
      return 'rgb(246,248,250)';
    default:
      return 'rgb(255,255,255)';
  }
}

function gridFor(count) {
  if (count <= 1) return { columns: 1, rows: 1 };
  if (count === 2) return { columns: 2, rows: 1 };
  if (count <= 4) return { columns: 2, rows: 2 };
  if (count <= 6) return { columns: 3, rows: 2 };
  return { columns: 4, rows: Math.ceil(count / 4) };
}

function className(value) {
  return String(value || 'unknown').replace(/[^a-z0-9_-]/gi, '').toLowerCase() || 'unknown';
}

function number(value) {
  return Number.isFinite(Number(value)) ? Number(value).toFixed(1).replace(/\.0$/, '') : '0';
}
