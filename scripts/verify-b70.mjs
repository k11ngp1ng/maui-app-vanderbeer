// Integration check against a disposable VanderBeer API/MySQL database.
// Usage: node scripts/verify-b70.mjs [apiBaseUrl] [responseLossProxyBaseUrl]
import { randomUUID } from 'node:crypto';
import assert from 'node:assert/strict';

const api = process.argv[2] ?? 'http://127.0.0.1:5080';
const proxy = process.argv[3] ?? 'http://127.0.0.1:5081';

async function call(base, method, path, body) {
  const response = await fetch(new URL(path, base), {
    method, headers: body ? { 'content-type': 'application/json' } : {},
    body: body ? JSON.stringify(body) : undefined,
  });
  return { status: response.status, replay: response.headers.get('idempotent-replay'),
    body: await response.json() };
}

function cpf() {
  const digits = Array.from({ length: 9 }, () => Math.floor(Math.random() * 10));
  if (digits.every(x => x === digits[0])) digits[0] = (digits[0] + 1) % 10;
  for (let size = 9; size <= 10; size++) {
    const sum = digits.slice(0, size).reduce((value, digit, index) => value + digit * (size + 1 - index), 0);
    digits.push((sum * 10) % 11 % 10);
  }
  return digits.join('');
}

const settings = await call(api, 'GET', '/api/v1/store-settings');
assert.equal(settings.status, 200);
const customer = await call(api, 'POST', '/api/v1/customers', {
  clientRequestId: randomUUID(), customerType: 'Individual', fullName: 'Cliente Fictício B70',
  companyName: null, cpf: cpf(), cnpj: null, phone: '11999999999', email: null, address: null,
});
assert.equal(customer.status, 201, JSON.stringify(customer.body));
const order = await call(api, 'POST', '/api/v1/orders', {
  clientRequestId: randomUUID(), customerId: customer.body.id,
  expectedCustomerVersion: customer.body.version, fulfillmentType: 'Pickup',
  requestedDate: settings.body.minRequestedDate,
  paymentMethodId: '00000000-0000-4000-8000-000000000004', cashTendered: 50,
  deliveryAddress: null, notes: 'Pedido fictício de verificação B70',
  items: [{ productId: '00000000-0000-4000-8000-000000000003', quantity: 2,
    expectedUnitPrice: 10 }],
});
assert.equal(order.status, 201, JSON.stringify(order.body));
const id = order.body.id;
const companyPath = `/api/v1/admin/orders/${id}`;
const list = await call(api, 'GET', '/api/v1/admin/orders?status=Received&page=1&pageSize=20');
assert.equal(list.status, 200);
assert.ok(list.body.items.some(item => item.id === id));
const detail = await call(api, 'GET', companyPath);
assert.equal(detail.status, 200);
assert.equal(detail.body.status, 'Received');

const invalid = await call(api, 'PATCH', companyPath + '/status', {
  clientRequestId: randomUUID(), expectedVersion: 1, status: 'Delivered', reason: null,
});
assert.equal(invalid.status, 409);
const stale = await call(api, 'PATCH', companyPath + '/status', {
  clientRequestId: randomUUID(), expectedVersion: 9, status: 'Confirmed', reason: null,
});
assert.equal(stale.status, 409);

const confirmed = { clientRequestId: randomUUID(), expectedVersion: 1,
  status: 'Confirmed', reason: null };
const simultaneous = await Promise.all(Array.from({ length: 2 }, () =>
  call(api, 'PATCH', companyPath + '/status', confirmed)));
assert.deepEqual(simultaneous.map(x => x.status).sort(), [200, 200]);
assert.deepEqual(simultaneous.map(x => x.replay).sort(), ['false', 'true']);
assert.equal(simultaneous[0].body.id, simultaneous[1].body.id);
assert.equal(simultaneous[0].body.version, 2);
const replay = await call(api, 'PATCH', companyPath + '/status', confirmed);
assert.equal(replay.status, 200);
assert.equal(replay.replay, 'true');

const preparing = { clientRequestId: randomUUID(), expectedVersion: 2,
  status: 'Preparing', reason: null };
let lost = false;
try { await call(proxy, 'PATCH', companyPath + '/status', preparing); }
catch { lost = true; }
assert.equal(lost, true, 'O proxy deveria descartar a primeira resposta PATCH');
const recovered = await call(api, 'PATCH', companyPath + '/status', preparing);
assert.equal(recovered.status, 200);
assert.equal(recovered.replay, 'true');
assert.equal(recovered.body.version, 3);

const current = await call(api, 'GET', companyPath);
const customerHistory = await call(api, 'GET',
  `/api/v1/customers/${customer.body.id}/orders?page=1&pageSize=20`);
assert.equal(current.body.status, 'Preparing');
assert.equal(current.body.statusHistory.length, 3);
assert.equal(customerHistory.status, 200);
assert.equal(customerHistory.body.items.find(x => x.id === id)?.status, 'Preparing');
console.log(JSON.stringify({ orderId: id, protocol: order.body.protocol,
  invalidTransition: invalid.status, staleVersion: stale.status,
  concurrent: simultaneous.map(x => x.replay), lostResponseRecovered: recovered.replay,
  version: current.body.version, events: current.body.statusHistory.length,
  customerHistoryStatus: 'Preparing' }));
