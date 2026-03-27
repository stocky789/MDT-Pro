/**
 * DEV SERVER ONLY — Do not include in release zip or ship to end users.
 * Minimal dev server to preview the MDT Pro UI without the game/plugin.
 * Uses placeholder PED and vehicle data for testing; never used by the product mod.
 * Run: node dev-server.js
 * Open: http://localhost:3010
 */
const http = require('http');
const fs = require('fs');
const path = require('path');
const crypto = require('crypto');

const PORT = 3010;
const ROOT = path.join(__dirname, 'MDTPro');

// ---------- Placeholder data (dev only; never shipped with product) ----------
const ts = () => new Date().toISOString();
const placeholderPeds = [
  { Name: 'John Doe', FirstName: 'John', LastName: 'Doe', ModelName: 'a_m_y_skater_01', Gender: 'Male', Address: '123 Vinewood Blvd, Vinewood', Birthday: '1985-06-15', LicenseStatus: 'Valid', LicenseExpiration: '2026-12-31', WeaponPermitStatus: 'Valid', WeaponPermitType: 'Concealed Carry', WeaponPermitExpiration: '2026-06-30', IsWanted: false, WarrantText: null, IsOnProbation: false, IsOnParole: false, IsInGang: false, AdvisoryText: null, TimesStopped: 2, IdentificationHistory: [{ Type: 'State ID', Timestamp: ts() }], Citations: [], Arrests: [], HuntingPermitStatus: 'None', HuntingPermitExpiration: null, FishingPermitStatus: 'None', FishingPermitExpiration: null },
  { Name: 'Jane Smith', FirstName: 'Jane', LastName: 'Smith', ModelName: 'a_f_y_business_01', Gender: 'Female', Address: '456 Alta St, Downtown', Birthday: '1990-03-22', LicenseStatus: 'Valid', LicenseExpiration: '2027-01-15', WeaponPermitStatus: 'None', WeaponPermitType: null, WeaponPermitExpiration: null, IsWanted: false, WarrantText: null, IsOnProbation: false, IsOnParole: false, IsInGang: false, AdvisoryText: null, TimesStopped: 1, IdentificationHistory: [{ Type: 'State ID', Timestamp: ts() }], Citations: [], Arrests: [], HuntingPermitStatus: 'None', HuntingPermitExpiration: null, FishingPermitStatus: 'Valid', FishingPermitExpiration: '2025-08-01' },
  { Name: 'Mike Johnson', FirstName: 'Mike', LastName: 'Johnson', Gender: 'Male', Address: '789 Mirror Park Dr', Birthday: '1978-11-08', LicenseStatus: 'Suspended', LicenseExpiration: '2024-05-01', WeaponPermitStatus: 'None', WeaponPermitType: null, WeaponPermitExpiration: null, IsWanted: true, WarrantText: 'Failure to appear', IsOnProbation: false, IsOnParole: false, IsInGang: false, AdvisoryText: null, TimesStopped: 5, IdentificationHistory: [{ Type: 'State ID', Timestamp: ts() }], Citations: [], Arrests: [], HuntingPermitStatus: 'None', HuntingPermitExpiration: null, FishingPermitStatus: 'None', FishingPermitExpiration: null },
  { Name: 'Sarah Davis', FirstName: 'Sarah', LastName: 'Davis', Gender: 'Female', Address: '321 Davis Ave, Davis', Birthday: '1992-07-14', LicenseStatus: 'Valid', LicenseExpiration: '2026-09-20', WeaponPermitStatus: 'Valid', WeaponPermitType: 'Concealed Carry', WeaponPermitExpiration: '2026-03-01', IsWanted: false, WarrantText: null, IsOnProbation: false, IsOnParole: false, IsInGang: false, AdvisoryText: null, TimesStopped: 0, IdentificationHistory: [{ Type: 'State ID', Timestamp: ts() }], Citations: [], Arrests: [], HuntingPermitStatus: 'None', HuntingPermitExpiration: null, FishingPermitStatus: 'None', FishingPermitExpiration: null },
  { Name: 'Tom Miller', FirstName: 'Tom', LastName: 'Miller', Gender: 'Male', Address: '555 Palomino Ave, Sandy Shores', Birthday: '1988-01-30', LicenseStatus: 'Valid', LicenseExpiration: '2026-11-10', WeaponPermitStatus: 'None', WeaponPermitType: null, WeaponPermitExpiration: null, IsWanted: false, WarrantText: null, IsOnProbation: false, IsOnParole: false, IsInGang: false, AdvisoryText: null, TimesStopped: 3, IdentificationHistory: [{ Type: 'State ID', Timestamp: ts() }], Citations: [], Arrests: [], HuntingPermitStatus: 'Valid', HuntingPermitExpiration: '2025-12-01', FishingPermitStatus: 'None', FishingPermitExpiration: null },
  { Name: 'Alice Brown', FirstName: 'Alice', LastName: 'Brown', Gender: 'Female', Address: '100 Grove St, Grapeseed', Birthday: '1982-09-05', LicenseStatus: 'Valid', LicenseExpiration: '2027-02-28', WeaponPermitStatus: 'None', WeaponPermitType: null, WeaponPermitExpiration: null, IsWanted: false, WarrantText: null, IsOnProbation: false, IsOnParole: false, IsInGang: false, AdvisoryText: null, TimesStopped: 1, IdentificationHistory: [{ Type: 'State ID', Timestamp: ts() }], Citations: [], Arrests: [], HuntingPermitStatus: 'None', HuntingPermitExpiration: null, FishingPermitStatus: 'None', FishingPermitExpiration: null },
  { Name: 'Bob Wilson', FirstName: 'Bob', LastName: 'Wilson', Gender: 'Male', Address: '200 Procopio Dr, Paleto Bay', Birthday: '1975-12-18', LicenseStatus: 'Expired', LicenseExpiration: '2023-08-15', WeaponPermitStatus: 'None', WeaponPermitType: null, WeaponPermitExpiration: null, IsWanted: false, WarrantText: null, IsOnProbation: false, IsOnParole: false, IsInGang: false, AdvisoryText: null, TimesStopped: 4, IdentificationHistory: [{ Type: 'State ID', Timestamp: ts() }], Citations: [], Arrests: [], HuntingPermitStatus: 'None', HuntingPermitExpiration: null, FishingPermitStatus: 'None', FishingPermitExpiration: null },
];

