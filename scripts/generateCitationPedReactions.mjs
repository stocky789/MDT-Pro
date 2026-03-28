/**
 * Generates MDTPro/defaults/citationPedReactions.json — full sentences only (no random fragment stitching).
 * node scripts/generateCitationPedReactions.mjs
 *
 * Bump PED_REACTIONS_DATA_VERSION when you change rules or dialogue so existing installs auto-receive
 * the new file (mod compares bundled vs installed "version" on load; players do not configure anything).
 */
import fs from 'fs';
import path from 'path';
import { fileURLToPath } from 'url';

/** Increment when changing generated dialogue so upgrades overwrite MDTPro/citationPedReactions.json. */
const PED_REACTIONS_DATA_VERSION = 1;

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

function rejectBad(line) {
  if (!line || line.length < 10) return true;
  if (/\bI are\b/i.test(line)) return true;
  if (/\bwe is\b|\bthey is\b|\byou is\b/i.test(line)) return true;
  if (/^\s*The did not\b/i.test(line)) return true;
  if (/^\s*The ordered\b/i.test(line)) return true;
  if (/^\s*The getting\b/i.test(line)) return true;
  if (/^\s*I getting\b/i.test(line)) return true;
  if (/^\s*I light just\b/i.test(line)) return true;
  if (/^\s*Nobody only\b/i.test(line)) return true;
  if (/\bThe department pay\b/i.test(line)) return true;
  if (/\bYour chief pay\b/i.test(line)) return true;
  if (/\bThose is not\b/i.test(line)) return true;
  if (/\bThe reading only\b/i.test(line)) return true;
  if (/\bThere was parked\b/i.test(line)) return true;
  if (/\bTraffic was late\b/i.test(line)) return true;
  if (/\bkidding me Unbelievable\?$/i.test(line)) return true;
  if (/\bkidding me Really\?$/i.test(line)) return true;
  if (/\bserious Really\?$/i.test(line)) return true;
  if (/\s{2,}/.test(line)) return true;
  return false;
}

function filterLines(arr) {
  return [...new Set((arr || []).filter((s) => s && !rejectBad(s)))];
}

const pools = {};

/* --- Generic --- */
pools.generic = {
  clean: filterLines([
    'Are you serious right now?',
    'Are you kidding me?',
    'Seriously, are you kidding me?',
    'No way, you cannot be serious.',
    'Is this a joke?',
    'How is this even fair?',
    'This is ridiculous.',
    'This cannot be happening.',
    'You have got to be kidding me.',
    'I cannot believe this.',
    'This is so unfair.',
    'Are you serious, officer?',
    'Come on, really?',
    'This feels completely wrong.',
    'Is this really necessary?',
    'There has to be some mistake.',
    'I do not understand this at all.',
    'This is insane.',
    'Every time I drive, something like this happens.',
    'Typical. Just typical.',
    'Of course this happens to me today.',
    'Unbelievable.',
    'Wow. Okay.',
    'Here we go again.',
    'This is not what I needed today.',
    'Can we talk about this for a second?',
    'I really think you have the wrong idea here.',
    'I am not trying to cause trouble.',
    'I was not doing anything dangerous.',
    'This is way out of proportion.',
    'I feel like I am being singled out.',
    'There must be worse things going on right now.',
    'I am late and this is making it worse.',
    'My whole day is ruined.',
    'Fine. Write the ticket.',
    'I will deal with it, I guess.',
    'I do not agree, but whatever.',
    'This is going to cost me a fortune.',
    'My insurance is going to hate me.',
    'I was just trying to get somewhere.',
    'I was almost on time until this.',
    'Could you cut me a break, please?',
    'Is there any chance of a warning?',
    'I have never had a ticket before.',
    'I honestly did not realize.',
    'I am not arguing, I am just shocked.',
    'This does not make any sense to me.',
  ]),
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
        'This citation is garbage.',
        'Bullshit charge.',
        'Complete scam.',
        'Ridiculous stop.',
        'You picked the wrong person to mess with.',
        'I am recording this.',
        'This is getting disputed.',
        'I want your badge number.',
        'I am filing a complaint.',
        'You could be catching real criminals.',
        'Must be a slow day.',
        'Really, this is what you chose to do?',
        'Unbelievable abuse of power.',
        'Feels like a racket.',
        'City needs revenue that bad?',
        'Badge does not make you right.',
        'Still a person under that uniform.',
        'I am late because of you.',
        'Thanks for nothing.',
        'Fuck this ticket.',
        'Screw your department.',
        'Harassment plain and simple.',
        'I am done being polite.',
        'You lost my respect today.',
        'This interaction is over.',
        'I know my rights.',
        'That is all you are getting from me.',
      ],
      160
    )
  ),
};

