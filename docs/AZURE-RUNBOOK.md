# Owner-run Azure drill

This runbook is prepared, not executed. Do not create an account in someone else's
name or use credentials from an unrelated project. There is no deployment workflow
or stored cloud credential in this repository.

## 1. Before creating resources

- Use an owner-controlled enabled subscription and a dedicated disposable resource
  group. Confirm region availability and role-assignment permissions.
- Review the current [Container Apps pricing](https://azure.microsoft.com/en-us/pricing/details/container-apps/),
  Key Vault transactions, log ingestion/retention and data transfer. A monthly
  compute grant does not make the entire solution free. A budget alert is not a
  hard spending cap; a workspace daily quota is also not a global cost ceiling.
- This template uses public ingress and public Key Vault networking for a synthetic
  drill, with identity authorization. Private networking is a separate design.
- Run `pwsh -File scripts/preflight.ps1`. Compile-only success is not what-if,
  permission validation or successful resource provisioning.

## 2. Build the reviewed image

```sh
docker build -t operations-lab:reviewed .
docker run --rm -p 127.0.0.1:5088:8080 -e LAB_DEPENDENCY_TOKEN=synthetic-local-value operations-lab:reviewed
```

Publish this image through an owner-approved registry. Record the manifest digest
and source SHA; use `registry/path@sha256:...`, not a mutable `latest` tag. These
templates assume anonymous pull from a PUBLIC registry. A private registry needs
separate pull identity/registry settings; do not pass a registry password as an
environment variable. The CI image is an ephemeral local build, not a published
registry artifact.

## 3. Foundation, lab secret, then application

All angle-bracket values below are deliberate owner inputs, not working defaults.
Run Azure what-if first and inspect the changes and price implications. Do not
reuse the user's portfolio, production or company resource group.

```powershell
az deployment group what-if --resource-group <dedicated-rg> --template-file infra/foundation.bicep
az deployment group create --resource-group <dedicated-rg> --name operations-foundation --template-file infra/foundation.bicep
az deployment group show --resource-group <dedicated-rg> --name operations-foundation --query properties.outputs
```

The deploying principal needs deployment and role-assignment rights. Secret-reader
permission is scoped to the lab vault and assigned to the app identity. The human
creating the synthetic `lab-token` needs a separate appropriate data-plane role;
the template does not silently grant that human administrator access. Configure
this using the owner's normal access procedure.

Create a random **lab-only** token using the Key Vault portal or a local temporary
file with `az keyvault secret set --vault-name <vault> --name lab-token --file
<private-file> --output none`. Do not put it on a public command transcript, in
Bicep parameters, in source or in deployment outputs. Remove only that known
temporary file afterwards. RBAC propagation can delay secret resolution: inspect
identity/role/system logs before retrying; do not fall back to inline credentials.

Copy `environmentId`, `identityId`, `secretUri` from the foundation outputs into
an ignored `application.local.bicepparam`, along with appName, location,
sourceCommit, revisionSuffix and the exact image digest. Parameter file form:

```bicep
using './application.bicep'
param image = '<public-registry/path>@sha256:<64-hex-digest>'
param environmentId = '<foundation environmentId>'
param identityId = '<foundation identityId>'
param secretUri = '<foundation secretUri>'
param sourceCommit = '<reviewed-full-git-sha>'
param revisionSuffix = 'baseline'
```

```powershell
az deployment group what-if --resource-group <dedicated-rg> --parameters infra/application.local.bicepparam
az deployment group create --resource-group <dedicated-rg> --name operations-baseline --parameters infra/application.local.bicepparam
```

The application template sends traffic to latest for INITIAL deployment. Do NOT
reapply it during a candidate/rollback drill: that would overwrite pinned traffic.
Read `/version`, `/health/live` and `/health/ready`; save exact source/revision/image
identity locally. Probe success and the version response are separate checks.

## 4. Logs and secret-delivery drill

Issue a healthy read, an invalid read and a timed-out synthetic read. Inspect actual
console and system logs, then run each query in `monitoring.kql` separately. Record
the trace/correlation path and observed statuses. Do not infer p99 from a few
requests. Prove Key Vault delivery using the reference/identity/role configuration
plus readiness and absence of inline values; never print the secret to prove it.

Secret updates are app-scoped, not automatically a new revision. Existing replicas
may need restart/revision handling to consume an updated setting. An image rollback
does not reverse a secret change. Plan rotation separately.

## 5. Candidate and rollback

First pin traffic to the known healthy baseline by its exact revision name. Keep
that revision ACTIVE. Check the current traffic before adding a candidate.

```powershell
az containerapp revision list --resource-group <dedicated-rg> --name <app> -o table
pwsh -File scripts/traffic.ps1 -ResourceGroup <dedicated-rg> -AppName <app> -RevisionName <baseline-name>
az containerapp revision copy --resource-group <dedicated-rg> --name <app> --from-revision <baseline-name> --revision-suffix candidate --set-env-vars SOURCE_COMMIT=<reviewed-sha>
```

Use the same image for a configuration-only drill; use a new pinned digest for a
code-change drill. Inspect the candidate's health and revision-specific endpoint
before moving traffic. For a secret/readiness failure, create a separate candidate
with `LAB_FAIL_READINESS=true`; it must NOT receive production traffic. Do not call
the unhealthy-candidate check a completed traffic rollback.

Promote only a verified healthy candidate with `traffic.ps1`, observe `/version`
and logs, then run the same script with the baseline name and verify return to
baseline. Record both traffic readbacks and endpoint identities. The script refuses
inactive/unhealthy targets and supports `-WhatIf`. Cold-start time and ingress
propagation need observation, not assumed zero downtime. Multiple active revisions
can each run a replica; maxReplicas=1 is PER REVISION.

## 6. Cost and teardown

Stop test requests, review costs/log retention and list every resource in the exact
dedicated group. Deleting a resource group is destructive: review the resolved group
and obtain the owner's action-time approval first. No teardown script is supplied.
Key Vault soft-delete/purge protection outlives group deletion; do not promise an
immediate reusable name, purge bypass or zero residual cost.

## Primary references (reviewed 6 November 2026)

- [Secrets and Key Vault references](https://learn.microsoft.com/en-us/azure/container-apps/manage-secrets)
- [Health probes](https://learn.microsoft.com/en-us/azure/container-apps/health-probes)
- [Revisions](https://learn.microsoft.com/en-us/azure/container-apps/revisions-manage)
- [Traffic splitting](https://learn.microsoft.com/en-us/azure/container-apps/traffic-splitting)
- [Billing](https://learn.microsoft.com/en-us/azure/container-apps/billing)
- [Container app resource reference](https://learn.microsoft.com/en-us/azure/templates/microsoft.app/containerapps)
