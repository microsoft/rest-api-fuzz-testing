# Copyright (c) Microsoft Corporation.
# Licensed under the MIT License.
import argparse
import json
import os
import uuid
import textwrap

import raft_sdk.raft_common
from raft_sdk.raft_service import RaftCLI, RaftJobConfig
from raft_sdk.raft_deploy import RaftServiceCLI

script_dir = os.path.dirname(os.path.abspath(__file__))

fresh_defaults = json.dumps(
    {
        "subscription": "",
        "deploymentName": "",
        "region": "",
        "metricsOptIn": True,
        "useAppInsights": True,
        "registry": "mcr.microsoft.com"
    }, indent=4)

defaults_help = '''
subscription - The Azure Subscription ID to which RAFT is deployed

deploymentName - RAFT deployment name
    deployment name requirements:
        - only letters or numbers
        - at most 20 characters long
        - no capital letters
        - no dashes

region - Region to deploy RAFT (e.g. westus2)
    https://docs.microsoft.com/en-us/azure/container-instances/container-instances-region-availability
    for to pick the optimal region for your deployment.
    All jobs will be deployed by default in the same
    region as your service deployment

metricsOptIn - allow Microsoft collect anonymized metrics from the deployment.

useAppInsights - deploy AppInsights and use it to write all service logs

registry - registry which stores service images.
    Default: mcr.microsoft.com

-------------------------
To apply any changes made to the defaults.json file,
please run 'raft.py service deploy'
-------------------------
'''


