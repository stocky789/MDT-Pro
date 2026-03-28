/**
 * Generates MDTPro/defaults/citationPedReactions.json — coherent full sentences + safe combos only.
 * node scripts/generateCitationPedReactions.mjs
 */
import fs from 'fs';
import path from 'path';
import { fileURLToPath } from 'url';

const __dirname = path.dirname(fileURLToPath(import.meta.url));
const root = path.join(__dirname, '..');
const outPath = path.join(root, 'MDTPro', 'defaults', 'citationPedReactions.json');

function mulberry32(a) {
  return function () {
    let t = (a += 0x6d2b79f5);
    t = Math.imul(t ^ (t >>> 15), t | 1);
    t ^= t + Math.imul(t ^ (t >>> 7), t | 61);
    return ((t ^ (t >>> 14)) >>> 0) / 4294967296;
  };
}

const rng = mulberry32(20260401);
const pick = (arr) => arr[Math.floor(rng() * arr.length)];

const PROF = [
  'God damn it',
  'Damn it',
  'Son of a bitch',
  'This is bullshit',
  'Are you shitting me',
  'Go to hell',
  'Screw this',
  'Kiss my ass',
  'Eat shit',
  'Fuck off',
  'Bullshit',
  'What the hell',
  'Holy shit',
  'For fuck sake',
  'Piss off',
  'Fuck this',
  'Piece of shit',
  'Up yours',
  'Shove it',
  'Blow me',
  'Motherfucker',
  'Asshole',
  'Dickhead',
  'Prick',
  'Cocksucker',
  'Garbage cop',
  'Tax collector with a gun',
  'Badge bully',
];

/** Full sentences only (mature opener added later). */
function matureFromBodies(bodies, count) {
  const out = new Set();
  let g = 0;
  while (out.size < count && g < count * 80) {
    g++;
    let b = pick(bodies);
    if (b.length < 12 || b.length > 130) continue;
    b = b.charAt(0).toLowerCase() + b.slice(1);
    out.add(`${pick(PROF)} — ${b}`);
  }
  return [...out];
}

/** Clean lines: question / statement templates — always grammatical. */
function cleanFromParts(openers, middles, closers, count) {
  const out = new Set();
  let g = 0;
  while (out.size < count && g < count * 100) {
    g++;
    const line = `${pick(openers)} ${pick(middles)}${pick(closers)}`.replace(/\s+/g, ' ').trim();
    if (line.length < 14 || line.length > 135) continue;
    if (!/[.?!]$/.test(line)) continue;
    out.add(line);
  }
  return [...out];
}

function rejectBad(line) {
  if (!line || line.length < 10) return true;
  if (/\bI are\b/i.test(line)) return true;
  if (/\bwe is\b|\bthey is\b|\byou is\b/i.test(line)) return true;
  if (/tickets (harassment|quota|pathetic|nonsense|entrapment|ridiculous)\.?$/i.test(line)) return true;
  if (/prints tickets (quota|pathetic|harassment|nonsense)/i.test(line)) return true;
  if (/loves tickets (harassment|pathetic|entrapment|quota|nonsense)/i.test(line)) return true;
  if (/writing tickets (harassment|quota|pathetic|entrapment|nonsense|ridiculous)\.?$/i.test(line)) return true;
  if (/\b(blow|dickhead|garbage cop|prick) — [a-z]/i.test(line) && / — [a-z]{2,} this is wrong/i.test(line)) return true;
  if (/\bfair I was\b/i.test(line)) return true;
  if (/\bserious Typical\.\s*$/i.test(line)) return true;
  if (/\bserious Unbelievable\?\s*$/i.test(line)) return true;
  if (/\bcourt will sort this in court\b/i.test(line)) return true;
  if (/\bfigures\.?\s*$/i.test(line) && /\bmoney figures\b/i.test(line)) return true;
  if (/\bhydrant is a joke ridiculous\b/i.test(line)) return true;
  if (/\bsticker harassment\b/i.test(line)) return true;
  if (/\bneighbor is nothing seriously\b/i.test(line)) return true;
  if (/\beveryone kidding me\b/i.test(line)) return true;
  if (/\b— i (public|did not)\b/i.test(line)) return true;
  if (/\s{2,}/.test(line)) return true;
  return false;
}

