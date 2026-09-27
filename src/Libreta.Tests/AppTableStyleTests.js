const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const test = require('node:test');

const css = fs.readFileSync(path.join(__dirname, '..', 'Libreta', 'wwwroot', 'app.css'), 'utf8');

test('selecting a period does not change its header font metrics', () => {
  assert.doesNotMatch(css,
    /\.book-table thead th\.selected,\s*\.book-table thead th\.cell-selected\s*\{[^}]*font-weight:/);
});
