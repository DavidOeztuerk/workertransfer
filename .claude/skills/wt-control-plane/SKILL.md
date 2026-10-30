---
name: wt-control-plane
description: Use when running, configuring or debugging the Noelia Control Plane against WorkerTransfer locally — `make control-plane`, `make keycloak`, `make control-plane-oidc`, the fleet file, the licence, Keycloak and its development certificate, or why it runs on the host and not in compose.
---

# The Noelia Control Plane, locally

Moved verbatim from `CLAUDE.md` (30.10.2026). The binding one-liner stays there.

### The Noelia Control Plane — on the host, deliberately not part of `make up`

```bash
make control-plane        # http://localhost:8091 — needs `make up`
make control-plane-down
```

It collects `/noelia/report.json` from all fourteen services and lays them side
by side — egress, AI dependencies, obligations, findings, drift, attestation.
It is pull-only: there is no path from it into a service. The script does not
stop at "it answers": it reads `/fleet.json` and goes red unless all fourteen
were collected.

**A separate target, and that is the decision.** The product lives in a
neighbouring repository (`../NoeliaControlPlane`, overridable with
`NOELIA_CONTROL_PLANE_PFAD`). Putting it in `docker-compose.yml` would make
`make up` fail for everyone who has not checked that repository out.

**On the host and not in a container, since 02.10.2026 — measured, not
chosen.** Since its PR #1 the product refuses to start without access control:
OIDC, or `Access:AllowLocalDevelopment=true` in Development. The local mode
accepts only a *loopback* peer with a local Host header and no forwarding
headers, and through Docker Desktop's port forwarding the peer is the VM's
gateway — in a container it started and refused every browser request. The
vendor's own `run-everything.sh` runs it from source for that reason. The old
compose overlay also named its container `workertransfer-control-plane`, the
name of the kind node `scripts/k8s-up.sh` filters for. With an identity
provider, the container path comes back — with OIDC, not with a relay in front
of the local mode.

Three things are easy to get wrong here:

- **The fleet file becomes `appsettings.Development.json` in `.control-plane/`**,
  which is the working directory and therefore the content root. The
  application loads `appsettings.{ASPNETCORE_ENVIRONMENT}.json` and no other
  file; the vendor's demo fleet beside its DLL is never read.
- **`scripts/control-plane-fleet.json` belongs here and not there.** It
  describes *this* stack, through the ports compose publishes.
- **The licence comes from the environment** (`WORKERTRANSFER_NOELIA_LICENCE`),
  never from that file: it is a signed paper naming a customer. Empty means the
  free tier — one fleet, three services — and this fleet has fourteen.

**With sign-in, since 20.10.2026: `make keycloak && make control-plane-oidc`.** Keycloak runs in a container of its own on `https://localhost:8443` (realm `workertransfer`, client `noelia-control-plane`, group `admins` → role `admin`, a user `operator`), set up by `kcadm` on first start and kept in the volume `workertransfer-keycloak-data`. Three things are easy to get wrong:

- **The control plane accepts only an https authority, in every environment** (`AccessOptions.Validate`), so Keycloak serves the .NET development certificate — and macOS must trust it, because the control plane fetches the discovery document from the host. Trusting it is the person's step (`dotnet dev-certs https --trust`), never a script's; `keycloak.sh` stops and names the command.
- **The secrets and the realm belong together.** The admin password, the client secret and the operator's password are rolled once into `.control-plane/keycloak.env` (mode 600). Missing file over an existing volume means the script refuses rather than rolling new ones nothing in the realm knows.
- **The OIDC start proves less than the local one, and says so.** Signed out every page redirects to Keycloak — and building that address needs the discovery document, so the redirect proves the control plane reached Keycloak and trusted it. Whether it *collects* needs a sign-in; the local mode measures that.

It presents the same paper as the dashboard it reads
(`WORKERTRANSFER_EVIDENCE_SECRET`) and no second one. Empty means every service
answers 404 and the fleet stays empty without anything being broken.
`.control-plane/` holds its history, signing key and key ring and is
git-ignored.