function filterLines(arr) {
  return [...new Set((arr || []).filter((s) => s && !rejectBad(s)))];
}

const officer = [' officer.', ' officer?'];
const manClose = [', man.', ', buddy.', ', friend.', ', seriously.'];
const soft = [
  ', can we not do this?',
  ', is this really necessary?',
  ', I was almost on time.',
  ', my insurance is going to love this.',
  ', I am already having a bad day.',
  ', could you cut me a break?',
  ', I was just trying to get home.',
];

const pools = {};

/* --- Generic --- */
pools.generic = {
  clean: filterLines(
    cleanFromParts(
      ['Are you', 'Seriously, are you', 'No way, are you'],
      ['kidding me', 'serious', 'serious right now'],
      ['?', ' officer?', ', man?', ' Unbelievable?', ' Really?'],
      55
    )
      .concat(
        cleanFromParts(
          ['Is this', 'How is this', 'Why is this'],
          ['serious', 'even legal', 'for real', 'a joke', 'real life'],
          ['?', ' officer?', ', buddy?', ' Really?', ' Unbelievable?'],
          55
        )
      )
      .concat(
        cleanFromParts(
          [
            'This is',
            'I cannot believe',
            'You have got to be',
            'This cannot be',
            'Every time, this is',
          ],
          [
            'ridiculous',
            'unfair',
            'insane',
            'a joke',
            'wrong',
            'my fault',
            'happening to me',
            'so typical',
          ],
          [...officer, ...manClose, ...soft, ' Unbelievable.', ' Typical.', ' Of course.', ' Here we go again.', '.'],
          110
        )
      )
  ),
  mature: filterLines(
    matureFromBodies(
      [
        'This ticket is a joke.',
        'This is pure harassment.',
        'You people have nothing better to do.',
        'Your department runs on fines.',
        'This feels like a shakedown.',
        'I am not thanking you for this.',
        'Write your ticket and leave me alone.',
        'This is why nobody trusts cops.',
        'Total waste of my time.',
        'You are having a power trip.',
        'This is pathetic enforcement.',
        'I will see you in court.',
        'My lawyer is going to love this.',
        'Quota met for the day yet?',
        'Hope that ticket was worth it.',
        'You just ruined my whole week.',
        'There goes my rent money.',
        'This city is a joke.',
        'Speed trap nonsense.',
        'Everyone knows this is about money.',
        'You hid behind a sign, real brave.',
        'I was going with traffic.',
        'Not even worth fighting, but I should.',
        'Fine, take my money.',
        'Whatever, I am done arguing.',
        'You win, happy now?',
        'This system is broken.',
        'Taxation with a badge.',
        'Harassment with paperwork.',
        'You must be so proud.',
        'Real hero work today.',
        'Public safety saved, huh?',
        'I feel so protected right now.',
        'My tax dollars hard at work.',
        'Give me the paper and go.',
        'I am not signing cheerfully.',
        'Shove that citation.',
        'This citation is garbage.',
        'Bullshit charge.',
        'Complete scam.',
        'Ridiculous stop.',
        'You picked the wrong person to mess with.',
        'I am recording this.',
        'This is getting disputed.',
        'I want your badge number.',
        'I am filing a complaint.',
        'Enjoy your coffee break after this.',
        'Hope it was worth the paperwork.',
        'You could be catching real criminals.',
        'Priorities are clearly straight here.',
        'Must be a slow day.',
        'Really, this is what you chose to do?',
        'Unbelievable abuse of power.',
        'This is entrapment vibes.',
        'Feels like a racket.',
        'City needs revenue that bad?',
        'Ticket printer working overtime?',
        'You are the real criminal here.',
        'Robbery with a uniform.',
        'Legalized theft.',
        'I hope you feel big.',
        'Tiny power, big ego.',
        'Badge does not make you right.',
        'Still a person under that uniform.',
        'Act like one sometime.',
        'I am late because of you.',
        'Thanks for nothing.',
        'Rot in hell for this.',
        'Burn in hell, ticket and all.',
        'Kiss my ass with that fine.',
        'Eat the whole citation.',
        'Shove the ticket where it fits.',
        'Fuck your quota.',
        'Fuck this ticket.',
        'Screw your department.',
        'Damn circus act.',
        'Circus act with a gun.',
        'Harassment plain and simple.',
        'I am done being polite.',
        'No respect left for this.',
        'You lost my respect today.',
        'This interaction is over.',
        'Walk away before I say worse.',
        'I am keeping my mouth shut now.',
        'Lawyer voice from here on.',
        'Say nothing without counsel.',
        'I know my rights.',
        'That is all you are getting from me.',
      ],
      160
    )
  ),
};

