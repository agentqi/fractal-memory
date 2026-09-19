#!/usr/bin/env python3
"""Demonstrate capture, resume, supersession and stale-write protection using the real CLI."""
import argparse
from contextlib import nullcontext
import json
import os
from pathlib import Path
import shutil
import subprocess
import tempfile
from time import perf_counter


def main():
    root = Path(__file__).resolve().parent.parent
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--cli', default=str(root / '.tools/fractalmem' / ('fm.exe' if os.name == 'nt' else 'fm')))
    parser.add_argument('--workspace', type=Path, help='Keep demo files in this new directory; otherwise use a temporary workspace.')
    args = parser.parse_args()
    executable = shutil.which(args.cli) or str(Path(args.cli).expanduser().resolve())
    if not Path(executable).is_file():
        parser.error('CLI not found. Run scripts/install-local.py or pass --cli /path/to/fm.')
    if args.workspace:
        workspace = args.workspace.expanduser().resolve()
        if workspace.exists():
            parser.error('--workspace must be a new directory.')
        workspace.mkdir(parents=True)
        manager = nullcontext(str(workspace))
    else:
        manager = tempfile.TemporaryDirectory(prefix='fractalmem-demo-')
    with manager as directory:
        def run(*arguments, expected=0):
            completed = subprocess.run([executable, *arguments, '--json'], cwd=directory,
                                       capture_output=True, text=True, timeout=30)
            if completed.returncode != expected:
                raise RuntimeError(f'{arguments[:2]}: {completed.stderr or completed.stdout}')
            return json.loads(completed.stdout) if expected == 0 else completed

        node = 'projects/checkout'
        started = perf_counter()
        run('init')
        run('node', 'create', node)
        for section, text in [('Current Objective', 'Ship the checkout pilot.'),
                              ('Active Constraints', 'Keep customer payment details out of project notes.'),
                              ('Next Best Actions', 'Verify the retry behavior before inviting testers.')]:
            document = run('read', node)
            run('update', node, section, text, '--expected-hash', document['hash'])
        document = run('read', node, '--file', 'decisions')
        first = run('append', node, 'decisions', 'Allow three automatic retries.', '--expected-hash', document['hash'])
        run('handoff', 'create', node)
        resumed = run('resume', node)
        assert resumed['comparisonAvailable'] and not resumed['changedFiles']
        assert 'Ship the checkout pilot.' in resumed['context']['text']
        print(f'1. Captured project state and resumed it in a new process ({perf_counter() - started:.2f}s).')

        document = run('read', node, '--file', 'decisions')
        run('append', node, 'decisions', 'Allow one automatic retry.', '--supersedes', first['decisionId'],
            '--expected-hash', document['hash'])
        resumed = run('resume', node)
        assert resumed['changedFiles'], 'The decision update must appear as a change since the handoff.'
        context = resumed['context']['text']
        assert 'Allow one automatic retry.' in context and 'Allow three automatic retries.' not in context
        history = run('read', node, '--file', 'decisions')['content']
        assert 'Allow three automatic retries.' in history
        print('2. Replaced a decision: current context uses one retry; the previous decision remains in history.')

        old = run('read', node)
        run('update', node, 'Next Best Actions', 'Run the checkout pilot with one tester.', '--expected-hash', old['hash'])
        current = run('read', node)
        failure = run('update', node, 'Next Best Actions', 'This stale edit must fail.', '--expected-hash', old['hash'], expected=2)
        assert 'changed since' in failure.stderr and run('read', node)['hash'] == current['hash']
        print('3. Rejected a stale update and preserved the newer state.')
        final = run('resume', node)
        print('\nCurrent source-linked context:\n' + final['context']['text'])
        print('\nDemo passed. ' + (f'Inspect the files in {directory}' if args.workspace else 'Temporary demo files removed on exit.'))


if __name__ == '__main__':
    main()
