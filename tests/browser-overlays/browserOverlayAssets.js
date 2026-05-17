import { readFileSync } from 'node:fs';
import { dirname, resolve } from 'node:path';
import { fileURLToPath } from 'node:url';

export const repoRoot = resolve(dirname(fileURLToPath(import.meta.url)), '../..');
export const browserAssetRoot = resolve(repoRoot, 'src/TmrOverlay.App/Overlays/BrowserSources/Assets');

export const pages = {
  standings: pageDefinition('standings', 'Standings', '/overlays/standings', {
    fadeWhenTelemetryUnavailable: true,
    modelRoute: '/api/overlay-model/standings',
    settingsRoute: '/api/standings',
    settingsProperty: 'standingsSettings'
  }),
  relative: pageDefinition('relative', 'Relative', '/overlays/relative', {
    bodyClass: 'relative-page',
    fadeWhenTelemetryUnavailable: true,
    modelRoute: '/api/overlay-model/relative',
    settingsRoute: '/api/relative',
    settingsProperty: 'relativeSettings'
  }),
  'fuel-calculator': pageDefinition('fuel-calculator', 'Fuel Calculator', '/overlays/fuel-calculator', {
    aliases: ['/overlays/calculator'],
    bodyClass: 'fuel-calculator-page',
    fadeWhenTelemetryUnavailable: true,
    modelRoute: '/api/overlay-model/fuel-calculator'
  }),
  'session-weather': pageDefinition('session-weather', 'Session / Weather', '/overlays/session-weather', {
    bodyClass: 'session-weather-page',
    fadeWhenTelemetryUnavailable: true,
    modelRoute: '/api/overlay-model/session-weather'
  }),
  'pit-service': pageDefinition('pit-service', 'Pit Service', '/overlays/pit-service', {
    fadeWhenTelemetryUnavailable: true,
    modelRoute: '/api/overlay-model/pit-service'
  }),
  'input-state': pageDefinition('input-state', 'Inputs', '/overlays/input-state', {
    aliases: ['/overlays/inputs'],
    bodyClass: 'input-state-page',
    renderWhenTelemetryUnavailable: true,
    fadeWhenTelemetryUnavailable: true,
    refreshIntervalMilliseconds: 50,
    modelRoute: '/api/overlay-model/input-state',
    settingsRoute: '/api/input-state',
    settingsProperty: 'inputStateSettings'
  }),
  'car-radar': pageDefinition('car-radar', 'Car Radar', '/overlays/car-radar', {
    bodyClass: 'car-radar-page',
    renderWhenTelemetryUnavailable: true,
    fadeWhenTelemetryUnavailable: true,
    refreshIntervalMilliseconds: 100,
    modelRoute: '/api/overlay-model/car-radar'
  }),
  'gap-to-leader': pageDefinition('gap-to-leader', 'Gap To Leader', '/overlays/gap-to-leader', {
    fadeWhenTelemetryUnavailable: true,
    modelRoute: '/api/overlay-model/gap-to-leader'
  }),
  'track-map': pageDefinition('track-map', 'Track Map', '/overlays/track-map', {
    bodyClass: 'track-map-page',
    renderWhenTelemetryUnavailable: true,
    fadeWhenTelemetryUnavailable: true,
    refreshIntervalMilliseconds: 100,
    modelRoute: '/api/overlay-model/track-map',
    settingsRoute: '/api/track-map',
    settingsProperty: 'trackMapSettings'
  }),
  flags: pageDefinition('flags', 'Flags', '/overlays/flags', {
    bodyClass: 'flags-page',
    renderWhenTelemetryUnavailable: true,
    fadeWhenTelemetryUnavailable: true,
    modelRoute: '/api/overlay-model/flags'
  }),
  'garage-cover': pageDefinition('garage-cover', 'Garage Cover', '/overlays/garage-cover', {
    bodyClass: 'garage-cover-page',
    renderWhenTelemetryUnavailable: true,
    fadeWhenTelemetryUnavailable: false,
    modelRoute: '/api/overlay-model/garage-cover',
    settingsRoute: '/api/garage-cover',
    settingsProperty: 'garageCover'
  }),
  'stream-chat': pageDefinition('stream-chat', 'Stream Chat', '/overlays/stream-chat', {
    bodyClass: 'stream-chat-page',
    requiresTelemetry: false,
    modelRoute: '/api/overlay-model/stream-chat',
    settingsRoute: '/api/stream-chat',
    settingsProperty: 'streamChat'
  })
};

const opacityExcludedOverlayIds = new Set([
  'stream-chat',
  'car-radar',
  'flags',
  'garage-cover',
  'track-map'
]);

export function browserOverlayPages() {
  return Object.values(pages);
}

export function browserOverlayPage(nameOrRoute) {
  const normalized = normalizeRoute(nameOrRoute);
  const page = pages[nameOrRoute]
    || Object.values(pages).find((candidate) =>
      candidate.page.id === nameOrRoute
      || candidate.route === normalized
      || candidate.aliases.includes(normalized));
  if (!page) {
    throw new Error(`Unknown browser overlay page: ${nameOrRoute}`);
  }

  return page;
}

export function renderOverlayIndexHtml(port = 8765) {
  const links = [
    '<a href="/review/app">Settings App Review</a>',
    '<a href="/review/settings/general">Settings - General</a>',
    '<a href="/review/installer">Windows Installer Review</a>',
    ...Object.values(pages)
    .map((page) => `<a href="${page.route}">${page.title}</a>`)
  ].join('\n');
  return assetText('templates/index.html')
    .replace('{{PORT}}', String(port))
    .replace('{{LINKS}}', links)
    .replace('{{INDEX_CSS}}', assetText('styles/index.css'));
}

export function renderInstallerReviewHtml({ menuId = 'welcome' } = {}) {
  const normalizedMenuId = normalizeInstallerMenuId(menuId);
  const installerCss = assetText('styles/installer-review.css');
  const welcome = installerWelcomeContent();
  const windowHtml = installerWindowHtml(normalizedMenuId, welcome);

  return assetText('templates/installer-review.html')
    .replace('{{TITLE}}', 'Tech Mates Racing Overlay Setup')
    .replace('{{INSTALLER_CSS}}', installerCss)
    .replace('{{MENU_ID}}', normalizedMenuId)
    .replace('{{MENU_LINKS}}', installerMenuLinks(normalizedMenuId))
    .replace('{{INSTALLER_HTML}}', windowHtml);
}

export function renderSettingsGeneralReviewHtml({ previewMode = 'off', reviewState = null } = {}) {
  return renderSettingsReviewHtml({
    previewMode,
    selectedTab: 'general',
    selectedRegion: 'general',
    reviewState
  });
}

export function renderAppValidatorReviewHtml({ previewMode = 'off', selectedTab = 'general', selectedRegion = 'general', reviewState = null } = {}) {
  return renderSettingsReviewHtml({
    previewMode,
    selectedTab,
    selectedRegion,
    reviewState
  });
}

function normalizeInstallerMenuId(value) {
  const normalized = String(value || '').trim().toLowerCase();
  if (normalized === 'options' || normalized === 'install-options') {
    return 'installer-page-02';
  }
  return installerMenuDefinitions().some((menu) => menu.id === normalized) ? normalized : 'welcome';
}

function installerMenuDefinitions() {
  return [
    { id: 'welcome', label: 'Welcome' },
    { id: 'installer-page-02', label: 'Install Scope' },
    { id: 'ready-to-install', label: 'Ready' },
    { id: 'cancel-confirm', label: 'Cancel' }
  ];
}

function installerMenuLinks(activeMenuId) {
  return installerMenuDefinitions()
    .map((menu) => {
      const current = menu.id === activeMenuId ? ' aria-current="page"' : '';
      return `<a class="installer-review-link" href="/review/installer?menu=${menu.id}"${current}>${menu.label}</a>`;
    })
    .join('');
}

function installerWindowHtml(menuId, welcome) {
  if (menuId === 'cancel-confirm') {
    return installerCancelConfirmWindowHtml();
  }

  const dialogUri = assetDataUri('assets/brand/TMRMsiLogo.bmp', 'image/bmp');
  const logoUri = assetDataUri('assets/brand/TMRLogo.png', 'image/png');
  const bannerUri = assetDataUri('assets/brand/TMRMsiBanner.bmp', 'image/bmp');
  const menu = installerMenuModel(menuId, welcome);
  return `
    <section class="installer-window" data-menu-id="${escapeAttribute(menu.id)}" aria-label="Tech Mates Racing Overlay Setup">
      <header class="installer-titlebar">
        <img class="installer-titlebar-icon" src="${logoUri}" alt="" aria-hidden="true">
        <span class="installer-titlebar-title">Tech Mates Racing Overlay Setup</span>
      </header>
      <div class="installer-body ${menu.visual === 'splash' ? 'with-splash' : 'with-banner'}">
        ${menu.visual === 'splash' ? installerDialogBitmapHtml(dialogUri) : installerBannerHtml(bannerUri)}
        <main class="installer-content">
          <h1 class="installer-heading">${escapeMarkup(menu.heading)}</h1>
          <div class="installer-copy">${menu.bodyHtml}</div>
        </main>
      </div>
      <footer class="installer-footer">
        ${installerButton('&Back', menu.backHref, { disabled: menu.backDisabled })}
        ${installerButton(menu.nextLabel, menu.nextHref, { primary: true, disabled: menu.nextDisabled })}
        ${installerButton('Cancel', '/review/installer?menu=cancel-confirm')}
      </footer>
    </section>`;
}

function installerMenuModel(menuId, welcome) {
  if (menuId === 'installer-page-02') {
    return {
      id: menuId,
      visual: 'banner',
      heading: 'Installation Scope',
      bodyHtml: `
        <p class="installer-text installer-subtitle">Choose the installation scope and folder</p>
        <div class="installer-maintenance-options">
          ${installerMaintenanceOption('Install &just for you (David)', 'Tech Mates Racing Overlay will be installed in a per-user folder and be available just for your user account. You do not need local Administrator privileges.', true)}
          ${installerMaintenanceOption('Install for all users of this &machine', 'Tech Mates Racing Overlay will be installed in a per-machine folder by default and be available for all users. You can change the default installation folder. You must have local Administrator privileges.')}
        </div>`,
      backHref: '/review/installer?menu=welcome',
      nextHref: '/review/installer?menu=ready-to-install',
      nextLabel: '&Next',
    };
  }
  if (menuId === 'ready-to-install') {
    return {
      id: menuId,
      visual: 'banner',
      heading: 'Ready to install Tech Mates Racing Overlay',
      bodyHtml: `
        <p class="installer-text">Click Install to begin the installation. Click Back to review or change any of your installation settings. Click Cancel to exit the wizard.</p>`,
      backHref: '/review/installer?menu=installer-page-02',
      nextHref: '/review/installer?menu=welcome',
      nextLabel: '&Install'
    };
  }
  return {
    id: 'welcome',
    visual: 'splash',
    heading: 'Welcome to the Tech Mates Racing Overlay Setup Wizard',
    bodyHtml: welcome.bodyHtml,
    backHref: null,
    nextHref: '/review/installer?menu=installer-page-02',
    nextLabel: '&Next',
    backDisabled: true
  };
}

function installerMaintenanceOption(label, description, disabled = false) {
  return `
    <div class="installer-maintenance-option">
      ${installerButton(label, null, { disabled, wide: true })}
      <p class="installer-text">${escapeMarkup(description)}</p>
    </div>`;
}

function installerBannerHtml(bannerUri) {
  return `
    <aside class="installer-banner">
      <img src="${bannerUri}" alt="">
    </aside>`;
}

function installerDialogBitmapHtml(dialogUri) {
  return `
    <aside class="installer-splash">
      <img src="${dialogUri}" alt="">
    </aside>`;
}

function installerCancelConfirmWindowHtml() {
  return `
    <section class="installer-window installer-cancel-window" data-menu-id="cancel-confirm" aria-label="Tech Mates Racing Overlay Setup">
      <header class="installer-titlebar">
        <span class="installer-titlebar-title">Tech Mates Racing Overlay Setup</span>
      </header>
      <main class="installer-cancel-body">
        <div class="installer-information-icon" aria-label="Information icon"></div>
        <p class="installer-text">Are you sure you want to cancel Tech Mates Racing Overlay installation?</p>
      </main>
      <footer class="installer-cancel-footer">
        ${installerButton('&Yes', '/review/installer?menu=welcome')}
        ${installerButton('&No', '/review/installer?menu=welcome')}
      </footer>
    </section>`;
}

function installerButton(label, href, { primary = false, danger = false, disabled = false, wide = false } = {}) {
  const classes = [
    'installer-button',
    primary ? 'primary' : '',
    danger ? 'danger' : '',
    disabled ? 'disabled' : '',
    wide ? 'wide' : ''
  ].filter(Boolean).join(' ');
  if (disabled || !href) {
    return `<button type="button" class="${classes}" disabled>${escapeMarkup(label)}</button>`;
  }
  return `<button type="button" class="${classes}" data-review-href="${escapeAttribute(href)}" onclick="window.location.href=this.dataset.reviewHref">${escapeMarkup(label)}</button>`;
}

function installerWelcomeContent() {
  return {
    bodyHtml: `<p class="installer-text">The Setup Wizard allows you to change the way Tech Mates Racing Overlay features are installed on your computer or to remove it from your computer. Click Next to continue or Cancel to exit the Setup Wizard.</p>`
  };
}

function assetDataUri(relativePath, mimeType) {
  return `data:${mimeType};base64,${readFileSync(resolve(repoRoot, relativePath)).toString('base64')}`;
}

function escapeMarkup(value) {
  return String(value)
    .replaceAll('&', '&amp;')
    .replaceAll('<', '&lt;')
    .replaceAll('>', '&gt;')
    .replaceAll('"', '&quot;')
    .replaceAll("'", '&#39;');
}

function escapeAttribute(value) {
  return escapeMarkup(value);
}

function renderSettingsReviewHtml({
  previewMode = 'off',
  selectedTab = 'general',
  selectedRegion = 'general',
  reviewState = null
}) {
  const settingsCss = assetText('styles/settings-general.css')
    .replace('{{THEME_CSS_VARIABLES}}', themeCssVariables());
  const config = settingsAppConfig({
    previewMode,
    selectedTab,
    selectedRegion,
    reviewState
  });

  return assetText('templates/settings-general.html')
    .replace('{{SETTINGS_CSS}}', settingsCss)
    .replace('{{APP_CONFIG_JSON}}', escapeHtml(JSON.stringify(config)));
}

function escapeHtml(value) {
  return String(value)
    .replaceAll('&', '\\u0026')
    .replaceAll('<', '\\u003c')
    .replaceAll('>', '\\u003e')
    .replaceAll('\u2028', '\\u2028')
    .replaceAll('\u2029', '\\u2029');
}

