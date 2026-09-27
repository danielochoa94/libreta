const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const test = require('node:test');
const vm = require('node:vm');

const app = fs.readFileSync(path.join(__dirname, '..', 'Libreta', 'wwwroot', 'app.js'), 'utf8');
const coordinatorSource = app.match(/class RequestCoordinator \{[\s\S]*?\n\}/)?.[0];
assert.ok(coordinatorSource, 'app.js must define RequestCoordinator');
const RequestCoordinator = vm.runInNewContext(`(${coordinatorSource})`);

test('an older view response cannot replace a newer navigation', () => {
  const requests = new RequestCoordinator();
  const first = requests.beginView('income');
  const second = requests.beginView('cash-flow');

  assert.equal(requests.acceptView(second, 'cash-flow'), true);
  assert.equal(requests.acceptView(first, 'cash-flow'), false);
});

test('an accepted catalog error invalidates an in-flight view response', () => {
  const requests = new RequestCoordinator();
  const catalog = requests.beginCatalog();
  const view = requests.beginView('income');

  assert.equal(requests.acceptCatalog(catalog), true);
  assert.equal(requests.acceptView(view, 'income'), false);
});

test('an older catalog response cannot replace a newer catalog', () => {
  const requests = new RequestCoordinator();
  const first = requests.beginCatalog();
  const second = requests.beginCatalog();

  assert.equal(requests.acceptCatalog(first), false);
  assert.equal(requests.acceptCatalog(second), true);
});