def run(args):
    def validate(defaults):
        s = defaults.get('subscription')
        d = defaults.get('deploymentName')
        r = defaults.get('region')
        return (s and d and r)

    defaults_path = args['defaults_context_path']
    defaults_json = args['defaults_context_json']

    if defaults_json:
        print(f"Loading defaults from command line: {defaults_json}")
        defaults = json.loads(defaults_json)
        if not validate(defaults):
            print(defaults_help)
            return
    # check if defaults.json is set in the context and it exists
    elif os.path.isfile(defaults_path):
        with open(defaults_path, 'r') as d:
            defaults = json.load(d)
            if not validate(defaults):
                print(defaults_help)
                return
    else:
        with open(defaults_path, 'w') as d:
            d.write(fresh_defaults)
        print(defaults_help)
        return

    defaults['secret'] = args.get('secret')
    if 'metricsOptIn' not in defaults:
        defaults['metricsOptIn'] = True

    if defaults.get('useAppInsights') is None:
        defaults['useAppInsights'] = True

    cli_action = args.get('logout')
    service_action = args.get('service-action')
    job_action = args.get('job-action')
    webhook_action = args.get('webhook-action')

    if cli_action == 'logout':
        raft_sdk.raft_common.delete_token_cache()
    elif service_action:
        service_cli = RaftServiceCLI(
                            defaults,
                            defaults_path,
                            args.get('secret'))
        if service_action == 'restart':
            service_cli.restart()
        elif service_action == 'info':
            info = service_cli.service_info()
            print(info)
        elif service_action == 'deploy':
            skip_sp_deployment = args.get('skip_sp_deployment')
            service_cli.deploy(
                args['sku'], skip_sp_deployment and skip_sp_deployment is True)
        elif service_action == 'upload-tools':
            utils_file_share = f'{uuid.uuid4()}'
            service_cli.upload_utils(
                utils_file_share, args.get('custom_tools_path'))
            service_cli.restart()
        else:
            raise Exception(f'Unhandled service argument: {service_action}')

    elif job_action:
        cli = RaftCLI()
        if job_action == 'create':
            json_config_path = args.get('file')
            if json_config_path is None:
                ArgumentRequired('--file')

            substitutionDictionary = {}
            substitutionParameter = args.get('substitute')
            if substitutionParameter:
                substitutionDictionary = json.loads(substitutionParameter)

            job_config = RaftJobConfig(file_path=json_config_path,
                                       substitutions=substitutionDictionary)

            print(job_config.config)
            duration = args.get('duration')
            if duration:
                job_config.config['duration'] = duration

            metadata = args.get('metadata')
            if metadata:
                job_config.config['metadata'] = json.loads(metadata)

            newJob = cli.new_job(job_config, args.get('region'))
            poll_interval = args.get('poll')
            if poll_interval:
                print(newJob)
                cli.poll(newJob['jobId'], poll_interval)
            else:
                print(newJob)

        elif job_action == 'status':
            job_id = args.get('job_id')
            if job_id is None:
                ArgumentRequired('--job-id')
            job_status = cli.job_status(job_id)
            poll_interval = args.get('poll')
            cli.print_status(job_status)
            if poll_interval:
                cli.poll(job_id, poll_interval)

        elif job_action == 'list':
            jobs_list = cli.list_jobs(args['look_back_hours'])
            sorted = {}
            for job in jobs_list:
                if sorted.get(job['jobId']):
                    sorted[job['jobId']].append(job)
                else:
                    sorted[job['jobId']] = [job]

            for s in sorted:
                cli.print_status(sorted[s])
                print()

            print(f"Total number of jobs: {len(sorted)}")

        elif job_action == 'update':
            json_config_path = args.get('file')
            if json_config_path is None:
                ArgumentRequired('--file')

            substitutionDictionary = {}
            substitutionParameter = args.get('substitute')
            if substitutionParameter:
                substitutionDictionary = json.loads(substitutionParameter)

            job_update = cli.update_job(
                args.get('job_id'),
                RaftJobConfig(file_path=json_config_path,
                              substitutions=substitutionDictionary))
            print(job_update)

        elif job_action == 'results':
            job_id = args.get('job_id')
            if job_id is None:
                ArgumentRequired('--job-id')
            url = cli.result_url(job_id)
            print(url)

        elif job_action == 'delete':
            job_id = args.get('job_id')
            if job_id is None:
                ArgumentRequired('--job-id')
            job_delete = cli.delete_job(job_id)
            print(job_delete)

    elif webhook_action:
        cli = RaftCLI()
        if webhook_action == 'events':
            webhook_events = cli.list_available_webhooks_events()
            print(webhook_events)

        elif webhook_action == 'create':
            name_parameter = args.get('name')
            if name_parameter is None:
                ArgumentRequired('--name')

            event_parameter = args.get('event')
            if event_parameter is None:
                ArgumentRequired('--event')

            uri_parameter = args.get('url')
            if uri_parameter is None:
                ArgumentRequired('--url')

            webhook_create_or_update = cli.set_webhooks_subscription(
                name_parameter, event_parameter, uri_parameter)
            print(webhook_create_or_update)

        elif webhook_action == 'test':
            name_parameter = args.get('name')
            if name_parameter is None:
                ArgumentRequired('--name')

            event_parameter = args.get('event')
            if event_parameter is None:
                ArgumentRequired('--event')

            webhook_test = cli.test_webhook(name_parameter, event_parameter)
            print(webhook_test)

        elif webhook_action == 'delete':
            name_parameter = args.get('name')
            if name_parameter is None:
                ArgumentRequired('--name')

            event_parameter = args.get('event')
            if event_parameter is None:
                ArgumentRequired('--event')

            webhook_delete = cli.delete_webhook(name_parameter,
                                                event_parameter)
            print(webhook_delete)

        elif webhook_action == 'list':
            name_parameter = args.get('name')
            if name_parameter is None:
                ArgumentRequired('--name')

            # the event name is not required.
            # If not supplied all events will be listed.
            event_parameter = args.get('event')

            webhooks_list = cli.list_webhooks(name_parameter, event_parameter)
            print(webhooks_list)

        else:
            raise Exception('Expected arguments could not be found in args')


def ArgumentRequired(name):
    print(f'The {name} parameter is required')
    quit()


def add_defaults_and_secret_args(parser):
    parser.add_argument(
        "--defaults-context-path",
        default=os.path.join(script_dir, 'defaults.json'),
        help="Path to the defaults.json",
        required=False
    )
    parser.add_argument(
        "--defaults-context-json",
        default=None,
        help="JSON blob containing service configuration",
        required=False
    )
    parser.add_argument('--secret', required=False)
    parser.add_argument('--skip-sp-deployment',
                        required=False,
                        action='store_true')


