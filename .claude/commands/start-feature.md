You are helping the user start a new feature branch from a topic they provide: $ARGUMENTS

Follow these steps in order:

1. Derive a branch name:
   - If no argument was given (empty or whitespace), generate a random slug of exactly three common English words joined by hyphens (e.g. `silver-fox-rain`). Pick words randomly — do not reuse the same combination.
   - Otherwise, slugify the argument: lowercase, replace spaces/punctuation with hyphens, collapse repeated hyphens, strip leading/trailing hyphens.
   - Prefix with `feature/`.
   - Proceed immediately — do NOT ask the user to confirm the name.

2. Safety check — before switching branches:
   - Run `git status --short`. If there are any uncommitted changes, stop and tell the user to commit or stash them first.
   - Run `git log origin/develop..HEAD --oneline`. If there are unpushed commits on the current branch, stop and warn the user.

3. Reset local develop to match remote:
   - Run `git checkout develop`
   - Run `git reset --hard origin/develop`

4. Create and switch to the new branch:
   - Run `git checkout -b <branch-name>`

5. Confirm success by printing the current branch name.
