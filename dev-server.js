/**
 * Minimal dev server to preview the MDT Pro UI without the game/plugin.
 * Run: node dev-server.js
 * Open: http://localhost:3010
 */
const http = require('http');
const fs = require('fs');
const path = require('path');

const PORT = 3010;
const ROOT = path.join(__dirname, 'MDTPro');

const MIME = {
  '.html': 'text/html',
  '.css': 'text/css',
  '.js': 'application/javascript',
  '.json': 'application/json',
  '.png': 'image/png',
  '.svg': 'image/svg+xml',
  '.ico': 'image/x-icon',
};

const mockConfig = {
  updateDomWithLanguageOnLoad: false,
  useInGameTime: false,
  showSecondsInTaskbarClock: false,
  initialWindowWidth: 900,
  initialWindowHeight: 600,
  port: PORT,
};

const mockLanguage = {
  index: {
    title: 'MDT Pro',
    static: {
      title: 'MDT Pro',
      desktop: {},
      taskbar: { settings: 'Control Panel' },
      settings: {
        officerInformation: {
          title: 'Officer Information',
          firstName: 'First Name',
          lastName: 'Last Name',
          badgeNumber: 'Badge Number',
          rank: 'Rank',
          callSign: 'Call Sign',
          agency: 'Department',
          autoFill: 'Fill from Game',
          save: 'Save',
          info: {
            title: 'Your character details. Used to pre-fill reports and show who is on duty.',
            firstName: 'Your character\'s first name.',
            lastName: 'Your character\'s last name.',
            badgeNumber: 'Your badge or employee number.',
            rank: 'e.g. Officer, Sergeant, Lieutenant.',
            callSign: 'Radio call sign or unit number (e.g. Adam-12).',
            agency: 'Your department or agency name.',
            autoFill: 'Pull your current character info from the game (LSPDFR).',
            save: 'Save these details to the MDT. They will be used on reports and when you start a shift.',
          },
        },
        currentShift: {
          title: 'Current Shift',
          startShift: 'Start Shift',
          endShift: 'End Shift',
          info: {
            title: 'Track your on-duty time. Start when you go on patrol, end when you finish.',
            startShift: 'Mark the start of your shift. Your info above is shown in notifications.',
            endShift: 'End your current shift. Duration is saved to your statistics.',
          },
        },
        officerMetrics: {
          title: 'Career Statistics',
          info: { title: 'Totals from your completed shifts and reports. Read-only.' },
        },
        customization: 'Config and Plugins',
        customizationInfo: 'Change config and manage installed plugins. Opens in a new tab.',
      },
    },
    settings: {
      version: 'Version',
      officerInformation: { title: 'Officer Information', firstName: 'First Name', lastName: 'Last Name', badgeNumber: 'Badge Number', rank: 'Rank', callSign: 'Call Sign', agency: 'Department', autoFill: 'Fill from Game', save: 'Save' },
      officerMetrics: { title: 'Career Statistics', totalShifts: 'Total Shifts', avgDuration: 'Avg. Shift Duration', incidents: 'Incidents', citations: 'Citations', arrests: 'Arrests', totalReports: 'Total Reports', reportsPerShift: 'Reports per Shift' },
      currentShift: { startTime: 'Start', duration: 'Duration', offDuty: 'Off duty', startShift: 'Start Shift', endShift: 'End Shift' },
      customization: 'Config and Plugins',
    },
    notifications: {
      currentShiftStarted: 'Shift started.',
      currentShiftStartedOfficerInformationExists: 'Shift started.',
      currentShiftStartedError: 'Failed to start shift.',
      currentShiftEnded: 'Shift ended.',
      currentShiftEndedError: 'Failed to end shift.',
      webSocketOnClose: 'Connection lost.',
      officerInformationSaved: 'Officer information saved.',
      officerInformationError: 'Failed to save.',
    },
  },
  desktop: { pedSearch: 'Person Search', vehicleSearch: 'Vehicle Search', reports: 'Reports', shiftHistory: 'Shift History', court: 'Court', map: 'GPS', callout: 'Active Call' },
  customization: {
    save: 'Save',
    reset: 'Reset',
    revert: 'Revert',
    revertTooltip: 'Discard unsaved changes and reload from server',
    searchPlaceholder: 'Filter settings…',
    saveSuccess: 'Config saved. Refreshing…',
    saveError: 'Failed to save.',
    static: { title: 'Customization', sidebar: { plugins: 'Plugins', config: 'Config' } },
    plugins: { version: 'Version', author: 'Author', noPlugins: 'No plugins installed.' },
  },
  units: {
    year: 'y', month: 'mo', day: 'd', hour: 'h', minute: 'm', second: 's',
    currencySymbol: '$', life: 'Life', meters: 'm', kilometers: 'km', feet: 'ft', miles: 'mi',
  },
};

