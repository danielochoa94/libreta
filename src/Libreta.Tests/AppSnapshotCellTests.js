const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const test = require('node:test');
const vm = require('node:vm');

const app = fs.readFileSync(path.join(__dirname, '..', 'Libreta', 'wwwroot', 'app.js'), 'utf8');
const source = app.match(/function snapshotCell\(hash, lines\) \{[\s\S]*?\n\}/)?.[0];
assert.ok(source, 'app.js must define snapshotCell');
const snapshotCell = vm.runInNewContext(`(${source})`, { URLSearchParams });

const lines = {
  'historical.revenue': { view: 'income', columns: { 2026: 'forecast' } }
};

test('a fragment naming no line asks for no cell', () => {
  assert.equal(snapshotCell('#view=income', lines), null);
  assert.equal(snapshotCell('', lines), null);
});

test('a cell opens in the first view presenting its line', () => {
  assert.deepEqual({ ...snapshotCell('#line=historical.revenue&column=2025', lines) },
    { view: 'income', line: 'historical.revenue', column: '2025' });
});

test('a column another view presents first opens there', () => {
  assert.equal(snapshotCell('#line=historical.revenue&column=2026', lines).view, 'forecast');
});

test('a view named in the fragment wins', () => {
  assert.equal(snapshotCell('#view=summary&line=historical.revenue&column=2026', lines).view, 'summary');
});

test('a line is named as the command line names it', () => {
  assert.equal(snapshotCell('#line=historical/revenue', lines).line, 'historical.revenue');
});

test('a line no view presents names no view', () => {
  assert.equal(snapshotCell('#line=historical.margin&column=2025', lines).view, null);
});
