# ProjectPilot — AI-Powered Project Manager for Windows

A Windows desktop app (C# / .NET 8 / WPF, SQLite database) for planning, running and tracking projects of any size, with an AI assistant (Phase 3).

## Download
Every push to `main` is built automatically by GitHub Actions. Open **Releases → latest** and download:
- **ProjectPilot-Setup.exe** — installer (Start menu + optional desktop shortcut), or
- **ProjectPilot.exe** — portable, runs without installing.

No .NET installation is needed. Windows 10/11, 64-bit.

## What works now (Phase 1)
- **Dashboard** — project counts (active / completed / pending), tasks done / outstanding, overdue, due in 7 days, per-project progress bars, tasks-by-status chart, upcoming deadlines, overdue list, team workload, rule-based alerts.
- **My Projects** — create from scratch or from 4 templates; name, description, objectives, outcomes, dates, owner, team, stakeholders, priority, budget, status; attachments; discussion; activity history; search & filter.
- **Tasks** — tasks and subtasks, assignment, priority, start/due dates, progress, statuses (Not Started, In Progress, Under Review, On Hold, Completed, Overdue — set automatically), dependencies with loop detection, recurring tasks (daily/weekly/monthly), notes, attachments, comments, activity log; filter by project/status/priority/owner, search, sort, group by project/priority/owner/status/deadline.
- **Settings** — light/dark theme, your name, backup now, restore, automatic daily backups (last 10 kept).

## What's new in Phase 2
- **Timeline & Gantt** — interactive chart per project: task bars with progress, subtasks as summary bars, milestones (◆), dependency arrows, today and target-date lines, Day/Week/Month zoom.
  Drag a bar to move it, drag its right edge to change the deadline, double-click to edit. Dependent tasks that would start too early are pushed later automatically (after you confirm).
- **Critical path** — tasks with no slack are highlighted; any delay to them delays the project end.
- **Planned vs actual** — every task keeps its planned (baseline) dates, actual start and completion; "Set baseline" re-plans; planned bars can be shown on the chart.
- **Project health** — On Track / At Risk / Delayed / Completed with the reason, shown on the timeline, projects list and dashboard; forecast end date vs target.
- **Milestones** — create, link tasks, track linked-task progress, mark complete; alerts for overdue / upcoming / ready milestones.
- **Automatic alerts** — banner at the top of the app when tasks or milestones are overdue or due today/tomorrow (re-checked every 15 minutes).

## Roadmap
| Phase | Content |
|---|---|
| 1 ✅ | Interface, dashboard, projects, tasks |
| 2 ✅ | Timeline & Gantt (drag to reschedule, dependency shifting, critical path), milestones, planned vs actual, health indicators |
| 3 | AI assistant: plan generation, smart prioritisation, next steps, AI reports (Claude / OpenAI / Gemini, your own API key, changes need your approval) |
| 4 | Team management, collaboration, reports & exports (PDF/Excel/CSV), calendar, notifications |
| 5 | Testing, performance, polish, installer |

## Offline vs online
Everything except the AI assistant works fully offline. The AI assistant (Phase 3) needs internet and an API key from your chosen provider (billed by that provider).

## Where data is stored
`%LOCALAPPDATA%\ProjectPilot\` — `projectpilot.db` (database), `attachments\`, `backups\`, `error.log`.

## Build locally
```
dotnet run --project src/ProjectPilot
```
