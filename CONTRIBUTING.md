# Contributing to Copilot Voice

This document defines the development workflow for both human developers and AI agents (GitHub Copilot).

## Workflow Overview

```
Issue (backlog)
    │
    ▼
Assign + set "In Progress"
    │
    ▼
Create branch: issue-<N>-<short-name>
    │
    ▼
Implement with frequent commits
    │
    ▼
Push branch + create PR (link issue)
    │
    ▼
Copilot Review + Security scan
    │
    ▼
Address review comments
    │
    ▼
Merge → Issue auto-closed
```

## 1. Picking Up Work

Before starting any work:

1. **Check the issue** — read the full description, acceptance criteria, and linked docs (README.md, WIREFRAMES.md)
2. **Assign yourself** — set the issue assignee so others can see it's being worked on
3. **Update status** — move the issue to "In Progress" in the project board
4. **Check dependencies** — make sure dependent issues are already merged

## 2. Branching

Always work on a feature branch, never commit directly to `main`.

```bash
git checkout main && git pull
git checkout -b issue-<N>-<short-name>

# Examples:
git checkout -b issue-3-speech-recognizer
git checkout -b issue-11-tts-engine
```

## 3. Committing

Make **frequent, small commits** — not one giant commit per issue.

### Commit message format

```
<type>(<scope>): <description> (#<issue>)

# Examples:
feat(audio): add PushToTalkRecognizer skeleton with events (#3)
fix(config): handle missing AZURE_SPEECH_KEY gracefully (#2)
test(config): add unit tests for auth resolution priority (#9)
```

**Types**: `feat`, `fix`, `refactor`, `test`, `docs`, `ci`, `chore`

### Commit cadence

Each logical step should be its own commit:
- Adding a new class → commit
- Adding method implementations → commit
- Adding error handling → commit
- Fixing a build error → commit

## 4. Building and Verifying

After every commit, verify the build passes:

```bash
dotnet build
dotnet test --filter 'Category!=Integration'
```

Fix build errors before moving to the next step. Never push broken code.

## 5. Creating Pull Requests

Push your branch and create a PR:

```bash
git push -u origin issue-<N>-<short-name>

gh pr create \
  --title "feat: <description> (#<N>)" \
  --body "## Summary
<What was implemented and why>

## Changes
- <file1>: <what changed>

## Testing
- [ ] \`dotnet build\` passes

## Acceptance Criteria (from issue)
- [ ] <criterion 1>
- [ ] <criterion 2>

Closes #<N>"
```

### PR requirements
- Title references the issue number
- Body explains what was done and how
- Acceptance criteria from the issue are listed
- `Closes #N` to auto-close the issue on merge
- CI must pass

## 6. Code Review

### Automated reviews
Every PR is automatically reviewed by:
- **GitHub Copilot** — code quality, logic errors, best practices
- **Security scanning** — dependency vulnerabilities, secret detection
- **CI checks** — build + test on macOS, Linux, Windows

### Handling review comments

1. **If the comment makes sense** — fix it, commit, push
2. **If you disagree** — reply with a clear explanation and resolve the thread
3. **Never ignore comments** — every thread must be either fixed or resolved with a reply

## 7. Merging

Once all checks pass and reviews are approved:

```bash
gh pr merge <N> --squash --delete-branch
```

- Use **squash merge** to keep main history clean
- Delete the branch after merge

## 8. Agent-Specific Guidelines

When an AI agent works on an issue:

### Before starting
- Read all linked docs (README.md, WIREFRAMES.md, CONTRIBUTING.md)
- Check what code already exists in relevant directories
- Verify dependencies are met

### While working
- Create a **draft PR early** so progress is visible
- Commit frequently (every logical step)
- Run `dotnet build` after each commit
- Don't modify files outside the scope of the issue

### After completing
- Mark PR as ready for review
- Verify CI passes
- Check acceptance criteria from the issue

### Visibility
- The issue should show work is happening (assignee set, branch linked)
- Commits visible on the branch in real-time
- Draft PR created early:
  ```bash
  gh pr create --draft --title "WIP: feat: <description> (#N)" --body "..."
  ```

## 9. Repository Configuration

### Branch protection (main)
- Require PR before merging (no direct pushes)
- Require CI status checks to pass
- Require Copilot code review
- Auto-delete branches after merge

### Security
- Dependabot alerts: enabled
- Dependabot security updates: enabled
- Secret scanning: enabled
- Copilot security analysis: enabled

## 10. Reference

| Resource | Location |
|----------|----------|
| Architecture | [README.md](README.md) |
| UI Design | [WIREFRAMES.md](WIREFRAMES.md) |
| Project Board | [GitHub Project #5](https://github.com/users/vbomfim/projects/5) |
| Issues | [Issues](https://github.com/vbomfim/copilot-voice/issues) |
| CI/CD | [Actions](https://github.com/vbomfim/copilot-voice/actions) |