pools.cop_hostile = {
  clean: filterLines([
    'My taxes pay your salary.',
    'We are out here working while you write tickets.',
    'This city should focus on real crime.',
    'People are tired of stops like this.',
    'This feels like a revenue game.',
    'This stop does not make anyone safer.',
    'The fine is outrageous for what happened.',
    'This citation is a waste of everyone’s time.',
    'Your department should be embarrassed.',
    'There are actual emergencies happening.',
    'This is why people do not trust the police.',
    'I am a taxpayer and I deserve better than this.',
    'This is harassment, plain and simple.',
    'You are not protecting anyone with this.',
    'This ticket is about money, not safety.',
    'I see what this department is doing.',
    'Citizens notice these patterns.',
    'This is exactly the kind of stop people complain about.',
    'You should be ashamed of this ticket.',
    'Real criminals are out there somewhere.',
    'This is a pathetic use of resources.',
    'I cannot respect this kind of enforcement.',
    'The community deserves better policing.',
    'This is a joke and you know it.',
    'I am disgusted by this interaction.',
    'You are proving every stereotype right.',
    'This department has the wrong priorities.',
    'I am going to remember this.',
    'Someone above you needs to hear about this.',
    'This is not community policing.',
    'You are bullying people with fines.',
    'Shame on this whole department.',
    'I hope your chief sees this report.',
    'This is not what public service looks like.',
    'You picked an easy target instead of doing real work.',
    'I am not the problem in this city.',
    'Save the lectures for actual criminals.',
    'This is a cash grab and we both know it.',
    'I am done pretending this is okay.',
    'You are hiding behind a badge.',
  ]),
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
        'Hope that ticket hits your bonus.',
        'Speed trap cowards everywhere.',
        'Hiding behind bushes, real brave.',
        'Fuck your quota and fuck this ticket.',
        'Corrupt little ticket mill.',
        'Scam with sirens.',
        'Garbage enforcement priorities.',
        'Power trip in uniform.',
        'Try real police work sometime.',
        'Harassment is not policing.',
        'This stop was bullshit.',
        'I am filing on every channel.',
        'Internal affairs will hear it.',
        'Bodycam better be on.',
        'Revenue officer, not peace officer.',
      ],
      120
    )
  ),
};

