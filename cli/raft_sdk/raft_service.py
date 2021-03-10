# Copyright (c) Microsoft Corporation.
# Licensed under the MIT License.

import json
import yaml
import os
import sys
import time
from pathlib import Path

import tabulate
from .raft_common import RaftApiException, RestApiClient, RaftDefinitions, RaftJsonDict

script_dir = os.path.dirname(os.path.abspath(__file__))
dos2unix_file_types = [".sh", ".bash"]


class RaftJobConfig():
    def __init__(self,
                 *,
                 substitutions={},
                 file_path=None,
                 json_config=None):
        if file_path:
            with open(file_path, 'r') as config_file:
                c = config_file.read()
                for src in substitutions:
                    c = c.replace(src, substitutions[src])

                ext = Path(file_path).suffix
                if ext == '.json':
                    config = json.loads(c, object_hook=RaftJsonDict.raft_json_object_hook)
                elif ext == '.yml' or ext == '.yaml':
                    config = yaml.load(c, Loader=yaml.FullLoader)
                else:
                    raise Exception('Unsupported config file type')

            self.config = config
        elif json:
            self.config = json_config
        else:
            raise Exception('Expected file_path or json to be set')

    def add_metadata(self, data):
        if 'webhook' in self.config:
            if 'metadata' in self.config['webhook']:
                self.config['webhook']['metadata'].update(data)
            else:
                self.config['webhook']['metadata'] = data


class RaftJobError(Exception):
    def __init__(self, error, message):
        self.error = error
        self.message = message


