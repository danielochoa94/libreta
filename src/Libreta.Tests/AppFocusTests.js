const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const test = require('node:test');
const vm = require('node:vm');

const app = fs.readFileSync(path.join(__dirname, '..', 'Libreta', 'wwwroot', 'app.js'), 'utf8');
const restorationSource = app.match(/function shouldRestoreGridFocus\([\s\S]*?\n\}/)?.[0];
assert.ok(restorationSource, 'app.js must define shouldRestoreGridFocus');
const shouldRestoreGridFocus = vm.runInNewContext(`(${restorationSource})`);

function target(matchingSelector = null) {
  return { closest(selector) { return selector === matchingSelector ? this : null; } };
}

test('plain page chrome restores focus to the selected grid cell', () => {
  const plainTarget = target();

  assert.equal(shouldRestoreGridFocus(plainTarget, true, false), true);
});

test('interactive elements keep their own focus', () => {
  const interactiveSelector = 'a, button, input, select, textarea, summary, [contenteditable], ' +
    '[tabindex]:not([tabindex="-1"]), [data-column]';
  const button = target(interactiveSelector);

  assert.equal(shouldRestoreGridFocus(button, true, false), false);
});

test('focus is not restored without a selection or while a dialog is open', () => {
  const plainTarget = target();

  assert.equal(shouldRestoreGridFocus(plainTarget, false, false), false);
  assert.equal(shouldRestoreGridFocus(plainTarget, true, true), false);
});
