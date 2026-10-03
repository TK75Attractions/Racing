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