class RaftCLI():
    def __init__(self, context=None):
        if context:
            self.context = context
        else:
            with open(os.path.join(
                        script_dir,
                        '..',
                        'defaults.json'), 'r') as defaults_json:
                self.context = json.load(defaults_json, object_hook=RaftJsonDict.raft_json_object_hook)

        self.definitions = RaftDefinitions(self.context)
        self.raft_api = RestApiClient(
                            self.definitions.endpoint,
                            self.context['clientId'],
                            self.context['tenantId'],
                            self.context.get('secret'))

    def job_status(self, job_id):
        '''
            Gets job status

            Parameters:
                job_id: job ID

            Returns:
                Job status
        '''
        response = self.raft_api.get(f'/jobs/{job_id}')
        if response.ok:
            return json.loads(response.text, object_hook=RaftJsonDict.raft_json_object_hook)
        else:
            raise RaftApiException(response.text, response.status_code)

    def list_jobs(self, time_span=None):
        '''
            List jobs for specified look-back timespan

            Parameters:
                time_span: look-back timespan.
                           Default is 24 hours

            Returns:
                List of job status objects within 'now' minus 'timespan'
                time window
        '''
        if time_span:
            response = self.raft_api.get(
                            f'/jobs?timeSpanFilter={time_span}')
        else:
            response = self.raft_api.get(f'/jobs')
        if response.ok:
            return json.loads(response.text, object_hook=RaftJsonDict.raft_json_object_hook)
        else:
            raise RaftApiException(response.text, response.status_code)

    def new_job(self, job_config, region=None):
        '''
            Creates and deploys a new job with specified job configuration

            Parameters:
                job_config: job configuration

                region: if set, then deploy job to that region

            Returns:
                Job ID assigned to newly created job
        '''
        if region:
            query = f'/jobs?region={region}'
        else:
            query = '/jobs'
        response = self.raft_api.post(query, job_config.config)
        if response.ok:
            return json.loads(response.text, object_hook=RaftJsonDict.raft_json_object_hook)
        else:
            raise RaftApiException(response.text, response.status_code)

    def update_job(self, job_id, job_config):
        '''
            Re-apply job configuration on an existing job.
            This is useful when one of the job tasks has isIdling flag set
            to 'true'

            Parameters:
                job_id: currently running job
                job_config: job configuration to apply to the job
        '''
        response = self.raft_api.post(f'/jobs/{job_id}', job_config.config)
        if response.ok:
            return json.loads(response.text, object_hook=RaftJsonDict.raft_json_object_hook)
        else:
            raise RaftApiException(response.text, response.status_code)

    def delete_job(self, job_id):
        '''
            Deletes job

            Parameters:
                job_id: ID of a job to delete
        '''
        response = self.raft_api.delete(f'/jobs/{job_id}')
        if response.ok:
            return json.loads(response.text, object_hook=RaftJsonDict.raft_json_object_hook)
        else:
            raise RaftApiException(response.text, response.status_code)

    def list_available_webhooks_events(self):
        '''
            Lists available webhook events

            Returns:
                list of events that are used with
                other webhook API calls
        '''
        response = self.raft_api.get('/webhooks/events')
        if response.ok:
            return json.loads(response.text, object_hook=RaftJsonDict.raft_json_object_hook)
        else:
            raise RaftApiException(response.text, response.status_code)

    def set_webhooks_subscription(self, name, event, url):
        '''
            Creates or updates webhook subscription

            Parameters:
                name: webhook name
                event: one of the events returned by
                       list_available_webhooks_events
                url: URL to POST webhook data to

            Returns:
                webhook configuration
        '''
        data = {
            'WebhookName': name,
            'Event': event,
            'TargetUrl': url
        }
        response = self.raft_api.post('/webhooks', data)
        if response.ok:
            return json.loads(response.text, object_hook=RaftJsonDict.raft_json_object_hook)
        else:
            raise RaftApiException(response.text, response.status_code)

    def test_webhook(self, name, event):
        '''
            Tests webhook by posting dummy data to the webhook
            registered with set_webhooks_subscription

            Parameters:
                name: webhook name
                event: one of the events returned by
                       list_available_webhooks_events

            Returns:
                Webhook send status
        '''
        response = self.raft_api.put(f'/webhooks/test/{name}/{event}', None)
        if response.ok:
            return json.loads(response.text, object_hook=RaftJsonDict.raft_json_object_hook)
        else:
            raise RaftApiException(response.text, response.status_code)

    def list_webhooks(self, name, event=None):
        '''
            Lists webhook registrations

            Parameters:
                name: webhook name
                event: one of the events returned by
                       list_available_webhooks_events
                       if None then list webhooks for all events

            Returns:
                List of webhook definitions
        '''
        if event:
            url = f'/webhooks?webhookName={name}&event={event}'
        else:
            url = f'/webhooks?webhookName={name}'

        response = self.raft_api.get(url)
        if response.ok:
            return json.loads(response.text, object_hook=RaftJsonDict.raft_json_object_hook)
        else:
            raise RaftApiException(response.text, response.status_code)

    def delete_webhook(self, name, event):
        '''
            Deletes webhook registration for the event

            Parameters:
                name: webhook name
                event: one of the events returned by
                       list_available_webhooks_events
        '''
        response = self.raft_api.delete(f'/webhooks/{name}/{event}')
        if response.ok:
            return json.loads(response.text, object_hook=RaftJsonDict.raft_json_object_hook)
        else:
            raise RaftApiException(response.text, response.status_code)

    def print_status(self, status):
        '''
            Prints status object to standard output in a readable format

            Parameters:
                status: status object returned by the service
        '''
        for s in status:
            if s['agentName'] == s['jobId']:
                print(f"{s['jobId']} {s['state']}")
                if s.get('utcEventTime'):
                    print(f'UtcEventTime: {s["utcEventTime"]}')
                if s.get('resultsUrl'):
                    print(f'Results: {s["resultsUrl"]}')
                if s.get('details'):
                    print("Details:")
                    for k in s['details']:
                        print(f"{k} : {s['details'][k]}")

        for s in status:
            if s['agentName'] != s['jobId']:
                agent_status = (
                    f"Agent: {s['agentName']}"
                    f"    Tool: {s['tool']}"
                    f"    State: {s['state']}")

                if 'metrics' in s:
                    metrics = s['metrics']
                    total_request_counts = metrics.get('totalRequestCount')
                    if total_request_counts and total_request_counts > 0:
                        print(f"{agent_status}"
                              "     Total Request Count:"
                              f" {total_request_counts}")
                        response_code_counts = []
                        for key in metrics['responseCodeCounts']:
                            response_code_counts.append(
                                [key, metrics['responseCodeCounts'][key]])
                        table = tabulate.tabulate(
                            response_code_counts,
                            headers=['Response Code', 'Count'])
                        print(table)
                        print()
                else:
                    print(agent_status)

                if s.get('details'):
                    print("Details:")
                    for k in s['details']:
                        print(f"{k} : {s['details'][k]}")

                print('======================')

    def is_completed(self, status):
        for s in status:
            # overall job status information
            if s['agentName'] == s['jobId']:
                completed = s['state'] == 'Completed'
                stopped = s['state'] == 'ManuallyStopped'
                error = s['state'] == 'Error'
                timed_out = s['state'] == 'TimedOut'
                if completed or stopped:
                    return True, None
                elif error or timed_out:
                    return True, RaftJobError(s['state'], s['details'])
                else:
                    return False, None
        return False, None

    def poll(self, job_id, poll_interval=10, print_status=True):
        '''
            Polls and prints job status updates until job terminates.

            Parameters:
                job_id: job id
                poll_interval: poll interval in seconds
        '''
        og_status = None
        while True:
            i = 0
            while i < poll_interval:
                time.sleep(1)
                sys.stdout.write('.')
                sys.stdout.flush()
                i += 1
            try:
                status = self.job_status(job_id)
                if og_status != status:
                    og_status = status
                    if print_status:
                        print()
                        self.print_status(status)
                completed, error = self.is_completed(status)
                if completed:
                    if error:
                        raise error
                    else:
                        return
            except RaftApiException as ex:
                if ex.status_code != 404:
                    print(f"{ex.message}")
                    raise RaftApiException(ex.text, ex.status_code)
