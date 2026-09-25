# Positioning and launch plan

Status: draft launch materials and experiment plan. No public release, outreach or customer results are implied.

## Positioning

**Project memory you can inspect, update, and resume.**

FractalMem keeps a project's current objective, constraints, decisions and next actions in local files. Developers and coding agents can read the same sources, update them with stale-write checks, and resume from a handoff through a CLI or MCP server.

Start with individual developers doing multi-session agent-assisted work. The useful distinction to test is explicit current state and decision maintenance, supported by inspectable sources. Local Markdown and MCP alone are not unique; Basic Memory already offers those capabilities. The [comparison protocol](https://github.com/agentqi/fractal-memory-bench/blob/main/docs/comparison-protocol.md) requires measured evidence before a competitive claim.

## Demo and landing-page copy

Headline: **Pick up the project without reconstructing its decisions.**

Body: Keep the objective, constraints and next action in local project memory. See the source behind a resumed task. Replace an outdated decision without losing its history. Use the CLI directly or connect your coding agent through MCP.

Primary action for the private pilot: **Try the local demo** → [quickstart](quickstart.md).

Secondary action: **See how memory is maintained** → [workflows](memory-workflows.md).

Suggested 90-second demo: run `scripts/demo.py`, show current state in a fresh process, change the retry decision, resume again, inspect history, and show the rejected stale write. Explain that this demonstrates the memory tools; it is not an autonomous-agent quality benchmark. Use real screen recordings from this script rather than simulated output.

## Claims policy

| Can demonstrate now | Needs further evidence or implementation |
| --- | --- |
| Local readable sources, CLI/MCP access | Faster real developer task completion |
| Explicit active/superseded decisions | Fewer model hallucinations across clients |
| Hash-checked updates and retained history | Better recall or lower total cost than competitors |
| Source-linked context and handoff change detection | Automatic capture, team permissions, hosted sync or audit service |
| Apache-2.0 product license | Revenue, adoption or customer satisfaction claims |

The corrected development benchmark shows tradeoffs: source selection can be more focused while phrase recall is lower than a simple file baseline. Do not call FractalMem “best,” “most accurate,” “zero hallucinations,” or “offline AI” on that evidence. Link the actual versioned protocol, results and limitations whenever quoting a metric.

## Distribution sequence

1. Finish both PRs, validate clean installs on supported operating systems and resolve release infrastructure prerequisites. Keep Apache-2.0 in LICENSE, NOTICE and package metadata.
2. Recruit the [small pilot](pilot/README.md) through existing developer relationships, with specific recipients and permission to contact them.
3. Fix observed setup, capture and retrieval friction. Publish consented case studies with exact workflows, failures and maintenance effort.
4. Prepare a public release: confirm source/data visibility, package credentials, versioned artifacts and working install instructions. Publish only after release approval and a clean-install check.
5. Share one useful workflow at a time: a technical walkthrough of decision supersession; a short resume demo; then a reproducible comparison with clearly named versions and scope. Match community rules and disclose that AgentQI maintains the product.

Track the funnel with counts: demo visitor → successful install → first supported resume → voluntary week-two use → feedback/referral. Stars and impressions alone do not establish value. Start with participant-reported data; hosted analytics is not required for this pilot.

## Business model experiments

Keep the core [Apache-2.0](https://www.apache.org/licenses/LICENSE-2.0). Test willingness to pay for setup help or support first. Consider optional team governance, managed sync/backup and hosting only after repeated demand and a clear operating/security model. Those services are future hypotheses, not current capabilities. Apache-2.0 permits competitors to build on the core; the commercial case should rest on useful operation, service and trust rather than implied exclusivity.

## Launch inputs still needed

Pilot recipients; consented examples; selected public distribution channel and repository visibility; a budget and endpoints for any hosted model comparison. These choices should follow the concrete materials and pilot evidence above.
