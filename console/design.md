# Console design

The landing, the product app, and the backoffice share one system: a near-black paper, orange action, Inter, a left rail, and rounded surface cards. The landing may use a larger title and more air. It does not invent a second palette or a second symbol.

## Who may enter

| Role | Person | Console |
| --- | --- | --- |
| admin | Service company | Full console. Writes commercial records and policies. |
| support | Service company | Support and Audit only. Reads customers, records calls. |
| owner | Customer account | No console. The account can exist as an organization member. |

The auditor role enters the console and sees only the Audit group: the trail and the access logs. Admin and support also see that group. Only admin writes plans, subscriptions, machine keys, and policies. The owner does not open the console.

The first local account is an admin. Signing in as owner returns a refusal: the console belongs to the service company.

## Breakpoints

Mobile first. Layout changes at three widths and nowhere else.

| Name | Width | Arrangement |
| --- | --- | --- |
| Phone | under 768px | Menu opens a drawer. Search sits on a second row. Records are cards. Forms are one column. |
| Tablet | 768px to 1023px | Word navigation wraps under the masthead. Tables replace cards. Forms use two columns. |
| Desktop | 1024px and up | The same masthead. Page padding grows to 32px. Forms and figures use the wider grid. |

Do not add a fourth breakpoint. Do not hide primary actions on the phone.

## Layout

- Page background is `--paper` (near black). Surfaces are `--surface` cards with 16px corners. The rail is 248px and stays on the left from 768px. Menu on the phone opens the same rail as a drawer.
- The rail holds the name, grouped words, and the account chip at the bottom. Search and organization live on the page, not in the rail. Language and theme sit in the account chip.
- Navigation groups stay 10px, uppercase, `--muted`. The active word sits on a `--surface` pill with a 3px `--accent` bar on the left.
- Page title is `text-3xl`, semibold, tight tracking. One short lede is allowed under it. A settings-style action may sit on the right of the title row.

## Color and type

- The first paint is dark. `--paper` is #070707, `--surface` is #141414, ink is #f5f5f5, muted is #8a8a8a. The accent and filled action are orange `#f97316`.
- `html.light` inverts the paper to #f4f4f5 and keeps the same orange action. Theme is stored in `gatekeeper.theme`.
- Focus is an `--accent` border. Status dots and meters use `--accent`.
- Type is Inter, then Segoe UI, then the system sans. The family does not change between the three surfaces.
- Colors are the variables in `src/index.css`: paper, surface, ink, muted, line, action, danger, warning, accent, rail, and hover.
- Icons are the set in `src/icons.tsx`. Same stroke, 16px base. A word remains the accessible name. The icon is `aria-hidden`.

## Forms

- Every input has a visible label above it. Placeholder text is not a label.
- Validate before the request. Show the error under the field in `text-xs text-red-700`.
- Required text, an email containing @, a password of at least 8 characters when one is typed, whole numbers, and a start date that is not after the end date.
- The primary button uses a verb from the record: Save plan, Update member, Issue key.
- A customer owner can be saved as a member. Say, next to the role, that this person cannot open the console.

## Feedback

- Success is a toast at the bottom of the phone and at the top right from 640px. It leaves on its own.
- A failure toast stays until it is dismissed. The text is the server detail, not a generic failure.
- A destructive action uses the confirm dialog. The warning says it is a soft delete and what remains afterwards.
- Do not stack a banner and a toast for the same event.

## Loading and empty

- Lists and figures show four quiet skeleton bars while the first request is in flight. Pulse is removed when the user prefers reduced motion.
- An empty list says "Nothing saved yet." Do not draw an empty-state illustration.

## Figures

- The overview is numbers, horizontal meters, and one usage chart. The chart uses the same ink and names its source and window.
- A meter is a 8px track. The fill is slate-900. The label and the value sit on one line.
- Show at most three summary numbers and one meter group. Support does not request admin-only resources.

## Search

- The page field searches from two characters.
- Results name the record and the screen it belongs to. Choosing one opens that screen and filters it.
- A filter chip states the match and offers Clear.

## Language

The console speaks Portuguese (`pt-BR`) and English (`en`). The switch sits in the avatar menu and is stored in `gatekeeper.locale`. The first visit follows the browser language.

Amounts are Brazilian real. `Intl.NumberFormat` writes `R$ 10,00` in Portuguese and `R$10.00` in English. Dates and times use `Intl.DateTimeFormat` with `dateStyle: "medium"` and, when a clock is useful, `timeStyle: "short"`. Whole counts use the locale grouping.

New interface copy is added to both catalogs in `src/messages.ts`. Field checks return a code (`required`, `email`, `password`, `whole`, `min`, `amount`, `end`); the screen turns that code into a sentence. A known server phrase is translated at the toast. An unknown phrase is shown as the server sent it.

Do not format a price or a timestamp by hand.

## Public surfaces

The landing is one page: masthead, title, lede, hairline, one figure, one filled action into the product, a short timeline, three paths as a list, and a language footer.

The product is a dense cockpit without page scroll. Templates and gatekeepers show an AI face. Forms surface the error under the field. The report and the provider list are searchable, sortable grids. Advanced detail stays behind a click; a short fast-track action jumps to chain, rack, or player.

The backoffice overview keeps its numbers, adds the usage chart, and lists recent audit as a timeline. Lists keep search, filters, skeleton, and the empty sentence. Forms keep the label, the error under the field, the toast, and the confirm dialog.

## Freshness

Lists and figures may come from memory for up to five minutes. A successful save, update, or delete refreshes every open screen that shows that family of records.

The first load, when memory has nothing for that list, uses the four skeleton bars. A later visit that memory can answer paints the records directly, with no second loading treatment and no cached badge.

Do not store API records in `localStorage` or `sessionStorage`. Language and theme stay where they are.

## Motion

- Drawer and menu appear without a long animation.
- Do not animate layout, numbers, or page changes.