export function renderOverlayHtml(name) {
  const page = browserOverlayPage(name);
  const overlayCss = assetText('styles/overlay.css')
    .replace('{{THEME_CSS_VARIABLES}}', themeCssVariables());
  const overlayScript = assetText('scripts/overlay-shell.js')
    .replace('{{PAGE_JSON}}', JSON.stringify(page.page))
    .replace('{{MODULE_SCRIPT}}', assetText(`modules/${page.module}.js`));

  return assetText('templates/overlay.html')
    .replaceAll('{{TITLE}}', page.title)
    .replace('{{BODY_CLASS}}', page.bodyClass)
    .replace('{{OVERLAY_CSS}}', overlayCss)
    .replace('{{OVERLAY_SCRIPT}}', overlayScript);
}

export function browserOverlayApiResponse(name, path, { live, settings = {}, model = null }) {
  const page = browserOverlayPage(name);
  if (page.modelRoute && path === page.modelRoute) {
    return { model: model ?? defaultDisplayModel(page, live, settings) };
  }
  if (path === page.settingsRoute) {
    if (page.page.id === 'track-map') {
      return {
        trackMap: settings.trackMap ?? null,
        trackMapSettings: settings.trackMapSettings ?? settings
      };
    }

    return { [page.settingsProperty]: settings };
  }
  if (path === '/api/snapshot') {
    return { live };
  }

  return null;
}

function settingsAppConfig({ previewMode = 'off', selectedTab = 'general', selectedRegion = 'general', reviewState = null } = {}) {
  return {
    previewMode: normalizePreviewMode(previewMode),
    selectedTab,
    selectedRegion,
    unitSystem: normalizeUnitSystem(reviewState?.unitSystem || 'Metric'),
    support: {
      rawCaptureEnabled: reviewState?.support?.rawCaptureEnabled === true,
      latestBundlePath: reviewState?.support?.latestBundlePath || '',
      statusText: reviewState?.support?.statusText || '',
      statusTone: reviewState?.support?.statusTone || 'neutral',
      updateText: reviewState?.support?.updateText || 'No update available.',
      canCheckUpdates: reviewState?.support?.canCheckUpdates ?? true,
      canInstallUpdate: reviewState?.support?.canInstallUpdate ?? false,
      canRestartUpdate: reviewState?.support?.canRestartUpdate ?? false,
      updatePendingRestart: reviewState?.support?.updatePendingRestart ?? false
    },
    sessionLabels: ['Practice', 'Qualifying', 'Race'],
    overlays: settingsAppOverlays(reviewState)
  };
}

function settingsAppOverlays(reviewState = null) {
  const order = [
    'standings',
    'relative',
    'gap-to-leader',
    'track-map',
    'stream-chat',
    'garage-cover',
    'fuel-calculator',
    'input-state',
    'car-radar',
    'flags',
    'session-weather',
    'pit-service'
  ];
  return order.map((id) => settingsOverlayDefinition(id, reviewState));
}

function settingsOverlayDefinition(id, reviewState = null) {
  const page = pages[id];
  const overlayState = reviewState?.overlays?.[id] || {};
  const sharedChrome = ['standings', 'relative', 'fuel-calculator', 'gap-to-leader', 'session-weather', 'pit-service'].includes(id);
  const noSessionFilters = ['stream-chat', 'gap-to-leader', 'flags'].includes(id);
  const noOpacity = ['stream-chat', 'car-radar', 'flags', 'garage-cover'].includes(id);
  return {
    id,
    title: page?.title || titleCase(id.replaceAll('-', ' ')),
    subtitle: settingsOverlaySubtitle(id),
    route: page?.route || null,
    browserSize: settingsBrowserSize(id, overlayState),
    enabled: overlayState.enabled ?? false,
    scalePercent: overlayState.scalePercent ?? 100,
    opacityPercent: overlayState.opacityPercent ?? (id === 'track-map' ? 0 : 100),
    showScale: true,
    showOpacity: !noOpacity,
    showSessionFilters: !noSessionFilters && id !== 'garage-cover',
    supportsChrome: sharedChrome,
    content: overlayState.content || {},
    sessions: overlayState.sessions ?? (id === 'gap-to-leader'
      ? { test: false, practice: false, qualifying: false, race: true }
      : { test: true, practice: true, qualifying: true, race: true }),
    providerLabel: providerLabelFromState(overlayState.provider),
    provider: overlayState.provider || 'twitch',
    twitchChannel: overlayState.twitchChannel || 'techmatesracing',
    streamlabsWidgetUrl: overlayState.streamlabsWidgetUrl || '',
    garageHasImage: overlayState.garageHasImage === true,
    garagePreviewVisible: overlayState.garagePreviewVisible === true,
    classSeparatorsEnabled: contentStateValue(
      overlayState,
      'standings.class-separators.enabled',
      'Multiclass sections',
      contentStateValue(overlayState, 'standings.class-separators.enabled', 'Class separators', true)),
    otherClassRows: overlayState.otherClassRows ?? 2,
    carsEachSide: overlayState.carsEachSide ?? 5,
    carsAhead: overlayState.carsAhead ?? 5,
    carsBehind: overlayState.carsBehind ?? 5,
    chrome: overlayState.chrome || {},
    headerRows: sharedChrome ? ['Status', 'Time remaining'] : [],
    footerRows: sharedChrome && id !== 'session-weather' ? ['Source'] : [],
    contentTitle: settingsContentTitle(id),
    gridColumns: ['session-weather', 'pit-service', 'stream-chat'].includes(id) ? 2 : 1,
    contentRows: settingsContentRows(id, overlayState)
  };
}

function providerLabelFromState(provider) {
  return provider === 'streamlabs' ? 'Streamlabs' : provider === 'none' ? 'Not configured' : 'Twitch';
}

function settingsOverlaySubtitle(id) {
  return {
    standings: 'Class and overall running order for the current session.',
    relative: 'Nearby-car timing around the local in-car reference.',
    'gap-to-leader': 'Focused class gap trend and nearby leader context.',
    'fuel-calculator': 'Fuel strategy, stint targets, and source confidence.',
    'session-weather': 'Session timing, track state, and weather telemetry.',
    'pit-service': 'Pit request state, service plan, and release context.',
    'track-map': 'Live car location and sector context.',
    'stream-chat': 'Local browser-source chat setup for Streamlabs or Twitch.',
    'garage-cover': 'Local browser-source privacy cover for garage and setup scenes.',
    'input-state': 'Input rail visibility for pedal, steering, gear, and speed telemetry.',
    'car-radar': 'Local proximity radar and multiclass approach warning controls.',
    flags: 'Compact session flag strip display and size controls.'
  }[id] || 'Overlay settings and browser-source controls.';
}

function settingsBrowserSize(id, overlayState = {}) {
  const base = {
    standings: [659, 334],
    relative: [360, 373],
    'gap-to-leader': [654, 357],
    'track-map': [360, 360],
    'stream-chat': [380, 520],
    'garage-cover': [1280, 720],
    'fuel-calculator': [503, 315],
    'input-state': [520, 260],
    'car-radar': [300, 300],
    flags: [360, 170],
    'session-weather': [464, 496],
    'pit-service': [530, 743]
  }[id] || [400, 300];
  if (id === 'input-state') {
    base[0] = inputStateBaseWidth(overlayState, base[0]);
  }
  const scale = Math.max(0.6, Math.min(2, Number(overlayState.scalePercent || 100) / 100));
  return `${Math.round(base[0] * scale)} x ${Math.round(base[1] * scale)}`;
}

function inputStateBaseWidth(overlayState, fullWidth) {
  const hasGraph = contentStateValue(overlayState, 'input-state.trace.throttle', 'Throttle trace', true)
    || contentStateValue(overlayState, 'input-state.trace.brake', 'Brake trace', true)
    || contentStateValue(overlayState, 'input-state.trace.clutch', 'Clutch trace', true);
  const hasRail = contentStateValue(overlayState, 'input-state.current.throttle', 'Throttle %', true)
    || contentStateValue(overlayState, 'input-state.current.brake', 'Brake %', true)
    || contentStateValue(overlayState, 'input-state.current.clutch', 'Clutch %', true)
    || contentStateValue(overlayState, 'input-state.current.steering', 'Steering wheel', true)
    || contentStateValue(overlayState, 'input-state.current.gear', 'Gear', true)
    || contentStateValue(overlayState, 'input-state.current.speed', 'Speed', true);
  if (hasGraph && hasRail) return fullWidth;
  if (hasGraph) return 380;
  return 276;
}

function settingsContentTitle(id) {
  return {
    'session-weather': 'Session / Weather Cells',
    'pit-service': 'Pit Service Cells',
    'stream-chat': 'Twitch Metadata'
  }[id] || 'Content Display';
}

function settingsContentRows(id, overlayState = {}) {
  const enabled = (label, value = true, extra = {}) => {
    const key = extra.key || settingsContentOptionKey(id, label) || label;
    return {
      label,
      key,
      defaultEnabled: value,
      enabled: contentStateValue(overlayState, key, label, value),
      ...extra
    };
  };
  switch (id) {
    case 'standings':
      return [
        enabled('Class position'),
        enabled('Car number'),
        enabled('Driver'),
        enabled('Class gap'),
        enabled('Focus interval'),
        enabled('Fastest lap'),
        enabled('Last lap'),
        enabled('Pit status')
      ];
    case 'relative':
      return [
        enabled('Relative position'),
        enabled('Driver'),
        enabled('Relative delta'),
        enabled('Pit status', false)
      ];
    case 'gap-to-leader':
      return [];
    case 'fuel-calculator':
      return [];
    case 'track-map':
      return [
        enabled('Sector boundaries')
      ];
    case 'stream-chat':
      return [
        enabled('Author color'),
        enabled('Badges'),
        enabled('Bits'),
        enabled('First message'),
        enabled('Replies'),
        enabled('Timestamps'),
        enabled('Emotes'),
        enabled('Alerts'),
        enabled('Message IDs', false)
      ];
    case 'input-state':
      return [
        enabled('Throttle trace'),
        enabled('Brake trace'),
        enabled('Clutch trace'),
        enabled('Throttle %'),
        enabled('Brake %'),
        enabled('Clutch %'),
        enabled('Steering wheel'),
        enabled('Gear'),
        enabled('Speed')
      ];
    case 'car-radar':
      return [
        enabled('Faster-class warning')
      ];
    case 'flags':
      return [
        enabled('Green'),
        enabled('Blue'),
        enabled('Yellow'),
        enabled('Red / black'),
        enabled('White / checkered')
      ];
    case 'session-weather':
      return [
        enabled('Session type'),
        enabled('Session name'),
        enabled('Session mode'),
        enabled('Elapsed time'),
        enabled('Remaining time'),
        enabled('Total time'),
        enabled('Event type'),
        enabled('Car'),
        enabled('Track name'),
        enabled('Track length'),
        enabled('Laps remaining'),
        enabled('Laps total'),
        enabled('Wetness'),
        enabled('Declared surface'),
        enabled('Rubber'),
        enabled('Skies'),
        enabled('Weather'),
        enabled('Rain'),
        enabled('Wind direction'),
        enabled('Wind speed'),
        enabled('Facing wind'),
        enabled('Air temp'),
        enabled('Track temp'),
        enabled('Humidity'),
        enabled('Fog'),
        enabled('Pressure')
      ];
    case 'pit-service':
      return [
        enabled('Session time'),
        enabled('Session laps'),
        enabled('Release'),
        enabled('Pit status'),
        enabled('Fuel requested'),
        enabled('Fuel selected'),
        enabled('Tearoff requested'),
        enabled('Required repair'),
        enabled('Optional repair'),
        enabled('Fast repair selected'),
        enabled('Fast repairs available'),
        enabled('Compound'),
        enabled('Change request'),
        enabled('Set limit'),
        enabled('Sets available'),
        enabled('Sets used'),
        enabled('Pressure'),
        enabled('Temperature'),
        enabled('Wear'),
        enabled('Distance')
      ];
    default:
      return [enabled('Content')];
  }
}

function contentStateValue(overlayState, key, label, defaultValue) {
  const content = overlayState.content || {};
  if (Object.hasOwn(content, key)) return content[key] !== false;
  if (Object.hasOwn(content, label)) return content[label] !== false;
  return defaultValue;
}

function settingsContentOptionKey(id, label) {
  const maps = {
    standings: {
      'Class position': 'standings.content.standings.class-position.enabled',
      'Car number': 'standings.content.standings.car-number.enabled',
      Driver: 'standings.content.standings.driver.enabled',
      'Class gap': 'standings.content.standings.gap.enabled',
      'Focus interval': 'standings.content.standings.interval.enabled',
      'Fastest lap': 'standings.content.standings.fastest-lap.enabled',
      'Last lap': 'standings.content.standings.last-lap.enabled',
      'Pit status': 'standings.content.standings.pit.enabled',
      'Multiclass sections': 'standings.class-separators.enabled',
      'Class separators': 'standings.class-separators.enabled'
    },
    relative: {
      'Relative position': 'relative.content.relative.position.enabled',
      Driver: 'relative.content.relative.driver.enabled',
      'Relative delta': 'relative.content.relative.gap.enabled',
      'Pit status': 'relative.content.relative.pit.enabled'
    },
    'fuel-calculator': {},
    'track-map': {
      'Sector boundaries': 'track-map.sector-boundaries.enabled',
      'Local map building': 'track-map.build-from-telemetry'
    },
    'input-state': {
      'Throttle trace': 'input-state.trace.throttle',
      'Brake trace': 'input-state.trace.brake',
      'Clutch trace': 'input-state.trace.clutch',
      'Throttle %': 'input-state.current.throttle',
      'Brake %': 'input-state.current.brake',
      'Clutch %': 'input-state.current.clutch',
      'Steering wheel': 'input-state.current.steering',
      Gear: 'input-state.current.gear',
      Speed: 'input-state.current.speed'
    },
    'car-radar': {
      'Faster-class warning': 'radar.multiclass-warning'
    },
    flags: {
      Green: 'flags.show-green',
      Blue: 'flags.show-blue',
      Yellow: 'flags.show-yellow',
      'Red / black': 'flags.show-critical',
      'White / checkered': 'flags.show-finish'
    },
    'stream-chat': {
      'Author color': 'stream-chat.twitch.author-color',
      Badges: 'stream-chat.twitch.badges',
      Bits: 'stream-chat.twitch.bits',
      'First message': 'stream-chat.twitch.first-message',
      Replies: 'stream-chat.twitch.replies',
      Timestamps: 'stream-chat.twitch.timestamps',
      Emotes: 'stream-chat.twitch.emotes',
      Alerts: 'stream-chat.twitch.alerts',
      'Message IDs': 'stream-chat.twitch.message-ids'
    },
    'session-weather': {
      'Session type': 'session-weather.session.type.enabled',
      'Session name': 'session-weather.session.name.enabled',
      'Session mode': 'session-weather.session.mode.enabled',
      'Elapsed time': 'session-weather.clock.elapsed.enabled',
      'Remaining time': 'session-weather.clock.remaining.enabled',
      'Total time': 'session-weather.clock.total.enabled',
      'Event type': 'session-weather.event.type.enabled',
      Car: 'session-weather.event.car.enabled',
      'Track name': 'session-weather.track.name.enabled',
      'Track length': 'session-weather.track.length.enabled',
      'Laps remaining': 'session-weather.laps.remaining.enabled',
      'Laps total': 'session-weather.laps.total.enabled',
      Wetness: 'session-weather.surface.wetness.enabled',
      'Declared surface': 'session-weather.surface.declared.enabled',
      Rubber: 'session-weather.surface.rubber.enabled',
      Skies: 'session-weather.sky.skies.enabled',
      Weather: 'session-weather.sky.weather.enabled',
      Rain: 'session-weather.sky.rain.enabled',
      'Wind direction': 'session-weather.wind.direction.enabled',
      'Wind speed': 'session-weather.wind.speed.enabled',
      'Facing wind': 'session-weather.wind.facing.enabled',
      'Air temp': 'session-weather.temps.air.enabled',
      'Track temp': 'session-weather.temps.track.enabled',
      Humidity: 'session-weather.atmosphere.humidity.enabled',
      Fog: 'session-weather.atmosphere.fog.enabled',
      Pressure: 'session-weather.atmosphere.pressure.enabled'
    },
    'pit-service': {
      'Session time': 'pit-service.session.time.enabled',
      'Session laps': 'pit-service.session.laps.enabled',
      Release: 'pit-service.signal.release.enabled',
      'Pit status': 'pit-service.signal.status.enabled',
      'Fuel requested': 'pit-service.service.fuel-requested.enabled',
      'Fuel selected': 'pit-service.service.fuel-selected.enabled',
      'Tearoff requested': 'pit-service.service.tearoff-requested.enabled',
      'Required repair': 'pit-service.service.repair-required.enabled',
      'Optional repair': 'pit-service.service.repair-optional.enabled',
      'Fast repair selected': 'pit-service.service.fast-repair-selected.enabled',
      'Fast repairs available': 'pit-service.service.fast-repair-available.enabled',
      Compound: 'pit-service.tire-analysis.compound',
      'Change request': 'pit-service.tire-analysis.change',
      'Set limit': 'pit-service.tire-analysis.set-limit',
      'Sets available': 'pit-service.tire-analysis.sets-available',
      'Sets used': 'pit-service.tire-analysis.sets-used',
      Pressure: 'pit-service.tire-analysis.pressure',
      Temperature: 'pit-service.tire-analysis.temperature',
      Wear: 'pit-service.tire-analysis.wear',
      Distance: 'pit-service.tire-analysis.distance'
    }
  };
  return maps[id]?.[label] || null;
}