# pip install -r requirements.txt
def main():
    parser = argparse.ArgumentParser(
        description='RAFT CLI',
        formatter_class=argparse.RawTextHelpFormatter)
    sub_parser = parser.add_subparsers()

    cli_parser = sub_parser.add_parser(
        'cli',
        formatter_class=argparse.RawTextHelpFormatter)

    cli_parser.add_argument('logout',
                            help=textwrap.dedent('''\
Clears the cache so re-authentication will be needed to use the CLI again.'''))

    service_parser = sub_parser.add_parser(
        'service',
        formatter_class=argparse.RawTextHelpFormatter)
    service_parser.add_argument(
        'service-action',
        choices=['deploy', 'restart', 'info', 'upload-tools'],
        help=textwrap.dedent('''\
deploy       - Deploys the service

restart      - Restarts the service updating the
               docker containers if a new one is available

info         - Show the version of the service and the last time it was started

upload-tools - Uploads the tool definitions to the service
'''))

    allowed_skus = [
        'B1', 'B2', 'B3', 'D1', 'F1',
        'I1', 'I2', 'I3', 'P1V2', 'P2V2', 'P3V2',
        'PC2', 'PC3', 'PC4', 'S1', 'S2', 'S3']

    service_parser.add_argument(
        '--sku', default='B2',
        choices=allowed_skus, required=False, help='Default value: B2')

    service_parser.add_argument(
        '--custom-tools-path', default=None, required=False)

    # Add the positional argument.
    job_parser = sub_parser.add_parser(
        'job',
        formatter_class=argparse.RawTextHelpFormatter)

    job_parser.add_argument(
        'job-action',
        choices=['create', 'update', 'delete', 'status', 'list', 'results'],
        help=textwrap.dedent('''\
create  - Create a new job
        --file is required

update  - Update an existing job
          --file is required
          --job-id is required

delete  - Delete a job
          --job-id is required

status  - Get the status of a job
          --job-id is required

list    - Get a list of jobs
          Use --look-back-hours to specify how far back to look
          the default is 24 hours

results - Get the Uri of the job results
            '''))

    # Add the flag arguments
    # This is a list of all the possible flags that can be
    # used with any of the job choices.
    # When parsing the job choice, the "required" flags will be enforced.
    job_parser.add_argument(
        '--file',
        help=textwrap.dedent('''\
File path to the job definition file.
Required for 'create' and 'update' commands'''))

    job_parser.add_argument(
        '--poll', type=int,
        help='Interval in seconds used to poll for job status')

    job_parser.add_argument(
        '--look-back-hours',
        default=24,
        help='The number of hours look back for job status')

    job_parser.add_argument(
        '--duration',
        help='The duration in hours that a job should run')

    job_parser.add_argument(
        '--region',
        help='''\
Optional parameter to run a job in a specific region.
If no region is specified the job runs in the same
region where the service is deployed
https://docs.microsoft.com/en-us/azure/container-instances/container-instances-region-availability
''')

    job_parser.add_argument(
        '--metadata',
        help='Arbitrary key/value pairs that will be included in webhooks.')

    job_parser.add_argument(
        '--job-id',
        help="Id of the job to update, required for 'status' and 'delete'")

    job_parser.add_argument(
        '--substitute',
        help=textwrap.dedent('''\
Dictionary of values to find and replace in the --file.
Should be in the form {"find1":"replace1", "find2":"replace2"}
This parameter is only valid with the create and update commands
    '''))

    # The webhook positional arguments
    webhook_parser = sub_parser.add_parser(
        'webhook',
        formatter_class=argparse.RawTextHelpFormatter)

    webhook_parser.add_argument(
        'webhook-action',
        choices=['create', 'delete', 'list', 'events', 'test'],
        help=textwrap.dedent('''\
create - Create a new webhook
         --name, --event, and --url are required parameters

delete - Delete a webhook
         --name is required

list   - List a webhook
         --name is required

events - List the supported events

test   - Test a webhook
         --name and --event are required
'''))

    # Add the flag arguments
    # This is a list of all the possible flags that
    # can be used with any of the webhook choices.
    # When parsing the webhook choice, the "required"
    # flags will be enforced.
    webhook_parser.add_argument(
        '--event',
        help=textwrap.dedent('''\
Identifies the webhook hook event, for example JobStatus or BugFound'''))

    webhook_parser.add_argument(
        '--url',
        help='The webhook Url which will accept the POST command')
    webhook_parser.add_argument(
        '--name',
        help='Name of the webhook')

    add_defaults_and_secret_args(parser)
    args = parser.parse_args()

    run(vars(args))


if __name__ == "__main__":
    try:
        main()
    except Exception as ex:
        print(ex)
