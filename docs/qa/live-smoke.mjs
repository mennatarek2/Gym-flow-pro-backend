/**
 * Live local smoke for QA-02 / QA-03 / QA-05 against https://localhost:5001
 * Usage: node docs/qa/live-smoke.mjs
 * Requires: API on launch-profile https; owner@gymflow.test / Test@1234 / GYM-TEST-01
 */
import fs from 'fs';
import https from 'https';
import { fileURLToPath } from 'url';
import path from 'path';

const BASE = process.env.GFP_API_BASE || 'https://localhost:5001/api';
const agent = new https.Agent({ rejectUnauthorized: false });
const __dirname = path.dirname(fileURLToPath(import.meta.url));

async function req(method, urlPath, body, token) {
  const url = new URL(urlPath.startsWith('http') ? urlPath : BASE + urlPath);
  const payload = body ? JSON.stringify(body) : null;
  const headers = { Accept: 'application/json' };
  if (payload) {
    headers['Content-Type'] = 'application/json';
    headers['Content-Length'] = Buffer.byteLength(payload);
  }
  if (token) headers.Authorization = `Bearer ${token}`;
  return new Promise((resolve, reject) => {
    const r = https.request(url, { method, headers, agent }, (res) => {
      let data = '';
      res.on('data', (c) => (data += c));
      res.on('end', () => {
        let json = null;
        try { json = data ? JSON.parse(data) : null; } catch { json = data; }
        resolve({ status: res.statusCode, json, raw: data });
      });
    });
    r.on('error', reject);
    if (payload) r.write(payload);
    r.end();
  });
}

function asArray(json) {
  if (Array.isArray(json)) return json;
  if (json && Array.isArray(json.items)) return json.items;
  if (json && Array.isArray(json.value)) return json.value;
  if (json && json.data && Array.isArray(json.data.items)) return json.data.items;
  if (json && json.data && Array.isArray(json.data.sales)) return json.data.sales;
  if (json && Array.isArray(json.sales)) return json.sales;
  return [];
}

const login = await req('POST', '/auth/login', {
  email: 'owner@gymflow.test',
  password: 'Test@1234',
  gymCode: 'GYM-TEST-01',
});
if (login.status !== 200 || !login.json?.accessToken) {
  console.error('LOGIN_FAIL', login.status, login.raw?.slice?.(0, 200));
  process.exit(1);
}
const token = login.json.accessToken;
console.log('ok — login');

const mA = '08AA6BA0-A627-478C-AD89-28A66F78E14E';
const mB = 'D9371482-12E0-4F30-8F7B-3848790CCDF5';
const inbox = asArray((await req('GET', '/member-orders?page=1&pageSize=100', null, token)).json);
const fa = asArray((await req('GET', `/member-orders?memberId=${mA}&page=1&pageSize=100`, null, token)).json);
const fb = asArray((await req('GET', `/member-orders?memberId=${mB}&page=1&pageSize=100`, null, token)).json);
const foreignA = fa.filter((o) => String(o.memberId).toLowerCase() !== mA.toLowerCase()).length;
const foreignB = fb.filter((o) => String(o.memberId).toLowerCase() !== mB.toLowerCase()).length;
const qa02 =
  inbox.length >= 2 &&
  fa.length > 0 &&
  fb.length > 0 &&
  foreignA === 0 &&
  foreignB === 0 &&
  fa.length + fb.length === inbox.length &&
  fa.length < inbox.length;
console.log(
  qa02 ? 'ok' : 'FAIL',
  `— QA-02 inbox=${inbox.length} A=${fa.length} B=${fb.length} foreignA=${foreignA} foreignB=${foreignB}`,
);

const debtor = 'F96AEF70-78E9-42C5-A9A3-A0F758B32F6E';
const salesRes = await req('GET', `/debtors/${debtor}/sales`, null, token);
const sales = asArray(salesRes.json);
const qa03 = salesRes.status === 200 && sales.length > 0 && sales.every((s) => Number(s.amountDue) > 0);
console.log(qa03 ? 'ok' : 'FAIL', `— QA-03 outstanding sales http=${salesRes.status} count=${sales.length}`);

const finRes = await req('GET', '/dashboard/overview?period=this_month', null, token);
const fin = (finRes.json && finRes.json.financial) || {};
const qa05 =
  finRes.status === 200 &&
  fin.calculationVersion === 'financial-v1' &&
  fin.operatingExpenses != null &&
  fin.payrollExpense != null &&
  Number(fin.operatingExpenses) !== Number(fin.payrollExpense); // payroll not folded into OpEx as the only number
console.log(
  qa05 ? 'ok' : 'FAIL',
  `— QA-05 version=${fin.calculationVersion} opex=${fin.operatingExpenses} payroll=${fin.payrollExpense}`,
);

const evidence = {
  at: new Date().toISOString(),
  gym: 'GYM-TEST-01',
  qa02: { pass: qa02, inbox: inbox.length, A: fa.length, B: fb.length, foreignA, foreignB },
  qa03: { pass: qa03, debtor, sales: sales.length },
  qa05: {
    pass: qa05,
    calculationVersion: fin.calculationVersion,
    operatingExpenses: fin.operatingExpenses,
    payrollExpense: fin.payrollExpense,
  },
};
fs.writeFileSync(path.join(__dirname, 'LIVE_SMOKE_EVIDENCE.json'), JSON.stringify(evidence, null, 2));
if (!qa02 || !qa03 || !qa05) process.exit(2);
console.log('\nAll live smoke checks passed.');
