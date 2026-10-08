Title: PotchMatch Procedure - Service Outage Runbook (DOC-SERVICE-OUTAGE)
Source URL: n/a (internal PotchMatch document, no public URL)
Attribution/licence: PotchMatch internal SOC procedure, written for ITRI623 coursework; no external licence

# Service Outage Runbook (DOC-SERVICE-OUTAGE)

Service Outage Runbook (DOC-SERVICE-OUTAGE) is a Document, an incident response procedure.

## Purpose
Restore a failed PotchMatch service and determine whether the outage is an attack.

## Trigger alerts
- "SERVICE_FAILURE"

## Steps
1. Confirm the alert and identify the failed service.
2. Check service health and recent logs, and the services that depend on it.
3. Look for a traffic flood against the service; if found, follow IP blocking.
4. Restart or redeploy the service and verify health checks pass.
5. Confirm dependent services recover.
6. Record the incident and root cause.

## Relationships
- Service Outage Runbook (DOC-SERVICE-OUTAGE) supports response to Endpoint Denial of Service (T1499).