export function freshLiveSnapshot(models) {
  return {
    isConnected: true,
    isCollecting: true,
    lastUpdatedAtUtc: new Date().toISOString(),
    sequence: 100,
    models
  };
}

export function assetText(relativePath) {
  return readFileSync(resolve(browserAssetRoot, relativePath), 'utf8');
}

function pageDefinition(id, title, route, options = {}) {
  return {
    title,
    route,
    aliases: (options.aliases || []).map(normalizeRoute),
    bodyClass: options.bodyClass || '',
    module: id,
    modelRoute: options.modelRoute ?? null,
    settingsRoute: options.settingsRoute ?? null,
    settingsProperty: options.settingsProperty ?? null,
    page: {
      id,
      title,
      requiresTelemetry: options.requiresTelemetry ?? true,
      renderWhenTelemetryUnavailable: options.renderWhenTelemetryUnavailable ?? false,
      fadeWhenTelemetryUnavailable: options.fadeWhenTelemetryUnavailable ?? false,
      refreshIntervalMilliseconds: options.refreshIntervalMilliseconds ?? 250,
      forwardQueryParameters: options.forwardQueryParameters ?? ['preview', 'frame', 'rel', 'spoofFocus', 'focus', 'pitService', 'streamChatFixture', 'sourceStart', 'sourceEnd', 'frameStart', 'frameEnd', 'replaySpeed']
    }
  };
}

function emptyDisplayModel(overlayId, title) {
  return {
    overlayId,
    title,
    status: 'waiting',
    source: 'source: waiting',
    bodyKind: 'table',
    columns: [],
    rows: [],
    metrics: [],
    points: [],
    headerItems: [],
    shouldRender: true
  };
}

function defaultDisplayModel(page, live, settings) {
  let model;
  switch (page.page.id) {
    case 'fuel-calculator':
      model = fuelCalculatorDisplayModel(page, live, settings);
      break;
    case 'car-radar':
      model = carRadarDisplayModel(page, live, settings);
      break;
    case 'track-map':
      model = trackMapDisplayModel(page, live, settings);
      break;
    case 'flags':
      model = flagsDisplayModel(page, live, settings);
      break;
    case 'garage-cover':
      model = garageCoverDisplayModel(page, live, settings);
      break;
    case 'stream-chat':
      model = streamChatDisplayModel(page, live, settings);
      break;
    case 'input-state':
      model = inputStateDisplayModel(page, live, settings);
      break;
    default:
      model = emptyDisplayModel(page.page.id, page.title);
      break;
  }

  return withBrowserRootOpacity(model, page.page.id, settings);
}

function withBrowserRootOpacity(model, overlayId, settings = {}) {
  if (!model || opacityExcludedOverlayIds.has(overlayId)) {
    return model ? { ...model, rootOpacity: 1 } : model;
  }

  const percent = Number(settings?.opacityPercent ?? 100);
  const opacity = Number.isFinite(percent) ? Math.max(0.2, Math.min(1, percent / 100)) : 1;
  return { ...model, rootOpacity: opacity };
}

function flagsDisplayModel(page, live, settings) {
  const flags = Array.isArray(settings?.flags)
    ? settings.flags
    : flagItemsFromSession(live?.models?.session?.sessionFlags, live?.models?.session?.sessionState);
  const enabled = {
    green: settings?.showGreen ?? true,
    blue: settings?.showBlue ?? true,
    yellow: settings?.showYellow ?? true,
    critical: settings?.showCritical ?? true,
    finish: settings?.showFinish ?? true
  };
  const visibleFlags = flags.filter((flag) => enabled[String(flag.category || '').toLowerCase()] !== false);
  return {
    ...emptyDisplayModel(page.page.id, page.title),
    status: visibleFlags.length ? visibleFlags.map((flag) => flag.label).join(' + ').toLowerCase() : 'none',
    source: 'source: session flags telemetry',
    bodyKind: 'flags',
    headerItems: [{ key: 'status', value: visibleFlags.length ? visibleFlags[0].label : 'none' }],
    flags: {
      flags: visibleFlags,
      isWaiting: false
    },
    shouldRender: visibleFlags.length > 0
  };
}

function flagItemsFromSession(sessionFlags, sessionState) {
  const value = Number.isInteger(sessionFlags) ? sessionFlags : 0;
  const items = [];
  if ((value & 0x00000010) !== 0) items.push(flagItem('red', 'critical', 'Red', null, 'error'));
  if ((value & 0x00100000) !== 0) items.push(flagItem('meatball', 'critical', 'Repair', null, 'error'));
  if ((value & 0x00010000) !== 0) items.push(flagItem('black', 'critical', 'Black', null, 'error'));
  if ((value & 0x00008000) !== 0 || (value & 0x00004000) !== 0) items.push(flagItem('caution', 'yellow', 'Caution', (value & 0x00008000) !== 0 ? 'waving' : null, 'warning'));
  else if ((value & 0x00000008) !== 0 || (value & 0x00000100) !== 0 || (value & 0x00000200) !== 0 || (value & 0x00000040) !== 0 || (value & 0x00002000) !== 0) items.push(flagItem('yellow', 'yellow', (value & 0x00000200) !== 0 ? 'One to green' : (value & 0x00000040) !== 0 ? 'Debris' : 'Yellow', (value & 0x00000100) !== 0 || (value & 0x00002000) !== 0 ? 'waving' : null, 'warning'));
  if ((value & 0x00000020) !== 0) items.push(flagItem('blue', 'blue', 'Blue', null, 'info'));
  if ((value & 0x00000001) !== 0 || sessionState === 5) items.push(flagItem('checkered', 'finish', 'Checkered', sessionState === 5 && (value & 0x00000001) === 0 ? 'session complete' : null, 'info'));
  if ((value & 0x00000002) !== 0 || (value & 0x00001000) !== 0 || (value & 0x00000800) !== 0 || (value & 0x00000080) !== 0) items.push(flagItem('white', 'finish', (value & 0x00000002) !== 0 ? 'White' : (value & 0x00001000) !== 0 ? 'Five to go' : (value & 0x00000800) !== 0 ? 'Ten to go' : 'Crossed', null, 'info'));
  if ((value & 0x00000400) !== 0 || (value & 0x20000000) !== 0 || (value & 0x40000000) !== 0 || (value & 0x80000000) !== 0) items.push(flagItem('green', 'green', (value & 0x80000000) !== 0 ? 'Start' : (value & 0x40000000) !== 0 ? 'Set' : (value & 0x20000000) !== 0 ? 'Ready' : 'Green', (value & 0x00000400) !== 0 ? 'held' : null, 'success'));
  return items;
}

function flagItem(kind, category, label, detail, tone) {
  return { kind, category, label, detail, tone };
}

function fuelCalculatorDisplayModel(page, live, settings) {
  const unitSystem = normalizeUnitSystem(settings?.unitSystem ?? settings?.general?.unitSystem);
  const context = fuelLocalContext(live);
  if (!context.isAvailable) {
    return {
      ...emptyDisplayModel(page.page.id, page.title),
      status: context.statusText,
      source: fuelSourceFromSettings(settings, 'source: waiting'),
      bodyKind: 'metrics',
      headerItems: fuelHeaderItems(context.statusText, live, settings),
      metricSections: [],
      shouldRender: false
    };
  }

  const strategy = fuelStrategy(live, unitSystem);
  return {
    ...emptyDisplayModel(page.page.id, page.title),
    status: strategy.status,
    source: fuelSourceFromSettings(settings, strategy.source),
    bodyKind: 'metrics',
    metrics: strategy.metricSections.flatMap((section) => section.rows),
    headerItems: fuelHeaderItems(strategy.status, live, settings),
    metricSections: strategy.metricSections
  };
}

function fuelLocalContext(live) {
  const statusText = 'waiting for local fuel context';
  if (live?.isConnected === false) {
    return { isAvailable: false, reason: 'disconnected', statusText: 'iRacing disconnected' };
  }

  if (live?.isCollecting === false) {
    return { isAvailable: false, reason: 'waiting_for_telemetry', statusText: 'waiting for telemetry' };
  }

  const reference = live?.models?.reference || {};
  const directory = live?.models?.driverDirectory || {};
  const race = live?.models?.raceEvents || {};
  const fuelPit = live?.models?.fuelPit || {};
  const playerCarIdx = validCarIdx(reference.playerCarIdx ?? directory.playerCarIdx);
  const focusCarIdx = validCarIdx(reference.focusCarIdx ?? directory.focusCarIdx);
  if (playerCarIdx == null) {
    return { isAvailable: false, reason: 'player_car_unavailable', statusText };
  }

  if (focusCarIdx == null) {
    return { isAvailable: false, reason: 'focus_unavailable', statusText };
  }

  if (focusCarIdx !== playerCarIdx || reference.focusIsPlayer === false) {
    return { isAvailable: false, reason: 'focus_on_another_car', statusText };
  }

  if (race.isInGarage === true || race.isGarageVisible === true || reference.isInGarage === true) {
    return { isAvailable: false, reason: 'garage', statusText };
  }

  if (race.isOnTrack === true || reference.isOnTrack === true) {
    return { isAvailable: true, reason: 'available', statusText: 'live' };
  }

  if (race.onPitRoad === true
    || reference.onPitRoad === true
    || reference.playerOnPitRoad === true
    || reference.playerCarInPitStall === true
    || isPitRoadTrackSurface(reference.trackSurface)
    || isPitRoadTrackSurface(reference.playerTrackSurface)
    || fuelPit.onPitRoad === true
    || fuelPit.pitstopActive === true
    || fuelPit.playerCarInPitStall === true
    || fuelPit.teamOnPitRoad === true) {
    return { isAvailable: true, reason: 'available', statusText: 'live' };
  }

  return { isAvailable: false, reason: 'not_in_car', statusText };
}

function validCarIdx(value) {
  return Number.isInteger(value) && value >= 0 && value < 64 ? value : null;
}

function isPitRoadTrackSurface(value) {
  return value === 1 || value === 2;
}

