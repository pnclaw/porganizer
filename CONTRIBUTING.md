# Contributing

Thank you for your interest in contributing! Please take a moment to read this guide before you get started.

---

## Table of Contents

- [Code of Conduct](#code-of-conduct)
- [Getting Started](#getting-started)
- [Branch Strategy (Git Flow)](#branch-strategy-git-flow)
- [How to Contribute](#how-to-contribute)
- [Commit Messages](#commit-messages)
- [Pull Requests](#pull-requests)
- [Reporting Bugs](#reporting-bugs)
- [Suggesting Features](#suggesting-features)

---

## Code of Conduct

Please be respectful and constructive in all interactions. We want this to be a welcoming place for everyone.

---

## Getting Started

1. **Fork** the repository on GitHub
2. **Clone** your fork locally:
   ```bash
   git clone https://github.com/pnclaw/porganizer.git
   cd your-repo
   ```
3. Add the upstream remote:
   ```bash
   git remote add upstream https://github.com/pnclaw/porganizer.git
   ```

---

## Branch Strategy (Git Flow)

This project follows **Git Flow**. Please read this carefully before opening a PR.

| Branch | Purpose | Direct push allowed |
|---|---|---|
| `main` | Stable releases only, always tagged | No |
| `develop` | Integration branch, latest working state | No |
| `feature/*` | New features | Yes (your own branch) |
| `bugfix/*` | Non-critical bug fixes | Yes (your own branch) |
| `release/*` | Release preparation | Owner only |
| `hotfix/*` | Critical fixes on `main` | Owner only |

**As a contributor you will only ever work with `feature/*` and `bugfix/*` branches.**

All PRs from contributors must target **`develop`**, not `main`.

---

## How to Contribute

### 1. Sync your fork with upstream

Before starting any work, make sure your local `develop` is up to date:

```bash
git checkout develop
git fetch upstream
git merge upstream/develop
```

### 2. Create a branch

Branch off from `develop`:

```bash
git checkout develop
git checkout -b feature/short-description
# or
git checkout -b bugfix/what-is-fixed
```

Branch naming examples:
- `feature/user-authentication`
- `feature/export-to-pdf`
- `bugfix/null-pointer-on-startup`
- `bugfix/incorrect-date-format`

### 3. Make your changes

- Keep changes focused — one feature or fix per PR
- Write clear, readable code
- Add or update tests if applicable
- Update documentation if your change affects behaviour

### 4. Push and open a Pull Request

```bash
git push origin feature/short-description
```

Then open a PR on GitHub against the **`develop`** branch.

---

## Commit Messages

This project uses [Conventional Commits](https://www.conventionalcommits.org/).

**Format:** `type: short description`

| Type | When to use |
|---|---|
| `feat` | A new feature |
| `fix` | A bug fix |
| `docs` | Documentation changes only |
| `refactor` | Code change that neither fixes a bug nor adds a feature |
| `test` | Adding or updating tests |
| `chore` | Build process, tooling, dependencies |

**Examples:**
```
feat: add CSV export for reports
fix: correct null reference on empty input
docs: update installation instructions
chore: upgrade dependencies
```

- Use the **imperative mood** ("add" not "added")
- Keep the subject line under 72 characters
- No period at the end

---

## Pull Requests

- Fill out the PR template completely
- Link any related issues (e.g. `Closes #42`)
- Keep PRs small and focused — large PRs are harder to review
- Make sure all checks pass before requesting a review
- Be patient — the maintainer will review your PR as soon as possible

PRs that do not target `develop`, skip tests, or contain unrelated changes may be closed without merging.

---

## Reporting Bugs

Please [open an issue](../../issues/new?template=bug_report.md) and include:

- A clear title and description
- Steps to reproduce
- Expected vs. actual behaviour
- Your environment (OS, runtime version, etc.)

---

## Suggesting Features

Please [open an issue](../../issues/new?template=feature_request.md) and describe:

- The problem you are trying to solve
- Your proposed solution
- Why this would be useful to others

We discuss all feature requests before any implementation work begins.

---

*Happy contributing!*
