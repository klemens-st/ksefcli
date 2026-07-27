#!/usr/bin/env python3
"""PreToolUse guard: refuse a `git commit` whose message is not a Conventional Commit.

The rules live in .githooks/commit-msg and are not duplicated here -- this only digs the
message out of a shell command line and hands it to that script. Two reasons it exists
alongside the git hook rather than instead of it:

  * .githooks/commit-msg needs `make install-hooks` (core.hooksPath is not versioned), so a
    fresh clone is unprotected until someone remembers. .claude/settings.json is committed.
  * `git commit --no-verify` skips the git hook. This runs before the shell does, so it
    doesn't.

Best-effort by design: anything it cannot confidently parse is allowed through, because the
git hook is the real authority and a false deny is worse than a miss. Everything is decided
from the tokens *after* the `commit` verb -- scanning the raw string instead would reject
`grep -n x && git commit -m 'fix: y'` over an unrelated -n, and would mistake the body of an
unrelated heredoc for the commit message.
"""
import json
import os
import re
import shlex
import subprocess
import sys
import tempfile
from pathlib import Path

# .claude/hooks/commit-msg-guard.py -> repo root. Resolved from __file__ rather than cwd or
# $CLAUDE_PROJECT_DIR so it still finds its own repo's validator inside .claude/worktrees/.
VALIDATOR = Path(__file__).resolve().parents[2] / ".githooks" / "commit-msg"

# `-m "$(cat <<'EOF' ... EOF)"` -- the form Claude Code emits for any multi-line message.
# shlex keeps that whole substitution as the single -m value, so this only ever runs against
# text the user meant as the message.
HEREDOC = re.compile(r"<<-?\s*(['\"]?)([A-Za-z_][A-Za-z0-9_]*)\1")


def allow():
    sys.exit(0)


def deny(reason):
    json.dump({"hookSpecificOutput": {
        "hookEventName": "PreToolUse",
        "permissionDecision": "deny",
        "permissionDecisionReason": reason,
    }}, sys.stdout)
    sys.exit(0)


def unwrap_heredoc(value):
    """Body of a heredoc embedded in a -m value, or the value unchanged."""
    match = HEREDOC.search(value)
    if not match:
        return value
    body = []
    for line in value[match.end():].split("\n")[1:]:
        # <<- strips leading tabs from the terminator; plain << does not.
        if line.strip() == match.group(2):
            return "\n".join(body)
        body.append(line)
    return value  # unterminated -- treat it as a literal message rather than guessing


# Git's own options that consume the following token, so `git -C dir commit` finds `commit`
# rather than mistaking `dir` for the verb.
GIT_OPTS_WITH_VALUE = ("-C", "-c", "--git-dir", "--work-tree", "--namespace", "--exec-path")

# Where one command ends and the next begins. `git add -A && git commit -m ...` has to be
# scanned past the `add`, and the -m/-n scan must not run off into whatever follows.
SEPARATORS = ("&&", "||", ";", "|", "&")


def commit_args(command):
    """Tokens following the `commit` verb of a `git commit`, or None if there isn't one."""
    try:
        args = shlex.split(command)
    except ValueError:
        return None  # unbalanced quotes; let the shell complain about it
    for i, arg in enumerate(args):
        # `git`, `/usr/bin/git`, ... match on the basename.
        if os.path.basename(arg) != "git":
            continue
        j = i + 1
        while j < len(args):
            if args[j] in GIT_OPTS_WITH_VALUE:
                j += 2
            elif args[j].startswith("-"):
                j += 1
            else:
                break
        if j >= len(args) or args[j] != "commit":
            continue  # some other git subcommand; there may still be a commit further along
        rest = args[j + 1:]
        for k, token in enumerate(rest):
            if token in SEPARATORS:
                return rest[:k]
        return rest
    return None


def message_from(args):
    """Concatenate -m/--message values the way git does: joined by a blank line."""
    parts = []
    i = 0
    while i < len(args):
        arg = args[i]
        if arg in ("-m", "--message") and i + 1 < len(args):
            parts.append(args[i + 1])
            i += 2
            continue
        if arg.startswith("--message="):
            parts.append(arg[len("--message="):])
        elif len(arg) > 2 and arg.startswith("-m") and not arg.startswith("--"):
            parts.append(arg[2:])
        i += 1
    return "\n\n".join(unwrap_heredoc(part) for part in parts) if parts else None


def main():
    try:
        command = json.load(sys.stdin)["tool_input"]["command"]
    except (json.JSONDecodeError, KeyError, TypeError):
        allow()

    args = commit_args(command)
    if args is None:
        allow()

    if "--no-verify" in args or "-n" in args:
        deny("Refusing `git commit --no-verify`: the commit-msg hook is the enforcement "
             "point for this repository's Conventional Commits rule, not an obstacle to "
             "route around. Fix the message instead.")

    message = message_from(args)
    if not message:
        allow()  # -F, -C, --amend --no-edit, or an editor commit; the git hook still runs

    if not VALIDATOR.is_file():
        allow()

    with tempfile.NamedTemporaryFile("w", suffix=".gitmsg", delete=False) as handle:
        handle.write(message if message.endswith("\n") else message + "\n")
        path = handle.name
    try:
        result = subprocess.run([str(VALIDATOR), path], capture_output=True, text=True)
    finally:
        os.unlink(path)

    if result.returncode != 0:
        deny((result.stderr or result.stdout).strip() or
             "The commit message is not a Conventional Commit.")
    allow()


if __name__ == "__main__":
    main()