function fuelStrategy(live, unitSystem) {
  const models = live?.models || {};
  const fuelPit = models.fuelPit || {};
  const fuel = fuelPit.fuel || live?.fuel || {};
  const progress = models.raceProgress || {};
  const projection = models.raceProjection || {};
  const session = models.session || {};
  const currentFuel = positiveNumber(fuel.fuelLevelLiters ?? live?.fuel?.fuelLevelLiters);
  const fuelConfidence = fuel.confidence ?? live?.fuel?.confidence;
  const fuelPerLap = fuelConfidence === 'measured-green-lap'
    ? positiveNumber(fuel.fuelPerLapLiters ?? live?.fuel?.fuelPerLapLiters)
    : null;
  const fuelPercent = positiveNumber(fuel.fuelLevelPercent ?? live?.fuel?.fuelLevelPercent);
  const maxFuel = fuelTankLiters(live, fuelPit, currentFuel, fuelPercent);
  const usageLabel = fuelUsageLabel(session);
  const lapTime = validLapTime(fuel.lapTimeSeconds)
    ?? validLapTime(progress.strategyLapTimeSeconds)
    ?? validLapTime(projection.overallLeaderPaceSeconds)
    ?? validLapTime(progress.racePaceSeconds);
  const raceLapsRemaining = fuelRaceLapsRemaining(session, progress, projection, lapTime);
  const fuelToFinish = fuelPerLap != null && raceLapsRemaining != null ? fuelPerLap * raceLapsRemaining : null;
  const additionalFuel = fuelToFinish != null && currentFuel != null ? Math.max(0, fuelToFinish - currentFuel) : null;
  const fullTankLaps = maxFuel != null && fuelPerLap != null ? maxFuel / fuelPerLap : null;
  const stintPlan = fuelStintPlan(currentFuel, fuelPerLap, maxFuel, raceLapsRemaining);
  const status = fuelStatus(currentFuel, fuelPerLap, raceLapsRemaining, additionalFuel, stintPlan);
  const planRow = fuelMetricRow('Plan', fuelPlanText(stintPlan.plannedRaceLaps, stintPlan.plannedStintCount, stintPlan.plannedStopCount), fuelStrategyTone(stintPlan), [
    fuelMetricSegment('Race', formatLapCount(stintPlan.plannedRaceLaps), stintPlan.plannedRaceLaps == null ? 'waiting' : 'info'),
    fuelMetricSegment('Remain', formatFuelLaps(raceLapsRemaining), raceLapsRemaining == null ? 'waiting' : 'info'),
    fuelMetricSegment('Stints', formatCount(stintPlan.plannedStintCount), stintPlan.plannedStintCount == null ? 'waiting' : stintPlan.plannedStintCount <= 1 ? 'success' : 'info'),
    fuelMetricSegment('Stops', formatCount(stintPlan.plannedStopCount), stintPlan.plannedStopCount == null ? 'waiting' : 'info'),
    fuelMetricSegment('Save', formatFuelSaving(stintPlan.requiredFuelSavingLitersPerLap, unitSystem), fuelSavingTone(stintPlan.requiredFuelSavingLitersPerLap))
  ]);
  const fuelRow = fuelMetricRow('Fuel', fuelFuelText(currentFuel, fuelPerLap, additionalFuel, unitSystem), fuelNeedTone(additionalFuel), [
    fuelMetricSegment('Current', formatFuelVolume(currentFuel, unitSystem), currentFuel == null ? 'waiting' : 'info'),
    fuelMetricSegment('Burn', formatFuelPerLap(fuelPerLap, unitSystem), fuelPerLap == null ? 'waiting' : 'info'),
    fuelMetricSegment('Tank', formatFuelLaps(fullTankLaps), fullTankLaps == null ? 'waiting' : 'info'),
    fuelMetricSegment('Need', formatFuelNeed(additionalFuel, unitSystem), fuelNeedTone(additionalFuel))
  ]);
  const stintRows = stintPlan.stints
    .filter((stint) => stint.lengthLaps > 0.05 || stint.source === 'finish')
    .slice(0, 4)
    .map((stint) => fuelMetricRow(
      `Stint ${stint.number}`,
      fuelStintText(stint, unitSystem),
      fuelStintTone(stint),
      [
        fuelMetricSegment('Laps', formatStintLaps(stint), 'info'),
        fuelMetricSegment('Target', formatStintTarget(stint, unitSystem), fuelStintTargetTone(stint)),
        fuelMetricSegment('Save', formatFuelSaving(stint.requiredFuelSavingLitersPerLap, unitSystem), fuelSavingTone(stint.requiredFuelSavingLitersPerLap))
      ]));
  const metricSections = [
    { title: 'Race Information', rows: [planRow, fuelRow] }
  ];
  if (usageLabel != null) {
    metricSections.push({ title: 'Fuel Usage', rows: [fuelUsageRow(usageLabel, fuel, unitSystem)] });
  }
  const visibleStintRows = usageLabel != null ? stintRows.slice(0, 1) : stintRows;
  if (visibleStintRows.length > 0) {
    metricSections.push({ title: 'Stint Targets', rows: visibleStintRows });
  }

  const source = fuelPerLap != null
    ? `burn ${formatFuelPerLap(fuelPerLap, unitSystem)} (measured green lap) | ${formatFuelLaps(fullTankLaps, ' laps/tank')} | history none`
    : 'source: waiting';
  return { status, metricSections, source };
}

function fuelUsageLabel(session) {
  const text = `${session?.sessionType ?? ''} ${session?.sessionName ?? ''}`.toLowerCase();
  if (text.includes('qual')) return 'Quali Usage';
  if (text.includes('practice') || text.includes('test')) return 'Practice Usage';
  return null;
}

function fuelUsageRow(label, fuel, unitSystem) {
  const samples = Number.isFinite(fuel?.measuredFuelPerLapSampleCount) ? Math.max(0, Math.trunc(fuel.measuredFuelPerLapSampleCount)) : 0;
  const average = positiveNumber(fuel?.measuredFuelPerLapAverageLiters);
  const hasMeasuredUsage = samples > 0 && average != null;
  const min = hasMeasuredUsage ? positiveNumber(fuel?.measuredFuelPerLapMinimumLiters ?? average) : null;
  const max = hasMeasuredUsage ? positiveNumber(fuel?.measuredFuelPerLapMaximumLiters ?? average) : null;
  const tone = hasMeasuredUsage ? 'info' : 'waiting';
  return fuelMetricRow(
    label,
    hasMeasuredUsage
      ? `min ${formatFuelPerLap(min, unitSystem)} | avg ${formatFuelPerLap(average, unitSystem)} | max ${formatFuelPerLap(max, unitSystem)}`
      : 'waiting for completed lap',
    tone,
    [
      fuelMetricSegment('Min', formatFuelPerLap(min, unitSystem), tone),
      fuelMetricSegment('Avg', formatFuelPerLap(average, unitSystem), tone),
      fuelMetricSegment('Max', formatFuelPerLap(max, unitSystem), tone),
      fuelMetricSegment('Laps', samples > 0 ? `${samples} ${samples === 1 ? 'lap' : 'laps'}` : '--', tone)
    ]);
}

function fuelTankLiters(live, fuelPit, currentFuel, fuelPercent) {
  const percentDerived = currentFuel != null && fuelPercent != null && fuelPercent > 0 && fuelPercent <= 1.5
    ? currentFuel / fuelPercent
    : null;
  return positiveNumber(live?.context?.car?.driverCarFuelMaxLiters)
    ?? positiveNumber(live?.context?.car?.driverCarFuelMaxLtr)
    ?? positiveNumber(percentDerived)
    ?? positiveNumber(fuelPit?.pitServiceFuelLiters)
    ?? positiveNumber(fuelPit?.pitServiceFuel);
}

function fuelRaceLapsRemaining(session, progress, projection, lapTime) {
  const direct = positiveNumber(projection.estimatedTeamLapsRemaining)
    ?? positiveNumber(projection.estimatedLapsRemaining)
    ?? positiveNumber(progress.raceLapsRemaining);
  if (direct != null) return direct;

  const lapBased = positiveNumber(session.sessionLapsRemain)
    ?? positiveNumber(session.sessionLapsRemainEx);
  if (lapBased != null && lapBased < 10000) return lapBased;

  const timeRemaining = positiveNumber(session.sessionTimeRemainSeconds);
  if (!isRacePreGreen(session) && timeRemaining != null && lapTime != null) {
    const leaderProgress = positiveNumber(progress.overallLeaderProgressLaps)
      ?? positiveNumber(progress.classLeaderProgressLaps);
    const carProgress = positiveNumber(progress.strategyCarProgressLaps)
      ?? positiveNumber(progress.referenceCarProgressLaps)
      ?? leaderProgress;
    if (leaderProgress != null && carProgress != null) {
      return Math.max(0, Math.ceil(leaderProgress + timeRemaining / lapTime) - carProgress);
    }

    return Math.ceil(timeRemaining / lapTime + 1);
  }

  return null;
}

function isRacePreGreen(session) {
  const state = session?.sessionState;
  if (!Number.isInteger(state) || state < 1 || state > 3) return false;
  const text = `${session?.sessionType || ''} ${session?.sessionName || ''} ${session?.eventType || ''}`.toLowerCase();
  return text.includes('race');
}

function fuelStintPlan(currentFuel, fuelPerLap, maxFuel, raceLapsRemaining) {
  const empty = {
    stints: [],
    plannedRaceLaps: null,
    plannedStintCount: null,
    plannedStopCount: null,
    requiredFuelSavingLitersPerLap: null
  };
  if (fuelPerLap == null || fuelPerLap <= 0) return empty;

  const currentStintLaps = currentFuel != null ? currentFuel / fuelPerLap : null;
  const fullTankStintLaps = maxFuel != null ? maxFuel / fuelPerLap : null;
  if (raceLapsRemaining != null && raceLapsRemaining > 0) {
    const plannedRaceLaps = Math.ceil(raceLapsRemaining);
    const plannedStintCount = plannedFuelStintCount(plannedRaceLaps, currentStintLaps, fullTankStintLaps);
    if (plannedStintCount != null && plannedStintCount > 0) {
      const targets = distributeWholeLapTargets(plannedRaceLaps, plannedStintCount);
      const stints = [];
      let displayedLapsConsumed = 0;
      const savings = [];
      for (let index = 0; index < targets.length; index++) {
        const targetLaps = targets[index];
        const availableFuel = index === 0 && currentFuel != null ? currentFuel : maxFuel;
        const requiredSaving = requiredSavingPerLap(targetLaps, fuelPerLap, availableFuel);
        if (requiredSaving != null && requiredSaving > 0) savings.push(requiredSaving);
        const remainingForDisplay = Math.max(0, raceLapsRemaining - displayedLapsConsumed);
        const lengthLaps = Math.min(targetLaps, remainingForDisplay);
        displayedLapsConsumed += targetLaps;
        stints.push({
          number: index + 1,
          lengthLaps,
          source: targets.length === 1 ? 'finish' : index === targets.length - 1 ? 'final' : 'target',
          targetLaps,
          targetFuelPerLapLiters: availableFuel != null && targetLaps > 0 ? availableFuel / targetLaps : null,
          requiredFuelSavingLitersPerLap: requiredSaving
        });
      }

      const requiredFuelSavingLitersPerLap = savings.length > 0 ? Math.max(...savings) : null;
      return {
        stints,
        plannedRaceLaps,
        plannedStintCount,
        plannedStopCount: Math.max(0, plannedStintCount - 1),
        requiredFuelSavingLitersPerLap
      };
    }
  }

  if (currentStintLaps != null) {
    return {
      ...empty,
      stints: [{
        number: 1,
        lengthLaps: raceLapsRemaining != null ? Math.min(currentStintLaps, raceLapsRemaining) : currentStintLaps,
        source: 'current fuel',
        targetLaps: null,
        targetFuelPerLapLiters: fuelPerLap,
        requiredFuelSavingLitersPerLap: null
      }]
    };
  }

  if (fullTankStintLaps != null) {
    return {
      ...empty,
      stints: [{
        number: 1,
        lengthLaps: fullTankStintLaps,
        source: 'full tank',
        targetLaps: null,
        targetFuelPerLapLiters: fuelPerLap,
        requiredFuelSavingLitersPerLap: null
      }]
    };
  }

  return empty;
}

function plannedFuelStintCount(plannedRaceLaps, currentStintLaps, fullTankStintLaps) {
  if (plannedRaceLaps <= 0) return 0;
  if (currentStintLaps != null && currentStintLaps > 0) {
    const remainingAfterCurrent = plannedRaceLaps - currentStintLaps;
    if (remainingAfterCurrent <= 0) return 1;
    return fullTankStintLaps != null && fullTankStintLaps > 0
      ? 1 + Math.ceil(remainingAfterCurrent / fullTankStintLaps)
      : 1;
  }

  return fullTankStintLaps != null && fullTankStintLaps > 0
    ? Math.ceil(plannedRaceLaps / fullTankStintLaps)
    : null;
}

function distributeWholeLapTargets(plannedRaceLaps, plannedStintCount) {
  if (plannedRaceLaps <= 0 || plannedStintCount <= 0) return [];
  const baseLaps = Math.floor(plannedRaceLaps / plannedStintCount);
  const extraLaps = plannedRaceLaps % plannedStintCount;
  return Array.from({ length: plannedStintCount }, (_, index) => baseLaps + (index < extraLaps ? 1 : 0));
}

function requiredSavingPerLap(targetLaps, fuelPerLap, availableFuel) {
  if (targetLaps <= 0 || availableFuel == null || availableFuel <= 0) return null;
  const extraFuelRequired = targetLaps * fuelPerLap - availableFuel;
  return extraFuelRequired > 0 ? extraFuelRequired / targetLaps : null;
}

function fuelStatus(currentFuel, fuelPerLap, raceLapsRemaining, additionalFuel, stintPlan) {
  if (currentFuel == null && (fuelPerLap == null || raceLapsRemaining == null || stintPlan.plannedStintCount == null)) {
    return 'waiting for fuel';
  }

  if (fuelPerLap == null) return 'waiting for burn';
  if (raceLapsRemaining == null) return 'stint estimate';
  if (stintPlan.requiredFuelSavingLitersPerLap != null && stintPlan.requiredFuelSavingLitersPerLap > 0.01) {
    const targetLaps = Math.max(...stintPlan.stints.map((stint) => stint.targetLaps || 0));
    return `${targetLaps}-lap target: save ${stintPlan.requiredFuelSavingLitersPerLap.toFixed(1)} L/lap`;
  }

  if (stintPlan.plannedStintCount != null && stintPlan.plannedStopCount != null) {
    return stintPlan.plannedStintCount <= 1
      ? 'fuel covers finish'
      : `${stintPlan.plannedStintCount} stints / ${stintPlan.plannedStopCount} ${stintPlan.plannedStopCount === 1 ? 'stop' : 'stops'}`;
  }

  if (additionalFuel != null && additionalFuel > 0.1) {
    return `+${additionalFuel.toFixed(1)} L needed`;
  }

  return 'fuel covers finish';
}

function fuelMetricRow(label, value, tone, segments = []) {
  return { label, value, tone, segments };
}

function fuelMetricSegment(label, value, tone = 'normal') {
  return { label, value, tone };
}

function fuelPlanText(plannedRaceLaps, plannedStintCount, plannedStopCount) {
  const laps = plannedRaceLaps != null ? `${plannedRaceLaps} laps` : '--';
  const stints = plannedStintCount != null ? plannedStintCount <= 1 ? 'no stop' : `${plannedStintCount} stints` : '--';
  const stops = plannedStopCount != null ? `${plannedStopCount} ${plannedStopCount === 1 ? 'stop' : 'stops'}` : '--';
  return `${laps} | ${stints} | ${stops}`;
}

function fuelFuelText(currentFuel, fuelPerLap, additionalFuel, unitSystem) {
  return `${formatFuelVolume(currentFuel, unitSystem)} | ${formatFuelPerLap(fuelPerLap, unitSystem)} | ${formatFuelNeed(additionalFuel, unitSystem)}`;
}

function fuelStintText(stint, unitSystem) {
  if (stint.source === 'finish') return 'no fuel stop needed';
  if (stint.targetLaps != null) {
    const suffix = stint.source === 'final' ? ' final' : '';
    return `${stint.targetLaps} laps${suffix} | target ${formatFuelPerLap(stint.targetFuelPerLapLiters, unitSystem)}`;
  }

  return formatFuelLaps(stint.lengthLaps);
}

function formatStintLaps(stint) {
  return stint.targetLaps != null ? `${stint.targetLaps} laps` : formatFuelLaps(stint.lengthLaps);
}

function formatStintTarget(stint, unitSystem) {
  return stint.source === 'finish'
    ? 'Finish'
    : formatFuelPerLap(stint.targetFuelPerLapLiters, unitSystem);
}

function fuelStrategyTone(stintPlan) {
  return stintPlan.requiredFuelSavingLitersPerLap != null && stintPlan.requiredFuelSavingLitersPerLap > 0.01
    ? 'warning'
    : 'info';
}

function fuelStintTone(stint) {
  return stint.requiredFuelSavingLitersPerLap != null && stint.requiredFuelSavingLitersPerLap > 0.01
    ? 'warning'
    : 'info';
}