const placeholderVehicles = [
  { LicensePlate: '12ABC345', ModelDisplayName: 'Police Cruiser', ModelName: 'POLICE', Make: 'Vapid', Owner: 'John Doe', Color: 'Black / White', VinStatus: 'Valid', VehicleIdentificationNumber: '1HGBH41JXMN109186', IsStolen: false, BOLOs: null, RegistrationStatus: 'Valid', RegistrationExpiration: '2026-06-15', InsuranceStatus: 'Valid', InsuranceExpiration: '2026-07-01' },
  { LicensePlate: '7LSV902', ModelDisplayName: 'Emperor', ModelName: 'EMPEROR', Make: 'Albany', Owner: 'Jane Smith', Color: 'Silver', VinStatus: 'Valid', VehicleIdentificationNumber: '2HGBH41JXMN109187', IsStolen: false, BOLOs: null, RegistrationStatus: 'Valid', RegistrationExpiration: '2026-03-20', InsuranceStatus: 'Valid', InsuranceExpiration: '2026-04-15' },
  { LicensePlate: '4FUN555', ModelDisplayName: 'Dominator', ModelName: 'DOMINATOR', Make: 'Vapid', Owner: 'Mike Johnson', Color: 'Red', VinStatus: 'Valid', VehicleIdentificationNumber: '3HGBH41JXMN109188', IsStolen: true, BOLOs: null, RegistrationStatus: 'Suspended', RegistrationExpiration: '2024-01-10', InsuranceStatus: 'Expired', InsuranceExpiration: '2023-12-01' },
  { LicensePlate: '22SAHP01', ModelDisplayName: 'Buffalo', ModelName: 'BUFFALO', Make: 'Bravado', Owner: 'Sarah Davis', Color: 'Blue', VinStatus: 'Valid', VehicleIdentificationNumber: '4HGBH41JXMN109189', IsStolen: false, BOLOs: null, RegistrationStatus: 'Valid', RegistrationExpiration: '2026-08-30', InsuranceStatus: 'Valid', InsuranceExpiration: '2026-09-15' },
  { LicensePlate: 'PLATE99', ModelDisplayName: 'Sandking', ModelName: 'SANDKING', Make: 'Vapid', Owner: 'Tom Miller', Color: 'Green', VinStatus: 'Scratched', VehicleIdentificationNumber: null, IsStolen: false, BOLOs: null, RegistrationStatus: 'Valid', RegistrationExpiration: '2026-12-01', InsuranceStatus: 'Valid', InsuranceExpiration: '2026-11-20' },
];