/* Cop / system hostility — only full sentences */
pools.cop_hostile = {
  clean: filterLines(
    cleanFromParts(
      [
        'My taxes',
        'This city',
        'The department',
        'Your chief',
        'We citizens',
        'People out here',
      ],
      [
        'pay your salary',
        'deserve better than ticket traps',
        'should focus on real crime',
        'see through this revenue game',
        'are tired of harassment stops',
      ],
      ['.', '!', ' officer.', ' man.'],
      80
    ).concat(
      cleanFromParts(
        ['This stop', 'This ticket', 'This citation', 'The fine'],
        [
          'feels like harassment',
          'is about money not safety',
          'does not protect anyone',
          'is a waste of resources',
          'should embarrass the department',
        ],
        ['.', '!', ' officer.'],
        60
      )
    )
  ),
  mature: filterLines(
    matureFromBodies(
      [
        'You are just a revenue collector.',
        'This department runs on our fines.',
        'Quota culture is obvious.',
        'Harassment with a smile.',
        'Badge does not excuse this.',
        'Your chief should be ashamed.',
        'Taxpayers paid for you to bother me.',
        'Real crime is out there, genius.',
        'This is why people hate traffic stops.',
        'I am done respecting this badge today.',
        'Shakedown complete, thanks.',
        'Robbery under color of law.',
        'Hope that ticket hits your bonus.',
        'Speed trap cowards everywhere.',
        'Hiding behind bushes, real brave.',
        'You people are parasites.',
        'Leech on working people.',
        'Fuck your quota and fuck this ticket.',
        'Eat my entire fine.',
        'Shove the city budget up yours.',
        'Corrupt little ticket mill.',
        'Scam with sirens.',
        'Garbage enforcement priorities.',
        'Proud of yourself, really?',
        'Tiny dick energy with a ticket book.',
        'Power trip in uniform.',
        'Try real police work sometime.',
        'Coward with a citation pad.',
        'Harassment is not policing.',
        'This stop was bullshit.',
        'I am filing on every channel.',
        'Internal affairs will hear it.',
        'Bodycam better be on.',
        'I know you need numbers.',
        'Meet your monthly numbers yet?',
        'Revenue officer, not peace officer.',
      ],
      120
    )
  ),
};

function poolTrafficSpeed() {
  const clean = cleanFromParts(
    ['I was', 'Nobody', 'Everyone', 'Traffic was', 'The road was'],
    [
      'only slightly over',
      'going with the flow',
      'not even fast',
      'barely above the limit',
      'matching other cars',
      'late for work',
      'rushing to an emergency',
    ],
    [...officer, ...soft, '.', ' The sign was hard to see.'],
    90
  );
  const mature = matureFromBodies(
    [
      'That was a speed trap and you know it.',
      'Everyone speeds here, you singled me out.',
      'This is a cash grab, not safety.',
      'Radar games for lunch money.',
      'You hid where nobody could see the limit.',
      'Barely over and you nail me.',
      'Highway robbery with a laser.',
      'Ticket printer goes brr, huh?',
      'Hope that mph was worth your soul.',
      'Fuck your radar and fuck this fine.',
      'Quota met, can I go now?',
      'This town farms drivers.',
      'Predatory enforcement.',
      'I was keeping up with traffic.',
      'Flow of traffic means nothing to you?',
      'Real criminals thank you for the distraction.',
    ],
    85
  );
  return { clean: filterLines(clean), mature: filterLines(mature) };
}

pools.traffic_speed = poolTrafficSpeed();

