const streamChatState = {
  initialized: false,
  pollTimer: null,
  pollActive: false,
  lastModel: null,
  badgeRegistry: {
    global: null,
    channels: new Map(),
    pending: new Set()
  }
};

TmrBrowserOverlay.register({
  start() {
    startStreamChatPolling();
  },
  render(model) {
    if (model?.streamChat) {
      renderStreamChatModel(model);
      return;
    }

    startStreamChatPolling();
  }
});

function startStreamChatPolling() {
  if (streamChatState.initialized) {
    return;
  }

  streamChatState.initialized = true;
  pollStreamChatModel();
  const refreshInterval = Math.max(250, Number(page.refreshIntervalMilliseconds) || 250);
  streamChatState.pollTimer = setInterval(pollStreamChatModel, refreshInterval);
  window.addEventListener('resize', () => {
    if (streamChatState.lastModel) {
      renderStreamChatModel(streamChatState.lastModel);
    }
  });
}

async function pollStreamChatModel() {
  if (streamChatState.pollActive) {
    return;
  }

  streamChatState.pollActive = true;
  try {
    const model = await fetchOverlayModel('stream-chat');
    renderStreamChatModel(model);
  } catch (error) {
    postBrowserSourceEvent('model-error', null, error);
    renderStreamChatLines([
      { name: 'TMR', text: `Chat model unavailable: ${error.message}`, kind: 'error' }
    ]);
    clearStreamChatChrome();
  } finally {
    streamChatState.pollActive = false;
  }
}

function renderStreamChatModel(model) {
  streamChatState.lastModel = model;
  if (model?.shouldRender === false) {
    postBrowserSourceEvent('model-hidden', model);
    modelRootOpacity = rootOpacityFromModel(model);
    applyOverlayOpacity(0);
    contentEl.innerHTML = '';
    clearStreamChatChrome();
    return;
  }

  postBrowserSourceEvent(model ? 'model-render' : 'model-null', model);
  modelRootOpacity = rootOpacityFromModel(model);
  applyOverlayOpacity(1);
  const rows = model?.streamChat?.rows || [];
  scheduleBadgeRegistryLoads(rows);
  renderStreamChatLines(rows.length ? rows : [
    { name: 'TMR', text: 'Choose Streamlabs or Twitch in the Stream Chat settings tab.', kind: 'system' }
  ]);
  renderStreamChatChrome(model);
}

function renderStreamChatLines(lines) {
  const visibleLines = latestVisibleChatLines(lines);
  contentEl.innerHTML = `
    <div class="chat">
      ${visibleLines.map((line) => `
        <div class="chat-line ${escapeAttribute(line.kind || 'message')}">
          <div class="chat-head">
            ${badgesHtml(line)}
            <span class="chat-name"${authorColorStyle(line)}>${escapeHtml(line.name)}</span>
            ${metadataHtml(line)}
          </div>
          <div class="chat-text">${segmentsHtml(line)}</div>
        </div>`).join('')}
    </div>`;
  pruneOverflowingChatLines();
}

function latestVisibleChatLines(lines) {
  const rows = Array.isArray(lines) ? lines : [];
  const minimumHeight = numberOr(streamChatGeometry?.minimumAvailableHeight, 48);
  const rowGap = numberOr(streamChatGeometry?.rowGap, 8);
  const rowBudget = numberOr(streamChatGeometry?.rowBudget, 52);
  const maxRows = Math.max(1, Math.round(numberOr(streamChatGeometry?.maxRows, 36)));
  const availableHeight = Math.max(
    minimumHeight,
    contentEl?.clientHeight || window.innerHeight - numberOr(streamChatGeometry?.availableHeightFallbackOffset, 42));
  const budget = Math.max(1, Math.min(maxRows, Math.floor((availableHeight + rowGap) / rowBudget)));
  return rows.slice(-budget);
}

function pruneOverflowingChatLines() {
  const chat = contentEl.querySelector('.chat');
  if (!chat) {
    return;
  }

  while (chat.scrollHeight - chat.clientHeight > 1) {
    const firstLine = chat.querySelector('.chat-line');
    const lineCount = chat.querySelectorAll('.chat-line').length;
    if (!firstLine || lineCount <= 1) {
      break;
    }

    firstLine.remove();
  }
}

function clearStreamChatChrome() {
  renderHeaderItems({ headerItems: [] }, '');
  clearFooterSource();
}

function renderStreamChatChrome(model) {
  renderHeaderItems(model, model?.status || 'Stream Chat');
  clearFooterSource();
}

function metadataHtml(line) {
  const metadata = Array.isArray(line?.metadata) ? line.metadata.filter(Boolean) : [];
  if (!metadata.length) {
    return '';
  }

  return `<span class="chat-meta">${metadata
    .map((item) => `<span class="chat-chip">${escapeHtml(item)}</span>`)
    .join('')}</span>`;
}

