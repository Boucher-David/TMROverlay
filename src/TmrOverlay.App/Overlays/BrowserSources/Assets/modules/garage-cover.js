let garageCoverSettings = { hasImage: false, imageVersion: null, fallbackReason: 'not_configured', previewVisible: false };
let garageCoverDisplayModel = null;
let lastGarageCoverRenderKey = '';

TmrBrowserOverlay.register({
  async beforeRefresh() {
    garageCoverDisplayModel = await fetchOverlayModel('garage-cover');
  },
  render() {
    renderGarageCover(garageCoverDisplayModel);
  },
  renderOffline() {
    renderGarageCover(null);
  }
});

function renderGarageCover(model) {
  if (model?.shouldRender === false) {
    modelRootOpacity = rootOpacityFromModel(model);
    applyOverlayOpacity(0);
    lastGarageCoverRenderKey = '';
    contentEl.innerHTML = '';
    renderHeaderItems(model, '');
    clearFooterSource();
    return;
  }

  const garageCover = model?.garageCover;
  const settings = garageCover?.browserSettings || garageCoverSettings;
  const detection = garageCover?.detection || { displayText: 'localhost offline', isFresh: false };
  const shouldCover = garageCover?.shouldCover ?? true;
  modelRootOpacity = rootOpacityFromModel(model);
  applyOverlayOpacity(shouldCover ? 1 : 0);

  const renderKey = `${settings.hasImage}:${settings.imageVersion ?? ''}:${settings.fallbackReason ?? ''}`;
  if (renderKey !== lastGarageCoverRenderKey) {
    lastGarageCoverRenderKey = renderKey;
    contentEl.innerHTML = garageCoverContent(settings);
  }

  renderHeaderItems(model, model?.status || detection.displayText || 'garage cover');
  renderFooterSource(model);
}

function garageCoverContent(settings) {
  if (settings?.hasImage) {
    const version = encodeURIComponent(settings.imageVersion || 'latest');
    return `
      <div class="garage-cover">
        <img alt="" src="/api/garage-cover/image?v=${version}" onerror="renderGarageCoverTextFallback(this);">
      </div>`;
  }

  return `
    <div class="garage-cover">
      <img alt="" src="/api/garage-cover/default-image?v=stock" onerror="renderGarageCoverTextFallback(this);">
    </div>`;
}

function renderGarageCoverTextFallback(image) {
  const parent = image?.parentElement;
  if (!parent) {
    return;
  }

  if (image.dataset.fallback !== 'default' && !String(image.getAttribute('src') || '').includes('/api/garage-cover/default-image')) {
    image.dataset.fallback = 'default';
    image.src = '/api/garage-cover/default-image?v=stock';
    return;
  }

  parent.classList.add('garage-cover-fallback');
  image.remove();
  parent.innerHTML = '<div>TMR</div>';
}
