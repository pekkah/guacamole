# Feature specs

Each `NNN-*` directory holds the planning artifacts (spec, plan, research, contracts,
quickstart, tasks) for one feature, created with the `/specify`, `/plan` and `/tasks`
commands. They are a historical record: the code has moved on since many of them were
written (for example `src/Guacamole.Core` was merged into `src/Guacamole` in #14, and the
generator was rewritten in #19 and #29). Prose in `spec.md`/`plan.md` is not updated after
the fact; only the status of `tasks.md` checklists is.

## Task status convention

`tasks.md` checklists were reconciled against the repository on 2026-11-08 (issue #45).
Every status was checked against the files, tests, workflows, CI runs or git history.

| Marker | Meaning |
|--------|---------|
| `- [x] T001 ...` | Done. A trailing `— done: ...` note gives the evidence when the result lives somewhere other than the path the task names (e.g. `src/Guacamole/...` instead of `src/Guacamole.Core/...`). |
| `- [x] ~~T001 ...~~ — superseded: <reason>` | Will not be done as written: the approach was replaced or the item became obsolete. The reason names the replacing PR or file. |
| `- [ ] T001 ... — open: <note>` | Still open and still relevant. |

Each reconciled `tasks.md` starts with a short **Reconciliation** note summarizing what
changed. `008-ci-pipelines-we/tasks.md` uses headings instead of checkboxes per task, so
it carries a **Status** line under each reconciled task heading instead.