function badgesHtml(line) {
  const badges = Array.isArray(line?.badges) ? line.badges.filter((badge) => badge?.label) : [];
  if (!badges.length) {
    return '';
  }

  return `<span class="chat-badges">${badges
    .map((badge) => badgeImageHtml(badge) || `<span class="chat-badge fallback" title="${escapeAttribute(badgeTitle(badge))}">${escapeHtml(shortBadgeFallback(badge.label))}</span>`)
    .join('')}</span>`;
}

function badgeTitle(badge) {
  const id = String(badge?.id || '').trim();
  const version = String(badge?.version || '').trim();
  return version ? `${id} ${version}` : id;
}

function badgeImageHtml(badge) {
  const imageUrl = badgeImageUrl(badge);
  if (!imageUrl) {
    return '';
  }

  return `<img class="chat-badge image" src="${escapeAttribute(imageUrl)}" alt="${escapeAttribute(badge.label || badge.id || 'badge')}" title="${escapeAttribute(badgeTitle(badge))}" loading="lazy" decoding="async">`;
}

function badgeImageUrl(badge) {
  const id = String(badge?.id || '').trim();
  const version = String(badge?.version || '').trim();
  const roomId = String(badge?.roomId || '').trim();
  return lookupBadgeImage(streamChatState.badgeRegistry.channels.get(roomId), id, version)
    || lookupBadgeImage(streamChatState.badgeRegistry.global, id, version);
}

function lookupBadgeImage(registry, id, version) {
  if (!registry || !id) {
    return null;
  }

  const versions = registry.get(id);
  return versions?.get(version) || versions?.get('0') || versions?.values().next().value || null;
}

function shortBadgeFallback(label) {
  const compact = String(label || '').trim();
  if (!compact) {
    return '';
  }

  return compact.length <= 3 ? compact : compact.slice(0, 3);
}

function scheduleBadgeRegistryLoads(rows) {
  const badges = rows.flatMap((row) => Array.isArray(row?.badges) ? row.badges : []);
  if (!badges.length) {
    return;
  }

  loadBadgeRegistry('global');
  for (const badge of badges) {
    const roomId = String(badge?.roomId || '').trim();
    if (roomId) {
      loadBadgeRegistry(roomId);
    }
  }
}

function loadBadgeRegistry(scope) {
  const registry = streamChatState.badgeRegistry;
  if ((scope === 'global' && registry.global) || (scope !== 'global' && registry.channels.has(scope)) || registry.pending.has(scope)) {
    return;
  }

  registry.pending.add(scope);
  const url = scope === 'global'
    ? 'https://badges.twitch.tv/v1/badges/global/display'
    : `https://badges.twitch.tv/v1/badges/channels/${encodeURIComponent(scope)}/display`;
  fetch(url)
    .then((response) => response.ok ? response.json() : null)
    .then((payload) => {
      const parsed = parseBadgeRegistry(payload);
      if (scope === 'global') {
        registry.global = parsed;
      } else {
        registry.channels.set(scope, parsed);
      }

      if (streamChatState.lastModel) {
        renderStreamChatModel(streamChatState.lastModel);
      }
    })
    .catch(() => {
      if (scope === 'global') {
        registry.global = new Map();
      } else {
        registry.channels.set(scope, new Map());
      }
    })
    .finally(() => registry.pending.delete(scope));
}

function parseBadgeRegistry(payload) {
  const registry = new Map();
  const badgeSets = payload?.badge_sets || {};
  for (const [id, badgeSet] of Object.entries(badgeSets)) {
    const versions = new Map();
    for (const [version, entry] of Object.entries(badgeSet?.versions || {})) {
      const url = entry?.image_url_2x || entry?.image_url_1x || entry?.image_url_4x;
      if (typeof url === 'string' && /^https:\/\/static-cdn\.jtvnw\.net\/badges\/v1\//i.test(url)) {
        versions.set(version, url);
      }
    }
    if (versions.size) {
      registry.set(id, versions);
    }
  }
  return registry;
}

function segmentsHtml(line) {
  const segments = Array.isArray(line?.segments) ? line.segments : [];
  if (!segments.length) {
    return escapeHtml(line?.text || '');
  }

  return segments.map((segment) => {
    if (segment?.kind === 'emote' && validHttpImageUrl(segment.imageUrl)) {
      return `<img class="chat-emote" src="${escapeAttribute(segment.imageUrl)}" alt="${escapeAttribute(segment.text || 'emote')}" title="${escapeAttribute(segment.text || 'emote')}" loading="lazy" decoding="async">`;
    }

    return escapeHtml(segment?.text || '');
  }).join('');
}

function validHttpImageUrl(value) {
  const url = String(value || '').trim();
  return /^https:\/\/static-cdn\.jtvnw\.net\/emoticons\/v2\/[^/]+\/[^/]+\/[^/]+\/[^/]+$/i.test(url);
}

function authorColorStyle(line) {
  const color = typeof line?.authorColorHex === 'string' ? line.authorColorHex.trim() : '';
  return /^#[0-9a-f]{6}$/i.test(color)
    ? ` style="color: ${escapeAttribute(color)}"`
    : '';
}
