You are helping the user finish a feature branch and raise a PR to `develop`.

Follow these steps in order:

1. Run `git branch --show-current` to get the current branch name. If it is `develop` or `main`, stop and tell the user they are not on a feature branch.

2. Run `git status --short` to check for uncommitted changes. If there are any, stop and tell the user to commit or stash them first.

3. Read `CHANGELOG.md` and check that it contains an entry that appears to relate to this branch's work. If you cannot find one, write an appropriate entry yourself based on the commits on this branch, then commit it with the message `docs: update changelog for <branch-name>` before continuing.

4. Run `git fetch --all` to ensure all remote refs are up to date, then run `git log origin/develop..HEAD --oneline` to list commits on this branch. If there are no commits ahead of develop, stop and say so.

5. Push the branch: run `git push -u origin HEAD`. If it fails, report the error and stop.

6. Create the PR using `gh pr create --base develop` with:
   - A concise title derived from the branch name (strip the `feature/` prefix, replace hyphens with spaces, title-case it)
   - A body using this template, populated from the commit list and your understanding of the changes:

```
## Summary
<2-4 bullet points describing what this PR does>

## Test plan
- [ ] <key things to verify>

🤖 Generated with [Claude Code](https://claude.com/claude-code)
```

7. Return the PR URL to the user.