function fuelStintTargetTone(stint) {
  if (stint.source === 'finish') return 'success';
  return stint.targetFuelPerLapLiters == null ? 'waiting' : fuelStintTone(stint);
}

function fuelSavingTone(value) {
  return value != null && value > 0.01 ? 'warning' : 'success';
}

function fuelNeedTone(value) {
  if (value == null) return 'waiting';
  return value > 0.1 ? 'warning' : 'success';
}

function formatFuelNeed(value, unitSystem) {
  return value != null && value > 0.1 ? `+${formatFuelVolume(value, unitSystem)}` : 'Covered';
}

function formatFuelSaving(value, unitSystem) {
  return value != null && value > 0.01 ? formatFuelPerLap(value, unitSystem) : 'None';
}

function formatLapCount(value) {
  return Number.isFinite(value) ? `${value} laps` : '--';
}

function formatFuelLaps(value, suffix = ' laps') {
  return Number.isFinite(value) ? `${value.toFixed(1)}${suffix}` : '--';
}

function formatCount(value) {
  return Number.isFinite(value) ? String(value) : '--';
}

function formatFuelVolume(liters, unitSystem) {
  const value = fuelUnitValue(liters, unitSystem);
  return value == null ? '--' : `${value.toFixed(1)} ${fuelVolumeSuffix(unitSystem)}`;
}

function formatFuelPerLap(liters, unitSystem) {
  const value = fuelUnitValue(liters, unitSystem);
  return value == null ? '--' : `${value.toFixed(1)} ${fuelPerLapSuffix(unitSystem)}`;
}

function fuelUnitValue(liters, unitSystem) {
  if (!Number.isFinite(liters)) return null;
  return unitSystem === 'Imperial' ? liters * 0.264172052 : liters;
}

function fuelVolumeSuffix(unitSystem) {
  return unitSystem === 'Imperial' ? 'gal' : 'L';
}

function fuelPerLapSuffix(unitSystem) {
  return unitSystem === 'Imperial' ? 'gal/lap' : 'L/lap';
}

function positiveNumber(value) {
  return Number.isFinite(value) && value > 0 ? value : null;
}

function validLapTime(value) {
  return Number.isFinite(value) && value > 20 && value < 1800 ? value : null;
}

function fuelHeaderItems(status, live, settings) {
  const items = [];
  if (settings?.showHeaderStatus !== false) {
    items.push({ key: 'status', value: status });
  }

  if (settings?.showHeaderTimeRemaining !== false) {
    const timeRemaining = formatFuelHeaderTimeRemaining(live?.models?.session);
    if (timeRemaining) {
      items.push({ key: 'timeRemaining', value: timeRemaining });
    }
  }

  return items;
}

function formatFuelHeaderTimeRemaining(session) {
  const seconds = session?.sessionTimeRemainSeconds;
  if (!Number.isFinite(seconds) || seconds < 0) return null;
  const totalSeconds = Math.ceil(seconds);
  const hours = Math.floor(totalSeconds / 3600);
  const minutes = Math.floor((totalSeconds % 3600) / 60);
  const remainingSeconds = totalSeconds % 60;
  return `${String(hours).padStart(2, '0')}:${String(minutes).padStart(2, '0')}:${String(remainingSeconds).padStart(2, '0')}`;
}

function fuelSourceFromSettings(settings, source) {
  return settings?.showFooterSource === false ? '' : source;
}

function inputStateDisplayModel(page, live, settings) {
  const inputs = live?.models?.inputs || {};
  const unitSystem = normalizeUnitSystem(settings?.unitSystem ?? settings?.general?.unitSystem);
  const inCar = isPlayerInCar(live);
  const isAvailable = inCar && inputs.hasData === true;
  const brakeAbsActive = inputs.brakeAbsActive === true;
  const gearText = formatInputGear(inputs.gear);
  const rpmText = formatInputRpm(inputs.rpm);
  const showThrottleTrace = settings.showThrottleTrace ?? true;
  const showBrakeTrace = settings.showBrakeTrace ?? true;
  const showClutchTrace = settings.showClutchTrace ?? true;
  const showThrottle = settings.showThrottle ?? true;
  const showBrake = settings.showBrake ?? true;
  const showClutch = settings.showClutch ?? true;
  const showSteering = settings.showSteering ?? true;
  const showGear = settings.showGear ?? true;
  const showSpeed = settings.showSpeed ?? true;
  const hasGraph = showThrottleTrace || showBrakeTrace || showClutchTrace;
  const hasRail = showThrottle || showBrake || showClutch || showSteering || showGear || showSpeed;
  const hasContent = hasGraph || hasRail;
  const status = !inCar
    ? 'waiting for player in car'
    : !inputs.hasData
      ? 'waiting for car telemetry'
      : hasContent
        ? [gearText, rpmText, brakeAbsActive ? 'ABS' : null].filter(Boolean).join(' | ')
        : 'no input content enabled';
  return {
    ...emptyDisplayModel(page.page.id, page.title),
    status,
    source: '',
    headerItems: [],
    bodyKind: 'inputs',
    inputs: {
      isAvailable,
      throttle: inputs.throttle,
      brake: inputs.brake,
      clutch: inputs.clutch,
      steeringWheelAngle: inputs.steeringWheelAngle,
      speedMetersPerSecond: inputs.speedMetersPerSecond,
      gear: inputs.gear,
      speedText: formatInputSpeed(inputs.speedMetersPerSecond, unitSystem),
      gearText,
      rpmText,
      steeringText: Number.isFinite(inputs.steeringWheelAngle)
        ? `${Math.round(inputs.steeringWheelAngle * 180 / Math.PI)} deg`
        : '--',
      brakeAbsActive,
      showThrottleTrace,
      showBrakeTrace,
      showClutchTrace,
      showThrottle,
      showBrake,
      showClutch,
      showSteering,
      showGear,
      showSpeed,
      hasGraph,
      hasRail,
      hasContent,
      sampleIntervalMilliseconds: 50,
      maximumTracePoints: 180,
      trace: isAvailable
        ? Array.isArray(inputs.trace)
          ? inputs.trace
          : [{
            throttle: clamp01(inputs.throttle),
            brake: clamp01(inputs.brake),
            clutch: clamp01(inputs.clutch),
            brakeAbsActive
          }]
        : []
    }
  };
}

function normalizeUnitSystem(value) {
  return String(value || '').trim().toLowerCase() === 'imperial'
    ? 'Imperial'
    : 'Metric';
}

function formatInputSpeed(metersPerSecond, unitSystem) {
  if (!Number.isFinite(metersPerSecond)) return '--';
  return unitSystem === 'Imperial'
    ? `${Math.round(metersPerSecond * 2.2369362921)} mph`
    : `${Math.round(metersPerSecond * 3.6)} km/h`;
}

function formatInputGear(value) {
  if (value === -1) return 'R';
  if (value === 0) return 'N';
  return Number.isFinite(value) ? String(value) : '--';
}

function formatInputRpm(value) {
  return Number.isFinite(value) ? `${Math.round(value)} rpm` : null;
}

function clamp01(value) {
  return Number.isFinite(value) ? Math.max(0, Math.min(1, value)) : 0;
}

function carRadarDisplayModel(page, live, settings = {}) {
  const spatial = live?.models?.spatial || {};
  const inCar = isPlayerInCar(live);
  const strongestMulticlassApproach = carRadarMulticlassApproach(spatial);
  const showMulticlassWarning = settings?.showMulticlassWarning ?? true;
  const hasCurrentSignal = Boolean(
    spatial.hasCarLeft
    || spatial.hasCarRight
    || (showMulticlassWarning && strongestMulticlassApproach)
    || spatial.cars?.length);
  const status = !inCar
    ? 'waiting for player in car'
    : spatial.hasData === false
      ? 'waiting for radar'
      : spatial.hasCarLeft && spatial.hasCarRight
        ? 'cars both sides'
        : spatial.hasCarLeft
        ? 'car left'
        : spatial.hasCarRight
          ? 'car right'
            : strongestMulticlassApproach
              ? 'faster class'
              : 'clear';
  return {
    ...emptyDisplayModel(page.page.id, page.title),
    status,
    headerItems: [{ key: 'status', value: status }],
    source: inCar && spatial.hasData !== false ? 'source: spatial telemetry' : 'source: waiting',
    bodyKind: 'car-radar',
    carRadar: {
      isAvailable: inCar,
      hasCarLeft: spatial.hasCarLeft === true,
      hasCarRight: spatial.hasCarRight === true,
      cars: spatial.cars || [],
      strongestMulticlassApproach: showMulticlassWarning ? strongestMulticlassApproach : null,
      showMulticlassWarning,
      previewVisible: false,
      hasCurrentSignal,
      renderModel: carRadarRenderModelFromState({
        isAvailable: inCar,
        hasCarLeft: spatial.hasCarLeft === true,
        hasCarRight: spatial.hasCarRight === true,
        cars: spatial.cars || [],
        strongestMulticlassApproach: showMulticlassWarning ? strongestMulticlassApproach : null,
        showMulticlassWarning,
        previewVisible: false,
        hasCurrentSignal,
        referenceCarClassColorHex: spatial.referenceCarClassColorHex
      })
    }
  };
}

function carRadarMulticlassApproach(spatial) {
  const approaches = Array.isArray(spatial?.multiclassApproaches)
    ? spatial.multiclassApproaches
    : spatial?.strongestMulticlassApproach
      ? [spatial.strongestMulticlassApproach]
      : [];
  return approaches
    .filter(isInCarRadarMulticlassWarningRange)
    .sort((left, right) =>
      Math.abs(left?.relativeSeconds ?? Number.POSITIVE_INFINITY)
        - Math.abs(right?.relativeSeconds ?? Number.POSITIVE_INFINITY)
      || Number(right?.urgency || 0) - Number(left?.urgency || 0))[0] || null;
}

function isInCarRadarMulticlassWarningRange(approach) {
  const seconds = approach?.relativeSeconds;
  return Number.isFinite(seconds) && seconds < -2 && seconds >= -5;
}

export function carRadarRenderModelFromState({
  isAvailable = false,
  hasCarLeft = false,
  hasCarRight = false,
  cars = [],
  strongestMulticlassApproach = null,
  showMulticlassWarning = true,
  previewVisible = false,
  hasCurrentSignal = false,
  referenceCarClassColorHex = null
} = {}) {
  const shouldRender = (isAvailable && hasCurrentSignal) || previewVisible;
  const empty = () => ({
    shouldRender: false,
    width: 300,
    height: 300,
    fadeInMilliseconds: 250,
    fadeOutMilliseconds: 850,
    minimumVisibleAlpha: 0.02,
    background: radarBackground(),
    rings: [],
    cars: [],
    labels: [],
    multiclassArc: null
  });
  if (!shouldRender) return empty();

  const currentCars = uniqueRadarCars((Array.isArray(cars) ? cars : []).filter(isInRadarRange));
  const sideAttachments = sideWarningAttachments(hasCarLeft, hasCarRight, currentCars);
  const renderCars = [
    ...radarCarPlacements(currentCars, sideAttachments).map(nearbyCarRectangle),
    ...sideWarningRectangles(hasCarLeft, hasCarRight, sideAttachments),
    playerCarRectangle(referenceCarClassColorHex)
  ];
  const rings = [distanceRing(1), distanceRing(2)];
  const labels = rings.map((ring) => ring.label).filter(Boolean);
  const multiclassArc = showMulticlassWarning && strongestMulticlassApproach
    ? multiclassApproachArc(strongestMulticlassApproach)
    : null;
  if (multiclassArc?.label) labels.push(multiclassArc.label);

  return {
    shouldRender: true,
    width: 300,
    height: 300,
    fadeInMilliseconds: 250,
    fadeOutMilliseconds: 850,
    minimumVisibleAlpha: 0.02,
    background: radarBackground(),
    rings,
    cars: renderCars,
    labels,
    multiclassArc
  };
}

const radarConstants = {
  radarInset: 4,
  radarDiameter: 292,
  radarCenter: 150,
  focusedCarLengthMeters: 4.746,
  contactWindowMeters: 4.746,
  proximityWarningGapMeters: 2,
  proximityRedStart: 0.74,
  timingAwareVisibilitySeconds: 2,
  maximumTimingAwareRangeMultiplier: 15,
  timingAwareEdgeOpacity: 0.2,
  maxWideRowRadarCars: 18,
  focusedCarWidth: 20,
  focusedCarHeight: 36,
  radarCarWidth: 20,
  radarCarHeight: 36,
  carCornerRadius: 4,
  separatedCarPaddingPixels: 2,
  radarEdgeCenterPaddingPixels: 2,
  gridRowReferenceMeters: 8,
  minimumDistinctRowGapPixels: 48,
  wideRowBucketPixels: 30,
  wideRowLongitudinalBucketMeters: 2,
  wideRowSlotPitchPixels: 36,
  multiclassWarningArcStartDegrees: 62.5,
  multiclassWarningArcSweepDegrees: 55
};
radarConstants.radarRadius = radarConstants.radarDiameter / 2;
radarConstants.radarRangeMeters = radarConstants.focusedCarLengthMeters * 6;
radarConstants.maximumTimingAwareRangeMeters = radarConstants.focusedCarLengthMeters * radarConstants.maximumTimingAwareRangeMultiplier;
radarConstants.sideAttachmentWindowMeters = radarConstants.focusedCarLengthMeters * 2;
radarConstants.usableRadarRadius = radarConstants.radarRadius - radarConstants.radarEdgeCenterPaddingPixels;
radarConstants.distinctRowPixelsPerMeter = radarConstants.minimumDistinctRowGapPixels / radarConstants.gridRowReferenceMeters;

function radarBackground() {
  return {
    x: radarConstants.radarInset,
    y: radarConstants.radarInset,
    width: radarConstants.radarDiameter,
    height: radarConstants.radarDiameter,
    fill: rgba(12, 18, 22, 82),
    stroke: rgba(0, 232, 255, 88),
    strokeWidth: 1.2,
    label: null
  };
}

function distanceRing(index) {
  const inset = radarConstants.radarDiameter * index / 6;
  const radius = radarConstants.radarDiameter / 2 - inset;
  return {
    x: radarConstants.radarInset + inset,
    y: radarConstants.radarInset + inset,
    width: radarConstants.radarDiameter - inset * 2,
    height: radarConstants.radarDiameter - inset * 2,
    fill: null,
    stroke: rgba(255, 255, 255, 40),
    strokeWidth: 1,
    label: {
      text: formatRingDistance(radius),
      x: radarConstants.radarCenter + radius * 0.35,
      y: radarConstants.radarCenter - radius - 8,
      width: 58,
      height: 16,
      fontSize: 7.5,
      bold: false,
      alignment: 'near',
      color: rgba(220, 230, 236, 118)
    }
  };
}

function formatRingDistance(offsetPixels) {
  return `${Math.round(distanceForLongitudinalOffset(offsetPixels))}m`;
}

