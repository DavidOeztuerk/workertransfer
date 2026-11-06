{{/*
Shared building blocks. The interesting ones are the last two:
`workertransfer.serviceEnv` is the one place where a service's environment
comes about. In the Python days the Deployment AND the migration Job fetched it
from here; since the service migrates its schema itself at startup, there is
only one consumer left — and with it no opportunity for the two to run against
different databases.
*/}}

{{- define "workertransfer.labels" -}}
app.kubernetes.io/name: {{ .Chart.Name }}
app.kubernetes.io/instance: {{ .Release.Name }}
app.kubernetes.io/managed-by: {{ .Release.Service }}
helm.sh/chart: {{ printf "%s-%s" .Chart.Name .Chart.Version | replace "+" "_" }}
{{- end -}}

{{/* The name of the Secret — either an externally managed one or our own. */}}
{{- define "workertransfer.secretName" -}}
{{- if .Values.secrets.existingSecret -}}
{{- .Values.secrets.existingSecret -}}
{{- else -}}
{{- printf "%s-secrets" .Release.Name -}}
{{- end -}}
{{- end -}}

{{/*
A secret: the wanted value, otherwise the one already lying in the cluster,
otherwise a freshly rolled one.

The middle step is the important one. Without it every `helm upgrade` would
generate new secrets and thereby sign out every open session — an upgrade that
throws people out is none.

Call: {{ include "workertransfer.keepOrMake" (dict "data" $old "key" "X" "wanted" .Values...) }}
*/}}
{{- define "workertransfer.keepOrMake" -}}
{{- if .wanted -}}
{{- .wanted -}}
{{- else if hasKey .data .key -}}
{{- index .data .key | b64dec -}}
{{- else -}}
{{- randAlphaNum 48 -}}
{{- end -}}
{{- end -}}

{{/* The origin under which the browser reaches the API. */}}
{{- define "workertransfer.apiOrigin" -}}
{{- .Values.web.apiOrigin | default .Values.publicUrl -}}
{{- end -}}

{{/*
A service's environment.

Call: {{ include "workertransfer.serviceEnv" (dict "root" $ "svc" $svc) }}

DB_PASSWORD stands deliberately BEFORE the connection string: Kubernetes
replaces $(VAR) only with variables that stand further up in the SAME list.
From `envFrom` it would not work — that is why the password comes here on its
own.
*/}}
{{- define "workertransfer.serviceEnv" -}}
{{- $root := .root -}}
{{- $svc := .svc -}}
- name: SERVICE_DIR
  value: {{ $svc.dir | quote }}
{{/*
0.0.0.0, not localhost: inside the container the service port would otherwise
answer nothing.
*/}}
- name: ASPNETCORE_URLS
  value: {{ printf "http://0.0.0.0:%v" $svc.port | quote }}
- name: DB_PASSWORD
  valueFrom:
    secretKeyRef:
      name: {{ include "workertransfer.secretName" $root }}
      key: {{ printf "WORKER_DB_PASSWORD_%s" (upper $svc.database) }}
{{/*
The key is called like the database, because the service asks for it that way:
`GetConnectionString("<name>")`. Two names for the same thing would be two
opportunities to let them drift apart.

The login is the service's own role, owner of this database alone (ADR-0058).
*/}}
- name: {{ printf "ConnectionStrings__%s" $svc.database }}
  value: {{ printf "Host=%s;Port=%v;Database=%s;Username=svc_%s;Password=$(DB_PASSWORD)" $root.Values.postgres.host $root.Values.postgres.port $svc.database $svc.database | quote }}
{{/*
The shared secrets. Listed one by one instead of `envFrom` over the whole
Secret: otherwise EVERY service would also get the drafting help's key, and one
that most services do not need does not belong in their environment.
*/}}
- name: Jwt__PublicKey
  valueFrom:
    secretKeyRef:
      name: {{ include "workertransfer.secretName" $root }}
      key: WORKER_JWT_PUBLIC_KEY
