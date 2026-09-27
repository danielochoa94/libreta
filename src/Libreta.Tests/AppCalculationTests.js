const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const test = require('node:test');
const vm = require('node:vm');

const app = fs.readFileSync(path.join(__dirname, '..', 'Libreta', 'wwwroot', 'app.js'), 'utf8');
const extract = (name) => app.match(new RegExp(`function ${name}\\([\\s\\S]*?\\n\\}`))?.[0];

test('a calculation colours each value it uses once and ignores what those values depend on', () => {
  // Arrange
  const context = vm.createContext({
    payload: {
      calculations: { 'present_value\u00002026': { tokens: [{ text: 'Jun 30, 2026', dependency: 'valuation_date' }] } },
    },
  });
  vm.runInContext(`${extract('coordinateKey')}\n${extract('calculationExpansion')}\n` +
    `${extract('calculationReferences')}`, context);
  const calculation = {
    tokens: [
      { text: 'sum(' },
      { text: '-28,093', dependency: 'present_value', column: '2026', expansionId: 'present_value\u00002026' },
      { text: ', ' },
      { text: '-55,395', dependency: 'present_value', column: '2027' },
      { text: ') + ' },
      { text: '786,769', dependency: 'terminal_value', column: 'value' },
      { text: ' − ' },
      { text: '-28,093', dependency: 'present_value', column: '2026' },
    ],
  };

  // Act
  const references = vm.runInContext('calculationReferences', context)(calculation);

  // Assert
  assert.deepEqual(Object.fromEntries(references), {
    'present_value\u00002026': 0,
    'present_value\u00002027': 1,
    'terminal_value\u0000value': 2,
  });
});
