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
    settings: {
      version: 'Version',
      officerInformation: { title: 'Officer Information', firstName: 'First Name', lastName: 'Last Name', badgeNumber: 'Badge Number', rank: 'Rank', callSign: 'Call Sign', agency: 'Department', autoFill: 'Auto Fill', save: 'Save' },
      officerMetrics: { title: 'Career Statistics', totalShifts: 'Total Shifts', avgDuration: 'Avg Shift Duration', incidents: 'Incidents', citations: 'Citations', arrests: 'Arrests', totalReports: 'Total Reports', reportsPerShift: 'Reports/Shift' },
      currentShift: { startTime: 'Start', duration: 'Duration', offDuty: 'Off duty', startShift: 'Start Shift', endShift: 'End Shift' },
      customization: 'Open Customization',
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
};

function send(res, status, body, contentType = 'text/plain') {
  const buf = typeof body === 'string' ? Buffer.from(body, 'utf8') : body;
  res.writeHead(status, { 'Content-Type': contentType, 'Content-Length': buf.length });
  res.end(buf);
}

function serveFile(res, filePath, contentType) {
  fs.readFile(filePath, (err, data) => {
    if (err) {
      send(res, 404, 'Not found');
      return;
    }
    res.writeHead(200, { 'Content-Type': contentType, 'Content-Length': data.length });
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
  } else if (url === '/favicon') {
    filePath = path.join(ROOT, 'img', 'favicon.png');
    contentType = 'image/png';
  } else if (url === '/image/desktop' || url === '/image/desktop.png') {
    filePath = path.join(ROOT, 'img', 'desktop.png');
    contentType = 'image/png';
  } else if (url === '/image/badge' || url === '/image/badge.png' || url === '/image/badge.svg') {
    const badgeSvg = path.join(ROOT, 'img', 'badge.svg');
    const badgePng = path.join(ROOT, 'img', 'badge.png');
    if (fs.existsSync(badgeSvg)) {
      filePath = badgeSvg;
      contentType = 'image/svg+xml';
    } else {
      filePath = badgePng;
      contentType = 'image/png';
    }
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
    send(res, 200, '[]', 'application/json');
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
