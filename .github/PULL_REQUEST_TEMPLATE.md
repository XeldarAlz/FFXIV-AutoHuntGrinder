## What

<!-- One or two sentences on what this PR changes. -->

## Why

<!-- The motivating problem, linked issue, or user-visible behavior this fixes. -->

Closes #

## How to test

<!--
Minimum steps a reviewer can run to verify the change. Testing usually means ticking one bill you can accept, pressing Start, and watching the plugin pick it up at the hunt board and clear at least one mark end-to-end. Make sure the dependencies listed in /ahg deps are installed first. For UI-only changes, describe what to click.
-->

## Checklist

- [ ] `dotnet build -c Release` passes
- [ ] Verified in-game on at least one hunt board in the affected expansion
- [ ] If this changes user-visible behavior, README is updated
- [ ] If this touches the automation loop, relevant `[AutoHuntGrinder]` log lines make the sequence auditable
