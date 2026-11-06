# Evidence boundary

| Claim | Required evidence | Current preview |
| --- | --- | --- |
| C# behavior | Exact-commit verification output | Local/CI checks; see release receipt |
| API HTTP behavior | Actual process, statuses and inspected logs | Local/CI harness |
| Container startup | Built image, non-root user, actual container HTTP check | CI Linux job; inspect exact run |
| Template syntax | Both Bicep compilations | Compiler/CI output; not Azure validation |
| Azure deployment | Subscription-scoped what-if/validation, deployment ID, image digest, `/version` readback | NOT RUN |
| Managed-identity secret delivery | Role scope, Key Vault reference, running revision ready without inline secret | NOT RUN |
| Cloud monitoring | Actual JSON request and system logs, successful KQL query | NOT RUN |
| Rollback | Two real revisions, before/after traffic and endpoint identity | NOT RUN |

Do not paste raw tenant IDs, personal addresses, tokens, vault values or complete
Azure outputs into a public issue. Keep full operational evidence locally; publish
redacted receipts only. A healthy readiness endpoint checks configuration presence,
not a real vendor dependency or production availability.

After a cloud drill, record UTC time, source commit, exact image digest, resource
region, revision IDs, probe results, secret delivery method, before/after traffic,
the `/version` result and the cost/cleanup decision. Do not replace NOT RUN with
PASS based on a template, a screenshot of an editor or someone else's deployment.
