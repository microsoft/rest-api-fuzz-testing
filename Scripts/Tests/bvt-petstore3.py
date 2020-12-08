# Copyright (c) Microsoft Corporation.
# Licensed under the MIT License.

import argparse
import json
import os
import pathlib
import sys
import requests
import time

cli_path = os.path.join(os.path.dirname(os.path.abspath(__file__)),'..', '..', 'cli')
sys.path.append(cli_path)
import raft
from raft_sdk.raft_service import RaftCLI
from raft_sdk.raft_common import RaftDefinitions, RaftApiException
from raft_sdk.raft_deploy import azure_function_keys

def webhooks_test_url(subscription_id, resource_group, function):
    keys = azure_function_keys(subscription_id, resource_group, function, "webhooks-trigger-test")
    return f"https://{function}.azurewebsites.net/api/test?{keys['default']}"

def webhook_triggers_results(job_id, test_url):
    webhook_triggers_response = requests.get(test_url + "&jobId=" + job_id)
    if webhook_triggers_response.ok:
        triggers = json.loads(webhook_triggers_response.text)
        for t in triggers:
            j = json.loads(t)
            yield j[0]
    else:
        raise Exception(webhook_triggers_response.text)

def time_span(t_start, t_end):
    return time.strftime("%H:%M:%S", time.gmtime(t_end - t_start))