pools.traffic_signal = {
  clean: filterLines(
    cleanFromParts(
      ['That light', 'The signal', 'The yellow', 'The intersection'],
      [
        'was yellow when I entered',
        'just changed',
        'was blocked by a truck',
        'was confusing',
        'had weird timing',
      ],
      [...officer, ...soft, ' I stopped as soon as I could.', ' I swear it was yellow.'],
      75
    )
  ),
  mature: filterLines(
    matureFromBodies(
      [
        'That light was yellow and you know it.',
        'Camera racket, not justice.',
        'Red light scam city.',
        'I will fight this camera BS.',
        'You were waiting to burn people.',
        'Bullshit intersection trap.',
        'Yellow means go, you know that.',
        'Fuck your red light ticket.',
      ],
      55
    )
  ),
};

pools.parking = {
  clean: filterLines(
    cleanFromParts(
      ['I was', 'I only', 'There was', 'I needed'],
      [
        'parked two minutes',
        'loading only',
        'no other spaces',
        'a real emergency',
        'picking someone up',
        'the sign was unclear',
      ],
      [...officer, ...soft, '.'],
      70
    )
  ),
  mature: filterLines(
    matureFromBodies(
      [
        'Parking ticket for that is petty.',
        'hydrant drama, really?',
        'You have nothing better to patrol.',
        'Meter maid energy.',
        'Fuck this parking fine.',
      ],
      50
    )
  ),
};

pools.dui = {
  clean: filterLines(
    cleanFromParts(
      ['I', 'The reading', 'That test'],
      [
        'only had one drink',
        'was not impaired',
        'was fine to drive',
        'think the machine is wrong',
        'need a lawyer present',
      ],
      [...officer, ...soft, ' I am not drunk.'],
      75
    )
  ),
  mature: filterLines(
    matureFromBodies(
      [
        'I am not drunk and you know it.',
        'Field test bullshit.',
        'Breathalyzer can be wrong.',
        'Lawyer before another word.',
        'This DUI stop is harassment.',
        'Fuck your bogus reading.',
        'I will blow again in court.',
      ],
      60
    )
  ),
};

pools.drugs = {
  clean: filterLines(
    cleanFromParts(
      ['That', 'Those', 'This'],
      [
        'is not mine',
        'was planted',
        'is a prescription',
        'is CBD legal',
        'got mixed up in my bag',
      ],
      [...officer, '.', ' I want a lawyer.'],
      70
    )
  ),
  mature: filterLines(
    matureFromBodies(
      [
        'Prove chain of custody.',
        'Illegal search, lawyer now.',
        'Not mine, period.',
        'Fuck your planted evidence story.',
        'I am not saying another word.',
      ],
      55
    )
  ),
};

pools.assault = {
  clean: filterLines(
    cleanFromParts(
      ['He', 'She', 'They', 'The other person'],
      [
        'started it',
        'swung first',
        'was in my face',
        'it was self defense',
        'it was an accident',
      ],
      [...officer, ...soft, '.'],
      65
    )
  ),
  mature: filterLines(
    matureFromBodies(
      [
        'Self defense, read the facts.',
        'They threw first punch.',
        'This assault charge is backwards.',
        'I was protecting myself.',
        'Bullshit domestic narrative.',
      ],
      50
    )
  ),
};

pools.theft = {
  clean: filterLines(
    cleanFromParts(
      ['I', 'That item', 'The register'],
      [
        'paid for it',
        'was a mistake at checkout',
        'thought it was mine',
        'was going to pay',
      ],
      [...officer, ...soft, '.'],
      65
    )
  ),
  mature: filterLines(
    matureFromBodies(
      [
        'I did not steal anything.',
        'Receipt is on my phone.',
        'Wrong person, wrong day.',
        'Asset protection is lying.',
      ],
      45
    )
  ),
};

