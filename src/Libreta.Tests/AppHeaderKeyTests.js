const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const test = require('node:test');
const vm = require('node:vm');

const app = fs.readFileSync(path.join(__dirname, '..', 'Libreta', 'wwwroot', 'app.js'), 'utf8');
const handlerSource = app.match(/function handleHeaderKey\([\s\S]*?\n\}/)?.[0];
assert.ok(handlerSource, 'app.js must define handleHeaderKey');
const selectTableCorner = app.match(/function selectTableCorner\([\s\S]*?\n\}/)?.[0];
assert.ok(selectTableCorner, 'app.js must define selectTableCorner');

function pressOnHeader(key, periodIndex, ctrlKey = false) {
  const selected = [];
  const context = {
    COLUMN_AXIS: -1,
    payload: { columns: [{}, {}, {}, {}, {}], rows: [{}, { cells: [{}] }, { cells: [{}] }, {}] },
    extendCellRangeByKey: () => false,
    hasCells: (row) => Boolean(row?.cells?.length),
    selectColumn: (index) => selected.push(index),
    selectRow: (row) => selected.push(['row', row]),
    inspectCell: (row, period) => selected.push(['cell', row, period])
  };
  vm.runInNewContext(selectTableCorner, context);
  const handleHeaderKey = vm.runInNewContext(`(${handlerSource})`, context);
  handleHeaderKey({ key, ctrlKey, metaKey: false, shiftKey: false, preventDefault() {} }, periodIndex);
  return selected;
}

test('ctrl+arrow on a column header jumps to the first or last column', () => {
  assert.deepEqual(pressOnHeader('ArrowLeft', 2, true), [0]);
  assert.deepEqual(pressOnHeader('ArrowRight', 2, true), [4]);
});

test('a plain arrow on a column header moves one column', () => {
  assert.deepEqual(pressOnHeader('ArrowLeft', 2), [1]);
  assert.deepEqual(pressOnHeader('ArrowRight', 2), [3]);
});


test('ctrl+home on a column header goes to the first line label and ctrl+end to the last line in the last period', () => {
  assert.deepEqual(pressOnHeader('Home', 2, true), [['row', 1]]);
  assert.deepEqual(pressOnHeader('End', 2, true), [['cell', 2, 4]]);
});