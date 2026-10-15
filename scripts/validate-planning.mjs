// Validação documental específica do contrato VanderBeer. Não substitui testes da API.
import fs from 'node:fs';
import path from 'node:path';
import assert from 'node:assert/strict';
import { fileURLToPath } from 'node:url';

const root = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '..');
const read = p => fs.readFileSync(path.join(root, p), 'utf8').replace(/^\uFEFF/, '');
const api = JSON.parse(read('docs/api/openapi.json'));
const fixtures = JSON.parse(read('docs/api/examples.json'));
const failures = [];
const check = (condition, message) => { if (!condition) failures.push(message); };
function resolveRef(ref) {
  assert(ref.startsWith('#/'), 'Somente referências internas são esperadas: ' + ref);
  let value = api;
  for (const part of ref.slice(2).split('/')) value = value?.[part.replace(/~1/g, '/').replace(/~0/g, '~')];
  assert(value !== undefined, 'Referência inexistente: ' + ref);
  return value;
}
let refs = 0;
function walk(value, visit) {
  if (value && typeof value === 'object') {
    visit(value);
    for (const child of Object.values(value)) walk(child, visit);
  }
}
walk(api, node => { if (node.$ref) { resolveRef(node.$ref); refs++; } });

const supported = new Set(['$ref', 'type', 'format', 'properties', 'required', 'additionalProperties',
  'items', 'minItems', 'maxItems', 'minLength', 'maxLength', 'minimum', 'maximum',
  'multipleOf', 'pattern', 'enum', 'const', 'anyOf', 'description', 'default']);