pools.traffic_speed = {
  clean: filterLines([
    'I was only slightly over the limit.',
    'I was going the same speed as everyone else.',
    'Traffic was moving; I was matching the flow.',
    'I did not think I was going that fast.',
    'I was late for work and I rushed.',
    'I had an emergency and I was hurrying.',
    'The limit sign was easy to miss.',
    'I just came down a hill and picked up speed.',
    'I was passing someone and sped up for a moment.',
    'My speedometer might be off.',
    'I thought this road was a higher limit.',
    'Everyone drives faster here.',
    'I was keeping up with the cars ahead.',
    'I checked my mirror; I was not flying.',
    'It did not feel unsafe at that speed.',
    'I slowed down as soon as I saw you.',
    'I honestly thought I was fine.',
    'Can I get a warning? I am usually careful.',
    'I have a clean record until now.',
    'This feels like a trap, not safety.',
    'Nobody else got pulled over.',
    'I was barely over for a second.',
    'I was merging onto the highway.',
    'The road was clear and I spaced out.',
    'I am sorry; I will watch it closer.',
    'I did not see you until it was too late.',
    'I was trying to make a yellow light earlier.',
    'My cruise control must have bumped up.',
    'I had music on and lost track.',
    'I am not trying to make excuses, but that was harsh.',
    'Could you show me the reading again?',
    'I respect you, but I disagree with this.',
    'This is going to wreck my insurance.',
    'I was not racing anyone.',
    'I was not weaving through traffic.',
    'Kids in the car; I was not trying to speed.',
    'GPS said I was lower than that.',
    'I thought the zone ended back there.',
  ]),
  mature: filterLines(
    matureFromBodies(
      [
        'That was a speed trap and you know it.',
        'Everyone speeds here, you singled me out.',
        'This is a cash grab, not safety.',
        'Radar games for lunch money.',
        'You hid where nobody could see the limit.',
        'Barely over and you nail me.',
        'Highway robbery with a laser.',
        'Hope that mph was worth your soul.',
        'Fuck your radar and fuck this fine.',
        'Quota met, can I go now?',
        'This town farms drivers.',
        'Predatory enforcement.',
        'I was keeping up with traffic.',
        'Flow of traffic means nothing to you?',
      ],
      85
    )
  ),
};

pools.traffic_signal = {
  clean: filterLines([
    'The light was yellow when I entered the intersection.',
    'It turned red right as I crossed the line.',
    'I stopped as soon as I could.',
    'A truck blocked my view of the signal.',
    'The signal timing at that corner is confusing.',
    'I thought I could clear it safely.',
    'I have driven this route a hundred times.',
    'The sun was in my eyes at that angle.',
    'I was stuck in the intersection when it changed.',
    'I was not trying to blow the light.',
    'I tapped the brakes; the car behind was close.',
    'The stop line was faded.',
    'Construction made the lane markings unclear.',
    'I slowed for a pedestrian and the light changed.',
    'I swear it was still yellow on my side.',
    'Can you check a camera if there is one?',
    'I will fight this if I have to.',
    'I stopped before the crosswalk.',
    'I was turning and the arrow was odd.',
    'The intersection layout is a mess.',
    'I did not see the sign until late.',
    'I thought it was a flashing yellow.',
    'Nobody honked; I thought I was okay.',
    'I am telling you the truth about the light.',
    'I have never run a red on purpose.',
    'This feels really harsh for what happened.',
    'I was inching forward to see traffic.',
    'The sensor did not trip and I waited forever.',
    'I was behind a tall vehicle.',
    'I braked hard; the road was wet.',
  ]),
  mature: filterLines(
    matureFromBodies(
      [
        'That light was yellow and you know it.',
        'Camera racket, not justice.',
        'Red light scam city.',
        'I will fight this camera BS.',
        'You were waiting to burn people.',
        'Bullshit intersection trap.',
        'Fuck your red light ticket.',
      ],
      55
    )
  ),
};

pools.parking = {
  clean: filterLines([
    'I was only parked for two minutes.',
    'I was loading groceries.',
    'I was picking someone up at the curb.',
    'There were no other spaces.',
    'The sign was confusing from this angle.',
    'I thought loading was allowed here.',
    'The meter app failed on my phone.',
    'I paid; the receipt must have blown away.',
    'It was a rental; I did not know the rules.',
    'I ran inside for medicine.',
    'The hydrant was not visible under the snow.',
    'I moved as soon as you walked up.',
    'I was double-parked because traffic was blocked.',
    'The delivery truck forced everyone over.',
    'I thought Sunday parking was free.',
    'My hazard lights were on.',
    'I was in and out; the engine was running.',
    'The paint on the curb was worn off.',
    'I am sorry; I will move it right now.',
    'Can I get a warning? I never park here.',
    'The event lot was full.',
    'I was waiting for a disabled passenger.',
    'The GPS sent me to the wrong entrance.',
    'I did not see the no-parking zone.',
    'I was parallel parking and you caught mid-move.',
    'The valet was supposed to take it.',
    'I thought fifteen minutes was okay.',
    'I was helping someone with a flat tire.',
    'This ticket is more than my lunch money.',
    'I genuinely did not mean to block anything.',
  ]),
  mature: filterLines(
    matureFromBodies(
      [
        'Parking ticket for that is petty.',
        'You have nothing better to patrol.',
        'Meter maid energy.',
        'Fuck this parking fine.',
      ],
      50
    )
  ),
};

