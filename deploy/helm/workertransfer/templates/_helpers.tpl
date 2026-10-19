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
      key: POSTGRES_PASSWORD
{{/*
The key is called like the database, because the service asks for it that way:
`GetConnectionString("<name>")`. Two names for the same thing would be two
opportunities to let them drift apart.
*/}}
- name: {{ printf "ConnectionStrings__%s" $svc.database }}
  value: {{ printf "Host=%s;Port=%v;Database=%s;Username=%s;Password=$(DB_PASSWORD)" $root.Values.postgres.host $root.Values.postgres.port $svc.database $root.Values.postgres.user | quote }}
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
The notification secret is called differently depending on the side and is THE
SAME: at identity-service it is `Notify__Secret` (it checks it), at every
service with `Identity__BaseUrl` it is `Identity__Secret` (it presents it), and
the sending services carry it as `Notifications__Secret`.

Expressly a DIFFERENT one from the erasure's (ADR-0027 §4.4): "may trigger a
mail" and "may erase everything about a person" must not be the same paper. The
conditions are independent: applications-service has BOTH addresses and needs
BOTH names for the same secret.
*/}}
{{/*
The door in front of the evidence, and it stands at EVERY service: each one
answers its own operator report (ADR-0044). A third paper, expressly — whoever
may trigger a mail need not learn which AI providers this instance uses. EMPTY
means: the door is shut and the report answers 404.
*/}}
- name: Evidence__Secret
  valueFrom:
    secretKeyRef:
      name: {{ include "workertransfer.secretName" $root }}
      key: WORKER_EVIDENCE_SECRET
{{/*
The master key, at EVERY service. It encrypts the data-protection key ring
(ADR-0046) and a person's AI key. Without it the pod does not start — which is
right, because in the clear nothing is stored.
*/}}
- name: WORKERTRANSFER_SECRETS_KEY
  valueFrom:
    secretKeyRef:
      name: {{ include "workertransfer.secretName" $root }}
      key: WORKER_SECRETS_KEY
{{- if or (eq $svc.name "identity-service") (eq $svc.name "profile-service") }}
{{/*
profile-service checks it at its internal search door (ADR-0036). EMPTY would
mean there: the door is shut — it answers 404, and the scout would search into
the void without a sound.
*/}}
- name: Notify__Secret
  valueFrom:
    secretKeyRef:
      name: {{ include "workertransfer.secretName" $root }}
      key: WORKER_NOTIFY_SECRET
{{- end }}
{{- if hasKey ($svc.env | default dict) "Identity__BaseUrl" }}
- name: Identity__Secret
  valueFrom:
    secretKeyRef:
      name: {{ include "workertransfer.secretName" $root }}
      key: WORKER_NOTIFY_SECRET
{{- end }}
{{- if hasKey ($svc.env | default dict) "Profile__BaseUrl" }}
{{/*
The same secret once more under a fourth name: scout-service searches through
profile-service's internal door. EMPTY would mean there not "everything" but
"nothing is searched" — and as a failure, so "not set up" does not look like
"there is nobody".
*/}}
- name: Profile__Secret
  valueFrom:
    secretKeyRef:
      name: {{ include "workertransfer.secretName" $root }}
      key: WORKER_NOTIFY_SECRET
{{- end }}
{{- if hasKey ($svc.env | default dict) "Notifications__BaseUrl" }}
- name: Notifications__Secret
  valueFrom:
    secretKeyRef:
      name: {{ include "workertransfer.secretName" $root }}
      key: WORKER_NOTIFY_SECRET
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