function auditSchema(schema) {
  for (const key of Object.keys(schema)) assert(supported.has(key), 'Keyword não suportada pelo verificador: ' + key);
  if (schema.$ref) assert(Object.keys(schema).length === 1, 'Não ignorar siblings de $ref.');
  for (const child of Object.values(schema.properties ?? {})) auditSchema(child);
  if (schema.items) auditSchema(schema.items);
  for (const child of schema.anyOf ?? []) auditSchema(child);
  for (const field of schema.required ?? []) assert(schema.properties?.[field], 'Campo required ausente: ' + field);
}
Object.values(api.components.schemas).forEach(auditSchema);
const formats = {
  uuid: v => /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i.test(v),
  date: v => /^\d{4}-\d{2}-\d{2}$/.test(v) && Number.isFinite(Date.parse(v)) && new Date(v).toISOString().slice(0, 10) === v,
  'date-time': v => /^\d{4}-\d{2}-\d{2}T\d{2}:\d{2}:\d{2}(?:\.\d+)?Z$/.test(v) && Number.isFinite(Date.parse(v)),
  email: v => /^[^\s@]+@[^\s@]+\.[^\s@]+$/.test(v),
  uri: v => { try { return Boolean(new URL(v).protocol); } catch { return false; } }
};
function validate(schema, value, location = '$') {
  if (schema.$ref) return validate(resolveRef(schema.$ref), value, location);
  if (schema.anyOf) {
    return schema.anyOf.some(s => validate(s, value, location).length === 0) ? [] : [location + ': nenhum anyOf compatível'];
  }
  const errors = [];
  const error = reason => errors.push(location + ': ' + reason);
  const matches = {
    null: value === null, string: typeof value === 'string',
    boolean: typeof value === 'boolean', number: typeof value === 'number' && Number.isFinite(value),
    integer: Number.isInteger(value), array: Array.isArray(value),
    object: value !== null && typeof value === 'object' && !Array.isArray(value)
  };
  if (schema.type && !matches[schema.type]) return [location + ': tipo ' + schema.type + ' esperado'];
  if ('const' in schema && value !== schema.const) error('const');
  if (schema.enum && !schema.enum.includes(value)) error('enum');
  if (typeof value === 'string') {
    const length = [...value].length;
    if (schema.minLength !== undefined && length < schema.minLength) error('minLength');
    if (schema.maxLength !== undefined && length > schema.maxLength) error('maxLength');
    if (schema.pattern && !new RegExp(schema.pattern, 'u').test(value)) error('pattern');
    if (schema.format) {
      assert(formats[schema.format], 'Formato sem validador: ' + schema.format);
      if (!formats[schema.format](value)) error('format ' + schema.format);
    }
  }
  if (typeof value === 'number') {
    if (schema.minimum !== undefined && value < schema.minimum) error('minimum');
    if (schema.maximum !== undefined && value > schema.maximum) error('maximum');
    if (schema.multipleOf !== undefined && Math.abs(value / schema.multipleOf - Math.round(value / schema.multipleOf)) > 1e-6) error('multipleOf');
  }
  if (Array.isArray(value)) {
    if (schema.minItems !== undefined && value.length < schema.minItems) error('minItems');
    if (schema.maxItems !== undefined && value.length > schema.maxItems) error('maxItems');
    if (schema.items) value.forEach((item, i) => errors.push(...validate(schema.items, item, location + '[' + i + ']')));
  }
  if (matches.object) {
    for (const key of schema.required ?? []) if (!(key in value)) error('required ' + key);
    for (const [key, item] of Object.entries(value)) {
      if (schema.properties?.[key]) errors.push(...validate(schema.properties[key], item, location + '.' + key));
      else if (schema.additionalProperties === false) error('propriedade desconhecida ' + key);
    }
  }
  return errors;
}
let mediaExamples = 0;
function validateMedia(content, label, status = null) {
  for (const media of Object.values(content ?? {})) {
    auditSchema(media.schema);
    for (const [name, example] of Object.entries(media.examples ?? {})) {
      const errors = validate(media.schema, example.value);
      check(errors.length === 0, label + '/' + name + ': ' + errors.join('; '));
      if (status && example.value?.status !== undefined && typeof example.value.status === 'number') {
        check(example.value.status === Number(status), label + ': status do Problem diverge do HTTP');
      }
      mediaExamples++;
    }
  }
}
const operationIds = new Set();
let commands = 0;
for (const [route, methods] of Object.entries(api.paths)) {
  for (const [method, op] of Object.entries(methods)) {
    check(!operationIds.has(op.operationId), 'operationId duplicado: ' + op.operationId);
    operationIds.add(op.operationId);
    const placeholders = [...route.matchAll(/\{([^}]+)\}/g)].map(m => m[1]);
    for (const name of placeholders) {
      check(op.parameters.some(p => p.name === name && p.in === 'path' && p.required), 'Parâmetro ausente: ' + route + '/' + name);
    }
    for (const parameter of op.parameters) {
      auditSchema(parameter.schema);
      if (parameter.in === 'path') check(placeholders.includes(parameter.name), 'Parâmetro de rota excedente: ' + parameter.name);
    }
    if (['post', 'put', 'patch'].includes(method)) {
      commands++;
      check(Boolean(op.requestBody?.required), 'Comando sem corpo obrigatório: ' + route);
      const schema = resolveRef(op.requestBody.content['application/json'].schema.$ref);
      check(schema.required.includes('clientRequestId'), 'Comando sem chave de tentativa: ' + route);
      if (method !== 'post') check(schema.required.includes('expectedVersion'), 'Edição sem versão: ' + route);
      validateMedia(op.requestBody.content, op.operationId + '/request');
    }
    for (const [status, responseRef] of Object.entries(op.responses)) {
      const response = responseRef.$ref ? resolveRef(responseRef.$ref) : responseRef;
      validateMedia(response.content, op.operationId + '/' + status, status);
    }
    if (method === 'post') {
      check(Boolean(op.responses['201'] && op.responses['200']), 'Criação sem resposta de replay: ' + route);
      const first = op.responses['201'].content['application/json'].examples.demo.value;
      const replay = op.responses['200'].content['application/json'].examples.demo.value;
      check(JSON.stringify(first) === JSON.stringify(replay), 'Replay difere da criação: ' + route);
    }
  }
}
for (const fixture of fixtures.cases) {
  const schema = fixture.schema ? api.components.schemas[fixture.schema] : fixture.inlineSchema;
  assert(schema, 'Schema de fixture ausente: ' + fixture.name);
  auditSchema(schema);
  const errors = validate(schema, fixture.value);
  check((errors.length === 0) === fixture.valid, fixture.name + ': expectativa não atendida: ' + errors.join('; '));
}
const createdOrder = api.paths['/api/v1/orders'].post.responses['201'].content['application/json'].examples.demo.value;
const cents = n => Math.round(n * 100);
const sum = createdOrder.items.reduce((total, item) => {
  check(cents(item.subtotal) === item.quantity * cents(item.unitPrice), 'Subtotal incorreto no exemplo');
  return total + cents(item.subtotal);
}, 0);
check(cents(createdOrder.total) === sum + cents(createdOrder.deliveryFee), 'Total incorreto no exemplo');
check(cents(createdOrder.changeDue) === cents(createdOrder.cashTendered) - cents(createdOrder.total), 'Troco incorreto no exemplo');
const changed = fixtures.cases.find(c => c.name === 'price-changed').value;
const orderRequest = fixtures.cases.find(c => c.name === 'CreateOrder-request').value;
const repriced = orderRequest.items.reduce((total, item) => total + item.quantity * cents(changed.priceChanges.find(c => c.productId === item.productId)?.currentUnitPrice ?? item.expectedUnitPrice), 0);
check(repriced === cents(changed.currentTotal), 'Total após mudança de preço incorreto');
const settings = fixtures.cases.find(c => c.name === 'GetStoreSettings-response').value;
check((Date.parse(settings.maxRequestedDate) - Date.parse(settings.minRequestedDate)) / 86400000 === 30, 'Janela de datas dos exemplos incorreta');
check(orderRequest.requestedDate >= settings.minRequestedDate && orderRequest.requestedDate <= settings.maxRequestedDate, 'Data de exemplo fora da janela');