function getRecentIds() {
  return placeholderPeds
    .filter(p => p.IdentificationHistory && p.IdentificationHistory.length > 0)
    .slice(0, 8)
    .map(p => ({ Name: p.Name, Type: p.IdentificationHistory[0].Type, Timestamp: p.IdentificationHistory[0].Timestamp }));
}

function readBody(req) {
  return new Promise((resolve, reject) => {
    const chunks = [];
    req.on('data', chunk => chunks.push(chunk));
    req.on('end', () => resolve(Buffer.concat(chunks).toString('utf8')));
    req.on('error', reject);
  });
}

/** Parse body as plain string or JSON for name/plate lookups */
function parseNameOrPlate(body) {
  let s = (body || '').trim();
  if (!s) return '';
  if (s.startsWith('"') && s.endsWith('"')) {
    try { s = JSON.parse(s); } catch (_) {}
  } else if (s.startsWith('{')) {
    try {
      const o = JSON.parse(s);
      s = (o && (o.name || o.Name || o.licensePlate || o.LicensePlate || o.query || o.plate)) || '';
    } catch (_) {}
  }
  return typeof s === 'string' ? s.trim() : '';
}

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
  showListeningAddressNotification: true,
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
  pedSearch: {
    static: { title: 'Person Search', noPhoto: 'No photo available', drugRecordsTitle: 'Substance History', vehiclesOwnedTitle: 'Vehicles Owned', registeredFirearmsTitle: 'Registered Firearms', reportsTitle: 'Associated Reports' },
    notifications: {
      emptySearchInput: 'Enter a name to search.',
      pedNotFound: 'Person not found.',
      wanted: 'WANTED',
      advisory: 'ADVISORY',
      isOnProbation: 'is on probation',
      isOnParole: 'is on parole',
    },
  },
  vehicleSearch: {
    static: { title: 'Vehicle Search' },
    notifications: {
      emptySearchInput: 'Enter a plate or VIN to search.',
      vehicleNotFound: 'Vehicle not found.',
      stolen: 'ALERT',
      vehicleStolen: 'Vehicle',
      reportedStolen: 'reported STOLEN',
    },
  },
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
  quickActions: {
    narcoticsCheatsheet: 'Narcotics & Drugs Cheat Sheet',
    narcoticsCheatsheetClose: 'Close',
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

  // Don't respond to WebSocket upgrade so server.on('upgrade') can handle /ws
  if (url === '/ws' && req.headers.upgrade && String(req.headers.upgrade).toLowerCase() === 'websocket') {
    return;
  }

  if (url === '/' || url === '') {
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
  } else if (url === '/image/firearms' || url === '/image/firearms.svg') {
    filePath = path.join(ROOT, 'img', 'firearms.svg');
    contentType = 'image/svg+xml';
    serveFile(res, filePath, contentType, true);
    return;
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
  } else if (url === '/seizureOptions') {
    try {
      const data = fs.readFileSync(path.join(ROOT, 'defaults', 'seizureOptions.json'), 'utf8');
      send(res, 200, data, 'application/json');
    } catch {
      send(res, 200, JSON.stringify({ drugTypes: [], firearmTypes: [] }), 'application/json');
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
      let ext = type === 'page' ? '.html' : type === 'script' ? '.js' : type === 'style' ? '.css' : null;
      let subPath = type === 'page' ? 'pages' : type === 'script' ? 'scripts' : type === 'style' ? 'styles' : null;
      let fullName;
      if (type === 'image') {
        subPath = 'images';
        const imgName = (fileName || '').trim();
        fullName = imgName && (imgName.endsWith('.png') || imgName.endsWith('.jpg') || imgName.endsWith('.svg')) ? imgName : imgName + '.png';
      } else {
        fullName = (fileName || '').replace(ext || '', '') + (ext || '');
      }
      if (subPath) {
        filePath = path.join(ROOT, 'plugins', pluginId, subPath, fullName);
        contentType = type === 'image' ? (fullName.endsWith('.svg') ? 'image/svg+xml' : fullName.endsWith('.jpg') ? 'image/jpeg' : 'image/png') : (MIME[ext] || 'text/plain');
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
  } else if (url === '/data/peds') {
    send(res, 200, JSON.stringify(placeholderPeds), 'application/json');
    return;
  } else if (url === '/data/vehicles') {
    send(res, 200, JSON.stringify(placeholderVehicles), 'application/json');
    return;
  } else if (url === '/data/recentIds') {
    send(res, 200, JSON.stringify(getRecentIds()), 'application/json');
    return;
  } else if (url === '/data/impoundReports' || url === '/data/trafficIncidentReports' || url === '/data/injuryReports' || url === '/data/propertyEvidenceReports' || url === '/data/propertyEvidenceReceiptReports') {
    send(res, 200, '[]', 'application/json');
    return;
  } else if (url === '/data/court') {
    send(res, 200, '[]', 'application/json');
    return;
  } else if (url === '/data/activeBolos') {
    send(res, 200, '[]', 'application/json');
    return;
  } else if (url === '/data/playerLocation') {
    send(res, 200, JSON.stringify({ Area: 'Vinewood', Street: 'Vinewood Blvd', County: 'Los Santos', Postal: '90210' }), 'application/json');
    return;
  } else if (url === '/data/currentTime') {
    send(res, 200, new Date().toTimeString().slice(0, 8), 'text/plain');
    return;
  } else if (req.method === 'POST' && (url === '/data/specificPed' || url === '/data/specificPed/')) {
    readBody(req).then(body => {
      const name = parseNameOrPlate(body);
      const reversed = name.split(/\s+/).filter(Boolean).reverse().join(' ');
      const ped = placeholderPeds.find(p => p.Name && (p.Name.toLowerCase() === name.toLowerCase() || p.Name.toLowerCase() === reversed.toLowerCase()));
      send(res, 200, JSON.stringify(ped != null ? ped : null), 'application/json');
    }).catch(() => send(res, 500, 'Error reading body', 'text/plain'));
    return;
  } else if (req.method === 'POST' && (url === '/data/specificVehicle' || url === '/data/specificVehicle/')) {
    readBody(req).then(body => {
      const plateOrVin = parseNameOrPlate(body).toUpperCase();
      const vehicle = placeholderVehicles.find(v => v.LicensePlate && (v.LicensePlate.toUpperCase() === plateOrVin || (v.VehicleIdentificationNumber && v.VehicleIdentificationNumber.toUpperCase() === plateOrVin)));
      send(res, 200, JSON.stringify(vehicle != null ? vehicle : null), 'application/json');
    }).catch(() => send(res, 500, 'Error reading body', 'text/plain'));
    return;
  } else if (req.method === 'POST' && url === '/data/searchHistory') {
    readBody(req).then(body => {
      const type = (body || 'ped').trim().toLowerCase();
      const history = type === 'vehicle'
        ? [{ ResultName: '12ABC345', LastSearched: new Date().toISOString(), SearchCount: 1 }, { ResultName: '7LSV902', LastSearched: new Date().toISOString(), SearchCount: 1 }]
        : placeholderPeds.slice(0, 5).map(p => ({ ResultName: p.Name, LastSearched: new Date().toISOString(), SearchCount: 1 }));
      send(res, 200, JSON.stringify(history), 'application/json');
    }).catch(() => send(res, 200, '[]', 'application/json'));
    return;
  } else if (url === '/data/activePostalCodeSet') {
    send(res, 200, 'null', 'text/plain');
    return;
  } else if (url === '/data/officerInformation') {
    send(res, 200, JSON.stringify({}), 'application/json');
    return;
  } else if (req.method === 'POST' && url === '/data/nearbyVehicles') {
    readBody(req).then(body => {
      const limit = Math.min(20, Math.max(1, parseInt(body, 10) || 5));
      const list = placeholderVehicles.slice(0, limit).map(v => ({
        LicensePlate: v.LicensePlate,
        ModelDisplayName: v.ModelDisplayName,
        Distance: 10 + Math.floor(Math.random() * 50),
        IsStolen: v.IsStolen,
      }));
      send(res, 200, JSON.stringify(list), 'application/json');
    }).catch(() => send(res, 200, JSON.stringify(placeholderVehicles.slice(0, 5)), 'application/json'));
    return;
  } else if (req.method === 'POST' && url === '/data/vehicleSearchByPlate') {
    readBody(req).then(body => {
      let plate = (body || '').trim();
      try {
        const parsed = JSON.parse(body || '""');
        if (typeof parsed === 'string') plate = parsed.trim();
      } catch (_) {}
      plate = plate.toUpperCase();
      // Return vehicle search records (contraband/drugs), not the vehicle — frontend expects an array
      const vehicle = placeholderVehicles.find(v => v.LicensePlate && v.LicensePlate.toUpperCase() === plate);
      const mockRecords = vehicle ? [
        { ItemType: 'Drug', DrugType: 'Cocaine', Description: '2g', ItemLocation: 'Glovebox' },
        { ItemType: 'Contraband', Description: 'Stolen electronics', ItemLocation: 'Trunk' }
      ] : [];
      send(res, 200, JSON.stringify(mockRecords), 'application/json');
    }).catch(() => send(res, 200, '[]', 'application/json'));
    return;
  } else if (req.method === 'POST' && url === '/data/impoundReportsByPlate') {
    readBody(req).then(() => send(res, 200, '[]', 'application/json')).catch(() => send(res, 200, '[]', 'application/json'));
    return;
  } else if (req.method === 'POST' && (url === '/data/drugsByOwner' || url === '/data/firearmsForPed')) {
    readBody(req).then(() => send(res, 200, '[]', 'application/json')).catch(() => send(res, 200, '[]', 'application/json'));
    return;
  } else if (req.method === 'POST' && url === '/data/pedVehicles') {
    readBody(req).then(() => {
      send(res, 200, JSON.stringify(placeholderVehicles.filter(v => v.Owner).slice(0, 5)), 'application/json');
    }).catch(() => send(res, 200, '[]', 'application/json'));
    return;
  } else if (req.method === 'POST' && url === '/data/firearmBySerial') {
    readBody(req).then(() => send(res, 200, '{}', 'application/json')).catch(() => send(res, 200, '{}', 'application/json'));
    return;
  } else if (url === '/data/recentFirearmOwners') {
    send(res, 200, JSON.stringify(placeholderPeds.slice(0, 5).map(p => ({ Name: p.Name }))), 'application/json');
    return;
  } else if (url === '/data/recentFirearms') {
    send(res, 200, '[]', 'application/json');
    return;
  } else if (req.method === 'POST' && url === '/data/pedReports') {
    readBody(req).then(body => {
      const name = (body || '').trim().toLowerCase();
      const d = new Date();
      send(res, 200, JSON.stringify({
        citations: name ? [{ Id: 'C-1', TimeStamp: d.toISOString(), Status: 1 }] : [],
        arrests: [],
        incidents: [],
        propertyEvidence: [],
        injuries: [],
        impounds: [],
      }), 'application/json');
    }).catch(() => send(res, 200, JSON.stringify({ citations: [], arrests: [], incidents: [], propertyEvidence: [], injuries: [], impounds: [] }), 'application/json'));
    return;
  } else if (req.method === 'POST' && url === '/data/recentReports') {
    readBody(req).then(() => {
      send(res, 200, '[]', 'application/json');
    }).catch(() => send(res, 200, '[]', 'application/json'));
    return;
  } else if (req.method === 'POST' && (url === '/data/reportSummaries' || url === '/data/reportSummaries/')) {
    readBody(req).then(() => send(res, 200, '[]', 'application/json')).catch(() => send(res, 200, '[]', 'application/json'));
    return;
  } else if (req.method === 'POST' && url === '/post/attachReportsToArrest') {
    readBody(req).then(() => send(res, 200, JSON.stringify({ success: true, added: 0 }), 'application/json')).catch(() => send(res, 200, JSON.stringify({ success: true, added: 0 }), 'application/json'));
    return;
  } else if (req.method === 'POST' && url === '/post/attachReportToArrest') {
    readBody(req).then(() => send(res, 200, 'OK', 'text/plain')).catch(() => send(res, 200, 'OK', 'text/plain'));
    return;
  } else if (url.startsWith('/page/')) {
    const name = url.slice('/page/'.length).replace(/\.html$/, '') || 'index';
    filePath = path.join(ROOT, 'main', 'pages', name + '.html');
    contentType = 'text/html';
    if (!fs.existsSync(filePath)) {
      send(res, 404, `Page not found: ${name}`);
      return;
    }
  } else if (url.startsWith('/style/')) {
    const name = url.slice('/style/'.length).replace(/\.css$/, '');
    filePath = path.join(ROOT, 'main', 'styles', name + '.css');
    contentType = 'text/css';
  } else if (url.startsWith('/script/')) {
    const name = url.slice('/script/'.length).replace(/\.js$/, '');
    filePath = path.join(ROOT, 'main', 'scripts', name + '.js');
    contentType = 'application/javascript';
  } else if (url.startsWith('/data/')) {
    // Catch-all: return valid JSON so client .json() never gets "Not found" and throws
    send(res, 200, req.method === 'POST' ? '{}' : '[]', 'application/json');
    return;
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

// WebSocket /ws so the app's clock and shift data don't fail (dev placeholder)
server.on('upgrade', (req, socket, head) => {
  const url = (req.url || '').replace(/\?.*$/, '');
  if (url !== '/ws') {
    socket.destroy();
    return;
  }
  const key = req.headers['sec-websocket-key'];
  if (!key) {
    socket.destroy();
    return;
  }
  const accept = crypto.createHash('sha1').update(key + '258EAFA5-E914-47DA-95CA-C5AB0DC85B11').digest('base64');
  socket.write(
    'HTTP/1.1 101 Switching Protocols\r\nUpgrade: websocket\r\nConnection: Upgrade\r\nSec-WebSocket-Accept: ' + accept + '\r\n\r\n'
  );
  socket.write(head);
  const sendFrame = (payload) => {
    const text = typeof payload === 'string' ? payload : JSON.stringify(payload);
    const buf = Buffer.from(text, 'utf8');
    const len = buf.length;
    const frame = Buffer.allocUnsafe(2 + (len < 126 ? 0 : 2) + len);
    frame[0] = 0x81;
    if (len < 126) {
      frame[1] = len;
      buf.copy(frame, 2);
    } else {
      frame[1] = 126;
      frame.writeUInt16BE(len, 2);
      buf.copy(frame, 4);
    }
    socket.write(frame);
  };
  const now = () => new Date();
  const tick = () => {
    try {
      const d = now();
      const h = d.getHours();
      const m = d.getMinutes();
      const s = d.getSeconds();
      sendFrame({ response: `${h}:${m}:${s}` });
    } catch (_) {}
  };
  tick();
  const interval = setInterval(tick, 1000);
  socket.on('data', (data) => {
    // client can send "interval/time" etc.; we just keep sending time
  });
  socket.on('close', () => clearInterval(interval));
  socket.on('error', () => clearInterval(interval));
});

server.listen(PORT, '127.0.0.1', () => {
  console.log(`MDT Pro dev server: http://localhost:${PORT}`);
  console.log('(Open Person Search, Vehicle Search, Reports, etc. from the sidebar.)');
});