pools.dui = {
  clean: filterLines([
    'I only had one drink hours ago.',
    'I was not impaired; I felt completely fine.',
    'I think the machine gave a bad reading.',
    'I want a lawyer before I say more.',
    'I passed the walk-and-turn fine.',
    'I have acid reflux; that can affect tests.',
    'I used mouthwash right before I drove.',
    'I am on prescription meds; I followed the label.',
    'I was tired, not drunk.',
    'I blew under at another checkpoint once.',
    'I want a blood test.',
    'I was not slurring my words.',
    'I was cooperating with everything you asked.',
    'This is embarrassing; I am not a drinker.',
    'I had dinner; one glass of wine.',
    'I was the designated driver.',
    'I spit out the drink; I did not swallow.',
    'I need to call my attorney.',
    'I am not resisting; I just want counsel.',
    'I have medical paperwork if you need it.',
    'I was stressed; I sounded off.',
    'I have diabetes; symptoms can look weird.',
    'I was on cold medicine.',
    'I swear I was okay to drive.',
    'I pulled over immediately when you lit me up.',
    'I was not swerving on purpose.',
    'The road was bumpy.',
    'I am scared; this is a mistake.',
    'I will take any test you want with a lawyer.',
    'I respect the process; I want it done right.',
  ]),
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
  clean: filterLines([
    'That is not mine.',
    'Those are not mine.',
    'This is a prescription.',
    'That is legal CBD.',
    'Someone else left that in my car.',
    'I have no idea where that came from.',
    'My bag got mixed up at the gym.',
    'I want a lawyer right now.',
    'I am not answering questions without counsel.',
    'Prove that belonged to me.',
    'I never touched that.',
    'You searched without cause.',
    'I do not use that stuff.',
    'That could be anyone’s in a shared apartment.',
    'I gave someone a ride; it might be theirs.',
    'I want everything documented.',
    'I am scared and I want an attorney.',
    'I have never seen that before in my life.',
    'Check the label; it is hemp.',
    'My doctor prescribed that.',
    'That is my roommate’s, not mine.',
    'I am not signing anything.',
    'I will wait for my lawyer.',
    'Chain of custody matters here.',
    'I am telling the truth.',
    'I feel like I am being set up.',
    'I just got this car used.',
    'The previous owner could have left it.',
    'I want a witness.',
    'I am not saying another word.',
  ]),
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
  clean: filterLines([
    'He started it.',
    'She swung first.',
    'They were in my face.',
    'I was defending myself.',
    'It was an accident during a shove.',
    'I was trying to leave.',
    'I only pushed him away.',
    'She hit me and I reacted.',
    'There are witnesses who saw the whole thing.',
    'I have bruises too.',
    'I was scared for my safety.',
    'It was mutual; we were both yelling.',
    'I was breaking up a fight.',
    'I was holding someone back.',
    'The door hit someone; I did not punch anyone.',
    'I slipped and grabbed them.',
    'I want to file a counter-report.',
    'I have photos of my injuries.',
    'I was protecting my kids.',
    'I told them to stop ten times.',
    'I was trapped in the corner.',
    'I did not mean to hurt anyone.',
    'It was self-defense, plain and simple.',
    'They threatened me first.',
    'I want a lawyer before I explain more.',
    'The video will show what happened.',
    'I am not the aggressor here.',
    'I was walking away.',
    'They followed me outside.',
    'I am shaken up; this is crazy.',
  ]),
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
  clean: filterLines([
    'I paid for it.',
    'I have the receipt on my phone.',
    'It was a mistake at self-checkout.',
    'I thought it was already scanned.',
    'I was going to pay; I had not left the store.',
    'Security stopped me before I could explain.',
    'My kid put that in the cart.',
    'I put it in my pocket while I grabbed my wallet.',
    'The tag fell off.',
    'I was returning it to the shelf.',
    'I bought the same item yesterday.',
    'That is my property; I brought it in.',
    'I work here; I was moving stock.',
    'Wrong person; I look like someone else.',
    'Check the cameras; I did not conceal anything.',
    'I am willing to pay right now.',
    'It was an honest mistake.',
    'I was distracted on the phone.',
    'I did not see it in the cart.',
    'I am not a thief.',
    'I want a manager.',
    'I want a lawyer.',
    'This is humiliating.',
    'I have never stolen anything.',
    'Someone swapped baskets.',
    'I was comparing prices.',
    'The alarm went off on something I bought last week.',
    'I am cooperating; do not twist this.',
    'I need to call my spouse.',
    'Please just listen for one minute.',
  ]),
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
  clean: filterLines([
    'I need a lawyer right now.',
    'I want an attorney before I answer.',
    'I thought that case was already resolved.',
    'My lawyer told me that warrant was cleared.',
    'I am not resisting; I want counsel.',
    'I know my rights.',
    'I will come quietly; I want a phone call.',
    'There has to be a clerical error.',
    'I paid that fine years ago.',
    'I moved; I never got the court letter.',
    'I will turn myself in properly with my attorney.',
    'I am scared; please explain what this is for.',
    'I want to see paperwork.',
    'I am not saying anything incriminating.',
    'I will bond out and fix this.',
    'My name is common; wrong guy maybe?',
    'I just got off probation.',
    'I completed the classes they ordered.',
    'I have proof I showed up to court.',
    'I need to contact my family.',
    'I have medical needs; please note that.',
    'I will cooperate with booking.',
    'I want this on the record: I request counsel.',
    'I am not running.',
    'I did not know there was a warrant.',
    'Someone used my ID.',
    'I am innocent of what you think.',
    'Let me call my bondsman.',
    'I will follow every instruction.',
    'Please do not make this worse than it is.',
  ]),
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
  clean: filterLines([
    'I swerved to avoid debris in the lane.',
    'I merged fast because someone cut me off.',
    'I lost control for one second on wet pavement.',
    'I was cut off and had to brake hard.',
    'My tire blew; I was correcting.',
    'A dog ran into the street.',
    'I was avoiding a cyclist.',
    'The truck beside me drifted into my lane.',
    'I was not racing; I accelerated to merge.',
    'It looked worse than it was.',
    'I honked and they flipped me off; I reacted.',
    'I was late and I made a dumb lane change.',
    'I did not see the motorcycle.',
    'My mirror was fogged.',
    'I was unfamiliar with the rental.',
    'I am sorry; I will drive slower.',
    'I thought I had room.',
    'The GPS told me to turn at the last second.',
    'I was not showing off.',
    'I have never been cited for this before.',
    'I was trying to get out of a blind spot.',
    'Traffic was chaotic; I was threading through.',
    'I braked when I realized it was tight.',
    'I was not doing donuts or burnouts.',
    'I was following detour cones.',
    'Construction narrowed the lane suddenly.',
    'I was not trying to hurt anyone.',
    'I will take a defensive driving class.',
    'I respect that it looked bad from your angle.',
    'Can I explain what you did not see?',
  ]),
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
  clean: filterLines([
    'I was only moving the car in the parking lot.',
    'I unbuckled for one second to reach something.',
    'I forgot right after I started.',
    'The latch sticks; I was fixing it.',
    'The belt was behind the seat from yesterday.',
    'I had it on; it might not have clicked.',
    'I was pulling into my driveway.',
    'I was dropping someone off.',
    'The sensor is broken.',
    'I am sorry; I usually wear it.',
    'I was wearing it; it slipped under my arm.',
    'I was in reverse only.',
    'Can I get a warning?',
    'I know; it was dumb.',
    'I was adjusting the height.',
    'The clip is jammed with food crumbs.',
    'I just got out of a car wash.',
    'I was buckling up when you saw me.',
    'My jacket hid the strap.',
    'I was reaching for the clicker.',
    'I feel stupid; I normally always use it.',
    'The dog was on my lap; I was sorting it out.',
    'I was teaching a new driver.',
    'I had it off while looking at a map.',
    'I will click it right now.',
    'I did not mean to break the law.',
    'I respect seat belt laws.',
    'I was uncomfortable from sunburn.',
    'I was only going a block.',
    'I am not making excuses; I messed up.',
  ]),
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
  clean: filterLines([
    'I was stopped at a red light.',
    'I was on maps with voice directions.',
    'I was using speakerphone hands-free.',
    'I looked once at a text at a stop sign.',
    'The phone was in the mount.',
    'I was skipping a song.',
    'My kid handed me the phone.',
    'I was reporting a road hazard.',
    'I thought hands-free was allowed.',
    'I was not texting while moving.',
    'I was checking the time.',
    'The screen lit up; I was not reading.',
    'I was pulling over to answer.',
    'I was dialing 911 for someone else.',
    'I was not holding it to my ear.',
    'I set it down right after.',
    'I know; even a second is bad.',
    'I am sorry; I will put it away.',
    'I was looking for a parking spot on the app.',
    'Delivery apps ping constantly.',
    'I was declining a call.',
    'I was not scrolling social media.',
    'I will turn on Do Not Disturb driving mode.',
    'I thought Bluetooth counted.',
    'I was moving it off the seat.',
    'I was not watching a video.',
    'I glanced at navigation once.',
    'I will buy a proper mount today.',
    'I respect that you enforce this.',
    'I will pay more attention.',
  ]),
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
  clean: filterLines([
    'My registration renewal is in the mail.',
    'I renewed insurance yesterday; the card is printing.',
    'The paperwork is in the glove box somewhere.',
    'This is a rental; the papers are messy.',
    'I just bought the car; DMV is backed up.',
    'I thought I put the new card in here.',
    'My wallet was stolen; I am replacing IDs.',
    'The dealer was supposed to mail the plates.',
    'I have a photo of the policy on my phone.',
    'I switched companies; there is a gap day.',
    'The sticker fell off.',
    'I moved states; I was transferring registration.',
    'I am driving my mom’s car with permission.',
    'The fleet office handles our insurance.',
    'I can log in and show proof online.',
    'I forgot to swap the paper into this bag.',
    'I have a temporary permit in my email.',
    'The printer at work ran out of ink.',
    'I am on my way to the DMV this week.',
    'I paid; the card has not arrived.',
    'I thought digital proof was enough.',
    'The lienholder has the title.',
    'I am sorry; I will fix it today.',
    'This is embarrassing.',
    'I have never been pulled over for paperwork.',
    'Can I bring proof to the station?',
    'I will fax it over tonight.',
    'My spouse handles the insurance.',
    'The registration auto-renewed; I did not stick it.',
    'I just got the notice in the mail.',
  ]),
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
  clean: filterLines([
    'We were not fighting.',
    'The music was not that loud.',
    'The neighbor complains about everything.',
    'It was a private gathering.',
    'We were on our own property.',
    'The party ended at ten.',
    'Nobody called the police except him.',
    'We turned it down when you asked.',
    'Kids were playing; that was the noise.',
    'It was a game on TV.',
    'We were laughing; not arguing.',
    'The windows were closed.',
    'It was a one-time celebration.',
    'I have lived here for years without issues.',
    'He is the problem, not us.',
    'We invited him over; he got weird.',
    'Security already cleared us.',
    'The bar across the street is louder.',
    'We were packing up when you arrived.',
    'I am sorry if it carried.',
    'It was a birthday; we got excited.',
    'No one was hurt.',
    'We were on the phone with family.',
    'The dog barked once.',
    'We were moving furniture.',
    'It sounded worse from outside.',
    'I will keep it down from now on.',
    'We respect the neighborhood.',
    'Can we just talk this out?',
    'I did not mean to disturb anyone.',
  ]),
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
  clean: filterLines([
    'My headlight burned out; I just noticed.',
    'I already ordered the replacement part.',
    'I have an appointment at the shop tomorrow.',
    'The mechanic has been waiting on a back-ordered bulb.',
    'The lens was cracked in a parking lot bump.',
    'I did not realize the taillight was out.',
    'The fuse keeps popping; I am troubleshooting.',
    'I was driving to get it fixed when you stopped me.',
    'The wiring is flaky; I am saving for the repair.',
    'That tire is temporary until payday.',
    'The muffler rusted through last week.',
    'My wipers failed in the rain; I was headed to AutoZone.',
    'The window tint was on the car when I bought it.',
    'The trailer wiring adapter is loose.',
    'The brake light works when I jiggle the bulb.',
    'I have the receipt for the new parts in the trunk.',
    'The truck is not mine; it belongs to my boss.',
    'I thought the reflector was enough until dark.',
    'I know it looks bad; I am fixing it.',
    'Can I get a fix-it ticket instead?',
    'The inspection is next month; I was lining everything up.',
    'A rock cracked the housing.',
    'The aftermarket lights are finicky.',
    'I just came from the dealer; they missed it.',
    'The car sat for months; the battery killed the electronics.',
    'I am broke; I am doing what I can.',
    'I swear I was not ignoring it on purpose.',
    'The warning light only started today.',
    'This is a work vehicle; maintenance is on the company.',
    'I will get it roadworthy as soon as possible.',
  ]),
  mature: filterLines(
    matureFromBodies(
      [
        'Equipment ticket, really?',
        'Fix-it ticket culture.',
        'It is a bulb, not a crime.',
        'Unroadworthy is harsh; I am fixing it.',
        'Write your ticket; I am still replacing the part.',
      ],
      35
    )
  ),
};

