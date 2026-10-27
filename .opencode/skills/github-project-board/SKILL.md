---
name: github-project-board
description: "Manage GitHub Projects (v2) boards via gh CLI: resolve project/field IDs, list tasks with column status and assignees, move cards between columns, assign tasks, comment on tasks, add/remove board items. WHEN: 'project board', 'task board', 'kanban', 'move card', 'change task status', 'pick a task', 'mark task done', 'project items', 'backlog/ready/done column'. DO NOT USE FOR: generic gh CLI ops (github-cli-ops skill), git operations."
version: 2
---

# GitHub Projects (v2) Board Management

Manage any GitHub Projects (v2) board (table/kanban) through the `gh` CLI + GraphQL.
Requires the `project` scope — check with `gh auth status`; fix with `gh auth refresh --scopes project`.

## Query files

All GraphQL queries live in `queries/` next to this SKILL.md — one `.graphql` file
per operation (raw GraphQL, no JSON wrapping). Placeholders are bare tokens
(`PROJECT_ID`, `ITEM_ID`, ...) that you replace with real values at call time.

| File | Purpose | Placeholders |
|---|---|---|
| `queries/project-id-user.graphql` | Project node ID, user-owned project (number is in the URL) | `OWNER_LOGIN`, `PROJECT_NUMBER` |
| `queries/project-id-org.graphql` | Project node ID, org-owned project | `ORG_LOGIN`, `PROJECT_NUMBER` |
| `queries/project-fields.graphql` | Field IDs + single-select option IDs (columns) | `PROJECT_ID` |
| `queries/user-id.graphql` | User node ID (needed for assignments) | `LOGIN` |
| `queries/list-board.graphql` | Board items with status/assignees + `totalCount` + `pageInfo` | `PROJECT_ID` |
| `queries/update-item-field.graphql` | Move card / set a field value | `PROJECT_ID`, `ITEM_ID`, `FIELD_ID`, `OPTION_ID` |
| `queries/add-assignees.graphql` | Assign an issue/PR | `ISSUE_NODE_ID`, `USER_ID` |
| `queries/add-item.graphql` | Add an issue/PR to the board | `PROJECT_ID`, `CONTENT_ID` |
| `queries/delete-item.graphql` | Remove an item from the board | `PROJECT_ID`, `ITEM_ID` |

## How to run a query (PowerShell)

```powershell
$skillDir = '.opencode/skills/github-project-board'   # this skill's base dir, relative to the repo root
$q = (Get-Content "$skillDir/queries/user-id.graphql" -Raw).Replace('LOGIN', 'michaeltg17')
@{ query = $q } | ConvertTo-Json -Compress | gh api graphql --input -
```

- Chain `.Replace(...)` calls for multiple placeholders.
- `ConvertTo-Json` escapes the query into a valid JSON body — **never pass the
  query as `-f query="..."`**: PowerShell's native command-line marshaling drops
  the embedded double quotes, so GraphQL string arguments arrive unquoted and
  fail with `invalid value (X). Expected type 'String!'`.
- PowerShell does NOT support `&&` — separate commands with `;`.

## Step 1 — Resolve IDs (once per board/session)

Project node ID. User-owned (number is in the URL, e.g. `github.com/users/LOGIN/projects/3`) —
run `queries/project-id-user.graphql` with `OWNER_LOGIN` + `PROJECT_NUMBER`; org-owned —
run `queries/project-id-org.graphql` with `ORG_LOGIN` + `PROJECT_NUMBER`.

Fields + single-select options (columns) — run `queries/project-fields.graphql` with
`PROJECT_ID`. Note the **status field ID** and the **option ID per column**
(e.g. Backlog / Ready / In progress / Done).

User node IDs (for assignments) — run `queries/user-id.graphql` with `LOGIN`.

If GitHub returns a deprecation warning for an ID, use the `next_global_id` from
`extensions.warnings` (new format, e.g. `U_...` instead of `MDQ6...`).

## Step 2 — List board

