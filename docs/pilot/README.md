# Developer pilot kit

Status: ready to recruit; no participants enrolled and no outcomes claimed by this document.

## Audience and question

Recruit 5–10 individual developers who use coding agents on projects that span several sessions. Favor people who currently repeat constraints, lose decisions after a reset, or switch between projects. Include at least two different agent clients and operating systems if available.

Test whether explicit project memory makes the next session easier to resume without excessive upkeep. The product promise is **project memory you can inspect, update, and resume**.

## Two-week procedure

1. Give each participant a random ID. Obtain permission for the specific logs or examples they choose to share. Do not collect credentials, proprietary repositories or raw transcripts by default.
2. Record their usual workflow, client, OS and a short baseline example. Use the [quickstart](../quickstart.md), time first successful resume, and record where help was needed.
3. Use two comparable real projects or task sets. Half the participants use their usual method first, then FractalMem; reverse the order for the other half. Avoid repeating exactly the same task after learning its answer.
4. Aim for at least three resumptions per method. At least one should follow a decision change, one a fresh agent session, and one a project switch. Record capture/editing time as well as time spent resuming.
5. End with a short interview and the feedback form below. Ask whether they voluntarily want to continue, rather than counting reminders as retention.

This is a directional usability pilot. A small convenience sample cannot establish market-wide superiority. Generate a separate consented held-out task set for competitive evaluation; keep it away from adapter development.

## What to record

Use [sessions.csv](sessions.csv), one row per resumption. Set `task_set` to the project or task set (A or B) so method and task effects can be separated. Keep participant details separately from IDs. Leave unavailable values blank, not zero. Use seconds for time and yes/no for outcomes. `supported_next_action` means the proposed action matches the participant's current intended action and cites the relevant current evidence. Record stale/cross-project mistakes even when the task later succeeds.

At the interview ask:

- What did you have to repeat? What was recalled incorrectly or with too much confidence?
- Could you find and correct the underlying source?
- Did a superseded decision appear as current? Did a different project's facts appear?
- How much effort did capture and maintenance take?
- Would you use this next week without a reminder? Which missing capability would change that answer?
- What would you pay for, if anything: easier capture, team handoffs, sync, hosting or support? These are research options, not available features or prices.

## Proposed go/no-go criteria

Agree on these targets before recruiting. They are product hypotheses, not external benchmarks:

- At least 80% of participants complete the first resume within 5 minutes **after prerequisites are installed**, with no maintainer intervention. Report total installation time separately.
- At least 60% voluntarily use FractalMem in week two; report exact counts and who had an eligible work session.
- Median capture/maintenance time stays below 2 minutes per resumption.
- No lost writes or destructive data bugs. Review every stale-decision or project-contamination error before broader launch.
- A majority prefers FractalMem for the tested continuity workflow and can explain why in their own words.

If setup dominates, simplify distribution first. If maintenance dominates, improve capture before adding hosted/team features. If retrieval misses current facts, fix retrieval and rerun the frozen tasks. If users prefer plain Markdown, document why and narrow the use case.

## Invitation draft

> I’m testing FractalMem, a local project-memory tool for developers who work with coding agents across multiple sessions. It keeps objectives, constraints, decisions and handoffs in files you can inspect and edit. I’m looking for a two-week trial with a few real project resumptions and a short feedback interview. You can keep your repository private and share only redacted examples. The trial needs a .NET 10 SDK, Python 3.11+ and a source checkout; it is early software. Would that fit your workflow?

This is a draft. Select recipients and confirm the message before sending it. Public access and package distribution must be ready before posting an open invitation.