function distanceForLongitudinalOffset(offsetPixels) {
  const separatedCenterOffset = Math.min(
    radarConstants.usableRadarRadius,
    radarConstants.focusedCarHeight / 2 + radarConstants.radarCarHeight / 2 + radarConstants.separatedCarPaddingPixels);
  const absOffset = Math.max(0, Math.min(radarConstants.usableRadarRadius, Math.abs(offsetPixels)));
  if (absOffset <= separatedCenterOffset) {
    return radarConstants.contactWindowMeters * absOffset / Math.max(0.001, separatedCenterOffset);
  }

  return radarConstants.contactWindowMeters
    + (absOffset - separatedCenterOffset) / radarConstants.distinctRowPixelsPerMeter;
}

function uniqueRadarCars(cars) {
  const byCar = new Map();
  for (const car of cars) {
    const current = byCar.get(car.carIdx);
    if (!current || Math.abs(rangeRatio(car)) < Math.abs(rangeRatio(current))) {
      byCar.set(car.carIdx, car);
    }
  }
  return [...byCar.values()];
}

function sideWarningAttachments(hasCarLeft, hasCarRight, cars) {
  const used = new Set();
  const left = hasCarLeft ? selectSideAttachment(cars, used) : null;
  if (left) used.add(left.carIdx);
  const right = hasCarRight ? selectSideAttachment(cars, used) : null;
  return { left, right };
}

function selectSideAttachment(cars, used) {
  return cars
    .filter((car) => !used.has(car.carIdx) && isSideAttachmentCandidate(car))
    .sort((a, b) => Math.abs(rangeRatio(a)) - Math.abs(rangeRatio(b)) || a.carIdx - b.carIdx)[0] || null;
}

function isSideAttachmentCandidate(car) {
  return isInRadarRange(car)
    && Number.isFinite(car.relativeMeters)
    && Math.abs(car.relativeMeters) <= radarConstants.sideAttachmentWindowMeters;
}

function radarCarPlacements(cars, sideAttachments) {
  const usableRadius = radarConstants.usableRadarRadius;
  const visibleCars = cars
    .filter((car) => car.carIdx !== sideAttachments.left?.carIdx && car.carIdx !== sideAttachments.right?.carIdx)
    .sort((a, b) => Math.abs(rangeRatio(a)) - Math.abs(rangeRatio(b)))
    .slice(0, radarConstants.maxWideRowRadarCars);
  const candidates = visibleCars.map((car, index) => {
    const offset = longitudinalOffset(car, usableRadius);
    return {
      car,
      sourceIndex: index,
      idealOffset: offset,
      longitudinalMeters: Number.isFinite(car.relativeMeters) ? car.relativeMeters : null,
      direction: placementDirection(car, index, offset)
    };
  });
  const rows = [];
  for (const candidate of candidates.sort((a, b) => a.idealOffset - b.idealOffset)) {
    const row = rows.find((entry) => isSameWideRadarRow(entry, candidate));
    if (row) {
      row.candidates.push(candidate);
    } else {
      rows.push({
        anchorOffset: candidate.idealOffset,
        anchorMeters: candidate.longitudinalMeters,
        direction: candidate.direction,
        candidates: [candidate]
      });
    }
  }
  return rows.flatMap((row) => placementsForRow(row, usableRadius));
}

function isSameWideRadarRow(row, candidate) {
  if (Math.abs(row.direction - candidate.direction) > 0.001) return false;
  if (Number.isFinite(row.anchorMeters) && Number.isFinite(candidate.longitudinalMeters)) {
    return Math.abs(row.anchorMeters - candidate.longitudinalMeters) <= radarConstants.wideRowLongitudinalBucketMeters;
  }
  return Math.abs(row.anchorOffset - candidate.idealOffset) <= radarConstants.wideRowBucketPixels;
}

function placementsForRow(row, usableRadius) {
  if (!row.candidates.length) return [];
  const rowOffset = row.candidates.reduce((sum, candidate) => sum + candidate.idealOffset, 0) / row.candidates.length;
  const clampedRowMagnitude = Math.min(Math.abs(rowOffset), usableRadius);
  const availableHalfWidth = Math.sqrt(Math.max(0, usableRadius * usableRadius - clampedRowMagnitude * clampedRowMagnitude));
  const maxCenterOffset = Math.max(0, availableHalfWidth - radarConstants.radarCarWidth / 2 - 4);
  const minimumSlots = row.candidates.length > 1 ? 2 : 1;
  const maxSlots = Math.max(minimumSlots, Math.floor(maxCenterOffset * 2 / radarConstants.wideRowSlotPitchPixels) + 1);
  const visibleCandidates = row.candidates
    .sort((a, b) => a.sourceIndex - b.sourceIndex || a.car.carIdx - b.car.carIdx)
    .slice(0, maxSlots);
  const xOffsets = focusOverlappingRow(rowOffset)
    ? focusAvoidingXOffsets(visibleCandidates.length, maxCenterOffset)
    : centeredXOffsets(visibleCandidates.length);
  return visibleCandidates.slice(0, xOffsets.length).map((candidate, slotIndex) => {
    return {
      car: candidate.car,
      x: radarConstants.radarCenter + xOffsets[slotIndex] - radarConstants.radarCarWidth / 2,
      y: radarConstants.radarCenter - rowOffset - radarConstants.radarCarHeight / 2,
      offset: rowOffset
    };
  });
}

function centeredXOffsets(count) {
  const lineWidth = radarConstants.wideRowSlotPitchPixels * Math.max(0, count - 1);
  return Array.from({ length: count }, (_, index) => index * radarConstants.wideRowSlotPitchPixels - lineWidth / 2);
}

function focusOverlappingRow(rowOffset) {
  const verticalSeparation = radarConstants.focusedCarHeight / 2 + radarConstants.radarCarHeight / 2 + 4;
  return Math.abs(rowOffset) < verticalSeparation;
}

function focusAvoidingXOffsets(count, maxCenterOffset) {
  const minimumOffset = radarConstants.focusedCarWidth / 2 + radarConstants.radarCarWidth / 2 + 22;
  if (maxCenterOffset < minimumOffset) {
    return centeredXOffsets(count);
  }

  const offsets = [];
  const signs = count === 1 ? [1] : [-1, 1];
  for (let lane = 0; offsets.length < count; lane += 1) {
    for (const sign of signs) {
      const offset = sign * (minimumOffset + lane * radarConstants.wideRowSlotPitchPixels);
      if (Math.abs(offset) <= maxCenterOffset) {
        offsets.push(offset);
      }
      if (offsets.length >= count) break;
    }
    if (minimumOffset + lane * radarConstants.wideRowSlotPitchPixels > maxCenterOffset + radarConstants.wideRowSlotPitchPixels) break;
  }
  return offsets.length ? offsets : centeredXOffsets(count);
}

function nearbyCarRectangle(placement) {
  const visualAlpha = radarEntryOpacity(placement.car);
  return {
    kind: 'nearby',
    carIdx: placement.car.carIdx,
    x: placement.x,
    y: placement.y,
    width: radarConstants.radarCarWidth,
    height: radarConstants.radarCarHeight,
    radius: radarConstants.carCornerRadius,
    fill: proximityColor(proximityTint(placement.car), visualAlpha),
    stroke: classBorderColor(placement.car.carClassColorHex, visualAlpha),
    strokeWidth: 2
  };
}

function sideWarningRectangles(hasCarLeft, hasCarRight, sideAttachments) {
  const rectangles = [];
  const usableRadius = radarConstants.usableRadarRadius;
  if (hasCarLeft) {
    rectangles.push(sideWarningRectangle('left', radarConstants.radarCenter - 42, sideWarningCenterY(usableRadius, sideAttachments.left), sideAttachments.left));
  }
  if (hasCarRight) {
    rectangles.push(sideWarningRectangle('right', radarConstants.radarCenter + 42, sideWarningCenterY(usableRadius, sideAttachments.right), sideAttachments.right));
  }
  return rectangles;
}

function sideWarningRectangle(side, centerX, centerY, attachment) {
  const fillAlpha = attachment ? 245 : 238;
  return {
    kind: `side-${side}`,
    carIdx: attachment?.carIdx ?? null,
    x: centerX - radarConstants.radarCarWidth / 2,
    y: centerY - radarConstants.radarCarHeight / 2,
    width: radarConstants.radarCarWidth,
    height: radarConstants.radarCarHeight,
    radius: radarConstants.carCornerRadius,
    fill: rgba(236, 112, 99, fillAlpha),
    stroke: classBorderColor(attachment?.carClassColorHex, fillAlpha / 255),
    strokeWidth: 2
  };
}

function sideWarningCenterY(usableRadius, car) {
  if (!car) return radarConstants.radarCenter;
  const maximumBias = radarConstants.focusedCarHeight * 0.55;
  const offset = Math.max(-maximumBias, Math.min(maximumBias, longitudinalOffset(car, usableRadius)));
  return radarConstants.radarCenter - offset;
}

function playerCarRectangle(referenceCarClassColorHex) {
  return {
    kind: 'focus',
    carIdx: null,
    x: radarConstants.radarCenter - radarConstants.focusedCarWidth / 2,
    y: radarConstants.radarCenter - radarConstants.focusedCarHeight / 2,
    width: radarConstants.focusedCarWidth,
    height: radarConstants.focusedCarHeight,
    radius: radarConstants.carCornerRadius,
    fill: rgba(255, 255, 255, 240),
    stroke: classBorderColor(referenceCarClassColorHex, 1),
    strokeWidth: 2
  };
}

function multiclassApproachArc(approach) {
  const urgency = Math.max(0, Math.min(1, Number.isFinite(approach.urgency) ? approach.urgency : 0));
  const alpha = Math.round(120 + urgency * 110);
  return {
    x: radarConstants.radarInset + 4,
    y: radarConstants.radarInset + 4,
    width: radarConstants.radarDiameter - 8,
    height: radarConstants.radarDiameter - 8,
    startDegrees: radarConstants.multiclassWarningArcStartDegrees,
    sweepDegrees: radarConstants.multiclassWarningArcSweepDegrees,
    strokeWidth: 5,
    stroke: rgba(236, 112, 99, alpha),
    label: {
      text: Number.isFinite(approach.relativeSeconds)
        ? `Faster class approaching ${Math.abs(approach.relativeSeconds).toFixed(1)}s`
        : 'Faster class approaching',
      x: radarConstants.radarInset + 28,
      y: radarConstants.radarInset + radarConstants.radarDiameter - 48,
      width: radarConstants.radarDiameter - 56,
      height: 18,
      fontSize: 9,
      bold: true,
      alignment: 'center',
      color: rgba(255, 225, 220, alpha)
    }
  };
}

function longitudinalOffset(car, usableRadius) {
  if (Number.isFinite(car.relativeMeters)) {
    return longitudinalOffsetFromDistance(placementMeters(car.relativeMeters), usableRadius);
  }
  return Math.sign(car.relativeLaps || 0) * usableRadius;
}

function longitudinalOffsetFromDistance(meters, usableRadius) {
  const sign = Math.sign(meters);
  if (sign === 0) return 0;
  const absMeters = Math.abs(meters);
  const separatedCenterOffset = Math.min(
    usableRadius,
    radarConstants.focusedCarHeight / 2 + radarConstants.radarCarHeight / 2 + radarConstants.separatedCarPaddingPixels);
  if (absMeters <= radarConstants.contactWindowMeters) {
    return sign * (absMeters / radarConstants.contactWindowMeters) * separatedCenterOffset;
  }
  const rowAwareOffset = separatedCenterOffset
    + (absMeters - radarConstants.contactWindowMeters) * radarConstants.distinctRowPixelsPerMeter;
  return sign * rowAwareOffset;
}

function placementDirection(car, index, idealOffset) {
  if (idealOffset < 0) return -1;
  if (idealOffset > 0) return 1;
  if (Math.abs(car.relativeLaps || 0) > 0.0001) return car.relativeLaps < 0 ? -1 : 1;
  return index % 2 === 0 ? 1 : -1;
}

function rangeRatio(car) {
  if (Number.isFinite(car.relativeMeters)) {
    return Math.max(-1, Math.min(1, car.relativeMeters / visualRadarRangeMeters(car)));
  }
  return Math.sign(car.relativeLaps || 0);
}

function isInRadarRange(car) {
  return Number.isFinite(car?.relativeMeters) && Math.abs(car.relativeMeters) <= visualRadarRangeMeters(car);
}

function proximityColor(proximityTintValue, visualAlpha) {
  const normalized = Math.max(0, Math.min(1, proximityTintValue));
  const alpha = scaleAlpha(238, visualAlpha);
  if (normalized <= 0) return rgba(255, 255, 255, alpha);
  if (normalized < radarConstants.proximityRedStart) {
    const yellowMix = smoothStep(0, radarConstants.proximityRedStart, normalized);
    return rgba(lerp(255, 255, yellowMix), lerp(255, 220, yellowMix), lerp(255, 66, yellowMix), alpha);
  }
  const redMix = smoothStep(radarConstants.proximityRedStart, 1, normalized);
  return rgba(lerp(255, 255, redMix), lerp(220, 24, redMix), lerp(66, 16, redMix), alpha);
}

function proximityTint(car) {
  return Number.isFinite(car.relativeMeters) ? bumperGapProximity(Math.abs(car.relativeMeters)) : 0;
}

function radarEntryOpacity(car) {
  if (!Number.isFinite(car.relativeMeters)) return 0;
  const physicalOpacity = opacityBetweenRangeEdgeAndWarningStart(
    Math.abs(car.relativeMeters),
    radarConstants.contactWindowMeters + radarConstants.proximityWarningGapMeters,
    radarConstants.radarRangeMeters);
  const timingAwareOpacity = timingAwareEntryOpacity(
    Math.abs(car.relativeMeters),
    visualRadarRangeMeters(car));
  return Math.max(physicalOpacity, timingAwareOpacity);
}

function timingAwareEntryOpacity(absoluteMeters, visualRangeMeters) {
  if (visualRangeMeters <= radarConstants.radarRangeMeters || absoluteMeters >= visualRangeMeters) return 0;
  if (absoluteMeters <= radarConstants.radarRangeMeters) return radarConstants.timingAwareEdgeOpacity;
  const normalized = 1 - Math.max(
    0,
    Math.min(1, (absoluteMeters - radarConstants.radarRangeMeters) / Math.max(0.001, visualRangeMeters - radarConstants.radarRangeMeters)));
  return radarConstants.timingAwareEdgeOpacity * smoothStep(0, 1, normalized);
}

function placementMeters(meters) {
  return Math.sign(meters) * Math.min(Math.abs(meters), radarConstants.radarRangeMeters);
}

