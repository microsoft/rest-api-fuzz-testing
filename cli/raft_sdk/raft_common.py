# Copyright (c) Microsoft Corporation.
# Licensed under the MIT License.
import msal
import requests
import os
import atexit
import string
from pathlib import Path

script_dir = os.path.dirname(os.path.abspath(__file__))
cache_dir = os.path.join(str(Path.home()), '.cache', 'raft')
cache_path = os.path.join(cache_dir, 'token_cache.bin')


def delete_token_cache():
    try:
        os.remove(cache_path)
    except OSError:
        pass


# https://msal-python.readthedocs.io/en/latest/#tokencache
def token_cache():
    if not os.path.isdir(cache_dir):
        os.makedirs(cache_dir)

    cache = msal.SerializableTokenCache()

    if os.path.exists(cache_path):
        cache.deserialize(open(cache_path, "r").read())

    atexit.register(
        lambda: open(cache_path, "w").write(cache.serialize())
        if cache.has_state_changed else None)
    return cache


cache = token_cache()


def get_auth_token(client_id, tenant_id, secret=None):
    authority = f"https://login.microsoftonline.com/{tenant_id}"
    scopes = [f"{client_id}/.default"]

    if secret:
        app = msal.ConfidentialClientApplication(
                client_id,
                authority=authority,
                client_credential=secret,
                token_cache=cache
            )

        return app.acquire_token_for_client(scopes)
    else:
        app = msal.PublicClientApplication(
                client_id,
                authority=authority,
                token_cache=cache
            )

        accounts = app.get_accounts(None)
        if accounts and app.acquire_token_silent(scopes, accounts[0]):
            return app.acquire_token_silent(scopes, accounts[0])
        else:
            flow = app.initiate_device_flow(scopes)
            print(flow['message'], flush=True)
            return app.acquire_token_by_device_flow(flow)


class RaftApiException(Exception):
    def __init__(self, message, status_code):
        self.message = message
        self.status_code = status_code


class RestApiClient():
    def __init__(self, endpoint, client_id, tenant_id, secret):
        self.endpoint = endpoint
        self.client_id = client_id
        self.tenant_id = tenant_id
        self.secret = secret

    def auth_header(self):
        token = get_auth_token(self.client_id, self.tenant_id, self.secret)
        if 'error_description' in token:
            raise RaftApiException(token['error_description'], 400)
        return {
            'Authorization': f"{token['token_type']} {token['access_token']}"
            }

    def post(self, relative_url, json_data):
        return requests.post(
            self.endpoint + relative_url,
            json=json_data,
            headers=self.auth_header())

    def put(self, relative_url, json_data):
        return requests.put(
            self.endpoint + relative_url,
            json=json_data,
            headers=self.auth_header())

    def delete(self, relative_url):
        return requests.delete(
            self.endpoint + relative_url,
            headers=self.auth_header())

    def get(self, relative_url):
        return requests.get(
            self.endpoint + relative_url,
            headers=self.auth_header())


class RaftDefinitions():
    def __init__(self, context):
        self.context = context
        self.deployment = context['deploymentName']

        if len(self.deployment) > 24:
            raise Exception("Deployment name must be no"
                            " more than 24 characters long")

        for c in self.deployment:
            if (c not in string.ascii_lowercase) and (c not in string.digits):
                raise Exception("Deployment name"
                                " must use lowercase"
                                " letters and numbers"
                                " only")
        self.subscription = context['subscription']

        self.resource_group = f"{self.deployment}-raft"
        self.service_bus = f"{self.deployment}-raft-servicebus"
        self.app_insights = f"{self.deployment}-raft-ai"
        self.asp = f"{self.deployment}-raft-asp"
        self.container_tag = "v1.0"
        self.queues = {
                'job_events': "raft-jobevents",
                'create_queue': "raft-jobcreate",
                'delete_queue': "raft-jobdelete"
            }

        self.orchestrator = f"{self.deployment}-raft-orchestrator"
        self.storage_suffix = self.subscription.split('-')[1]
        self.storage_account = f"{self.deployment}raft" + self.storage_suffix
        self.event_domain = f"{self.deployment}-raft-events"
        self.storage_utils = f"{self.deployment}raftutil" + self.storage_suffix
        self.storage_results = f"{self.deployment}raftrslt" + self.storage_suffix
        self.api_service_webapp = f"{self.deployment}-raft-apiservice"
        self.endpoint = f"https://{self.api_service_webapp}.azurewebsites.net"

        self.test_infra = f"{self.deployment}-raft-test-infra"
        self.test_infra_storage = (f"{self.deployment}rafttest"
                                   f"{self.storage_suffix}")
        self.key_vault = f"{self.deployment}-raft-kv"
