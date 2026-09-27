const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const test = require('node:test');
const vm = require('node:vm');

const app = fs.readFileSync(path.join(__dirname, '..', 'Libreta', 'wwwroot', 'app.js'), 'utf8');
const source = ['handleGridCopy', 'selectedCopyText', 'coordinateText', 'cleanClipboardValue', 'displayedExactValue']
  .map((name) => app.match(new RegExp(`function ${name}\\([\\s\\S]*?\\n\\}`))?.[0])
  .filter(Boolean)
  .join('\n');

function copy(selection, cellRanges = []) {
  const context = {
    COLUMN_AXIS: -1,
    selection,
    payload: {
      columns: [{ label: 'FY2024' }, { label: 'FY2025' }],
      rows: [{ label: 'Revenue', cells: [{ display: '1,200' }, { display: '1,500' }] }]
    },
    selectedCoordinates: () => cellRanges,
    selectedRangeTsv: () => 'range'
  };
  const handleGridCopy = vm.runInNewContext(`${source}\nhandleGridCopy`, context);
  let copied = null;
  let prevented = false;
  handleGridCopy({ preventDefault: () => { prevented = true; }, clipboardData: { setData: (_, text) => { copied = text; } } });
  return prevented ? copied : null;
}

test('copying a single cell copies its display value', () => {
  // Arrange
  const cell = { index: 0, periodIndex: 1 };

  // Act
  const copied = copy(cell, [cell]);

  // Assert
  assert.equal(copied, 'range');
});

test('copying a selected line copies its label', () => {
  // Arrange
  const selection = { index: 0, periodIndex: null };

  // Act
  const copied = copy(selection);

  // Assert
  assert.equal(copied, 'Revenue');
});

test('copying a selected period header copies its label', () => {
  // Arrange
  const selection = { index: -1, periodIndex: 1 };

  // Act
  const copied = copy(selection);

  // Assert
  assert.equal(copied, 'FY2025');
});

test('copying with nothing selected leaves the browser alone', () => {
  // Arrange, Act
  const copied = copy(null);

  // Assert
  assert.equal(copied, null);
});