function filesIn(dir) {
  return fs.readdirSync(path.join(root, dir), { withFileTypes: true }).flatMap(entry => {
    const relative = path.join(dir, entry.name);
    return entry.isDirectory() ? filesIn(relative) : entry.name.endsWith('.md') ? [relative] : [];
  });
}
const fullPlanning = fs.existsSync(path.join(root, 'docs/requisitos.md'));
const docs = fullPlanning ? ['README.md', 'AGENTS.md', ...['docs', 'rules', 'workflows'].flatMap(filesIn)] : ['README.md', 'docs/backlog.md'].filter(file => fs.existsSync(path.join(root, file)));
for (const file of docs) {
  const body = read(file);
  for (const match of body.matchAll(/\[[^\]]*\]\(([^)]+)\)/g)) {
    const target = match[1];
    if (/^(https?:\/\/|#)/.test(target)) continue;
    const resolved = path.resolve(root, path.dirname(file), target.split('#')[0]);
    check(fs.existsSync(resolved), 'Link local ausente: ' + file + ' -> ' + target);
  }
}
const reqIds = fullPlanning ? [...read('docs/requisitos.md').matchAll(/^\| (RF\d+) \|/gm)].map(m => m[1]) : [];
const tasks = [...read('docs/backlog.md').matchAll(/^\| (B\d+) \|.*$/gm)];
const taskIds = tasks.map(m => m[1]);
check(new Set(reqIds).size === reqIds.length, 'RF duplicado');
check(new Set(taskIds).size === taskIds.length, 'Tarefa duplicada');
const graph = new Map(tasks.map(m => [m[1], [...m[0].split('|')[3].matchAll(/\bB\d+\b/g)].map(x => x[0])]));
for (const [task, deps] of graph) for (const dep of deps) check(graph.has(dep), 'Dependência ausente: ' + task + ' -> ' + dep);
const remaining = new Map(graph);
while (remaining.size) {
  const ready = [...remaining].filter(([, deps]) => deps.every(d => !remaining.has(d))).map(([task]) => task);
  if (!ready.length) break;
  ready.forEach(task => remaining.delete(task));
}
check(remaining.size === 0, 'Ciclo no backlog: ' + [...remaining.keys()].join(', '));
check(operationIds.size === 19 && commands === 6, 'Inventário de operações inesperado');
check((!fullPlanning || reqIds.length === 11) && taskIds.length === 35, 'Inventário de requisitos/tarefas inesperado');
const report = { markdownFiles: docs.length, paths: Object.keys(api.paths).length, operations: operationIds.size, commands,
  schemas: Object.keys(api.components.schemas).length, internalReferences: refs, mediaExamples,
  fixtureCases: fixtures.cases.length, intentionallyInvalidCases: fixtures.cases.filter(c => !c.valid).length,
  requirements: reqIds.length, tasks: taskIds.length, failures };
console.log(JSON.stringify(report, null, 2));
if (failures.length) process.exitCode = 1;