pools.arrestable = {
  clean: filterLines(
    cleanFromParts(
      ['I', 'That warrant', 'The court', 'My lawyer'],
      [
        'need a lawyer now',
        'thought that was cleared',
        'can handle this through my attorney',
        'know my rights',
        'am not resisting paperwork',
      ],
      [...officer, '.', ' I want an attorney.', ...manClose],
      95
    )
  ),
  mature: filterLines(
    matureFromBodies(
      [
        'Lawyer. Now. No questions.',
        'That warrant is old news.',
        'I already handled that case.',
        'You are not getting a confession.',
        'Arrest me, I will bond out.',
        'This is a mistake on the system.',
        'Clerical screw-up, not my fault.',
        'Fuck you, I still want counsel.',
      ],
      80
    )
  ),
};

pools.reckless = {
  clean: filterLines(
    cleanFromParts(
      ['I', 'The road', 'Traffic'],
      [
        'swerved to avoid debris',
        'merged fast because I had to',
        'lost control for one second',
        'was cut off',
      ],
      [...officer, ...soft, '.'],
      55
    )
  ),
  mature: filterLines(
    matureFromBodies(
      [
        'Reckless is exaggerated.',
        'One bad lane change, really?',
        'Everyone drives like that here.',
      ],
      40
    )
  ),
};

pools.seatbelt = {
  clean: filterLines(
    cleanFromParts(
      ['I', 'The belt'],
      [
        'was only moving the car in the lot',
        'unbuckled for one second',
        'forgot just now',
        'the latch sticks',
      ],
      [...officer, ...soft, '.'],
      45
    )
  ),
  mature: filterLines(
    matureFromBodies(
      [
        'Seatbelt ticket is nanny state BS.',
        'Really, for that?',
      ],
      30
    )
  ),
};

pools.distracted = {
  clean: filterLines(
    cleanFromParts(
      ['I', 'The phone'],
      [
        'was at a red light',
        'was on maps only',
        'used speaker hands-free',
        'looked once at a text',
      ],
      [...officer, ...soft, '.'],
      45
    )
  ),
  mature: filterLines(
    matureFromBodies(
      [
        'Everyone uses phones, you included.',
        'Hypocrite with a ticket book.',
      ],
      30
    )
  ),
};

pools.paperwork = {
  clean: filterLines(
    cleanFromParts(
      ['My', 'The', 'Registration'],
      [
        'renewal is in the mail',
        'insurance renewed yesterday',
        'paperwork is in the glove box',
        'rental car papers are messy',
      ],
      [...officer, ...soft, '.'],
      55
    )
  ),
  mature: filterLines(
    matureFromBodies(
      [
        'Paperwork ticket is petty.',
        'Victimless paperwork BS.',
      ],
      35
    )
  ),
};

pools.disorderly = {
  clean: filterLines(
    cleanFromParts(
      ['We', 'The party', 'The music'],
      [
        'were not fighting',
        'was not that loud',
        'neighbor complains about everything',
        'was a private gathering',
      ],
      [...officer, ...soft, '.'],
      50
    )
  ),
  mature: filterLines(
    matureFromBodies(
      [
        'Noise complaint is weak.',
        'Party was tame.',
      ],
      30
    )
  ),
};

pools.equipment = {
  clean: filterLines(
    cleanFromParts(
      ['The', 'I'],
      [
        'light just burned out',
        'did not notice the bulb',
        'ordered the part already',
        'getting it fixed tomorrow',
      ],
      [...officer, ...soft, '.'],
      45
    )
  ),
  mature: filterLines(
    matureFromBodies(
      [
        'Equipment ticket, really?',
        'Fix-it ticket culture.',
      ],
      28
    )
  ),
};

pools.pedestrian = {
  clean: filterLines(
    cleanFromParts(
      ['I', 'The crosswalk'],
      [
        'crossed with the group',
        'thought it was clear',
        'jaywalked once on an empty street',
      ],
      [...officer, ...soft, '.'],
      45
    )
  ),
  mature: filterLines(
    matureFromBodies(
      [
        'Jaywalking ticket is a joke.',
        'Nobody was endangered.',
      ],
      28
    )
  ),
};

pools.trespass = {
  clean: filterLines(
    cleanFromParts(
      ['I', 'We'],
      [
        'did not see the signs',
        'thought it was public',
        'were looking for an address',
        'cut through once',
      ],
      [...officer, ...soft, '.'],
      40
    )
  ),
  mature: filterLines(
    matureFromBodies(
      [
        'Trespass is overstated.',
        'I was not harming anything.',
      ],
      25
    )
  ),
};