pools.pedestrian = {
  clean: filterLines([
    'I crossed with the crowd.',
    'I thought the crosswalk was clear.',
    'The walk signal was flashing.',
    'I was jaywalking on an empty street.',
    'Nobody was coming for blocks.',
    'I was late for the bus.',
    'The crosswalk button was broken.',
    'I was in the crosswalk when it changed.',
    'The driver waved me through.',
    'I was following someone else.',
    'I did not see the sign.',
    'The sidewalk was closed.',
    'I stepped out to see around a van.',
    'I was helping someone with a stroller.',
    'I am sorry; I will use the crosswalk.',
    'I thought this block allowed mid-block crossing.',
    'It was pouring rain; I took a shortcut.',
    'I was not trying to cause traffic.',
    'I stopped in the median.',
    'I looked both ways twice.',
    'The light was stuck on don’t walk forever.',
    'I am not from here.',
    'I was lost and confused.',
    'Kids ran ahead; I chased them.',
    'I was on the phone and not thinking.',
    'I will wait next time.',
    'I respect pedestrian laws.',
    'I feel stupid.',
    'Can I get a warning?',
    'Nobody honked until you showed up.',
  ]),
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
  clean: filterLines([
    'I did not see any signs.',
    'I thought it was public property.',
    'We were looking for an address.',
    'I cut through once to save time.',
    'The gate was open.',
    'I was picking someone up.',
    'I used to live here years ago.',
    'The map sent me this way.',
    'I was turning around.',
    'I was dropping off a package.',
    'The event staff pointed us through.',
    'I did not mean to go past the line.',
    'I was taking a photo of the view.',
    'I am sorry; I will leave right now.',
    'I thought the trail connected.',
    'We were lost hiking.',
    'The fence had a gap.',
    'I was meeting a friend who said it was okay.',
    'I did not break anything.',
    'I was not trying to steal or vandalize.',
    'I will never come back.',
    'Please just let me go.',
    'I did not know it was private.',
    'The lighting was bad; I misread the sign.',
    'I was helping a stranded motorist.',
    'I am embarrassed.',
    'I respect if I need to go.',
    'Can I speak to the owner?',
    'I have permission; they are just not answering.',
    'I was one step over the line.',
  ]),
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
  clean: filterLines([
    'Oh, you caught the real menace to society.',
    'Wow, public safety is saved.',
    'Amazing hero work today.',
    'Great, my taxes hard at work.',
    'Cool, crime must be at zero now.',
    'Fantastic, another ticket solves everything.',
    'Bravo, truly impressive police work.',
    'Wonderful, paperwork fixes the world.',
    'Nice, you must feel really important.',
    'Super, this is exactly what we needed.',
    'Incredible, I feel so much safer.',
    'Lovely, another fine for breathing wrong.',
    'Perfect timing, I love surprises.',
    'Awesome, my budget appreciates it.',
    'Sweet, just what my Monday needed.',
    'Neat, you really nailed the big stuff.',
    'Good job, dangerous criminal off the streets.',
    'Solid, I am sure the chief is proud.',
    'Impressive, you should get a medal.',
    'Stunning work, truly breathtaking.',
    'Remarkable, I am in awe.',
    'Astounding, what a win for justice.',
    'Phenomenal, the city is healed now.',
    'Outstanding, I will tell everyone.',
    'Marvelous, my faith is restored.',
    'Spectacular, I did not know laws could bend like that.',
    'Wonderful, I am glowing with civic pride.',
    'Charming, you have a real gift for this.',
    'Delightful, I will frame the citation.',
    'Enchanting, I am spellbound by your dedication.',
  ]),
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
  clean: filterLines([
    'Fine, write it.',
    'Whatever, I will pay it.',
    'It is not worth the fight.',
    'You ruined my day.',
    'There goes my budget.',
    'I am too tired to argue.',
    'I give up.',
    'Just do what you are going to do.',
    'I do not have the energy for this.',
    'Okay, I hear you.',
    'I am not happy, but fine.',
    'I will take the points.',
    'I will deal with the DMV.',
    'I guess this is happening.',
    'I am numb to it at this point.',
    'Another bill. Great.',
    'I expected better, but okay.',
    'I am not going to cry in front of you.',
    'I will sign.',
    'I will show up if I have to.',
    'I hope you sleep fine tonight.',
    'I am walking away before I say something worse.',
    'I am done talking.',
    'I will vent to my friends later.',
    'I cannot win this one.',
    'I am not going to beg.',
    'I will eat ramen for a month.',
    'Thanks for nothing, I guess.',
    'I am logging this mentally.',
    'I am leaving.',
  ]),
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
  {
    keywords: [
      'headlight',
      'taillight',
      'tail light',
      'brake light',
      'equipment',
      'unroadworthy',
      'roadworthy',
      'defective',
      'windshield',
      'muffler',
      'wiper',
      'tint',
      'horn',
      'mirror',
      'exhaust',
    ],
    pool: 'equipment',
  },
  { keywords: ['jaywalk', 'pedestrian', 'crosswalk', 'sidewalk'], pool: 'pedestrian' },
  { keywords: ['trespass'], pool: 'trespass' },
  { keywords: ['speed trap', 'ticket quota', 'revenue trap', 'quota trap'], pool: 'cop_hostile' },
];

for (const k of Object.keys(pools)) {
  pools[k].clean = filterLines(pools[k].clean);
  pools[k].mature = filterLines(pools[k].mature);
}

const doc = { version: PED_REACTIONS_DATA_VERSION, rules, pools };
fs.mkdirSync(path.dirname(outPath), { recursive: true });
fs.writeFileSync(outPath, JSON.stringify(doc, null, 2), 'utf8');

let total = 0;
for (const k of Object.keys(pools)) {
  total += (pools[k].clean?.length || 0) + (pools[k].mature?.length || 0);
}
console.log(`Wrote ${outPath} — ${Object.keys(pools).length} pools, ${total} lines, ${rules.length} rules.`);
