# Running Libreta

Libreta provides a local review server for humans and headless commands for agents and scripts.

Book commands accept a book root—the folder containing `book.yaml`—before any options.
`--help` and `--docs` work without a book.

## Read the documentation from the command line

Read the introduction or either complete guide from any folder:

```bash
libreta --docs
libreta --docs format
libreta --docs running
```

## Preview a book

Pass the book folder with no headless option:

```bash
libreta books/spacex
```

The command opens the book in a browser tab, normally at `http://localhost:5173`.
Relative book paths are resolved from the working directory; absolute paths work from anywhere.

Running the command again for a book that is already being served opens the existing server's page rather than starting a second server.
Print that server's address without opening anything:

```bash
libreta books/spacex --url
```

The server stops about ten seconds after its last tab closes, long enough for a reload to reconnect.
It keeps running until a first tab connects, and `Ctrl+C` stops it at any time.
The command always prints the address as well, for sessions with no browser to open, such as over SSH.

## Find the book

Omitting the book root searches the working folder and up to three folders below it.
The search skips `bin`, `obj`, hidden folders, and symbolic links, and stops descending when it finds a `book.yaml`.
Exactly one book must be found; otherwise Libreta reports no books or lists the candidates and asks for an explicit path.

## Watch for changes

Editing a book file while the server runs recalculates its views in place without restarting the command or reloading the page.
A book that temporarily fails to load reports the error in the page rather than stopping the server.

## Run more than one book

Without `--port`, each server starts at port 5173 and probes upward until it finds a free port:

```bash
libreta books/spacex
libreta books/acme
```

Use `--port` when a stable port matters:

```bash
libreta books/spacex --port 5174
```

If that explicit port is occupied, Libreta reports the conflict and exits instead of silently choosing another one.
It also exits if the book is already served on a different port.

## Read a book without a server

List view ids in navigation order:

```bash
libreta books/spacex --list
```

Print one complete view as a Markdown table:

```bash
libreta books/spacex --view dcf/terminal-value
```

Print the engine's exact, unrounded value for one presented cell:

```bash
libreta books/spacex --value dcf value_per_share Value
```

Add `--json` to any headless command for structured output:

```bash
libreta books/spacex --list --json
libreta books/spacex --view dcf/terminal-value --json
libreta books/spacex --value dcf value_per_share Value --json
```

The view form returns the table's columns, rows, exact and displayed values, checks and sensitivities; formula graphs and sources are left out, so use `--value` for those.
The value form includes the resolved line and column, exact and displayed values, units, formula, note and source, the calculation written out with displayed values, the cells it reads as `inputs`, and the cells that read it as `dependents`.
An input this view does not show carries its own formula, calculation and inputs; one that a `prior()` reaches past the first period carries `missing` instead of a column.
`--value` addresses the underlying line and column even when the view is transposed, and fails if the cell is unresolved or is not presented by that view.
With `--json`, an unresolved cell still prints before the nonzero exit, so its inputs show what is missing.

These commands write results to standard output, report errors to standard error, and return a nonzero exit code on failure.
Use one headless command per invocation; `--port` applies only to the server.

## Verify a book

Run every check and validate every presented formula graph:

```bash
libreta books/spacex --check
libreta books/spacex --check --json
```

The report lists results by view and ends with totals for passed checks, failed checks, view errors, and missing lines.
Failed checks include their per-period deltas.

A presented row that resolves to neither a fact nor a formula is reported as a missing line.
The command exits 1 if a check fails, a view fails to load, a presented calculation is invalid, or a line is missing.

## Share a book as one page

Write the whole book to a single HTML file that opens in a browser with no server:

```bash
libreta books/spacex --export spacex.html
```

The page carries every view, its calculations and provenance, and the source images, so it works offline and attached to an email.
It is a snapshot: it never reloads, and the status bar shows the date it was exported.
The export refuses a book with a view that fails to load, rather than sharing a broken page.

## Export a workbook

Write the whole book to an Excel workbook, one sheet per view in navigation order:

```bash
libreta books/spacex --export spacex.xlsx
```

Each sheet is laid out like the page, and each cell holds the value the page shows, contra lines included.
A cell's units become an Excel number format, so the cell keeps its exact value and only the display is scaled and rounded.
Unresolved cells are left empty.
The file extension picks the format, so `--export` accepts only `.html` and `.xlsx`.
