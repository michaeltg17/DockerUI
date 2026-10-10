---
name: github-project-board
description: "Manage GitHub Projects (v2) boards via gh CLI: resolve project/field IDs, list tasks with column status and assignees, move cards between columns, assign tasks, comment on tasks, add/remove board items. WHEN: 'project board', 'task board', 'kanban', 'move card', 'change task status', 'pick a task', 'mark task done', 'project items', 'backlog/ready/done column'. DO NOT USE FOR: generic gh CLI ops (github-cli-ops skill), git operations."
version: 1
---

# GitHub Projects (v2) Board Management

Manage any GitHub Projects v2 board (table/kanban) through the `gh` CLI + GraphQL.
Requires the `project` scope — check with `gh auth status`; fix with `gh auth refresh --scopes project`.

## PowerShell rules

- PowerShell does NOT support `&&` — use `;`.
- Always pass GraphQL via a single-quoted JSON string piped to `--input -`. This preserves the inner double quotes (inline `-f query="..."` breaks them):

  ```powershell
  '{"query":"query { viewer { login } }"}' | gh api graphql --input -
  ```

## Step 1 — Resolve IDs (once per board/session)

Project node ID. User-owned (number is in the URL, e.g. `github.com/users/LOGIN/projects/3`):

```powershell
'{"query":"query { user(login: \"OWNER_LOGIN\") { projectV2(number: PROJECT_NUMBER) { id title } } }"}' | gh api graphql --input -
```

Org-owned:

```powershell
'{"query":"query { organization(login: \"ORG_LOGIN\") { projectV2(number: PROJECT_NUMBER) { id title } } }"}' | gh api graphql --input -
```

Fields + single-select options (columns). Replace `PROJECT_ID`:

```powershell
'{"query":"query { node(id: \"PROJECT_ID\") { ... on ProjectV2 { fields(first: 50) { nodes { ... on ProjectV2FieldCommon { id name } ... on ProjectV2SingleSelectField { id name options { id name } } } } } } }"}' | gh api graphql --input -
```

Note the **status field ID** and the **option ID per column** (e.g. Backlog / Ready / In progress / Done).

User node IDs (for assignments). Replace `LOGIN`:

```powershell
'{"query":"query { user(login: \"LOGIN\") { id } }"}' | gh api graphql --input -
```

If GitHub returns a deprecation warning for an ID, use the `next_global_id` from `extensions.warnings` (new format, e.g. `U_...` instead of `MDQ6...`).

## Step 2 — List board

Replace `PROJECT_ID`. Returns every item with its field values and content.

```powershell
'{"query":"query { node(id: \"PROJECT_ID\") { ... on ProjectV2 { items(first: 100) { nodes { id fieldValues(first: 12) { nodes { ... on ProjectV2ItemFieldSingleSelectValue { name field { ... on ProjectV2FieldCommon { name } } } ... on ProjectV2ItemFieldTextValue { text field { ... on ProjectV2FieldCommon { name } } } ... on ProjectV2ItemFieldNumberValue { number field { ... on ProjectV2FieldCommon { name } } } } } content { ... on Issue { id number title state repository { nameWithOwner } assignees(first: 10) { nodes { login } } } ... on PullRequest { id number title assignees(first: 10) { nodes { login } } } ... on DraftIssue { id title } } } } } } }"}' | gh api graphql --input -
```

Per item:
- `id` — the project item ID (`PVTI_...`) needed for moves
- `fieldValues.nodes` — set values; the single-select node whose `field.name` is the status column holds the current column name. Ignore empty `{}` nodes.
- `content` — for issues/PRs: issue `id` (node ID, `I_...` — needed for assignments), `number`, `title`, `state`, repository, assignee logins

**Filter a subset** without paging everything: add a search filter to the connection, e.g. `items(first: 5, query: "some text")`.

**Pick eligible tasks**: filter the list client-side (JSON) — e.g. status column `Ready` + `assignees.nodes` empty + issue `state: OPEN`.

## Step 3 — Move card (change status column)

Replace `PROJECT_ID`, `ITEM_ID` (from the list), `FIELD_ID` (status field), `OPTION_ID` (target column).

```powershell
'{"query":"mutation { updateProjectV2ItemFieldValue(input: { projectId: \"PROJECT_ID\" itemId: \"ITEM_ID\" fieldId: \"FIELD_ID\" value: { singleSelectOptionId: \"OPTION_ID\" } }) { projectV2Item { id } } }"}' | gh api graphql --input -
```

Other field types use the same mutation with a different `value`: `text: "..."`, `number: 5`, `date: "2026-01-01"`, `multiSelectOptionIds: ["..."]`, `iterationId: "..."`.
Assignees/labels/milestone are properties of the issue/PR, not project fields — use issue mutations instead.

## Step 4 — Assign / unassign

Replace `ISSUE_NODE_ID` (`I_...` from the list) and the user ID from Step 1.

```powershell
'{"query":"mutation { addAssigneesToAssignable(input: { assignableId: \"ISSUE_NODE_ID\" assigneeIds: [\"USER_ID\"] }) { assignable { assignees(first: 5) { nodes { login } } } } }"}' | gh api graphql --input -
```

Unassign: `removeAssigneesFromAssignable` with `assigneeIds`.
The `Assignable` interface has no `id` field — select `assignees` to confirm the result.

## Step 5 — Comment on a task

There is no project-item comment API — comments live on the underlying issue or PR:

```powershell
gh issue comment ISSUE_NUMBER --repo REPO_OWNER/REPO_NAME --body "What was done and what to review."
gh pr comment PR_NUMBER --repo REPO_OWNER/REPO_NAME --body "..."
```

## Maintenance (only when asked)

Add an existing issue/PR to the board (`CONTENT_ID` = issue/PR node ID):

```powershell
'{"query":"mutation { addProjectV2ItemById(input: { projectId: \"PROJECT_ID\" contentId: \"CONTENT_ID\" }) { item { id } } }"}' | gh api graphql --input -
```

Remove an item from the board (does NOT delete the issue):

```powershell
'{"query":"mutation { deleteProjectV2Item(input: { projectId: \"PROJECT_ID\" itemId: \"ITEM_ID\" }) { deletedItemId } }"}' | gh api graphql --input -
```

## Verify after every change

Re-run **Step 2** (ideally with the `query:` filter for the item's title) and confirm the column/assignee changed before proceeding.

## Troubleshooting

| Error | Fix |
|---|---|
| `missing required scopes [read:project]` | `gh auth refresh --scopes project` |
| Unknown field/option ID | Re-run **Step 1**; the board may have been recreated or columns renamed |
| `Argument ... Expected type 'String!'` | Quoting broke — use the single-quoted JSON + `--input -` pattern |
| `Field 'id' doesn't exist on type 'Assignable'` | Select `assignees` in the mutation's payload instead |
| Deprecation warning on a user ID | Switch to the `next_global_id` from the warning |
| Item not found (`PVTI_...`) | Item was archived/removed — re-run **Step 2** |