function visualRadarRangeMeters(car) {
  const range = radarConstants.radarRangeMeters;
  if (!Number.isFinite(car?.relativeMeters) || !Number.isFinite(car?.relativeSeconds)) return range;
  const absMeters = Math.abs(car.relativeMeters);
  const absSeconds = Math.abs(car.relativeSeconds);
  if (absMeters <= range || absSeconds <= 0.05) return range;
  const inferredMetersPerSecond = absMeters / absSeconds;
  if (!Number.isFinite(inferredMetersPerSecond) || inferredMetersPerSecond <= 0) return range;
  const timingAwareRange = inferredMetersPerSecond * radarConstants.timingAwareVisibilitySeconds;
  return Math.max(
    range,
    Math.min(radarConstants.maximumTimingAwareRangeMeters, timingAwareRange));
}

function opacityBetweenRangeEdgeAndWarningStart(absoluteValue, warningStart, radarRange) {
  if (absoluteValue <= warningStart) return 1;
  if (radarRange <= warningStart) return absoluteValue <= radarRange ? 1 : 0;
  const normalized = 1 - Math.max(0, Math.min(1, (absoluteValue - warningStart) / (radarRange - warningStart)));
  return smoothStep(0, 1, normalized);
}

function bumperGapProximity(centerDistanceMeters) {
  return 1 - Math.max(0, Math.min(1, (centerDistanceMeters - radarConstants.focusedCarLengthMeters) / radarConstants.proximityWarningGapMeters));
}

function scaleAlpha(alpha, multiplier) {
  return Math.round(Math.max(0, Math.min(255, alpha * multiplier)));
}

function classBorderColor(colorHex, visualAlpha) {
  const parsed = parseHexColor(colorHex);
  const alpha = scaleAlpha(245, visualAlpha);
  return parsed
    ? rgba(parsed.red, parsed.green, parsed.blue, alpha)
    : rgba(255, 255, 255, alpha);
}