Run `queries/list-board.graphql` with `PROJECT_ID`. Returns up to 100 items per
page with their field values and content, plus `totalCount` and `pageInfo`.

Per item:
- `id` — the project item ID (`PVTI_...`) needed for moves
- `fieldValues.nodes` — set values; the single-select node whose `field.name` is
  the status column holds the current column name. Ignore empty `{}` nodes.
- `content` — for issues/PRs: issue `id` (node ID, `I_...` — needed for
  assignments), `number`, `title`, `state`, repository, assignee logins

**Pagination (mandatory on boards with >100 items)**: if `pageInfo.hasNextPage`
is `true`, re-run the query with the previous page's `endCursor` and repeat until
`hasNextPage` is `false`. Without this you silently miss items beyond the first 100
(e.g. whole columns).

```powershell
$q = (Get-Content "$skillDir/queries/list-board.graphql" -Raw).Replace('PROJECT_ID', 'PVT_...')
$q = $q.Replace('items(first: 100)', 'items(first: 100, after: "END_CURSOR")')
@{ query = $q } | ConvertTo-Json -Compress | gh api graphql --input -
```

**Filter a subset** without paging everything: replace `items(first: 100)` with
`items(first: 100, query: "some text")` in the same way.

**Pick eligible tasks**: filter the list client-side (JSON) — e.g. status column
`Ready` + `assignees.nodes` empty + issue `state: OPEN`.

## Step 3 — Move card (change status column)

Run `queries/update-item-field.graphql` with `PROJECT_ID`, `ITEM_ID` (from the
list), `FIELD_ID` (status field), `OPTION_ID` (target column).

Other field types use the same mutation with a different `value` — replace
`singleSelectOptionId: "OPTION_ID"` in the query with: `text: "..."`,
`number: 5`, `date: "2026-01-01"`, `multiSelectOptionIds: ["..."]`,
`iterationId: "..."`.
Assignees/labels/milestone are properties of the issue/PR, not project fields —
use issue mutations instead.

## Step 4 — Assign / unassign

Run `queries/add-assignees.graphql` with `ISSUE_NODE_ID` (`I_...` from the list)
and the `USER_ID` from Step 1.

Unassign: use the same mutation file, replacing `addAssigneesToAssignable` with
`removeAssigneesFromAssignable` (same input shape).
The `Assignable` interface has no `id` field — select `assignees` in the
mutation's payload to confirm the result.

## Step 5 — Comment on a task

There is no project-item comment API — comments live on the underlying issue or PR:

```powershell
gh issue comment ISSUE_NUMBER --repo REPO_OWNER/REPO_NAME --body "What was done and what to review."
gh pr comment PR_NUMBER --repo REPO_OWNER/REPO_NAME --body "..."
```

## Maintenance (only when asked)

Add an existing issue/PR to the board: run `queries/add-item.graphql`
(`CONTENT_ID` = issue/PR node ID).

Remove an item from the board (does NOT delete the issue): run
`queries/delete-item.graphql`.

## Verify after every change

Re-run **Step 2** (ideally with the `query:` filter for the item's title) and
confirm the column/assignee changed before proceeding.

## Troubleshooting

| Error | Fix |
|---|---|
| `missing required scopes [read:project]` | `gh auth refresh --scopes project` |
| `Argument ... has an invalid value (X). Expected type 'String!'` | Query was passed via `-f query=` — PowerShell dropped embedded quotes. Use the `ConvertTo-Json` + `--input -` pattern |
| Unknown field/option ID | Re-run **Step 1**; the board may have been recreated or columns renamed |
| `Field 'id' doesn't exist on type 'Assignable'` | Select `assignees` in the mutation's payload instead |
| Deprecation warning on a user ID | Switch to the `next_global_id` from the warning |
| Item not found (`PVTI_...`) | Item was archived/removed — re-run **Step 2** |
| Board looks missing items / a whole column is absent | Board has >100 items — paginate with `after: "END_CURSOR"` (see Step 2) |