def bvt(cli, definitions, subs):
    print('Getting available wehook events')
    webhook_events = cli.list_available_webhooks_events()
    try:
        test_url = webhooks_test_url(definitions.subscription, definitions.resource_group, definitions.test_infra)
        for event in webhook_events:
            print(f'Setting webhook for {event}')
            try:
                compile_webhook = cli.set_webhooks_subscription('petstore3-compile', event, test_url)
                print(f'Set webhooks {compile_webhook}')
            except RaftApiException as ex:
                if ex.status_code == 504:
                    print(f'Proceeding even though got gateway timeout error when setting webhook {ex}')
                else:
                    raise ex

            try:
                fuzz_webhook = cli.set_webhooks_subscription('petstore3-fuzz', event, test_url)
                print(f'Set webhooks {fuzz_webhook}')
            except RaftApiException as ex:
                if ex.status_code == 504:
                    print(f'Proceeding even though got gateway timeout error when setting webhook {ex}')
                else:
                    raise ex

        added_compile = cli.list_webhooks('petstore3-compile', event)
        if len(added_compile) == 0:
            raise Exception('Expected petstore3-compile webhooks not to be empty after creation')

        added_fuzz = cli.list_webhooks('petstore3-fuzz', event)
        if len(added_fuzz) == 0:
            raise Exception('Expected petstore3-fuzz webhooks not to be empty after creation')

        t_pre_compile = time.time()

        print('Compile')
        compile_config_path = os.path.abspath(os.path.join(cli_path, 'samples', 'restler', 'self-contained', 'swagger-petstore3', 'compile.json'))

        compile_config = raft.RaftJobConfig(file_path=compile_config_path, substitutions=subs)
        compile_job = cli.new_job(compile_config)
        cli.poll(compile_job['jobId'], 10)

        #calculate compile duration
        t_pre_fuzz = time.time()
        timespan_pre_fuzz = time_span(t_pre_compile, t_pre_fuzz)
        after_compile_pre_fuzz = cli.list_jobs(timespan_pre_fuzz)

        n = 0
        for x in after_compile_pre_fuzz:
            if x['jobId'] == compile_job['jobId']:
                n += 1
            if x['agentName'] == compile_job['jobId']:
                if x['state'] != 'Completed':
                    raise Exception('Expected job to be in completed state when retrieved job list.'
                                    f'{after_compile_pre_fuzz}')

        if n != 2:
            raise Exception('Expected 2 after compile job step'
                            f' for job {compile_job["jobId"]}'
                            f' got {n}'
                            f' {after_compile_pre_fuzz}')

        print('Fuzz')
        fuzz_config_path = os.path.abspath(os.path.join(cli_path, 'samples', 'restler', 'self-contained', 'swagger-petstore3', 'fuzz.json'))
        subs['{compile.jobId}'] = compile_job['jobId']
        fuzz_config = raft.RaftJobConfig(file_path=fuzz_config_path, substitutions=subs)
        fuzz_job = cli.new_job(fuzz_config)
        cli.poll(fuzz_job['jobId'], 10)

        #calculate fuzz duration
        timespan_post_fuzz = time_span(t_pre_fuzz, time.time())
        after_fuzz = cli.list_jobs(timespan_post_fuzz)

        #validate list jobs
        m = 0
        for x in after_fuzz:
            if x['jobId'] == fuzz_job['jobId']:
                m += 1

            if x['agentName'] == fuzz_job['jobId']:
                if x['state'] != 'Completed':
                    raise Exception('Expected job to be in completed state when retrieved job list.'
                                    f'{after_fuzz}')
        
        if m != 3:
            raise Exception('Expected 3 after compile job step'
                            f' for job {fuzz_job["jobId"]}'
                            f' got {m}'
                            f' {after_fuzz}')


        print('Validating webhook posted triggers')
        compile_webhook_triggers = webhook_triggers_results(compile_job['jobId'], test_url)
        for r in compile_webhook_triggers:
            if r['EventType'] == 'BugFound':
                raise Exception(f'Compile step produced BugFound event')

        fuzz_webhook_triggers = webhook_triggers_results(fuzz_job['jobId'], test_url)

        bug_found_events = []
        job_status_events = []

        for r in fuzz_webhook_triggers:
            if r['EventType'] == 'BugFound':
                bug_found_events.append(r)
            elif r['EventType'] == 'JobStatus':
                job_status_events.append(r)
            else:
                raise Exception(f'Unhandled webhook trigger event type {r["EventType"]} : {r}')

        if len(job_status_events) == 0:
            raise Exception('Job did not post any job status events webhooks')

        print('Validating that bugs posted events matches total bugs found in job status')
        total_bugs_found = 0
        for r in job_status_events:
            if r['Data']['State'] == 'Completed' and r['Data']['AgentName'] != r['Data']['JobId'] and r['Data']['Tool'] == 'RESTler':
                total_bugs_found += r['Data']['Metrics']['TotalBugBucketsCount']

        print(f'Total bugs found: {total_bugs_found}')
        print(f'Number of Bug found events: {len(bug_found_events)}')
        if total_bugs_found != len(bug_found_events):
            raise Exception(f"Total bugs found does not match number of bug found webhook triggered events {total_bugs_found} and {len(bug_found_events)}")

    except Exception as ex:
        print(f"FAIL: {ex}")
        raise ex
    finally:
        for event in webhook_events:
            print(f'Cleaning up webhook {event}')
            cli.delete_webhook('petstore-compile', event)
            cli.delete_webhook('petstore-fuzz', event)

        deleted_compile = cli.list_webhooks('petstore-compile', event)
        if len(deleted_compile) > 0:
            raise Exception('Expected petstore-compile webhooks to be empty after deletion, instead got %s', deleted_compile)

        deleted_fuzz = cli.list_webhooks('petstore-fuzz', event)
        if len(deleted_fuzz) > 0:
            raise Exception('Expected petstore-fuzz webhooks to be empty after deletion, instead got %s', deleted_compile)

if __name__ == "__main__":
    formatter = argparse.ArgumentDefaultsHelpFormatter
    parser = argparse.ArgumentParser(description='bvt', formatter_class=formatter)
    raft.add_defaults_and_secret_args(parser)
    parser.add_argument('--build', required=True)
    args = parser.parse_args()

    if args.defaults_context_json:
        print(f"Loading defaults from command line: {args.defaults_context_json}")
        defaults = json.loads(args.defaults_context_json)
    else:
        with open(args.defaults_context_path, 'r') as defaults_json:
            defaults = json.load(defaults_json)

    definitions = RaftDefinitions(defaults)
    defaults['secret'] = args.secret
    cli = RaftCLI(defaults)
    subs = {
        "{build-url}" : os.environ.get('SYSTEM_COLLECTIONURI'),
        "{build-id}" : os.environ.get('BUILD_BUILDID'),
        "{ci-run}" : args.build.replace('.', '-')
    }
    print(f"SUBS: {subs}")
    bvt(cli, definitions, subs)