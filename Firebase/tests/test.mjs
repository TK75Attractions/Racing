import { initializeTestEnvironment, assertSucceeds, assertFails } from '@firebase/rules-unit-testing';
import { readFileSync } from 'node:fs';
const ref = (database, path) => database.ref(path);
const get = (reference) => reference.get();
const set = (reference, value) => reference.set(value);
const serverTimestamp = () => ({ '.sv': 'timestamp' });

const projectId = 'demo-racing-remote';
const env = await initializeTestEnvironment({
  projectId,
  database: { host: '127.0.0.1', port: 9010,
    rules: readFileSync(new URL('../database.rules.json', import.meta.url), 'utf8') }
});
const writer = env.authenticatedContext('writer').database();
const viewer = env.authenticatedContext('viewer').database();
const operator = env.authenticatedContext('operator').database();
const anonymous = env.unauthenticatedContext().database();
const live = {
  schemaVersion: 1, sessionId: '0123456789abcdef0123456789abcdef', seq: 8,
  updatedAt: Date.now(), state: 'Title', raceTime: 0, countdown: 0,
  goalLap: 3, inputMode: 'Keyboard',
  players: [
    {number:1, connected:true, lap:0, speed:0, pedal:0, steering:0},
    {number:2, connected:true, lap:0, speed:0, pedal:0, steering:0}
  ],
  serial: {portOpen:false, port:'-', received:0, processed:0, errors:0,
    latestSerialId:0, lastResult:'Waiting'}
};
const request = (id, overrides={}) => ({ id, action:'start', targetSession:live.sessionId,
  targetSeq:8, expectedState:'Title', createdAt:serverTimestamp(), ...overrides });
try {
  await env.withSecurityRulesDisabled(async (context) => {
    const database = context.database();
    await set(ref(database, 'roles'), {writerUid:'writer', viewers:{viewer:true,operator:true}, operators:{operator:true}});
    await set(ref(database, 'live'), live);
  });
  await assertSucceeds(get(ref(viewer, 'live')));
  await assertFails(get(ref(anonymous, 'live')));
  await assertFails(get(ref(writer, 'live')));
  await assertSucceeds(set(ref(writer, 'live'), {...live, updatedAt:serverTimestamp()}));
  await assertFails(set(ref(operator, 'live'), {...live, updatedAt:serverTimestamp()}));
  const participants = Array.from({length:4}, (_, index) => ({
    number:index+1, isCpu:index>=2, position:index+1, lap:1,
    speed:72.4, progress:123.5+index*10, status:index===2?'Airborne':'Driving',
    rivalNumber:index>=2?index-1:0, gap:index>=2?-20:0, targetSpeed:index>=2?90:0,
    recoveries:index>=2?2:0, contactGrace:index===3
  }));
  const npcLive = {...live, participantCount:4, participants, updatedAt:serverTimestamp()};
  await assertSucceeds(set(ref(writer, 'live'), npcLive));
  if (process.env.NPC_DASHBOARD_FIXTURE) {
    const actualJson = readFileSync(process.env.NPC_DASHBOARD_FIXTURE, 'utf8');
    if (Buffer.byteLength(actualJson) > 4096) throw new Error('Unity snapshot exceeded 4 KiB');
    await assertSucceeds(set(ref(writer, 'live'), JSON.parse(actualJson)));
  }
  await assertSucceeds(get(ref(viewer, 'live/participants')));
  await assertFails(get(ref(anonymous, 'live/participants')));
  await assertFails(set(ref(viewer, 'live/participants'), participants));
  const invalidParticipant = (overrides) => ({...npcLive,
    participants:participants.map((entry, index)=>index===2?{...entry,...overrides}:entry)});
  for (const change of [{number:2}, {isCpu:false}, {position:5}, {rivalNumber:3},
    {recoveries:-1}, {status:'Warping'}, {speed:1001}, {contactGrace:'true'}, {camera:'CPU'}]) {
    await assertFails(set(ref(writer, 'live'), invalidParticipant(change)));
  }
  await assertFails(set(ref(writer, 'live'), {...npcLive, participantCount:2}));
  await assertFails(set(ref(writer, 'live'), {...npcLive, participants:[...participants, {...participants[3],number:5}]}));
  await assertSucceeds(set(ref(writer, 'live'), {...npcLive, participantCount:2, participants:participants.slice(0,2)}));
  for (const state of ['CarSelection','Tutorial','Countdown','Result']) {
    await assertSucceeds(set(ref(writer, 'live'), {...live, state, participantCount:0, updatedAt:serverTimestamp()}));
  }
  await assertSucceeds(set(ref(writer, 'live'), {...npcLive, participants:participants.map(entry=>({...entry,status:entry.isCpu?'DNF':'Finished'}))}));
  // Old Unity snapshots remain valid while the site/rules roll out first.
  await assertSucceeds(set(ref(writer, 'live'), {...live, updatedAt:serverTimestamp()}));
  await assertSucceeds(get(ref(operator, 'roles/operators/operator')));
  await assertFails(get(ref(viewer, 'roles/operators/operator')));
  await assertFails(set(ref(viewer, 'control/request'), request('aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa')));
  await assertFails(set(ref(writer, 'control/request'), request('aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa')));
  await assertFails(set(ref(operator, 'control/request'), request('aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa', {targetSession:'ffffffffffffffffffffffffffffffff'})));
  await assertSucceeds(set(ref(operator, 'control/request'), request('aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa')));
  await assertFails(set(ref(operator, 'control/request'), request('bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb')));
  await assertSucceeds(get(ref(writer, 'control/request')));
  await assertFails(set(ref(operator, 'control/ack'), {id:'aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa', result:'applied', state:'Countdown', completedAt:serverTimestamp()}));
  await assertFails(set(ref(writer, 'control/ack'), {id:'bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb', result:'applied', state:'Countdown', completedAt:serverTimestamp()}));
  await assertSucceeds(set(ref(writer, 'control/ack'), {id:'aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa', result:'applied', state:'Countdown', completedAt:serverTimestamp()}));
  await assertSucceeds(get(ref(operator, 'control/ack')));
  await assertFails(get(ref(viewer, 'control/ack')));
  await assertSucceeds(set(ref(operator, 'control/request'), request('bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb')));
  await assertFails(set(ref(operator, 'control/request'), request('cccccccccccccccccccccccccccccccc', {action:'retry'})));
  await env.withSecurityRulesDisabled(async (context) => {
    const database = context.database();
    await set(ref(database, 'live/updatedAt'), Date.now()-30000);
    await set(ref(database, 'control/ack/id'), 'bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb');
  });
  await assertFails(set(ref(operator, 'control/request'), request('dddddddddddddddddddddddddddddddd')));
  console.log('Firebase remote control rules: all permission and state checks passed');
} finally {
  await env.cleanup();
}
