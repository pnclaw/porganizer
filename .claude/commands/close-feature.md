You are helping the user clean up after a feature branch has been merged on GitHub.

Follow these steps in order:

1. Run `git branch --show-current` to get the current branch name. If it is `develop` or `main`, stop and tell the user they are not on a feature branch.

2. Run `gh pr list --head <branch-name> --state merged --json number,mergedAt,baseRefName` to check if a PR for this branch has been merged. If no merged PR is found, stop and warn the user — do not delete anything. If a merged PR is found but its base branch is not `develop`, note this to the user and ask them to confirm before continuing.

3. Switch to develop: `git checkout develop`

4. Pull latest: `git pull origin develop`

5. Delete the local branch: `git branch -D <branch-name>`

6. Delete the remote branch: `git push origin --delete <branch-name>`

7. Confirm to the user that the branch has been cleaned up and they are now on `develop` at the latest commit.