- name: Jwt__KeyId
  value: {{ $root.Values.secrets.jwtKeyId | quote }}
{{/*
ONLY identity-service gets the private key. That is why it stands here in a
condition and not in the shared block: every further holder would be a second
place that can create an acting principal.
*/}}
{{- if eq $svc.name "identity-service" }}
- name: Jwt__PrivateKey
  valueFrom:
    secretKeyRef:
      name: {{ include "workertransfer.secretName" $root }}
      key: WORKER_JWT_PRIVATE_KEY
{{- end }}
- name: Erasure__Secret
  valueFrom:
    secretKeyRef:
      name: {{ include "workertransfer.secretName" $root }}
      key: WORKER_ERASURE_SECRET
{{/*
Who a service is at the doors of the others (ADR-0057). Each pod gets its OWN
secret and nothing else; identity-service alone gets the hashes. The endpoint
stands here and not in the shared `-env` ConfigMap: a host named there would
pass every service's egress boundary.
*/}}
{{- if $svc.m2mClient }}
- name: NOELIA_M2M_TOKEN_ENDPOINT
  value: "http://identity-service:8001/m2m/token"
- name: NOELIA_M2M_CLIENT_ID
  value: {{ $svc.m2mClient | quote }}
- name: NOELIA_M2M_CLIENT_SECRET
  valueFrom:
    secretKeyRef:
      name: {{ include "workertransfer.secretName" $root }}
      key: {{ printf "WORKER_M2M_%s_SECRET" (upper $svc.m2mClient) }}
{{- end }}
{{- if eq $svc.name "identity-service" }}
{{- range $client, $_ := $root.Values.secrets.m2m }}
- name: {{ printf "NOELIA_M2M_CLIENT_%s_SECRET_HASH" (upper $client) }}
  valueFrom:
    secretKeyRef:
      name: {{ include "workertransfer.secretName" $root }}
      key: {{ printf "WORKER_M2M_%s_SECRET_HASH" (upper $client) }}
{{- end }}
{{- end }}
{{/*
The door in front of the evidence, and it stands at EVERY service: each one
answers its own operator report (ADR-0044). A paper of its own, expressly —
whoever may trigger a mail need not learn which AI providers this instance uses.
And one per service (ADR-0058): a leaked one opens one report and not fifteen.
EMPTY means: the door is shut and the report answers 404.
*/}}
- name: Evidence__Secret
  valueFrom:
    secretKeyRef:
      name: {{ include "workertransfer.secretName" $root }}
      key: {{ printf "WORKER_EVIDENCE_SECRET_%s" (upper $svc.database) }}
{{/*
The key this service's data-protection key ring lies under (ADR-0058). Without
it the pod does not start.
*/}}
- name: WORKERTRANSFER_KEYRING_KEY
  valueFrom:
    secretKeyRef:
      name: {{ include "workertransfer.secretName" $root }}
      key: {{ printf "WORKER_KEYRING_KEY_%s" (upper $svc.database) }}
{{/*
The master key, at identity-service alone. With it and the row out of identity's
database every person's AI key opens.
*/}}
{{- if eq $svc.name "identity-service" }}
- name: WORKERTRANSFER_SECRETS_KEY
  valueFrom:
    secretKeyRef:
      name: {{ include "workertransfer.secretName" $root }}
      key: WORKER_SECRETS_KEY
{{- end }}
{{- if $svc.drafting }}
{{/*
The drafting help (ADR-0024). EMPTY means: it is off, and the UI says so — no
foreign service is called then.
*/}}
- name: Draft__ApiKey
  valueFrom:
    secretKeyRef:
      name: {{ include "workertransfer.secretName" $root }}
      key: WORKER_AI_KEY
- name: Draft__Model
  value: {{ $root.Values.draftingModel | quote }}
- name: Draft__BaseUrl
  value: {{ $root.Values.draftingUrl | quote }}
- name: Draft__Provider
  value: {{ $root.Values.draftingProvider | quote }}
{{- end }}
{{- range $key, $value := ($svc.env | default dict) }}
- name: {{ $key }}
  value: {{ $value | quote }}
{{- end }}
{{- end -}}
