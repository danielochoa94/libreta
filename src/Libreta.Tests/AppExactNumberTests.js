const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const test = require('node:test');
const vm = require('node:vm');

const app = fs.readFileSync(path.join(__dirname, '..', 'Libreta', 'wwwroot', 'app.js'), 'utf8');
const names = ['escapeHtml', 'stableNumber', 'groupExactNumber', 'shortenExactNumber', 'unitFormat',
  'exactNumberHtml', 'displayMatchesExact', 'exactDiffersFromDisplay', 'displayedExactValue'];
const source = names.map((name) => {
  const text = app.match(new RegExp(`function ${name}\\([\\s\\S]*?\\n\\}`))?.[0];
  assert.ok(text, `app.js must define ${name}`);
  return text;
}).join('\n');
const context = vm.createContext({
  payload: {
    units: {
      '': { scale: 1, prefix: '', suffix: '' },
      basis_points: { scale: 10000, prefix: '~', suffix: ' bp' },
      share: { scale: 100, prefix: '', suffix: '' }
    }
  }
});
vm.runInContext(source, context);
const exactNumberHtml = vm.runInContext('exactNumberHtml', context);
const displayMatchesExact = vm.runInContext('displayMatchesExact', context);
const exactDiffersFromDisplay = vm.runInContext('exactDiffersFromDisplay', context);
const displayedExactValue = vm.runInContext('displayedExactValue', context);

test('an exact value is shown in its unit\'s scale with the unit\'s suffix', () => {
  assert.equal(exactNumberHtml('0.0125', 'basis_points'), '125.00 bp');
  assert.equal(exactNumberHtml('0.125', 'share'), '12.50');
});

test('an exact value in an unscaled unit is shown as it is', () => {
  assert.equal(exactNumberHtml('1234.5', ''), '1,234.5');
});

test('a display matches its exact value once the unit\'s symbols and scale are undone', () => {
  assert.equal(displayMatchesExact('(~125 bp)', '-0.0125', 'basis_points'), true);
  assert.equal(displayMatchesExact('~125 bp', '0.01251', 'basis_points'), false);
  assert.equal(displayMatchesExact('12.5', '0.125', 'share'), true);
});

test('an unresolved cell, which carries no exact value, neither differs from its display nor flips sign', () => {
  const unresolved = { display: '', units: '', contra: true };
  assert.equal(exactDiffersFromDisplay(unresolved), false);
  assert.equal(displayedExactValue(unresolved), undefined);
});