function parseHexColor(value) {
  if (typeof value !== 'string') return null;
  const token = value.trim().replace(/^#/, '');
  if (!/^[0-9a-fA-F]{6}$/.test(token)) return null;
  return {
    red: Number.parseInt(token.slice(0, 2), 16),
    green: Number.parseInt(token.slice(2, 4), 16),
    blue: Number.parseInt(token.slice(4, 6), 16)
  };
}

function smoothStep(edge0, edge1, value) {
  const ratio = Math.max(0, Math.min(1, (value - edge0) / (edge1 - edge0)));
  return ratio * ratio * (3 - 2 * ratio);
}

function lerp(start, end, ratio) {
  return Math.round(start + (end - start) * ratio);
}

function rgba(red, green, blue, alpha) {
  return { red, green, blue, alpha };
}

function trackMapDisplayModel(page, live, settings) {
  const includeUserMaps = settings?.trackMapSettings?.includeUserMaps ?? settings?.includeUserMaps ?? true;
  return {
    ...emptyDisplayModel(page.page.id, page.title),
    status: 'live',
    headerItems: [{ key: 'status', value: 'live' }],
    source: 'source: live position telemetry',
    bodyKind: 'track-map',
    trackMap: {
      markers: trackMapMarkers(live),
      sectors: live?.models?.trackMap?.sectors || [],
      showSectorBoundaries: settings?.trackMapSettings?.showSectorBoundaries ?? settings?.showSectorBoundaries ?? true,
      internalOpacity: settings?.trackMapSettings?.internalOpacity ?? settings?.internalOpacity ?? 0,
      includeUserMaps,
      renderModel: trackMapRenderModel(live, settings)
    }
  };
}

function garageCoverDisplayModel(page, live, settings) {
  const garageVisible = live?.models?.raceEvents?.isGarageVisible === true;
  const browserSettings = settings?.garageCover || settings || {};
  const previewVisible = browserSettings.previewVisible === true;
  const detection = garageCoverDetection(live, garageVisible);
  const status = previewVisible ? 'preview visible' : detection.displayText;
  return {
    ...emptyDisplayModel(page.page.id, page.title),
    status,
    headerItems: [{ key: 'status', value: status }],
    source: 'source: garage telemetry/settings',
    bodyKind: 'garage-cover',
    garageCover: {
      shouldCover: previewVisible || !detection.isFresh || garageVisible,
      browserSettings,
      detection
    }
  };
}

function streamChatDisplayModel(page, live, settings) {
  const streamSettings = settings?.streamChat || settings || {};
  const browserSettings = {
    provider: streamSettings.provider || 'none',
    isConfigured: streamSettings.isConfigured === true,
    streamlabsWidgetUrl: streamSettings.streamlabsWidgetUrl ?? null,
    twitchChannel: streamSettings.twitchChannel ?? null,
    status: streamSettings.status || 'not_configured',
    contentOptions: streamSettings.contentOptions || defaultStreamChatContentOptions()
  };
  const replayRows = normalizeStreamChatRows(streamSettings.replayRows || streamSettings.rows);
  const isTwitch = browserSettings.isConfigured && browserSettings.provider === 'twitch' && browserSettings.twitchChannel;
  const isStreamlabs = browserSettings.isConfigured && browserSettings.provider === 'streamlabs';
  const message = !browserSettings.isConfigured
    ? streamChatStatusText(browserSettings.status)
    : isTwitch
      ? `Connecting to #${browserSettings.twitchChannel}...`
      : isStreamlabs
        ? 'Streamlabs is browser-source only in this build.'
        : 'Stream chat provider unavailable.';
  const status = replayRows.length > 0
    ? streamSettings.replayStatus || 'replay chat'
    : !browserSettings.isConfigured
      ? 'waiting for chat source'
      : isTwitch
        ? 'connecting | twitch'
        : isStreamlabs
          ? 'streamlabs unavailable'
          : 'chat provider unavailable';
  const rows = replayRows.length > 0
    ? replayRows
    : [{ name: 'TMR', text: message, kind: isStreamlabs || (browserSettings.isConfigured && !isTwitch) ? 'error' : 'system' }];
  return {
    ...emptyDisplayModel(page.page.id, page.title),
    status,
    headerItems: [{ key: 'status', value: status }],
    source: '',
    bodyKind: 'stream-chat',
    streamChat: {
      settings: browserSettings,
      rows
    }
  };
}

function browserStatus(headerItems, fallback) {
  return headerItems.find((item) => String(item.key || '').toLowerCase() === 'status')?.value
    || headerItems[0]?.value
    || fallback;
}

function normalizeStreamChatRows(rows) {
  return (Array.isArray(rows) ? rows : [])
    .map((row) => {
      const normalized = {
        name: String(row?.name || 'chat'),
        text: String(row?.text || ''),
        kind: ['message', 'notice', 'system', 'error'].includes(String(row?.kind || '').toLowerCase())
          ? String(row.kind).toLowerCase()
          : 'message',
        source: String(row?.source || ''),
        authorColorHex: /^#[0-9a-f]{6}$/i.test(String(row?.authorColorHex || ''))
          ? String(row.authorColorHex).toUpperCase()
          : null,
        metadata: Array.isArray(row?.metadata)
          ? row.metadata.map((item) => String(item)).filter(Boolean)
          : [],
        badges: Array.isArray(row?.badges)
          ? row.badges
              .map((badge) => ({
                id: String(badge?.id || badge?.label || '').trim(),
                version: String(badge?.version || '').trim(),
                label: String(badge?.label || badge?.id || '').trim(),
                roomId: badge?.roomId == null ? null : String(badge.roomId).trim()
              }))
              .filter((badge) => badge.label.length > 0)
          : [],
        segments: Array.isArray(row?.segments)
          ? row.segments
              .map((segment) => ({
                kind: String(segment?.kind || 'text').toLowerCase() === 'emote' ? 'emote' : 'text',
                text: String(segment?.text || ''),
                imageUrl: typeof segment?.imageUrl === 'string' ? segment.imageUrl : null
              }))
              .filter((segment) => segment.text.length > 0 || segment.imageUrl)
          : [{ kind: 'text', text: String(row?.text || ''), imageUrl: null }]
      };
      const twitch = normalizeStreamChatTwitchPayload(row?.twitch);
      if (twitch) {
        normalized.twitch = twitch;
      }
      return normalized;
    })
    .filter((row) => row.text.length > 0)
    .slice(-36);
}

function normalizeStreamChatTwitchPayload(value) {
  if (!value || typeof value !== 'object' || Array.isArray(value)) {
    return null;
  }

  try {
    return JSON.parse(JSON.stringify(value));
  } catch {
    return null;
  }
}

function defaultStreamChatContentOptions() {
  return {
    showAuthorColor: true,
    showBadges: true,
    showBits: true,
    showFirstMessage: true,
    showReplies: true,
    showTimestamps: true,
    showEmotes: true,
    showAlerts: true,
    showMessageIds: false
  };
}

function garageCoverDetection(live, garageVisible) {
  if (live?.isConnected === false) {
    return { state: 'iracing_disconnected', displayText: 'iRacing disconnected', isFresh: false };
  }

  if (live?.isCollecting === false || live?.models?.raceEvents?.hasData === false) {
    return { state: 'waiting_for_telemetry', displayText: 'waiting for telemetry', isFresh: false };
  }

  const lastUpdated = Date.parse(live?.lastUpdatedAtUtc || '');
  if (!Number.isFinite(lastUpdated)) {
    return { state: 'waiting_for_telemetry', displayText: 'waiting for telemetry', isFresh: false };
  }

  if (Date.now() - lastUpdated > 2500) {
    return { state: 'telemetry_stale', displayText: 'telemetry stale', isFresh: false };
  }

  return {
    state: garageVisible ? 'garage_visible' : 'garage_hidden',
    displayText: garageVisible ? 'garage visible' : 'garage hidden',
    isFresh: true
  };
}

function isPlayerInCar(live) {
  const race = live?.models?.raceEvents || {};
  const reference = live?.models?.reference || {};
  const directory = live?.models?.driverDirectory || {};
  const playerCarIdx = Number.isFinite(reference.playerCarIdx) ? reference.playerCarIdx : directory.playerCarIdx;
  const focusCarIdx = Number.isFinite(reference.focusCarIdx) ? reference.focusCarIdx : directory.focusCarIdx;
  if (!Number.isFinite(playerCarIdx) || !Number.isFinite(focusCarIdx)) return false;
  if (reference.focusIsPlayer === false) return false;
  if (focusCarIdx !== playerCarIdx) {
    return false;
  }
  if (race.isInGarage === true || race.isGarageVisible === true || reference.isInGarage === true) {
    return false;
  }
  const hasRaceContext = race.hasData === true || reference.hasData === true;
  if (!hasRaceContext) return true;
  return race.isOnTrack === true || reference.isOnTrack === true;
}

function trackMapMarkers(live) {
  const rows = [
    ...(live?.models?.timing?.overallRows || []),
    ...(live?.models?.timing?.classRows || [])
  ];
  const reference = live?.models?.reference || {};
  const referenceCarIdx = live?.models?.reference?.focusCarIdx
    ?? live?.models?.timing?.focusCarIdx
    ?? live?.models?.driverDirectory?.focusCarIdx;
  const markers = new Map();
  for (const row of rows) {
    if (row.hasSpatialProgress === false || !Number.isFinite(row.lapDistPct) || row.lapDistPct < 0) continue;
    const isFocus = row.isFocus === true || row.carIdx === referenceCarIdx;
    if (!isFocus && row.hasTakenGrid !== true) continue;
    markers.set(row.carIdx, {
      carIdx: row.carIdx,
      lapDistPct: normalizeProgress(row.lapDistPct),
      isFocus,
      classColorHex: row.carClassColorHex || null,
      position: row.classPosition ?? row.overallPosition ?? null,
      trackSurface: Number.isFinite(row.trackSurface) ? row.trackSurface : null,
      alertKind: isFocus ? null : row.trackMapAlertKind ?? row.alertKind ?? null,
      alertPulseProgress: isFocus ? 0 : row.trackMapAlertPulseProgress ?? row.alertPulseProgress ?? 0
    });
  }

  const referenceProgress = Number.isFinite(reference.lapDistPct) ? reference.lapDistPct : null;
  const referenceTrackSurface = Number.isFinite(reference.playerTrackSurface)
    ? reference.playerTrackSurface
    : Number.isFinite(reference.trackSurface)
      ? reference.trackSurface
      : null;
  if (Number.isFinite(referenceCarIdx)
    && referenceProgress !== null
    && referenceProgress >= 0
    && reference.onPitRoad !== true
    && reference.playerOnPitRoad !== true
    && referenceTrackSurface !== 1
    && referenceTrackSurface !== 2) {
    const existing = markers.get(referenceCarIdx);
    const focusRow = live?.models?.timing?.focusRow;
    markers.set(referenceCarIdx, {
      carIdx: referenceCarIdx,
      lapDistPct: normalizeProgress(referenceProgress),
      isFocus: true,
      classColorHex: null,
      position: existing?.position
        ?? focusRow?.classPosition
        ?? focusRow?.overallPosition
        ?? reference.classPosition
        ?? reference.overallPosition
        ?? null,
      trackSurface: referenceTrackSurface,
      alertKind: null,
      alertPulseProgress: 0
    });
  }

  return [...markers.values()].sort((left, right) => Number(left.isFocus) - Number(right.isFocus) || left.carIdx - right.carIdx);
}

function trackMapRenderModel(live, settings) {
  const markers = trackMapMarkers(live);
  const sectors = live?.models?.trackMap?.sectors || [];
  const trackMap = settings?.trackMap ?? null;
  const internalOpacity = settings?.trackMapSettings?.internalOpacity ?? settings?.internalOpacity ?? 0;
  const showSectorBoundaries = settings?.trackMapSettings?.showSectorBoundaries ?? settings?.showSectorBoundaries ?? true;
  const primitives = trackMap?.racingLine?.points?.length >= 3
    ? generatedTrackMapPrimitives(trackMap, sectors, internalOpacity, showSectorBoundaries)
    : circleTrackMapPrimitives(sectors, internalOpacity, showSectorBoundaries);
  return {
    width: 360,
    height: 360,
    isAvailable: true,
    mapKind: trackMap?.racingLine?.points?.length >= 3 ? 'generated' : 'circle',
    primitives,
    markers: markers.map((marker) => trackMapRenderMarker(marker, trackMap))
  };
}

function generatedTrackMapPrimitives(trackMap, sectors, internalOpacity, showSectorBoundaries) {
  const transform = trackMapTransform(trackMap);
  if (!transform) return circleTrackMapPrimitives(sectors, internalOpacity, showSectorBoundaries);
  const racing = renderPath(trackMap.racingLine, transform);
  const primitives = [];
  if (trackMap.racingLine?.closed === true && racing.length >= 3) {
    primitives.push(primitivePath(racing, true, trackInteriorFill(internalOpacity), null, 0));
  }
  primitives.push(
    primitivePath(racing, trackMap.racingLine?.closed === true, null, rgba(255, 255, 255, 82), 11),
    primitivePath(racing, trackMap.racingLine?.closed === true, null, rgba(222, 237, 245, 255), 4.4));
  for (const sector of sectors.filter(hasTrackMapHighlight)) {
    for (const range of segmentRanges(sector.startPct, sector.endPct)) {
      const points = renderSegment(trackMap.racingLine, transform, range.startPct, range.endPct);
      if (points.length >= 2) {
        primitives.push(primitivePath(points, false, null, sectorHighlightColor(sector.highlight), 5.8));
      }
    }
  }
  if (showSectorBoundaries) {
    for (const boundary of boundaryMarkers(sectors)) {
      const tick = geometryBoundaryTick(trackMap.racingLine, transform, boundary.progress);
      if (tick) addBoundaryPrimitives(primitives, tick.start, tick.end, isStartFinish(boundary.progress), boundary.highlight);
    }
  }
  if (trackMap?.pitLane?.points?.length >= 2) {
    primitives.push(primitivePath(renderPath(trackMap.pitLane, transform), false, null, rgba(98, 199, 255, 190), 2.2));
  }
  return primitives;
}

function circleTrackMapPrimitives(sectors, internalOpacity, showSectorBoundaries) {
  const rect = { x: 20, y: 20, width: 320, height: 320 };
  const primitives = [
    primitiveEllipse(rect, trackInteriorFill(internalOpacity), null, 0),
    primitiveEllipse(rect, null, rgba(255, 255, 255, 82), 11),
    primitiveEllipse(rect, null, rgba(222, 237, 245, 255), 4.4)
  ];
  for (const sector of sectors.filter(hasTrackMapHighlight)) {
    for (const range of segmentRanges(sector.startPct, sector.endPct)) {
      primitives.push({
        kind: 'arc',
        points: [],
        closed: false,
        rect,
        startDegrees: range.startPct * 360 - 90,
        sweepDegrees: (range.endPct - range.startPct) * 360,
        fill: null,
        stroke: sectorHighlightColor(sector.highlight),
        strokeWidth: 5.8
      });
    }
  }
  if (showSectorBoundaries) {
    for (const boundary of boundaryMarkers(sectors)) {
      const progress = boundary.progress;
      const point = pointOnCircle(progress);
      const dx = point.x - 180;
      const dy = point.y - 180;
      const length = Math.max(0.001, Math.hypot(dx, dy));
      const half = boundaryTickLength(progress) / 2;
      addBoundaryPrimitives(
        primitives,
        { x: point.x - dx / length * half, y: point.y - dy / length * half },
        { x: point.x + dx / length * half, y: point.y + dy / length * half },
        isStartFinish(progress),
        boundary.highlight);
    }
  }
  return primitives;
}

function trackMapRenderMarker(marker, trackMap) {
  const transform = trackMap?.racingLine?.points?.length >= 3 ? trackMapTransform(trackMap) : null;
  const point = transform ? pointOnGeometry(trackMap.racingLine, transform, marker.lapDistPct) : pointOnCircle(marker.lapDistPct);
  const label = Number.isFinite(marker.position) && marker.position > 0 ? String(marker.position) : null;
  const labelFontSize = marker.isFocus ? 7.6 : 5.4;
  const radius = label
    ? marker.isFocus
      ? Math.max(5.7, 5.1 + label.length * 2.9)
      : Math.max(5.7, 3.9 + Math.max(0, label.length - 2) * 1.9)
    : marker.isFocus ? 5.7 : Math.max(1.2, 3.6 - 2);
  const alertPulseProgress = marker.alertKind === 'off-track'
    ? Math.max(0, Math.min(1, finiteNumberOr(marker.alertPulseProgress, 0.25)))
    : 0;
  const alertActive = marker.alertKind === 'off-track';
  const alertRingRadius = alertActive ? radius + 3.2 + 5.2 * alertPulseProgress : 0;
  const alertRingAlpha = alertActive ? Math.round(230 - alertPulseProgress * 140) : 0;
  return {
    carIdx: marker.carIdx,
    x: point.x,
    y: point.y,
    radius,
    isFocus: marker.isFocus,
    fill: marker.alertKind === 'off-track'
      ? rgba(255, 218, 89, 255)
      : marker.isFocus ? rgba(0, 232, 255, 255) : classBorderColor(marker.classColorHex, 1),
    stroke: rgba(8, 14, 18, 230),
    strokeWidth: marker.isFocus ? 2 : 1.4,
    label,
    labelColor: rgba(5, 13, 17, 255),
    labelFontSize,
    alertKind: alertActive ? 'off-track' : null,
    alertPulseProgress,
    alertRingRadius,
    alertRingStroke: alertActive ? rgba(255, 247, 210, alertRingAlpha) : null,
    alertRingStrokeWidth: 2.3
  };
}

function trackMapTransform(trackMap) {
  const points = [
    ...(trackMap?.racingLine?.points || []),
    ...(trackMap?.pitLane?.points || [])
  ].filter((point) => Number.isFinite(point.x) && Number.isFinite(point.y));
  if (!points.length) return null;
  const minX = Math.min(...points.map((point) => point.x));
  const maxX = Math.max(...points.map((point) => point.x));
  const minY = Math.min(...points.map((point) => point.y));
  const maxY = Math.max(...points.map((point) => point.y));
  const width = Math.max(1, maxX - minX);
  const height = Math.max(1, maxY - minY);
  const scale = Math.min(320 / width, 320 / height);
  return {
    minX,
    maxY,
    scale,
    left: 20 + (320 - width * scale) / 2,
    top: 20 + (320 - height * scale) / 2
  };
}

function finiteNumberOr(value, fallback) {
  const number = Number(value);
  return Number.isFinite(number) ? number : fallback;
}

function mapTrackPoint(point, transform) {
  return {
    x: transform.left + (point.x - transform.minX) * transform.scale,
    y: transform.top + (transform.maxY - point.y) * transform.scale
  };
}

function renderPath(geometry, transform) {
  return (geometry?.points || []).map((point) => mapTrackPoint(point, transform));
}

function renderSegment(geometry, transform, startPct, endPct) {
  const start = pointOnGeometry(geometry, transform, startPct);
  const end = pointOnGeometry(geometry, transform, endPct);
  if (!start || !end) return [];
  return [
    start,
    ...(geometry?.points || [])
      .filter((point) => point.lapDistPct > startPct && point.lapDistPct < endPct)
      .map((point) => mapTrackPoint(point, transform)),
    end
  ];
}

function pointOnGeometry(geometry, transform, progress) {
  const points = geometry?.points || [];
  if (!points.length) return pointOnCircle(progress);
  const pct = normalizeProgress(progress);
  for (let index = 1; index < points.length; index += 1) {
    const previous = points[index - 1];
    const current = points[index];
    if (pct >= previous.lapDistPct && pct <= current.lapDistPct) {
      return interpolateTrackPoint(previous, current, pct, transform);
    }
  }
  if (geometry.closed) {
    const previous = points[points.length - 1];
    const current = { ...points[0], lapDistPct: points[0].lapDistPct + 1 };
    const target = pct < previous.lapDistPct ? pct + 1 : pct;
    return interpolateTrackPoint(previous, current, target, transform);
  }
  return mapTrackPoint(points[0], transform);
}

function interpolateTrackPoint(previous, current, target, transform) {
  const span = current.lapDistPct - previous.lapDistPct;
  const ratio = span <= 0 ? 0 : Math.max(0, Math.min(1, (target - previous.lapDistPct) / span));
  return mapTrackPoint({
    x: previous.x + (current.x - previous.x) * ratio,
    y: previous.y + (current.y - previous.y) * ratio
  }, transform);
}

function geometryBoundaryTick(geometry, transform, progress) {
  const center = pointOnGeometry(geometry, transform, progress);
  const before = pointOnGeometry(geometry, transform, progress - 0.002);
  const after = pointOnGeometry(geometry, transform, progress + 0.002);
  const dx = after.x - before.x;
  const dy = after.y - before.y;
  const length = Math.max(0.001, Math.hypot(dx, dy));
  const normalX = -dy / length;
  const normalY = dx / length;
  const half = boundaryTickLength(progress) / 2;
  return {
    start: { x: center.x - normalX * half, y: center.y - normalY * half },
    end: { x: center.x + normalX * half, y: center.y + normalY * half }
  };
}

function pointOnCircle(progress) {
  const angle = normalizeProgress(progress) * Math.PI * 2 - Math.PI / 2;
  return { x: 180 + Math.cos(angle) * 160, y: 180 + Math.sin(angle) * 160 };
}

function primitivePath(points, closed, fill, stroke, strokeWidth) {
  return { kind: 'path', points, closed, rect: null, startDegrees: 0, sweepDegrees: 0, fill, stroke, strokeWidth };
}

function primitiveEllipse(rect, fill, stroke, strokeWidth) {
  return { kind: 'ellipse', points: [], closed: false, rect, startDegrees: 0, sweepDegrees: 0, fill, stroke, strokeWidth };
}

function addBoundaryPrimitives(primitives, start, end, isStartFinishLine, highlight = 'none') {
  if (isStartFinishLine) {
    primitives.push(primitiveLine(start, end, rgba(5, 9, 14, 210), 5.8));
    primitives.push(primitiveLine(start, end, boundaryHighlightColor(highlight) || rgba(255, 209, 91, 255), 3.2));
    primitives.push(primitiveLine(start, end, rgba(255, 247, 255, 235), 1.2));
    return;
  }
  primitives.push(primitiveLine(start, end, boundaryHighlightColor(highlight) || rgba(0, 232, 255, 255), 2.2));
}

function primitiveLine(start, end, stroke, strokeWidth) {
  return { kind: 'line', points: [start, end], closed: false, rect: null, startDegrees: 0, sweepDegrees: 0, fill: null, stroke, strokeWidth };
}

function segmentRanges(startPct, endPct) {
  const start = normalizeProgress(startPct);
  const end = endPct >= 1 ? 1 : normalizeProgress(endPct);
  if (end <= start && endPct < 1) return [{ startPct: start, endPct: 1 }, { startPct: 0, endPct: end }];
  return [{ startPct: start, endPct: Math.max(0, Math.min(1, end)) }];
}

function boundaryMarkers(sectors) {
  if (!Array.isArray(sectors) || sectors.length < 2) return [];
  const seen = new Set();
  return sectors.map((sector) => ({
    progress: sector.endPct >= 1 ? 1 : normalizeProgress(sector.endPct),
    highlight: sector.boundaryHighlight || 'none'
  })).filter((boundary) => {
    const progress = normalizeProgress(boundary.progress);
    const key = Math.round(progress * 100000);
    if (seen.has(key)) return false;
    seen.add(key);
    return true;
  });
}

function hasTrackMapHighlight(sector) {
  return sector?.highlight === 'personal-best' || sector?.highlight === 'best-lap';
}

function sectorHighlightColor(highlight) {
  return highlight === 'best-lap' ? rgba(182, 92, 255, 255) : rgba(80, 214, 124, 255);
}

function boundaryHighlightColor(highlight) {
  return hasTrackMapHighlight({ highlight }) ? sectorHighlightColor(highlight) : null;
}

function trackInteriorFill(opacity) {
  const alpha = Math.round(150 * Math.max(0, Math.min(1, opacity)));
  return alpha <= 0 ? null : rgba(9, 14, 18, alpha);
}

function boundaryTickLength(progress) {
  return isStartFinish(progress) ? 17 * 1.45 : 17;
}

function isStartFinish(progress) {
  const normalized = normalizeProgress(progress);
  return normalized <= 0.0005 || normalized >= 0.9995;
}

function normalizeProgress(value) {
  const normalized = value % 1;
  return normalized < 0 ? normalized + 1 : normalized;
}

function streamChatStatusText(status) {
  switch (status) {
    case 'missing_or_invalid_streamlabs_url':
      return 'Choose Streamlabs and paste a valid Streamlabs Chat Box widget URL.';
    case 'missing_or_invalid_twitch_channel':
      return 'Choose Twitch and enter a valid public channel name.';
    default:
      return 'Choose Streamlabs or Twitch in the Stream Chat settings tab.';
  }
}

function normalizeRoute(route) {
  const text = String(route || '').trim().toLowerCase();
  return text.endsWith('/') && text.length > 1 ? text.slice(0, -1) : text;
}

function normalizePreviewMode(mode) {
  const normalized = String(mode || '').trim().toLowerCase();
  return ['practice', 'qualifying', 'race'].includes(normalized) ? normalized : 'off';
}

function themeCssVariables() {
  const contract = JSON.parse(readFileSync(resolve(repoRoot, 'shared/tmr-overlay-contract.json'), 'utf8'));
  const colors = contract.design.v2.colors;
  const variables = [
    ['--tmr-surface', colors.surface],
    ['--tmr-surface-inset', colors.surfaceInset],
    ['--tmr-surface-raised', colors.surfaceRaised],
    ['--tmr-title', colors.titleBar],
    ['--tmr-border', colors.border],
    ['--tmr-border-muted', colors.borderMuted],
    ['--tmr-text', colors.textPrimary],
    ['--tmr-text-rgb', colors.textPrimary],
    ['--tmr-text-secondary', colors.textSecondary],
    ['--tmr-text-secondary-rgb', colors.textSecondary],
    ['--tmr-text-muted', colors.textMuted],
    ['--tmr-text-muted-rgb', colors.textMuted],
    ['--tmr-text-dim', colors.textDim],
    ['--tmr-text-dim-rgb', colors.textDim],
    ['--tmr-cyan', colors.cyan],
    ['--tmr-cyan-rgb', colors.cyan],
    ['--tmr-magenta', colors.magenta],
    ['--tmr-magenta-rgb', colors.magenta],
    ['--tmr-amber', colors.amber],
    ['--tmr-amber-rgb', colors.amber],
    ['--tmr-green', colors.green],
    ['--tmr-green-rgb', colors.green],
    ['--tmr-orange', colors.orange],
    ['--tmr-orange-rgb', colors.orange],
    ['--tmr-error', colors.error],
    ['--tmr-error-rgb', colors.error],
    ['--tmr-best-lap', colors.bestLapSector],
    ['--tmr-track-line', colors.trackLine],
    ['--tmr-start-finish-boundary', colors.startFinishBoundary],
    ['--tmr-start-finish-boundary-shadow', colors.startFinishBoundaryShadow]
  ];
  return variables.map(([name, value]) => `      ${name}: ${name.endsWith('-rgb') ? rgbTuple(value) : cssColor(value)};`).join('\n');
}

function cssColor(value) {
  const match = /^#([0-9a-f]{6})([0-9a-f]{2})?$/i.exec(value);
  if (!match) return value;
  if (!match[2]) return `#${match[1].toLowerCase()}`;
  const rgb = Number.parseInt(match[1], 16);
  const alpha = Number.parseInt(match[2], 16) / 255;
  return `rgba(${(rgb >> 16) & 255}, ${(rgb >> 8) & 255}, ${rgb & 255}, ${Number(alpha.toFixed(3))})`;
}

function rgbTuple(value) {
  const match = /^#([0-9a-f]{6})/i.exec(value);
  if (!match) return value;
  const rgb = Number.parseInt(match[1], 16);
  return `${(rgb >> 16) & 255}, ${(rgb >> 8) & 255}, ${rgb & 255}`;
}