pools.sarcastic = {
  clean: filterLines(
    cleanFromParts(
      ['Oh', 'Wow', 'Amazing', 'Great', 'Cool'],
      [
        'you caught the real menace to society',
        'public safety is saved',
        'hero work today',
        'my taxes well spent',
        'crime must be at zero now',
      ],
      ['.', '!', ' officer.', ' really.'],
      90
    )
  ),
  mature: filterLines(
    matureFromBodies(
      [
        'Real hero, a ticket pad.',
        'Scary paperwork, I am terrified.',
        'Wow, brave cop work.',
        'Oscar performance writing this.',
      ],
      55
    )
  ),
};

pools.defeated = {
  clean: filterLines(
    cleanFromParts(
      ['Fine', 'Whatever', 'Okay', 'Just'],
      [
        'write it',
        'I will pay it',
        'not worth the fight',
        'ruined my day',
        'there goes my budget',
      ],
      ['.', ' officer.', ' man.'],
      85
    )
  ),
  mature: filterLines(
    matureFromBodies(
      [
        'Fine, take the money.',
        'I am too tired to fight.',
        'You win, I give up today.',
        'Screw it, not worth my energy.',
      ],
      55
    )
  ),
};

const rules = [
  { keywords: ['speed', 'mph', 'limit', 'radar', 'fast', 'velocity', 'pace'], pool: 'traffic_speed' },
  { keywords: ['red light', 'redlight', 'signal', 'traffic light', 'stop sign', 'intersection'], pool: 'traffic_signal' },
  { keywords: ['park', 'parking', 'hydrant', 'fire lane', 'blocking', 'double park'], pool: 'parking' },
  { keywords: ['dui', 'dwi', 'drunk', 'intoxicat', 'alcohol', 'breath', 'sobriety', 'bac'], pool: 'dui' },
  { keywords: ['drug', 'narcotic', 'paraphernalia', 'cocaine', 'meth', 'heroin', 'marijuana', 'cannabis', 'controlled substance', 'possession'], pool: 'drugs' },
  { keywords: ['assault', 'battery', 'fight', 'strike', 'domestic'], pool: 'assault' },
  { keywords: ['theft', 'shoplift', 'burglary', 'stolen', 'larceny', 'robbery'], pool: 'theft' },
  { keywords: ['warrant', 'felony', 'failure to appear'], pool: 'arrestable' },
  { keywords: ['reckless', 'careless', 'exhibition', 'drag', 'racing'], pool: 'reckless' },
  { keywords: ['seat belt', 'seatbelt'], pool: 'seatbelt' },
  { keywords: ['phone', 'text', 'distracted', 'device', 'cell'], pool: 'distracted' },
  { keywords: ['insurance', 'registration', 'plate', 'expired tag', 'tags'], pool: 'paperwork' },
  { keywords: ['noise', 'disturb', 'loud', 'curfew', 'peace'], pool: 'disorderly' },
  { keywords: ['headlight', 'taillight', 'equipment', 'tail light', 'brake light'], pool: 'equipment' },
  { keywords: ['jaywalk', 'pedestrian', 'crosswalk', 'sidewalk'], pool: 'pedestrian' },
  { keywords: ['trespass'], pool: 'trespass' },
  { keywords: ['speed trap', 'ticket quota', 'revenue trap', 'quota trap'], pool: 'cop_hostile' },
];

// Final pass: trim empties, ensure minimum lines per pool
for (const k of Object.keys(pools)) {
  pools[k].clean = filterLines(pools[k].clean);
  pools[k].mature = filterLines(pools[k].mature);
}

const doc = { rules, pools };
fs.mkdirSync(path.dirname(outPath), { recursive: true });
fs.writeFileSync(outPath, JSON.stringify(doc, null, 2), 'utf8');

let total = 0;
for (const k of Object.keys(pools)) {
  total += (pools[k].clean?.length || 0) + (pools[k].mature?.length || 0);
}
console.log(`Wrote ${outPath} — ${Object.keys(pools).length} pools, ${total} lines, ${rules.length} rules.`);