function send(res, status, body, contentType = 'text/plain') {
  const buf = typeof body === 'string' ? Buffer.from(body, 'utf8') : body;
  res.writeHead(status, { 'Content-Type': contentType, 'Content-Length': buf.length });
  res.end(buf);
}

function serveFile(res, filePath, contentType, noCache) {
  fs.readFile(filePath, (err, data) => {
    if (err) {
      send(res, 404, 'Not found');
      return;
    }
    const headers = { 'Content-Type': contentType, 'Content-Length': data.length };
    if (noCache) {
      headers['Cache-Control'] = 'no-store, no-cache, must-revalidate';
      headers['Pragma'] = 'no-cache';
    }
    res.writeHead(200, headers);
    res.end(data);
  });
}

const server = http.createServer((req, res) => {
  const url = req.url.replace(/\?.*$/, '');
  let filePath;
  let contentType;

  if (url === '/') {
    filePath = path.join(ROOT, 'main', 'pages', 'index.html');
    contentType = 'text/html';
    serveFile(res, filePath, contentType, true);
    return;
  } else if (url === '/favicon' || url === '/favicon.svg') {
    const faviconSvg = path.join(ROOT, 'img', 'favicon.svg');
    const faviconPng = path.join(ROOT, 'img', 'favicon.png');
    if (fs.existsSync(faviconSvg)) {
      filePath = faviconSvg;
      contentType = 'image/svg+xml';
    } else if (fs.existsSync(faviconPng)) {
      filePath = faviconPng;
      contentType = 'image/png';
    } else {
      send(res, 404, 'Not found');
      return;
    }
    serveFile(res, filePath, contentType, true);
    return;
  } else if (url === '/image/desktop' || url === '/image/desktop.png') {
    filePath = path.join(ROOT, 'img', 'desktop.png');
    contentType = 'image/png';
  } else if (url === '/image/badge' || url === '/image/badge.png' || url === '/image/badge.svg') {
    const badgeSvg = path.join(ROOT, 'img', 'badge.svg');
    const badgePng = path.join(ROOT, 'img', 'badge.png');
    if (fs.existsSync(badgePng)) {
      filePath = badgePng;
      contentType = 'image/png';
    } else if (fs.existsSync(badgeSvg)) {
      filePath = badgeSvg;
      contentType = 'image/svg+xml';
    } else {
      send(res, 404, 'Not found');
      return;
    }
    serveFile(res, filePath, contentType, true);
    return;
  } else if (url === '/version') {
    send(res, 200, '1.0.0-dev', 'text/plain');
    return;
  } else if (url === '/config') {
    send(res, 200, JSON.stringify(mockConfig), 'application/json');
    return;
  } else if (url === '/language') {
    send(res, 200, JSON.stringify(mockLanguage), 'application/json');
    return;
  } else if (url === '/citationOptions') {
    try {
      const data = fs.readFileSync(path.join(ROOT, 'defaults', 'citationOptions.json'), 'utf8');
      send(res, 200, data, 'application/json');
    } catch {
      send(res, 200, '[]', 'application/json');
    }
    return;
  } else if (url === '/arrestOptions') {
    try {
      const data = fs.readFileSync(path.join(ROOT, 'defaults', 'arrestOptions.json'), 'utf8');
      send(res, 200, data, 'application/json');
    } catch {
      send(res, 200, '[]', 'application/json');
    }
    return;
  } else if (url === '/pluginInfo') {
    const pluginsDir = path.join(ROOT, 'plugins');
    const pluginDirs = fs.existsSync(pluginsDir) ? fs.readdirSync(pluginsDir, { withFileTypes: true }).filter(d => d.isDirectory()) : [];
    const plugins = pluginDirs.map(d => {
      const infoPath = path.join(pluginsDir, d.name, 'info.json');
      if (!fs.existsSync(infoPath)) return null;
      try {
        const info = JSON.parse(fs.readFileSync(infoPath, 'utf8'));
        info.id = d.name;
        info.pages = fs.existsSync(path.join(pluginsDir, d.name, 'pages'))
          ? fs.readdirSync(path.join(pluginsDir, d.name, 'pages')).filter(f => f.endsWith('.html')).map(f => f.replace('.html', '')) : [];
        info.scripts = fs.existsSync(path.join(pluginsDir, d.name, 'scripts'))
          ? fs.readdirSync(path.join(pluginsDir, d.name, 'scripts')).filter(f => f.endsWith('.js')).map(f => f.replace('.js', '')) : [];
        info.styles = fs.existsSync(path.join(pluginsDir, d.name, 'styles'))
          ? fs.readdirSync(path.join(pluginsDir, d.name, 'styles')).filter(f => f.endsWith('.css')).map(f => f.replace('.css', '')) : [];
        return info;
      } catch { return null; }
    }).filter(Boolean);
    send(res, 200, JSON.stringify(plugins), 'application/json');
    return;
  } else if (url.startsWith('/plugin/')) {
    const parts = url.slice('/plugin/'.length).split('/');
    if (parts.length >= 3) {
      const [pluginId, type, fileName] = parts;
      const ext = type === 'page' ? '.html' : type === 'script' ? '.js' : type === 'style' ? '.css' : null;
      if (ext) {
        const name = (fileName || '').replace(ext, '');
        const subPath = type === 'page' ? 'pages' : type === 'script' ? 'scripts' : 'styles';
        filePath = path.join(ROOT, 'plugins', pluginId, subPath, name + ext);
        contentType = MIME[ext] || 'text/plain';
        if (fs.existsSync(filePath) && path.resolve(filePath).startsWith(path.resolve(ROOT))) {
          serveFile(res, filePath, contentType);
          return;
        }
      }
    }
    send(res, 404, 'Not found');
    return;
  } else if (url === '/data/officerInformationData') {
    send(res, 200, JSON.stringify({}), 'application/json');
    return;
  } else if (url === '/data/officerMetrics') {
    send(res, 200, JSON.stringify({
      totalShifts: 0,
      averageShiftDurationMs: 0,
      totalIncidentReports: 0,
      totalCitationReports: 0,
      totalArrestReports: 0,
      totalReports: 0,
      reportsPerShift: 0,
    }), 'application/json');
    return;
  } else if (url === '/data/currentShift') {
    send(res, 200, JSON.stringify({}), 'application/json');
    return;
  } else if (url === '/data/shiftHistory') {
    const d = new Date();
    const mockShifts = [
      { startTime: new Date(d.getFullYear(), d.getMonth(), 1, 8, 0).toISOString(), endTime: new Date(d.getFullYear(), d.getMonth(), 1, 16, 30).toISOString(), reports: [] },
      { startTime: new Date(d.getFullYear(), d.getMonth(), 5, 7, 0).toISOString(), endTime: new Date(d.getFullYear(), d.getMonth(), 5, 15, 0).toISOString(), reports: [] },
      { startTime: new Date(d.getFullYear(), d.getMonth(), 12, 20, 0).toISOString(), endTime: new Date(d.getFullYear(), d.getMonth(), 13, 4, 0).toISOString(), reports: [] },
    ];
    send(res, 200, JSON.stringify(mockShifts), 'application/json');
    return;
  } else if (url === '/data/citationReports') {
    const d = new Date();
    const mockCitations = [
      { Id: 'C-1', TimeStamp: new Date(d.getFullYear(), d.getMonth(), 5, 14, 30).toISOString(), OffenderPedName: 'John Doe', Status: 1 },
      { Id: 'C-2', TimeStamp: new Date(d.getFullYear(), d.getMonth(), 12, 9, 15).toISOString(), OffenderPedName: 'Jane Smith', Status: 1 },
      { Id: 'C-3', TimeStamp: new Date(d.getFullYear(), d.getMonth(), 12, 16, 45).toISOString(), OffenderPedName: 'Bob Wilson', Status: 1 },
      { Id: 'C-4', TimeStamp: new Date(d.getFullYear(), d.getMonth(), 20, 11, 0).toISOString(), OffenderPedName: 'Alice Brown', Status: 1 },
    ];
    send(res, 200, JSON.stringify(mockCitations), 'application/json');
    return;
  } else if (url === '/data/arrestReports') {
    const d = new Date();
    const mockArrests = [
      { Id: 'A-1', TimeStamp: new Date(d.getFullYear(), d.getMonth(), 3, 22, 10).toISOString(), OffenderPedName: 'Mike Johnson', Status: 1 },
      { Id: 'A-2', TimeStamp: new Date(d.getFullYear(), d.getMonth(), 15, 2, 30).toISOString(), OffenderPedName: 'Sarah Davis', Status: 1 },
      { Id: 'A-3', TimeStamp: new Date(d.getFullYear(), d.getMonth(), 20, 18, 0).toISOString(), OffenderPedName: 'Tom Miller', Status: 1 },
    ];
    send(res, 200, JSON.stringify(mockArrests), 'application/json');
    return;
  } else if (url === '/data/incidentReports') {
    const d = new Date();
    const mockIncidents = [
      { Id: 'I-1', TimeStamp: new Date(d.getFullYear(), d.getMonth(), 5, 8, 0).toISOString(), OffenderPedsNames: ['Unknown Suspect'], Status: 1 },
      { Id: 'I-2', TimeStamp: new Date(d.getFullYear(), d.getMonth(), 18, 12, 20).toISOString(), OffenderPedsNames: ['John Doe'], Status: 1 },
    ];
    send(res, 200, JSON.stringify(mockIncidents), 'application/json');
    return;
  } else if (url.startsWith('/page/')) {
    const name = url.slice('/page/'.length).replace(/\.html$/, '') || 'index';
    filePath = path.join(ROOT, 'main', 'pages', name + '.html');
    contentType = 'text/html';
  } else if (url.startsWith('/style/')) {
    const name = url.slice('/style/'.length).replace(/\.css$/, '');
    filePath = path.join(ROOT, 'main', 'styles', name + '.css');
    contentType = 'text/css';
  } else if (url.startsWith('/script/')) {
    const name = url.slice('/script/'.length).replace(/\.js$/, '');
    filePath = path.join(ROOT, 'main', 'scripts', name + '.js');
    contentType = 'application/javascript';
  } else {
    send(res, 404, 'Not found');
    return;
  }

  if (!path.resolve(filePath).startsWith(path.resolve(ROOT))) {
    send(res, 403, 'Forbidden');
    return;
  }
  serveFile(res, filePath, contentType);
});

server.listen(PORT, '127.0.0.1', () => {
  console.log(`MDT Pro dev server: http://localhost:${PORT}`);
  console.log('(WebSocket time/location will not update; open apps to see sub-pages.)');
});
