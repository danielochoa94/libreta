const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const test = require('node:test');
const vm = require('node:vm');

const app = fs.readFileSync(path.join(__dirname, '..', 'Libreta', 'wwwroot', 'app.js'), 'utf8');
const source = app.match(/function downloadName\(disposition, format\) \{[\s\S]*?\n\}/)?.[0];
assert.ok(source, 'app.js must define downloadName');
const downloadName = vm.runInNewContext(`(${source})`);

test('an export takes the name the server gives it', () => {
  assert.equal(downloadName("attachment; filename=spacex.xlsx; filename*=UTF-8''spacex.xlsx", 'xlsx'), 'spacex.xlsx');
});

test('an encoded name is decoded', () => {
  assert.equal(downloadName("attachment; filename=\"a b.xlsx\"; filename*=UTF-8''a%20b.xlsx", 'xlsx'), 'a b.xlsx');
});

test('a plain name is read without its quotes', () => {
  assert.equal(downloadName('attachment; filename="book one.html"', 'html'), 'book one.html');
});

test('a response without a name still downloads in its format', () => {
  assert.equal(downloadName(null, 'html'), 'book.html');
});
