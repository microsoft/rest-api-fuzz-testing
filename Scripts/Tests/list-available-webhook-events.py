import argparse
import json
import os
import pathlib
import sys
sys.path.append(os.path.join(os.path.dirname(os.path.abspath(__file__)),'..', '..', 'cli'))
import raft
from raft_sdk.raft_service import RaftCLI


def compare_lists(cli):
    expected = ['JobStatus', 'BugFound']
                  
    current = cli.list_available_webhooks_events()

    if expected != current:
        raise Exception(f'Expected {expected} does not match current {current}')
    else:
        print('PASS')

if __name__ == "__main__":
    formatter = argparse.ArgumentDefaultsHelpFormatter
    parser = argparse.ArgumentParser(description = 'List webhooks', formatter_class=formatter)
    raft.add_defaults_and_secret_args(parser)

    args = parser.parse_args()

    if args.defaults_context_json:
        print(f"Loading defaults from command line: {args.defaults_context_json}")
        defaults = json.loads(args.defaults_context_json)
    else:
        with open(args.defaults_context_path, 'r') as defaults_json:
            defaults = json.load(defaults_json)

    defaults['secret'] = args.secret
    cli = RaftCLI(defaults)
    compare_lists(cli)